namespace TerminalQuest.Saves
{
    /// <summary>
    /// A person in the world - the player or anyone the narrator voices.
    /// </summary>
    internal sealed class Character
    {
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
