using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// Seeding a fresh save — the only place the game process writes story data itself.
    /// </summary>
    public sealed class NewGameTests
    {
        private static ClassTemplate Warrior => ClassTemplates.All.Single(t => t.Name == "Warrior");

        [Fact]
        public void The_player_is_written_as_the_player()
        {
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", "A quiet sort.", Warrior, null);

            var player = Assert.Single(save.Store.ReadCharacters().Characters);
            Assert.Equal(CharacterKind.Player, player.Kind);
            Assert.Equal("Rowan", player.Name);
        }

        [Fact]
        public void The_name_is_trimmed()
        {
            using var save = new TempSave();

            NewGame.Create(save.Store, "  Rowan  ", string.Empty, Warrior, null);

            Assert.Equal("Rowan", Assert.Single(save.Store.ReadCharacters().Characters).Name);
        }

        [Fact]
        public void The_save_is_stamped_with_the_current_schema()
        {
            // Stamped before anything else, so a folder abandoned halfway through seeding is still
            // recognisably a save of this shape rather than an old one.
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", string.Empty, Warrior, null);

            Assert.Equal(SaveStore.CurrentSchemaVersion, save.Store.ReadMetadata().SchemaVersion);
            save.Store.RequireSupportedSchema();
        }

        [Fact]
        public void Health_starts_full_at_the_class_maximum()
        {
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", string.Empty, Warrior, null);

            var player = Assert.Single(save.Store.ReadCharacters().Characters);
            Assert.Equal(Warrior.MaxHealth, player.MaxHealth);
            Assert.Equal(Warrior.MaxHealth, player.Health);
        }

        [Fact]
        public void The_purse_and_the_kit_come_from_the_class()
        {
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", string.Empty, Warrior, null);

            var inventory = save.Store.ReadInventory();
            Assert.Equal(Warrior.StartingMoney, inventory.Money);
            Assert.Equal(
                Warrior.StartingItems.Select(item => item.Name).ToList(),
                inventory.Items.Select(item => item.Name).ToList());
        }

        [Fact]
        public void Ids_are_allocated_in_order()
        {
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", string.Empty, Warrior, "The Ford");

            Assert.Equal("chr_1", Assert.Single(save.Store.ReadCharacters().Characters).Id);
            Assert.Equal("loc_1", Assert.Single(save.Store.ReadLocations().Locations).Id);
            Assert.Equal(
                Enumerable.Range(1, Warrior.StartingItems.Count).Select(n => $"itm_{n}").ToList(),
                save.Store.ReadInventory().Items.Select(item => item.Id).ToList());
        }

        [Fact]
        public void The_player_leaves_the_screen_with_all_six_attributes()
        {
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", string.Empty, Warrior, null);

            var player = Assert.Single(save.Store.ReadCharacters().Characters);
            Assert.Equal(
                CharacterAttributes.Core,
                player.Attributes.Select(attribute => attribute.Name).ToList());
        }

        [Fact]
        public void The_class_spread_is_copied_rather_than_handed_out()
        {
            // The templates are static and the narrator edits attributes in place, so sharing one
            // would spend the next character's scores.
            using var save = new TempSave();
            var before = Warrior.Attributes.Select(a => a.Score).ToList();

            NewGame.Create(save.Store, "Rowan", string.Empty, Warrior, null);

            var player = Assert.Single(save.Store.ReadCharacters().Characters);
            CharacterAttributes.Set(player, "Strength", 3);

            Assert.Equal(before, Warrior.Attributes.Select(a => a.Score).ToList());
        }

        [Fact]
        public void The_kit_is_copied_rather_than_handed_out()
        {
            using var save = new TempSave();
            var before = Warrior.StartingItems.Select(item => item.Quantity).ToList();

            NewGame.Create(save.Store, "Rowan", string.Empty, Warrior, null);

            foreach (var item in save.Store.ReadInventory().Items)
            {
                item.Quantity = 99;
            }

            Assert.Equal(before, Warrior.StartingItems.Select(item => item.Quantity).ToList());
            Assert.All(Warrior.StartingItems, item => Assert.True(string.IsNullOrEmpty(item.Id)));
        }

        // ---- Starting location ------------------------------------------------------------

        [Fact]
        public void A_named_start_puts_the_player_in_it()
        {
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", string.Empty, Warrior, "  The Ford  ");

            var location = Assert.Single(save.Store.ReadLocations().Locations);
            Assert.Equal("The Ford", location.Name);
            Assert.Equal(["chr_1"], location.CharacterIds);
        }

        [Fact]
        public void A_named_start_is_left_undescribed_for_the_narrator()
        {
            // A description invented here would be one the story never agreed to.
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", string.Empty, Warrior, "The Ford");

            Assert.Equal(string.Empty, Assert.Single(save.Store.ReadLocations().Locations).Description);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void No_start_leaves_the_world_empty(string? startLocation)
        {
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", string.Empty, Warrior, startLocation);

            Assert.Empty(save.Store.ReadLocations().Locations);
            Assert.False(save.Has("locations.json"));
        }

        // ---- Description --------------------------------------------------------------------

        [Fact]
        public void An_empty_description_is_the_aptitude_alone()
        {
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", "   ", Warrior, null);

            Assert.Equal(Warrior.Aptitude, Assert.Single(save.Store.ReadCharacters().Characters).Description);
        }

        [Theory]
        [InlineData("A quiet sort", "A quiet sort. ")]
        [InlineData("A quiet sort.", "A quiet sort. ")]
        [InlineData("A quiet sort!", "A quiet sort! ")]
        [InlineData("A quiet sort,", "A quiet sort, ")]
        [InlineData("Called 'Rowan'", "Called 'Rowan' ")]
        public void A_description_ending_in_punctuation_does_not_gain_a_second_full_stop(
            string typed,
            string expectedPrefix)
        {
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", typed, Warrior, null);

            Assert.Equal(
                expectedPrefix + Warrior.Aptitude,
                Assert.Single(save.Store.ReadCharacters().Characters).Description);
        }

        [Fact]
        public void A_description_ending_in_something_that_is_not_punctuation_gains_a_full_stop()
        {
            // char.IsPunctuation is narrower than "ends a sentence": a digit does not count.
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", "Veteran of 3", Warrior, null);

            Assert.Equal(
                "Veteran of 3. " + Warrior.Aptitude,
                Assert.Single(save.Store.ReadCharacters().Characters).Description);
        }

        [Fact]
        public void The_description_is_trimmed_before_it_is_joined()
        {
            using var save = new TempSave();

            NewGame.Create(save.Store, "Rowan", "  A quiet sort  ", Warrior, null);

            Assert.StartsWith(
                "A quiet sort. ",
                Assert.Single(save.Store.ReadCharacters().Characters).Description,
                StringComparison.Ordinal);
        }

        // ---- Guards -------------------------------------------------------------------------

        [Fact]
        public void A_null_store_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(
                () => NewGame.Create(null!, "Rowan", string.Empty, Warrior, null));
        }

        [Fact]
        public void A_null_template_is_a_programming_error()
        {
            using var save = new TempSave();

            Assert.Throws<ArgumentNullException>(
                () => NewGame.Create(save.Store, "Rowan", string.Empty, null!, null));
        }

        [Fact]
        public void A_null_name_is_a_programming_error()
        {
            using var save = new TempSave();

            Assert.Throws<ArgumentNullException>(
                () => NewGame.Create(save.Store, null!, string.Empty, Warrior, null));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void A_blank_name_is_refused(string name)
        {
            using var save = new TempSave();

            Assert.Throws<ArgumentException>(
                () => NewGame.Create(save.Store, name, string.Empty, Warrior, null));
        }

        [Fact]
        public void Every_class_seeds_a_playable_save()
        {
            foreach (var template in ClassTemplates.All)
            {
                using var save = new TempSave();

                NewGame.Create(save.Store, "Rowan", string.Empty, template, "The Ford");

                save.Store.RequireSupportedSchema();
                var player = Assert.Single(save.Store.ReadCharacters().Characters);
                Assert.True(player.Health > 0);
                Assert.NotEmpty(save.Store.ReadInventory().Items);
                Assert.Equal(player.Id, Assert.Single(save.Store.ReadLocations().Locations).CharacterIds.Single());
            }
        }
    }
}
