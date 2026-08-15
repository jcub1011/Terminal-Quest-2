namespace TerminalQuest.Saves
{
    /// <summary>A place in the world, who is standing in it, and what is lying there.</summary>
    internal sealed class Location
    {
        /// <summary>
        /// What every other record points at this place by. Opaque and never shown - see
        /// <see cref="EntityIds"/>.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Who is here right now - presence, not history. Holds <see cref="Character.Id"/>, not
        /// names, so renaming somebody does not have to be chased into every roster they stand in.
        /// </summary>
        public List<string> CharacterIds { get; set; } = [];

        /// <summary>Items lying at this location or in containers here.</summary>
        public List<ItemStack> Items { get; set; } = [];
    }
}
