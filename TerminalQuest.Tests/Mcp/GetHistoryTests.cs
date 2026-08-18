using System.Text.Json;
using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;
using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    public sealed class GetHistoryTests
    {
        private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static ToolOutcome Call(SaveStore store, string arguments = "{}") =>
            QuestTools.Invoke(store, "get_history", Args(arguments));

        private static void Say(TempSave save, int turn, TranscriptVoice voice, string text) =>
            save.Store.Transcript.Append(new TranscriptEntry { Turn = turn, Voice = voice, Text = text });

        [Fact]
        public void The_tool_is_defined_and_dispatched()
        {
            using var save = new TempSave();

            Assert.Contains(QuestTools.Definitions, tool => tool.Name == "get_history");
            Assert.False(Call(save.Store).IsError);
        }

        [Fact]
        public void It_is_offered_to_both_narrator_and_director()
        {
            Assert.Contains("get_history", QuestTools.AllowedTools(ToolRole.Narrator), StringComparison.Ordinal);
            Assert.Contains("get_history", QuestTools.AllowedTools(ToolRole.Director), StringComparison.Ordinal);
        }

        [Fact]
        public void History_alias_also_dispatches_cleanly()
        {
            using var save = new TempSave();
            Say(save, 1, TranscriptVoice.Narrator, "The story begins.");

            var outcome = QuestTools.Invoke(save.Store, "history", Args("{}"));
            Assert.False(outcome.IsError);
            Assert.Contains("The story begins.", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Empty_save_returns_clean_message_without_failing()
        {
            using var save = new TempSave();

            var outcome = Call(save.Store, "{}");

            Assert.False(outcome.IsError);
            Assert.Contains("empty", outcome.Text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void Query_by_turn_returns_all_messages_for_that_turn()
        {
            using var save = new TempSave();

            Say(save, 1, TranscriptVoice.Player, "go north");
            Say(save, 1, TranscriptVoice.Narrator, "You see a mountain.");
            Say(save, 2, TranscriptVoice.Player, "climb it");
            Say(save, 2, TranscriptVoice.Narrator, "The rocks are steep.");

            var outcome = Call(save.Store, """{"turn": 2}""");

            Assert.False(outcome.IsError);
            Assert.Contains("Messages for turn 2:", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("PLAYER: climb it", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("NARRATOR: The rocks are steep.", outcome.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("go north", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Query_by_turn_with_no_messages_reports_so()
        {
            using var save = new TempSave();
            Say(save, 1, TranscriptVoice.Player, "hello");

            var outcome = Call(save.Store, """{"turn": 99}""");

            Assert.False(outcome.IsError);
            Assert.Contains("No messages recorded for turn 99.", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Query_by_entity_id_finds_matching_entries_and_turns()
        {
            using var save = new TempSave();

            Say(save, 1, TranscriptVoice.Narrator, "You meet [Rowan](chr_1) by the well.");
            Say(save, 2, TranscriptVoice.Player, "ask [Rowan](chr_1) about the [rusted key](itm_1)");
            Say(save, 2, TranscriptVoice.Narrator, "Rowan shakes his head.");
            Say(save, 3, TranscriptVoice.Narrator, "You find a [rusted key](itm_1) in the dust.");

            var outcome = Call(save.Store, """{"entity_id": "itm_1"}""");

            Assert.False(outcome.IsError);
            Assert.Contains("History matching 'itm_1'", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("[Turn 2]", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("[Turn 3]", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("PLAYER: ask [Rowan](chr_1) about the [rusted key](itm_1)", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("NARRATOR: You find a [rusted key](itm_1) in the dust.", outcome.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("[Turn 1]", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Query_by_entity_name_finds_resolved_entity()
        {
            using var save = new TempSave();
            var charFile = save.Store.ReadCharacters();
            charFile.Characters.Add(new Character { Id = "chr_2", Name = "Bess", Kind = CharacterKind.Npc });
            save.Store.WriteCharacters(charFile);

            Say(save, 1, TranscriptVoice.Narrator, "You see [Bess](chr_2) gathering herbs.");
            Say(save, 2, TranscriptVoice.Player, "greet Bess");

            var outcome = Call(save.Store, """{"entity_id": "Bess"}""");

            Assert.False(outcome.IsError);
            Assert.Contains("[Turn 1]", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("[Turn 2]", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Pagination_defaults_to_5_entries()
        {
            using var save = new TempSave();

            for (var i = 1; i <= 8; i++)
            {
                Say(save, i, TranscriptVoice.Narrator, $"Message {i} mentioning [Rowan](chr_1).");
            }

            var page1 = Call(save.Store, """{"entity_id": "chr_1", "page": 1}""");
            Assert.False(page1.IsError);
            Assert.Contains("Page 1 of 2", page1.Text, StringComparison.Ordinal);
            Assert.Contains("5 of 8 matches", page1.Text, StringComparison.Ordinal);
            Assert.Contains("Message 1", page1.Text, StringComparison.Ordinal);
            Assert.Contains("Message 5", page1.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Message 6", page1.Text, StringComparison.Ordinal);
            Assert.Contains("Use page: 2", page1.Text, StringComparison.Ordinal);

            var page2 = Call(save.Store, """{"entity_id": "chr_1", "page": 2}""");
            Assert.False(page2.IsError);
            Assert.Contains("Page 2 of 2", page2.Text, StringComparison.Ordinal);
            Assert.Contains("3 of 8 matches", page2.Text, StringComparison.Ordinal);
            Assert.Contains("Message 6", page2.Text, StringComparison.Ordinal);
            Assert.Contains("Message 8", page2.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Message 1", page2.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Custom_page_size_is_respected()
        {
            using var save = new TempSave();

            for (var i = 1; i <= 10; i++)
            {
                Say(save, i, TranscriptVoice.Narrator, $"Turn {i} with [sword](itm_5).");
            }

            var outcome = Call(save.Store, """{"entity_id": "itm_5", "page": 1, "page_size": 3}""");
            Assert.False(outcome.IsError);
            Assert.Contains("Page 1 of 4", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("3 of 10 matches", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Page_out_of_range_returns_clean_message()
        {
            using var save = new TempSave();
            Say(save, 1, TranscriptVoice.Narrator, "Hello [Rowan](chr_1)");

            var outcome = Call(save.Store, """{"entity_id": "chr_1", "page": 99}""");
            Assert.False(outcome.IsError);
            Assert.Contains("Page 99 is out of range", outcome.Text, StringComparison.Ordinal);
        }
    }
}
