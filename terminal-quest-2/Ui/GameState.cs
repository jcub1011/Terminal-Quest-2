using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>One line of the status pane's inventory list.</summary>
    internal readonly record struct InventoryEntry(int Quantity, string Name);

    /// <summary>
    /// The player-visible state shown in the status pane.
    /// <para>
    /// A view onto the save, not a second copy of it. Nothing here is authoritative: the narrator
    /// writes the files through its tools, from a different process, so this is refreshed from
    /// disk after every turn rather than mutated in step with the story. When the pane and the
    /// files disagree, the files are right.
    /// </para>
    /// </summary>
    internal sealed class GameState
    {
        /// <summary>The save being played, shown in the window title.</summary>
        public string SaveName { get; set; } = string.Empty;

        public int Health { get; set; }

        public int MaxHealth { get; set; }

        /// <summary>Where the player is standing, or empty when nowhere is on record yet.</summary>
        public string Location { get; set; } = string.Empty;

        public int Turn { get; set; }

        /// <summary>
        /// What the player carries, unformatted. The quantity and the name are kept apart because
        /// the pane colours them differently and wraps them as one line - neither is possible once
        /// they have been flattened into a single string.
        /// </summary>
        public List<InventoryEntry> Inventory { get; } = [];

        /// <summary>Coin in hand, shown on its own line however long the item list gets.</summary>
        public int Money { get; set; }

        /// <summary>Running total for the session, accumulated from each turn's reported cost.</summary>
        public double CostUsd { get; set; }

        /// <summary>Cache tokens read on the most recent turn.</summary>
        public int LastCacheRead { get; set; }

        /// <summary>Wall-clock duration of the most recent turn.</summary>
        public int LastDurationMs { get; set; }

        /// <summary>True while a turn is in flight, so the UI can show that it is waiting.</summary>
        public bool IsBusy { get; set; }

        /// <summary>
        /// Re-reads the save. Call after each turn: the narrator's writes happened in the MCP
        /// server process, so there is no event to subscribe to and nothing in memory to trust.
        /// </summary>
        /// <exception cref="SaveException">A document exists but could not be parsed.</exception>
        public void RefreshFrom(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            SaveName = store.Name;

            var characters = store.ReadCharacters();
            var player = SaveStore.Player(characters);

            Health = player?.Health ?? 0;
            MaxHealth = player?.MaxHealth ?? 0;
            Location = SaveStore.WhereIs(store.ReadLocations(), player?.Id)?.Name ?? string.Empty;

            var inventory = store.ReadInventory();
            Money = inventory.Money;

            Inventory.Clear();
            foreach (var item in inventory.Items)
            {
                Inventory.Add(new InventoryEntry(item.Quantity, item.Name));
            }
        }
    }
}
