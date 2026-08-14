using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// The narrator's semantic markup, turned into styled spans.
    /// </summary>
    /// <remarks>
    /// Input is model-authored, so nothing here may throw and nothing may swallow the transcript.
    /// The two properties that matter most are that malformed markup degrades to literal text, and
    /// that a tag split across stream deltas still parses — the parser is fed raw deltas, so that
    /// case is the normal one rather than an edge.
    /// </remarks>
    public sealed class MarkupParserTests
    {
        private static string TextOf(StyledLine line) =>
            string.Concat(line.Spans.Select(span => span.Text));

        private static IReadOnlyList<(string Text, TextRole Role)> SpansOf(StyledLine line) =>
            line.Spans.Select(span => (span.Text, span.Role)).ToList();

        // ---- Plain text ----------------------------------------------------------------------

        [Fact]
        public void Text_without_markup_comes_through_unchanged()
        {
            var line = MarkupParser.Parse("The road was empty.");

            Assert.Equal([("The road was empty.", TextRole.Normal)], SpansOf(line));
        }

        [Fact]
        public void An_empty_string_produces_nothing()
        {
            Assert.Empty(MarkupParser.Parse(string.Empty).Spans);
        }

        // ---- Known tags ----------------------------------------------------------------------

        [Theory]
        [InlineData("item")]
        [InlineData("danger")]
        [InlineData("speech")]
        [InlineData("place")]
        [InlineData("system")]
        public void Each_known_tag_styles_its_text(string tag)
        {
            var expected = tag switch
            {
                "item" => TextRole.Item,
                "danger" => TextRole.Danger,
                "speech" => TextRole.Speech,
                "place" => TextRole.Place,
                _ => TextRole.System,
            };

            var line = MarkupParser.Parse($"a [{tag}]b[/] c");

            Assert.Equal(
                [("a ", TextRole.Normal), ("b", expected), (" c", TextRole.Normal)],
                SpansOf(line));
        }

        [Fact]
        public void Tags_nest()
        {
            var line = MarkupParser.Parse("[speech]he said [item]key[/] there[/]");

            Assert.Equal(
                [("he said ", TextRole.Speech), ("key", TextRole.Item), (" there", TextRole.Speech)],
                SpansOf(line));
        }

        [Fact]
        public void A_named_closer_is_accepted_as_well_as_a_bare_one()
        {
            // Models emit either regardless of what the prompt asks for.
            var line = MarkupParser.Parse("[place]The Ford[/place] beyond");

            Assert.Equal(
                [("The Ford", TextRole.Place), (" beyond", TextRole.Normal)],
                SpansOf(line));
        }

        [Fact]
        public void A_named_closer_pops_through_a_missing_inner_closer()
        {
            // A missing inner closer must not strand the stack for the rest of the paragraph.
            var line = MarkupParser.Parse("[place]The [item]Ford[/place] beyond");

            Assert.Equal(TextRole.Normal, line.Spans[^1].Role);
            Assert.Equal(" beyond", line.Spans[^1].Text);
        }

        [Fact]
        public void An_unclosed_tag_styles_the_rest_of_the_line()
        {
            var line = MarkupParser.Parse("plain [danger]the rest");

            Assert.Equal(
                [("plain ", TextRole.Normal), ("the rest", TextRole.Danger)],
                SpansOf(line));
        }

        // ---- The game's own voice is not the narrator's to use ------------------------------------

        [Theory]
        [InlineData("roll")]
        [InlineData("command")]
        public void The_games_own_tags_are_not_available_to_the_narrator(string tag)
        {
            // Missing on purpose, not by oversight. Giving the narrator a [roll] tag would let it
            // type a roll line — inventing a number, or spelling out one it was told to keep quiet.
            // An unknown tag renders literally, so the mistake is visible rather than convincing.
            var line = MarkupParser.Parse($"[{tag}]1d20 = 18[/]");

            Assert.Equal($"[{tag}]1d20 = 18", TextOf(line));
            Assert.All(line.Spans, span => Assert.Equal(TextRole.Normal, span.Role));
        }

        [Fact]
        public void An_unknown_tag_is_shown_as_the_narrator_wrote_it()
        {
            var line = MarkupParser.Parse("a [wobble]b");

            Assert.Equal("a [wobble]b", TextOf(line));
        }

        [Fact]
        public void A_closer_for_a_tag_that_was_never_understood_is_shown_as_written()
        {
            var line = MarkupParser.Parse("a[/wobble]b");

            Assert.Equal("a[/wobble]b", TextOf(line));
        }

        [Fact]
        public void An_unmatched_closer_for_a_known_role_is_dropped()
        {
            // Printing it would put "[/item]" in the narration; popping it would corrupt the stack.
            var line = MarkupParser.Parse("a[/item]b");

            Assert.Equal("ab", TextOf(line));
            Assert.All(line.Spans, span => Assert.Equal(TextRole.Normal, span.Role));
        }

        [Fact]
        public void A_bare_closer_with_nothing_open_is_harmless()
        {
            Assert.Equal("ab", TextOf(MarkupParser.Parse("a[/]b")));
        }

        // ---- Escaping and stray brackets --------------------------------------------------------

        [Fact]
        public void Two_brackets_are_an_escaped_literal_one()
        {
            Assert.Equal("a [ b", TextOf(MarkupParser.Parse("a [[ b")));
        }

        [Fact]
        public void An_escaped_bracket_does_not_start_a_tag()
        {
            var line = MarkupParser.Parse("[[item]not styled");

            Assert.Equal("[item]not styled", TextOf(line));
            Assert.All(line.Spans, span => Assert.Equal(TextRole.Normal, span.Role));
        }

        [Fact]
        public void A_stray_bracket_cannot_swallow_the_rest_of_the_stream()
        {
            // One '[' that is never closed within the tag limit reverts to literal text.
            var tail = new string('x', 40);

            var line = MarkupParser.Parse($"[{tail}");

            Assert.Contains(tail[..30], TextOf(line), StringComparison.Ordinal);
            Assert.StartsWith("[", TextOf(line), StringComparison.Ordinal);
        }

        [Fact]
        public void A_second_bracket_means_the_first_was_never_a_tag()
        {
            var line = MarkupParser.Parse("a [not a tag [item]styled[/]");

            Assert.StartsWith("a [not a tag ", TextOf(line), StringComparison.Ordinal);
            Assert.Contains(line.Spans, span => span.Role == TextRole.Item && span.Text == "styled");
        }

        [Fact]
        public void A_bracket_at_the_very_end_is_held_rather_than_emitted()
        {
            // It may be the start of a tag whose remainder is in the next delta.
            var parser = new MarkupParser();
            var line = new StyledLine();

            parser.Append("text[", line);

            Assert.Equal("text", TextOf(line));
        }

        // ---- Streaming: the case the parser exists for --------------------------------------------

        [Fact]
        public void A_tag_split_across_two_deltas_still_parses()
        {
            var parser = new MarkupParser();
            var line = new StyledLine();

            parser.Append("a [dan", line);
            parser.Append("ger]b[/] c", line);

            Assert.Equal(
                [("a ", TextRole.Normal), ("b", TextRole.Danger), (" c", TextRole.Normal)],
                SpansOf(line));
        }

        [Fact]
        public void A_tag_split_one_character_at_a_time_still_parses()
        {
            var parser = new MarkupParser();
            var line = new StyledLine();

            foreach (var c in "the [item]rusted key[/] lay there")
            {
                parser.Append(c.ToString(), line);
            }

            Assert.Equal("the rusted key lay there", TextOf(line));
            Assert.Contains(line.Spans, span => span.Role == TextRole.Item && span.Text == "rusted key");
        }

        [Fact]
        public void A_role_survives_between_deltas()
        {
            var parser = new MarkupParser();
            var line = new StyledLine();

            parser.Append("[speech]he said", line);
            parser.Append(" more[/]", line);

            Assert.Equal([("he said more", TextRole.Speech)], SpansOf(line));
        }

        [Fact]
        public void Streaming_produces_the_same_text_as_parsing_the_whole_string()
        {
            const string source = "[place]The Ford[/] - [speech]\"Mind the [item]rope[/],\" she said[/]. [[literal]";

            for (var split = 0; split <= source.Length; split++)
            {
                var parser = new MarkupParser();
                var line = new StyledLine();

                parser.Append(source[..split], line);
                parser.Append(source[split..], line);

                Assert.Equal(TextOf(MarkupParser.Parse(source)), TextOf(line));
            }
        }

        [Fact]
        public void Resetting_clears_the_role_stack_between_blocks()
        {
            var parser = new MarkupParser();
            var first = new StyledLine();
            parser.Append("[danger]unclosed", first);

            parser.Reset();

            var second = new StyledLine();
            parser.Append("plain", second);

            Assert.Equal([("plain", TextRole.Normal)], SpansOf(second));
        }

        [Fact]
        public void Resetting_discards_a_partial_tag()
        {
            var parser = new MarkupParser();
            var first = new StyledLine();
            parser.Append("text [dan", first);

            parser.Reset();

            var second = new StyledLine();
            parser.Append("ger]plain", second);

            Assert.Equal("ger]plain", TextOf(second));
        }

        // ---- Nothing throws -------------------------------------------------------------------------

        [Theory]
        [InlineData("[")]
        [InlineData("]")]
        [InlineData("[]")]
        [InlineData("[/]")]
        [InlineData("[[[[")]
        [InlineData("[/[/[/")]
        [InlineData("[item][item][item]")]
        [InlineData("[/item][/place][/]")]
        [InlineData("[ITEM]shouting[/]")]
        [InlineData("[item ]spaced[/]")]
        [InlineData("[/ ]")]
        [InlineData("]]]]")]
        public void Malformed_markup_never_throws(string source)
        {
            var line = MarkupParser.Parse(source);

            Assert.NotNull(line);
            Assert.Equal(TextOf(line).Length, line.Length);
        }

        [Fact]
        public void Tag_names_are_matched_exactly()
        {
            // Deliberately case-sensitive: the prompt names the tags in lower case, and a model
            // shouting one is showing a drift worth seeing rather than papering over.
            var line = MarkupParser.Parse("[ITEM]key[/]");

            Assert.Equal("[ITEM]key", TextOf(line));
        }
    }
}
