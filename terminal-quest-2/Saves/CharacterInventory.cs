namespace TerminalQuest.Saves
{
    /// <summary>
    /// The belongings and purse of a specific character (player or NPC).
    /// </summary>
    internal sealed class CharacterInventory
    {
        /// <summary>The ID of the character who owns this inventory (e.g. <c>chr_1</c>).</summary>
        public string CharacterId { get; set; } = string.Empty;

        /// <summary>Coin in purse. Must never be negative.</summary>
        public int Money { get; set; }

        /// <summary>Item stacks carried by this character.</summary>
        public List<ItemStack> Items { get; set; } = [];
    }
}
