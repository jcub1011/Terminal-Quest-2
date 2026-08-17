using System.Text;
using Spectre.Console;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Interactive CLI prompt for player input with command history, tab-completion for slash commands,
    /// and Ctrl+G external editor support.
    /// </summary>
    internal sealed class CliPrompt
    {
        private readonly List<string> _history = [];
        private readonly ExternalEditor _editor;

        public CliPrompt(ExternalEditor editor)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
        }

        public async Task<string> ReadLineAsync(
            IReadOnlyList<NarrationOption>? activeOptions = null,
            string promptSymbol = "❯")
        {
            var buffer = new StringBuilder();
            var cursorPosition = 0;
            var historyIndex = _history.Count;
            var currentDraft = string.Empty;

            AnsiConsole.Markup($"[bold #8fb26a]{promptSymbol}[/] ");
            var promptLeft = Console.CursorLeft;
            var promptTop = Console.CursorTop;

            void Redraw()
            {
                Console.SetCursorPosition(promptLeft, promptTop);
                var text = buffer.ToString();
                Console.Write(text);
                
                // Clear any remaining characters from previous longer lines
                var blankLength = Math.Max(0, Console.WindowWidth - promptLeft - text.Length - 1);
                if (blankLength > 0)
                {
                    Console.Write(new string(' ', blankLength));
                }

                Console.SetCursorPosition(promptLeft + cursorPosition, promptTop);
            }

            while (true)
            {
                var keyInfo = Console.ReadKey(intercept: true);

                // Check for Ctrl+G (external editor)
                if (keyInfo.Key == ConsoleKey.G && keyInfo.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    Console.WriteLine();
                    var edited = await _editor.EditStringAsync(buffer.ToString(), "Player Command");
                    if (!string.IsNullOrEmpty(edited))
                    {
                        var trimmed = edited.Trim();
                        if (trimmed.Length > 0)
                        {
                            _history.Add(trimmed);
                            AnsiConsole.MarkupLine($"[bold #d7d2c4]{Markup.Escape(trimmed)}[/]");
                            return trimmed;
                        }
                    }

                    // Redraw prompt if nothing was returned
                    AnsiConsole.Markup($"[bold #8fb26a]{promptSymbol}[/] ");
                    promptLeft = Console.CursorLeft;
                    promptTop = Console.CursorTop;
                    Redraw();
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    var command = buffer.ToString().Trim();
                    if (command.Length > 0)
                    {
                        _history.Add(command);
                    }
                    return command;
                }

                if (keyInfo.Key == ConsoleKey.Escape)
                {
                    // Clear current line
                    buffer.Clear();
                    cursorPosition = 0;
                    Redraw();
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (cursorPosition > 0)
                    {
                        buffer.Remove(cursorPosition - 1, 1);
                        cursorPosition--;
                        Redraw();
                    }
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.Delete)
                {
                    if (cursorPosition < buffer.Length)
                    {
                        buffer.Remove(cursorPosition, 1);
                        Redraw();
                    }
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.LeftArrow)
                {
                    if (cursorPosition > 0)
                    {
                        cursorPosition--;
                        Console.SetCursorPosition(promptLeft + cursorPosition, promptTop);
                    }
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.RightArrow)
                {
                    if (cursorPosition < buffer.Length)
                    {
                        cursorPosition++;
                        Console.SetCursorPosition(promptLeft + cursorPosition, promptTop);
                    }
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.Home)
                {
                    cursorPosition = 0;
                    Console.SetCursorPosition(promptLeft, promptTop);
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.End)
                {
                    cursorPosition = buffer.Length;
                    Console.SetCursorPosition(promptLeft + cursorPosition, promptTop);
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.UpArrow)
                {
                    if (_history.Count == 0)
                    {
                        continue;
                    }

                    if (historyIndex == _history.Count)
                    {
                        currentDraft = buffer.ToString();
                    }

                    if (historyIndex > 0)
                    {
                        historyIndex--;
                        buffer.Clear();
                        buffer.Append(_history[historyIndex]);
                        cursorPosition = buffer.Length;
                        Redraw();
                    }
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.DownArrow)
                {
                    if (historyIndex < _history.Count)
                    {
                        historyIndex++;
                        buffer.Clear();
                        if (historyIndex < _history.Count)
                        {
                            buffer.Append(_history[historyIndex]);
                        }
                        else
                        {
                            buffer.Append(currentDraft);
                        }
                        cursorPosition = buffer.Length;
                        Redraw();
                    }
                    continue;
                }

                if (keyInfo.Key == ConsoleKey.Tab)
                {
                    var currentText = buffer.ToString();
                    if (PlayerCommands.IsCommand(currentText))
                    {
                        var matches = PlayerCommands.Matching(currentText);
                        if (matches.Count == 1)
                        {
                            buffer.Clear();
                            buffer.Append($"/{matches[0].Name} ");
                            cursorPosition = buffer.Length;
                            Redraw();
                        }
                        else if (matches.Count > 1)
                        {
                            // Show completions inline
                            Console.WriteLine();
                            var suggestions = string.Join("  ", matches.Select(m => $"/{m.Name}"));
                            AnsiConsole.MarkupLine($"[dim]{Markup.Escape(suggestions)}[/]");
                            AnsiConsole.Markup($"[bold #8fb26a]{promptSymbol}[/] ");
                            promptLeft = Console.CursorLeft;
                            promptTop = Console.CursorTop;
                            Redraw();
                        }
                    }
                    continue;
                }

                if (!char.IsControl(keyInfo.KeyChar))
                {
                    buffer.Insert(cursorPosition, keyInfo.KeyChar);
                    cursorPosition++;
                    Redraw();
                }
            }
        }
    }
}
