using TerminalQuest.Agents.Claude;
using TerminalQuest.Agents.LmStudio;
using TerminalQuest.Agents.Pricing;
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
        /// pointed at its folder; an OpenAI-compatible provider is handed the store itself and
        /// calls the tools here.
        /// </param>
        /// <summary>Creates the Narrator session with Narrator-scoped tools.</summary>
        public static IAgentSession CreateNarrator(AppSettings settings, SaveStore store, string systemPrompt)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(store);

            return Create(settings, store, systemPrompt, ToolRole.Narrator, settings.ClaudeModel, settings.ActiveModel);
        }

        /// <summary>Creates the Director session with Director-scoped tools.</summary>
        public static IAgentSession CreateDirector(AppSettings settings, SaveStore store, string directorPrompt)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(store);

            var claudeModel = Or(settings.DirectorClaudeModel, settings.ClaudeModel);
            var endpointModel = Or(settings.ActiveDirectorModel, settings.ActiveModel);
            return Create(settings, store, directorPrompt, ToolRole.Director, claudeModel, endpointModel);
        }

        public static IAgentSession Create(AppSettings settings, SaveStore store, string systemPrompt) =>
            CreateNarrator(settings, store, systemPrompt);

        private static IAgentSession Create(
            AppSettings settings,
            SaveStore store,
            string systemPrompt,
            ToolRole role,
            string? claudeModel,
            string? lmStudioModel)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(store);

            // Unknown values fall through to Claude Code: a session that narrates with the
            // default is better than one that cannot be constructed at all.
            if (!AppSettings.IsOpenAiProvider(settings.Provider))
            {
                return new ClaudeSession(new ClaudeSessionOptions
                {
                    Model = Trimmed(claudeModel),
                    SystemPrompt = systemPrompt,
                    McpConfigJson = QuestServerConfig.Build(store.Directory),
                    AllowedTools = QuestTools.AllowedTools(role),
                });
            }

            var baseUrl = settings.ActiveBaseUrl;

            return new LmStudioSession(
                new LmStudioSessionOptions
                {
                    BaseUrl = baseUrl,
                    Model = Trimmed(lmStudioModel),
                    SystemPrompt = systemPrompt,
                    ApiKey = settings.ActiveApiKey,
                    Role = role,

                    // Handed over as a method rather than a catalog: construction must not reach the
                    // network, and the shared catalog starts downloading the first time it is asked.
                    Catalog = ModelCatalog.GetSharedAsync,
                    CatalogProvider = ModelCatalog.ProviderFor(settings.Provider, baseUrl),
                },
                store);
        }

        /// <summary>A blank field means "you decide", which for both providers is null.</summary>
        private static string? Trimmed(string? value) =>
            value?.Trim() is { Length: > 0 } trimmed ? trimmed : null;

        private static string Or(string? value, string fallback) => Trimmed(value) ?? fallback;
    }
}
