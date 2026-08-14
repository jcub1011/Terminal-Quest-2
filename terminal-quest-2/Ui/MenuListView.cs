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

        /// <summary>What the chevron on a row that leads deeper costs at the right edge.</summary>
        private const string Submenu = " >";

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
        /// Where the value column starts, or 0 to right-align it against the far edge.
        /// <para>
        /// A page of settings wants its values lined up under each other so they read as a column;
        /// a page of choices wants the aside pushed away from the name it belongs to. Both shapes
        /// come up, so the caller says which it wants.
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

            // No scroll window, unlike the save and class lists: every menu here is a handful of
            // rows, and keeping the drawn row and its index the same number is what lets the
            // settings screen drop an editor onto a row without any arithmetic to get wrong.
            for (var row = 0; row < height && row < _rows.Count; row++)
            {
                DrawRow(_rows[row], row, width);
            }

            return true;
        }

        private void DrawRow(MenuRow entry, int row, int width)
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

            var label = Fit(entry.Label, Math.Max(0, width - MarkerWidth));
            AddStr(label);

            // The chevron owns the right edge outright, so a long value is dropped or truncated
            // before the one mark saying this row leads somewhere is.
            var edge = width;

            if (entry.HasSubmenu && width > MarkerWidth + Submenu.Length)
            {
                edge = width - Submenu.Length;

                Move(edge, row);
                SetRole(TextRole.System);
                AddStr(Submenu);
            }

            if (entry.Value.Length == 0)
            {
                return;
            }

            var start = MarkerWidth + label.Length + 1;
            var column = ValueColumn > 0 ? ValueColumn : edge - entry.Value.Length;

            // Dropped rather than overlapped: a value crushed against its own label is worse than
            // a value the player can see by widening the terminal.
            if (column < start || column >= edge)
            {
                return;
            }

            Move(column, row);
            SetRole(TextRole.System);
            AddStr(Fit(entry.Value, edge - column));
        }

        private int Clamp(int index) => Math.Clamp(index, 0, Math.Max(0, _rows.Count - 1));

        private static string Fit(string text, int width) =>
            text.Length <= width ? text : text[..Math.Max(0, width)];
    }
}
