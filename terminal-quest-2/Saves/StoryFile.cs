namespace TerminalQuest.Saves
{
    /// <summary>Root document of <c>story.json</c>.</summary>
    internal sealed class StoryFile
    {
        public List<StoryEvent> Events { get; set; } = [];
    }
}
