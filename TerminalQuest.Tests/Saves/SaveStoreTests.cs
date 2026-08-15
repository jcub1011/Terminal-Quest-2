using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// Read and write access to one save folder. Two processes use this at once — the TUI reads to
    /// refresh the status pane while the MCP server writes on the narrator's behalf — so the
    /// tear-free write and the tolerant read are the contract, not an implementation detail.
    /// </summary>
    public sealed class SaveStoreTests
    {
        // ---- Construction ----------------------------------------------------------------

        [Fact]
        public void The_directory_is_made_absolute()
        {
            var store = new SaveStore("relative-save");

            Assert.True(Path.IsPathFullyQualified(store.Directory));
        }

        [Fact]
        public void The_name_is_the_folder_name()
        {
            using var save = new TempSave("Riverbend");

            Assert.Equal("Riverbend", save.Store.Name);
        }

        [Theory]
        [InlineData("")]
        public void An_empty_directory_is_a_programming_error(string directory)
        {
            Assert.Throws<ArgumentException>(() => new SaveStore(directory));
        }

        [Fact]
        public void A_null_directory_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(() => new SaveStore(null!));
        }

        // ---- Reading -----------------------------------------------------------------------

        [Fact]
        public void A_missing_document_reads_as_an_empty_one()
        {
            // A freshly created save needs no seeding.
            using var save = new TempSave();

            Assert.Empty(save.Store.ReadCharacters().Characters);
            Assert.Empty(save.Store.ReadLocations().Locations);
            Assert.Empty(save.Store.ReadItems().Items);
            Assert.Empty(save.Store.ReadInventory().Inventories);
            Assert.Empty(save.Store.ReadStory().Events);
            Assert.Empty(save.Store.ReadRolls().Rolls);
            Assert.Equal(0, save.Store.ReadMetadata().SchemaVersion);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\t")]
        public void A_blank_document_reads_as_absent_rather_than_as_corruption(string contents)
        {
            // What a crash mid-write used to leave behind. The player should not have to repair it.
            using var save = new TempSave();
            save.WriteRaw("characters.json", contents);

            Assert.Empty(save.Store.ReadCharacters().Characters);
        }

        [Fact]
        public void A_malformed_document_is_a_real_problem_and_says_so()
        {
            // Silently replacing it with an empty file would destroy a playthrough.
            using var save = new TempSave();
            save.WriteRaw("characters.json", "{ this is not json");

            var exception = Assert.Throws<SaveException>(() => save.Store.ReadCharacters());

            Assert.Contains("characters.json", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_document_holding_only_null_reads_as_an_empty_one()
        {
            using var save = new TempSave();
            save.WriteRaw("characters.json", "null");

            Assert.Empty(save.Store.ReadCharacters().Characters);
        }

        // ---- Writing -----------------------------------------------------------------------

        [Fact]
        public void What_is_written_is_what_comes_back()
        {
            using var save = new TempSave();

            var file = new CharacterFile { NextId = 4 };
            file.Characters.Add(new Character
            {
                Id = "chr_1",
                Name = "Rowan",
                Kind = CharacterKind.Player,
                Health = 12,
                MaxHealth = 20,
                Description = "Weather-beaten.",
            });

            save.Store.WriteCharacters(file);
            var read = save.Store.ReadCharacters();

            var character = Assert.Single(read.Characters);
            Assert.Equal(4, read.NextId);
            Assert.Equal("chr_1", character.Id);
            Assert.Equal("Rowan", character.Name);
            Assert.Equal(CharacterKind.Player, character.Kind);
            Assert.Equal(12, character.Health);
            Assert.Equal(20, character.MaxHealth);
            Assert.Equal("Weather-beaten.", character.Description);
        }

        [Fact]
        public void A_write_leaves_no_temporary_file_behind()
        {
            // The temporary is half of a write that has not been moved into place. One left in the
            // folder would be copied by Duplicate and read as a document by nothing.
            using var save = new TempSave();

            save.Store.WriteCharacters(new CharacterFile());

            Assert.Empty(save.TempFiles);
        }

        [Fact]
        public void A_stale_temporary_from_an_earlier_crash_is_overwritten()
        {
            using var save = new TempSave();
            save.WriteRaw("characters.json.tmp", "garbage from a crash");

            save.Store.WriteCharacters(new CharacterFile());

            Assert.Empty(save.TempFiles);
        }

        [Fact]
        public void Writing_twice_replaces_rather_than_appends()
        {
            using var save = new TempSave();

            var first = new CharacterFile();
            first.Characters.Add(new Character { Id = "chr_1", Name = "Rowan" });
            save.Store.WriteCharacters(first);

            save.Store.WriteCharacters(new CharacterFile());

            Assert.Empty(save.Store.ReadCharacters().Characters);
        }

        [Fact]
        public void A_write_creates_the_folder_when_it_is_missing()
        {
            using var save = new TempSave();
            var nested = Path.Combine(save.Directory, "nested");
            var store = new SaveStore(nested);

            store.WriteStory(new StoryFile());

            Assert.True(Directory.Exists(nested));
        }

        [Fact]
        public void Every_document_round_trips()
        {
            using var save = new TempSave();

            var locations = new LocationFile { NextId = 2 };
            locations.Locations.Add(new Location { Id = "loc_1", Name = "The Ford" });
            save.Store.WriteLocations(locations);

            var items = new ItemFile { NextId = 2 };
            items.Items.Add(new ItemDefinition { Id = "itm_1", Name = "Rope", Description = "Hemp." });
            save.Store.WriteItems(items);

            var inventory = new InventoryFile();
            var charInv = new CharacterInventory { CharacterId = "chr_1", Money = 40 };
            charInv.Items.Add(new ItemStack { ItemId = "itm_1", Quantity = 2 });
            inventory.Inventories.Add(charInv);
            save.Store.WriteInventory(inventory);

            var story = new StoryFile();
            story.Events.Add(new StoryEvent { Id = 1, Turn = 5, Title = "The ford", Detail = "Crossed." });
            save.Store.WriteStory(story);

            var rolls = new RollFile();
            rolls.Rolls.Add(new DiceRoll { Id = 1, Turn = 5, Notation = "1d20", Total = 14 });
            save.Store.WriteRolls(rolls);

            Assert.Equal("The Ford", Assert.Single(save.Store.ReadLocations().Locations).Name);
            Assert.Equal(40, save.Store.ReadInventory().Find("chr_1")!.Money);
            Assert.Equal("The ford", Assert.Single(save.Store.ReadStory().Events).Title);
            Assert.Equal(14, Assert.Single(save.Store.ReadRolls().Rolls).Total);
        }

        // ---- Schema gate --------------------------------------------------------------------

        [Fact]
        public void A_current_save_is_playable()
        {
            using var save = new TempSave();
            save.Store.WriteMetadata(new SaveMetadata { SchemaVersion = SaveStore.CurrentSchemaVersion });

            save.Store.RequireSupportedSchema();
        }

        [Fact]
        public void An_empty_save_is_adopted_whatever_its_metadata_says()
        {
            // There is no playthrough in it to lose, and an empty folder is what both the save menu
            // and an older build leave behind.
            using var save = new TempSave();

            save.Store.RequireSupportedSchema();
        }

        [Fact]
        public void An_unversioned_save_with_characters_in_it_is_refused()
        {
            using var save = new TempSave();
            var file = new CharacterFile();
            file.Characters.Add(new Character { Id = "chr_1", Name = "Rowan" });
            save.Store.WriteCharacters(file);

            var exception = Assert.Throws<SaveException>(() => save.Store.RequireSupportedSchema());

            Assert.Contains("stable identifiers", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_version_one_save_is_refused_rather_than_converted()
        {
            using var save = new TempSave();
            save.Store.WriteMetadata(new SaveMetadata { SchemaVersion = 1 });

            var exception = Assert.Throws<SaveException>(() => save.Store.RequireSupportedSchema());

            Assert.Contains("not converted", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void A_save_from_a_newer_build_is_refused_with_its_own_message()
        {
            // A narrator pointed at a save this build misreads would see a half-empty world and
            // cheerfully write a new one on top of it, so the two directions read differently.
            using var save = new TempSave();
            save.Store.WriteMetadata(new SaveMetadata { SchemaVersion = SaveStore.CurrentSchemaVersion + 1 });

            var exception = Assert.Throws<SaveException>(() => save.Store.RequireSupportedSchema());

            Assert.Contains("newer version", exception.Message, StringComparison.Ordinal);
        }

        // ---- Touch ---------------------------------------------------------------------------

        [Fact]
        public void Touch_records_the_turn_reached()
        {
            using var save = new TempSave();

            save.Store.Touch(7);

            Assert.Equal(7, save.Store.CurrentTurn());
        }

        [Fact]
        public void Touch_stamps_the_time_and_repairs_a_blank_name()
        {
            using var save = new TempSave("Riverbend");
            var before = DateTimeOffset.Now.AddSeconds(-1);

            save.Store.Touch(1);

            var metadata = save.Store.ReadMetadata();
            Assert.Equal("Riverbend", metadata.Name);
            Assert.InRange(metadata.LastPlayed, before, DateTimeOffset.Now.AddSeconds(1));
            Assert.InRange(metadata.Created, before, DateTimeOffset.Now.AddSeconds(1));
        }

        [Fact]
        public void Touch_leaves_an_existing_creation_stamp_alone()
        {
            using var save = new TempSave();
            var created = DateTimeOffset.Now.AddDays(-30);
            save.Store.WriteMetadata(new SaveMetadata { Name = "Riverbend", Created = created });

            save.Store.Touch(2);

            Assert.Equal(created, save.Store.ReadMetadata().Created);
        }

        [Fact]
        public void Touch_never_promotes_an_old_save_to_the_current_schema()
        {
            // Filling the version in here would quietly promote a genuine old save on the first
            // turn, which is the exact thing RequireSupportedSchema exists to stop.
            using var save = new TempSave();
            save.Store.WriteMetadata(new SaveMetadata { SchemaVersion = 1, Name = "Riverbend" });

            save.Store.Touch(3);

            Assert.Equal(1, save.Store.ReadMetadata().SchemaVersion);
        }

        // ---- Moving ---------------------------------------------------------------------------

        [Fact]
        public void Moving_a_character_puts_them_in_one_place_only()
        {
            using var save = new TempSave();
            var file = new LocationFile();
            var ford = new Location { Id = "loc_1", Name = "The Ford" };
            ford.CharacterIds.Add("chr_1");
            file.Locations.Add(ford);
            file.Locations.Add(new Location { Id = "loc_2", Name = "The Mill" });
            save.Store.WriteLocations(file);

            Assert.True(save.Store.MoveCharacter("chr_1", "loc_2"));

            var read = save.Store.ReadLocations();
            Assert.Empty(SaveStore.FindLocationById(read, "loc_1")!.CharacterIds);
            Assert.Equal(["chr_1"], SaveStore.FindLocationById(read, "loc_2")!.CharacterIds);
        }

        [Fact]
        public void Moving_to_an_unknown_destination_changes_nothing()
        {
            using var save = new TempSave();
            var file = new LocationFile();
            var ford = new Location { Id = "loc_1", Name = "The Ford" };
            ford.CharacterIds.Add("chr_1");
            file.Locations.Add(ford);
            save.Store.WriteLocations(file);

            var before = save.ReadRaw("locations.json");

            Assert.False(save.Store.MoveCharacter("chr_1", "loc_9"));
            Assert.Equal(before, save.ReadRaw("locations.json"));
        }

        [Fact]
        public void Moving_somebody_to_where_they_already_are_leaves_them_there_once()
        {
            using var save = new TempSave();
            var file = new LocationFile();
            var ford = new Location { Id = "loc_1", Name = "The Ford" };
            ford.CharacterIds.Add("chr_1");
            file.Locations.Add(ford);
            save.Store.WriteLocations(file);

            Assert.True(save.Store.MoveCharacter("chr_1", "loc_1"));

            Assert.Equal(
                ["chr_1"],
                SaveStore.FindLocationById(save.Store.ReadLocations(), "loc_1")!.CharacterIds);
        }

        [Fact]
        public void A_duplicated_presence_entry_is_collapsed_by_a_move()
        {
            using var save = new TempSave();
            var file = new LocationFile();
            var ford = new Location { Id = "loc_1", Name = "The Ford" };
            ford.CharacterIds.Add("chr_1");
            ford.CharacterIds.Add("chr_1");
            file.Locations.Add(ford);
            save.Store.WriteLocations(file);

            save.Store.MoveCharacter("chr_1", "loc_1");

            Assert.Equal(
                ["chr_1"],
                SaveStore.FindLocationById(save.Store.ReadLocations(), "loc_1")!.CharacterIds);
        }

        [Fact]
        public void Moving_nobody_is_a_programming_error()
        {
            using var save = new TempSave();

            Assert.Throws<ArgumentException>(() => save.Store.MoveCharacter(string.Empty, "loc_1"));
        }

        // ---- Lookups ---------------------------------------------------------------------------

        [Fact]
        public void Characters_are_found_by_name_case_insensitively()
        {
            var file = new CharacterFile();
            file.Characters.Add(new Character { Id = "chr_1", Name = "Rowan" });

            Assert.Equal("chr_1", SaveStore.FindCharacter(file, "  rowan ")?.Id);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("Nobody")]
        public void An_unknown_name_finds_nothing(string? name)
        {
            var file = new CharacterFile();
            file.Characters.Add(new Character { Id = "chr_1", Name = "Rowan" });

            Assert.Null(SaveStore.FindCharacter(file, name));
        }

        [Fact]
        public void Ids_are_matched_exactly()
        {
            var file = new CharacterFile();
            file.Characters.Add(new Character { Id = "chr_1", Name = "Rowan" });

            Assert.NotNull(SaveStore.FindCharacterById(file, "chr_1"));
            Assert.Null(SaveStore.FindCharacterById(file, "CHR_1"));
        }

        [Fact]
        public void The_player_is_the_one_marked_as_such()
        {
            var file = new CharacterFile();
            file.Characters.Add(new Character { Id = "chr_1", Name = "Ash", Kind = CharacterKind.Npc });
            file.Characters.Add(new Character { Id = "chr_2", Name = "Rowan", Kind = CharacterKind.Player });

            Assert.Equal("chr_2", SaveStore.Player(file)?.Id);
            Assert.Equal("Rowan", SaveStore.PlayerName(file));
        }

        [Fact]
        public void A_world_with_no_player_has_no_player_name()
        {
            Assert.Null(SaveStore.PlayerName(new CharacterFile()));
        }

        [Fact]
        public void Where_is_finds_the_location_holding_a_character()
        {
            var file = new LocationFile();
            var ford = new Location { Id = "loc_1", Name = "The Ford" };
            ford.CharacterIds.Add("chr_1");
            file.Locations.Add(ford);

            Assert.Equal("loc_1", SaveStore.WhereIs(file, "chr_1")?.Id);
            Assert.Null(SaveStore.WhereIs(file, "chr_2"));
            Assert.Null(SaveStore.WhereIs(file, null));
        }

        [Fact]
        public void Next_id_starts_at_one_and_never_reuses()
        {
            Assert.Equal(1, SaveStore.NextId(new List<StoryEvent>(), e => e.Id));

            var events = new List<StoryEvent> { new() { Id = 1 }, new() { Id = 7 } };
            Assert.Equal(8, SaveStore.NextId(events, e => e.Id));
        }

        [Theory]
        [InlineData("Rowan", "rowan", true)]
        [InlineData("  Rowan  ", "Rowan", true)]
        [InlineData("Rowan", "Ash", false)]
        [InlineData(null, null, true)]
        [InlineData(null, "", false)]
        [InlineData("", "", true)]
        public void Matches_is_the_trimmed_case_insensitive_rule(string? left, string? right, bool expected)
        {
            Assert.Equal(expected, SaveStore.Matches(left, right));
        }

        // ---- Text the encoder cannot represent ---------------------------------------------------

        [Fact]
        public void Unrepresentable_text_is_sanitised_rather_than_failing_the_write()
        {
            // Worth pinning because the surrounding code reads as though it could fail here:
            // JsonSerializer.Serialize sits inside Write's try block, but JsonException is absent
            // from its `when` filter, so a serializer that threw would escape as a raw
            // JsonException past every caller's SaveException handling.
            //
            // It does not throw. The relaxed encoder replaces an unpaired surrogate with U+FFFD,
            // so the write succeeds and the save stays readable. The gap in the filter is
            // therefore unreachable for these documents rather than a live defect — but if the
            // serializer is ever swapped or a converter added, this test is what notices.
            using var save = new TempSave();

            var file = new CharacterFile();
            file.Characters.Add(new Character { Id = "chr_1", Name = "Rowan \ud800 unpaired" });

            save.Store.WriteCharacters(file);

            var name = Assert.Single(save.Store.ReadCharacters().Characters).Name;
            Assert.DoesNotContain('\ud800', name);
            Assert.Contains("Rowan", name, StringComparison.Ordinal);
            Assert.Empty(save.TempFiles);
        }
    }
}
