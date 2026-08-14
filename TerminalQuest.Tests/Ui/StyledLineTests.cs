using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// One logical paragraph of the transcript.
    /// </summary>
    public sealed class StyledLineTests
    {
        [Fact]
        public void A_new_line_is_empty()
        {
            var line = new StyledLine();

            Assert.Empty(line.Spans);
            Assert.Equal(0, line.Length);
        }

        [Fact]
        public void Appending_records_the_text_and_its_length()
        {
            var line = new StyledLine();

            line.Append("hello", TextRole.Normal);

            Assert.Equal("hello", Assert.Single(line.Spans).Text);
            Assert.Equal(5, line.Length);
        }

        [Fact]
        public void Text_of_the_same_role_merges_into_one_span()
        {
            // A token-by-token stream would otherwise produce one span per token.
            var line = new StyledLine();

            line.Append("hel", TextRole.Normal);
            line.Append("lo", TextRole.Normal);

            Assert.Equal("hello", Assert.Single(line.Spans).Text);
            Assert.Equal(5, line.Length);
        }

        [Fact]
        public void A_change_of_role_starts_a_new_span()
        {
            var line = new StyledLine();

            line.Append("a ", TextRole.Normal);
            line.Append("key", TextRole.Item);

            Assert.Equal(2, line.Spans.Count);
            Assert.Equal(5, line.Length);
        }

        [Fact]
        public void Merging_survives_many_tokens()
        {
            var line = new StyledLine();

            for (var i = 0; i < 100; i++)
            {
                line.Append("x", TextRole.Speech);
            }

            Assert.Single(line.Spans);
            Assert.Equal(100, line.Length);
        }

        [Fact]
        public void Appending_nothing_changes_nothing()
        {
            var line = new StyledLine();

            line.Append(string.Empty, TextRole.Normal);

            Assert.Empty(line.Spans);
            Assert.Equal(0, line.Length);
        }

        [Fact]
        public void A_span_can_be_appended_whole()
        {
            var line = new StyledLine();

            line.Append(new StyledSpan("key", TextRole.Item));

            Assert.Equal(TextRole.Item, Assert.Single(line.Spans).Role);
        }

        [Fact]
        public void From_text_builds_a_one_span_line()
        {
            var line = StyledLine.FromText("hello", TextRole.Danger);

            Assert.Equal(("hello", TextRole.Danger), (line.Spans[0].Text, line.Spans[0].Role));
            Assert.Equal(5, line.Length);
        }

        [Fact]
        public void From_text_defaults_to_the_normal_role()
        {
            Assert.Equal(TextRole.Normal, StyledLine.FromText("hello").Spans[0].Role);
        }

        // ---- TrimEnd ---------------------------------------------------------------------------

        [Fact]
        public void Trailing_spaces_are_dropped_and_the_length_follows()
        {
            // The space sitting at a wrap point must not survive as invisible padding.
            var line = StyledLine.FromText("hello   ");

            line.TrimEnd();

            Assert.Equal("hello", Assert.Single(line.Spans).Text);
            Assert.Equal(5, line.Length);
        }

        [Fact]
        public void A_span_that_was_nothing_but_spaces_is_removed_entirely()
        {
            var line = new StyledLine();
            line.Append("hello", TextRole.Normal);
            line.Append("   ", TextRole.Item);

            line.TrimEnd();

            Assert.Equal("hello", Assert.Single(line.Spans).Text);
            Assert.Equal(5, line.Length);
        }

        [Fact]
        public void Trimming_walks_back_through_several_blank_spans()
        {
            var line = new StyledLine();
            line.Append("hello", TextRole.Normal);
            line.Append("  ", TextRole.Item);
            line.Append("  ", TextRole.Speech);

            line.TrimEnd();

            Assert.Equal("hello", Assert.Single(line.Spans).Text);
            Assert.Equal(5, line.Length);
        }

        [Fact]
        public void Trimming_a_line_with_no_trailing_space_changes_nothing()
        {
            var line = StyledLine.FromText("hello");

            line.TrimEnd();

            Assert.Equal("hello", Assert.Single(line.Spans).Text);
            Assert.Equal(5, line.Length);
        }

        [Fact]
        public void Trimming_an_empty_line_is_harmless()
        {
            var line = new StyledLine();

            line.TrimEnd();

            Assert.Empty(line.Spans);
            Assert.Equal(0, line.Length);
        }

        [Fact]
        public void Trimming_a_line_of_nothing_but_spaces_empties_it()
        {
            var line = StyledLine.FromText("    ");

            line.TrimEnd();

            Assert.Empty(line.Spans);
            Assert.Equal(0, line.Length);
        }

        [Fact]
        public void Only_spaces_are_trimmed_not_other_whitespace()
        {
            var line = StyledLine.FromText("hello\t");

            line.TrimEnd();

            Assert.Equal("hello\t", Assert.Single(line.Spans).Text);
            Assert.Equal(6, line.Length);
        }

        [Fact]
        public void The_length_always_matches_the_text_it_holds()
        {
            var line = new StyledLine();
            line.Append("one ", TextRole.Normal);
            line.Append("two", TextRole.Item);
            line.Append("   ", TextRole.Item);
            line.TrimEnd();

            Assert.Equal(
                string.Concat(line.Spans.Select(span => span.Text)).Length,
                line.Length);
        }
    }
}
