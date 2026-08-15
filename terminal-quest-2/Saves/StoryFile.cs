namespace TerminalQuest.Saves
{
    /// <summary>Root document of <c>story.json</c>.</summary>
    internal sealed class StoryFile
    {
        public List<StoryEvent> Events { get; set; } = [];

        /// <summary>The monotonic counter behind numeric story event IDs.</summary>
        public int NextId { get; set; }

        /// <summary>Allocates the next free id and advances the counter.</summary>
        public int TakeId()
        {
            NextId = Math.Max(NextId, Events.Count == 0 ? 0 : Events.Max(e => e.Id)) + 1;
            return NextId;
        }
    }
}
