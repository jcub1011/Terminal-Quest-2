using Terminal.Gui.App;
using TerminalQuest.Claude;
using TerminalQuest.Ui;

namespace TerminalQuest
{
    internal class Program
    {
        private const string SystemPrompt =
            "You are the narrator of a terminal adventure game. Answer in at most two sentences. "
          + "Mark up your prose semantically, closing each tag by name: "
          + "items as [item]a rusted key[/item], dangers as [danger]a wolf[/danger], "
          + "spoken words as [speech]\"who goes there?\"[/speech], "
          + "and place names as [place]the Hollow Gate[/place]. "
          + "Use no other formatting, and never use square brackets for anything else.";

        static async Task<int> Main(string[] args)
        {
            var state = new GameState();

            await using var claude = new ClaudeSession(new ClaudeSessionOptions
            {
                Model = "claude-haiku-4-5-20251001",
                SystemPrompt = SystemPrompt,
            });

            // Start the session before the TUI takes over the terminal. A process that fails to
            // launch then reports plainly, instead of inside a UI that is about to be torn down.
            Console.Write("Starting Claude... ");
            try
            {
                await claude.StartAsync();
            }
            catch (ClaudeException ex)
            {
                Console.WriteLine("failed.");
                Console.Error.WriteLine(ex.Message);
                return 1;
            }

            Console.WriteLine("ready.");

            // TQ_DRIVER selects the Terminal.Gui driver; valid names are "windows", "dotnet" and
            // "ansi" (null picks the platform default). The Windows driver is reported to render
            // 24-bit colour incorrectly under conhost, so set TQ_DRIVER=ansi if colours look
            // wrong in cmd or PowerShell. Windows Terminal handles the default fine.
            var driver = Environment.GetEnvironmentVariable("TQ_DRIVER");

            using var app = Application.Create().Init(driver);

            using var window = new GameWindow(state);
            var pump = new NarrationPump(app, window.Narration);

            claude.OnTextDelta += pump.Enqueue;

            window.QuitRequested += () => app.RequestStop(window);
            window.CommandEntered += text =>
            {
                state.Turn++;
                window.InputEnabled = false;
                _ = Task.Run(() => RunTurnAsync(text));
            };

            window.Narration.AddLine("Terminal Quest", TextRole.System);
            window.Narration.AddLine("Type a command and press Enter. PgUp/PgDn scrolls. Esc quits.", TextRole.System);
            window.Narration.AddBlankLine();

            // Open on a narrated scene rather than an empty pane.
            window.InputEnabled = false;
            _ = Task.Run(() => RunTurnAsync("Begin the adventure. Describe the opening scene."));

            app.Run(window);
            return 0;

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
        }
    }
}
