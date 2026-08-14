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

            for (var row = 0; row < height && first + row < _classes.Count; row++)
            {
                var template = _classes[first + row];
                var isSelected = first + row == _selectedIndex;

                Move(0, row);
                SetRole(TextRole.System);
                AddStr(isSelected ? "> " : "  ");

                SetRole(isSelected ? TextRole.Command : TextRole.Normal);
                AddStr(Fit(template.Name, Math.Max(0, width - 2)));

                var detail = $"{template.Summary}   HP {template.MaxHealth}";

                var column = width - detail.Length;
                if (column > template.Name.Length + 3)
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
