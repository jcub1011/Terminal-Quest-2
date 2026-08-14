namespace TerminalQuest.Settings
{
    /// <summary>
    /// Everything the player can change about how the game reaches a model.
    /// </summary>
    /// <remarks>
    /// Every provider's fields are kept, not just the selected one's, so switching back and forth
    /// does not cost the player the model name and address they already typed. A plain class with
    /// settable properties for the same reason as the save documents: it is what the source
    /// generator serializes without reflection.
    /// </remarks>
    internal sealed class AppSettings
    {
        /// <summary>Which provider a new session is built against.</summary>
        public AgentProvider Provider { get; set; } = AgentProvider.ClaudeCode;

        /// <summary>
        /// The Claude model, by id or alias. Empty leaves the choice to whatever the CLI is
        /// configured to use.
        /// </summary>
        public string ClaudeModel { get; set; } = DefaultClaudeModel;

        /// <summary>Root of the OpenAI-compatible API, endpoint paths excluded.</summary>
        public string LmStudioBaseUrl { get; set; } = DefaultLmStudioBaseUrl;

        /// <summary>The model id, exactly as the server lists it. Empty means whatever is loaded.</summary>
        public string LmStudioModel { get; set; } = string.Empty;

        /// <summary>
        /// Bearer token. Only needed once LM Studio has authentication turned on, but needed
        /// exactly then - without it the server refuses every request.
        /// </summary>
        public string LmStudioApiKey { get; set; } = DefaultLmStudioApiKey;

        /// <summary>Small and fast, which is what a turn of narration wants.</summary>
        public const string DefaultClaudeModel = "claude-haiku-4-5-20251001";

        /// <summary>Where LM Studio's server listens unless it has been told otherwise.</summary>
        public const string DefaultLmStudioBaseUrl = "http://localhost:1234/v1";

        /// <summary>
        /// The placeholder LM Studio's own examples use, which is right for a server that has not
        /// been told to check. One that has needs the real token pasted in.
        /// </summary>
        public const string DefaultLmStudioApiKey = "lm-studio";
    }
}
