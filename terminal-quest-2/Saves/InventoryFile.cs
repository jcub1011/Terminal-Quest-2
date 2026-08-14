namespace TerminalQuest.Saves
{
    /// <summary>Root document of <c>inventory.json</c>.</summary>
    internal sealed class InventoryFile
    {
        public List<Item> Items { get; set; } = [];

        /// <summary>
        /// Coin in hand. A field of its own rather than an item named "gold", because currency is
        /// spent and counted rather than carried: it needs no description, it must never be one of
        /// several near-identical stacks the narrator invented, and the status pane gives it a line
        /// whether the player has any or not. Never negative - the tools refuse to overspend.
        /// </summary>
        public int Money { get; set; }
    }
}
