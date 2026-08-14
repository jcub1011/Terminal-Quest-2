using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Which program Ctrl+G hands a text field to.
    /// <para>
    /// A box to type a command into rather than a list to pick from, unlike the model pages: what is
    /// installed on this machine is not something the game can know, and the answer is as likely to
    /// be a path with a flag on the end as it is a name.
    /// </para>
    /// </summary>
    internal sealed class SettingsEditorPage : SettingsPage
    {
        /// <summary>The only row, named rather than numbered for the same reason as the others.</summary>
        public const int CommandRow = 0;

        /// <summary>Past the label, so the value lines up as a column like the LM Studio page's.</summary>
        private const int ValuesAt = 14;

        public SettingsEditorPage(AppSettings draft)
            : base(draft)
        {
        }

        public override string Title => "Editor";

        public override string Hint =>
            "Enter edits the command.  Left goes back.  Ctrl+Enter saves.";

        public override int ValueColumn => ValuesAt;

        public override IReadOnlyList<MenuRow> Rows =>
        [
            new("Command", Draft.EditorCommand, false),
        ];

        public override bool TryBeginEdit(int index, out string text)
        {
            text = index == CommandRow ? Draft.EditorCommand : string.Empty;
            return index == CommandRow;
        }

        public override string? Commit(int index, string text)
        {
            if (index != CommandRow)
            {
                return null;
            }

            var typed = text?.Trim() ?? string.Empty;

            // Refused rather than quietly restored to the default: an empty box is as likely to be a
            // half-finished edit as a request to go back to Notepad, and the player can type the
            // default in if that is what they meant.
            if (typed.Length == 0)
            {
                return $"That needs to name a program, such as {AppSettings.DefaultEditorCommand}";
            }

            Draft.EditorCommand = typed;
            return null;
        }
    }
}
