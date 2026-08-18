namespace TerminalQuest.Saves
{
    /// <summary>
    /// Structured options presented by the Narrator for the player to select from on a turn.
    /// </summary>
    internal sealed class OptionsFile
    {
        public int Turn { get; set; }

        public List<string> Options { get; set; } = [];
    }
}
