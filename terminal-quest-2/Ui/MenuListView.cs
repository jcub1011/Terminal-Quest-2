using Terminal.Gui.Views;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The rows of whichever menu is open - the start page's options, or a level of the settings.
    /// <para>
    /// A <see cref="ListView"/> with a <see cref="StyledListSource{T}"/>, like the save and class
    /// lists. A menu row is the same shape wherever it appears - a label, a value, whether it is in
    /// force, and whether it leads deeper - so callers hand over plain <see cref="MenuRow"/> values
    /// and this decides the columns.
    /// </para>
    /// </summary>
    internal sealed class MenuListView : ListView
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

        private readonly StyledListSource<MenuRow> _source;

        public MenuListView()
        {
            // Both hosts drive this from their own key handlers, so it stays out of the focus chain.
            CanFocus = false;

            _source = new StyledListSource<MenuRow>(Row);
            Source = _source;
        }

        /// <summary>What to draw. Setting it keeps the cursor in range of the new rows.</summary>
        public IReadOnlyList<MenuRow> Rows
        {
            get => _source.Items;

            set
            {
                _source.Items = value;
                SelectedItem = Index;
                SetNeedsDraw();
            }
        }

        /// <summary>Where the cursor is resting, which is not the same as what is in force.</summary>
        public int SelectedIndex
        {
            get => Index;

            set
            {
                SelectedItem = Math.Clamp(value, 0, Math.Max(0, Rows.Count - 1));
                EnsureSelectedItemVisible();
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

        /// <summary>
        /// The highlight as a plain index. <see cref="ListView.SelectedItem"/> is null for an empty
        /// list, and every use here wants a number in range of what is currently listed.
        /// </summary>
        private int Index => Math.Clamp(SelectedItem ?? 0, 0, Math.Max(0, Rows.Count - 1));

        /// <summary>Moves the cursor, clamping at both ends rather than wrapping.</summary>
        public void MoveSelection(int delta) => SelectedIndex = Index + delta;

        /// <summary>
        /// Which row of this pane an item is drawn on, for a caller placing something over it.
        /// <para>
        /// <see cref="SettingsWindow"/> drops its editor onto the row being edited, and needs the
        /// position on screen rather than the position in the list. The two used to be the same
        /// number because the hand-drawn version had no scroll window; asking for it outright means
        /// they no longer have to be.
        /// </para>
        /// </summary>
        public int RowOf(int index) => index - Viewport.Y;

        /// <summary>
        /// One menu row: a cursor, an in-force marker, the label, a chevron where it leads deeper,
        /// and the value.
        /// </summary>
        private StyledLine Row(MenuRow entry, int width, bool isCursor)
        {
            var line = new StyledLine();

            line.Append(isCursor ? "> " : "  ", TextRole.System);
            line.Append(entry.IsActive ? "* " : "  ", TextRole.Place);

            // Clamped rather than allowed to run off: a narrow terminal pulls the column left and
            // clips the labels, which is legible, where drawing past the viewport is not. Once the
            // clamp has eaten the gap there is no column left at all, and the labels take the width
            // back rather than being clipped for a right-hand side that cannot be drawn.
            var gutter = Math.Min(MarkerWidth + LongestVisibleLabel() + Gap, width - Submenu.Length);
            var labelWidth = gutter > MarkerWidth + Gap
                ? gutter - MarkerWidth - Gap
                : Math.Max(0, width - MarkerWidth);

            // Green wins over the cursor's brightness when a row is both: the arrow already says
            // where the cursor is, and nothing else says what is in force.
            var label = Fit(entry.Label, labelWidth);

            line.Append(
                label,
                entry.IsActive ? TextRole.Place
                : isCursor ? TextRole.Command
                : TextRole.Normal);

            // The chevron owns the gutter outright, so a long value is dropped or truncated before
            // the one mark saying this row leads somewhere is.
            if (gutter <= MarkerWidth)
            {
                return line;
            }

            if (entry.HasSubmenu)
            {
                Pad(line, gutter);
                line.Append(Submenu, TextRole.System);
            }

            if (entry.Value.Length == 0)
            {
                return line;
            }

            var column = ValueColumn > 0 ? ValueColumn : gutter + Submenu.Length + 1;

            // Dropped rather than overlapped: a value crushed against its own label is worse than
            // a value the player can see by widening the terminal. Only a fixed column can land on
            // a label - a measured one is past every one of them by construction.
            if (column < MarkerWidth + label.Length + 1 || column >= width)
            {
                return line;
            }

            Pad(line, column);
            line.Append(Fit(entry.Value, width - column), TextRole.System);

            return line;
        }

        /// <summary>The longest label among the rows currently on screen.</summary>
        /// <remarks>
        /// Measured over the visible window rather than the whole menu, so a long label on a page
        /// that scrolls cannot push every other row's value off to the right. The window comes from
        /// the viewport because a data source is asked for one row at a time and is never told which
        /// others are showing.
        /// </remarks>
        private int LongestVisibleLabel()
        {
            var first = Math.Clamp(Viewport.Y, 0, Math.Max(0, Rows.Count - 1));
            var last = Math.Min(Rows.Count, first + Math.Max(0, Viewport.Height));

            var longest = 0;
            for (var index = first; index < last; index++)
            {
                longest = Math.Max(longest, Rows[index].Label.Length);
            }

            return longest;
        }

        /// <summary>Blanks a row out to a column, so the next run starts where it belongs.</summary>
        private static void Pad(StyledLine line, int column)
        {
            if (column > line.Length)
            {
                line.Append(new string(' ', column - line.Length), TextRole.Normal);
            }
        }

        private static string Fit(string text, int width) =>
            text.Length <= width ? text : text[..Math.Max(0, width)];
    }
}
