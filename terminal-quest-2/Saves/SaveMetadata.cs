namespace TerminalQuest.Saves
{
    /// <summary>Root document of <c>save.json</c>: what the save menu needs without opening the rest.</summary>
    internal sealed class SaveMetadata
    {
        /// <summary>
        /// Which shape of save this is, so a build can tell a playthrough it understands from one
        /// it would quietly corrupt. Zero means it predates the field entirely - see
        /// <see cref="SaveStore.RequireSupportedSchema"/>.
        /// </summary>
        public int SchemaVersion { get; set; }

        public string Name { get; set; } = string.Empty;

        public DateTimeOffset Created { get; set; }

        public DateTimeOffset LastPlayed { get; set; }

        public int Turn { get; set; }
    }
}
