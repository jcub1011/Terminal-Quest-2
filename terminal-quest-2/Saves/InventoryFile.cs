namespace TerminalQuest.Saves
{
    /// <summary>Root document of <c>inventory.json</c>.</summary>
    internal sealed class InventoryFile
    {
        public List<Item> Items { get; set; } = [];
    }
}
