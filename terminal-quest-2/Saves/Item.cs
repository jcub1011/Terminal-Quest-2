namespace TerminalQuest.Saves
{
    /// <summary>One line of the player's inventory.</summary>
    internal sealed class Item
    {
        public string Name { get; set; } = string.Empty;

        public int Quantity { get; set; }

        public string Description { get; set; } = string.Empty;
    }
}
