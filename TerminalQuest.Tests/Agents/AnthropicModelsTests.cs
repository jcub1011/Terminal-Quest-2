using TerminalQuest.Agents.Anthropic;
using TerminalQuest.Settings;

using Xunit;

namespace TerminalQuest.Tests.Agents
{
    /// <summary>
    /// The Anthropic Models API parser behind the Claude dropdown's live refresh, and the merge
    /// that layers live ids over the curated list without ever blanking it.
    /// </summary>
    public sealed class AnthropicModelsTests
    {
        [Fact]
        public void Model_ids_come_back_in_the_order_the_api_lists_them()
        {
            var models = AnthropicModels.Parse(
                """{"data":[{"id":"claude-opus-6","type":"model"},{"id":"claude-sonnet-6"}]}""");

            Assert.Equal(["claude-opus-6", "claude-sonnet-6"], models);
        }

        [Theory]
        [InlineData("""{"data":[]}""")]
        [InlineData("""{"data":{}}""")]
        [InlineData("""{"models":["a"]}""")]
        [InlineData("{}")]
        [InlineData("{ not json")]
        [InlineData("")]
        public void An_answer_in_an_unexpected_shape_says_nothing_rather_than_failing(string body)
        {
            // A failed refresh keeps the curated list; "no list" must never read as "no models".
            Assert.Empty(AnthropicModels.Parse(body));
        }

        [Fact]
        public void Null_live_ids_keep_the_curated_list()
        {
            Assert.Same(ClaudeModels.All, ClaudeModels.WithLiveIds(null));
        }

        [Fact]
        public void Unknown_live_ids_are_appended_under_their_own_id()
        {
            var merged = ClaudeModels.WithLiveIds(["claude-opus-6", "  ", "claude-haiku-4-5"]);

            // Curated entries first, in order; only the genuinely new id joins, blanks skipped.
            Assert.Equal(ClaudeModels.All.Length + 1, merged.Length);
            Assert.Equal(ClaudeModels.All, merged[..ClaudeModels.All.Length]);
            var added = merged[^1];
            Assert.Equal("claude-opus-6", added.Id);
            Assert.Equal("claude-opus-6", added.Name);
        }

        [Fact]
        public void Live_ids_matching_known_ones_are_not_offered_twice()
        {
            var merged = ClaudeModels.WithLiveIds(["CLAUDE-HAIKU-4-5", "claude-sonnet-5"]);

            Assert.Equal(ClaudeModels.All.Length, merged.Length);
        }
    }
}
