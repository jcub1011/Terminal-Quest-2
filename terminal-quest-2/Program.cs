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
          + "gained or lost with add_item and remove_item; travel with move_character, after "
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
          + "using {This} for the one remembering and {Player} for the player.\n\n"

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
                return await McpServer.RunAsync(new SaveStore(saveDirectory));
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
            var driver = Environment.GetEnvironmentVariable("TQ_DRIVER");

            using var app = Application.Create().Init(driver);

            // Before any screen opens: the game is keyboard-only, so the terminal keeps the mouse
            // and with it text selection, right-click copy and right-click paste.
            MouseReporting.Disable(app);

            // The save has to be chosen before the narrator exists: its folder becomes a command
            // line argument to the state server, which the CLI launches as it starts. The same
            // screen is where the provider is chosen, for the same reason - both are settled
            // before anything is built, and neither can be changed once a session is open.
            var settings = SettingsStore.Read();

            var store = ChooseSave(app, settings);
            if (store is null)
            {
                return 0;
            }

            // A save that will not load must not start a turn: the narrator would see an empty
            // world through get_state and cheerfully build a new one on top of the broken files.
            // It must not be seeded with a character either, for the same reason - so this is read
            // before the character screen runs rather than after it.
            string? startupError = null;
            var needsCharacter = false;

            try
            {
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
                var created = CreateCharacter(app, store);

                // Backing out of the character screen means backing out of the game. The save
                // folder is left as the menu made it - empty, and offered again next time.
                if (created is null)
                {
                    return 0;
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

            await using var narrator = AgentSessionFactory.Create(settings, store, SystemPrompt);

            using var window = new GameWindow(state) { Title = $"Terminal Quest - {store.Name}" };
            var pump = new NarrationPump(app, window.Narration);

            narrator.OnTextDelta += pump.Enqueue;

            window.QuitRequested += () => app.RequestStop(window);
            window.CommandEntered += OnCommandEntered;

            window.Narration.AddLine($"Terminal Quest - {store.Name}", TextRole.System);
            window.Narration.AddLine("Type a command and press Enter. /help lists yours. PgUp/PgDn scrolls. Esc quits.", TextRole.System);
            window.Narration.AddBlankLine();

            if (startupError is not null)
            {
                window.Narration.AddLine(startupError, TextRole.Danger);
                window.Narration.AddLine(
                    "This save did not load, so the narrator has not been started. Fix the file, or Esc to quit.",
                    TextRole.System);
                window.Narration.AddBlankLine();
            }
            else
            {
                // The narrator is started here rather than before the UI so that a failure to launch
                // can be reported into the transcript, on a screen the player is already looking at.
                window.InputEnabled = false;
                window.Narration.AddLine("Waking the narrator...", TextRole.System);
                _ = Task.Run(OpenAsync);
            }

            app.Run(window);

            // Leave the save stamped with where the player actually got to.
            TryTouch(store, state.Turn);
            return 0;

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

                window.InputEnabled = false;
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
                    app.RequestStop(window);
                }
            }

            async Task OpenAsync()
            {
                try
                {
                    await narrator.StartAsync();
                }
                catch (AgentException ex)
                {
                    app.Invoke(() =>
                    {
                        window.Narration.AddLine(ex.Message, TextRole.Danger);
                        window.Narration.AddLine("Your commands still work. Esc quits.", TextRole.System);

                        // Input comes back on so the player can still read the save with /story
                        // and friends; every narrated turn will fail until the game is restarted.
                        window.InputEnabled = true;
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
                    var turn = await narrator.SendAsync(prompt);
                    pump.CompleteBlock();

                    app.Invoke(() =>
                    {
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

                        window.InputEnabled = true;
                        window.Narration.ScrollToBottom();
                    });
                }
                catch (Exception ex)
                {
                    app.Invoke(() =>
                    {
                        window.Narration.CommitBlock();
                        window.Narration.AddLine($"[{ex.Message}]", TextRole.Danger);
                        window.InputEnabled = true;
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

                window.Status.SetNeedsDraw();
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
        private static SaveStore? ChooseSave(IApplication app, AppSettings settings)
        {
            while (true)
            {
                using var menu = new SaveMenuWindow(Describe(settings));

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

            settings.Provider = chosen.Provider;
            settings.ClaudeModel = chosen.ClaudeModel;
            settings.LmStudioBaseUrl = chosen.LmStudioBaseUrl;
            settings.LmStudioModel = chosen.LmStudioModel;
            settings.LmStudioApiKey = chosen.LmStudioApiKey;
        }

        /// <summary>The one-line summary of who will be narrating, for the save menu.</summary>
        private static string Describe(AppSettings settings) => settings.Provider switch
        {
            AgentProvider.LmStudio =>
                $"LM Studio - {(settings.LmStudioModel is { Length: > 0 } model ? model : "whichever model is loaded")}",
            _ => $"Claude Code - {(settings.ClaudeModel is { Length: > 0 } model ? model : "default model")}",
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
        private static StartedCharacter? CreateCharacter(IApplication app, SaveStore store)
        {
            using var window = new NewCharacterWindow(store.Name);

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
