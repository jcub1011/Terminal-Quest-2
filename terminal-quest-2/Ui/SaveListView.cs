using Terminal.Gui.ViewBase;

using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The list of saves on the startup screen.
    /// <para>
    /// Hand-drawn like <see cref="NarrationView"/> and <see cref="StatusView"/> rather than built
    /// on a stock <c>ListView</c>, for the same reason they are: a stock control paints one scheme
    /// across the whole row, and the save name wants to read differently from its turn count.
    /// </para>
    /// </summary>
    internal sealed class SaveListView : ThemedView
    {
        private IReadOnlyList<SaveMetadata> _saves = [];
        private int _selectedIndex;

        /// <summary>The saves to offer, most recently played first.</summary>
        public IReadOnlyList<SaveMetadata> Saves
        {
            get => _saves;
            set
            {
                _saves = value;
                _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, value.Count - 1));
                SetNeedsDraw();
            }
        }

        /// <summary>The highlighted save, or null when there are none.</summary>
        public SaveMetadata? Selected =>
            _selectedIndex >= 0 && _selectedIndex < _saves.Count ? _saves[_selectedIndex] : null;

        /// <summary>Moves the highlight, clamping at both ends rather than wrapping.</summary>
        public void MoveSelection(int delta)
        {
            if (_saves.Count == 0)
            {
                return;
            }

            _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _saves.Count - 1);
            SetNeedsDraw();
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            var height = Viewport.Height;

            if (width <= 0 || height <= 0)
            {
                return true;
            }

            BeginPaint(width, height);

            if (_saves.Count == 0)
            {
                Move(0, 0);
                SetRole(TextRole.System);
                AddStr(Fit("No saves yet. Name one below to begin.", width));
                return true;
            }

            // Keep the highlight on screen when the list is longer than the pane.
            var first = Math.Max(0, Math.Min(_selectedIndex - (height / 2), _saves.Count - height));
            first = Math.Max(0, first);

            for (var row = 0; row < height && first + row < _saves.Count; row++)
            {
                var save = _saves[first + row];
                var isSelected = first + row == _selectedIndex;

                Move(0, row);
                SetRole(TextRole.System);
                AddStr(isSelected ? "> " : "  ");

                SetRole(isSelected ? TextRole.Command : TextRole.Normal);
                AddStr(Fit(save.Name, Math.Max(0, width - 2)));

                var detail = save.Turn > 0
                    ? $"turn {save.Turn}   {save.LastPlayed.ToLocalTime():yyyy-MM-dd HH:mm}"
                    : "untouched";

                var column = width - detail.Length;
                if (column > save.Name.Length + 3)
                {
                    Move(column, row);
                    SetRole(TextRole.System);
                    AddStr(detail);
                }
            }

            return true;
        }

        private static string Fit(string text, int width) =>
            text.Length <= width ? text : text[..Math.Max(0, width)];
    }
}
