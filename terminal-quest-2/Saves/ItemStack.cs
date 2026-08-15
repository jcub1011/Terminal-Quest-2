namespace TerminalQuest.Saves
{
    /// <summary>
    /// A stack reference to an item definition in the central item store.
    /// </summary>
    internal sealed class ItemStack
    {
        /// <summary>The ID of the item in <c>items.json</c> (e.g. <c>itm_1</c>).</summary>
        public string ItemId { get; set; } = string.Empty;

        /// <summary>Number of items in this stack.</summary>
        public int Quantity { get; set; }
    }
}
