using TerminalQuest.Agents.Claude;
using TerminalQuest.Agents.LmStudio;
using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Settings;

namespace TerminalQuest.Agents
{
    /// <summary>
    /// Turns the player's settings into the session that will narrate for them.
    /// </summary>
    /// <remarks>
    /// The one place in the game that knows both providers exist. Everything upstream of this
    /// picks a value out of a list; everything downstream has an <see cref="IAgentSession"/> and no
    /// reason to ask which kind it is.
    /// </remarks>
    internal static class AgentSessionFactory
    {
        /// <param name="store">
        /// The save being played. Claude Code reaches it through the MCP server, which is launched
        /// pointed at its folder; LM Studio is handed the store itself and calls the tools here.
        /// </param>
        public static IAgentSession Create(AppSettings settings, SaveStore store, string systemPrompt)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(store);

            return settings.Provider switch
            {
                AgentProvider.LmStudio => new LmStudioSession(
                    new LmStudioSessionOptions
                    {
                        BaseUrl = Or(settings.LmStudioBaseUrl, AppSettings.DefaultLmStudioBaseUrl),
                        Model = Trimmed(settings.LmStudioModel),
                        SystemPrompt = systemPrompt,
                        ApiKey = settings.LmStudioApiKey?.Trim() ?? string.Empty,
                    },
                    store),

                _ => new ClaudeSession(new ClaudeSessionOptions
                {
                    Model = Trimmed(settings.ClaudeModel),
                    SystemPrompt = systemPrompt,
                    McpConfigJson = QuestServerConfig.Build(store.Directory),
                    AllowedTools = QuestTools.AllowedTools(),
                }),
            };
        }

        /// <summary>A blank field means "you decide", which for both providers is null.</summary>
        private static string? Trimmed(string? value) =>
            value?.Trim() is { Length: > 0 } trimmed ? trimmed : null;

        private static string Or(string? value, string fallback) => Trimmed(value) ?? fallback;
    }
}
