using Terminal.Gui.Views;

using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The archetype picker on the character screen.
    /// <para>
    /// A <see cref="ListView"/> with a <see cref="StyledListSource{T}"/>, so that everything except
    /// the shape of a row - the scroll window, the highlight, the keys that move it - belongs to the
    /// library. All that is left here is which columns an archetype gets.
    /// </para>
    /// </summary>
    internal sealed class ClassListView : ListView
    {
        /// <summary>Two columns for the cursor.</summary>
        private const int NameColumn = 2;

        /// <summary>What sits between the longest name and the column the details start in.</summary>
        private const int Gap = 2;

        private readonly StyledListSource<ClassTemplate> _source;

        public ClassListView()
        {
            // The character screen keeps focus in the name field and drives this from its own key
            // handler, so the list must not be in the focus chain: a focusable ListView would take
            // Tab, and would answer Up and Down itself before the window was offered them.
            CanFocus = false;

            _source = new StyledListSource<ClassTemplate>(Row);
            Source = _source;
            Classes = ClassTemplates.All;
        }

        /// <summary>The archetypes to offer, in the order they are listed.</summary>
        public IReadOnlyList<ClassTemplate> Classes
        {
            get => _source.Items;

            set
            {
                _source.Items = value;

                // The list is replaced wholesale, so the highlight has to be brought back into range
                // of what is now there.
                this.Highlight(_source.Count, Index);
                SetNeedsDraw();
            }
        }

        /// <summary>
        /// The highlight as a plain index. <see cref="ListView.SelectedItem"/> is null for an empty
        /// list, and every use here wants a number in range of what is currently listed.
        /// </summary>
        private int Index => Math.Clamp(SelectedItem ?? 0, 0, Math.Max(0, Classes.Count - 1));

        /// <summary>The highlighted archetype, or null when there are none to offer.</summary>
        public ClassTemplate? Selected => Classes.Count == 0 ? null : Classes[Index];

        /// <summary>Moves the highlight, clamping at both ends rather than wrapping.</summary>
        public void MoveSelection(int delta)
        {
            if (Classes.Count == 0)
            {
                return;
            }

            SelectedItem = Math.Clamp(Index + delta, 0, Classes.Count - 1);
            EnsureSelectedItemVisible();
        }

        /// <summary>
        /// One archetype's row: a cursor, the name, and the detail in a column beside it.
        /// </summary>
        /// <remarks>
        /// The column is measured off this row alone rather than off the longest name on screen, as
        /// the hand-drawn version did. A data source is asked for one row at a time and is not told
        /// which others are visible, so a shared column would need a measuring pass the list no
        /// longer exposes. What it costs is a ragged detail column; what it buys is the whole scroll
        /// window, which used to be this class's to get right.
        /// </remarks>
        private StyledLine Row(ClassTemplate template, int width, bool isSelected)
        {
            var line = new StyledLine();

            line.Append(isSelected ? "> " : "  ", TextRole.System);

            var detail = Detail(template);
            var column = NameColumn + template.Name.Length + Gap;

            // A detail with nowhere to go that leaves the name room is dropped rather than crushed
            // against it: the archetype's name is what is being chosen.
            // Trimmed to the room left after the cursor. Where there is a detail the name already
            // fits by construction, since the column it sits in was measured from the whole name.
            var hasDetail = column + detail.Length <= width;

            line.Append(
                Fit(template.Name, Math.Max(0, width - NameColumn)),
                isSelected ? TextRole.Command : TextRole.Normal);

            if (!hasDetail)
            {
                return line;
            }

            line.Append(new string(' ', Math.Max(0, column - line.Length)), TextRole.Normal);
            line.Append(Fit(detail, width - column), TextRole.System);

            return line;
        }

        /// <summary>
        /// What is shown beside the name. The two highest scores rather than all six: the picker is
        /// a choice about how you want to play, and "STR 16 CON 15" says that in the space the row
        /// has, where a full spread would not fit and a full spread nobody read would be worse.
        /// </summary>
        private static string Detail(ClassTemplate template)
        {
            var best = template.Attributes
                .OrderByDescending(attribute => attribute.Score)
                .Take(2)
                .Select(attribute => $"{attribute.Name[..3].ToUpperInvariant()} {attribute.Score}");

            return $"{template.Summary}   HP {template.MaxHealth}   {string.Join("  ", best)}";
        }

        private static string Fit(string text, int width) =>
            text.Length <= width ? text : text[..Math.Max(0, width)];
    }
}
