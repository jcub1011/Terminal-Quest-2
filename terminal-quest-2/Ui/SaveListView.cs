using Terminal.Gui.ViewBase;

using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The list of saves behind Load on the startup screen.
    /// <para>
    /// Hand-drawn like <see cref="NarrationView"/> and <see cref="StatusView"/> rather than built
    /// on a stock <c>ListView</c>, for the same reason they are: a stock control paints one scheme
    /// across the whole row, and the save name wants to read differently from when it was saved.
    /// Not built on <see cref="MenuListView"/> either - that draws one value against a label, and
    /// this has three columns and a scroll window.
    /// </para>
    /// </summary>
    internal sealed class SaveListView : ThemedView
    {
        /// <summary>Two columns for the cursor, and one space before the saved-at column.</summary>
        private const int NameColumn = 2;

        /// <summary>Widest the size column ever gets: <c>1023.9 KB</c>.</summary>
        private const int SizeWidth = 9;

        /// <summary>Width of <c>yyyy-MM-dd HH:mm</c>, plus the gap before the size.</summary>
        private const int SavedWidth = 16 + 3;

        /// <summary>Below this, there is no room for the columns and only names are drawn.</summary>
        private const int MinimumForColumns = 40;

        private IReadOnlyList<SaveEntry> _saves = [];
        private int _selectedIndex;

        /// <summary>The saves to offer, most recently saved first.</summary>
        public IReadOnlyList<SaveEntry> Saves
        {
            get => _saves;
            set
            {
                _saves = value ?? [];
                _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _saves.Count - 1));
                SetNeedsDraw();
            }
        }

        /// <summary>Where the cursor is resting.</summary>
        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                _selectedIndex = Math.Clamp(value, 0, Math.Max(0, _saves.Count - 1));
                SetNeedsDraw();
            }
        }

        /// <summary>The highlighted save, or null when there are none.</summary>
        public SaveEntry? Selected =>
            _selectedIndex >= 0 && _selectedIndex < _saves.Count ? _saves[_selectedIndex] : null;

        /// <summary>Moves the highlight, clamping at both ends rather than wrapping.</summary>
        public void MoveSelection(int delta)
        {
            if (_saves.Count == 0)
            {
                return;
            }

            SelectedIndex = _selectedIndex + delta;
        }

        /// <summary>
        /// Puts the cursor on a save by name. Used after a save is copied or renamed, when the
        /// list has been rebuilt and re-sorted underneath it and the index no longer means what it
        /// did. Does nothing when there is no such save.
        /// </summary>
        public void Select(string? name)
        {
            for (var index = 0; index < _saves.Count; index++)
            {
                if (SaveStore.Matches(_saves[index].Name, name))
                {
                    SelectedIndex = index;
                    return;
                }
            }
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
                AddStr(Fit("No saves yet.  Left goes back, and New Save starts one.", width));
                return true;
            }

            // Keep the highlight on screen when the list is longer than the pane.
            var first = ScrollWindowStart(_selectedIndex, _saves.Count, height);

            for (var row = 0; row < height && first + row < _saves.Count; row++)
            {
                DrawRow(_saves[first + row], row, width, first + row == _selectedIndex);
            }

            return true;
        }

        private void DrawRow(SaveEntry save, int row, int width, bool isSelected)
        {
            Move(0, row);
            SetRole(TextRole.System);
            AddStr(isSelected ? "> " : "  ");

            SetRole(isSelected ? TextRole.Command : TextRole.Normal);

            // A narrow terminal loses the two asides rather than the names: which save is which is
            // the one thing this list cannot do without.
            if (width < MinimumForColumns)
            {
                AddStr(Fit(save.Name, width - NameColumn));
                return;
            }

            var savedColumn = width - (SavedWidth + SizeWidth);
            AddStr(Fit(save.Name, Math.Max(0, savedColumn - NameColumn - 1)));

            SetRole(TextRole.System);

            Move(savedColumn, row);
            AddStr(save.LastPlayedText);

            // Right-aligned, so the numbers line up under each other however wide they are.
            var size = save.SizeText;
            Move(width - size.Length, row);
            AddStr(size);
        }

        private static string Fit(string text, int width) =>
            width <= 0 ? string.Empty
            : text.Length <= width ? text
            : text[..width];
    }
}
