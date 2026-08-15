using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The list of saves behind Load on the startup screen.
    /// <para>
    /// A <see cref="ListView"/> with a <see cref="StyledListSource{T}"/>: the library owns the scroll
    /// window, the highlight and the keys, and what is left here is the three columns a save gets and
    /// the colours they carry.
    /// </para>
    /// </summary>
    internal sealed class SaveListView : ListView
    {
        /// <summary>Two columns for the cursor, and one space before the saved-at column.</summary>
        private const int NameColumn = 2;

        /// <summary>What sits between the longest name and the column the asides start in.</summary>
        private const int Gap = 2;

        /// <summary>Widest the size column ever gets: <c>1023.9 KB</c>.</summary>
        private const int SizeWidth = 9;

        /// <summary>Width of <c>yyyy-MM-dd HH:mm</c>, plus the gap before the size.</summary>
        private const int SavedWidth = 16 + 3;

        /// <summary>Below this, there is no room for the columns and only names are drawn.</summary>
        private const int MinimumForColumns = 40;

        /// <summary>Shown in place of the list when there is nothing in it.</summary>
        private const string Empty = "No saves yet.  Left goes back, and New Save starts one.";

        private readonly StyledListSource<SaveEntry> _source;

        public SaveListView()
        {
            // The save menu drives this from its own key handler, so the list stays out of the focus
            // chain - see ClassListView, which is here for the same reason.
            CanFocus = false;

            _source = new StyledListSource<SaveEntry>(Row);
            Source = _source;
        }

        /// <summary>The saves to offer, most recently saved first.</summary>
        public IReadOnlyList<SaveEntry> Saves
        {
            get => _source.Items;

            set
            {
                _source.Items = value;
                SelectedItem = Index;
                SetNeedsDraw();
            }
        }

        /// <summary>
        /// The highlight as a plain index. <see cref="ListView.SelectedItem"/> is null for an empty
        /// list, and every use here wants a number in range of what is currently listed.
        /// </summary>
        private int Index => Math.Clamp(SelectedItem ?? 0, 0, Math.Max(0, Saves.Count - 1));

        /// <summary>Where the cursor is resting.</summary>
        public int SelectedIndex
        {
            get => Index;

            set
            {
                SelectedItem = Math.Clamp(value, 0, Math.Max(0, Saves.Count - 1));
                EnsureSelectedItemVisible();
            }
        }

        /// <summary>The highlighted save, or null when there are none.</summary>
        public SaveEntry? Selected => Saves.Count == 0 ? null : Saves[Index];

        /// <summary>Moves the highlight, clamping at both ends rather than wrapping.</summary>
        public void MoveSelection(int delta)
        {
            if (Saves.Count == 0)
            {
                return;
            }

            SelectedIndex = Index + delta;
        }

        /// <summary>
        /// Puts the cursor on a save by name. Used after a save is copied or renamed, when the
        /// list has been rebuilt and re-sorted underneath it and the index no longer means what it
        /// did. Does nothing when there is no such save.
        /// </summary>
        public void Select(string? name)
        {
            for (var index = 0; index < Saves.Count; index++)
            {
                if (SaveStore.Matches(Saves[index].Name, name))
                {
                    SelectedIndex = index;
                    return;
                }
            }
        }

        /// <summary>
        /// Says so when there is nothing to list, which a list with no rows cannot say for itself.
        /// </summary>
        protected override bool OnDrawingContent(DrawContext? context)
        {
            if (Saves.Count > 0)
            {
                return base.OnDrawingContent(context);
            }

            var width = Viewport.Width;
            if (width <= 0 || Viewport.Height <= 0)
            {
                return true;
            }

            Move(0, 0);
            SetAttribute(Theme.Attr(TextRole.System));
            AddStr(Fit(Empty, width));

            return true;
        }

        /// <summary>
        /// One save's row: a cursor, the name, when it was last played, and how big it is.
        /// </summary>
        /// <remarks>
        /// The asides line up under each other in a column measured over the saves on screen rather
        /// than the whole list, so a name far down the list cannot push them off to the right.
        /// Scrolling reflows them, which reads better than a column set by a save nobody can see -
        /// which is why the visible window is read from the viewport here rather than measured over
        /// everything.
        /// </remarks>
        private StyledLine Row(SaveEntry save, int width, bool isSelected)
        {
            var line = new StyledLine();
            line.Append(isSelected ? "> " : "  ", TextRole.System);

            var nameRole = isSelected ? TextRole.Command : TextRole.Normal;

            // A narrow terminal loses the two asides rather than the names: which save is which is
            // the one thing this list cannot do without.
            if (width < MinimumForColumns)
            {
                line.Append(Fit(save.Name, width - NameColumn), nameRole);
                return line;
            }

            var savedColumn = Math.Min(
                NameColumn + LongestVisibleName() + Gap,
                width - (SavedWidth + SizeWidth));

            line.Append(Fit(save.Name, Math.Max(0, savedColumn - NameColumn - 1)), nameRole);
            Pad(line, savedColumn);
            line.Append(save.LastPlayedText, TextRole.System);

            // Right-aligned inside its own field rather than against the far edge, so the numbers
            // still line up under each other however wide they are without the whole block being
            // stranded away from the names it belongs to.
            var size = save.SizeText;

            Pad(line, savedColumn + SavedWidth + SizeWidth - size.Length);
            line.Append(size, TextRole.System);

            return line;
        }

        /// <summary>The longest name among the saves currently on screen.</summary>
        /// <remarks>
        /// The visible window comes from the viewport because a data source is asked for one row at
        /// a time and is never told which others are showing. <see cref="ListView"/> scrolls by
        /// moving the viewport over the rows, so its top is the index of the first save on screen.
        /// </remarks>
        private int LongestVisibleName()
        {
            var first = Math.Clamp(Viewport.Y, 0, Math.Max(0, Saves.Count - 1));
            var last = Math.Min(Saves.Count, first + Math.Max(0, Viewport.Height));

            var longest = 0;
            for (var index = first; index < last; index++)
            {
                longest = Math.Max(longest, Saves[index].Name.Length);
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
            width <= 0 ? string.Empty
            : text.Length <= width ? text
            : text[..width];
    }
}
