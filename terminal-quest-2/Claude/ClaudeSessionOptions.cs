namespace TerminalQuest.Claude
{
    /// <summary>
    /// Configuration for a <see cref="ClaudeSession"/>.
    /// </summary>
    public sealed record ClaudeSessionOptions
    {
        /// <summary>
        /// Model to run, for example <c>claude-haiku-4-5-20251001</c> or an alias such as
        /// <c>sonnet</c>. Deliberately has no default: set it explicitly, or set it to
        /// <see langword="null"/> to inherit whatever the CLI is configured to use.
        /// </summary>
        public required string? Model { get; init; }

        /// <summary>
        /// Replaces Claude Code's default system prompt outright. Keep it short — with no tools
        /// and no MCP servers this is essentially the entire prompt prefix.
        /// </summary>
        public string SystemPrompt { get; init; } = "You are a helpful assistant. Answer concisely.";

        /// <summary>Executable to launch. Resolved against PATH when not an absolute path.</summary>
        public string ExecutablePath { get; init; } = "claude";

        /// <summary>Working directory for the process. Defaults to the current directory.</summary>
        public string? WorkingDirectory { get; init; }

        /// <summary>
        /// When false (the default), passes <c>--no-session-persistence</c> so the conversation is
        /// never written to disk. Set true if you want the transcript to survive for <c>--resume</c>.
        /// </summary>
        public bool PersistSession { get; init; }

        /// <summary>
        /// How long <see cref="ClaudeSession.StartAsync"/> watches a freshly launched process for an
        /// immediate failure, such as a rejected flag. Claude Code produces no output at all until
        /// the first message is sent, so an early exit is the only startup signal available.
        /// </summary>
        public TimeSpan StartupGracePeriod { get; init; } = TimeSpan.FromSeconds(2);

        /// <summary>How long a single turn may run before it is interrupted and faulted.</summary>
        public TimeSpan TurnTimeout { get; init; } = TimeSpan.FromMinutes(5);
    }
}
