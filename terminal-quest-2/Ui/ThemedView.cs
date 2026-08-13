using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Base for views that draw themselves with <see cref="Theme"/> colours.
    /// <para>
    /// Nothing here paints a background. Every attribute uses a
    /// <see cref="Terminal.Gui.Drawing.Color.None"/> background, which Terminal.Gui renders as the
    /// terminal's own default - so the game sits on whatever the user's terminal already shows,
    /// including transparency or acrylic effects.
    /// </para>
    /// </summary>
    internal abstract class ThemedView : View
    {
        protected ThemedView()
        {
            CanFocus = false;
        }

        /// <summary>
        /// Clears the viewport. Call once at the top of <c>OnDrawingContent</c>.
        /// <para>
        /// The clear is explicit rather than left to the base class so that shrinking content -
        /// a shorter status value, a scrolled-away row - cannot leave stale glyphs behind. It
        /// writes spaces in the terminal's default colours, so it erases without tinting.
        /// </para>
        /// </summary>
        protected void BeginPaint(int width, int height)
        {
            SetRole(TextRole.Normal);

            var blank = new string(' ', width);
            for (var y = 0; y < height; y++)
            {
                Move(0, y);
                AddStr(blank);
            }
        }

        /// <summary>Selects the attribute for a semantic role over the terminal's background.</summary>
        protected void SetRole(TextRole role) => SetAttribute(Theme.Attr(role));
    }
}
