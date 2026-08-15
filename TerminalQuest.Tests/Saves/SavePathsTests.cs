using System.Text.Json;

using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// Where saves live, and the rules for naming one.
    /// </summary>
    /// <remarks>
    /// Serialized: every test here redirects <c>TQ_SAVES</c>, which is process-wide state.
    /// </remarks>
    [Collection(EnvironmentCollection.Name)]
    [Trait(Categories.Name, Categories.Environment)]
    public sealed class SavePathsTests
    {
        // ---- Naming (pure, no filesystem) ------------------------------------------------

        [Theory]
        [InlineData("Riverbend")]
        [InlineData("a")]
        [InlineData("  Trimmed  ")]
        [InlineData("A save with spaces")]
        [InlineData("Save-1_final(2)")]
        public void A_usable_name_is_accepted(string name)
        {
            Assert.True(SavePaths.IsValidName(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(".")]
        [InlineData("..")]
        [InlineData("  ..  ")]
        public void A_name_that_could_never_be_a_folder_is_refused(string? name)
        {
            Assert.False(SavePaths.IsValidName(name));
        }

        [Theory]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        [InlineData("../evil")]
        [InlineData("..\\evil")]
        [InlineData("C:\\Windows")]
        public void A_name_carrying_a_path_is_refused(string name)
        {
            // Rejected rather than silently rewritten, so the name in the menu is always the name
            // on disk — and so nothing can be pointed outside the saves folder.
            Assert.False(SavePaths.IsValidName(name));
        }

        [Fact]
        public void A_name_longer_than_the_column_allows_is_refused()
        {
            Assert.True(SavePaths.IsValidName(new string('x', 64)));
            Assert.False(SavePaths.IsValidName(new string('x', 65)));
        }

        // ---- Path traversal ----------------------------------------------------------------

        public static TheoryData<string> HostileNames() =>
        [
            "../evil",
            "..\\evil",
            "a/b",
            "a\\b",
            "..",
            ".",
            "",
            "   ",
        ];

        [Theory]
        [MemberData(nameof(HostileNames))]
        public void Deleting_refuses_a_name_that_could_reach_outside_the_saves_folder(string name)
        {
            // The operation where this matters most: a recursive delete taking a path from the
            // caller could remove anything at all.
            using var root = new SavesRoot();

            Assert.Throws<ArgumentException>(() => SavePaths.Delete(name));
        }

        [Theory]
        [MemberData(nameof(HostileNames))]
        public void Opening_refuses_a_hostile_name(string name)
        {
            using var root = new SavesRoot();

            Assert.Throws<ArgumentException>(() => SavePaths.Open(name));
        }

        [Theory]
        [MemberData(nameof(HostileNames))]
        public void Naming_a_folder_refuses_a_hostile_name(string name)
        {
            using var root = new SavesRoot();

            Assert.Throws<ArgumentException>(() => SavePaths.Folder(name));
        }

        [Theory]
        [MemberData(nameof(HostileNames))]
        public void Resetting_refuses_a_hostile_name(string name)
        {
            using var root = new SavesRoot();

            Assert.Throws<ArgumentException>(() => SavePaths.Reset(name));
        }

        [Fact]
        public void A_traversing_delete_leaves_the_target_alone()
        {
            using var root = new SavesRoot();
            var outside = Path.Combine(Path.GetDirectoryName(root.Root)!, "Outside");
            Directory.CreateDirectory(outside);

            Assert.Throws<ArgumentException>(() => SavePaths.Delete("../Outside"));
            Assert.True(Directory.Exists(outside));
        }

        // ---- Root ----------------------------------------------------------------------------

        [Fact]
        public void The_root_follows_the_environment_variable()
        {
            using var root = new SavesRoot();

            Assert.Equal(Path.GetFullPath(root.Root), SavePaths.Root);
        }

        [Fact]
        public void A_saves_folder_that_does_not_exist_lists_nothing()
        {
            using var root = new SavesRoot();
            Directory.Delete(root.Root, recursive: true);

            Assert.Empty(SavePaths.List());
        }

        // ---- Open -------------------------------------------------------------------------------

        [Fact]
        public void Opening_a_new_save_creates_and_stamps_it()
        {
            using var root = new SavesRoot();
            var before = DateTimeOffset.Now.AddSeconds(-1);

            var store = SavePaths.Open("Riverbend");
            var metadata = store.ReadMetadata();

            Assert.True(Directory.Exists(Path.Combine(root.Root, "Riverbend")));
            Assert.Equal(SaveStore.CurrentSchemaVersion, metadata.SchemaVersion);
            Assert.Equal("Riverbend", metadata.Name);
            Assert.Equal(0, metadata.Turn);
            Assert.InRange(metadata.Created, before, DateTimeOffset.Now.AddSeconds(1));
        }

        [Fact]
        public void Opening_an_existing_save_leaves_its_metadata_alone()
        {
            using var root = new SavesRoot();
            SavePaths.Open("Riverbend").Touch(9);

            var reopened = SavePaths.Open("Riverbend");

            Assert.Equal(9, reopened.ReadMetadata().Turn);
        }

        [Fact]
        public void A_name_is_trimmed_on_the_way_to_disk()
        {
            using var root = new SavesRoot();

            SavePaths.Open("  Riverbend  ");

            Assert.Equal(["Riverbend"], root.Folders);
        }

        [Fact]
        public void Existence_follows_the_folder()
        {
            using var root = new SavesRoot();

            Assert.False(SavePaths.Exists("Riverbend"));
            SavePaths.Open("Riverbend");
            Assert.True(SavePaths.Exists("Riverbend"));
        }

        [Fact]
        public void An_unusable_name_never_exists()
        {
            using var root = new SavesRoot();

            Assert.False(SavePaths.Exists("../evil"));
        }

        [Fact]
        public void Folder_names_a_save_without_creating_it()
        {
            using var root = new SavesRoot();

            var folder = SavePaths.Folder("Riverbend");

            Assert.Equal(Path.Combine(Path.GetFullPath(root.Root), "Riverbend"), folder);
            Assert.False(Directory.Exists(folder));
        }

        // ---- List ---------------------------------------------------------------------------------

        [Fact]
        public void Saves_are_listed_most_recently_played_first()
        {
            using var root = new SavesRoot();

            SavePaths.Open("Older").WriteMetadata(new SaveMetadata
            {
                SchemaVersion = 2,
                Name = "Older",
                LastPlayed = DateTimeOffset.Now.AddDays(-3),
            });

            SavePaths.Open("Newer").WriteMetadata(new SaveMetadata
            {
                SchemaVersion = 2,
                Name = "Newer",
                LastPlayed = DateTimeOffset.Now,
            });

            Assert.Equal(["Newer", "Older"], SavePaths.List().Select(entry => entry.Name).ToList());
        }

        [Fact]
        public void The_folder_name_wins_over_what_the_document_claims()
        {
            // A stale Name in save.json would otherwise offer a save that cannot be reached.
            using var root = new SavesRoot();
            SavePaths.Open("Riverbend").WriteMetadata(new SaveMetadata
            {
                SchemaVersion = 2,
                Name = "Something Else",
            });

            Assert.Equal("Riverbend", Assert.Single(SavePaths.List()).Name);
        }

        [Fact]
        public void A_listed_save_reports_what_it_costs_on_disk()
        {
            using var root = new SavesRoot();
            var store = SavePaths.Open("Riverbend");
            store.WriteStory(new StoryFile());

            Assert.True(Assert.Single(SavePaths.List()).SizeBytes > 0);
        }

        // ---- Delete ---------------------------------------------------------------------------------

        [Fact]
        public void Deleting_removes_the_folder_and_everything_in_it()
        {
            using var root = new SavesRoot();
            SavePaths.Open("Riverbend").WriteStory(new StoryFile());

            Assert.True(SavePaths.Delete("Riverbend"));
            Assert.Empty(root.Folders);
        }

        [Fact]
        public void Deleting_a_save_that_is_already_gone_reports_so()
        {
            using var root = new SavesRoot();

            Assert.False(SavePaths.Delete("Riverbend"));
        }

        // ---- Rename ------------------------------------------------------------------------------------

        [Fact]
        public void Renaming_moves_the_folder_and_updates_the_document()
        {
            using var root = new SavesRoot();
            SavePaths.Open("Riverbend");

            SavePaths.Rename("Riverbend", "Stonebridge");

            Assert.Equal(["Stonebridge"], root.Folders);
            Assert.Equal("Stonebridge", SavePaths.Open("Stonebridge").ReadMetadata().Name);
        }

        [Fact]
        public void Renaming_to_the_same_name_does_nothing()
        {
            using var root = new SavesRoot();
            SavePaths.Open("Riverbend");

            SavePaths.Rename("Riverbend", "  Riverbend  ");

            Assert.Equal(["Riverbend"], root.Folders);
        }

        [Fact]
        public void Renaming_only_the_casing_works_despite_windows()
        {
            // Windows treats the two as the same folder and would refuse a direct move, so this
            // goes through a staging name. Nothing of that may survive.
            using var root = new SavesRoot();
            SavePaths.Open("riverbend");

            SavePaths.Rename("riverbend", "RIVERBEND");

            Assert.Equal(["RIVERBEND"], root.Folders);
            Assert.DoesNotContain(root.Folders, folder => folder.Contains(".renaming", StringComparison.Ordinal));
        }

        [Fact]
        public void Renaming_a_save_that_is_not_there_is_refused()
        {
            using var root = new SavesRoot();

            var exception = Assert.Throws<SaveException>(() => SavePaths.Rename("Missing", "Other"));

            Assert.Contains("no save called", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Renaming_onto_an_existing_save_is_refused()
        {
            using var root = new SavesRoot();
            SavePaths.Open("Riverbend");
            SavePaths.Open("Stonebridge");

            var exception = Assert.Throws<SaveException>(() => SavePaths.Rename("Riverbend", "Stonebridge"));

            Assert.Contains("already a save", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void Renaming_refuses_a_hostile_target()
        {
            using var root = new SavesRoot();
            SavePaths.Open("Riverbend");

            Assert.Throws<ArgumentException>(() => SavePaths.Rename("Riverbend", "../evil"));
        }

        // ---- Duplicate ------------------------------------------------------------------------------------

        [Fact]
        public void Duplicating_copies_the_documents_under_a_free_name()
        {
            using var root = new SavesRoot();
            var store = SavePaths.Open("Riverbend");
            var story = new StoryFile();
            story.Events.Add(new StoryEvent { Id = 1, Turn = 2, Title = "The ford" });
            store.WriteStory(story);

            var copy = SavePaths.Duplicate("Riverbend");

            Assert.Equal("Riverbend (copy)", copy);
            Assert.Equal("The ford", Assert.Single(SavePaths.Open(copy).ReadStory().Events).Title);
        }

        [Fact]
        public void Duplicating_takes_the_logs_along_with_the_documents()
        {
            // The logs belong to the playthrough being copied, so a duplicate carries its history and
            // continues its numbering rather than starting over. Nothing in the copy is special-cased -
            // this works because the folder is the save - so it is asserted rather than assumed, which is
            // also what would catch a future log being written somewhere outside it.
            using var root = new SavesRoot();
            var store = SavePaths.Open("Riverbend");

            var noArguments = JsonDocument.Parse("{}").RootElement;

            store.Journal.Append(new JournalEntry { Turn = 1, Tool = "get_state", Arguments = noArguments });
            store.Ledger.Append(new LedgerEntry { Turn = 1, Claim = "The ford runs high." });

            var copy = SavePaths.Open(SavePaths.Duplicate("Riverbend"));

            Assert.Equal("get_state", Assert.Single(copy.Journal.Read().Entries).Tool);
            Assert.Equal("The ford runs high.", Assert.Single(copy.Ledger.Read().Entries).Claim);
            Assert.Equal(
                2,
                copy.Journal.Append(new JournalEntry { Turn = 1, Tool = "get_story", Arguments = noArguments }));
        }

        [Fact]
        public void Duplicating_twice_numbers_the_copies()
        {
            using var root = new SavesRoot();
            SavePaths.Open("Riverbend");

            Assert.Equal("Riverbend (copy)", SavePaths.Duplicate("Riverbend"));
            Assert.Equal("Riverbend (copy 2)", SavePaths.Duplicate("Riverbend"));
        }

        [Fact]
        public void A_copy_keeps_its_place_in_the_menu_rather_than_taking_the_top()
        {
            // Duplicating a save is taking a backup; it should not become the one Continue offers.
            using var root = new SavesRoot();
            var played = DateTimeOffset.Now.AddDays(-5);
            SavePaths.Open("Riverbend").WriteMetadata(new SaveMetadata
            {
                SchemaVersion = 2,
                Name = "Riverbend",
                Created = played,
                LastPlayed = played,
                Turn = 12,
            });

            var copy = SavePaths.Duplicate("Riverbend");
            var metadata = SavePaths.Open(copy).ReadMetadata();

            Assert.Equal(played, metadata.LastPlayed);
            Assert.True(metadata.Created > played);
            Assert.Equal(copy, metadata.Name);
        }

        [Fact]
        public void A_copy_never_carries_a_half_written_document()
        {
            // A .tmp is half of a write SaveStore has not finished moving into place; copying one
            // would hand the duplicate a document that was never valid.
            using var root = new SavesRoot();
            SavePaths.Open("Riverbend");
            File.WriteAllText(Path.Combine(root.Root, "Riverbend", "story.json.tmp"), "half a write");

            var copy = SavePaths.Duplicate("Riverbend");

            Assert.False(File.Exists(Path.Combine(root.Root, copy, "story.json.tmp")));
        }

        [Fact]
        public void A_copy_of_a_long_name_stays_a_name_that_would_be_accepted()
        {
            using var root = new SavesRoot();
            var longName = new string('x', 64);
            SavePaths.Open(longName);

            var copy = SavePaths.Duplicate(longName);

            Assert.True(SavePaths.IsValidName(copy));
            Assert.EndsWith("(copy)", copy, StringComparison.Ordinal);
        }

        [Fact]
        public void Duplicating_a_save_that_is_not_there_is_refused()
        {
            using var root = new SavesRoot();

            Assert.Throws<SaveException>(() => SavePaths.Duplicate("Missing"));
        }

        // ---- Resetting ---------------------------------------------------------------------------------

        [Fact]
        public void Resetting_a_save_that_is_not_there_is_refused()
        {
            using var root = new SavesRoot();

            Assert.Throws<SaveException>(() => SavePaths.Reset("Missing"));
        }

        [Fact]
        public void Resetting_a_save_with_no_player_character_is_refused()
        {
            using var root = new SavesRoot();
            SavePaths.Open("EmptySave");

            var ex = Assert.Throws<SaveException>(() => SavePaths.Reset("EmptySave"));
            Assert.Contains("no player character", ex.Message);
        }

        [Fact]
        public void Resetting_restores_player_defined_character_and_start_location_and_clears_playthrough_data()
        {
            using var root = new SavesRoot();
            var warrior = ClassTemplates.All.Single(t => t.Name == "Warrior");
            var store = SavePaths.Open("Campaign");

            NewGame.Create(store, "Rowan", "A wandering veteran", warrior, "The Prancing Pony");

            // Simulate playthrough data
            store.Touch(15);
            store.WriteSystemPrompt("Custom instructions for narrator");

            // Damage player, modify attributes, add secret
            var characters = store.ReadCharacters();
            var player = SaveStore.Player(characters)!;
            player.Health = 10;
            player.Attributes.Add(new CharacterAttribute { Name = "Guild Standing", Score = 18 });
            player.Secrets.Add(new Secret { Turn = 5, Name = "Hidden key", Text = "The key is hidden under the floor" });

            // Add NPC
            characters.Characters.Add(new Character
            {
                Id = characters.TakeId(),
                Name = "Gorm",
                Kind = CharacterKind.Npc,
                Health = 12,
                MaxHealth = 12,
            });
            store.WriteCharacters(characters);

            // Modify inventory
            var items = store.ReadItems();
            var ring = new ItemDefinition { Id = items.TakeId(), Name = "Magic Ring" };
            items.Items.Add(ring);
            store.WriteItems(items);

            var inventory = store.ReadInventory();
            var pInv = inventory.GetOrCreate(player.Id);
            pInv.Money = 500;
            pInv.Items.Add(new ItemStack { ItemId = ring.Id, Quantity = 1 });
            store.WriteInventory(inventory);

            // Add locations
            var locations = store.ReadLocations();
            var startLoc = locations.Locations[0];
            startLoc.Description = "A busy, smoke-filled inn.";
            var secondLoc = new Location { Id = locations.TakeId(), Name = "Dark Dungeon" };
            locations.Locations.Add(secondLoc);
            store.WriteLocations(locations);
            store.MoveCharacter(player.Id, secondLoc.Id);

            // Add story events and rolls
            var story = store.ReadStory();
            story.Events.Add(new StoryEvent { Turn = 1, Title = "Arrived at inn", Detail = "The tavern was warm" });
            store.WriteStory(story);

            var rolls = store.ReadRolls();
            rolls.Rolls.Add(new DiceRoll { Turn = 1, Total = 18 });
            store.WriteRolls(rolls);

            // Append logs
            store.Journal.Append(new JournalEntry { Turn = 1, Tool = "get_state", Arguments = JsonDocument.Parse("{}").RootElement });
            store.Ledger.Append(new LedgerEntry { Turn = 1, Claim = "Hello", Speaker = "Rowan" });
            store.Transcript.Append(new TranscriptEntry { Turn = 1, Text = "The wind howled." });
            store.Diagnostics.Append(new DiagnosticEntry { Turn = 1, Finding = Finding.RecordUnwritable, Detail = "Test diagnostic" });

            // Act: Reset save
            SavePaths.Reset("Campaign");

            // Assert: Metadata
            var metadata = store.ReadMetadata();
            Assert.Equal(0, metadata.Turn);
            Assert.Equal("Campaign", metadata.Name);
            Assert.Equal(SaveStore.CurrentSchemaVersion, metadata.SchemaVersion);

            // Assert: Player character
            var resetCharacters = store.ReadCharacters();
            var resetPlayer = Assert.Single(resetCharacters.Characters);
            Assert.Equal(CharacterKind.Player, resetPlayer.Kind);
            Assert.Equal("Rowan", resetPlayer.Name);
            Assert.Equal(warrior.MaxHealth, resetPlayer.MaxHealth);
            Assert.Equal(warrior.MaxHealth, resetPlayer.Health);
            Assert.Empty(resetPlayer.Secrets);
            Assert.Equal(6, resetPlayer.Attributes.Count);
            Assert.Equal(16, resetPlayer.Attributes.Single(a => a.Name == "Strength").Score);

            // Assert: Inventory
            var resetInventory = store.ReadInventory().Find(resetPlayer.Id)!;
            var resetItems = store.ReadItems();
            Assert.Equal(warrior.StartingMoney, resetInventory.Money);
            Assert.Equal(warrior.StartingItems.Count, resetInventory.Items.Count);
            Assert.Equal("iron longsword", SaveStore.FindItemById(resetItems, resetInventory.Items[0].ItemId)!.Name);

            // Assert: Location
            var resetLocations = store.ReadLocations();
            var loc = Assert.Single(resetLocations.Locations);
            Assert.Equal("The Prancing Pony", loc.Name);
            Assert.Equal(string.Empty, loc.Description);
            Assert.Equal([resetPlayer.Id], loc.CharacterIds);

            // Assert: Story & Rolls
            Assert.Empty(store.ReadStory().Events);
            Assert.Empty(store.ReadRolls().Rolls);

            // Assert: Logs deleted
            Assert.Empty(store.Journal.Read().Entries);
            Assert.Empty(store.Ledger.Read().Entries);
            Assert.Empty(store.Transcript.Read().Entries);
            Assert.Empty(store.Diagnostics.Read().Entries);

            // Assert: Custom system prompt preserved
            Assert.Equal("Custom instructions for narrator", store.ReadSystemPrompt());
        }

        [Fact]
        public void Resetting_a_save_without_player_defined_location_clears_narrator_invented_location()
        {
            using var root = new SavesRoot();
            var mage = ClassTemplates.All.Single(t => t.Name == "Mage");
            var store = SavePaths.Open("MageCampaign");

            // Created with null starting location
            NewGame.Create(store, "Elidor", "A studious wizard", mage, null);

            // Turn 1: narrator creates loc_1
            var locations = store.ReadLocations();
            var loc1 = new Location { Id = locations.TakeId(), Name = "Old Tower", Description = "A tall tower" };
            locations.Locations.Add(loc1);
            store.WriteLocations(locations);
            var player = SaveStore.Player(store.ReadCharacters())!;
            store.MoveCharacter(player.Id, loc1.Id);

            store.Journal.Append(new JournalEntry
            {
                Turn = 1,
                Tool = "upsert_location",
                Arguments = JsonDocument.Parse("{\"name\":\"Old Tower\",\"description\":\"A tall tower\"}").RootElement,
            });

            // Act: Reset save
            SavePaths.Reset("MageCampaign");

            // Assert: Locations list should be empty so narrator can invent location anew
            var resetLocations = store.ReadLocations();
            Assert.Empty(resetLocations.Locations);
        }

        // ---- What must not keep a save out of the menu ---------------------------------------------------

        [Fact]
        public void One_corrupt_save_does_not_take_down_the_whole_menu()
        {
            // List() documents that "a folder with unreadable or missing metadata is still listed -
            // it is a save that needs looking at, not one to hide from the menu". Measure() and the
            // ReadMetadata() call beside it are both guarded to honour that: unguarded, a single
            // unparseable save.json throws SaveException out of List and the player cannot reach
            // any of their saves, including the healthy ones.
            using var root = new SavesRoot();
            SavePaths.Open("Healthy");
            var broken = root.Folder("Broken");
            File.WriteAllText(Path.Combine(broken, "save.json"), "{ not json");

            var names = SavePaths.List().Select(entry => entry.Name).Order(StringComparer.Ordinal).ToList();

            Assert.Equal(["Broken", "Healthy"], names);
        }

        [Theory]
        [InlineData("CON")]
        [InlineData("NUL")]
        [InlineData("COM1")]
        [InlineData("LPT1")]
        public void A_windows_device_name_is_refused_before_the_filesystem_refuses_it(string name)
        {
            // None of their characters is invalid, so the character check alone would pass them
            // and they would fail at Directory.CreateDirectory well after validation said yes -
            // the player told the name is fine and then handed an IO error from Open.
            Assert.False(SavePaths.IsValidName(name));
        }
    }
}
