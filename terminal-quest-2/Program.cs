using Terminal.Gui.App;

using TerminalQuest.Claude;
using TerminalQuest.Mcp;
using TerminalQuest.Saves;
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

          + "Call get_state before narrating the first scene of a session. If the save is empty, "
          + "create the player with upsert_character, create where they begin with upsert_location, "
          + "and move_character them into it.\n\n"

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

        private const string NewGamePrompt =
            "This is a new save. Create the player character and where they begin, then describe "
          + "the opening scene.";

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

            // The save has to be chosen before the narrator exists: its folder becomes a command
            // line argument to the state server, which the CLI launches as it starts.
            var store = ChooseSave(app);
            if (store is null)
            {
                return 0;
            }

            var state = new GameState { SaveName = store.Name };

            // A save that will not load must not start a turn: the narrator would see an empty
            // world through get_state and cheerfully build a new one on top of the broken files.
            string? startupError = null;
            var isNewGame = true;

            try
            {
                // A save with nobody in it has never been played, whatever its metadata says.
                isNewGame = store.ReadCharacters().Characters.Count == 0;
                state.Turn = store.ReadMetadata().Turn;
                state.RefreshFrom(store);
            }
            catch (SaveException ex)
            {
                startupError = ex.Message;
            }

            await using var claude = new ClaudeSession(new ClaudeSessionOptions
            {
                Model = "claude-haiku-4-5-20251001",
                SystemPrompt = SystemPrompt,
                McpConfigJson = QuestServerConfig.Build(store.Directory),
                AllowedTools = QuestTools.AllowedTools(),
            });

            using var window = new GameWindow(state) { Title = $"Terminal Quest - {store.Name}" };
            var pump = new NarrationPump(app, window.Narration);

            claude.OnTextDelta += pump.Enqueue;

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
                // Claude is started here rather than before the UI so that a failure to launch can
                // be reported into the transcript, on a screen the player is already looking at.
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
                    await claude.StartAsync();
                }
                catch (ClaudeException ex)
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

                await RunTurnAsync(isNewGame ? NewGamePrompt : ContinuePrompt);
            }

            async Task RunTurnAsync(string prompt)
            {
                try
                {
                    var turn = await claude.SendAsync(prompt);
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
        private static SaveStore? ChooseSave(IApplication app)
        {
            using var menu = new SaveMenuWindow();

            menu.Done += () => app.RequestStop(menu);
            menu.Cancelled += () => app.RequestStop(menu);

            app.Run(menu);

            return menu.Chosen;
        }
    }
}
