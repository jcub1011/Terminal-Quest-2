using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Who narrates, and how to reach them.
    /// <para>
    /// The one page where picking a row and opening it are different acts, so they get different
    /// keys: Enter puts an adapter in force, Right goes in to configure it. A player can set up an
    /// adapter they are not using and switch to it later without the two ever being confused.
    /// </para>
    /// </summary>
    internal sealed class SettingsAdaptersPage : SettingsPage
    {
        private static readonly (AgentProvider Provider, string Name, string Detail)[] Adapters =
        [
            (AgentProvider.ClaudeCode, "Claude Code", "the claude CLI, run as a child process"),
            (AgentProvider.LmStudio, "LM Studio", "a model on this machine, over HTTP"),
        ];

        public SettingsAdaptersPage(AppSettings draft)
            : base(draft)
        {
        }

        public override string Title => "Model Selection";

        public override string Hint =>
            "Enter picks the adapter.  Right opens its settings.  Left goes back.  Ctrl+Enter saves.";

        public override IReadOnlyList<SettingsRow> Rows
        {
            get
            {
                var rows = new SettingsRow[Adapters.Length];

                for (var index = 0; index < Adapters.Length; index++)
                {
                    var (provider, name, detail) = Adapters[index];
                    rows[index] = new SettingsRow(name, detail, provider == Draft.Provider);
                }

                return rows;
            }
        }

        public override SettingsPage? Enter(int index)
        {
            if (index < 0 || index >= Adapters.Length)
            {
                return null;
            }

            return Adapters[index].Provider switch
            {
                AgentProvider.LmStudio => new SettingsLmStudioPage(Draft),
                _ => new SettingsClaudePage(Draft),
            };
        }

        public override bool CanSelect(int index) => index >= 0 && index < Adapters.Length;

        public override bool Select(int index)
        {
            if (index < 0 || index >= Adapters.Length)
            {
                return false;
            }

            var chosen = Adapters[index].Provider;

            if (chosen == Draft.Provider)
            {
                return false;
            }

            Draft.Provider = chosen;
            return true;
        }
    }
}
