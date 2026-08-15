namespace TerminalQuest.Settings
{
    /// <summary>Who answers as the narrator.</summary>
    internal enum AgentProvider
    {
        /// <summary>The <c>claude</c> CLI, driven as a child process.</summary>
        ClaudeCode = 0,

        /// <summary>A model served over an OpenAI-compatible HTTP API (Google, OpenAI, Anthropic, LM Studio, etc.).</summary>
        OpenAiApi = 1,

        /// <summary>Legacy alias for <see cref="OpenAiApi"/>.</summary>
        LmStudio = OpenAiApi,
    }
}
