using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The top of the settings screen: the categories there are.
    /// <para>
    /// One row today. It exists anyway so that the second category - whenever there is one - is a
    /// row added here rather than a screen rebuilt around it.
    /// </para>
    /// </summary>
    internal sealed class SettingsTabsPage : SettingsPage
    {
        public SettingsTabsPage(AppSettings draft)
            : base(draft)
        {
        }

        public override string Title => "Settings";

        public override string Hint =>
            "Enter or Right opens a tab.  Ctrl+Enter saves.  Esc leaves.";

        public override IReadOnlyList<SettingsRow> Rows =>
        [
            new("Model Selection", Summary(), false),
        ];

        public override SettingsPage? Enter(int index) =>
            index == 0 ? new SettingsAdaptersPage(Draft) : null;

        /// <summary>
        /// Who would narrate, so the choice made two levels down is legible from the top without
        /// having to go and look.
        /// </summary>
        private string Summary() => Draft.Provider switch
        {
            AgentProvider.LmStudio => Draft.LmStudioModel is { Length: > 0 } model
                ? $"LM Studio - {model}"
                : "LM Studio",
            _ => ClaudeModels.Describe(Draft.ClaudeModel) is { Length: > 0 } name
                ? $"Claude Code - {name}"
                : "Claude Code",
        };
    }
}
