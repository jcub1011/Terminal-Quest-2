using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    /// <summary>
    /// That every tool call reaches the journal, and that the journal never costs a tool call.
    /// </summary>
    /// <remarks>
    /// Two properties are load-bearing rather than merely tidy, and both are asserted here. Reads are
    /// recorded as well as writes, because the rule deciding whether a knowledge fetch may be answered
    /// is computed from them; and a failure to record is swallowed, because the narrator would read a
    /// refused tool as the world declining and narrate around it.
    /// <para>
    /// In the environment collection because two of these set <c>QuestJournal.OnFailure</c>, which is
    /// process-wide. It is not an environment variable, but it is the same hazard the collection
    /// exists for: one test's reporter answering another test's question.
    /// </para>
    /// </remarks>
    [Collection(EnvironmentCollection.Name)]
    [Trait(Categories.Name, Categories.Environment)]
    public sealed class QuestJournalTests
    {
        private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static ToolOutcome Call(SaveStore store, string name, string arguments = "{}") =>
            QuestTools.Invoke(store, name, Args(arguments));

        private static TempSave Seeded()
        {
            var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", "A quiet sort.", ClassTemplates.All[0], "The Ford");
            return save;
        }

        // ---- What gets recorded --------------------------------------------------------------

        [Fact]
        public void Every_tool_call_is_recorded_including_the_ones_that_only_read()
        {
            // The deviation from a journal of writes only. Without the reads there is nothing to
            // compute the divergence rule from, because handing a secret over is a read.
            using var save = Seeded();

            Call(save.Store, "get_state");
            Call(save.Store, "get_character", """{"name":"Rowan"}""");

            Assert.Equal(
                ["get_state", "get_character"],
                save.Store.Journal.Read().Entries.Select(entry => entry.Tool));
        }

        [Fact]
        public void A_successful_call_is_recorded_once_and_not_as_failed()
        {
            using var save = Seeded();

            Call(save.Store, "get_state");

            var entry = Assert.Single(save.Store.Journal.Read().Entries);

            Assert.False(entry.Failed);
            Assert.Empty(entry.Error);
            Assert.Equal(1, entry.Seq);
        }

        [Fact]
        public void A_refused_call_is_recorded_as_failed()
        {
            // What makes a refusal distinguishable from an answer later on. A fetch that was turned
            // back handed nothing over and must not count against the turn.
            using var save = new TempSave();

            var outcome = Call(save.Store, "get_character", """{"name":"Nobody"}""");

            Assert.True(outcome.IsError);
            Assert.True(Assert.Single(save.Store.Journal.Read().Entries).Failed);
        }

        [Fact]
        public void An_unknown_tool_is_still_recorded()
        {
            // A narrator reaching for something that does not exist is the most useful line in this
            // file when working out why a turn went wrong, and it is invisible everywhere else.
            using var save = Seeded();

            Call(save.Store, "conjure_dragon");

            var entry = Assert.Single(save.Store.Journal.Read().Entries);

            Assert.Equal("conjure_dragon", entry.Tool);
            Assert.True(entry.Failed);
        }

        [Fact]
        public void A_handler_that_throws_is_recorded_and_still_throws()
        {
            // The catch is not narrowed, so a handler that fails in some way nobody anticipated is
            // still a call that happened - and the exception still reaches the host unchanged.
            using var save = Seeded();
            save.WriteRaw("characters.json", "{ not json");

            Assert.Throws<SaveException>(() =>
                Call(save.Store, "upsert_character", """{"name":"Bess"}"""));

            var entry = Assert.Single(save.Store.Journal.Read().Entries);

            Assert.True(entry.Failed);
            Assert.NotEmpty(entry.Error);
        }

        [Fact]
        public void A_knowledge_fetch_that_throws_before_it_is_gated_is_still_recorded()
        {
            // The gate reads the roster to decide, so it can fail on a save that will not parse - and it
            // runs before any handler. It therefore has to sit inside the same guard the handlers do, or
            // "every call is recorded" would quietly hold for twenty-odd tools and not for the two the
            // divergence rule actually reads.
            using var save = Seeded();
            save.WriteRaw("characters.json", "{ not json");

            Assert.Throws<SaveException>(() => Call(save.Store, "get_character", """{"name":"Rowan"}"""));

            var entry = Assert.Single(save.Store.Journal.Read().Entries);

            Assert.Equal("get_character", entry.Tool);
            Assert.True(entry.Failed);
            Assert.NotEmpty(entry.Error);
        }

        [Fact]
        public void The_arguments_are_recorded_verbatim()
        {
            using var save = Seeded();

            Call(save.Store, "get_memories", """{"character":"Rowan","about":"the ford"}""");

            var arguments = Assert.Single(save.Store.Journal.Read().Entries).Arguments;

            Assert.Equal("Rowan", arguments.GetProperty("character").GetString());
            Assert.Equal("the ford", arguments.GetProperty("about").GetString());
        }

        [Fact]
        public void A_tool_that_takes_no_arguments_records_an_empty_object()
        {
            // The server hands over an undefined element for these, which throws when written. Pins
            // that the substitution happens rather than the call being lost.
            using var save = Seeded();

            QuestTools.Invoke(save.Store, "get_state", default);

            var arguments = Assert.Single(save.Store.Journal.Read().Entries).Arguments;

            Assert.Equal(JsonValueKind.Object, arguments.ValueKind);
            Assert.Equal("{}", arguments.GetRawText());
        }

        [Fact]
        public void The_arguments_survive_the_document_they_came_from()
        {
            // Recording is synchronous inside the call, and has to stay that way: one provider runs
            // tools in process and disposes the document backing these arguments the moment the call
            // returns. A queued write would be reading freed memory.
            using var save = Seeded();

            using (var document = JsonDocument.Parse("""{"name":"Rowan"}"""))
            {
                QuestTools.Invoke(save.Store, "get_character", document.RootElement);
            }

            Assert.Equal(
                "Rowan",
                Assert.Single(save.Store.Journal.Read().Entries).Arguments.GetProperty("name").GetString());
        }

        [Fact]
        public void The_turn_recorded_is_the_one_on_disk()
        {
            // The tool server has no other way to know, which is why the game stamps the turn before
            // the turn rather than after it.
            using var save = Seeded();
            save.Store.Touch(7);

            Call(save.Store, "get_state");

            Assert.Equal(7, Assert.Single(save.Store.Journal.Read().Entries).Turn);
        }

        [Fact]
        public void Calls_are_recorded_in_the_order_they_were_made()
        {
            using var save = Seeded();

            Call(save.Store, "list_characters");
            Call(save.Store, "list_locations");
            Call(save.Store, "get_inventory");

            Assert.Equal(
                ["list_characters", "list_locations", "get_inventory"],
                save.Store.Journal.Read().Entries.Select(entry => entry.Tool));
        }

        // ---- When the journal itself is broken -----------------------------------------------

        [Fact]
        public void A_journal_that_cannot_be_written_does_not_fail_the_tool_call()
        {
            // A directory where the log should be is the cheapest way to make every append fail. The
            // narrator must not learn about it: a refused tool is something it narrates around.
            using var save = Seeded();
            Directory.CreateDirectory(Path.Combine(save.Directory, "journal.jsonl"));

            var reported = new List<string>();
            var previous = QuestJournal.OnFailure;
            QuestJournal.OnFailure = reported.Add;

            try
            {
                var outcome = Call(save.Store, "get_state");

                Assert.False(outcome.IsError);
                Assert.Contains("Rowan", outcome.Text, StringComparison.Ordinal);
            }
            finally
            {
                QuestJournal.OnFailure = previous;
            }

            Assert.Contains("get_state", Assert.Single(reported), StringComparison.Ordinal);
        }

        [Fact]
        public void A_journal_failure_with_nobody_listening_is_silent()
        {
            using var save = Seeded();
            Directory.CreateDirectory(Path.Combine(save.Directory, "journal.jsonl"));

            var previous = QuestJournal.OnFailure;
            QuestJournal.OnFailure = null;

            try
            {
                Assert.False(Call(save.Store, "get_state").IsError);
            }
            finally
            {
                QuestJournal.OnFailure = previous;
            }
        }
    }
}
