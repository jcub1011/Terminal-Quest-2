using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// The status pane's view onto the save.
    /// </summary>
    /// <remarks>
    /// Nothing here is authoritative — the narrator writes the files from another process. When the
    /// pane and the files disagree, the files are right, so the property under test is really "does
    /// a refresh discard everything it previously believed".
    /// </remarks>
    public sealed class GameStateTests
    {
        private static TempSave Seeded(string startLocation = "The Ford")
        {
            var save = new TempSave("Riverbend");
            NewGame.Create(save.Store, "Rowan", "A quiet sort.", ClassTemplates.All[0], startLocation);
            return save;
        }

        [Fact]
        public void A_refresh_reads_the_player_and_the_place()
        {
            using var save = Seeded();
            var state = new GameState();

            state.RefreshFrom(save.Store);

            Assert.Equal("Riverbend", state.SaveName);
            Assert.Equal("The Ford", state.Location);
            Assert.True(state.Health > 0);
            Assert.Equal(state.MaxHealth, state.Health);
        }

        [Fact]
        public void An_empty_save_reads_as_nothing_rather_than_throwing()
        {
            using var save = new TempSave();
            var state = new GameState();

            state.RefreshFrom(save.Store);

            Assert.Equal(0, state.Health);
            Assert.Equal(0, state.MaxHealth);
            Assert.Equal(string.Empty, state.Location);
            Assert.Empty(state.Attributes);
            Assert.Empty(state.Inventory);
        }

        [Fact]
        public void A_player_who_is_nowhere_shows_no_place()
        {
            using var save = Seeded(startLocation: string.Empty);
            var state = new GameState();

            state.RefreshFrom(save.Store);

            Assert.Equal(string.Empty, state.Location);
        }

        [Fact]
        public void The_pane_shows_the_six_by_their_first_three_letters()
        {
            using var save = Seeded();
            var state = new GameState();

            state.RefreshFrom(save.Store);

            Assert.Equal(
                CharacterAttributes.Core.Select(name => name[..3].ToUpperInvariant()).ToList(),
                state.Attributes.Select(entry => entry.Label).ToList());
        }

        [Fact]
        public void Freeform_attributes_are_left_out_of_the_pane()
        {
            // There is no bound on how many the narrator invents or how long it names them, and the
            // pane is twenty-seven columns wide.
            using var save = Seeded();
            var characters = save.Store.ReadCharacters();
            CharacterAttributes.Set(SaveStore.Player(characters)!, "Standing in the guild", 14);
            save.Store.WriteCharacters(characters);

            var state = new GameState();
            state.RefreshFrom(save.Store);

            Assert.Equal(CharacterAttributes.Core.Count, state.Attributes.Count);
        }

        [Fact]
        public void Every_core_label_is_three_characters_wide()
        {
            // The pane has room for a three-wide label and a two-digit score, twice to a row.
            using var save = Seeded();
            var state = new GameState();

            state.RefreshFrom(save.Store);

            Assert.All(state.Attributes, entry => Assert.Equal(3, entry.Label.Length));
        }

        [Fact]
        public void The_inventory_and_purse_come_from_the_record()
        {
            using var save = Seeded();
            var state = new GameState();

            state.RefreshFrom(save.Store);

            var player = SaveStore.Player(save.Store.ReadCharacters())!;
            var inventory = save.Store.ReadInventory().Find(player.Id)!;
            var items = save.Store.ReadItems();
            Assert.Equal(inventory.Money, state.Money);
            var itemNames = inventory.Items.Select(stack => SaveStore.FindItemById(items, stack.ItemId)!.Name).ToList();
            Assert.Equal(
                itemNames,
                state.Inventory.Select(entry => entry.Name).ToList());
        }

        [Fact]
        public void A_refresh_replaces_rather_than_appends()
        {
            // The stale-entry bug: refreshing twice must not double the lists.
            using var save = Seeded();
            var state = new GameState();

            state.RefreshFrom(save.Store);
            var items = state.Inventory.Count;
            var attributes = state.Attributes.Count;

            state.RefreshFrom(save.Store);

            Assert.Equal(items, state.Inventory.Count);
            Assert.Equal(attributes, state.Attributes.Count);
        }

        [Fact]
        public void A_refresh_follows_the_files_when_they_change_underneath()
        {
            using var save = Seeded();
            var state = new GameState();
            state.RefreshFrom(save.Store);

            var characters = save.Store.ReadCharacters();
            SaveStore.Player(characters)!.Health = 3;
            save.Store.WriteCharacters(characters);

            state.RefreshFrom(save.Store);

            Assert.Equal(3, state.Health);
        }

        [Fact]
        public void A_refresh_that_finds_a_broken_document_reports_it()
        {
            using var save = Seeded();
            save.WriteRaw("characters.json", "{ not json");

            Assert.Throws<SaveException>(() => new GameState().RefreshFrom(save.Store));
        }

        [Fact]
        public void A_null_store_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(() => new GameState().RefreshFrom(null!));
        }

        [Fact]
        public void Session_counters_are_not_touched_by_a_refresh()
        {
            // They accumulate across turns and are not in the save at all.
            using var save = Seeded();
            var state = new GameState { CostUsd = 1.25, LastCacheRead = 42, LastDurationMs = 900, IsBusy = true };

            state.RefreshFrom(save.Store);

            Assert.Equal(1.25, state.CostUsd);
            Assert.Equal(42, state.LastCacheRead);
            Assert.Equal(900, state.LastDurationMs);
            Assert.True(state.IsBusy);
        }
    }
}
