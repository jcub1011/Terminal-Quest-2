using System.Text;
using Spectre.Console;
using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Interactive prompt for player input built with standard Spectre.Console components.
    /// </summary>
    internal sealed class CliPrompt
    {
        private readonly ExternalEditor _editor;
        private readonly SaveStore? _store;

        public CliPrompt(ExternalEditor editor, SaveStore? store = null)
        {
            _editor = editor ?? throw new ArgumentNullException(nameof(editor));
            _store = store;
        }

        public async Task<string?> ReadLineAsync(
            IReadOnlyList<NarrationOption>? activeOptions = null,
            string promptSymbol = "❯",
            SaveStore? store = null,
            Action? onRepaint = null)
        {
            if (Console.IsInputRedirected)
            {
                var line = Console.ReadLine();
                return line?.Trim();
            }

            var effectiveStore = store ?? _store;
            var optionList = activeOptions ?? [];

            void EnsureSpace()
            {
                try
                {
                    var spaceNeeded = optionList.Count > 0 ? optionList.Count + 9 : 8;
                    if (Console.CursorTop + spaceNeeded >= Console.BufferHeight)
                    {
                        for (var i = 0; i < spaceNeeded; i++)
                        {
                            Console.WriteLine();
                        }
                        Console.SetCursorPosition(0, Math.Max(0, Console.CursorTop - spaceNeeded));
                    }
                }
                catch
                {
                }
            }

            EnsureSpace();
            var optionsTop = 0;
            try
            {
                optionsTop = Console.CursorTop;
            }
            catch
            {
            }

            if (optionList.Count > 0)
            {
                for (var i = 0; i <= optionList.Count; i++)
                {
                    Console.WriteLine();
                }
            }

            var startTop = optionsTop + (optionList.Count > 0 ? optionList.Count + 1 : 0);
            var startLeft = 0;
            try
            {
                Console.SetCursorPosition(0, startTop);
                AnsiConsole.Markup($"[bold #8fb26a]{promptSymbol}[/] ");
                startLeft = Console.CursorLeft;
                startTop = Console.CursorTop;
            }
            catch
            {
                startLeft = 0;
                startTop = 0;
            }

            var buffer = new StringBuilder();
            var cursorIndex = 0;
            var lastLength = 0;
            var lastRenderedSuggestions = 0;
            var cancelled = false;

            const int MaxVisibleSuggestions = 6;
            IReadOnlyList<SuggestionItem> currentSuggestions = [];
            var isChoosing = false;
            var selectedIndex = -1;
            var selectedOptionIndex = -1;

            void RefreshSuggestions()
            {
                var (items, choosing) = PlayerCommands.GetSuggestions(buffer.ToString(), effectiveStore);
                currentSuggestions = items;
                isChoosing = choosing;
                if (!isChoosing || currentSuggestions.Count == 0)
                {
                    selectedIndex = -1;
                }
                else if (selectedIndex >= currentSuggestions.Count)
                {
                    selectedIndex = currentSuggestions.Count - 1;
                }
            }

            void RenderAll()
            {
                if (optionList.Count > 0)
                {
                    RenderOptionsList(optionsTop, optionList, selectedOptionIndex);
                }
                RedrawInput(startLeft, startTop, buffer.ToString(), cursorIndex, ref lastLength);
                RenderSuggestions(startTop, currentSuggestions, selectedIndex, isChoosing, ref lastRenderedSuggestions, MaxVisibleSuggestions);
                SetCursorPositionSafe(startLeft + cursorIndex, startTop);
            }

            void HandleResizeRepaint()
            {
                if (onRepaint != null)
                {
                    try
                    {
                        onRepaint();
                    }
                    catch
                    {
                    }
                }
                else
                {
                    try
                    {
                        AnsiConsole.Clear();
                    }
                    catch
                    {
                    }
                }

                EnsureSpace();
                try
                {
                    optionsTop = Console.CursorTop;
                }
                catch
                {
                    optionsTop = 0;
                }

                if (optionList.Count > 0)
                {
                    for (var i = 0; i <= optionList.Count; i++)
                    {
                        Console.WriteLine();
                    }
                }

                startTop = optionsTop + (optionList.Count > 0 ? optionList.Count + 1 : 0);
                try
                {
                    Console.SetCursorPosition(0, startTop);
                    AnsiConsole.Markup($"[bold #8fb26a]{promptSymbol}[/] ");
                    startLeft = Console.CursorLeft;
                    startTop = Console.CursorTop;
                }
                catch
                {
                    startLeft = 0;
                    startTop = 0;
                }

                lastLength = 0;
                lastRenderedSuggestions = 0;
                RefreshSuggestions();
                RenderAll();
            }

            // Initial render
            RenderAll();

            while (true)
            {
                var key = await TerminalMonitor.ReadKeyOrResizeAsync(HandleResizeRepaint);

                if (key.Key == ConsoleKey.Escape)
                {
                    if (buffer.Length > 0)
                    {
                        buffer.Clear();
                        cursorIndex = 0;
                        selectedOptionIndex = -1;
                        selectedIndex = -1;
                        RefreshSuggestions();
                        RenderAll();
                        continue;
                    }

                    ClearSuggestions(startTop, lastRenderedSuggestions);
                    cancelled = true;
                    break;
                }
                else if (key.Key == ConsoleKey.Enter)
                {
                    if (isChoosing && currentSuggestions.Count > 0 && selectedIndex >= 0 && selectedIndex < currentSuggestions.Count)
                    {
                        var chosen = currentSuggestions[selectedIndex];
                        if (!string.IsNullOrEmpty(chosen.InsertText))
                        {
                            buffer.Clear().Append(chosen.InsertText);
                            cursorIndex = buffer.Length;
                            selectedIndex = -1;
                            selectedOptionIndex = -1;
                            RefreshSuggestions();
                            RenderAll();
                            continue;
                        }
                    }

                    ClearSuggestions(startTop, lastRenderedSuggestions);
                    break;
                }
                else if (key.Key == ConsoleKey.DownArrow)
                {
                    if (isChoosing && currentSuggestions.Count > 0)
                    {
                        selectedIndex = (selectedIndex + 1) % currentSuggestions.Count;
                        RenderAll();
                    }
                    else if (optionList.Count > 0)
                    {
                        if (selectedOptionIndex == -1)
                        {
                            selectedOptionIndex = 0;
                        }
                        else
                        {
                            selectedOptionIndex = (selectedOptionIndex + 1) % optionList.Count;
                        }

                        buffer.Clear().Append(optionList[selectedOptionIndex].Text);
                        cursorIndex = buffer.Length;
                        selectedIndex = -1;
                        RefreshSuggestions();
                        RenderAll();
                    }
                }
                else if (key.Key == ConsoleKey.UpArrow)
                {
                    if (isChoosing && currentSuggestions.Count > 0)
                    {
                        selectedIndex = selectedIndex <= 0 ? currentSuggestions.Count - 1 : selectedIndex - 1;
                        RenderAll();
                    }
                    else if (optionList.Count > 0)
                    {
                        if (selectedOptionIndex == -1)
                        {
                            selectedOptionIndex = optionList.Count - 1;
                        }
                        else
                        {
                            selectedOptionIndex = selectedOptionIndex <= 0 ? optionList.Count - 1 : selectedOptionIndex - 1;
                        }

                        buffer.Clear().Append(optionList[selectedOptionIndex].Text);
                        cursorIndex = buffer.Length;
                        selectedIndex = -1;
                        RefreshSuggestions();
                        RenderAll();
                    }
                }
                else if (key.Key == ConsoleKey.Tab)
                {
                    if (isChoosing && currentSuggestions.Count > 0)
                    {
                        var pickIndex = (selectedIndex >= 0 && selectedIndex < currentSuggestions.Count) ? selectedIndex : 0;
                        var chosen = currentSuggestions[pickIndex];
                        if (!string.IsNullOrEmpty(chosen.InsertText))
                        {
                            buffer.Clear().Append(chosen.InsertText);
                            cursorIndex = buffer.Length;
                            selectedIndex = -1;
                            selectedOptionIndex = -1;
                            RefreshSuggestions();
                            RenderAll();
                        }
                    }
                }
                else if (key.Key == ConsoleKey.RightArrow)
                {
                    if (cursorIndex < buffer.Length)
                    {
                        cursorIndex++;
                        SetCursorPositionSafe(startLeft + cursorIndex, startTop);
                    }
                    else if (isChoosing && currentSuggestions.Count > 0)
                    {
                        var pickIndex = (selectedIndex >= 0 && selectedIndex < currentSuggestions.Count) ? selectedIndex : 0;
                        var chosen = currentSuggestions[pickIndex];
                        if (!string.IsNullOrEmpty(chosen.InsertText))
                        {
                            buffer.Clear().Append(chosen.InsertText);
                            cursorIndex = buffer.Length;
                            selectedIndex = -1;
                            selectedOptionIndex = -1;
                            RefreshSuggestions();
                            RenderAll();
                        }
                    }
                }
                else if (key.Modifiers.HasFlag(ConsoleModifiers.Control) && key.Key == ConsoleKey.G)
                {
                    ClearSuggestions(startTop, lastRenderedSuggestions);
                    Console.WriteLine();
                    var edited = await _editor.EditStringAsync(buffer.ToString(), "Player Command");
                    if (!string.IsNullOrWhiteSpace(edited))
                    {
                        var trimmed = edited.Trim();
                        AnsiConsole.MarkupLine($"[bold #d7d2c4]{Markup.Escape(trimmed)}[/]");
                        return trimmed;
                    }

                    // If editor cancelled or returned empty, re-render prompt with existing buffer
                    HandleResizeRepaint();
                    continue;
                }
                else if (key.Key == ConsoleKey.Backspace)
                {
                    if (cursorIndex > 0)
                    {
                        buffer.Remove(cursorIndex - 1, 1);
                        cursorIndex--;
                        selectedIndex = -1;
                        selectedOptionIndex = -1;
                        RefreshSuggestions();
                        RenderAll();
                    }
                }
                else if (key.Key == ConsoleKey.Delete)
                {
                    if (cursorIndex < buffer.Length)
                    {
                        buffer.Remove(cursorIndex, 1);
                        selectedIndex = -1;
                        selectedOptionIndex = -1;
                        RefreshSuggestions();
                        RenderAll();
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
                    if (optionList.Count > 0
                        && (buffer.Length == 0 || selectedOptionIndex >= 0)
                        && char.IsAsciiDigit(key.KeyChar))
                    {
                        var digit = key.KeyChar - '0';
                        if (digit >= 1 && digit <= optionList.Count)
                        {
                            selectedOptionIndex = digit - 1;
                            buffer.Clear().Append(optionList[selectedOptionIndex].Text);
                            cursorIndex = buffer.Length;
                            selectedIndex = -1;
                            RefreshSuggestions();
                            RenderAll();
                            continue;
                        }
                    }

                    selectedOptionIndex = -1;
                    buffer.Insert(cursorIndex, key.KeyChar);
                    cursorIndex++;
                    selectedIndex = -1;
                    RefreshSuggestions();
                    RenderAll();
                }
            }

            if (cancelled)
            {
                return null;
            }

            Console.WriteLine();
            var input = buffer.ToString().Trim();

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
            string cancelHint = "cancel",
            Action? onRepaint = null)
        {
            if (Console.IsInputRedirected)
            {
                var line = Console.ReadLine()?.Trim();
                if (string.IsNullOrEmpty(line) && defaultValue != null)
                {
                    return defaultValue;
                }
                return line;
            }

            while (true)
            {
                var buffer = new StringBuilder();
                var cursorIndex = 0;
                var startLeft = 0;
                var startTop = 0;
                var lastLength = 0;
                var cancelled = false;

                void RenderPromptHeaders()
                {
                    if (onRepaint != null)
                    {
                        try
                        {
                            onRepaint();
                        }
                        catch
                        {
                        }
                    }

                    AnsiConsole.MarkupLine($"[dim]• Press Enter to submit, ESC to {cancelHint}[/]");
                    AnsiConsole.Markup(promptMarkup);
                    if (!string.IsNullOrEmpty(defaultValue))
                    {
                        AnsiConsole.Markup($"[dim]({Markup.Escape(defaultValue)})[/] ");
                    }

                    try
                    {
                        startLeft = Console.CursorLeft;
                        startTop = Console.CursorTop;
                    }
                    catch
                    {
                        startLeft = 0;
                        startTop = 0;
                    }
                    lastLength = 0;
                    RedrawInput(startLeft, startTop, buffer.ToString(), cursorIndex, ref lastLength);
                }

                RenderPromptHeaders();

                while (true)
                {
                    var key = TerminalMonitor.ReadKeyOrResize(RenderPromptHeaders);

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
            string cancelHint = "cancel",
            Action? onRepaint = null)
        {
            while (true)
            {
                var input = AskString(
                    promptMarkup,
                    defaultValue: defaultValue?.ToString(),
                    allowEmpty: false,
                    cancelHint: cancelHint,
                    onRepaint: onRepaint);

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

        public static bool? Confirm(
            string promptMarkup,
            bool defaultValue = true,
            string cancelHint = "cancel",
            Action? onRepaint = null)
        {
            if (Console.IsInputRedirected)
            {
                return defaultValue;
            }

            void RenderConfirm()
            {
                if (onRepaint != null)
                {
                    try
                    {
                        onRepaint();
                    }
                    catch
                    {
                    }
                }

                var hint = defaultValue
                    ? $"[dim]([bold green]Y[/]/n, ESC to {cancelHint})[/] "
                    : $"[dim](y/[bold red]N[/], ESC to {cancelHint})[/] ";

                AnsiConsole.Markup($"{promptMarkup} {hint}");
            }

            RenderConfirm();

            while (true)
            {
                var key = TerminalMonitor.ReadKeyOrResize(RenderConfirm);
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

        public static bool WaitKeyOrCancel(
            string? message = null,
            Action? onRepaint = null)
        {
            if (Console.IsInputRedirected)
            {
                return true;
            }

            void RenderWait()
            {
                if (onRepaint != null)
                {
                    try
                    {
                        onRepaint();
                    }
                    catch
                    {
                    }
                }

                if (!string.IsNullOrEmpty(message))
                {
                    AnsiConsole.MarkupLine(message);
                }
                else
                {
                    AnsiConsole.MarkupLine("[dim]Press Enter to continue, or Esc to cancel...[/]");
                }
            }

            RenderWait();

            var key = TerminalMonitor.ReadKeyOrResize(RenderWait);
            return key.Key != ConsoleKey.Escape;
        }

        private static void SetCursorPositionSafe(int left, int top)
        {
            try
            {
                var size = TerminalMonitor.GetSize();
                var width = size.Width > 0 ? size.Width : 80;
                var totalOffset = top * width + left;
                var targetTop = totalOffset / width;
                var targetLeft = totalOffset % width;
                var bufferHeight = Console.BufferHeight > 0 ? Console.BufferHeight : size.Height;
                if (targetTop < bufferHeight && targetLeft < width)
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

        private static void RenderSuggestions(
            int startTop,
            IReadOnlyList<SuggestionItem> suggestions,
            int selectedIndex,
            bool isChoosing,
            ref int lastRenderedLines,
            int maxVisible)
        {
            ClearSuggestions(startTop, lastRenderedLines);

            if (suggestions.Count == 0)
            {
                lastRenderedLines = 0;
                return;
            }

            var linesToDraw = new List<string>();

            var windowStart = 0;
            if (selectedIndex >= 0 && selectedIndex >= maxVisible)
            {
                windowStart = selectedIndex - maxVisible + 1;
            }
            var windowEnd = Math.Min(suggestions.Count, windowStart + maxVisible);

            for (var i = windowStart; i < windowEnd; i++)
            {
                var item = suggestions[i];
                var isSelected = (i == selectedIndex);

                var prefix = isSelected ? "[bold #8fb26a]❯ [/]" : "  ";
                var displayMarkup = isSelected
                    ? $"[bold #f0e6d2]{Markup.Escape(item.DisplayText)}[/]"
                    : Theme.Format(item.DisplayText, item.Role);

                var pad = Math.Max(1, 24 - item.DisplayText.Length);
                var spacing = new string(' ', pad);

                var summaryMarkup = string.IsNullOrEmpty(item.Summary)
                    ? string.Empty
                    : (isSelected ? $"[#d7d2c4]{Markup.Escape(item.Summary)}[/]" : $"[dim]{Markup.Escape(item.Summary)}[/]");

                linesToDraw.Add($"{prefix}{displayMarkup}{spacing}{summaryMarkup}");
            }

            for (var rowOffset = 0; rowOffset < linesToDraw.Count; rowOffset++)
            {
                var targetRow = startTop + 1 + rowOffset;
                if (targetRow < Console.BufferHeight)
                {
                    try
                    {
                        Console.SetCursorPosition(0, targetRow);
                        AnsiConsole.Markup(linesToDraw[rowOffset]);
                    }
                    catch
                    {
                    }
                }
            }

            lastRenderedLines = linesToDraw.Count;
        }

        private static void ClearSuggestions(int startTop, int lineCount)
        {
            if (lineCount <= 0)
            {
                return;
            }

            try
            {
                var size = TerminalMonitor.GetSize();
                var width = size.Width > 0 ? size.Width : 80;
                var emptyLine = new string(' ', Math.Max(0, width - 1));
                for (var i = 1; i <= lineCount; i++)
                {
                    var row = startTop + i;
                    var bufferHeight = Console.BufferHeight > 0 ? Console.BufferHeight : size.Height;
                    if (row < bufferHeight)
                    {
                        Console.SetCursorPosition(0, row);
                        Console.Write(emptyLine);
                    }
                }
            }
            catch
            {
            }
        }

        private static void RenderOptionsList(
            int optionsTop,
            IReadOnlyList<NarrationOption> options,
            int selectedIndex)
        {
            if (options.Count == 0)
            {
                return;
            }

            var size = TerminalMonitor.GetSize();
            var width = size.Width > 0 ? size.Width : 80;

            for (var i = 0; i < options.Count; i++)
            {
                var opt = options[i];
                var isSelected = (i == selectedIndex);
                var targetRow = optionsTop + i;

                if (targetRow < Console.BufferHeight)
                {
                    try
                    {
                        Console.SetCursorPosition(0, targetRow);
                        var prefix = isSelected ? "[bold #8fb26a]❯ [/]" : "  ";
                        var numMarkup = $"[bold #8fb26a]{opt.Number}.[/]";
                        var textMarkup = isSelected
                            ? $"[bold #f0e6d2]{Markup.Escape(opt.Text)}[/]"
                            : $"[#d7d2c4]{Markup.Escape(opt.Text)}[/]";

                        var emptyLine = new string(' ', Math.Max(0, width - 1));
                        Console.Write(emptyLine);
                        Console.SetCursorPosition(0, targetRow);
                        AnsiConsole.Markup($"{prefix}{numMarkup} {textMarkup}");
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}

