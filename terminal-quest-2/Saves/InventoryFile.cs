namespace TerminalQuest.Saves
{
    /// <summary>
    /// Root document of <c>inventory.json</c>, mapping each character (player and NPCs) to their belongings.
    /// </summary>
    internal sealed class InventoryFile
    {
        /// <summary>Every character's inventory and purse on record.</summary>
        public List<CharacterInventory> Inventories { get; set; } = [];

        /// <summary>
        /// Gets the inventory for a character by id, or null if they have no belongings recorded yet.
        /// </summary>
        public CharacterInventory? Find(string characterId) =>
            Inventories.Find(inv => string.Equals(inv.CharacterId, characterId, StringComparison.Ordinal));

        /// <summary>
        /// Gets the inventory for a character by id, creating an empty one if none exists.
        /// </summary>
        public CharacterInventory GetOrCreate(string characterId)
        {
            var found = Find(characterId);
            if (found is not null)
            {
                return found;
            }

            found = new CharacterInventory { CharacterId = characterId };
            Inventories.Add(found);
            return found;
        }
    }
}
