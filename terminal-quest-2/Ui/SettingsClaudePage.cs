using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Which Claude model narrates.
    /// <para>
    /// A short list of names rather than a box to type an id into. The id still goes to
    /// <c>--model</c> unchanged, but a player choosing who tells their story should be choosing
    /// between "fastest" and "most capable", not spelling a version number correctly.
    /// </para>
    /// </summary>
    internal sealed class SettingsClaudePage : SettingsPage
    {
        public SettingsClaudePage(AppSettings draft)
            : base(draft)
        {
        }

        public override string Title => "Claude Code";

        public override string Hint =>
            "Enter picks a model.  Left goes back.  Ctrl+Enter saves.";

        // Every row here is a choice, the custom one included - picking it is a no-op, but it is
        // still not something to open.
        public override bool CanSelect(int index) => index >= 0 && index < Rows.Count;

        public override IReadOnlyList<MenuRow> Rows
        {
            get
            {
                var known = ClaudeModels.All;
                var stored = Draft.ClaudeModel?.Trim() ?? string.Empty;
                var match = ClaudeModels.IndexOf(stored);

                // A settings file written by an older build, or edited by hand, can name a model
                // this list has never heard of. Showing it as an extra row is the honest answer:
                // the player sees what is actually in force and can leave it be, where silently
                // rewriting it to something nearby would change their game without asking.
                var rows = new MenuRow[known.Length + (match < 0 ? 1 : 0)];

                for (var index = 0; index < known.Length; index++)
                {
                    var entry = known[index];
                    rows[index] = new MenuRow(entry.Name, entry.Detail, index == match);
                }

                if (match < 0)
                {
                    rows[^1] = new MenuRow("(custom)", stored, true);
                }

                return rows;
            }
        }

        public override bool Select(int index)
        {
            // The custom row, when there is one, sits past the known models and is already in
            // force - picking it is a no-op rather than a way to lose the id.
            if (index < 0 || index >= ClaudeModels.All.Length)
            {
                return false;
            }

            var chosen = ClaudeModels.All[index].Id;

            if (string.Equals(chosen, Draft.ClaudeModel, StringComparison.Ordinal))
            {
                return false;
            }

            Draft.ClaudeModel = chosen;
            return true;
        }
    }
}
