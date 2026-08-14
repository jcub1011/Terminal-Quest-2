namespace TerminalQuest.Agents
{
    /// <summary>
    /// The outcome of one user message.
    /// <para>
    /// Only <see cref="Text"/> and <see cref="IsError"/> are guaranteed. The rest is telemetry, and
    /// what a provider can report varies: a local model has no price and no prompt cache, so those
    /// members read zero rather than being made nullable and checked at every use.
    /// </para>
    /// </summary>
    internal readonly record struct AgentTurnResult
    {
        /// <summary>
        /// The complete response text. When <see cref="IsError"/> is set this carries the
        /// failure message instead, for example <c>"Not logged in · Please run /login"</c>.
        /// </summary>
        public required string Text { get; init; }

        /// <summary>True when the provider reported the turn itself as a failure.</summary>
        public required bool IsError { get; init; }

        /// <summary>Cost of the turn in USD. Zero for a locally served model.</summary>
        public double CostUsd { get; init; }

        /// <summary>Uncached input tokens billed for this turn.</summary>
        public int InputTokens { get; init; }

        /// <summary>Output tokens generated for this turn.</summary>
        public int OutputTokens { get; init; }

        /// <summary>
        /// Tokens served from the prompt cache. Under Claude Code this is zero on the first turn
        /// and expected to be large from the second onward - the payoff of holding one process
        /// open. Providers without a prompt cache report zero throughout.
        /// </summary>
        public int CacheReadTokens { get; init; }

        /// <summary>Tokens written into the prompt cache during this turn, where there is one.</summary>
        public int CacheCreationTokens { get; init; }

        /// <summary>
        /// How much the model is holding after this turn - the prompt it was last sent plus what it
        /// answered with.
        /// <para>
        /// Deliberately not derivable from the counts above. Those are billing figures for the whole
        /// turn, and a turn is as many requests as the tool loop takes: the same conversation is sent
        /// again with every round trip, so summing them counts one context several times over. This
        /// is the size of the last request alone, which is what is actually occupied.
        /// </para>
        /// </summary>
        public int ContextTokens { get; init; }

        /// <summary>
        /// The window <see cref="ContextTokens"/> is filling, or zero where it cannot be established.
        /// A local server that will not say which context length it loaded reports zero rather than a
        /// guess, and a reader with no number has nothing to be wrong about.
        /// </summary>
        public int ContextWindowTokens { get; init; }

        /// <summary>Wall-clock duration of the turn, in milliseconds.</summary>
        public int DurationMs { get; init; }
    }
}
