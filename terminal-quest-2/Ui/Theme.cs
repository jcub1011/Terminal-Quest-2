using Spectre.Console;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The single source of truth for what the game looks like. Every colour decision lives here.
    /// </summary>
    internal static class Theme
    {
        /// <summary>A foreground colour paired with a decoration, with no background of its own.</summary>
        internal readonly record struct Ink(Color Foreground, Decoration Decoration, string MarkupTag);

        private static readonly Ink NormalInk = new(new Color(0xd7, 0xd2, 0xc4), Decoration.None, "#d7d2c4");
        private static readonly Ink ItemInk = new(new Color(0xe0, 0xb0, 0x50), Decoration.Bold, "bold #e0b050");
        private static readonly Ink DangerInk = new(new Color(0xd0, 0x5a, 0x4a), Decoration.Bold, "bold #d05a4a");
        private static readonly Ink SpeechInk = new(new Color(0x7f, 0xc3, 0xc8), Decoration.Italic, "italic #7fc3c8");
        private static readonly Ink PlaceInk = new(new Color(0x8f, 0xb2, 0x6a), Decoration.Bold, "bold #8fb26a");
        private static readonly Ink CharacterInk = new(new Color(0xe6, 0x98, 0x75), Decoration.Bold, "bold #e69875");
        private static readonly Ink SystemInk = new(new Color(0x8a, 0x83, 0x75), Decoration.None, "#8a8375");
        private static readonly Ink CommandInk = new(new Color(0xf0, 0xe6, 0xd2), Decoration.Bold, "bold #f0e6d2");
        private static readonly Ink RollInk = new(new Color(0x9a, 0x8f, 0xd0), Decoration.Bold, "bold #9a8fd0");

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
            _ => NormalInk,
        };

        public static Style StyleFor(TextRole role)
        {
            var ink = For(role);
            return new Style(foreground: ink.Foreground, decoration: ink.Decoration);
        }

        public static string Format(string text, TextRole role)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var escaped = Markup.Escape(text);
            var ink = For(role);
            return $"[{ink.MarkupTag}]{escaped}[/]";
        }
    }
}
