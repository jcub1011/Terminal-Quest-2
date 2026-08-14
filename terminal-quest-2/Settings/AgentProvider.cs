namespace TerminalQuest.Settings
{
    /// <summary>Who answers as the narrator.</summary>
    internal enum AgentProvider
    {
        /// <summary>The <c>claude</c> CLI, driven as a child process.</summary>
        ClaudeCode,

        /// <summary>A model served locally over an OpenAI-compatible HTTP API.</summary>
        LmStudio,
    }
}
