namespace TerminalQuest.Saves
{
    /// <summary>
    /// A save file could not be read or written. Carries a message fit to show the player, since
    /// both callers - the TUI and the MCP server - have to report it rather than crash.
    /// </summary>
    internal sealed class SaveException : Exception
    {
        public SaveException(string message, Exception? innerException = null)
            : base(message, innerException)
        {
        }
    }
}
