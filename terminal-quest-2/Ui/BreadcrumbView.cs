using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The trail across the top of the settings screen, saying how deep the player has gone.
    /// <para>
    /// A view rather than a <see cref="Terminal.Gui.Views.Label"/> because the whole point is that
    /// the crumbs are not all equal: where you are now is bright and where you came from is dim,
    /// which a label - one scheme for all its text - cannot say.
    /// </para>
    /// </summary>
    internal sealed class BreadcrumbView : ThemedView
    {
        private const string Separator = " > ";

        private IReadOnlyList<string> _crumbs = [];

        /// <summary>The trail, outermost first.</summary>
        public IReadOnlyList<string> Crumbs
        {
            set
            {
                _crumbs = value ?? [];
                SetNeedsDraw();
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
            Move(0, 0);

            var column = 0;

            for (var index = 0; index < _crumbs.Count && column < width; index++)
            {
                if (index > 0)
                {
                    SetRole(TextRole.System);
                    column += Write(Separator, width - column);
                }

                SetRole(index == _crumbs.Count - 1 ? TextRole.Command : TextRole.System);
                column += Write(_crumbs[index], width - column);
            }

            return true;
        }

        /// <summary>Writes as much as fits and reports how much that was.</summary>
        private int Write(string text, int room)
        {
            if (room <= 0)
            {
                return 0;
            }

            var fitted = text.Length <= room ? text : text[..room];
            AddStr(fitted);
            return fitted.Length;
        }
    }
}
