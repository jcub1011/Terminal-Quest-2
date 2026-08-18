using Spectre.Console;
using Spectre.Console.Rendering;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Clean, standard menu helper built on Spectre.Console with interactive keyboard navigation and ESC cancellation.
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
            bool allowCancel = false,
            bool enableSearch = false,
            string cancelHint = "cancel") where T : notnull
        {
            ArgumentNullException.ThrowIfNull(items);
            if (items.Count == 0)
            {
                throw new ArgumentException("Menu must have at least one item.", nameof(items));
            }

            if (Console.IsInputRedirected)
            {
                return items[Math.Clamp(defaultIndex, 0, items.Count - 1)];
            }

            var pageSize = Math.Max(3, customPageSize ?? 10);
            var selectedIndex = Math.Clamp(defaultIndex, 0, items.Count - 1);
            var searchQuery = string.Empty;
            T? result = default;
            var finished = false;

            while (!finished)
            {
                AnsiConsole.Clear();
                try
                {
                    renderHeader?.Invoke();
                }
                catch
                {
                }

                using var cts = new CancellationTokenSource();
                var resized = false;

                var filtered = FilterItems(items, searchQuery, displayFormatter, matchKeySelector);
                if (filtered.Count == 0)
                {
                    selectedIndex = 0;
                }
                else if (selectedIndex >= filtered.Count)
                {
                    selectedIndex = filtered.Count - 1;
                }

                AnsiConsole.Live(BuildMenuRenderable(
                    title,
                    filtered,
                    selectedIndex,
                    pageSize,
                    searchQuery,
                    enableSearch,
                    allowCancel,
                    displayFormatter,
                    cancelHint))
                    .AutoClear(false)
                    .Overflow(VerticalOverflow.Ellipsis)
                    .Start(ctx =>
                    {
                        while (!finished && !resized && !cts.IsCancellationRequested)
                        {
                            var currentFiltered = FilterItems(items, searchQuery, displayFormatter, matchKeySelector);
                            if (currentFiltered.Count == 0)
                            {
                                selectedIndex = 0;
                            }
                            else if (selectedIndex >= currentFiltered.Count)
                            {
                                selectedIndex = currentFiltered.Count - 1;
                            }

                            ctx.UpdateTarget(BuildMenuRenderable(
                                title,
                                currentFiltered,
                                selectedIndex,
                                pageSize,
                                searchQuery,
                                enableSearch,
                                allowCancel,
                                displayFormatter,
                                cancelHint));
                            ctx.Refresh();

                            var key = TerminalMonitor.ReadKeyOrResize(
                                () =>
                                {
                                    resized = true;
                                    cts.Cancel();
                                },
                                pollIntervalMs: 30,
                                cancellationToken: cts.Token);

                            if (resized || cts.IsCancellationRequested)
                            {
                                break;
                            }

                            if (key.Key == ConsoleKey.UpArrow)
                            {
                                if (currentFiltered.Count > 0)
                                {
                                    selectedIndex = (selectedIndex - 1 + currentFiltered.Count) % currentFiltered.Count;
                                }
                            }
                            else if (key.Key == ConsoleKey.DownArrow)
                            {
                                if (currentFiltered.Count > 0)
                                {
                                    selectedIndex = (selectedIndex + 1) % currentFiltered.Count;
                                }
                            }
                            else if (key.Key == ConsoleKey.PageUp)
                            {
                                if (currentFiltered.Count > 0)
                                {
                                    selectedIndex = Math.Max(0, selectedIndex - pageSize);
                                }
                            }
                            else if (key.Key == ConsoleKey.PageDown)
                            {
                                if (currentFiltered.Count > 0)
                                {
                                    selectedIndex = Math.Min(currentFiltered.Count - 1, selectedIndex + pageSize);
                                }
                            }
                            else if (key.Key == ConsoleKey.Home)
                            {
                                selectedIndex = 0;
                            }
                            else if (key.Key == ConsoleKey.End)
                            {
                                if (currentFiltered.Count > 0)
                                {
                                    selectedIndex = currentFiltered.Count - 1;
                                }
                            }
                            else if (key.Key == ConsoleKey.Enter)
                            {
                                if (currentFiltered.Count > 0 && selectedIndex >= 0 && selectedIndex < currentFiltered.Count)
                                {
                                    result = currentFiltered[selectedIndex];
                                }
                                finished = true;
                                break;
                            }
                            else if (key.Key == ConsoleKey.Escape)
                            {
                                if (enableSearch && !string.IsNullOrEmpty(searchQuery))
                                {
                                    searchQuery = string.Empty;
                                    selectedIndex = 0;
                                }
                                else if (allowCancel)
                                {
                                    result = default;
                                    finished = true;
                                    break;
                                }
                            }
                            else if (enableSearch)
                            {
                                if (key.Key == ConsoleKey.Backspace)
                                {
                                    if (searchQuery.Length > 0)
                                    {
                                        searchQuery = searchQuery[..^1];
                                        selectedIndex = 0;
                                    }
                                }
                                else if (!char.IsControl(key.KeyChar))
                                {
                                    searchQuery += key.KeyChar;
                                    selectedIndex = 0;
                                }
                            }
                        }
                    });
            }

            return result;
        }

        private static List<T> FilterItems<T>(
            IReadOnlyList<T> items,
            string searchQuery,
            Func<T, string> displayFormatter,
            Func<T, string>? matchKeySelector) where T : notnull
        {
            if (string.IsNullOrWhiteSpace(searchQuery))
            {
                return new List<T>(items);
            }

            var query = searchQuery.Trim();
            var matches = new List<T>();
            foreach (var item in items)
            {
                var text = displayFormatter(item);
                var key = matchKeySelector?.Invoke(item);
                if (text.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    (key is not null && key.Contains(query, StringComparison.OrdinalIgnoreCase)))
                {
                    matches.Add(item);
                }
            }
            return matches;
        }

        private static IRenderable BuildMenuRenderable<T>(
            string title,
            IReadOnlyList<T> items,
            int selectedIndex,
            int pageSize,
            string searchQuery,
            bool enableSearch,
            bool allowCancel,
            Func<T, string> displayFormatter,
            string cancelHint = "cancel") where T : notnull
        {
            var rows = new List<IRenderable>();

            if (!string.IsNullOrWhiteSpace(title))
            {
                rows.Add(new Markup(title));
            }

            if (enableSearch)
            {
                var searchDisplay = string.IsNullOrEmpty(searchQuery)
                    ? "[dim]Type to filter options...[/]"
                    : $"[bold #8fb26a]{Markup.Escape(searchQuery)}[/]";
                rows.Add(new Markup($"[dim]Search:[/] {searchDisplay}"));
            }

            rows.Add(Text.Empty);

            if (items.Count == 0)
            {
                rows.Add(new Markup("  [dim](No matching options)[/]"));
            }
            else
            {
                var pageStart = (selectedIndex / pageSize) * pageSize;
                var pageEnd = Math.Min(items.Count, pageStart + pageSize);

                if (pageStart > 0)
                {
                    rows.Add(new Markup($"  [dim]▲ ({pageStart} more above)[/]"));
                }

                for (var i = pageStart; i < pageEnd; i++)
                {
                    var item = items[i];
                    var text = displayFormatter(item);
                    if (i == selectedIndex)
                    {
                        rows.Add(new Markup($"[bold #8fb26a]❯ [/]{text}"));
                    }
                    else
                    {
                        rows.Add(new Markup($"  {text}"));
                    }
                }

                if (pageEnd < items.Count)
                {
                    rows.Add(new Markup($"  [dim]▼ ({items.Count - pageEnd} more below)[/]"));
                }
            }

            rows.Add(Text.Empty);
            var hint = allowCancel
                ? $"[dim](Use ↑/↓ to navigate, Enter to select, ESC to {cancelHint})[/]"
                : "[dim](Use ↑/↓ to navigate, Enter to select)[/]";
            rows.Add(new Markup(hint));

            return new Rows(rows);
        }

        internal static T? MatchItem<T>(
            IReadOnlyList<T> items,
            string input,
            Func<T, string> displayFormatter,
            Func<T, string>? matchKeySelector) where T : notnull
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

