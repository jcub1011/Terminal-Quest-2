using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    /// <summary>
    /// The tools the narrator writes the world through.
    /// </summary>
    /// <remarks>
    /// Every argument here arrives from a language model, so the coercion and refusal paths are as
    /// important as the happy ones — a refused call costs a turn, and a wrongly accepted one writes
    /// nonsense into a save the player cannot easily repair.
    /// <para>
    /// <c>roll</c> and the word banks use <see cref="Random.Shared"/>, so those assertions are on
    /// shape and range rather than on values.
    /// </para>
    /// </remarks>
    public sealed class QuestToolsTests
    {
        private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static ToolOutcome Call(SaveStore store, string name, string arguments = "{}") =>
            QuestTools.Invoke(store, name, Args(arguments));

        private static TempSave Seeded(string startLocation = "The Ford")
        {
            var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", "A quiet sort.", ClassTemplates.All[0], startLocation);
            return save;
        }

        // ---- Reading the world --------------------------------------------------------------

        [Fact]
        public void Get_state_describes_the_player_and_where_they_are()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, "get_state");

            Assert.False(outcome.IsError);
            Assert.Contains("Rowan", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("The Ford", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Get_state_on_an_empty_save_refuses_to_invent_a_player()
        {
            // A save that reaches here has lost its roster; inviting the narrator to quietly
            // replace whoever used to be there would be worse than saying so.
            using var save = new TempSave();

            var outcome = Call(save.Store, "get_state");

            Assert.Contains("no player on record", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void No_tool_output_ever_leaks_an_entity_id()
        {
            // The one rule the whole identity scheme rests on: an id never leaves the save layer.
            // The narrator cannot do anything with "chr_1", and echoing one back teaches it to.
            using var save = Seeded();
            Call(save.Store, "add_memory", """{"character":"Rowan","text":"{This} crossed the ford."}""");
            Call(save.Store, "record_event", """{"title":"The ford","detail":"Crossed at dusk."}""");

            foreach (var name in new[]
            {
                "get_state", "list_characters", "list_locations", "get_inventory", "get_story",
            })
            {
                var text = Call(save.Store, name).Text;

                Assert.DoesNotContain(EntityIds.Character, text, StringComparison.Ordinal);
                Assert.DoesNotContain(EntityIds.Location, text, StringComparison.Ordinal);
                Assert.DoesNotContain(EntityIds.Item, text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Get_character_reads_one_in_full()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, "get_character", """{"name":"Rowan"}""");

            Assert.False(outcome.IsError);
            Assert.Contains("Rowan", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Get_character_says_so_when_nobody_answers_to_the_name()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, "get_character", """{"name":"Bess"}""");

            Assert.True(outcome.IsError);
            Assert.Contains("Bess", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_required_argument_that_is_missing_is_refused_rather_than_guessed()
        {
            using var save = Seeded();

            Assert.True(Call(save.Store, "get_character").IsError);
        }

        // ---- Writing characters ----------------------------------------------------------------

        [Fact]
        public void Upserting_creates_a_character()
        {
            using var save = Seeded();

            var outcome = Call(
                save.Store,
                "upsert_character",
                """{"name":"Bess","description":"The ferrywoman.","maxHealth":14}""");

            Assert.False(outcome.IsError);
            var bess = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess");
            Assert.NotNull(bess);
            Assert.Equal(14, bess.MaxHealth);
            Assert.Equal(CharacterKind.Npc, bess.Kind);
        }

        [Fact]
        public void Upserting_an_existing_character_keeps_their_id()
        {
            // The id is what memories and rosters point at; reissuing one would strand them.
            using var save = Seeded();
            var before = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!.Id;

            Call(save.Store, "upsert_character", """{"name":"Rowan","description":"Changed."}""");

            Assert.Equal(before, SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!.Id);
        }

        [Fact]
        public void A_number_sent_as_a_string_is_still_a_number()
        {
            // Models routinely send "14" where the schema asks for 14, and refusing would cost a
            // turn to no purpose.
            using var save = Seeded();

            Call(save.Store, "upsert_character", """{"name":"Bess","maxHealth":"14"}""");

            Assert.Equal(14, SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!.MaxHealth);
        }

        [Fact]
        public void Attributes_can_be_set_in_the_same_breath_as_health()
        {
            using var save = Seeded();

            Call(
                save.Store,
                "upsert_character",
                """{"name":"Bess","attributes":{"Strength":15,"dex":"12"}}""");

            var bess = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!;
            Assert.Equal(15, CharacterAttributes.Find(bess, "Strength")!.Score);
            Assert.Equal(12, CharacterAttributes.Find(bess, "Dexterity")!.Score);
        }

        [Fact]
        public void One_unreadable_score_does_not_cost_the_whole_character()
        {
            using var save = Seeded();

            Call(
                save.Store,
                "upsert_character",
                """{"name":"Bess","attributes":{"Strength":15,"Wisdom":"not a number"}}""");

            var bess = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!;
            Assert.Equal(15, CharacterAttributes.Find(bess, "Strength")!.Score);
        }

        [Fact]
        public void Setting_an_attribute_clamps_it_into_range()
        {
            using var save = Seeded();

            Call(save.Store, "set_attribute", """{"character":"Rowan","attribute":"Strength","score":999}""");

            var rowan = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!;
            Assert.Equal(CharacterAttributes.MaxScore, CharacterAttributes.Find(rowan, "Strength")!.Score);
        }

        // ---- Memories --------------------------------------------------------------------------

        [Fact]
        public void A_memory_is_stored_with_its_tokens_unresolved()
        {
            // Writing resolved names to disk would make the record wrong the moment a character is
            // renamed, and would lose what makes a memory portable.
            using var save = Seeded();

            Call(save.Store, "add_memory", """{"character":"Rowan","text":"{This} crossed the ford."}""");

            var rowan = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!;
            Assert.Equal("{This} crossed the ford.", Assert.Single(rowan.Memories).Text);
        }

        [Fact]
        public void A_memory_reads_back_with_its_tokens_resolved()
        {
            using var save = Seeded();
            Call(save.Store, "add_memory", """{"character":"Rowan","text":"{This} crossed the ford."}""");

            var outcome = Call(save.Store, "get_memories", """{"character":"Rowan"}""");

            Assert.Contains("Rowan crossed the ford.", outcome.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("{This}", outcome.Text, StringComparison.Ordinal);
        }

        // ---- Rolling -----------------------------------------------------------------------------

        [Fact]
        public void A_roll_is_recorded_and_reported()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, "roll", """{"notation":"1d20","reason":"Forcing the door"}""");

            Assert.False(outcome.IsError);
            var roll = Assert.Single(save.Store.ReadRolls().Rolls);
            Assert.Equal("1d20", roll.Notation);
            Assert.InRange(roll.Total, 1, 20);
            Assert.Equal("Forcing the door", roll.Reason);
        }

        [Fact]
        public void An_attribute_supplies_the_modifier()
        {
            using var save = Seeded();
            Call(save.Store, "set_attribute", """{"character":"Rowan","attribute":"Strength","score":16}""");

            Call(
                save.Store,
                "roll",
                """{"notation":"1d20","reason":"Forcing the door","character":"Rowan","attribute":"Strength"}""");

            var roll = Assert.Single(save.Store.ReadRolls().Rolls);
            Assert.Equal("Strength", roll.Attribute);
            Assert.Equal(3, roll.Modifier);
            Assert.InRange(roll.Total, 4, 23);
        }

        [Fact]
        public void An_attribute_and_a_flat_bonus_together_are_refused()
        {
            // Two sources for one number is the ambiguity the resolver exists to remove.
            using var save = Seeded();

            var outcome = Call(
                save.Store,
                "roll",
                """{"notation":"1d20+3","reason":"Forcing","character":"Rowan","attribute":"Strength"}""");

            Assert.True(outcome.IsError);
            Assert.Empty(save.Store.ReadRolls().Rolls);
        }

        [Fact]
        public void A_malformed_notation_is_refused_with_a_sentence_the_narrator_can_act_on()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, "roll", """{"notation":"nonsense","reason":"Forcing"}""");

            Assert.True(outcome.IsError);
            Assert.Empty(save.Store.ReadRolls().Rolls);
        }

        [Fact]
        public void A_hidden_roll_is_stored_hidden_and_the_narrator_is_told_not_to_say_the_number()
        {
            using var save = Seeded();

            var outcome = Call(
                save.Store,
                "roll",
                """{"notation":"1d20","reason":"Sensing the lie","hidden":true}""");

            Assert.True(Assert.Single(save.Store.ReadRolls().Rolls).Hidden);
            Assert.Contains("never the total", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_boolean_sent_as_a_string_is_still_a_boolean()
        {
            using var save = Seeded();

            Call(save.Store, "roll", """{"notation":"1d20","reason":"Sensing","hidden":"true"}""");

            Assert.True(Assert.Single(save.Store.ReadRolls().Rolls).Hidden);
        }

        [Fact]
        public void Revealing_marks_the_most_recent_hidden_roll()
        {
            using var save = Seeded();
            Call(save.Store, "roll", """{"notation":"1d20","reason":"Sensing the lie","hidden":true}""");

            var outcome = Call(save.Store, "reveal_roll", "{}");

            Assert.False(outcome.IsError);
            Assert.True(Assert.Single(save.Store.ReadRolls().Rolls).Revealed);
        }

        [Fact]
        public void Revealing_when_nothing_is_hidden_says_so()
        {
            using var save = Seeded();

            Assert.True(Call(save.Store, "reveal_roll", "{}").IsError);
        }

        // ---- Places ---------------------------------------------------------------------------------

        [Fact]
        public void Moving_a_character_needs_a_place_that_exists()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, "move_character", """{"character":"Rowan","location":"Nowhere"}""");

            Assert.True(outcome.IsError);
        }

        [Fact]
        public void A_character_moves_between_places()
        {
            using var save = Seeded();
            Call(save.Store, "upsert_location", """{"name":"The Mill","description":"Wheel turning."}""");

            var outcome = Call(save.Store, "move_character", """{"character":"Rowan","location":"The Mill"}""");

            Assert.False(outcome.IsError);
            var locations = save.Store.ReadLocations();
            var rowan = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!;
            Assert.Equal("The Mill", SaveStore.WhereIs(locations, rowan.Id)!.Name);
        }

        // ---- Inventory and coin -------------------------------------------------------------------------

        [Fact]
        public void Adding_an_item_puts_it_in_the_pack()
        {
            using var save = Seeded();

            Call(save.Store, "add_item", """{"name":"lantern","quantity":1,"description":"Tin, dented."}""");

            Assert.Contains(save.Store.ReadInventory().Items, item => item.Name == "lantern");
        }

        [Fact]
        public void Removing_more_of_an_item_than_is_carried_takes_all_of_it()
        {
            // Note the asymmetry with remove_money, which refuses rather than clamping because the
            // narrator has to know the player cannot afford something before it writes that they
            // bought it. Items take the other choice: over-removing means the thing is gone, and
            // the quantity never goes negative. Pinned because the two rules read alike but differ.
            using var save = Seeded();
            Call(save.Store, "add_item", """{"name":"lantern","quantity":1}""");

            var outcome = Call(save.Store, "remove_item", """{"name":"lantern","quantity":5}""");

            Assert.False(outcome.IsError);
            Assert.DoesNotContain(save.Store.ReadInventory().Items, item => item.Name == "lantern");
            Assert.All(save.Store.ReadInventory().Items, item => Assert.True(item.Quantity > 0));
        }

        [Fact]
        public void Removing_some_of_a_stack_leaves_the_rest()
        {
            using var save = Seeded();
            Call(save.Store, "add_item", """{"name":"arrows","quantity":10}""");

            Call(save.Store, "remove_item", """{"name":"arrows","quantity":4}""");

            var arrows = save.Store.ReadInventory().Items.Single(item => item.Name == "arrows");
            Assert.Equal(6, arrows.Quantity);
        }

        [Fact]
        public void Removing_an_item_the_player_does_not_have_is_refused()
        {
            using var save = Seeded();

            Assert.True(Call(save.Store, "remove_item", """{"name":"lantern"}""").IsError);
        }

        [Fact]
        public void Coin_is_paid_in_and_out()
        {
            using var save = Seeded();
            var start = save.Store.ReadInventory().Money;

            Call(save.Store, "add_money", """{"amount":10}""");
            Assert.Equal(start + 10, save.Store.ReadInventory().Money);

            Call(save.Store, "remove_money", """{"amount":4}""");
            Assert.Equal(start + 6, save.Store.ReadInventory().Money);
        }

        [Fact]
        public void Spending_more_than_the_purse_holds_is_refused_rather_than_clamped()
        {
            // The narrator is about to describe a purchase and needs to know the player cannot
            // afford it before it writes that they bought it.
            using var save = Seeded();
            var start = save.Store.ReadInventory().Money;

            var outcome = Call(save.Store, "remove_money", $$"""{"amount":{{start + 1}}}""");

            Assert.True(outcome.IsError);
            Assert.Equal(start, save.Store.ReadInventory().Money);
        }

        [Fact]
        public void The_purse_can_never_go_negative_through_the_tools()
        {
            using var save = Seeded();

            for (var attempt = 0; attempt < 20; attempt++)
            {
                Call(save.Store, "remove_money", """{"amount":7}""");
                Assert.True(save.Store.ReadInventory().Money >= 0);
            }
        }

        [Theory]
        [InlineData("add_money", 0)]
        [InlineData("add_money", -5)]
        [InlineData("remove_money", 0)]
        [InlineData("remove_money", -5)]
        public void Coin_moves_only_in_positive_amounts(string tool, int amount)
        {
            using var save = Seeded();

            Assert.True(Call(save.Store, tool, $$"""{"amount":{{amount}}}""").IsError);
        }

        [Theory]
        [InlineData("add_money")]
        [InlineData("remove_money")]
        public void Coin_needs_an_amount(string tool)
        {
            using var save = Seeded();

            Assert.True(Call(save.Store, tool, "{}").IsError);
        }

        // ---- Story ------------------------------------------------------------------------------------

        [Fact]
        public void An_event_is_recorded_and_read_back()
        {
            using var save = Seeded();

            Call(save.Store, "record_event", """{"title":"The ford","detail":"Crossed at dusk."}""");

            Assert.Equal("The ford", Assert.Single(save.Store.ReadStory().Events).Title);
            Assert.Contains("The ford", Call(save.Store, "get_story").Text, StringComparison.Ordinal);
        }

        // ---- Word banks ---------------------------------------------------------------------------------

        [Theory]
        [InlineData("random_noun")]
        [InlineData("random_adjective")]
        public void The_word_bank_hands_out_words(string tool)
        {
            using var save = Seeded();

            var outcome = Call(save.Store, tool, """{"count":3}""");

            Assert.False(outcome.IsError);
            Assert.False(string.IsNullOrWhiteSpace(outcome.Text));
        }

        [Theory]
        [InlineData("random_noun")]
        [InlineData("random_adjective")]
        public void The_word_bank_is_bounded(string tool)
        {
            using var save = Seeded();

            var outcome = Call(save.Store, tool, """{"count":10000}""");

            Assert.False(outcome.IsError);
            Assert.True(outcome.Text.Length < 4096);
        }

        // ---- Failure is text, not an exception ------------------------------------------------------------

        [Fact]
        public void A_tool_reports_a_broken_save_by_throwing_for_the_server_to_catch()
        {
            // SaveException is the store's way of saying the document is unreadable; McpServer
            // turns it into a JSON-RPC error rather than letting the process die.
            using var save = Seeded();
            save.WriteRaw("characters.json", "{ not json");

            Assert.Throws<SaveException>(() => Call(save.Store, "list_characters"));
        }

        [Fact]
        public void Every_tool_survives_being_called_with_nothing_at_all()
        {
            // A model that sends an empty argument object must get a sentence back, never an
            // unhandled exception that takes the server down mid-turn.
            using var save = Seeded();

            foreach (var tool in QuestTools.Definitions)
            {
                var outcome = QuestTools.Invoke(save.Store, tool.Name, Args("{}"));

                Assert.False(string.IsNullOrWhiteSpace(outcome.Text));
            }
        }

        [Fact]
        public void Every_tool_survives_arguments_of_the_wrong_shape()
        {
            using var save = Seeded();
            var hostile = Args(
                """
                {"name":42,"character":true,"text":[],"amount":"lots","quantity":{},
                 "notation":null,"attributes":"not an object","count":"three"}
                """);

            foreach (var tool in QuestTools.Definitions)
            {
                var outcome = QuestTools.Invoke(save.Store, tool.Name, hostile);

                Assert.False(string.IsNullOrWhiteSpace(outcome.Text));
            }
        }
    }
}
