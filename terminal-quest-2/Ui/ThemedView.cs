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
        /// <summary>
        /// The blank row the last clear used, kept so a redraw at an unchanged width does not
        /// allocate a fresh one. Every view here clears at the full viewport width, and the width
        /// only changes when the terminal is resized, so in practice one string serves them all.
        /// </summary>
        private static string _blank = string.Empty;

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

            var blank = Blank(width);
            for (var y = 0; y < height; y++)
            {
                Move(0, y);
                AddStr(blank);
            }
        }

        /// <summary>
        /// The first row a selection list should draw, so that the highlighted row stays on screen
        /// and sits mid-pane once the list is taller than the pane.
        /// <para>
        /// Derived from the selection on every draw rather than carried as a scroll offset - which
        /// is also why these lists do not use the base class's own content scrolling. There is no
        /// state to fall out of step with the cursor, and nothing to keep in range when the list is
        /// replaced underneath it.
        /// </para>
        /// </summary>
        internal static int ScrollWindowStart(int selectedIndex, int count, int height) =>
            Math.Max(0, Math.Min(selectedIndex - (height / 2), count - height));

        /// <summary>A row of spaces <paramref name="width"/> wide, reusing the last one when it fits.</summary>
        private static string Blank(int width)
        {
            // Read once: drawing is on the UI thread, but the field is shared by every view and
            // there is no reason to read it twice.
            var cached = _blank;
            if (cached.Length == width)
            {
                return cached;
            }

            var blank = new string(' ', width);
            _blank = blank;
            return blank;
        }

        /// <summary>Selects the attribute for a semantic role over the terminal's background.</summary>
        protected void SetRole(TextRole role) => SetAttribute(Theme.Attr(role));
    }
}
