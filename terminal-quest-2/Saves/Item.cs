namespace TerminalQuest.Saves
{
    /// <summary>One line of the player's inventory.</summary>
    internal sealed class Item
    {
        /// <summary>
        /// What other records point at this stack by. Opaque and never shown - see
        /// <see cref="EntityIds"/>.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public string Description { get; set; } = string.Empty;
    }
}
