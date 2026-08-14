using TerminalQuest.Agents.LmStudio;

using Xunit;

namespace TerminalQuest.Tests.Agents
{
    /// <summary>
    /// Stripping a local model's inline chain of thought out of the narration.
    /// </summary>
    /// <remarks>
    /// The property that matters is that reasoning never reaches the pane, however the stream
    /// happens to be cut up — the boundaries of a token and the boundaries of a delta have nothing
    /// to do with each other.
    /// </remarks>
    public sealed class ThinkTagFilterTests
    {
        /// <summary>Feeds a whole string as one delta and flushes.</summary>
        private static string Whole(string text)
        {
            var filter = new ThinkTagFilter();
            return filter.Feed(text) + filter.Flush();
        }

        /// <summary>Feeds a string one character at a time, the worst case for a split tag.</summary>
        private static string CharByChar(string text)
        {
            var filter = new ThinkTagFilter();
            var visible = new System.Text.StringBuilder();

            foreach (var c in text)
            {
                visible.Append(filter.Feed(c.ToString()));
            }

            return visible.Append(filter.Flush()).ToString();
        }

        // ---- Plain text ---------------------------------------------------------------------

        [Fact]
        public void Text_with_no_reasoning_passes_straight_through()
        {
            Assert.Equal("The road was empty.", Whole("The road was empty."));
        }

        [Fact]
        public void An_empty_delta_produces_nothing()
        {
            Assert.Equal(string.Empty, new ThinkTagFilter().Feed(string.Empty));
        }

        [Fact]
        public void Flushing_an_untouched_filter_produces_nothing()
        {
            Assert.Equal(string.Empty, new ThinkTagFilter().Flush());
        }

        // ---- Removing reasoning ----------------------------------------------------------------

        [Fact]
        public void A_think_block_is_removed()
        {
            Assert.Equal("before after", Whole("before <think>reasoning</think>after"));
        }

        [Fact]
        public void Only_the_block_is_removed()
        {
            Assert.Equal("AB", Whole("A<think>x</think>B"));
        }

        [Fact]
        public void Several_blocks_in_one_delta_are_all_removed()
        {
            Assert.Equal("ABC", Whole("A<think>x</think>B<think>y</think>C"));
        }

        [Fact]
        public void Text_between_two_blocks_survives()
        {
            Assert.Equal("keep", Whole("<think>a</think>keep<think>b</think>"));
        }

        [Theory]
        [InlineData("<THINK>x</THINK>")]
        [InlineData("<Think>x</Think>")]
        [InlineData("<think>x</THINK>")]
        public void Tag_case_is_ignored(string block)
        {
            Assert.Equal("ab", Whole($"a{block}b"));
        }

        [Fact]
        public void An_unclosed_block_stays_dropped()
        {
            // It is still reasoning; releasing it on flush would put the model's chain of thought
            // into the transcript at the end of every truncated turn.
            Assert.Equal("before ", Whole("before <think>reasoning that never ends"));
        }

        [Fact]
        public void Nesting_is_not_tracked_and_the_first_closer_wins()
        {
            // Pinned rather than fixed: no local model emits nested think blocks, and tracking
            // depth would hold real narration back on a stray closer.
            Assert.Equal("after", Whole("<think>a<think>b</think>after"));
        }

        // ---- Split across deltas -------------------------------------------------------------------

        [Fact]
        public void An_opening_tag_split_across_deltas_is_still_recognised()
        {
            var filter = new ThinkTagFilter();

            var first = filter.Feed("visible <th");
            var second = filter.Feed("ink>hidden</think> more");

            Assert.Equal("visible ", first);
            Assert.Equal(" more", second);
        }

        [Fact]
        public void A_closing_tag_split_across_deltas_is_still_recognised()
        {
            var filter = new ThinkTagFilter();

            filter.Feed("<think>hidden</th");
            var visible = filter.Feed("ink>shown");

            Assert.Equal("shown", visible);
        }

        [Fact]
        public void Text_that_might_still_become_a_tag_is_held_back()
        {
            var filter = new ThinkTagFilter();

            Assert.Equal("shown", filter.Feed("shown<th"));
        }

        [Fact]
        public void Held_text_that_was_never_a_tag_is_released_on_flush()
        {
            var filter = new ThinkTagFilter();
            filter.Feed("shown<th");

            Assert.Equal("<th", filter.Flush());
        }

        [Fact]
        public void A_lone_bracket_at_the_end_of_a_stream_is_released()
        {
            var filter = new ThinkTagFilter();

            Assert.Equal("text", filter.Feed("text<"));
            Assert.Equal("<", filter.Flush());
        }

        [Fact]
        public void Held_text_that_turns_out_to_be_ordinary_is_released_by_the_next_delta()
        {
            var filter = new ThinkTagFilter();
            filter.Feed("a<th");

            Assert.Equal("<though", filter.Feed("ough"));
        }

        [Theory]
        [InlineData("before <think>reasoning</think>after")]
        [InlineData("A<think>x</think>B<think>y</think>C")]
        [InlineData("no tags at all")]
        [InlineData("<think>all of it</think>")]
        [InlineData("trailing <thi")]
        [InlineData("a<think>b</think>c<thi")]
        public void Cutting_the_stream_anywhere_gives_the_same_result(string source)
        {
            // The real guarantee: the visible text must not depend on how the server chunked it.
            var whole = Whole(source);

            Assert.Equal(whole, CharByChar(source));

            for (var split = 0; split <= source.Length; split++)
            {
                var filter = new ThinkTagFilter();
                var visible = filter.Feed(source[..split])
                    + filter.Feed(source[split..])
                    + filter.Flush();

                Assert.Equal(whole, visible);
            }
        }

        [Fact]
        public void Reasoning_never_reaches_the_pane_however_the_stream_is_cut()
        {
            const string source = "The road was <think>I should describe the weather</think>empty.";

            for (var split = 0; split <= source.Length; split++)
            {
                var filter = new ThinkTagFilter();
                var visible = filter.Feed(source[..split])
                    + filter.Feed(source[split..])
                    + filter.Flush();

                Assert.DoesNotContain("should describe", visible, StringComparison.Ordinal);
                Assert.Equal("The road was empty.", visible);
            }
        }

        // ---- Near misses ----------------------------------------------------------------------------

        [Fact]
        public void A_longer_word_starting_with_the_tag_is_not_a_tag()
        {
            // "<thinking>" contains "<think" but not "<think>", so it is ordinary text. Pinned to
            // confirm it is deliberate: a model writing about thinking is not reasoning aloud.
            Assert.Equal("a<thinking>b", Whole("a<thinking>b"));
        }

        [Fact]
        public void A_tag_with_attributes_is_not_recognised()
        {
            Assert.Equal("a<think class=\"x\">b", Whole("a<think class=\"x\">b"));
        }

        [Fact]
        public void A_closer_with_nothing_open_is_ordinary_text()
        {
            Assert.Equal("a</think>b", Whole("a</think>b"));
        }

        // ---- Reuse -------------------------------------------------------------------------------------

        [Fact]
        public void A_filter_is_usable_again_after_a_flush()
        {
            var filter = new ThinkTagFilter();
            filter.Feed("<think>unfinished");
            filter.Flush();

            Assert.Equal("fresh", filter.Feed("fresh"));
        }

        [Fact]
        public void Flushing_clears_what_was_held()
        {
            var filter = new ThinkTagFilter();
            filter.Feed("held<th");

            Assert.Equal("<th", filter.Flush());
            Assert.Equal(string.Empty, filter.Flush());
        }
    }
}
