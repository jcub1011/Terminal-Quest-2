namespace TerminalQuest.Saves
{
    /// <summary>
    /// One save as the menu needs to show it: what <c>save.json</c> says, plus what the folder
    /// costs on disk.
    /// <para>
    /// Separate from <see cref="SaveMetadata"/> because that type <em>is</em> the document - every
    /// property on it is written back out. Size is measured from the filesystem and would be a
    /// field that lies the moment anything else in the folder changes, so it lives here instead.
    /// </para>
    /// </summary>
    /// <param name="Name">The folder name, which is also the save's name.</param>
    /// <param name="LastPlayed">When the save was last stamped by <see cref="SaveStore.Touch"/>.</param>
    /// <param name="Turn">The turn reached, or zero for a save nobody has played yet.</param>
    /// <param name="SizeBytes">
    /// The total size of the folder's contents, or zero when it could not be measured.
    /// </param>
    internal readonly record struct SaveEntry(
        string Name,
        DateTimeOffset LastPlayed,
        int Turn,
        long SizeBytes)
    {
        private const long Kilobyte = 1024;
        private const long Megabyte = Kilobyte * 1024;

        /// <summary>
        /// The size as a column entry: bytes below a kilobyte, then one decimal place, so the
        /// number stays four characters wide however big the save gets.
        /// </summary>
        public string SizeText => SizeBytes switch
        {
            >= Megabyte => $"{SizeBytes / (double)Megabyte:0.0} MB",
            >= Kilobyte => $"{SizeBytes / (double)Kilobyte:0.0} KB",
            _ => $"{SizeBytes} B",
        };

        /// <summary>
        /// When the save was last written, in the player's own time zone. Never a relative phrase:
        /// the whole point of the column is telling two similar saves apart.
        /// </summary>
        public string LastPlayedText =>
            LastPlayed == default ? "never" : LastPlayed.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}
