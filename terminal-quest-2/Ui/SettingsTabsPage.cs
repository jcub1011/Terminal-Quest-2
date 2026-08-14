using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The top of the settings screen: the categories there are.
    /// <para>
    /// Who narrates, what Ctrl+G opens, and how much a resumed save remembers. All three are rows
    /// added here rather than screens built around them, which is what this level exists for.
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
            "Enter or Right opens a tab.  Ctrl+Enter saves.  Left or Esc leaves.";

        public override IReadOnlyList<MenuRow> Rows =>
        [
            new("Model Selection", Summary(), HasSubmenu: true),
            new("Editor", Draft.EditorCommand, HasSubmenu: true),
            new("Memory", $"{Draft.TranscriptRecallCharacters} characters", HasSubmenu: true),
        ];

        public override SettingsPage? Enter(int index) => index switch
        {
            0 => new SettingsAdaptersPage(Draft),
            1 => new SettingsEditorPage(Draft),
            2 => new SettingsMemoryPage(Draft),
            _ => null,
        };

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
