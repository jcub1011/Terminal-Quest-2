using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// A dedicated view docked at the bottom of the transcript pane displaying the active action choices.
    /// Anchored below the transcript so that intermediate terminal commands do not scroll it off screen.
    /// </summary>
    internal sealed class OptionsView : ThemedView
    {
        private IReadOnlyList<NarrationOption> _options = [];
        private int? _highlightedOption;
        private readonly List<(NarrationOption Option, int RowIndex, string Prefix, string Text)> _renderedRows = [];

        public OptionsView()
        {
            CanFocus = false;
            Visible = false;
        }

        /// <summary>
        /// The active choices currently presented to the player.
        /// </summary>
        public IReadOnlyList<NarrationOption> Options => _options;

        /// <summary>
        /// The 1-based number of the currently selected/highlighted option, or null if none.
        /// </summary>
        public int? HighlightedOption
        {
            get => _highlightedOption;
            set
            {
                if (_highlightedOption == value)
                {
                    return;
                }

                _highlightedOption = value;
                SetNeedsDraw();
            }
        }

        /// <summary>
        /// Raised when the player clicks on an option row.
        /// </summary>
        public event Action<NarrationOption>? OptionClicked;

        /// <summary>
        /// Updates the options displayed by this view.
        /// </summary>
        public void SetOptions(IReadOnlyList<string> options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var list = new List<NarrationOption>();
            for (var i = 0; i < options.Count; i++)
            {
                if (!string.IsNullOrWhiteSpace(options[i]))
                {
                    list.Add(new NarrationOption(list.Count + 1, options[i].Trim()));
                }
            }

            SetOptions(list);
        }

        /// <summary>
        /// Sets the active narration options directly.
        /// </summary>
        public void SetOptions(IReadOnlyList<NarrationOption> options)
        {
            ArgumentNullException.ThrowIfNull(options);

            _options = options;
            _highlightedOption = null;
            Visible = _options.Count > 0;
            RebuildRenderedRows(Viewport.Width > 0 ? Viewport.Width : 80);
            SetNeedsDraw();
        }

        /// <summary>
        /// Clears all options and hides the view.
        /// </summary>
        public void ClearOptions()
        {
            _options = [];
            _highlightedOption = null;
            _renderedRows.Clear();
            Visible = false;
            Height = 0;
            SetNeedsDraw();
        }

        /// <summary>
        /// Calculates the number of terminal rows required to display all current options at the given width.
        /// </summary>
        public int CalculateRequiredHeight(int width)
        {
            if (_options.Count == 0 || width <= 0)
            {
                return 0;
            }

            var count = 0;
            foreach (var opt in _options)
            {
                var prefix = $"[{opt.Number}] ";
                var availableTextWidth = Math.Max(1, width - prefix.Length);
                var wrapped = WrapText(opt.Text, availableTextWidth);
                count += Math.Max(1, wrapped.Count);
            }

            return count;
        }

        private void RebuildRenderedRows(int width)
        {
            _renderedRows.Clear();
            if (_options.Count == 0)
            {
                return;
            }

            var availableWidth = Math.Max(1, width);
            var rowIndex = 0;

            foreach (var opt in _options)
            {
                var prefix = $"[{opt.Number}] ";
                var indent = new string(' ', prefix.Length);
                var availableTextWidth = Math.Max(1, availableWidth - prefix.Length);
                var wrapped = WrapText(opt.Text, availableTextWidth);

                for (var lineIdx = 0; lineIdx < wrapped.Count; lineIdx++)
                {
                    var linePrefix = lineIdx == 0 ? prefix : indent;
                    _renderedRows.Add((opt, rowIndex++, linePrefix, wrapped[lineIdx]));
                }
            }
        }

        private static List<string> WrapText(string text, int width)
        {
            var lines = new List<string>();
            if (string.IsNullOrEmpty(text) || width <= 0)
            {
                lines.Add(string.Empty);
                return lines;
            }

            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var currentLine = string.Empty;

            foreach (var word in words)
            {
                if (currentLine.Length == 0)
                {
                    if (word.Length > width)
                    {
                        var remaining = word;
                        while (remaining.Length > width)
                        {
                            lines.Add(remaining[..width]);
                            remaining = remaining[width..];
                        }
                        currentLine = remaining;
                    }
                    else
                    {
                        currentLine = word;
                    }
                }
                else if (currentLine.Length + 1 + word.Length <= width)
                {
                    currentLine += " " + word;
                }
                else
                {
                    lines.Add(currentLine);
                    if (word.Length > width)
                    {
                        var remaining = word;
                        while (remaining.Length > width)
                        {
                            lines.Add(remaining[..width]);
                            remaining = remaining[width..];
                        }
                        currentLine = remaining;
                    }
                    else
                    {
                        currentLine = word;
                    }
                }
            }

            if (currentLine.Length > 0)
            {
                lines.Add(currentLine);
            }

            return lines.Count == 0 ? [string.Empty] : lines;
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            ArgumentNullException.ThrowIfNull(mouse);

            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } pos)
            {
                var clickedRow = _renderedRows.FirstOrDefault(r => r.RowIndex == pos.Y);
                if (clickedRow.Option is not null)
                {
                    HighlightedOption = clickedRow.Option.Number;
                    OptionClicked?.Invoke(clickedRow.Option);
                    return true;
                }
            }

            return base.OnMouseEvent(mouse);
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            var height = Viewport.Height;

            if (width <= 0 || height <= 0 || _options.Count == 0)
            {
                return true;
            }

            BeginPaint(width, height);
            RebuildRenderedRows(width);

            for (var y = 0; y < Math.Min(height, _renderedRows.Count); y++)
            {
                Move(0, y);
                var row = _renderedRows[y];
                var isHighlighted = _highlightedOption == row.Option.Number;

                if (isHighlighted)
                {
                    SetAttribute(Theme.OptionSelection);
                    var fullText = row.Prefix + row.Text;
                    if (fullText.Length > width)
                    {
                        fullText = fullText[..width];
                    }
                    AddStr(fullText);
                    if (fullText.Length < width)
                    {
                        AddStr(Blank(width - fullText.Length));
                    }
                }
                else
                {
                    SetRole(TextRole.Item);
                    AddStr(row.Prefix);

                    SetRole(TextRole.Normal);
                    var remainingWidth = Math.Max(0, width - row.Prefix.Length);
                    var lineText = row.Text.Length > remainingWidth ? row.Text[..remainingWidth] : row.Text;
                    AddStr(lineText);

                    var drawn = row.Prefix.Length + lineText.Length;
                    if (drawn < width)
                    {
                        AddStr(Blank(width - drawn));
                    }
                }
            }

            return true;
        }
    }
}
