using System.Text.Json;

using TerminalQuest.Agents.LmStudio;

using Xunit;

namespace TerminalQuest.Tests.Agents
{
    /// <summary>
    /// The two parsers behind the LM Studio session: the model list, and the tool calls assembled
    /// out of streamed fragments.
    /// </summary>
    public sealed class LmStudioParseTests
    {
        // ---- The model list ------------------------------------------------------------------

        [Fact]
        public void Model_ids_come_back_in_the_order_the_server_lists_them()
        {
            var models = LmStudioModels.Parse(
                """{"data":[{"id":"first"},{"id":"second"},{"id":"third"}]}""");

            Assert.Equal(["first", "second", "third"], models);
        }

        [Theory]
        [InlineData("""{"data":[]}""")]
        [InlineData("""{"data":{}}""")]
        [InlineData("""{"data":"not an array"}""")]
        [InlineData("""{"models":["a"]}""")]
        [InlineData("{}")]
        [InlineData("{ not json")]
        [InlineData("")]
        [InlineData("   ")]
        public void An_answer_in_an_unexpected_shape_says_nothing_rather_than_failing(string body)
        {
            // The server answered, which is the harder half of what the caller wanted to know.
            // Both callers read "no list" as "cannot say" rather than as "no models".
            Assert.Empty(LmStudioModels.Parse(body));
        }

        [Fact]
        public void Entries_without_a_usable_id_are_skipped()
        {
            var models = LmStudioModels.Parse(
                """{"data":[{"id":"good"},{"id":""},{"id":42},{"name":"no id"},"a string",{"id":"also-good"}]}""");

            Assert.Equal(["good", "also-good"], models);
        }

        [Fact]
        public void Extra_properties_on_an_entry_are_ignored()
        {
            var models = LmStudioModels.Parse(
                """{"data":[{"id":"a-model","object":"model","owned_by":"someone"}],"object":"list"}""");

            Assert.Equal(["a-model"], models);
        }

        // ---- Tool calls assembled from fragments --------------------------------------------------

        private static JsonElement Deltas(string json) => JsonDocument.Parse(json).RootElement.Clone();

        [Fact]
        public void One_call_arriving_whole_is_assembled()
        {
            var calls = new List<LmStudioSession.PartialToolCall>();

            LmStudioSession.Accumulate(
                calls,
                Deltas("""[{"index":0,"id":"call_1","function":{"name":"get_state","arguments":"{}"}}]"""));

            var call = Assert.Single(calls).Build(0);
            Assert.Equal("call_1", call.Id);
            Assert.Equal("get_state", call.Name);
            Assert.Equal("{}", call.Arguments);
        }

        [Fact]
        public void Argument_fragments_are_concatenated_in_arrival_order()
        {
            // Arguments are streamed as pieces of their JSON text; index is the only thing tying
            // the pieces together.
            var calls = new List<LmStudioSession.PartialToolCall>();

            LmStudioSession.Accumulate(calls, Deltas("""[{"index":0,"id":"c1","function":{"name":"roll"}}]"""));
            LmStudioSession.Accumulate(calls, Deltas("""[{"index":0,"function":{"arguments":"{\"nota"}}]"""));
            LmStudioSession.Accumulate(calls, Deltas("""[{"index":0,"function":{"arguments":"tion\":\"1d20\"}"}}]"""));

            Assert.Equal("""{"notation":"1d20"}""", Assert.Single(calls).Build(0).Arguments);
        }

        [Fact]
        public void Several_calls_at_once_are_kept_apart_by_their_index()
        {
            var calls = new List<LmStudioSession.PartialToolCall>();

            LmStudioSession.Accumulate(
                calls,
                Deltas("""
                [{"index":0,"id":"c1","function":{"name":"one","arguments":"{\"a\":"}},
                 {"index":1,"id":"c2","function":{"name":"two","arguments":"{\"b\":"}}]
                """));
            LmStudioSession.Accumulate(
                calls,
                Deltas("""[{"index":1,"function":{"arguments":"2}"}},{"index":0,"function":{"arguments":"1}"}}]"""));

            Assert.Equal(2, calls.Count);
            Assert.Equal("""{"a":1}""", calls[0].Build(0).Arguments);
            Assert.Equal("""{"b":2}""", calls[1].Build(1).Arguments);
        }

        [Fact]
        public void A_missing_index_means_the_first_call()
        {
            var calls = new List<LmStudioSession.PartialToolCall>();

            LmStudioSession.Accumulate(calls, Deltas("""[{"id":"c1","function":{"name":"one"}}]"""));

            Assert.Equal("one", Assert.Single(calls).Name);
        }

        [Fact]
        public void A_later_name_or_id_does_not_blank_an_earlier_one()
        {
            var calls = new List<LmStudioSession.PartialToolCall>();

            LmStudioSession.Accumulate(calls, Deltas("""[{"index":0,"id":"c1","function":{"name":"roll"}}]"""));
            LmStudioSession.Accumulate(calls, Deltas("""[{"index":0,"id":"","function":{"name":""}}]"""));

            var call = Assert.Single(calls);
            Assert.Equal("c1", call.Id);
            Assert.Equal("roll", call.Name);
        }

        [Fact]
        public void A_delta_that_is_not_an_object_is_skipped()
        {
            var calls = new List<LmStudioSession.PartialToolCall>();

            LmStudioSession.Accumulate(calls, Deltas("""["a string",42,null,{"index":0,"function":{"name":"one"}}]"""));

            Assert.Equal("one", Assert.Single(calls).Name);
        }

        [Fact]
        public void A_call_the_server_never_named_gets_a_position_based_id()
        {
            // The id is only ever used to pair the result back to the request, and a server that
            // omits it has nothing else to pair on either.
            var calls = new List<LmStudioSession.PartialToolCall>();

            LmStudioSession.Accumulate(calls, Deltas("""[{"index":0,"function":{"name":"one"}}]"""));

            Assert.Equal("call_0", Assert.Single(calls).Build(0).Id);
        }

        // ---- What the server is not trusted about ----------------------------------------------------

        [Theory]
        [InlineData("[]")]
        [InlineData("""["a-model"]""")]
        [InlineData("\"a string\"")]
        [InlineData("42")]
        [InlineData("null")]
        [InlineData("true")]
        public void An_answer_that_is_not_an_object_says_nothing_rather_than_throwing(string body)
        {
            // Parse documents that "an answer in an unexpected shape yields an empty list rather
            // than an error", and the catch beside it only covers JsonException. JsonDocument
            // parses all of these happily, so the root's kind has to be checked: TryGetProperty on
            // a non-object root throws InvalidOperationException, which would escape ListAsync and
            // StartAsync as something no caller is prepared for, unlike the AgentException every
            // other failure arrives as. A server answering /v1/models with a bare array is not
            // far-fetched.
            Assert.Empty(LmStudioModels.Parse(body));
        }

        [Fact]
        public void A_negative_index_is_ignored_rather_than_throwing()
        {
            // The index comes from the server and `calls[index]` indexes with it directly, after a
            // loop that cannot grow the list to a negative size. Unchecked, a hostile or buggy
            // server takes the turn down with an ArgumentOutOfRangeException instead of the
            // AgentException the caller is prepared for.
            var calls = new List<LmStudioSession.PartialToolCall>();

            LmStudioSession.Accumulate(calls, Deltas("""[{"index":-1,"function":{"name":"one"}}]"""));

            Assert.Empty(calls);
        }

        [Fact]
        public void An_absurd_index_does_not_allocate_a_list_to_match()
        {
            // `while (calls.Count <= index) calls.Add(...)` would grow the list to whatever the
            // server asked for, so the index is capped first. One delta claiming index 20,000,000
            // would otherwise allocate twenty million objects before anything else got a say.
            var calls = new List<LmStudioSession.PartialToolCall>();

            LmStudioSession.Accumulate(calls, Deltas("""[{"index":20000000,"function":{"name":"one"}}]"""));

            Assert.True(
                calls.Count < 1000,
                $"the server asked for index 20,000,000 and got a list of {calls.Count}");
        }
    }
}
