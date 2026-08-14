using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The rows of whichever menu is open - the start page's options, or a level of the settings.
    /// <para>
    /// The one hand-drawn list the game does share. <see cref="SaveListView"/> and
    /// <see cref="ClassListView"/> were deliberately left unfactored because their columns differ
    /// and a common control would have taken a formatter callback for the sake of two call sites.
    /// That does not hold here: a menu row is the same shape wherever it appears - a label, a
    /// value, whether it is in force, and whether it leads deeper - callers hand over plain
    /// <see cref="MenuRow"/> values rather than a delegate, and the number of call sites grows
    /// with every setting added.
    /// </para>
    /// </summary>
    internal sealed class MenuListView : ThemedView
    {
        /// <summary>Two columns for the cursor and two for the active marker.</summary>
        private const int MarkerWidth = 4;

        /// <summary>What sits between the longest label and the column the rest of the row starts in.</summary>
        private const int Gap = 2;

        /// <summary>
        /// The chevron on a row that leads deeper. Its width is reserved on every row, so the values
        /// line up whether or not the row beside them goes anywhere.
        /// </summary>
        private const string Submenu = ">";

        private IReadOnlyList<MenuRow> _rows = [];
        private int _selectedIndex;

        /// <summary>What to draw. Setting it keeps the cursor in range of the new rows.</summary>
        public IReadOnlyList<MenuRow> Rows
        {
            get => _rows;

            set
            {
                _rows = value ?? [];
                _selectedIndex = Clamp(_selectedIndex);
                SetNeedsDraw();
            }
        }

        /// <summary>Where the cursor is resting, which is not the same as what is in force.</summary>
        public int SelectedIndex
        {
            get => _selectedIndex;

            set
            {
                _selectedIndex = Clamp(value);
                SetNeedsDraw();
            }
        }

        /// <summary>
        /// Where the value column starts, or 0 to measure one just past the longest label.
        /// <para>
        /// A page of settings wants its values lined up under each other so they read as a column;
        /// a page of choices wants the aside pushed away from the name it belongs to. Both shapes
        /// come up, so the caller says which it wants.
        /// </para>
        /// <para>
        /// A fixed column is also what lets <see cref="SettingsWindow"/> drop its editor onto a row:
        /// it needs the number in advance, and a measured column would move under the field.
        /// </para>
        /// </summary>
        public int ValueColumn { get; set; }

        /// <summary>Moves the cursor, clamping at both ends rather than wrapping.</summary>
        public void MoveSelection(int delta) => SelectedIndex = _selectedIndex + delta;

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            var height = Viewport.Height;

            if (width <= 0 || height <= 0)
            {
                return true;
            }

            BeginPaint(width, height);

            var drawn = Math.Min(height, _rows.Count);

            // Measured on every draw rather than cached against Rows: a page rebuilds its rows on
            // every read, so there is no one moment to invalidate on, and a handful of lengths is
            // cheaper than a stale column.
            var longest = 0;

            for (var row = 0; row < drawn; row++)
            {
                longest = Math.Max(longest, _rows[row].Label.Length);
            }

            // Clamped rather than allowed to run off: a narrow terminal pulls the column left and
            // clips the labels, which is legible, where drawing past the viewport is not. Once the
            // clamp has eaten the gap there is no column left at all, and the labels take the width
            // back rather than being clipped for a right-hand side that cannot be drawn.
            var gutter = Math.Min(MarkerWidth + longest + Gap, width - Submenu.Length);
            var labelWidth = gutter > MarkerWidth + Gap
                ? gutter - MarkerWidth - Gap
                : Math.Max(0, width - MarkerWidth);

            // No scroll window, unlike the save and class lists: every menu here is a handful of
            // rows, and keeping the drawn row and its index the same number is what lets the
            // settings screen drop an editor onto a row without any arithmetic to get wrong.
            for (var row = 0; row < drawn; row++)
            {
                DrawRow(_rows[row], row, width, gutter, labelWidth);
            }

            return true;
        }

        private void DrawRow(MenuRow entry, int row, int width, int gutter, int labelWidth)
        {
            var isCursor = row == _selectedIndex;

            Move(0, row);
            SetRole(TextRole.System);
            AddStr(isCursor ? "> " : "  ");

            SetRole(TextRole.Place);
            AddStr(entry.IsActive ? "* " : "  ");

            // Green wins over the cursor's brightness when a row is both: the arrow already says
            // where the cursor is, and nothing else says what is in force.
            SetRole(entry.IsActive ? TextRole.Place
                : isCursor ? TextRole.Command
                : TextRole.Normal);

            var label = Fit(entry.Label, labelWidth);
            AddStr(label);

            // The chevron owns the gutter outright, so a long value is dropped or truncated before
            // the one mark saying this row leads somewhere is. Drawn after the label for the same
            // reason: a label wide enough to reach the gutter loses the argument.
            if (gutter <= MarkerWidth)
            {
                return;
            }

            if (entry.HasSubmenu)
            {
                Move(gutter, row);
                SetRole(TextRole.System);
                AddStr(Submenu);
            }

            if (entry.Value.Length == 0)
            {
                return;
            }

            var column = ValueColumn > 0 ? ValueColumn : gutter + Submenu.Length + 1;

            // Dropped rather than overlapped: a value crushed against its own label is worse than
            // a value the player can see by widening the terminal. Only a fixed column can land on
            // a label - a measured one is past every one of them by construction.
            if (column < MarkerWidth + label.Length + 1 || column >= width)
            {
                return;
            }

            Move(column, row);
            SetRole(TextRole.System);
            AddStr(Fit(entry.Value, width - column));
        }

        private int Clamp(int index) => Math.Clamp(index, 0, Math.Max(0, _rows.Count - 1));

        private static string Fit(string text, int width) =>
            text.Length <= width ? text : text[..Math.Max(0, width)];
    }
}
