using Terminal.Gui.ViewBase;

using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The archetype picker on the character screen.
    /// <para>
    /// A near-sibling of <see cref="SaveListView"/> rather than a shared generic control: the two
    /// draw different columns, and factoring them together would mean a formatter callback for the
    /// sake of two call sites.
    /// </para>
    /// </summary>
    internal sealed class ClassListView : ThemedView
    {
        /// <summary>Two columns for the cursor.</summary>
        private const int NameColumn = 2;

        /// <summary>What sits between the longest name and the column the details start in.</summary>
        private const int Gap = 2;

        private IReadOnlyList<ClassTemplate> _classes = ClassTemplates.All;
        private int _selectedIndex;

        /// <summary>The archetypes to offer, in the order they are listed.</summary>
        public IReadOnlyList<ClassTemplate> Classes
        {
            get => _classes;
            set
            {
                _classes = value;
                _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, value.Count - 1));
                SetNeedsDraw();
            }
        }

        /// <summary>The highlighted archetype, or null when there are none to offer.</summary>
        public ClassTemplate? Selected =>
            _selectedIndex >= 0 && _selectedIndex < _classes.Count ? _classes[_selectedIndex] : null;

        /// <summary>Moves the highlight, clamping at both ends rather than wrapping.</summary>
        public void MoveSelection(int delta)
        {
            if (_classes.Count == 0)
            {
                return;
            }

            _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, _classes.Count - 1);
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

            // Keep the highlight on screen when the list is longer than the pane.
            var first = ScrollWindowStart(_selectedIndex, _classes.Count, height);
            var last = Math.Min(height, _classes.Count - first);

            // One column for every detail on screen, measured off the longest name and the longest
            // detail, so they read as a column beside the names instead of ragged against the far
            // edge. Measured over this pageful only, like the save list's.
            var longestName = 0;
            var longestDetail = 0;

            for (var row = 0; row < last; row++)
            {
                longestName = Math.Max(longestName, _classes[first + row].Name.Length);
                longestDetail = Math.Max(longestDetail, Detail(_classes[first + row]).Length);
            }

            var column = Math.Min(NameColumn + longestName + Gap, width - longestDetail);

            for (var row = 0; row < last; row++)
            {
                var template = _classes[first + row];
                var isSelected = first + row == _selectedIndex;

                // A detail with nowhere to go that leaves the name room is dropped rather than
                // crushed against it: the archetype's name is what is being chosen.
                var hasDetail = column > NameColumn + template.Name.Length && column < width;
                var nameWidth = hasDetail ? column - NameColumn - 1 : width - NameColumn;

                Move(0, row);
                SetRole(TextRole.System);
                AddStr(isSelected ? "> " : "  ");

                SetRole(isSelected ? TextRole.Command : TextRole.Normal);
                AddStr(Fit(template.Name, Math.Max(0, nameWidth)));

                if (!hasDetail)
                {
                    continue;
                }

                Move(column, row);
                SetRole(TextRole.System);
                AddStr(Fit(Detail(template), width - column));
            }

            return true;
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
