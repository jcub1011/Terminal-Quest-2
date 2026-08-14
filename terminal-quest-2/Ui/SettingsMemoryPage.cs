using System.Globalization;

using TerminalQuest.Saves;
using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// How much of the last session a resumed save remembers word for word.
    /// <para>
    /// A box to type a number into rather than a list of sizes, for <see cref="SettingsEditorPage"/>'s
    /// reason turned around: there is no short list of right answers here either, but where that page
    /// cannot know what is installed, this one cannot know how long the player's scenes run.
    /// </para>
    /// </summary>
    internal sealed class SettingsMemoryPage : SettingsPage
    {
        /// <summary>The only row, named rather than numbered for the same reason as the others.</summary>
        public const int RecallRow = 0;

        /// <summary>Past the label, so the value lines up as a column like the other pages'.</summary>
        private const int ValuesAt = 18;

        public SettingsMemoryPage(AppSettings draft)
            : base(draft)
        {
        }

        public override string Title => "Memory";

        public override string Hint =>
            "Enter edits the size.  Left goes back.  Ctrl+Enter saves.";

        public override int ValueColumn => ValuesAt;

        public override IReadOnlyList<MenuRow> Rows =>
        [
            new("Recalled prose", $"{Draft.TranscriptRecallCharacters} characters", false),
        ];

        public override bool TryBeginEdit(int index, out string text)
        {
            text = index == RecallRow
                ? Draft.TranscriptRecallCharacters.ToString(CultureInfo.InvariantCulture)
                : string.Empty;

            return index == RecallRow;
        }

        /// <summary>
        /// Takes a size, or says why it will not.
        /// </summary>
        /// <remarks>
        /// Refused rather than clamped quietly. <see cref="TranscriptRecall.Clamp"/> exists for values
        /// arriving from a hand-edited file or from the model, where there is nobody to tell; here
        /// there is somebody looking at the screen, and silently storing a different number than they
        /// typed is how a setting comes to be mistrusted.
        /// </remarks>
        public override string? Commit(int index, string text)
        {
            if (index != RecallRow)
            {
                return null;
            }

            var typed = text?.Trim() ?? string.Empty;

            if (!int.TryParse(typed, NumberStyles.None, CultureInfo.InvariantCulture, out var characters))
            {
                return "That needs to be a whole number of characters.";
            }

            if (characters < TranscriptRecall.MinCharacters || characters > TranscriptRecall.MaxCharacters)
            {
                return $"That needs to be between {TranscriptRecall.MinCharacters} "
                     + $"and {TranscriptRecall.MaxCharacters} characters.";
            }

            Draft.TranscriptRecallCharacters = characters;
            return null;
        }
    }
}
