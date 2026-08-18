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
            using var save = new TempSave();

            var outcome = Call(save.Store, "get_state");

            Assert.Contains("no player on record", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Tool_outputs_include_entity_ids_for_referencing()
        {
            using var save = Seeded();
            Call(save.Store, "record_event", """{"title":"The ford","detail":"Crossed at dusk.","characters":["Rowan"],"locations":["The Ford"]}""");

            var stateText = Call(save.Store, "get_state").Text;
            Assert.Contains(EntityIds.Character, stateText, StringComparison.Ordinal);
            Assert.Contains(EntityIds.Location, stateText, StringComparison.Ordinal);

            var charText = Call(save.Store, "get_character", """{"name":"Rowan"}""").Text;
            Assert.Contains(EntityIds.Character, charText, StringComparison.Ordinal);

            var locText = Call(save.Store, "get_location", """{"name":"The Ford"}""").Text;
            Assert.Contains(EntityIds.Location, locText, StringComparison.Ordinal);
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
        public void Setting_creates_a_character()
        {
            using var save = Seeded();

            var outcome = Call(
                save.Store,
                "set_character",
                """{"name":"Bess","description":"The ferrywoman.","max_health":14}""");

            Assert.False(outcome.IsError);
            var bess = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess");
            Assert.NotNull(bess);
            Assert.Equal(14, bess.MaxHealth);
            Assert.Equal(CharacterKind.Npc, bess.Kind);
        }

        [Fact]
        public void Setting_an_existing_character_keeps_their_id()
        {
            using var save = Seeded();
            var before = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!.Id;

            Call(save.Store, "set_character", """{"name":"Rowan","description":"Changed."}""");

            Assert.Equal(before, SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!.Id);
        }

        [Fact]
        public void A_number_sent_as_a_string_is_still_a_number()
        {
            using var save = Seeded();

            Call(save.Store, "set_character", """{"name":"Bess","max_health":"14"}""");

            Assert.Equal(14, SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!.MaxHealth);
        }

        [Fact]
        public void Attributes_can_be_set_in_the_same_breath_as_health()
        {
            using var save = Seeded();

            Call(
                save.Store,
                "set_character",
                """{"name":"Bess","attributes":{"Strength":15,"dex":"12"}}""");

            var bess = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!;
            Assert.Equal(15, CharacterAttributes.Find(bess, "Strength")!.Score);
            Assert.Equal(12, CharacterAttributes.Find(bess, "Dexterity")!.Score);
        }

        // ---- Health -----------------------------------------------------------------------------

        [Fact]
        public void Health_is_set_to_the_number_it_was_given()
        {
            using var save = Seeded();
            Call(save.Store, "set_character", """{"name":"Bess","max_health":14}""");

            var outcome = Call(
                save.Store,
                "set_character",
                """{"name":"Bess","health":9}""");

            Assert.False(outcome.IsError);
            Assert.Equal(9, SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!.Health);
        }

        [Fact]
        public void Health_delta_adjusts_health_relatively()
        {
            using var save = Seeded();
            Call(save.Store, "set_character", """{"name":"Bess","max_health":20,"health":15}""");

            var outcome = Call(
                save.Store,
                "set_character",
                """{"name":"Bess","health_delta":-4}""");

            Assert.False(outcome.IsError);
            Assert.Equal(11, SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!.Health);

            Call(save.Store, "set_character", """{"name":"Bess","health_delta":5}""");
            Assert.Equal(16, SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!.Health);
        }

        [Fact]
        public void Health_may_go_above_the_maximum()
        {
            using var save = Seeded();
            Call(save.Store, "set_character", """{"name":"Bess","max_health":14}""");

            var outcome = Call(
                save.Store,
                "set_character",
                """{"name":"Bess","health":20}""");

            Assert.False(outcome.IsError);
            Assert.Equal(20, SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!.Health);
            Assert.Contains("above", outcome.Text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Health_still_has_a_floor_at_zero()
        {
            using var save = Seeded();
            Call(save.Store, "set_character", """{"name":"Bess","max_health":14,"health":5}""");

            var outcome = Call(
                save.Store,
                "set_character",
                """{"name":"Bess","health_delta":-10}""");

            Assert.Equal(0, SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!.Health);
            Assert.Contains("at 0", outcome.Text, StringComparison.Ordinal);
        }

        // ---- Rolling -----------------------------------------------------------------------------

        [Fact]
        public void A_roll_is_recorded_and_reported()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, "roll", """{"notation":"1d20","reason":"Forcing the door"}""");

            Assert.False(outcome.IsError);
            var roll = Assert.Single(save.Store.Rolls.Read().Entries);
            Assert.Equal("1d20", roll.Notation);
            Assert.InRange(roll.Total, 1, 20);
            Assert.Equal("Forcing the door", roll.Reason);
        }

        [Fact]
        public void An_attribute_supplies_the_modifier()
        {
            using var save = Seeded();
            Call(save.Store, "set_character", """{"name":"Rowan","attributes":{"Strength":16}}""");

            Call(
                save.Store,
                "roll",
                """{"notation":"1d20","reason":"Forcing the door","character":"Rowan","attribute":"Strength"}""");

            var roll = Assert.Single(save.Store.Rolls.Read().Entries);
            Assert.Equal("Strength", roll.Attribute);
            Assert.Equal(3, roll.Modifier);
            Assert.InRange(roll.Total, 4, 23);
        }

        [Fact]
        public void An_attribute_and_a_flat_bonus_together_are_refused()
        {
            using var save = Seeded();

            var outcome = Call(
                save.Store,
                "roll",
                """{"notation":"1d20+3","reason":"Forcing","character":"Rowan","attribute":"Strength"}""");

            Assert.True(outcome.IsError);
            Assert.Empty(save.Store.Rolls.Read().Entries);
        }

        [Fact]
        public void A_situational_modifier_modifies_the_roll_total()
        {
            using var save = Seeded();

            var outcome = Call(
                save.Store,
                "roll",
                """{"notation":"1d20","reason":"Slippery ledge","situational_modifier":-4}""");

            Assert.False(outcome.IsError);
            var roll = Assert.Single(save.Store.Rolls.Read().Entries);
            Assert.Equal(-4, roll.SituationalModifier);
            Assert.InRange(roll.Total, -3, 16);
            Assert.Contains("-4 situational", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_situational_modifier_combines_with_an_attribute_modifier()
        {
            using var save = Seeded();
            Call(save.Store, "set_character", """{"name":"Jock Wae","attributes":{"Charisma":13}}""");

            var outcome = Call(
                save.Store,
                "roll",
                """{"notation":"1d20","reason":"Being obnoxious","character":"Jock Wae","attribute":"Charisma","situational_modifier":-5}""");

            Assert.False(outcome.IsError);
            var roll = Assert.Single(save.Store.Rolls.Read().Entries);
            Assert.Equal("Charisma", roll.Attribute);
            Assert.Equal(1, roll.Modifier);
            Assert.Equal(-5, roll.SituationalModifier);
            Assert.InRange(roll.Total, -3, 16);
            Assert.Contains("+1 Charisma -5 situational", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_hidden_roll_is_stored_hidden()
        {
            using var save = Seeded();

            var outcome = Call(
                save.Store,
                "roll",
                """{"notation":"1d20","reason":"Sensing the lie","hidden":true}""");

            Assert.True(Assert.Single(save.Store.Rolls.Read().Entries).Hidden);
            Assert.Contains("Hidden", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Revealing_marks_the_most_recent_hidden_roll()
        {
            using var save = Seeded();
            Call(save.Store, "roll", """{"notation":"1d20","reason":"Sensing the lie","hidden":true}""");

            var outcome = Call(save.Store, "reveal_roll", "{}");

            Assert.False(outcome.IsError);
            var entries = save.Store.Rolls.Read().Entries;
            Assert.Equal(2, entries.Count);
            Assert.True(entries[1].Revealed);
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
            Call(save.Store, "set_location", """{"name":"The Mill","description":"Wheel turning."}""");

            var outcome = Call(save.Store, "move_character", """{"character":"Rowan","location":"The Mill"}""");

            Assert.False(outcome.IsError);
            var locations = save.Store.ReadLocations();
            var rowan = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!;
            Assert.Equal("The Mill", SaveStore.WhereIs(locations, rowan.Id)!.Name);
        }

        // ---- Inventory and coin -------------------------------------------------------------------------

        [Fact]
        public void Modifying_an_item_puts_it_in_the_pack()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, "modify_item", """{"name":"lantern","quantity":1,"description":"Tin, dented."}""");

            Assert.False(outcome.IsError);
            var items = save.Store.ReadItems();
            var itemDef = SaveStore.FindItem(items, "lantern");
            Assert.NotNull(itemDef);
            Assert.Equal("Tin, dented.", itemDef.Description);

            var rowan = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!;
            var inv = save.Store.ReadInventory().Find(rowan.Id);
            Assert.NotNull(inv);
            Assert.Contains(inv.Items, stack => stack.ItemId == itemDef.Id && stack.Quantity == 1);
        }

        [Fact]
        public void Modifying_items_for_npc_works()
        {
            using var save = Seeded();
            Call(save.Store, "set_character", """{"name":"Bess"}""");

            Call(save.Store, "modify_item", """{"name":"oar","quantity":2,"character":"Bess","description":"Ash wood."}""");

            var bess = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!;
            var inv = save.Store.ReadInventory().Find(bess.Id);
            Assert.NotNull(inv);
            Assert.Single(inv.Items);
            Assert.Equal(2, inv.Items[0].Quantity);
        }

        [Fact]
        public void Removing_some_of_a_stack_leaves_the_rest()
        {
            using var save = Seeded();
            Call(save.Store, "modify_item", """{"name":"arrows","quantity":10}""");
            Call(save.Store, "modify_item", """{"name":"arrows","quantity":-4}""");

            var rowan = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!;
            var inv = save.Store.ReadInventory().Find(rowan.Id)!;
            var itemDef = SaveStore.FindItem(save.Store.ReadItems(), "arrows")!;
            var stack = inv.Items.Single(s => s.ItemId == itemDef.Id);
            Assert.Equal(6, stack.Quantity);
        }

        [Fact]
        public void Coin_is_paid_in_and_out()
        {
            using var save = Seeded();
            var rowan = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!;
            var start = save.Store.ReadInventory().Find(rowan.Id)?.Money ?? 0;

            Call(save.Store, "modify_money", """{"amount":10}""");
            Assert.Equal(start + 10, save.Store.ReadInventory().Find(rowan.Id)!.Money);

            Call(save.Store, "modify_money", """{"amount":-4}""");
            Assert.Equal(start + 6, save.Store.ReadInventory().Find(rowan.Id)!.Money);
        }

        [Fact]
        public void Spending_more_than_the_purse_holds_is_refused()
        {
            using var save = Seeded();
            var rowan = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Rowan")!;
            var start = save.Store.ReadInventory().Find(rowan.Id)?.Money ?? 0;

            var outcome = Call(save.Store, "modify_money", $$"""{"amount":{{-(start + 1)}}}""");

            Assert.True(outcome.IsError);
            Assert.Equal(start, save.Store.ReadInventory().Find(rowan.Id)!.Money);
        }

        // ---- Story and Recall -----------------------------------------------------------------------

        [Fact]
        public void An_event_is_recorded_and_recalled()
        {
            using var save = Seeded();

            Call(save.Store, "record_event", """{"title":"The ford","detail":"Crossed at dusk.","characters":["Rowan"],"locations":["The Ford"]}""");

            var ev = Assert.Single(save.Store.Story.Read().Entries);
            Assert.Equal("The ford", ev.Title);

            var recall = Call(save.Store, "recall", """{"character":"Rowan"}""");
            Assert.False(recall.IsError);
            Assert.Contains("The ford", recall.Text, StringComparison.Ordinal);
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

        // ---- Options ------------------------------------------------------------------------------------

        [Fact]
        public void Present_options_persists_choices_to_save_store()
        {
            using var save = Seeded();
            save.Store.Touch(3);

            var outcome = Call(save.Store, "present_options", """{"options":["Force the gate","Search the courtyard","Call out"]}""");

            Assert.False(outcome.IsError);
            Assert.Contains("3 options", outcome.Text, StringComparison.Ordinal);

            var file = save.Store.ReadOptions();
            Assert.Equal(3, file.Turn);
            Assert.Equal(3, file.Options.Count);
            Assert.Equal("Force the gate", file.Options[0]);
            Assert.Equal("Search the courtyard", file.Options[1]);
            Assert.Equal("Call out", file.Options[2]);
        }

        [Fact]
        public void Present_options_with_empty_array_is_refused()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, "present_options", """{"options":[]}""");

            Assert.True(outcome.IsError);
        }

        [Fact]
        public void Present_options_without_options_argument_is_refused()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, "present_options", "{}");

            Assert.True(outcome.IsError);
        }
    }
}
