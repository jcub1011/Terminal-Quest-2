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

        /// <summary>
        /// The counter behind <c>chr_N</c>. Monotonic: an id is never reused, not even after the
        /// character holding it is gone, because a reused id would silently re-point every stale
        /// reference at somebody else.
        /// </summary>
        public int NextId { get; set; }

        /// <summary>
        /// Allocates the next free id and advances the counter. The caller writes the file.
        /// </summary>
        /// <remarks>
        /// Deliberately on the document rather than on <see cref="SaveStore"/>: allocating an id
        /// and adding the record it belongs to must land in the same write, so that the temporary
        /// file and move that already makes a write atomic covers both. A counter kept in another
        /// document would need a second write, and a crash between the two would either burn an id
        /// or issue one twice.
        /// </remarks>
        public string TakeId()
        {
            NextId = EntityIds.Ceiling(EntityIds.Character, Characters.Select(character => character.Id), NextId) + 1;
            return EntityIds.Character + NextId;
        }
    }
}
