namespace TerminalQuest.Saves
{
    /// <summary>
    /// Canonical item entity definition in the central item store (<c>items.json</c>).
    /// </summary>
    internal sealed class ItemDefinition
    {
        /// <summary>
        /// Opaque persistent identifier (e.g. <c>itm_1</c>). Never shown directly to the player or model.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Canonical name of the item.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Canonical sensory description of the item.</summary>
        public string Description { get; set; } = string.Empty;
    }
}
