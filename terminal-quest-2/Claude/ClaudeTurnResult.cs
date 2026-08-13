namespace TerminalQuest.Claude
{
    /// <summary>
    /// The outcome of one user message, built from the <c>result</c> line Claude Code emits
    /// at the end of every turn.
    /// </summary>
    public readonly record struct ClaudeTurnResult
    {
        /// <summary>
        /// The complete response text. When <see cref="IsError"/> is set this carries the
        /// failure message instead, for example <c>"Not logged in · Please run /login"</c>.
        /// </summary>
        public required string Text { get; init; }

        /// <summary>True when Claude Code reported the turn itself as a failure.</summary>
        public required bool IsError { get; init; }

        /// <summary>Client-side cost estimate for the turn, in USD.</summary>
        public double CostUsd { get; init; }

        /// <summary>Uncached input tokens billed for this turn.</summary>
        public int InputTokens { get; init; }

        /// <summary>Output tokens generated for this turn.</summary>
        public int OutputTokens { get; init; }

        /// <summary>
        /// Tokens served from the prompt cache. Zero on the first turn and expected to be
        /// large from the second turn onward — this is the payoff of holding one process open.
        /// </summary>
        public int CacheReadTokens { get; init; }

        /// <summary>Tokens written into the prompt cache during this turn.</summary>
        public int CacheCreationTokens { get; init; }

        /// <summary>Wall-clock duration of the turn, in milliseconds.</summary>
        public int DurationMs { get; init; }
    }
}
