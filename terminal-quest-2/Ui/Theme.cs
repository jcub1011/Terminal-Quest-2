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
        private static readonly Ink ItemInk = new(new Color("#ffb62e"), TextStyle.Bold);
        private static readonly Ink DangerInk = new(new Color("#ff5147"), TextStyle.Bold);
        private static readonly Ink SpeechInk = new(new Color("#4fe3e8"), TextStyle.Bold | TextStyle.Italic);
        private static readonly Ink PlaceInk = new(new Color("#7ddf64"), TextStyle.Bold);
        private static readonly Ink CharacterInk = new(new Color("#ff9e64"), TextStyle.Bold);
        private static readonly Ink SystemInk = new(new Color("#8a8375"), TextStyle.None);
        private static readonly Ink CommandInk = new(new Color("#f0e6d2"), TextStyle.Bold);
        private static readonly Ink HintInk = new(new Color("#6f6a5e"), TextStyle.None);
        private static readonly Ink ButtonInk = new(new Color("#4fc3ff"), TextStyle.Bold);
        private static readonly Ink InputInk = new(new Color("#f5efdf"), TextStyle.Bold);
        private static readonly Ink ImportantInk = new(new Color("#ffa02e"), TextStyle.Bold);

        /// <summary>
        /// The dice. A violet of its own rather than a borrowed ink: a roll is a third voice in the
        /// transcript, neither narration nor the game's furniture. Grey would bury the one number
        /// the player is looking for among the /help text, and gold already means money and items.
        /// </summary>
        private static readonly Ink RollInk = new(new Color("#b39dff"), TextStyle.Bold);

        public static Ink For(TextRole role) => role switch
        {
            TextRole.Item => ItemInk,
            TextRole.Danger => DangerInk,
            TextRole.Speech => SpeechInk,
            TextRole.Place => PlaceInk,
            TextRole.Character => CharacterInk,
            TextRole.System => SystemInk,
            TextRole.Command => CommandInk,
            TextRole.Roll => RollInk,
            TextRole.Hint => HintInk,
            TextRole.Button => ButtonInk,
            TextRole.Input => InputInk,
            TextRole.Important => ImportantInk,
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
        /// A scheme for a label drawn entirely in one role: a field name, help text, or a
        /// status line. Only the roles a label ever uses are pinned; the background stays
        /// the terminal's own, like <see cref="CreateScheme"/>.
        /// </summary>
        public static Scheme LabelScheme(TextRole role)
        {
            var ink = Attr(role);

            return new Scheme
            {
                Normal = ink,
                Focus = ink,
                HotNormal = ink,
                HotFocus = ink,
            };
        }

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
                HotNormal = Attr(TextRole.Button),
                Focus = Attr(TextRole.Input),
                HotFocus = Attr(TextRole.Button),
                Active = Attr(TextRole.Input),
                HotActive = Attr(TextRole.Button),
                Highlight = Attr(TextRole.Button),
                Disabled = Attr(TextRole.Hint),
                Editable = Attr(TextRole.Input),
                ReadOnly = Attr(TextRole.Hint),
            };
        }
    }
}
