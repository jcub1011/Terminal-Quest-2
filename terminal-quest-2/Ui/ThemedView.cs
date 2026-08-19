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

        /// <summary>A row of spaces <paramref name="width"/> wide, reusing the last one when it fits.</summary>
        internal static string Blank(int width)
        {
            if (width <= 0)
            {
                return string.Empty;
            }

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
