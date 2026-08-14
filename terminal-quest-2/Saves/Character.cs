namespace TerminalQuest.Saves
{
    /// <summary>
    /// A person in the world - the player or anyone the narrator voices.
    /// </summary>
    internal sealed class Character
    {
        /// <summary>
        /// What every other record points at them by. Opaque, permanent, and never shown to the
        /// player or the narrator - see <see cref="EntityIds"/>.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// What they are called, which is theirs to change. Renaming touches this and nothing
        /// else: rosters and memory subjects point at <see cref="Id"/>.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        public CharacterKind Kind { get; set; }

        public int Health { get; set; }

        public int MaxHealth { get; set; }

        /// <summary>Background and aptitude: who they are and what they are good at.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Everything they know, oldest first.</summary>
        public List<Memory> Memories { get; set; } = [];
    }
}
