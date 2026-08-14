using System.Text.Json;

using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The shape saves take on disk.
    /// </summary>
    /// <remarks>
    /// These are contract tests, not implementation tests. A save is meant to be opened and
    /// hand-edited, and the enum spellings are shared with the MCP tool schemas, so the wire format
    /// is part of the product rather than an internal detail.
    /// </remarks>
    public sealed class SaveJsonContextTests
    {
        private static string Write<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
            JsonSerializer.Serialize(value, typeInfo);

        // ---- Round trips -------------------------------------------------------------------

        [Fact]
        public void A_fully_populated_character_file_round_trips()
        {
            var file = new CharacterFile { NextId = 3 };
            var character = new Character
            {
                Id = "chr_1",
                Name = "Rowan",
                Kind = CharacterKind.Player,
                Health = 12,
                MaxHealth = 20,
                Description = "Weather-beaten.",
            };
            character.Attributes.Add(new CharacterAttribute { Name = "Strength", Score = 16 });
            character.Memories.Add(new Memory { Id = 1, Turn = 4, Text = "{This} met {Player}." });
            character.Memories[0].SubjectIds.Add("chr_2");
            file.Characters.Add(character);

            var json = Write(file, SaveJsonContext.Readable.CharacterFile);
            var read = JsonSerializer.Deserialize(json, SaveJsonContext.Readable.CharacterFile)!;

            var back = Assert.Single(read.Characters);
            Assert.Equal(3, read.NextId);
            Assert.Equal("chr_1", back.Id);
            Assert.Equal(CharacterKind.Player, back.Kind);
            Assert.Equal(12, back.Health);
            Assert.Equal(16, Assert.Single(back.Attributes).Score);
            Assert.Equal("{This} met {Player}.", Assert.Single(back.Memories).Text);
            Assert.Equal(["chr_2"], Assert.Single(back.Memories).SubjectIds);
        }

        [Fact]
        public void A_fully_populated_location_file_round_trips()
        {
            var file = new LocationFile { NextId = 2 };
            var location = new Location { Id = "loc_1", Name = "The Ford", Description = "Shallow." };
            location.CharacterIds.Add("chr_1");
            location.Events.Add(new LocationEvent { Id = 1, Turn = 3, Text = "The water rose." });
            file.Locations.Add(location);

            var read = JsonSerializer.Deserialize(
                Write(file, SaveJsonContext.Readable.LocationFile),
                SaveJsonContext.Readable.LocationFile)!;

            var back = Assert.Single(read.Locations);
            Assert.Equal(["chr_1"], back.CharacterIds);
            Assert.Equal("The water rose.", Assert.Single(back.Events).Text);
        }

        [Fact]
        public void A_fully_populated_inventory_file_round_trips()
        {
            var file = new InventoryFile { NextId = 2, Money = 40 };
            file.Items.Add(new Item { Id = "itm_1", Name = "Rope", Quantity = 2, Description = "Hemp." });

            var read = JsonSerializer.Deserialize(
                Write(file, SaveJsonContext.Readable.InventoryFile),
                SaveJsonContext.Readable.InventoryFile)!;

            Assert.Equal(40, read.Money);
            Assert.Equal(2, Assert.Single(read.Items).Quantity);
        }

        [Fact]
        public void A_fully_populated_story_file_round_trips()
        {
            var file = new StoryFile();
            var entry = new StoryEvent { Id = 1, Turn = 6, Title = "The ford", Detail = "Crossed at dusk." };
            entry.Tags.Add("travel");
            file.Events.Add(entry);

            var read = JsonSerializer.Deserialize(
                Write(file, SaveJsonContext.Readable.StoryFile),
                SaveJsonContext.Readable.StoryFile)!;

            Assert.Equal(["travel"], Assert.Single(read.Events).Tags);
        }

        [Fact]
        public void A_fully_populated_roll_file_round_trips()
        {
            var file = new RollFile();
            var roll = new DiceRoll
            {
                Id = 1,
                Turn = 6,
                CharacterId = "chr_1",
                Reason = "Forcing the door",
                Attribute = "Strength",
                Modifier = 3,
                Notation = "1d20+3",
                Total = 17,
                Hidden = true,
                Revealed = false,
            };
            roll.Faces.Add(14);
            file.Rolls.Add(roll);

            var read = JsonSerializer.Deserialize(
                Write(file, SaveJsonContext.Readable.RollFile),
                SaveJsonContext.Readable.RollFile)!;

            var back = Assert.Single(read.Rolls);
            Assert.Equal([14], back.Faces);
            Assert.True(back.Hidden);
            Assert.False(back.Revealed);
            Assert.Equal("Strength", back.Attribute);
        }

        [Fact]
        public void Metadata_round_trips_including_its_stamps()
        {
            var metadata = new SaveMetadata
            {
                SchemaVersion = 2,
                Name = "Riverbend",
                Created = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(1)),
                LastPlayed = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.FromHours(1)),
                Turn = 12,
            };

            var read = JsonSerializer.Deserialize(
                Write(metadata, SaveJsonContext.Readable.SaveMetadata),
                SaveJsonContext.Readable.SaveMetadata)!;

            Assert.Equal(metadata.Created, read.Created);
            Assert.Equal(metadata.LastPlayed, read.LastPlayed);
            Assert.Equal(12, read.Turn);
        }

        // ---- Wire format -------------------------------------------------------------------

        [Fact]
        public void Property_names_are_camel_case()
        {
            var file = new CharacterFile { NextId = 3 };
            file.Characters.Add(new Character { Id = "chr_1", Name = "Rowan", MaxHealth = 20 });

            var json = Write(file, SaveJsonContext.Readable.CharacterFile);

            Assert.Contains("\"nextId\"", json, StringComparison.Ordinal);
            Assert.Contains("\"maxHealth\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\"NextId\"", json, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(true, "player")]
        [InlineData(false, "npc")]
        public void Character_kind_is_written_in_the_spelling_the_tool_schema_uses(
            bool isPlayer,
            string expected)
        {
            var kind = isPlayer ? CharacterKind.Player : CharacterKind.Npc;

            // Pinned with [JsonStringEnumMemberName] precisely because the member-name default
            // would say "Player" and diverge from what the MCP tools advertise. A drift here is a
            // cross-boundary bug: the narrator would send a value the save layer cannot read.
            var file = new CharacterFile();
            file.Characters.Add(new Character { Id = "chr_1", Name = "Rowan", Kind = kind });

            var json = Write(file, SaveJsonContext.Readable.CharacterFile);

            Assert.Contains($"\"kind\": \"{expected}\"", json, StringComparison.Ordinal);
        }

        [Fact]
        public void An_npc_is_what_an_unstated_kind_means()
        {
            var read = JsonSerializer.Deserialize(
                """{"characters":[{"id":"chr_1","name":"Ash"}]}""",
                SaveJsonContext.Readable.CharacterFile)!;

            Assert.Equal(CharacterKind.Npc, Assert.Single(read.Characters).Kind);
        }

        [Fact]
        public void An_apostrophe_survives_unescaped()
        {
            // The whole reason the Readable context exists: a save is meant to be opened and read
            // by a person, and ' everywhere makes that unpleasant.
            var file = new CharacterFile();
            file.Characters.Add(new Character { Id = "chr_1", Name = "Rowan", Description = "A smith's apprentice." });

            var json = Write(file, SaveJsonContext.Readable.CharacterFile);

            Assert.Contains("A smith's apprentice.", json, StringComparison.Ordinal);
            Assert.DoesNotContain("\\u0027", json, StringComparison.Ordinal);
        }

        [Fact]
        public void The_document_is_written_for_a_person_to_read()
        {
            var file = new CharacterFile();
            file.Characters.Add(new Character { Id = "chr_1", Name = "Rowan" });

            Assert.Contains('\n', Write(file, SaveJsonContext.Readable.CharacterFile));
        }

        [Fact]
        public void Serialising_twice_produces_the_same_bytes()
        {
            var file = new CharacterFile { NextId = 2 };
            file.Characters.Add(new Character { Id = "chr_1", Name = "Rowan" });

            Assert.Equal(
                Write(file, SaveJsonContext.Readable.CharacterFile),
                Write(file, SaveJsonContext.Readable.CharacterFile));
        }

        // ---- Tolerance for hand-edited files -------------------------------------------------

        [Fact]
        public void An_unknown_property_does_not_stop_a_save_loading()
        {
            var read = JsonSerializer.Deserialize(
                """{"characters":[],"nextId":4,"somethingFromALaterBuild":true}""",
                SaveJsonContext.Readable.CharacterFile)!;

            Assert.Equal(4, read.NextId);
        }

        [Fact]
        public void A_missing_counter_lands_at_zero()
        {
            var read = JsonSerializer.Deserialize(
                """{"characters":[]}""",
                SaveJsonContext.Readable.CharacterFile)!;

            Assert.Equal(0, read.NextId);
        }

        [Fact]
        public void An_empty_document_reads_as_an_empty_world()
        {
            var read = JsonSerializer.Deserialize("{}", SaveJsonContext.Readable.CharacterFile)!;

            Assert.NotNull(read.Characters);
            Assert.Empty(read.Characters);
        }

        [Fact]
        public void An_unreadable_document_is_a_json_exception_for_the_store_to_wrap()
        {
            Assert.Throws<JsonException>(
                () => JsonSerializer.Deserialize("{ not json", SaveJsonContext.Readable.CharacterFile));
        }

        // ---- Ids are never invented ------------------------------------------------------------

        [Fact]
        public void Taking_an_id_never_reissues_one_already_in_use()
        {
            var file = new CharacterFile { NextId = 0 };
            file.Characters.Add(new Character { Id = "chr_5", Name = "Rowan" });

            Assert.Equal("chr_6", file.TakeId());
        }

        [Fact]
        public void Taking_an_id_respects_a_counter_that_leads_the_records()
        {
            // Deleting a character must never free their id for reuse: something may still point
            // at it, and reissuing would silently merge two entities.
            var file = new CharacterFile { NextId = 100 };

            Assert.Equal("chr_101", file.TakeId());
        }

        [Fact]
        public void Successive_ids_do_not_repeat()
        {
            var file = new LocationFile();

            var ids = Enumerable.Range(0, 5).Select(_ => file.TakeId()).ToList();

            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
            Assert.All(ids, id => Assert.True(EntityIds.IsWellFormed(id, EntityIds.Location)));
        }

        [Fact]
        public void Each_document_stamps_its_own_prefix()
        {
            Assert.StartsWith(EntityIds.Character, new CharacterFile().TakeId(), StringComparison.Ordinal);
            Assert.StartsWith(EntityIds.Location, new LocationFile().TakeId(), StringComparison.Ordinal);
            Assert.StartsWith(EntityIds.Item, new InventoryFile().TakeId(), StringComparison.Ordinal);
        }

        [Fact]
        public void A_freshly_written_save_survives_a_full_trip_through_disk()
        {
            using var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", "A quiet sort.", ClassTemplates.All[0], "The Ford");

            // Re-read through a second store over the same folder: nothing is cached, so this is
            // what another process sees.
            var reader = new SaveStore(save.Directory);

            Assert.Equal("Rowan", SaveStore.PlayerName(reader.ReadCharacters()));
            Assert.Equal("The Ford", Assert.Single(reader.ReadLocations().Locations).Name);
        }
    }
}
