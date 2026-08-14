namespace TerminalQuest.Agents
{
    /// <summary>
    /// One narrator conversation, whoever is answering it.
    /// <para>
    /// The two implementations sit at different layers, and this interface is deliberately drawn
    /// at the higher of the two. Claude Code is an agent: it owns the transcript and runs the tool
    /// loop itself, in a child process. An OpenAI-compatible endpoint is a completion call: it is
    /// stateless, it executes nothing, and it hands tool calls back for the client to run. What
    /// they have in common is only what is here - send a prompt, watch text arrive, get a result -
    /// so everything either one does to keep that promise stays behind its own implementation.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Turns are serialized by every implementation: a second <see cref="SendAsync"/> waits for the
    /// first to finish rather than interleaving with it.
    /// </remarks>
    internal interface IAgentSession : IAsyncDisposable
    {
        /// <summary>
        /// Raised for each chunk of response text as it is generated. Reasoning output is filtered
        /// out, so only text the player should see is forwarded. Invoked on a background thread.
        /// </summary>
        event Action<string>? OnTextDelta;

        /// <summary>
        /// Makes the session ready to take a turn, and confirms the provider is actually there.
        /// </summary>
        /// <exception cref="AgentException">
        /// The provider could not be reached or refused to start. This is the one failure the host
        /// is expected to report to the player and then carry on around.
        /// </exception>
        Task StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Sends one message and waits for the complete response.
        /// </summary>
        /// <remarks>
        /// Prose arrives through <see cref="OnTextDelta"/> as it is generated; the returned text is
        /// the same content assembled, and is only worth reading on the error path.
        /// </remarks>
        Task<AgentTurnResult> SendAsync(string prompt, CancellationToken cancellationToken = default);

        /// <summary>Asks the provider to abandon the turn in progress, if it can.</summary>
        Task InterruptAsync();
    }
}
