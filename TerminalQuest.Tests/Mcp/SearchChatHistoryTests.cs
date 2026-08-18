using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    public sealed class SearchChatHistoryTests
    {
        private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static ToolOutcome Call(SaveStore store, string arguments = "{}") =>
            QuestTools.Invoke(store, "search_chat_history", Args(arguments));

        private static void Say(TempSave save, int turn, TranscriptVoice voice, string text) =>
            save.Store.Transcript.Append(new TranscriptEntry { Turn = turn, Voice = voice, Text = text });

        [Fact]
        public void The_tool_is_defined_and_dispatched()
        {
            using var save = new TempSave();

            Assert.Contains(QuestTools.Definitions, tool => tool.Name == "search_chat_history");
            Assert.False(Call(save.Store).IsError);
        }

        [Fact]
        public void Tool_is_available_to_both_narrator_and_director()
        {
            var narratorAllowed = QuestTools.AllowedTools(ToolRole.Narrator);
            var directorAllowed = QuestTools.AllowedTools(ToolRole.Director);

            Assert.Contains("mcp__quest__search_chat_history", narratorAllowed);
            Assert.Contains("mcp__quest__search_chat_history", directorAllowed);
        }

        [Fact]
        public void Empty_transcript_returns_no_entries_message()
        {
            using var save = new TempSave();

            var outcome = Call(save.Store);

            Assert.False(outcome.IsError);
            Assert.Contains("No transcript entries", outcome.Text);
        }

        [Fact]
        public void Search_by_specific_turn()
        {
            using var save = new TempSave();
            Say(save, 1, TranscriptVoice.Player, "go north");
            Say(save, 1, TranscriptVoice.Narrator, "You arrive at the gate.");
            Say(save, 2, TranscriptVoice.Player, "open the gate");
            Say(save, 2, TranscriptVoice.Narrator, "The gate creaks open.");

            var outcome = Call(save.Store, """{"turn": 2}""");

            Assert.False(outcome.IsError);
            Assert.Contains("open the gate", outcome.Text);
            Assert.Contains("The gate creaks open.", outcome.Text);
            Assert.DoesNotContain("go north", outcome.Text);
        }

        [Fact]
        public void Search_by_turn_range()
        {
            using var save = new TempSave();
            for (var t = 1; t <= 5; t++)
            {
                Say(save, t, TranscriptVoice.Player, $"action {t}");
                Say(save, t, TranscriptVoice.Narrator, $"result {t}");
            }

            var outcome = Call(save.Store, """{"from_turn": 2, "to_turn": 3, "page_size": 10}""");

            Assert.False(outcome.IsError);
            Assert.Contains("action 2", outcome.Text);
            Assert.Contains("result 3", outcome.Text);
            Assert.DoesNotContain("action 1", outcome.Text);
            Assert.DoesNotContain("action 4", outcome.Text);
        }

        [Fact]
        public void Search_by_query_text()
        {
            using var save = new TempSave();
            Say(save, 1, TranscriptVoice.Player, "ask about the dragon");
            Say(save, 1, TranscriptVoice.Narrator, "The elder warns of the red beast.");
            Say(save, 2, TranscriptVoice.Player, "buy a sword");
            Say(save, 2, TranscriptVoice.Narrator, "The blacksmith hands over a blade.");

            var outcome = Call(save.Store, """{"query": "dragon"}""");

            Assert.False(outcome.IsError);
            Assert.Contains("ask about the dragon", outcome.Text);
            Assert.DoesNotContain("blacksmith", outcome.Text);
        }

        [Fact]
        public void Search_by_entity_id_matches_character_name_and_id()
        {
            using var save = new TempSave();

            var characters = new CharacterFile();
            var bess = new Character { Id = "chr_1", Name = "Bess", Kind = CharacterKind.Npc };
            characters.Characters.Add(bess);
            save.Store.WriteCharacters(characters);

            Say(save, 1, TranscriptVoice.Player, "greet Bess");
            Say(save, 1, TranscriptVoice.Narrator, "Bess nods quietly.");
            Say(save, 2, TranscriptVoice.Player, "look around the room");
            Say(save, 2, TranscriptVoice.Narrator, "The room is empty.");

            var outcome = Call(save.Store, """{"entity_id": "chr_1"}""");

            Assert.False(outcome.IsError);
            Assert.Contains("greet Bess", outcome.Text);
            Assert.Contains("Bess nods quietly.", outcome.Text);
            Assert.DoesNotContain("The room is empty.", outcome.Text);
        }

        [Fact]
        public void Search_by_entity_id_matches_location_and_item()
        {
            using var save = new TempSave();

            var locations = new LocationFile();
            locations.Locations.Add(new Location { Id = "loc_1", Name = "The Ford" });
            save.Store.WriteLocations(locations);

            var items = new ItemFile();
            items.Items.Add(new ItemDefinition { Id = "itm_1", Name = "rusty key" });
            save.Store.WriteItems(items);

            Say(save, 1, TranscriptVoice.Player, "travel to The Ford");
            Say(save, 1, TranscriptVoice.Narrator, "You cross the shallow waters.");
            Say(save, 2, TranscriptVoice.Player, "pick up the rusty key");
            Say(save, 2, TranscriptVoice.Narrator, "You take the key.");

            var locOutcome = Call(save.Store, """{"entity_id": "loc_1"}""");
            Assert.False(locOutcome.IsError);
            Assert.Contains("The Ford", locOutcome.Text);
            Assert.DoesNotContain("rusty key", locOutcome.Text);

            var itemOutcome = Call(save.Store, """{"entity_id": "itm_1"}""");
            Assert.False(itemOutcome.IsError);
            Assert.Contains("rusty key", itemOutcome.Text);
            Assert.DoesNotContain("shallow waters", itemOutcome.Text);
        }

        [Fact]
        public void Pagination_splits_results_and_provides_navigation_hint()
        {
            using var save = new TempSave();

            for (var t = 1; t <= 10; t++)
            {
                Say(save, t, TranscriptVoice.Player, $"command {t}");
                Say(save, t, TranscriptVoice.Narrator, $"narration {t}");
            }

            // Total 20 entries. With default page_size of 5, there are 4 pages.
            var page1 = Call(save.Store, """{"page": 1, "page_size": 5}""");

            Assert.False(page1.IsError);
            Assert.Contains("Page 1 of 4", page1.Text);
            Assert.Contains("20 total matches", page1.Text);
            Assert.Contains("command 1", page1.Text);
            Assert.Contains("call search_chat_history with page: 2", page1.Text);

            var page2 = Call(save.Store, """{"page": 2, "page_size": 5}""");
            Assert.False(page2.IsError);
            Assert.Contains("Page 2 of 4", page2.Text);
            Assert.Contains("command 4", page2.Text);
            Assert.DoesNotContain("command 1", page2.Text);
        }

        [Fact]
        public void Out_of_bounds_page_is_clamped()
        {
            using var save = new TempSave();
            Say(save, 1, TranscriptVoice.Player, "hello");
            Say(save, 1, TranscriptVoice.Narrator, "world");

            var outcome = Call(save.Store, """{"page": 999, "page_size": 5}""");

            Assert.False(outcome.IsError);
            Assert.Contains("Page 1 of 1", outcome.Text);
            Assert.Contains("hello", outcome.Text);
        }
    }
}
