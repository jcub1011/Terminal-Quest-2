namespace TerminalQuest.Ui
{
    /// <summary>What a player command produced: lines to print, and whether the game should end.</summary>
    internal sealed record PlayerCommandResult
    {
        public required IReadOnlyList<StyledLine> Lines { get; init; }

        public bool Quit { get; init; }
    }
}
