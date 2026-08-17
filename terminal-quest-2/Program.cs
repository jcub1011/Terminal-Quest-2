using Terminal.Gui.App;

using TerminalQuest.Agents;
using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Settings;
using TerminalQuest.Ui;

namespace TerminalQuest
{
    internal class Program
    {
        /// <summary>
        /// Opening turn for a save whose player named where they begin. Everything the narrator
        /// needs is already on disk, so this only has to stop it from building a second one.
        /// </summary>
        private const string OpeningPrompt =
            "This is the first scene. The player character and where they begin are already on "
          + "record. Call get_state, then record_claims for what you are about to say, then describe "
          + "the place as they stand in it. Do not create the player and do not ask who they are.";

        /// <summary>Opening turn for a save whose player left the starting place to the narrator.</summary>
        private const string OpeningPromptNoPlace =
            "This is the first scene. The player character is already on record but has nowhere to "
          + "be. Call get_state, then invent where they begin: upsert_location, move_character them "
          + "into it, record_claims for what you are about to say, and describe it. Do not create the "
          + "player and do not ask who they are.";

        /// <summary>
        /// How often the transcript looks for rolls the narrator has just made.
        /// <para>
        /// The narrator's tools run in another process and there is nothing to subscribe to, so the
        /// only way a roll reaches the screen while the turn it belongs to is still running is to go
        /// looking for it. Fast enough that dice land beside the prose that describes them; slow
        /// enough that a three-minute turn costs a few hundred reads of a small file, which is
        /// nothing beside the model call it is waiting on.
        /// </para>
        /// </summary>
        private static readonly TimeSpan RollPollInterval = TimeSpan.FromMilliseconds(400);

        /// <summary>
        /// Opening turn for a save that has been played before.
        /// <para>
        /// Leads with <c>get_transcript</c> rather than <c>get_state</c>, which is the opposite order
        /// to every other opening, and deliberately: the world tells the narrator what is true, and
        /// the transcript tells it what it sounded like. Only one of those is unrecoverable from the
        /// other, and it is the one that has never been on disk until now.
        /// </para>
        /// </summary>
        private const string ContinuePrompt =
            "This save is being resumed and your memory of it is gone. Call get_transcript first: it "
          + "returns the end of the last session word for word, and says whether the player is owed an "
          + "answer. Then get_state, then record_claims for what you are about to say. Pick the thread "
          + "up in the voice the transcript is written in. Describe where the player is now rather than "
          + "recapping what they already lived through - and if their last line went unanswered, answer "
          + "it rather than opening a fresh scene over the top of it.";

        private static async Task<int> Main(string[] args)
        {
            // Re-entry as the narrator's state server. This branch must come before anything that
            // touches the console: stdout is the MCP transport from here on.
            if (args is ["--mcp-server", var saveDirectory, ..])
            {
                return await RunStateServerAsync(saveDirectory);
            }

            return await RunGameAsync();
        }

        /// <summary>
        /// Serves one save folder over stdio until the parent closes the pipe.
        /// </summary>
        /// <remarks>
        /// Failures are reported on stderr and nowhere else. Writing a diagnostic to stdout would
        /// be indistinguishable from a protocol frame and would take the whole session down.
        /// </remarks>
        private static async Task<int> RunStateServerAsync(string saveDirectory)
        {
            try
            {
                var store = new SaveStore(saveDirectory);

                // The game checks this before starting us, so reaching it here means the server was
                // pointed at a folder some other way. Refuse rather than serve a world we would
                // read wrong - the narrator would build on top of whatever we got back.
                store.RequireSupportedSchema();

                // Stderr for the same reason everything else here uses it, and because a journal that
                // will not write must not become a tool the narrator is told refused.
                QuestJournal.OnFailure = message => Console.Error.WriteLine($"quest server: {message}");

                return await McpServer.RunAsync(store);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"quest server failed: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> RunGameAsync()
        {
            // TQ_DRIVER selects the Terminal.Gui driver; valid names are "windows", "dotnet" and
            // "ansi" (null picks the platform default). The Windows driver is reported to render
            // 24-bit colour incorrectly under conhost, so set TQ_DRIVER=ansi if colours look
            // wrong in cmd or PowerShell. Windows Terminal handles the default fine.
            //
            // This is not a knob for input lag: every driver shares one input loop, which polls
            // on a fixed 20ms delay, so switching drivers cannot make typing land sooner. What
            // can is Responsiveness, below.
            var driver = Environment.GetEnvironmentVariable("TQ_DRIVER");

            using var app = Application.Create().Init(driver);

            // Before any screen opens: the transcript scrolls on the wheel, so the application wants
            // the mouse. The cost is the terminal's own selection, which moves onto Shift+drag.
            MouseReporting.Enable(app);

            // And so that a keystroke is drawn on the next tick rather than up to 25ms later.
            Responsiveness.Apply(app);

            // Migrate legacy data (settings, unnested saves) to new folder structure if needed
            PathProvider.EnsureMigrated();

            // Read once and carried across sessions, because the settings screen mutates it in
            // place. A provider changed between one save and the next takes effect on the next,
            // since the narrator is built per session.
            var settings = SettingsStore.Read();

            // One for the whole program, reading the command through a lambda rather than taking a
            // copy of it: the settings screen mutates the object above in place, so an editor chosen
            // there is in force on the very next Ctrl+G with nothing to re-wire.
            var editor = new ExternalEditor(app, () => settings.EditorCommand);

            // The menu and a session alternate for as long as the player keeps choosing saves.
            // Leaving a session comes back here rather than ending the program; the only way out
            // is Quit on the menu, which is the one screen where there is nothing left to back
            // out of.
            while (true)
            {
                // The save has to be chosen before the narrator exists: its folder becomes a
                // command line argument to the state server, which the CLI launches as it starts.
                // The same screen is where the provider is chosen, for the same reason - both are
                // settled before anything is built, and neither can be changed once a session is
                // open.
                var store = ChooseSave(app, settings, editor);
                if (store is null)
                {
                    return 0;
                }

                await RunSessionAsync(app, settings, store, editor);
            }
        }

        /// <summary>
        /// Plays one save, from the character screen to the last turn, and returns when the player
        /// leaves it.
        /// </summary>
        /// <remarks>
        /// Everything a session owns is scoped to this method - the narrator and its child
        /// processes above all - so that by the time the save menu is drawn again there is nothing
        /// of the last save still running.
        /// </remarks>
        private static async Task RunSessionAsync(
            IApplication app,
            AppSettings settings,
            SaveStore store,
            ExternalEditor editor)
        {
            // A save that will not load must not start a turn: the narrator would see an empty
            // world through get_state and cheerfully build a new one on top of the broken files.
            // It must not be seeded with a character either, for the same reason - so this is read
            // before the character screen runs rather than after it.
            string? startupError = null;
            var needsCharacter = false;

            // What this save tells the narrator. Read once, here, because both providers keep the
            // prompt they were given for the whole life of their process - which is what makes
            // /system-prompt end the session rather than take effect in it.
            var systemPrompt = SystemPromptFile.Default;

            try
            {
                // Before anything else, and before the narrator is created further down: a save
                // this build would misread must not reach a turn.
                store.RequireSupportedSchema();

                // Seeded rather than merely read, so a save made before this file existed grows one
                // now and can be edited from the character screen and from /system-prompt. Inside
                // this try, so a folder that cannot be written says so on the same path as every
                // other save fault - and the character screen and the narrator are both skipped.
                systemPrompt = SystemPromptFile.Ensure(store);

                // A save with nobody in it has never been played, whatever its metadata says.
                needsCharacter = store.ReadCharacters().Characters.Count == 0;
            }
            catch (SaveException ex)
            {
                startupError = ex.Message;
            }

            // Set only when this run made the character, so the opening turn can tell the narrator
            // what is already on disk. Re-deriving it from an empty roster would not work: by then
            // the character has been written.
            var startedFresh = false;
            var hasStartLocation = false;

            if (needsCharacter && startupError is null)
            {
                var created = CreateCharacter(app, store, editor);

                // Backing out of the character screen means backing out of the save. The folder is
                // left as the menu made it - empty, and offered again on the menu this returns to.
                if (created is null)
                {
                    return;
                }

                startedFresh = true;
                hasStartLocation = created.Value.HasStartLocation;
                startupError = created.Value.Error;
            }

            var state = new GameState { SaveName = store.Name };
            var watcher = new RollWatcher(store);

            // Cleared when the roll log turns out to be unreadable, so the poll stops rather than
            // reporting the same trouble several times a second. RefreshStatus still says it once,
            // after the turn, on a pass the player will actually read.
            var rollsAvailable = true;

            if (startupError is null)
            {
                try
                {
                    state.Turn = store.ReadMetadata().Turn;

                    if (!startedFresh)
                    {
                        var player = SaveStore.Player(store.ReadCharacters());
                        hasStartLocation = SaveStore.WhereIs(store.ReadLocations(), player?.Id) is not null;
                    }

                    // Read after seeding, so the pane opens showing the health and kit the player
                    // just chose rather than filling in once the first turn lands.
                    state.RefreshFrom(store);

                    // Rolls already on record belong to sessions that are over. The log is the
                    // save's memory; the transcript is this sitting's, and replaying a campaign of
                    // dice into it would bury the scene the player came back for.
                    watcher.CatchUp();
                }
                catch (SaveException ex)
                {
                    startupError = ex.Message;
                }
            }

            // Cancelled when the player leaves, and handed to every call that can take minutes.
            // Without it, leaving mid-turn would mean waiting out the provider's turn timeout.
            //
            // Declared before the narrator so that it is disposed after it: a turn still unwinding
            // while the narrator shuts down may yet touch this token, and a disposed source is not
            // safe to register against.
            using var life = new CancellationTokenSource();
            var leaving = false;

            await using var narrator = AgentSessionFactory.Create(settings, store, systemPrompt);

            // No Title: the window draws its own title row from the state, which already knows the
            // save name, so that the place name in it can be green on its own.
            using var window = new GameWindow(state)
            {
                Editor = editor,
                Store = store,
            };
            var pump = new NarrationPump(app, window.Narration);

            // Subscribed beside the pump rather than fed from it, so what reaches the transcript on
            // disk is the same stream that reached the screen and not a summary of it.
            var recorder = new NarrationRecorder();

            narrator.OnTextDelta += pump.Enqueue;
            narrator.OnTextDelta += recorder.Append;

            // Written down rather than shown. A journal that cannot be written is the game's problem
            // with its own record-keeping: the story is unaffected, the player can do nothing about
            // it, and a red line in the middle of a scene only takes the scene away from them.
            //
            // Every failure now, not the first one only. That guard existed because a Danger line per
            // tool call would bury the transcript; a log has no such objection, and how many calls
            // were lost is the thing somebody investigating actually wants to know.
            //
            // Not marshalled onto the UI thread either, since nothing here touches a view. Only the
            // in-process provider reaches this at all: on the Claude path the tools run in the state
            // server, which reports on its own stderr.
            QuestJournal.OnFailure = message =>
                Findings.Record(store, state.Turn, Finding.RecordUnwritable, message);

            window.LeaveRequested += Leave;
            window.CommandEntered += OnCommandEntered;
            window.CanSubmit = CanSubmit;

            window.Narration.AddLine($"Terminal Quest - {store.Name}", TextRole.System);
            window.Narration.AddLine(
                "Type a command and press Enter. /help lists yours. The wheel and PgUp/PgDn scroll. Esc returns to the menu.",
                TextRole.System);

            // Its own line rather than folded into the one above, because the players who need it
            // are the ones least well served by a long line. The terminal's keys rather than the
            // game's: see MouseReporting for why Ctrl+Scroll is not among them.
            window.Narration.AddLine(
                "Most terminals resize their own text with Ctrl+= and Ctrl+-.",
                TextRole.System);
            window.Narration.AddBlankLine();

            if (startupError is not null)
            {
                window.Narration.AddLine(startupError, TextRole.Danger);
                window.Narration.AddLine(
                    "This save did not load, so the narrator has not been started. Fix the file, or Esc to go back to the menu.",
                    TextRole.System);
                window.Narration.AddBlankLine();
            }
            else
            {
                // Said before the narrator is woken, because on the Claude path that is the thing that
                // will fail: the prompt goes as a single command line argument, and Windows caps the
                // whole line a little over 32,000 characters. A process that would not start is
                // reported already, but "could not start claude" is a baffling thing to be told when
                // the cause is a file the player edited an hour ago.
                if (systemPrompt.Length > SystemPromptFile.WarnAboveCharacters)
                {
                    window.Narration.AddLine(
                        $"system-prompt.txt is {systemPrompt.Length:N0} characters. Past about "
                      + $"{SystemPromptFile.WarnAboveCharacters:N0} the Claude narrator may refuse to start, "
                      + "because the prompt is passed to it on the command line. Shorten the file if it does.",
                        TextRole.Danger);
                    window.Narration.AddBlankLine();
                }

                // Before the narrator is woken, so the player has the scene they left to read while
                // it starts up rather than an empty pane. Skipped for a save made this run, which has
                // no last session to recall.
                if (!startedFresh)
                {
                    ShowRecalledScene();
                }

                // The narrator is started here rather than before the UI so that a failure to launch
                // can be reported into the transcript, on a screen the player is already looking at.
                window.IsBusy = true;
                window.Narration.AddLine("Waking the narrator...", TextRole.System);
                _ = Task.Run(OpenAsync);
            }

            app.Run(window);

            // Detached before the window goes: a delta still arriving from a turn that has not
            // finished unwinding would otherwise be pumped into a view that is being disposed.
            narrator.OnTextDelta -= pump.Enqueue;
            narrator.OnTextDelta -= recorder.Append;

            // Cleared for the same reason, one level up: this one is static, so left in place it would
            // outlive the session and draw the next save's trouble into a window that has closed.
            QuestJournal.OnFailure = null;

            // Leave the save stamped with where the player actually got to.
            TryTouch(store, state.Turn);
            return;

            // The one way out of a session, whether it was asked for with Esc or with /quit.
            void Leave()
            {
                // Esc during the wind-down would otherwise cancel twice and stop a window that has
                // already been asked to stop.
                if (leaving)
                {
                    return;
                }

                leaving = true;

                // Cancel first: it is what actually unblocks a turn in flight, and it gives the
                // narrator a head start on shutting down before disposal waits for it below.
                life.Cancel();
                _ = InterruptAsync();

                app.RequestStop(window);
            }

            // Asks the narrator to abandon its turn. Best effort - we are leaving either way.
            async Task InterruptAsync()
            {
                try
                {
                    await narrator.InterruptAsync();
                }
                catch (Exception)
                {
                    // Nowhere to report it and nothing to do about it: the session is closing, and
                    // disposal will take the process down regardless of what this said.
                }
            }

            // Whether a submitted line can be taken right now.
            bool CanSubmit(string text)
            {
                // The player's own commands only read the save, and every write goes to a temporary
                // file that is then renamed over the real one (SaveStore.Write), so a reader never
                // sees half a document - not even while the narrator is writing through the state
                // server. They stay available for the whole of a turn, which is the point: a turn
                // can take minutes, and /story or /quit must not be among the things it takes away.
                if (PlayerCommands.IsCommand(text) || !window.IsBusy)
                {
                    return true;
                }

                // Refused rather than queued. A line typed against a scene the player has not read
                // yet is rarely the line they would have written once they had.
                window.Narration.AddLine("The narrator is still speaking. Esc returns to the menu.", TextRole.System);
                window.Narration.ScrollToBottom();
                return false;
            }

            void OnCommandEntered(string text)
            {
                if (PlayerCommands.IsCommand(text))
                {
                    RunPlayerCommand(text);
                    return;
                }

                state.Turn++;

                // Stamped before the turn runs, because the state server reads the turn number out
                // of save.json to date the memories and events the narrator is about to write.
                if (!TryTouch(store, state.Turn))
                {
                    return;
                }

                // Before the turn rather than after it, so the ledger's order matches the fiction's: the
                // player spoke, and then the narrator answered.
                RecordPlayerClaim(text);

                // And for the same reason - but it carries a second meaning the ledger's copy does
                // not. Written now, a session that dies mid-reply leaves the player's line as the last
                // thing on record, which is how a resumed save knows the narrator was interrupted.
                RecordSpoken(TranscriptVoice.Player, state.Turn, text);

                window.IsBusy = true;
                _ = Task.Run(() => RunTurnAsync(text));
            }

            /// <summary>
            /// Puts the player's line on the ledger as a claim of their own.
            /// </summary>
            /// <remarks>
            /// Taken from what they typed rather than asked of the model, which is both free and more
            /// reliable - the game is holding the literal text either way.
            /// <para>
            /// Recorded unverified, and that is not a placeholder for something the game could work out.
            /// "I paid the toll last week" may be true, may be a boast, may be a lie being tried on, and
            /// settling it would take a model call. Unverified is also the honest tier: the claim binds
            /// nobody until something is built on it.
            /// </para>
            /// <para>
            /// Not every line asserts anything - "go north" asserts nothing - and it is recorded anyway,
            /// because sorting the assertions from the instructions is that same model call. What follows
            /// is that a player entry is a record of speech and never a fact to check the world against.
            /// </para>
            /// <para>
            /// Unlike <see cref="TryTouch"/> a failure here does not abort the turn. A missing turn stamp
            /// means the narrator dates everything it writes wrongly, which damages the save; a missing
            /// ledger line means a later audit is one entry short.
            /// </para>
            /// </remarks>
            void RecordPlayerClaim(string text)
            {
                try
                {
                    var player = SaveStore.Player(store.ReadCharacters());

                    store.Ledger.Append(new LedgerEntry
                    {
                        Turn = state.Turn,
                        Speaker = player?.Name ?? string.Empty,
                        SpeakerId = player?.Id ?? string.Empty,
                        Claim = text,
                        Truth = ClaimTruth.Unverified,
                    });
                }
                catch (SaveException ex)
                {
                    window.Narration.AddLine($"[{ex.Message}]", TextRole.Danger);
                }
            }

            /// <summary>
            /// Puts one line of the conversation on the transcript, exactly as it was written.
            /// </summary>
            /// <remarks>
            /// The game writes this rather than a tool, for the reason it already writes the player's
            /// ledger claim: it is holding both halves anyway - the typed line, and the deltas it just
            /// drew - so asking the narrator to report its own prose back would cost a round trip per
            /// turn to learn something already in hand, and would be lost on the turns that matter
            /// most, the ones that went wrong.
            /// <para>
            /// A failure is noted and swallowed rather than shown. A transcript one line short costs
            /// the next resume a little context, which the player can neither act on nor prevent;
            /// taking the turn down over it would cost them the scene as well.
            /// </para>
            /// </remarks>
            void RecordSpoken(TranscriptVoice voice, int turn, string text)
            {
                if (text.AsSpan().IsWhiteSpace())
                {
                    return;
                }

                try
                {
                    store.Transcript.Append(new TranscriptEntry
                    {
                        Turn = turn,
                        Voice = voice,
                        Text = text,
                    });
                }
                catch (SaveException ex)
                {
                    Findings.Record(store, turn, Finding.RecordUnwritable, ex.Message);
                }
            }

            /// <summary>
            /// Draws the end of the last session into the pane the player is about to read.
            /// </summary>
            /// <remarks>
            /// Runs before the narrator is woken and on the UI thread, while the window is still being
            /// built - so the recalled scene is simply the first thing in the transcript rather than
            /// something that arrives on top of it.
            /// <para>
            /// A transcript that will not parse leaves the pane as it was and says so once, on the same
            /// reasoning as the status refresh: the save is still playable, and the thing that failed
            /// is a convenience.
            /// </para>
            /// </remarks>
            void ShowRecalledScene()
            {
                IReadOnlyList<StyledLine> lines;

                try
                {
                    var recalled = TranscriptRecall.Tail(
                        store.Transcript.Read().Entries,
                        settings.TranscriptRecallCharacters);

                    if (recalled.Count == 0)
                    {
                        return;
                    }

                    lines = TranscriptReplay.Lines(recalled, store.Rolls.Read().Entries, store.ReadCharacters());
                }
                catch (SaveException ex)
                {
                    window.Narration.AddLine(
                        $"[{ex.Message}. The last session cannot be shown; the save is otherwise fine.]",
                        TextRole.Danger);
                    window.Narration.AddBlankLine();
                    return;
                }

                foreach (var line in lines)
                {
                    window.Narration.AddLine(line);
                }

                window.Narration.AddBlankLine();
            }

            void RunPlayerCommand(string text)
            {
                var result = PlayerCommands.Execute(text, store);

                foreach (var line in result.Lines)
                {
                    window.Narration.AddLine(line);
                }

                window.Narration.AddBlankLine();
                window.Narration.ScrollToBottom();

                if (result.EditSystemPrompt)
                {
                    EditSystemPrompt();
                }

                if (result.Quit)
                {
                    Leave();
                }
            }

            /// <summary>
            /// Hands this save's narrator brief to an editor, and ends the session once it changes.
            /// </summary>
            /// <remarks>
            /// Refused mid-turn, alone among the player's commands. The rest only read the save and are
            /// deliberately available while the narrator speaks; this one would put another program over
            /// a reply still arriving and then take the session away underneath it. Waiting out one turn
            /// is a smaller cost than either.
            /// <para>
            /// The leaving is not a courtesy. The narrator was given the old prompt as a command line
            /// argument, or as the first message of a history it resends every turn, and neither can be
            /// replaced in place - so a session that carried on would be one quietly ignoring the file
            /// the player had just written.
            /// </para>
            /// </remarks>
            void EditSystemPrompt()
            {
                if (window.IsBusy)
                {
                    window.Narration.AddLine(
                        "The narrator is mid-turn. Wait for it to finish, then ask again.",
                        TextRole.System);
                    window.Narration.AddBlankLine();
                    window.Narration.ScrollToBottom();
                    return;
                }

                // Seeded again rather than trusted: the file has been on disk since the session opened,
                // and anything may have happened to it in between - including being deleted by hand.
                try
                {
                    SystemPromptFile.Ensure(store);
                }
                catch (SaveException ex)
                {
                    window.Narration.AddLine(ex.Message, TextRole.Danger);
                    window.Narration.AddBlankLine();
                    window.Narration.ScrollToBottom();
                    return;
                }

                if (!window.BeginExternalEdit(store.SystemPromptPath, OnSystemPromptSaved))
                {
                    window.Narration.AddLine(
                        $"There is no editor to open it with. The file is {store.SystemPromptPath}.",
                        TextRole.System);
                    window.Narration.AddBlankLine();
                    window.Narration.ScrollToBottom();
                }
            }

            /// <summary>
            /// Runs once the editor has closed having really changed the prompt. Says so, and leaves.
            /// </summary>
            void OnSystemPromptSaved()
            {
                if (leaving)
                {
                    return;
                }

                window.Narration.AddLine(
                    $"The narrator's instructions have changed. Leaving '{store.Name}' - open it again to play with them.",
                    TextRole.System);
                window.Narration.ScrollToBottom();

                Leave();
            }

            async Task OpenAsync()
            {
                try
                {
                    await narrator.StartAsync(life.Token);
                }
                catch (OperationCanceledException)
                {
                    // The player left before it finished waking. There is nobody to tell.
                    return;
                }
                catch (Exception ex)
                {
                    // Every exception, not only AgentException. This runs on a fire-and-forget
                    // task, so anything that escapes here is a fault nobody observes - and it
                    // would leave the session marked busy for good, with no way to take a turn
                    // and no explanation on screen.
                    app.Invoke(() =>
                    {
                        if (leaving)
                        {
                            return;
                        }

                        window.Narration.AddLine(ex.Message, TextRole.Danger);
                        window.Narration.AddLine(
                            "Your commands still work. Esc returns to the menu.",
                            TextRole.System);

                        // The session stops being busy so the player can still read the save with
                        // /story and friends; every narrated turn will fail until the game is
                        // restarted.
                        //
                        // Nothing is scrolled. The turn died on its own account rather than on the
                        // player's, and someone who had scrolled back to re-read a scene is owed
                        // their place in it more than they are owed this sentence the instant it is
                        // written; the marker on the last row says it is there to be come back to.
                        window.IsBusy = false;
                    });

                    return;
                }

                if (startedFresh)
                {
                    // The scene-setting turn is a turn: it reads the world, it narrates, and the narrator
                    // will date memories and events to it. Left sharing a number with the last session's
                    // final turn, those writes are misdated - and anything that asks what happened "this
                    // turn" is answered with the end of the previous sitting as well as this one.
                    app.Invoke(() =>
                    {
                        state.Turn++;
                        TryTouch(store, state.Turn);
                    });

                    await RunTurnAsync(hasStartLocation ? OpeningPrompt : OpeningPromptNoPlace);
                    return;
                }

                var awaitingNarrator = false;
                var hasTranscript = false;

                try
                {
                    var entries = store.Transcript.Read().Entries;
                    hasTranscript = entries.Count > 0;
                    awaitingNarrator = TranscriptRecall.AwaitingNarrator(entries);
                }
                catch (SaveException)
                {
                    // Leave defaults if transcript is unreadable
                }

                if (!hasTranscript)
                {
                    // An existing save with no recorded transcript at all needs an opening scene.
                    app.Invoke(() =>
                    {
                        state.Turn++;
                        TryTouch(store, state.Turn);
                    });

                    await RunTurnAsync(hasStartLocation ? OpeningPrompt : OpeningPromptNoPlace);
                }
                else if (awaitingNarrator)
                {
                    // The last session ended while the narrator was speaking (e.g. interrupted mid-turn).
                    // The player is owed an answer to their last command.
                    app.Invoke(() =>
                    {
                        state.Turn++;
                        TryTouch(store, state.Turn);
                    });

                    await RunTurnAsync(ContinuePrompt);
                }
                else
                {
                    // The last session was waiting for the player to respond.
                    // The recalled scene was already drawn by ShowRecalledScene.
                    // The narrator is awake and ready, but we wait for player input before taking a turn.
                    app.Invoke(() =>
                    {
                        if (leaving)
                        {
                            return;
                        }

                        window.Narration.AddLine("The narrator is ready.", TextRole.System);
                        window.Narration.AddBlankLine();
                        window.Narration.ScrollToBottom();
                        window.IsBusy = false;
                    });
                }
            }

            async Task RunTurnAsync(string prompt)
            {
                // Scoped to this turn and linked to the session, so the watcher stops when the turn
                // ends and again if the player leaves mid-turn. Without the link it would go on
                // reading files and drawing into a window that is being disposed.
                using var turnLife = CancellationTokenSource.CreateLinkedTokenSource(life.Token);

                // The token is taken here rather than inside the lambda. Reading it there would be a
                // read of the source itself, which may already have been disposed by the time the
                // task is scheduled - a turn that fails on its first line would throw on the way out.
                var watching = turnLife.Token;
                _ = Task.Run(() => WatchRollsAsync(watching));

                // Read here rather than off the field later. This task was started immediately after
                // the turn was incremented, so this is the turn the prose belongs to - and by the time
                // it lands the field may have moved on.
                var spokenTurn = state.Turn;

                // Whatever the last turn left behind is not this turn's opening words. Cleared going
                // in rather than coming out, so an abandoned turn cannot bequeath its half-sentence to
                // the next one.
                recorder.Clear();

                try
                {
                    var turn = await narrator.SendAsync(prompt, life.Token);

                    // Checked before anything is drawn: a turn that lands in the instant between
                    // the player leaving and the window closing has an answer nobody asked for any
                    // more, and a view that is on its way out to draw it into.
                    if (leaving)
                    {
                        return;
                    }

                    // Before the paragraph is closed, so a roll written in the last few hundred
                    // milliseconds still lands above the prose rather than under it.
                    app.Invoke(ShowRolls);

                    pump.CompleteBlock();

                    app.Invoke(() =>
                    {
                        if (leaving)
                        {
                            return;
                        }

                        state.CostUsd += turn.CostUsd;
                        state.LastCacheRead = turn.CacheReadTokens;
                        state.LastDurationMs = turn.DurationMs;

                        // Assigned rather than accumulated: this is how full the context is now, not
                        // how much has passed through it.
                        state.ContextTokens = turn.ContextTokens;
                        state.ContextWindowTokens = turn.ContextWindowTokens;

                        if (turn.IsError)
                        {
                            window.Narration.AddLine($"[{turn.Text}]", TextRole.Danger);
                        }
                        else
                        {
                            if (ClaimsMissing(turn.Text, spokenTurn))
                            {
                                Findings.Record(store, spokenTurn, Finding.ClaimsMissing);
                            }

                            // Only here: past the cancellation, past the failure, past the provider
                            // reporting the turn itself as an error, and past the leaving check above.
                            // A reply is written down once it exists in full or it is not written down
                            // at all, which is what lets a resumed save trust every line it reads back.
                            //
                            // The streamed copy is preferred over turn.Text because it is what the
                            // player actually saw; turn.Text stands in only for a provider that
                            // answers without streaming, where there is nothing else to have.
                            RecordSpoken(
                                TranscriptVoice.Narrator,
                                spokenTurn,
                                recorder.TakeAndClear() is { Length: > 0 } spoken ? spoken : turn.Text);
                        }

                        // The narrator's writes happened in another process, so this is the only
                        // point at which the pane learns what the turn actually changed.
                        RefreshStatus();

                        // The turn ending is the narrator finishing a thought, not the player asking
                        // to be taken anywhere, so the pane is left exactly where it is. It is
                        // already where it belongs without help: every line of the turn re-synced it
                        // as it landed, which follows the stream to the end for a player who never
                        // left it and holds position for one who did.
                        window.IsBusy = false;
                    });
                }
                catch (OperationCanceledException)
                {
                    // The player left. Writing "the operation was cancelled" into a transcript
                    // that is about to be thrown away helps nobody.
                }
                catch (Exception ex)
                {
                    app.Invoke(() =>
                    {
                        if (leaving)
                        {
                            return;
                        }

                        // Even a turn that failed may have rolled before it did, and the player was
                        // promised they would see every die thrown - a promise a bad turn does not
                        // release the game from.
                        ShowRolls();

                        window.Narration.CommitBlock();
                        window.Narration.AddLine($"[{ex.Message}]", TextRole.Danger);
                        window.IsBusy = false;
                    });
                }
                finally
                {
                    turnLife.Cancel();
                }
            }

            /// <summary>
            /// Whether the turn narrated something and then failed to write down what it said.
            /// </summary>
            /// <remarks>
            /// Recorded as a fault rather than shrugged at, because an unextracted claim is invisible to
            /// any later consistency check - a ledger cannot record a gap in itself. Recorded in every
            /// build too, not only a debug one: the alternative is a shipped game quietly losing its
            /// record. The fix is the prompt, not this check.
            /// <para>
            /// It goes to <c>diagnostics.jsonl</c> and not to the screen. The player is not the audience
            /// - they can do nothing about it, and a red line in the middle of a scene costs them the
            /// scene to tell them something that does not concern them. A narrator that forgets
            /// habitually now shows up as a run of findings in one file, which is a better diagnostic
            /// than a run of lines nobody can count.
            /// </para>
            /// <para>
            /// Prose is taken from the turn's own text rather than from a count of pumped deltas, because
            /// that text is one of the two things every provider guarantees - a delta count would be a
            /// fact about the streaming path instead of about the turn.
            /// </para>
            /// </remarks>
            bool ClaimsMissing(string prose, int turn)
            {
                if (prose.AsSpan().IsWhiteSpace())
                {
                    return false;
                }

                try
                {
                    return !store.Journal.ForTurn(turn).Any(entry =>
                        !entry.Failed && string.Equals(entry.Tool, "record_claims", StringComparison.Ordinal));
                }
                catch (SaveException)
                {
                    // Silently. A journal that will not parse is a problem, but not one worth reporting as
                    // though the narrator had misbehaved - and the status refresh a few lines below is
                    // about to say the save is in trouble anyway.
                    return false;
                }
            }

            /// <summary>
            /// Looks for new rolls for as long as the turn lasts.
            /// </summary>
            async Task WatchRollsAsync(CancellationToken token)
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        await Task.Delay(RollPollInterval, token);
                        app.Invoke(ShowRolls);
                    }
                }
                catch (OperationCanceledException)
                {
                    // The turn ended, or the player left. Either way there is nothing left to watch.
                }
            }

            /// <summary>
            /// Draws every roll the narrator has made since this last ran.
            /// </summary>
            void ShowRolls()
            {
                if (leaving || !rollsAvailable)
                {
                    return;
                }

                IReadOnlyList<DiceRoll> rolls;
                CharacterFile characters;

                try
                {
                    rolls = watcher.Take();

                    if (rolls.Count == 0)
                    {
                        return;
                    }

                    characters = store.ReadCharacters();
                }
                catch (SaveException)
                {
                    // Silently, and once. This runs several times a second, and a roll log that will
                    // not parse would otherwise fill the transcript with the same sentence over and
                    // over. RefreshStatus reports the save's trouble after the turn.
                    rollsAvailable = false;
                    return;
                }

                // The paragraph in flight has to be closed first. NarrationView.AddLine appends to
                // the committed rows, which draw above the paragraph being streamed - so adding a
                // roll mid-stream without committing would shove it above prose already on screen.
                // Closing first is also the honest ordering: a tool call ends a block of text.
                //
                // The cost is that CommitBlock resets the markup parser, so a [speech] tag left open
                // across a tool call loses its colour for the rest of the paragraph. That is a
                // sentence in the wrong colour rather than broken text, and a tag spanning a tool
                // call is already a mistake on the narrator's part.
                pump.CompleteBlockNow();

                foreach (var roll in rolls)
                {
                    window.Narration.AddLine(
                        RollWatcher.Line(roll, SaveStore.FindCharacterById(characters, roll.CharacterId)?.Name));
                }

                // CommitBlock cleared the placeholder on its way past. The turn is not over, so the
                // narrator is still thinking and should still be seen to be.
                //
                // This is also the last word on where the pane sits: setting IsWaiting re-syncs it.
                // Nothing here scrolls, and this is the site where that matters most - the rolls
                // arrive several times a second for the whole of a turn, so a jump to the end here
                // would make reading back during a turn impossible rather than merely jarring.
                window.Narration.IsWaiting = window.IsBusy;
            }

            void RefreshStatus()
            {
                try
                {
                    state.RefreshFrom(store);
                }
                catch (SaveException ex)
                {
                    // A save that will not parse must not take the game down mid-turn; the prose
                    // is already on screen and the player deserves to know why the pane is stale.
                    window.Narration.AddLine(ex.Message, TextRole.Danger);
                }

                window.RefreshState();
            }

            bool TryTouch(SaveStore target, int turn)
            {
                try
                {
                    target.Touch(turn);
                    return true;
                }
                catch (SaveException ex)
                {
                    window.Narration.AddLine(ex.Message, TextRole.Danger);
                    window.Narration.ScrollToBottom();
                    return false;
                }
            }
        }

        /// <summary>Runs the startup screen. Null when the player quit instead of picking a save.</summary>
        /// <remarks>
        /// A loop rather than a single screen, because the settings live behind it: opening them
        /// closes the menu, and coming back has to rebuild it so a save created or deleted in the
        /// meantime is not missing from the list. <paramref name="settings"/> is mutated in place
        /// when the player changes anything, so the caller sees what they chose.
        /// </remarks>
        private static SaveStore? ChooseSave(IApplication app, AppSettings settings, ExternalEditor editor)
        {
            while (true)
            {
                using var menu = new SaveMenuWindow(app, Describe(settings)) { Editor = editor };

                var settingsRequested = false;

                menu.Done += () => app.RequestStop(menu);
                menu.Cancelled += () => app.RequestStop(menu);
                menu.SettingsRequested += () =>
                {
                    settingsRequested = true;
                    app.RequestStop(menu);
                };

                app.Run(menu);

                if (!settingsRequested)
                {
                    return menu.Chosen;
                }

                EditSettings(app, settings);
            }
        }

        /// <summary>Runs the settings screen and keeps what it settled.</summary>
        private static void EditSettings(IApplication app, AppSettings settings)
        {
            using var window = new SettingsWindow(app, settings);

            window.Done += () => app.RequestStop(window);
            window.Cancelled += () => app.RequestStop(window);

            app.Run(window);

            if (window.Chosen is not { } chosen)
            {
                return;
            }

            settings.CopyFrom(chosen);

            try
            {
                SettingsStore.Write(settings);
            }
            catch
            {
                // Ignored - SettingsWindow already attempted write
            }
        }

        /// <summary>The one-line summary of who will be narrating, for the save menu.</summary>
        private static string Describe(AppSettings settings) => settings.Provider switch
        {
            AgentProvider.OpenAiApi =>
                $"OpenAI API ({(settings.OpenAiPreset is { Length: > 0 } preset ? preset : "Custom")}) - {(settings.LmStudioModel is { Length: > 0 } model ? model : "whichever model is loaded")}",
            // By the name the settings screen offered rather than the raw id, so the menu says
            // back the same word the player picked.
            _ => $"Claude Code - {(settings.ClaudeModel is { Length: > 0 } model ? ClaudeModels.Describe(model) : "default model")}",
        };

        /// <summary>What the character screen settled, once it has been written to the save.</summary>
        /// <param name="HasStartLocation">
        /// Whether the player named where they begin. Decides which opening prompt the narrator
        /// gets, since the alternative is that it has to invent one.
        /// </param>
        /// <param name="Error">The save write that failed, if one did.</param>
        private readonly record struct StartedCharacter(bool HasStartLocation, string? Error);

        /// <summary>
        /// Runs the character screen and seeds the save from it. Null when the player quit instead
        /// of making anyone.
        /// </summary>
        /// <remarks>
        /// A further <c>app.Run</c> in the same process, following <see cref="ChooseSave"/>: the
        /// answers have to exist before the narrator is started, because the whole point is that it
        /// reads the character rather than inventing one.
        /// </remarks>
        private static StartedCharacter? CreateCharacter(IApplication app, SaveStore store, ExternalEditor editor)
        {
            // The prompt file is already on disk by now - RunSessionAsync seeds it before this screen
            // opens - so the window is handed a path it can simply give to an editor.
            using var window = new NewCharacterWindow(store.Name)
            {
                Editor = editor,
                SystemPromptPath = store.SystemPromptPath,
            };

            window.Done += () => app.RequestStop(window);
            window.Cancelled += () => app.RequestStop(window);

            app.Run(window);

            if (!window.Confirmed)
            {
                return null;
            }

            try
            {
                NewGame.Create(
                    store,
                    window.PlayerName,
                    window.Description,
                    window.Template,
                    window.StartLocation);
            }
            catch (SaveException ex)
            {
                // Reported into the transcript by the caller, on a screen the player will be
                // looking at, rather than swallowed here where there is nowhere to show it.
                return new StartedCharacter(window.StartLocation is not null, ex.Message);
            }

            return new StartedCharacter(window.StartLocation is not null, null);
        }
    }
}
