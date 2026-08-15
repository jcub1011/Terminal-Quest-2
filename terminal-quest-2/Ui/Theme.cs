using Terminal.Gui.Drawing;

using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The single source of truth for what the game looks like. Every colour decision lives here.
    /// <para>
    /// The theme deliberately specifies only a foreground and a text style. The background is
    /// left to the host terminal, so the game sits on whatever background the user already has
    /// rather than painting its own - a painted background fights the terminal's own theme and
    /// makes dimmed text hard to read.
    /// </para>
    /// </summary>
    internal static class Theme
    {
        /// <summary>A foreground colour paired with a text style, with no background of its own.</summary>
        internal readonly record struct Ink(Color Foreground, TextStyle Style);

        private static readonly Ink NormalInk = new(new Color("#d7d2c4"), TextStyle.None);
        private static readonly Ink ItemInk = new(new Color("#e0b050"), TextStyle.Bold);
        private static readonly Ink DangerInk = new(new Color("#d05a4a"), TextStyle.Bold);
        private static readonly Ink SpeechInk = new(new Color("#7fc3c8"), TextStyle.Italic);
        private static readonly Ink PlaceInk = new(new Color("#8fb26a"), TextStyle.Bold);
        private static readonly Ink SystemInk = new(new Color("#8a8375"), TextStyle.None);
        private static readonly Ink CommandInk = new(new Color("#f0e6d2"), TextStyle.Bold);

        /// <summary>
        /// The dice. A violet of its own rather than a borrowed ink: a roll is a third voice in the
        /// transcript, neither narration nor the game's furniture. Grey would bury the one number
        /// the player is looking for among the /help text, and gold already means money and items.
        /// </summary>
        private static readonly Ink RollInk = new(new Color("#9a8fd0"), TextStyle.Bold);

        public static Ink For(TextRole role) => role switch
        {
            TextRole.Item => ItemInk,
            TextRole.Danger => DangerInk,
            TextRole.Speech => SpeechInk,
            TextRole.Place => PlaceInk,
            TextRole.System => SystemInk,
            TextRole.Command => CommandInk,
            TextRole.Roll => RollInk,
            _ => NormalInk,
        };

        /// <summary>Builds an attribute for an ink over the terminal's own background.</summary>
        public static Attribute Attr(TextRole role)
        {
            var ink = For(role);
            return new Attribute(ink.Foreground, Color.None, ink.Style);
        }

        /// <summary>
        /// The attribute used to highlight the currently selected choice/option in the transcript.
        /// </summary>
        public static readonly Attribute OptionSelection = new(Color.Black, Color.White);

        /// <summary>
        /// The scheme applied to the window and every stock control inside it.
        /// <para>
        /// Every role is pinned explicitly with a <see cref="Color.None"/> background. That matters
        /// most for <see cref="Scheme.Focus"/>: left underived, Terminal.Gui builds it by swapping
        /// foreground and background, which turns the focused command box into a solid block of
        /// colour. Pinning it keeps the input looking like the rest of the screen.
        /// </para>
        /// </summary>
        public static Scheme CreateScheme()
        {
            var normal = Attr(TextRole.Normal);

            return new Scheme(normal)
            {
                Normal = normal,
                HotNormal = Attr(TextRole.Item),
                Focus = Attr(TextRole.Command),
                HotFocus = Attr(TextRole.Item),
                Active = Attr(TextRole.Command),
                HotActive = Attr(TextRole.Item),
                Highlight = Attr(TextRole.Item),
                Disabled = Attr(TextRole.System),
                Editable = Attr(TextRole.Command),
                ReadOnly = Attr(TextRole.System),
            };
        }
    }
}
