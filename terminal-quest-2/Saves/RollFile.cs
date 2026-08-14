namespace TerminalQuest.Saves
{
    /// <summary>
    /// Root document of <c>rolls.json</c>.
    /// <para>
    /// Its own document rather than a list on <see cref="StoryFile"/>, though either would have been
    /// equally additive. One fight produces more rolls than a session produces story beats, and
    /// <c>story.json</c> is the file meant to be read by hand and fed back to the narrator on load -
    /// interleaving the two would spoil both. The transcript also re-reads this several times a
    /// second while a turn is running, which is a thing to do to a small append-only log and not to
    /// the continuity spine.
    /// </para>
    /// </summary>
    internal sealed class RollFile
    {
        /// <summary>Every roll, oldest first. Append-only in practice: nothing rewrites history here.</summary>
        public List<DiceRoll> Rolls { get; set; } = [];
    }
}
