using Spectre.Console;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Clean, standard menu helper built entirely on Spectre.Console's native <see cref="SelectionPrompt{T}"/>.
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
            bool enableSearch = false) where T : notnull
        {
            ArgumentNullException.ThrowIfNull(items);
            if (items.Count == 0)
            {
                throw new ArgumentException("Menu must have at least one item.", nameof(items));
            }

            AnsiConsole.Clear();
            renderHeader?.Invoke();

            var prompt = new SelectionPrompt<T>()
                .Title(title)
                .PageSize(customPageSize ?? 10)
                .HighlightStyle(new Style(new Color(0x8f, 0xb2, 0x6a), decoration: Decoration.Bold))
                .UseConverter(displayFormatter);

            if (enableSearch)
            {
                prompt.EnableSearch();
                prompt.SearchPlaceholderText("[dim]Type to filter options...[/]");
            }

            prompt.AddChoices(items);

            return AnsiConsole.Prompt(prompt);
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

