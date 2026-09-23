using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
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
            Assert.Equal("Rowan", state.PlayerName);
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
            var itemIds = inventory.Items.Select(stack => stack.ItemId).ToList();
            Assert.Equal(
                itemNames,
                state.Inventory.Select(entry => entry.Name).ToList());
            Assert.Equal(
                itemIds,
                state.Inventory.Select(entry => entry.Id).ToList());
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

        [Fact]
        public void A_refresh_updates_to_new_player_when_tag_is_transferred()
        {
            using var save = Seeded(startLocation: "The Ford");
            var state = new GameState();
            state.RefreshFrom(save.Store);

            Assert.Equal("Rowan", state.PlayerName);
            Assert.Equal("The Ford", state.Location);

            // Add new character Bess at The Mill with distinct stats, inventory, and purse
            var charFile = save.Store.ReadCharacters();
            var bess = new Character
            {
                Id = "chr_2",
                Name = "Bess",
                Kind = CharacterKind.Npc,
                Health = 15,
                MaxHealth = 18,
            };
            CharacterAttributes.Seed(bess, null);
            CharacterAttributes.Set(bess, "Strength", 16);
            charFile.Characters.Add(bess);
            save.Store.WriteCharacters(charFile);

            var locFile = save.Store.ReadLocations();
            var mill = new Location { Id = "loc_2", Name = "The Mill" };
            mill.CharacterIds.Add(bess.Id);
            locFile.Locations.Add(mill);
            save.Store.WriteLocations(locFile);

            var itemFile = save.Store.ReadItems();
            var herb = new ItemDefinition { Id = "itm_10", Name = "Healing Herb" };
            itemFile.Items.Add(herb);
            save.Store.WriteItems(itemFile);

            var invFile = save.Store.ReadInventory();
            var bessInv = invFile.GetOrCreate(bess.Id);
            bessInv.Money = 75;
            bessInv.Items.Add(new ItemStack { ItemId = herb.Id, Quantity = 3 });
            save.Store.WriteInventory(invFile);

            // Transfer player tag to Bess
            save.Store.SetPlayer(bess.Id);

            // Refresh state
            state.RefreshFrom(save.Store);

            Assert.Equal("Bess", state.PlayerName);
            Assert.Equal("The Mill", state.Location);
            Assert.Equal(15, state.Health);
            Assert.Equal(18, state.MaxHealth);
            Assert.Equal(75, state.Money);
            Assert.Equal(16, state.Attributes.Single(a => a.Label == "STR").Score);
            var itemEntry = Assert.Single(state.Inventory);
            Assert.Equal("Healing Herb", itemEntry.Name);
            Assert.Equal(3, itemEntry.Quantity);
            Assert.Equal("itm_10", itemEntry.Id);
        }

        // ---- The status pane's colour hierarchy --------------------------------------------------

        private static IEnumerable<T> FindDescendants<T>(View root) where T : View
        {
            foreach (var sub in root.SubViews)
            {
                if (sub is T match) yield return match;
                foreach (var nested in FindDescendants<T>(sub))
                {
                    yield return nested;
                }
            }
        }

        private static Label LabelWith(StatusView view, string text) =>
            FindDescendants<Label>(view).First(l => l.Text == text);

        [Fact]
        public void Captions_recede_so_values_lead()
        {
            using var view = new StatusView(new GameState { Health = 20, MaxHealth = 20 });

            foreach (var caption in new[] { "HP:", "Turn:", "Gold:", "Context:" })
            {
                Assert.Equal(Theme.Attr(TextRole.Hint), LabelWith(view, caption).GetAttributeForRole(VisualRole.Normal));
            }
        }

        [Fact]
        public void Healthy_vitals_read_normally()
        {
            using var view = new StatusView(new GameState { Health = 20, MaxHealth = 20, Money = 10 });

            Assert.Equal(Theme.Attr(TextRole.Normal), LabelWith(view, "20 / 20").GetAttributeForRole(VisualRole.Normal));
            Assert.Equal(Theme.Attr(TextRole.Item), LabelWith(view, "10 gp").GetAttributeForRole(VisualRole.Normal));
        }

        [Fact]
        public void Low_health_reads_as_danger()
        {
            using var view = new StatusView(new GameState { Health = 5, MaxHealth = 20 });

            Assert.Equal(Theme.Attr(TextRole.Danger), LabelWith(view, "5 / 20").GetAttributeForRole(VisualRole.Normal));
        }

        [Theory]
        [InlineData(50, (int)TextRole.Normal)]
        [InlineData(85, (int)TextRole.Important)]
        [InlineData(96, (int)TextRole.Danger)]
        public void Context_pressure_escalates_through_warning_to_danger(int percent, int expectedRole)
        {
            using var view = new StatusView(new GameState { ContextTokens = percent, ContextWindowTokens = 100 });

            Assert.Equal(Theme.Attr((TextRole)expectedRole), LabelWith(view, $"{percent} ({percent}%)").GetAttributeForRole(VisualRole.Normal));
        }

        [Fact]
        public void Placeholders_recede_like_captions()
        {
            using var view = new StatusView(new GameState());

            Assert.Equal(Theme.Attr(TextRole.Hint), LabelWith(view, "(No attributes)").GetAttributeForRole(VisualRole.Normal));
        }
    }
}
