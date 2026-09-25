namespace TerminalQuest.Agents
{
    /// <summary>
    /// Where a turn in flight has got to: how full the context is and what the turn has cost so far.
    /// </summary>
    /// <remarks>
    /// Raised once per request the turn makes rather than per token, because that is how often either
    /// provider reports usage - once per request. A turn that uses tools makes several, so the pane
    /// moves in steps as the narrator reads and writes the world, instead of jumping once at the end.
    /// Superseded by <see cref="AgentTurnResult"/> once the turn is over.
    /// </remarks>
    internal readonly record struct AgentProgress
    {
        /// <summary>Tokens the model is holding as of the latest request. See <see cref="AgentTurnResult.ContextTokens"/>.</summary>
        public int ContextTokens { get; init; }

        /// <summary>The window being filled, or zero where it is not known.</summary>
        public int ContextWindowTokens { get; init; }

        /// <summary>
        /// What the turn has cost so far, in USD. Null where the provider only states a price once the
        /// turn is over, as Claude Code does - which is not the same as the turn being free.
        /// </summary>
        public double? CostUsd { get; init; }

        /// <summary>True once a request in this turn could not be priced.</summary>
        public bool CostIncomplete { get; init; }
    }
}
