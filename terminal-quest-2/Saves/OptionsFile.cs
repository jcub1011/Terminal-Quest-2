namespace TerminalQuest.Saves
{
    /// <summary>
    /// The action choices presented by the narrator for the player to decide what to do next.
    /// </summary>
    public sealed class OptionsFile
    {
        public int Turn { get; set; }

        public List<string> Options { get; set; } = [];
    }
}
