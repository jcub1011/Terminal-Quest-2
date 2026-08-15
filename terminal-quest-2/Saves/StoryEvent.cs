namespace TerminalQuest.Saves
{
    /// <summary>
    /// A beat or memory in the narrative: arriving somewhere, meeting someone, a bargain struck,
    /// a conversation overheard, or an event witnessed.
    /// <para>
    /// This is the unified narrative and continuity spine. On load the narrator is handed the log
    /// or queries it via <c>recall</c>, and the player can read it with <c>/story</c>.
    /// </para>
    /// </summary>
    internal sealed class StoryEvent
    {
        public int Id { get; set; }

        public int Turn { get; set; }

        /// <summary>A short headline, the form <c>/story</c> lists.</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Detailed prose description of what occurred.</summary>
        public string Detail { get; set; } = string.Empty;

        /// <summary>IDs of characters involved or witnessing (e.g. <c>chr_1</c>).</summary>
        public List<string> CharacterIds { get; set; } = [];

        /// <summary>IDs of locations where this occurred (e.g. <c>loc_1</c>).</summary>
        public List<string> LocationIds { get; set; } = [];

        /// <summary>IDs of items involved (e.g. <c>itm_1</c>).</summary>
        public List<string> ItemIds { get; set; } = [];

        /// <summary>Optional tags for topic classification.</summary>
        public List<string> Tags { get; set; } = [];
    }
}
