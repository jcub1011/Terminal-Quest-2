namespace TerminalQuest.Ui
{
    /// <summary>What a player command produced: lines to print, and anything the host must do next.</summary>
    /// <remarks>
    /// The flags are intents rather than effects. <see cref="PlayerCommands.Execute"/> is static and
    /// holds nothing but a save - no window, no editor, no session - so a command that has to reach
    /// any of those says what it wants here and lets the host, which has all three, carry it out.
    /// </remarks>
    internal sealed record PlayerCommandResult
    {
        public required IReadOnlyList<StyledLine> Lines { get; init; }

        public bool Quit { get; init; }

        /// <summary>
        /// Whether the player asked to rewrite this save's narrator brief.
        /// </summary>
        /// <remarks>
        /// The host opens an editor on the save's <c>system-prompt.txt</c> and then ends the session,
        /// because the running narrator captured the old prompt when it started and will hold it until
        /// it is replaced by a new one.
        /// </remarks>
        public bool EditSystemPrompt { get; init; }

        /// <summary>
        /// If set, requests opening the Entity Details modal for the specified entity ID.
        /// </summary>
        public string? InspectEntityId { get; init; }

        /// <summary>
        /// Whether the command modified save state and requires refreshing GameState and UI status views.
        /// </summary>
        public bool RefreshState { get; init; }
    }
}
