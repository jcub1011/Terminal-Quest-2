using TerminalQuest.Mcp;
using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    /// <summary>
    /// The plain text a tool call hands back to the narrator.
    /// </summary>
    public sealed class QuestRenderTests
    {
        private static Character Rowan() => new()
        {
            Id = "chr_1",
            Name = "Rowan",
            Kind = CharacterKind.Player,
            Health = 12,
            MaxHealth = 20,
        };

        [Fact]
        public void A_character_line_reads_as_a_sentence_a_model_can_parse()
        {
            Assert.Equal("Rowan (chr_1, player) - HP 12/20", QuestRender.CharacterLine(Rowan()));
        }

        [Fact]
        public void An_npc_is_marked_as_one()
        {
            var bess = Rowan();
            bess.Name = "Bess";
            bess.Kind = CharacterKind.Npc;

            Assert.StartsWith("Bess (chr_1, npc)", QuestRender.CharacterLine(bess), StringComparison.Ordinal);
        }

        [Fact]
        public void The_rendered_kind_matches_the_wire_spelling()
        {
            Assert.Equal("player", QuestRender.Kind(CharacterKind.Player));
            Assert.Equal("npc", QuestRender.Kind(CharacterKind.Npc));
        }

        [Fact]
        public void Attributes_show_the_modifier_beside_the_score()
        {
            var character = Rowan();
            CharacterAttributes.Set(character, "Strength", 16);

            var line = QuestRender.Attributes(character);

            Assert.Contains("Strength 16 (+3)", line, StringComparison.Ordinal);
        }

        [Fact]
        public void Attributes_are_spelled_out_in_full()
        {
            var line = QuestRender.Attributes(Rowan());

            Assert.Contains("Constitution", line, StringComparison.Ordinal);
            Assert.DoesNotContain("CON ", line, StringComparison.Ordinal);
        }

        [Fact]
        public void Every_character_shows_all_six_even_when_the_save_mentions_none()
        {
            var line = QuestRender.Attributes(Rowan());

            Assert.All(
                CharacterAttributes.Core,
                name => Assert.Contains(name, line, StringComparison.Ordinal));
        }

        [Fact]
        public void A_full_character_includes_inventory()
        {
            var character = Rowan();
            var itemFile = new ItemFile();
            var itemDef = new ItemDefinition { Id = "itm_1", Name = "dagger", Description = "Sharp iron." };
            itemFile.Items.Add(itemDef);

            var inv = new CharacterInventory { CharacterId = character.Id, Money = 15 };
            inv.Items.Add(new ItemStack { ItemId = "itm_1", Quantity = 1 });

            var text = QuestRender.Character(character, inv, itemFile);

            Assert.Contains("Rowan (chr_1, player)", text, StringComparison.Ordinal);
            Assert.Contains("Money: 15 coin.", text, StringComparison.Ordinal);
            Assert.Contains("dagger (itm_1) x1 - Sharp iron.", text, StringComparison.Ordinal);
        }

        // ---- Rolls -----------------------------------------------------------------------------

        [Fact]
        public void A_roll_reads_back_with_its_faces_and_total()
        {
            var roll = new DiceRoll
            {
                Notation = "1d20",
                Reason = "Forcing the door",
                Total = 14,
            };
            roll.Faces.Add(14);

            Assert.Equal(
                "Rowan rolled 1d20 for Forcing the door: [14] = 14.",
                QuestRender.Roll(roll, "Rowan"));
        }

        [Fact]
        public void A_roll_names_the_attribute_that_supplied_the_modifier()
        {
            var roll = new DiceRoll
            {
                Notation = "1d20",
                Reason = "Forcing the door",
                Attribute = "Strength",
                Modifier = 3,
                Total = 17,
            };
            roll.Faces.Add(14);

            Assert.Contains("+3 Strength = 17", QuestRender.Roll(roll, "Rowan"), StringComparison.Ordinal);
        }

        [Fact]
        public void A_roll_names_situational_modifiers()
        {
            var roll = new DiceRoll
            {
                Notation = "1d20",
                Reason = "Being obnoxious",
                Attribute = "Charisma",
                Modifier = 1,
                SituationalModifier = -5,
                Total = 10,
            };
            roll.Faces.Add(14);

            Assert.Equal(
                "Rowan rolled 1d20 for Being obnoxious: [14] +1 Charisma -5 situational = 10.",
                QuestRender.Roll(roll, "Rowan"));
        }

        [Fact]
        public void A_roll_with_nobody_behind_it_still_reads()
        {
            var roll = new DiceRoll { Notation = "1d6", Reason = "The weather", Total = 3 };

            Assert.StartsWith("Something rolled", QuestRender.Roll(roll, null), StringComparison.Ordinal);
        }

        [Fact]
        public void The_narrator_sees_the_total_of_a_hidden_roll()
        {
            var roll = new DiceRoll
            {
                Notation = "1d20",
                Reason = "Sensing the lie",
                Total = 18,
                Hidden = true,
            };
            roll.Faces.Add(18);

            Assert.Contains("= 18.", QuestRender.Roll(roll, "Rowan"), StringComparison.Ordinal);
        }

        // ---- Locations --------------------------------------------------------------------------

        [Fact]
        public void A_location_line_names_who_is_present()
        {
            var characters = new CharacterFile();
            characters.Characters.Add(new Character { Id = "chr_1", Name = "Rowan" });
            var location = new Location { Id = "loc_1", Name = "The Ford" };
            location.CharacterIds.Add("chr_1");

            var line = QuestRender.LocationLine(location, WorldIndex.Build(characters));

            Assert.Equal("The Ford (loc_1) (Rowan)", line);
        }

        [Fact]
        public void An_empty_location_says_nobody_is_there()
        {
            var location = new Location { Id = "loc_1", Name = "The Ford" };

            Assert.Equal("The Ford (loc_1) (nobody here)", QuestRender.LocationLine(location, WorldIndex.Build()));
        }

        [Fact]
        public void A_roster_never_shows_an_id()
        {
            var location = new Location { Id = "loc_1", Name = "The Ford" };
            location.CharacterIds.Add("chr_9");

            var line = QuestRender.LocationLine(location, WorldIndex.Build());

            Assert.Equal("The Ford (loc_1) (nobody here)", line);
            Assert.DoesNotContain("chr_9", line, StringComparison.Ordinal);
        }

        [Fact]
        public void A_full_location_resolves_its_history_and_items()
        {
            var location = new Location { Id = "loc_1", Name = "The Ford", Description = "Shallow." };
            var itemFile = new ItemFile();
            var itemDef = new ItemDefinition { Id = "itm_1", Name = "lantern", Description = "Tin." };
            itemFile.Items.Add(itemDef);
            location.Items.Add(new ItemStack { ItemId = "itm_1", Quantity = 1 });

            var recentEvents = new List<StoryEvent>
            {
                new() { Turn = 3, Title = "The flood", Detail = "The river rose." }
            };

            var text = QuestRender.Location(location, WorldIndex.Build(), itemFile, recentEvents);

            Assert.Contains("The Ford (loc_1)", text, StringComparison.Ordinal);
            Assert.Contains("lantern (itm_1) x1 - Tin.", text, StringComparison.Ordinal);
            Assert.Contains("[turn 3] The flood - The river rose.", text, StringComparison.Ordinal);
        }

        // ---- Odds and ends -----------------------------------------------------------------------

        [Fact]
        public void Nought_coin_reads_as_a_fact_rather_than_a_missing_value()
        {
            Assert.Equal("Money: none.", QuestRender.Money(0));
            Assert.Equal("Money: 12 coin.", QuestRender.Money(12));
        }

        [Fact]
        public void An_item_shows_its_description_when_it_has_one()
        {
            Assert.Equal(
                "  rope x2 - Hemp, knotted.",
                QuestRender.Item(new ItemDefinition { Name = "rope", Description = "Hemp, knotted." }, 2));

            Assert.Equal(
                "  rope x2",
                QuestRender.Item(new ItemDefinition { Name = "rope" }, 2));
        }

        [Fact]
        public void A_story_event_shows_its_detail_when_it_has_one()
        {
            Assert.Equal(
                "  [turn 6] The ford - Crossed at dusk.",
                QuestRender.StoryEvent(new StoryEvent { Turn = 6, Title = "The ford", Detail = "Crossed at dusk." }));

            Assert.Equal(
                "  [turn 6] The ford",
                QuestRender.StoryEvent(new StoryEvent { Turn = 6, Title = "The ford" }));
        }
    }
}
