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
        /// The narrator's entire brief: how to write, and how to keep the world.
        /// <para>
        /// The markup rules come first and are matched exactly by <see cref="MarkupParser"/> - the
        /// two have to be changed together. Everything after them is the tool contract, and it is
        /// worded as instructions about <em>when</em> to reach for a tool rather than what the
        /// tools are, because the schemas already say what they are. This whole prefix is cached
        /// after the first turn, so its length costs once per session rather than once per turn.
        /// </para>
        /// </summary>
        private const string SystemPrompt =
            "You are the narrator of a terminal adventure game. Answer in at most two sentences. "
          + "Mark up your prose semantically, closing each tag by name: "
          + "items as [item]a rusted key[/item], dangers as [danger]a wolf[/danger], "
          + "spoken words as [speech]\"who goes there?\"[/speech], "
          + "and place names as [place]the Hollow Gate[/place]. "
          + "Use no other formatting, and never use square brackets for anything else.\n\n"

          + "The world is kept in files. Your tools are the only way to read or change it, and "
          + "nothing you merely say is remembered. Never invent health, inventory, or who is "
          + "present - read them.\n\n"

          + "Call get_state before narrating the first scene of a session. The player character is "
          + "made before the session starts, by the player: never invent one, never replace one, "
          + "and never ask who they are - read them. If no location is on record, create where they "
          + "begin with upsert_location and move_character them into it.\n\n"

          + "Record what happens as it happens: damage or healing with update_character; items "
          + "gained or lost with add_item and remove_item; coin earned or spent with add_money and "
          + "remove_money, never as an item; travel with move_character, after "
          + "upsert_location when the place is new; a lasting change to a place with "
          + "add_location_event; and each beat of the story - arriving somewhere, meeting someone, "
          + "a bargain struck - with record_event.\n\n"

          + "On arriving anywhere, call get_location and describe the place as it now stands. What "
          + "happened there has not been undone.\n\n"

          + "Before voicing a character, call get_memories for them, with 'about' set to whoever "
          + "they are dealing with. What they remember decides their tone: trust, fear, a grudge, "
          + "a debt. Never write a character who holds memories as a blank slate.\n\n"

          + "Give a memory to every character who perceived something, not only the one it "
          + "happened to - a witness remembers what they saw. Write it from their vantage point, "
          + "using {This} for the one remembering and {Player} for the player. Name who or what a "
          + "memory concerns in 'subjects', using names already on record so it can be found "
          + "again.\n\n"

          + "Names can change. A character who gives a false name and later admits their real one, "
          + "or a place the player learns the true name of, is renamed with update_character or "
          + "update_location - not replaced with a second record. Where people stand and what they "
          + "remember follow a rename by themselves, but prose you have already written is left "
          + "alone, so an old memory will still say the old name. Treat that as the character's own "
          + "recollection rather than a mistake to correct, and never narrate the correction.\n\n"

          + "Tool calls are silent. The player sees only your prose.";

        /// <summary>
        /// Opening turn for a save whose player named where they begin. Everything the narrator
        /// needs is already on disk, so this only has to stop it from building a second one.
        /// </summary>
        private const string OpeningPrompt =
            "This is the first scene. The player character and where they begin are already on "
          + "record. Call get_state, then describe the place as they stand in it. Do not create "
          + "the player and do not ask who they are.";

        /// <summary>Opening turn for a save whose player left the starting place to the narrator.</summary>
        private const string OpeningPromptNoPlace =
            "This is the first scene. The player character is already on record but has nowhere to "
          + "be. Call get_state, then invent where they begin: upsert_location, move_character them "
          + "into it, and describe it. Do not create the player and do not ask who they are.";

        private const string ContinuePrompt =
            "This save is being resumed. Call get_state, then set the scene where the player left "
          + "off - describe where they are now rather than recapping what they already lived through.";

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

            try
            {
                // Before anything else, and before the narrator is created further down: a save
                // this build would misread must not reach a turn.
                store.RequireSupportedSchema();

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

            if (startupError is null)
            {
                try
                {
                    state.Turn = store.ReadMetadata().Turn;

                    // Read after seeding, so the pane opens showing the health and kit the player
                    // just chose rather than filling in once the first turn lands.
                    state.RefreshFrom(store);
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

            await using var narrator = AgentSessionFactory.Create(settings, store, SystemPrompt);

            // No Title: the window draws its own title row from the state, which already knows the
            // save name, so that the place name in it can be green on its own.
            using var window = new GameWindow(state)
            {
                Editor = editor,
            };
            var pump = new NarrationPump(app, window.Narration);

            narrator.OnTextDelta += pump.Enqueue;

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

                window.IsBusy = true;
                _ = Task.Run(() => RunTurnAsync(text));
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

                if (result.Quit)
                {
                    Leave();
                }
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
                        window.IsBusy = false;
                        window.Narration.ScrollToBottom();
                    });

                    return;
                }

                await RunTurnAsync(startedFresh
                    ? hasStartLocation ? OpeningPrompt : OpeningPromptNoPlace
                    : ContinuePrompt);
            }

            async Task RunTurnAsync(string prompt)
            {
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

                        if (turn.IsError)
                        {
                            window.Narration.AddLine($"[{turn.Text}]", TextRole.Danger);
                        }

                        // The narrator's writes happened in another process, so this is the only
                        // point at which the pane learns what the turn actually changed.
                        RefreshStatus();

                        window.IsBusy = false;
                        window.Narration.ScrollToBottom();
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

                        window.Narration.CommitBlock();
                        window.Narration.AddLine($"[{ex.Message}]", TextRole.Danger);
                        window.IsBusy = false;
                    });
                }
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
                using var menu = new SaveMenuWindow(Describe(settings)) { Editor = editor };

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
        }

        /// <summary>The one-line summary of who will be narrating, for the save menu.</summary>
        private static string Describe(AppSettings settings) => settings.Provider switch
        {
            AgentProvider.LmStudio =>
                $"LM Studio - {(settings.LmStudioModel is { Length: > 0 } model ? model : "whichever model is loaded")}",
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
            using var window = new NewCharacterWindow(store.Name) { Editor = editor };

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
