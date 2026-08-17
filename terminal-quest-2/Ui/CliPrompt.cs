using Spectre.Console;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Interactive prompt for player input built with standard Spectre.Console components.
    /// </summary>
    internal sealed class CliPrompt
    {
        private readonly ExternalEditor _editor;

        public CliPrompt(ExternalEditor editor)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        }

        public async Task<string> ReadLineAsync(
            IReadOnlyList<NarrationOption>? activeOptions = null,
            string promptSymbol = "❯")
        {
            var prompt = new TextPrompt<string>($"[bold #8fb26a]{promptSymbol}[/] ")
                .AllowEmpty()
                .PromptStyle(new Style(new Color(0xd7, 0xd2, 0xc4)));

            var input = AnsiConsole.Prompt(prompt).Trim();

            // Support opening external editor via /edit
            if (string.Equals(input, "/edit", StringComparison.OrdinalIgnoreCase))
            {
                var edited = await _editor.EditStringAsync(string.Empty, "Player Command");
                if (!string.IsNullOrWhiteSpace(edited))
                {
                    var trimmed = edited.Trim();
                    AnsiConsole.MarkupLine($"[bold #d7d2c4]{Markup.Escape(trimmed)}[/]");
                    return trimmed;
                }
                return string.Empty;
            }

            return input;
        }
    }
}

