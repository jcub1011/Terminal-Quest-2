using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    /// <summary>
    /// The tool a resumed narrator reads its own last session back through.
    /// </summary>
    /// <remarks>
    /// Every assertion here passes <c>characters</c> explicitly. The default is the player's setting,
    /// read off the real profile, and a test that depended on it would pass or fail according to what
    /// the machine running it happens to have configured.
    /// </remarks>
    public sealed class GetTranscriptTests
    {
        private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static ToolOutcome Call(SaveStore store, string arguments = "{}") =>
            QuestTools.Invoke(store, "get_transcript", Args(arguments));

        private static void Say(TempSave save, int turn, TranscriptVoice voice, string text) =>
            save.Store.Transcript.Append(new TranscriptEntry { Turn = turn, Voice = voice, Text = text });

        // ---- The tool exists on both halves of the surface -------------------------------------

        [Fact]
        public void The_tool_is_defined_and_dispatched()
        {
            using var save = new TempSave();

            Assert.Contains(QuestTools.Definitions, tool => tool.Name == "get_transcript");
            Assert.False(Call(save.Store).IsError);
        }

        [Fact]
        public void It_is_offered_to_the_narrator()
        {
            // The allowlist is derived from the definitions, so this is really a check that the tool
            // was added where deriving happens rather than only to the dispatch switch.
            Assert.Contains("get_transcript", QuestTools.AllowedTools(), StringComparison.Ordinal);
        }

        // ---- What comes back -------------------------------------------------------------------

        [Fact]
        public void A_save_with_no_transcript_says_so_without_failing()
        {
            // Every save made before this existed. The narrator has to be able to carry on from the
            // world alone, which is what it did before there was anything else.
            using var save = new TempSave();

            var outcome = Call(save.Store, """{"characters":4000}""");

            Assert.False(outcome.IsError);
            Assert.Contains("Nothing", outcome.Text, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_conversation_comes_back_oldest_first_and_attributed()
        {
            using var save = new TempSave();

            Say(save, 1, TranscriptVoice.Player, "push open the door");
            Say(save, 1, TranscriptVoice.Narrator, "The hinges shriek.");

            var text = Call(save.Store, """{"characters":4000}""").Text;

            var playerAt = text.IndexOf("PLAYER: push open the door", StringComparison.Ordinal);
            var narratorAt = text.IndexOf("NARRATOR: The hinges shriek.", StringComparison.Ordinal);

            Assert.True(playerAt >= 0 && narratorAt >= 0, "both voices should be named");
            Assert.True(playerAt < narratorAt, "the conversation should read forwards");
        }

        [Fact]
        public void Markup_is_handed_back_intact()
        {
            // The narrator reads its own tagging and copies it. Stripping it here would teach it to
            // stop using tags on exactly the turn that most needs to look like the last one.
            using var save = new TempSave();

            Say(save, 1, TranscriptVoice.Narrator, "The [item]iron key[/item] turns.");

            Assert.Contains("[item]iron key[/item]", Call(save.Store, """{"characters":4000}""").Text, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unanswered_line_is_reported_as_unanswered()
        {
            // The whole reason a resumed narrator does not simply open a new scene.
            using var save = new TempSave();

            Say(save, 1, TranscriptVoice.Player, "push open the door");
            Say(save, 1, TranscriptVoice.Narrator, "The hinges shriek.");
            Say(save, 2, TranscriptVoice.Player, "look behind me");

            var text = Call(save.Store, """{"characters":4000}""").Text;

            Assert.Contains("has not been answered", text, StringComparison.Ordinal);
            Assert.Contains("discarded", text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_session_that_ended_on_the_narrator_says_the_player_has_not_replied()
        {
            using var save = new TempSave();

            Say(save, 1, TranscriptVoice.Player, "push open the door");
            Say(save, 1, TranscriptVoice.Narrator, "The hinges shriek.");

            var text = Call(save.Store, """{"characters":4000}""").Text;

            Assert.Contains("has not replied", text, StringComparison.Ordinal);
            Assert.DoesNotContain("discarded", text, StringComparison.Ordinal);
        }

        // ---- The window ------------------------------------------------------------------------

        [Fact]
        public void A_smaller_window_returns_less()
        {
            using var save = new TempSave();

            for (var turn = 1; turn <= 40; turn++)
            {
                Say(save, turn, TranscriptVoice.Player, new string('p', 100));
                Say(save, turn, TranscriptVoice.Narrator, new string('n', 100));
            }

            var wide = Call(save.Store, """{"characters":8000}""").Text;
            var narrow = Call(save.Store, """{"characters":600}""").Text;

            Assert.True(narrow.Length < wide.Length, "a smaller budget should recall less");
        }

        [Fact]
        public void The_window_reaches_the_end_of_a_long_campaign()
        {
            // The read is bounded to the tail of the file rather than the whole of it, so the thing
            // most worth checking is that the bound is taken from the right end.
            using var save = new TempSave();

            for (var turn = 1; turn <= 400; turn++)
            {
                Say(save, turn, TranscriptVoice.Narrator, $"scene {turn} " + new string('n', 200));
            }

            var text = Call(save.Store, """{"characters":1000}""").Text;

            Assert.Contains("scene 400", text, StringComparison.Ordinal);
            Assert.DoesNotContain("scene 1 ", text, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("""{"characters":0}""")]
        [InlineData("""{"characters":-5}""")]
        [InlineData("""{"characters":999999999}""")]
        [InlineData("""{"characters":"nonsense"}""")]
        [InlineData("""{"characters":null}""")]
        public void An_unusable_size_is_absorbed_rather_than_refused(string arguments)
        {
            // A refusal costs the narrator a turn and teaches it nothing; the clamp is the whole
            // answer, and the same one a hand-edited settings file gets.
            using var save = new TempSave();

            Say(save, 1, TranscriptVoice.Narrator, "The hinges shriek.");

            var outcome = Call(save.Store, arguments);

            Assert.False(outcome.IsError);
            Assert.Contains("The hinges shriek.", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_size_sent_as_a_string_is_taken_as_a_number()
        {
            // Models routinely do this, and the shared Number helper already tolerates it.
            using var save = new TempSave();

            for (var turn = 1; turn <= 40; turn++)
            {
                Say(save, turn, TranscriptVoice.Narrator, new string('n', 100));
            }

            Assert.True(
                Call(save.Store, """{"characters":"600"}""").Text.Length
                    < Call(save.Store, """{"characters":"8000"}""").Text.Length,
                "\"600\" should be read as 600 rather than ignored");
        }

        // ---- What it must not do ---------------------------------------------------------------

        [Fact]
        public void Reading_the_transcript_does_not_count_as_asking_about_anybody()
        {
            // The trap get_state is deliberately kept out of. A tool that poisons the turn's fetch
            // history would refuse the very next get_character, on the opening turn of every
            // resumed save.
            Assert.DoesNotContain("get_transcript", SecretGate.KnowledgeFetches.Keys);
        }

        [Fact]
        public void The_call_is_journalled_like_any_other()
        {
            using var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", "A quiet sort.", ClassTemplates.All[0], "The Ford");

            Call(save.Store, """{"characters":4000}""");

            Assert.Contains(
                save.Store.Journal.Read().Entries,
                entry => entry.Tool == "get_transcript" && !entry.Failed);
        }

        [Fact]
        public void Asking_creates_no_transcript_for_a_save_that_had_none()
        {
            using var save = new TempSave();

            Call(save.Store, """{"characters":4000}""");

            Assert.False(save.Has("transcript.jsonl"));
        }
    }
}
