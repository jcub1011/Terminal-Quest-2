using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The one-row title bar: what is being played on the left, where the player is standing on
    /// the right.
    /// <para>
    /// Drawn here rather than left to the window's border title because Terminal.Gui paints a
    /// border title with a single attribute, and the place name has to stay green while the rest
    /// of the row does not. The location belongs at the top instead of in the status pane: it is
    /// the one fact the player checks constantly, and prose-length place names never fitted the
    /// pane's width.
    /// </para>
    /// </summary>
    internal sealed class TitleBarView : ThemedView
    {
        /// <summary>Blank columns kept between the two halves so they never read as one phrase.</summary>
        private const int Gap = 2;

        private readonly GameState _state;

        public TitleBarView(GameState state)
        {
            _state = state;
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;

            if (width <= 0 || Viewport.Height <= 0)
            {
                return true;
            }

            BeginPaint(width, Viewport.Height);

            // The place name wins the room when the row is too narrow for both: the save name is
            // the same on every turn and the location is not.
            var place = _state.Location.Length > 0 ? _state.Location : "nowhere";
            if (place.Length > width)
            {
                place = place[..width];
            }

            var name = _state.SaveName.Length > 0
                ? $"Terminal Quest - {_state.SaveName}"
                : "Terminal Quest";

            var room = width - place.Length - Gap;
            if (room > 0)
            {
                Move(0, 0);
                SetRole(TextRole.System);
                AddStr(name.Length <= room ? name : name[..room]);
            }

            Move(width - place.Length, 0);
            SetRole(TextRole.Place);
            AddStr(place);

            return true;
        }
    }
}
