namespace TerminalQuest.Saves
{
    /// <summary>Root document of <c>characters.json</c>.</summary>
    /// <remarks>
    /// An object wrapping the array rather than a bare array, so the schema has somewhere to grow
    /// without invalidating every save already on disk.
    /// </remarks>
    internal sealed class CharacterFile
    {
        public List<Character> Characters { get; set; } = [];
    }
}
