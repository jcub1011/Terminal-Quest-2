namespace TerminalQuest.Saves
{
    /// <summary>Root document of <c>save.json</c>: what the save menu needs without opening the rest.</summary>
    internal sealed class SaveMetadata
    {
        public string Name { get; set; } = string.Empty;

        public DateTimeOffset Created { get; set; }

        public DateTimeOffset LastPlayed { get; set; }

        public int Turn { get; set; }
    }
}
