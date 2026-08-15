namespace TerminalQuest.Saves
{
    /// <summary>Root document of <c>items.json</c>.</summary>
    internal sealed class ItemFile
    {
        public List<ItemDefinition> Items { get; set; } = [];

        /// <summary>
        /// The counter behind <c>itm_N</c>. Monotonic - an id is never reused.
        /// </summary>
        public int NextId { get; set; }

        /// <summary>Allocates the next free id and advances the counter. The caller writes the file.</summary>
        public string TakeId()
        {
            NextId = EntityIds.Ceiling(EntityIds.Item, Items.Select(item => item.Id), NextId) + 1;
            return EntityIds.Item + NextId;
        }
    }
}
