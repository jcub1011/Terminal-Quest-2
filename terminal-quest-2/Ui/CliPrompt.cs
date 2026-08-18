using System.Text;
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

        public static string? AskString(
            string promptMarkup,
            string? defaultValue = null,
            bool allowEmpty = false,
            Func<string, ValidationResult>? validator = null,
            string cancelHint = "cancel")
        {
            if (Console.IsInputRedirected)
            {
                var line = Console.ReadLine() ?? defaultValue;
                return line;
            }

            while (true)
            {
                AnsiConsole.MarkupLine($"[dim]• Press Enter to submit, ESC to {cancelHint}[/]");
                AnsiConsole.Markup(promptMarkup);
                if (!string.IsNullOrEmpty(defaultValue))
                {
                    AnsiConsole.Markup($"[dim]({Markup.Escape(defaultValue)})[/] ");
                }

                var buffer = new StringBuilder();
                var cursorIndex = 0;
                var startLeft = 0;
                var startTop = 0;
                try
                {
                    startLeft = Console.CursorLeft;
                    startTop = Console.CursorTop;
                }
                catch
                {
                }

                var lastLength = 0;
                var cancelled = false;

                while (true)
                {
                    var key = Console.ReadKey(intercept: true);

                    if (key.Key == ConsoleKey.Escape)
                    {
                        cancelled = true;
                        break;
                    }
                    else if (key.Key == ConsoleKey.Enter)
                    {
                        break;
                    }
                    else if (key.Key == ConsoleKey.Backspace)
                    {
                        if (cursorIndex > 0)
                        {
                            buffer.Remove(cursorIndex - 1, 1);
                            cursorIndex--;
                            RedrawInput(startLeft, startTop, buffer.ToString(), cursorIndex, ref lastLength);
                        }
                    }
                    else if (key.Key == ConsoleKey.Delete)
                    {
                        if (cursorIndex < buffer.Length)
                        {
                            buffer.Remove(cursorIndex, 1);
                            RedrawInput(startLeft, startTop, buffer.ToString(), cursorIndex, ref lastLength);
                        }
                    }
                    else if (key.Key == ConsoleKey.LeftArrow)
                    {
                        if (cursorIndex > 0)
                        {
                            cursorIndex--;
                            SetCursorPositionSafe(startLeft + cursorIndex, startTop);
                        }
                    }
                    else if (key.Key == ConsoleKey.RightArrow)
                    {
                        if (cursorIndex < buffer.Length)
                        {
                            cursorIndex++;
                            SetCursorPositionSafe(startLeft + cursorIndex, startTop);
                        }
                    }
                    else if (key.Key == ConsoleKey.Home)
                    {
                        cursorIndex = 0;
                        SetCursorPositionSafe(startLeft, startTop);
                    }
                    else if (key.Key == ConsoleKey.End)
                    {
                        cursorIndex = buffer.Length;
                        SetCursorPositionSafe(startLeft + cursorIndex, startTop);
                    }
                    else if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Insert(cursorIndex, key.KeyChar);
                        cursorIndex++;
                        RedrawInput(startLeft, startTop, buffer.ToString(), cursorIndex, ref lastLength);
                    }
                }

                if (cancelled)
                {
                    AnsiConsole.MarkupLine(" [dim](Cancelled)[/]");
                    return null;
                }

                Console.WriteLine();
                var resultText = buffer.ToString().Trim();
                if (string.IsNullOrEmpty(resultText) && defaultValue != null)
                {
                    resultText = defaultValue;
                }

                if (string.IsNullOrEmpty(resultText) && !allowEmpty)
                {
                    AnsiConsole.MarkupLine("[red]Value cannot be empty.[/]");
                    continue;
                }

                if (validator != null)
                {
                    var validation = validator(resultText);
                    if (!validation.Successful)
                    {
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(validation.Message ?? "Invalid value.")}[/]");
                        continue;
                    }
                }

                return resultText;
            }
        }

        public static int? AskInt(
            string promptMarkup,
            int? defaultValue = null,
            Func<int, ValidationResult>? validator = null,
            string cancelHint = "cancel")
        {
            while (true)
            {
                var input = AskString(
                    promptMarkup,
                    defaultValue: defaultValue?.ToString(),
                    allowEmpty: false,
                    cancelHint: cancelHint);

                if (input is null)
                {
                    return null;
                }

                if (int.TryParse(input.Trim(), out var parsed))
                {
                    if (validator != null)
                    {
                        var validation = validator(parsed);
                        if (!validation.Successful)
                        {
                            AnsiConsole.MarkupLine($"[red]{Markup.Escape(validation.Message ?? "Invalid value.")}[/]");
                            continue;
                        }
                    }
                    return parsed;
                }

                AnsiConsole.MarkupLine("[red]Please enter a valid integer.[/]");
            }
        }

        public static bool? Confirm(string promptMarkup, bool defaultValue = true, string cancelHint = "cancel")
        {
            if (Console.IsInputRedirected)
            {
                return defaultValue;
            }

            var hint = defaultValue
                ? $"[dim]([bold green]Y[/]/n, ESC to {cancelHint})[/] "
                : $"[dim](y/[bold red]N[/], ESC to {cancelHint})[/] ";

            AnsiConsole.Markup($"{promptMarkup} {hint}");

            while (true)
            {
                var key = Console.ReadKey(intercept: true);
                if (key.Key == ConsoleKey.Y)
                {
                    AnsiConsole.MarkupLine("[green]yes[/]");
                    return true;
                }
                else if (key.Key == ConsoleKey.N)
                {
                    AnsiConsole.MarkupLine("[red]no[/]");
                    return false;
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    if (defaultValue)
                    {
                        AnsiConsole.MarkupLine("[green]yes[/]");
                        return true;
                    }
                    else
                    {
                        AnsiConsole.MarkupLine("[red]no[/]");
                        return false;
                    }
                }
                else if (key.Key == ConsoleKey.Escape)
                {
                    AnsiConsole.MarkupLine("[dim](Cancelled)[/]");
                    return null;
                }
            }
        }

        public static bool WaitKeyOrCancel(string? message = null)
        {
            if (Console.IsInputRedirected)
            {
                return true;
            }

            if (!string.IsNullOrEmpty(message))
            {
                AnsiConsole.MarkupLine(message);
            }
            else
            {
                AnsiConsole.MarkupLine("[dim]Press Enter to continue, or Esc to cancel...[/]");
            }

            var key = Console.ReadKey(intercept: true);
            return key.Key != ConsoleKey.Escape;
        }

        private static void SetCursorPositionSafe(int left, int top)
        {
            try
            {
                var width = Console.BufferWidth > 0 ? Console.BufferWidth : 80;
                var totalOffset = top * width + left;
                var targetTop = totalOffset / width;
                var targetLeft = totalOffset % width;
                if (targetTop < Console.BufferHeight && targetLeft < width)
                {
                    Console.SetCursorPosition(targetLeft, targetTop);
                }
            }
            catch
            {
            }
        }

        private static void RedrawInput(int startLeft, int startTop, string text, int cursorIndex, ref int lastLength)
        {
            try
            {
                SetCursorPositionSafe(startLeft, startTop);
                var spaces = Math.Max(1, lastLength - text.Length);
                Console.Write(text + new string(' ', spaces));
                lastLength = text.Length;
                SetCursorPositionSafe(startLeft + cursorIndex, startTop);
            }
            catch
            {
            }
        }
    }
}

