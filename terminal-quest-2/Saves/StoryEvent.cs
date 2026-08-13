namespace TerminalQuest.Saves
{
    /// <summary>
    /// A beat in the player's narrative, as judged by the narrator: arriving somewhere, meeting
    /// someone, a bargain struck.
    /// <para>
    /// This is the continuity spine. On load the narrator is handed the log so it can pick the
    /// thread back up, and the player can read it with <c>/story</c>.
    /// </para>
    /// </summary>
    internal sealed class StoryEvent
    {
        public int Id { get; set; }

        public int Turn { get; set; }

        /// <summary>A short headline, the form <c>/story</c> lists.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Optional elaboration.</summary>
        public string Detail { get; set; } = string.Empty;

        public List<string> Tags { get; set; } = [];
    }
}
