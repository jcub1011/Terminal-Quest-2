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
        /// else: rosters and story event subjects point at <see cref="Id"/>.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        public CharacterKind Kind { get; set; }

        public int Health { get; set; }

        public int MaxHealth { get; set; }

        /// <summary>Background and aptitude: who they are and what they are good at.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// What they are made of. The six core attributes are always present in effect - seeded at
        /// creation, and filled in at <see cref="CharacterAttributes.Neutral"/> on read for anyone
        /// who predates them - and the narrator may add named ones of its own as the story earns
        /// them.
        /// </summary>
        public List<CharacterAttribute> Attributes { get; set; } = [];

        /// <summary>
        /// What they know that not everybody may. Absent on a save made before secrets existed, which
        /// reads as an empty list and is exactly right: nobody was keeping anything.
        /// </summary>
        public List<Secret> Secrets { get; set; } = [];
    }
}
