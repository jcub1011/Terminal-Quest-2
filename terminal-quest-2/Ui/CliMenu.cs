using System.Text;
using Spectre.Console;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Rock-solid, flicker-free windowed CLI menu controller.
    /// Immune to line wrapping, long model lists, terminal resizing, ghost characters, and buffer scrolling.
    /// Supports automatic resize detection, live item preview cards, Up/Down arrows, PageUp/PageDown, Home/End,
    /// 1-based digit hotkeys, search filtering, Enter confirmation, and universal Esc cancellation.
    /// </summary>
    internal static class CliMenu
    {
        public static T? Prompt<T>(
            string title,
            IReadOnlyList<T> items,
            Func<T, string> displayFormatter,
            Func<T, string>? matchKeySelector = null,
            int defaultIndex = 0,
            Action? renderHeader = null,
            Action<T>? renderDetails = null,
            int? customPageSize = null,
            bool allowCancel = false)
        {
            ArgumentNullException.ThrowIfNull(items);
            if (items.Count == 0)
            {
                throw new ArgumentException("Menu must have at least one item.", nameof(items));
            }

            var selectedIndex = Math.Clamp(defaultIndex, 0, items.Count - 1);
            var buffer = new StringBuilder();
            var windowStart = 0;
            var lastWidth = -1;
            var lastHeight = -1;

            var prevCursorVisible = true;
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    prevCursorVisible = Console.CursorVisible;
                    Console.CursorVisible = false;
                }
                catch
                {
                }
            }

            void Draw(bool clearFull = false)
            {
                int windowHeight;
                try
                {
                    windowHeight = Console.WindowHeight;
                }
                catch
                {
                    windowHeight = 25;
                }

                // If a details card is rendered below the list, constrain list page size so everything fits
                var defaultMaxPage = renderDetails is not null ? 6 : Math.Clamp(windowHeight - 10, 5, 12);
                var maxPageSize = customPageSize ?? defaultMaxPage;
                var pageSize = Math.Min(items.Count, maxPageSize);

                if (selectedIndex < windowStart)
                {
                    windowStart = selectedIndex;
                }
                else if (selectedIndex >= windowStart + pageSize)
                {
                    windowStart = selectedIndex - pageSize + 1;
                }
                windowStart = Math.Clamp(windowStart, 0, Math.Max(0, items.Count - pageSize));

                if (clearFull)
                {
                    AnsiConsole.Clear();
                }
                else
                {
                    try
                    {
                        Console.SetCursorPosition(0, 0);
                        Console.Write("\x1b[J");
                    }
                    catch
                    {
                        Console.Write("\x1b[H\x1b[J");
                    }
                }

                renderHeader?.Invoke();

                if (!string.IsNullOrWhiteSpace(title))
                {
                    Console.Write("\x1b[2K\r");
                    AnsiConsole.MarkupLine(title);
                    Console.Write("\x1b[2K\r");
                    AnsiConsole.WriteLine();
                }

                if (items.Count > pageSize)
                {
                    Console.Write("\x1b[2K\r");
                    if (windowStart > 0)
                    {
                        AnsiConsole.MarkupLine($"  [dim cyan]▲ ({windowStart} more above)[/]");
                    }
                    else
                    {
                        AnsiConsole.WriteLine();
                    }
                }

                var windowEnd = Math.Min(items.Count, windowStart + pageSize);
                for (var i = windowStart; i < windowEnd; i++)
                {
                    var text = displayFormatter(items[i]);
                    var isSelected = (i == selectedIndex);
                    var numStr = $"{i + 1}.";

                    Console.Write("\x1b[2K\r");
                    if (isSelected)
                    {
                        AnsiConsole.MarkupLine($"  [bold cyan]❯[/] [bold #8fb26a]{numStr,-3}[/] [bold #ffffff]{text}[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"    [dim]{numStr,-3}[/] {text}");
                    }
                }

                if (items.Count > pageSize)
                {
                    Console.Write("\x1b[2K\r");
                    var remainingBelow = items.Count - windowEnd;
                    if (remainingBelow > 0)
                    {
                        AnsiConsole.MarkupLine($"  [dim cyan]▼ ({remainingBelow} more below)[/]");
                    }
                    else
                    {
                        AnsiConsole.WriteLine();
                    }
                }

                // Render live details card for the currently selected item if provided
                if (renderDetails is not null && selectedIndex >= 0 && selectedIndex < items.Count)
                {
                    Console.Write("\x1b[2K\r");
                    AnsiConsole.WriteLine();
                    renderDetails(items[selectedIndex]);
                }

                Console.Write("\x1b[2K\r");
                AnsiConsole.WriteLine();

                Console.Write("\x1b[2K\r");
                var escNote = allowCancel ? ", Esc to cancel" : string.Empty;
                var promptText = buffer.Length > 0
                    ? $"[bold #8fb26a]❯[/] [dim]Choice [[1-{items.Count}]]:[/] [bold #e0b050]{Markup.Escape(buffer.ToString())}[/]"
                    : $"[bold #8fb26a]❯[/] [dim]Select [[1-{items.Count}]] or ↑/↓, Enter to confirm{escNote}:[/] ";

                AnsiConsole.Markup(promptText);

                // Erase from cursor to bottom of the screen to clean any old trailing lines
                Console.Write("\x1b[J");
            }

            bool CheckResize()
            {
                try
                {
                    var w = Console.WindowWidth;
                    var h = Console.WindowHeight;
                    if (w != lastWidth || h != lastHeight)
                    {
                        lastWidth = w;
                        lastHeight = h;
                        return true;
                    }
                }
                catch
                {
                }
                return false;
            }

            CheckResize();
            Draw(clearFull: true);

            try
            {
                while (true)
                {
                    ConsoleKeyInfo key;
                    while (true)
                    {
                        if (CheckResize())
                        {
                            Draw(clearFull: true);
                        }

                        if (Console.KeyAvailable)
                        {
                            key = Console.ReadKey(intercept: true);
                            break;
                        }

                        Thread.Sleep(30);
                    }

                    if (key.Key == ConsoleKey.UpArrow)
                    {
                        selectedIndex = (selectedIndex - 1 + items.Count) % items.Count;
                        buffer.Clear();
                        Draw(clearFull: false);
                        continue;
                    }

                    if (key.Key == ConsoleKey.DownArrow)
                    {
                        selectedIndex = (selectedIndex + 1) % items.Count;
                        buffer.Clear();
                        Draw(clearFull: false);
                        continue;
                    }

                    if (key.Key == ConsoleKey.PageUp)
                    {
                        selectedIndex = Math.Max(0, selectedIndex - 5);
                        buffer.Clear();
                        Draw(clearFull: false);
                        continue;
                    }

                    if (key.Key == ConsoleKey.PageDown)
                    {
                        selectedIndex = Math.Min(items.Count - 1, selectedIndex + 5);
                        buffer.Clear();
                        Draw(clearFull: false);
                        continue;
                    }

                    if (key.Key == ConsoleKey.Home)
                    {
                        selectedIndex = 0;
                        buffer.Clear();
                        Draw(clearFull: false);
                        continue;
                    }

                    if (key.Key == ConsoleKey.End)
                    {
                        selectedIndex = items.Count - 1;
                        buffer.Clear();
                        Draw(clearFull: false);
                        continue;
                    }

                    if (key.Key == ConsoleKey.Enter)
                    {
                        AnsiConsole.WriteLine();
                        if (buffer.Length > 0)
                        {
                            var input = buffer.ToString().Trim();
                            if (int.TryParse(input, out var num) && num >= 1 && num <= items.Count)
                            {
                                return items[num - 1];
                            }

                            var matched = MatchItem(items, input, displayFormatter, matchKeySelector);
                            if (matched is not null)
                            {
                                return matched;
                            }
                        }

                        return items[selectedIndex];
                    }

                    if (key.Key == ConsoleKey.Backspace)
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Remove(buffer.Length - 1, 1);
                            Draw(clearFull: false);
                        }
                        continue;
                    }

                    if (key.Key == ConsoleKey.Escape)
                    {
                        if (buffer.Length > 0)
                        {
                            buffer.Clear();
                            Draw(clearFull: false);
                            continue;
                        }

                        if (allowCancel)
                        {
                            AnsiConsole.WriteLine();
                            return default;
                        }
                    }

                    // Direct digit hotkey selection
                    if (char.IsDigit(key.KeyChar))
                    {
                        var digit = key.KeyChar - '0';
                        if (digit >= 1 && digit <= items.Count && items.Count <= 9)
                        {
                            AnsiConsole.WriteLine();
                            return items[digit - 1];
                        }
                        else
                        {
                            buffer.Append(key.KeyChar);
                            if (int.TryParse(buffer.ToString(), out var parsedNum) && parsedNum >= 1 && parsedNum <= items.Count)
                            {
                                selectedIndex = parsedNum - 1;
                            }
                            Draw(clearFull: false);
                            continue;
                        }
                    }

                    if (!char.IsControl(key.KeyChar))
                    {
                        buffer.Append(key.KeyChar);
                        var input = buffer.ToString().Trim();

                        for (var i = 0; i < items.Count; i++)
                        {
                            var item = items[i];
                            var text = displayFormatter(item);
                            var keyStr = matchKeySelector?.Invoke(item) ?? text;
                            if (text.Contains(input, StringComparison.OrdinalIgnoreCase) ||
                                keyStr.Contains(input, StringComparison.OrdinalIgnoreCase))
                            {
                                selectedIndex = i;
                                break;
                            }
                        }
                        Draw(clearFull: false);
                    }
                }
            }
            finally
            {
                if (OperatingSystem.IsWindows())
                {
                    try
                    {
                        Console.CursorVisible = prevCursorVisible;
                    }
                    catch
                    {
                    }
                }
            }
        }

        internal static T? MatchItem<T>(
            IReadOnlyList<T> items,
            string input,
            Func<T, string> displayFormatter,
            Func<T, string>? matchKeySelector)
        {
            foreach (var item in items)
            {
                var text = displayFormatter(item);
                var key = matchKeySelector?.Invoke(item) ?? text;
                if (string.Equals(text, input, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(key, input, StringComparison.OrdinalIgnoreCase) ||
                    text.StartsWith(input, StringComparison.OrdinalIgnoreCase) ||
                    key.StartsWith(input, StringComparison.OrdinalIgnoreCase))
                {
                    return item;
                }
            }

            return default;
        }
    }
}
