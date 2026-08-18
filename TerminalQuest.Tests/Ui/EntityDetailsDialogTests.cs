using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class EntityDetailsDialogTests
    {
        private static TempSave Seeded()
        {
            var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", "A quiet ranger.", ClassTemplates.All[0], "The Ford");

            // Add an NPC, location, item, and story events
            var charFile = save.Store.ReadCharacters();
            var npc = new Character
            {
                Id = "chr_2",
                Name = "Bess",
                Kind = CharacterKind.Npc,
                Health = 12,
                MaxHealth = 15,
                Description = "A wise herbalist with grey hair."
            };
            charFile.Characters.Add(npc);
            save.Store.WriteCharacters(charFile);

            var itemFile = save.Store.ReadItems();
            var item = new ItemDefinition
            {
                Id = itemFile.TakeId(),
                Name = "Rusted Key",
                Description = "An old iron key found near the river."
            };
            itemFile.Items.Add(item);
            save.Store.WriteItems(itemFile);

            save.Store.Story.Append(new StoryEvent
            {
                Turn = 1,
                Title = "Met Bess at the river",
                Detail = "She spoke of an ancient ruin.",
                CharacterIds = ["chr_2"],
                LocationIds = ["loc_1"],
                ItemIds = [item.Id]
            });

            return save;
        }

        [Fact]
        public void Character_details_format_name_health_description_and_memories()
        {
            using var save = Seeded();

            var (title, content) = EntityDetailsDialog.FormatEntityDetails(save.Store, "chr_2");

            Assert.Equal("Character: Bess (chr_2)", title);
            Assert.Contains("Bess (ID: chr_2, npc)", content, StringComparison.Ordinal);
            Assert.Contains("Health: 12/15", content, StringComparison.Ordinal);
            Assert.Contains("A wise herbalist with grey hair.", content, StringComparison.Ordinal);
            Assert.Contains("Met Bess at the river", content, StringComparison.Ordinal);
            Assert.Contains("She spoke of an ancient ruin.", content, StringComparison.Ordinal);
        }

        [Fact]
        public void Location_details_format_name_roster_and_events()
        {
            using var save = Seeded();

            var (title, content) = EntityDetailsDialog.FormatEntityDetails(save.Store, "loc_1");

            Assert.Equal("Location: The Ford (loc_1)", title);
            Assert.Contains("The Ford (ID: loc_1)", content, StringComparison.Ordinal);
            Assert.Contains("Met Bess at the river", content, StringComparison.Ordinal);
        }

        [Fact]
        public void Item_details_format_name_description_and_events()
        {
            using var save = Seeded();

            var item = save.Store.ReadItems().Items.First(i => i.Name == "Rusted Key");
            var (title, content) = EntityDetailsDialog.FormatEntityDetails(save.Store, item.Id);

            Assert.Equal($"Item: Rusted Key ({item.Id})", title);
            Assert.Contains($"Rusted Key (ID: {item.Id})", content, StringComparison.Ordinal);
            Assert.Contains("An old iron key found near the river.", content, StringComparison.Ordinal);
            Assert.Contains("Met Bess at the river", content, StringComparison.Ordinal);
        }

        [Fact]
        public void Item_carried_by_player_shows_possession()
        {
            using var save = Seeded();

            // Starting weapon given to Rowan in NewGame.Create
            var item = save.Store.ReadItems().Items.First(i => i.Name == "iron longsword");
            var (title, content) = EntityDetailsDialog.FormatEntityDetails(save.Store, item.Id);

            Assert.Equal($"Item: iron longsword ({item.Id})", title);
            Assert.Contains($"iron longsword (ID: {item.Id})", content, StringComparison.Ordinal);
            Assert.Contains("Location / Possession:", content, StringComparison.Ordinal);
            Assert.Contains("Carried by Rowan (x1)", content, StringComparison.Ordinal);
        }

        [Fact]
        public void Item_at_location_shows_location()
        {
            using var save = Seeded();

            var locFile = save.Store.ReadLocations();
            var loc = locFile.Locations.First();
            var item = save.Store.ReadItems().Items.First(i => i.Name == "Rusted Key");
            loc.Items.Add(new ItemStack { ItemId = item.Id, Quantity = 1 });
            save.Store.WriteLocations(locFile);

            var (title, content) = EntityDetailsDialog.FormatEntityDetails(save.Store, item.Id);

            Assert.Contains("Location / Possession:", content, StringComparison.Ordinal);
            Assert.Contains($"At {loc.Name} (x1)", content, StringComparison.Ordinal);
        }

        [Fact]
        public void Unknown_entity_id_returns_clean_message()
        {
            using var save = Seeded();

            var (title, content) = EntityDetailsDialog.FormatEntityDetails(save.Store, "chr_999");

            Assert.Equal("Entity: chr_999", title);
            Assert.Contains("No entity found on record", content, StringComparison.Ordinal);
        }

        [Fact]
        public void CalculateDialogDimensions_respects_minimum_and_maximum_bounds()
        {
            // Short content should respect the minimum height of 18
            var (widthShort, heightShort) = EntityDetailsDialog.CalculateDialogDimensions("A short line.");
            Assert.Equal(18, heightShort);
            Assert.Equal(56, widthShort);

            // Tall content should expand up to 36 rows
            var tallContent = string.Join("\n", Enumerable.Range(1, 50).Select(i => $"Line {i}"));
            var (widthTall, heightTall) = EntityDetailsDialog.CalculateDialogDimensions(tallContent);
            Assert.Equal(36, heightTall);

            // Content with 20 lines -> 20 + 4 = 24 rows
            var mediumContent = string.Join("\n", Enumerable.Range(1, 20).Select(i => $"Line {i}"));
            var (_, heightMedium) = EntityDetailsDialog.CalculateDialogDimensions(mediumContent);
            Assert.Equal(24, heightMedium);

            // Constrained terminal rows/cols clamps safely
            var (widthConstrained, heightConstrained) = EntityDetailsDialog.CalculateDialogDimensions(tallContent, terminalRows: 24, terminalCols: 50);
            Assert.Equal(22, heightConstrained);
            Assert.Equal(46, widthConstrained);
        }

        [Fact]
        public void CreateDialog_configures_contentView_and_close_button()
        {
            var dialog = EntityDetailsDialog.CreateDialog(null, "Test Title", "Sample details text.");

            Assert.Equal("Test Title", dialog.Title);
            Assert.Equal(0, dialog.Padding.Thickness.Bottom);

            var closeButton = dialog.SubViews.OfType<Terminal.Gui.Views.Button>().FirstOrDefault(b => b.Text == "Close");
            Assert.NotNull(closeButton);
            Assert.True(closeButton.IsDefault);

            var contentView = dialog.SubViews.OfType<EntityDetailsContentView>().FirstOrDefault();
            Assert.NotNull(contentView);
            Assert.Equal("Sample details text.", contentView.Text);

            dialog.Layout();
            Assert.Equal(dialog.Viewport.Height - 1, closeButton.Frame.Y);
        }

        [Fact]
        public void Story_events_are_ordered_newest_first_and_separated_by_lines()
        {
            using var save = Seeded();

            // Append a second event on turn 3
            save.Store.Story.Append(new StoryEvent
            {
                Turn = 3,
                Title = "Found an ancient vault",
                Detail = "Bess helped translate the inscription.",
                CharacterIds = ["chr_2"],
                LocationIds = ["loc_1"],
            });

            var (_, content) = EntityDetailsDialog.FormatEntityDetails(save.Store, "chr_2");

            var turn3Index = content.IndexOf("[Turn 3]", StringComparison.Ordinal);
            var turn1Index = content.IndexOf("[Turn 1]", StringComparison.Ordinal);
            var separatorIndex = content.IndexOf("────────────────────────────────────────", StringComparison.Ordinal);

            Assert.True(turn3Index >= 0);
            Assert.True(turn1Index >= 0);
            Assert.True(separatorIndex >= 0);

            // Turn 3 appears before Turn 1 (newest first)
            Assert.True(turn3Index < turn1Index);
            // Separator appears between Turn 3 and Turn 1
            Assert.True(separatorIndex > turn3Index && separatorIndex < turn1Index);
        }
    }
}
