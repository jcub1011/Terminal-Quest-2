using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// The narrator's semantic markup ([Entity Name](entity_id) and ["Speech"]), turned into styled spans.
    /// </summary>
    public sealed class MarkupParserTests
    {
        private static string TextOf(StyledLine line) =>
            string.Concat(line.Spans.Select(span => span.Text));

        private static IReadOnlyList<(string Text, TextRole Role, string? EntityId)> SpansOf(StyledLine line) =>
            line.Spans.Select(span => (span.Text, span.Role, span.EntityId)).ToList();

        // ---- Plain text ----------------------------------------------------------------------

        [Fact]
        public void Text_without_markup_comes_through_unchanged()
        {
            var line = MarkupParser.Parse("The road was empty.");

            Assert.Equal([("The road was empty.", TextRole.Normal, null)], SpansOf(line));
        }

        [Fact]
        public void An_empty_string_produces_nothing()
        {
            Assert.Empty(MarkupParser.Parse(string.Empty).Spans);
        }

        // ---- Entity syntax [Entity Name](entity_id) ------------------------------------------

        [Theory]
        [InlineData("Rowan", "chr_1", "character")]
        [InlineData("The Ford", "loc_1", "place")]
        [InlineData("rusted key", "itm_1", "item")]
        [InlineData("Unknown Thing", "other_1", "normal")]
        public void Entity_reference_is_styled_and_associates_id(string name, string id, string roleName)
        {
            var expectedRole = roleName switch
            {
                "character" => TextRole.Character,
                "place" => TextRole.Place,
                "item" => TextRole.Item,
                _ => TextRole.Normal,
            };

            var line = MarkupParser.Parse($"I saw [{name}]({id}) on the path.");

            Assert.Equal(
                [
                    ("I saw ", TextRole.Normal, null),
                    (name, expectedRole, id),
                    (" on the path.", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        [Fact]
        public void Multiple_entities_in_one_line()
        {
            var line = MarkupParser.Parse("[Rowan](chr_1) met [Bess](chr_2) at [The Tavern](loc_1) with a [dagger](itm_1).");

            Assert.Equal("Rowan met Bess at The Tavern with a dagger.", TextOf(line));
            Assert.Equal(
                [
                    ("Rowan", TextRole.Character, "chr_1"),
                    (" met ", TextRole.Normal, null),
                    ("Bess", TextRole.Character, "chr_2"),
                    (" at ", TextRole.Normal, null),
                    ("The Tavern", TextRole.Place, "loc_1"),
                    (" with a ", TextRole.Normal, null),
                    ("dagger", TextRole.Item, "itm_1"),
                    (".", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        // ---- Speech syntax ["Speech dialogue"] ------------------------------------------------

        [Fact]
        public void Speech_syntax_is_rendered_with_quotes_and_speech_styling()
        {
            var line = MarkupParser.Parse("He said, [\"Who goes there?\"] and stopped.");

            Assert.Equal(
                [
                    ("He said, ", TextRole.Normal, null),
                    ("\"Who goes there?\"", TextRole.Speech, null),
                    (" and stopped.", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        [Fact]
        public void Speech_with_speaker_tag_associates_id_and_does_not_render_tag()
        {
            var line = MarkupParser.Parse("He said, [\"Who goes there?\"](chr_1) and stopped.");

            Assert.Equal("He said, \"Who goes there?\" and stopped.", TextOf(line));
            Assert.Equal(
                [
                    ("He said, ", TextRole.Normal, null),
                    ("\"Who goes there?\"", TextRole.Speech, "chr_1"),
                    (" and stopped.", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        [Fact]
        public void Nested_entity_inside_speech_preserves_entity_styling_and_id()
        {
            var line = MarkupParser.Parse("[\"Have you seen [Rowan](chr_1) at [The Tavern](loc_2)?\"]");

            Assert.Equal(
                [
                    ("\"Have you seen ", TextRole.Speech, null),
                    ("Rowan", TextRole.Character, "chr_1"),
                    (" at ", TextRole.Speech, null),
                    ("The Tavern", TextRole.Place, "loc_2"),
                    ("?\"", TextRole.Speech, null)
                ],
                SpansOf(line));
        }

        [Fact]
        public void Nested_entity_inside_speech_with_speaker_tag_preserves_nested_and_speaker_ids()
        {
            var line = MarkupParser.Parse("[\"Have you seen [Rowan](chr_1) at [The Tavern](loc_2)?\"](chr_3)");

            Assert.Equal("\"Have you seen Rowan at The Tavern?\"", TextOf(line));
            Assert.Equal(
                [
                    ("\"Have you seen ", TextRole.Speech, "chr_3"),
                    ("Rowan", TextRole.Character, "chr_1"),
                    (" at ", TextRole.Speech, "chr_3"),
                    ("The Tavern", TextRole.Place, "loc_2"),
                    ("?\"", TextRole.Speech, "chr_3")
                ],
                SpansOf(line));
        }

        [Fact]
        public void Multiple_speeches_with_different_speakers()
        {
            var line = MarkupParser.Parse("[\"Hello!\"](chr_1) said Rowan. [\"Farewell!\"](chr_2) replied Bess.");

            Assert.Equal("\"Hello!\" said Rowan. \"Farewell!\" replied Bess.", TextOf(line));
            Assert.Equal(
                [
                    ("\"Hello!\"", TextRole.Speech, "chr_1"),
                    (" said Rowan. ", TextRole.Normal, null),
                    ("\"Farewell!\"", TextRole.Speech, "chr_2"),
                    (" replied Bess.", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        [Fact]
        public void Quotes_inside_speech_are_tolerated()
        {
            var line = MarkupParser.Parse("[\"She said, \"no\", plainly.\"](chr_2)");

            Assert.Equal(
                [
                    ("\"She said, \"no\", plainly.\"", TextRole.Speech, "chr_2")
                ],
                SpansOf(line));
        }

        [Fact]
        public void Speech_missing_closing_bracket_before_newline_closes_speech_cleanly()
        {
            var line = MarkupParser.Parse("[\"Give me both and walk away.\"\n\nHis gaze was cold.");

            Assert.Equal(
                [
                    ("\"Give me both and walk away.\"", TextRole.Speech, null),
                    ("\n\nHis gaze was cold.", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        [Fact]
        public void Speech_missing_closing_bracket_before_speaker_id_associates_speaker_id()
        {
            var line = MarkupParser.Parse("[\"Give me both and walk away.\"(chr_1)\n\nHis gaze was cold.");

            Assert.Equal(
                [
                    ("\"Give me both and walk away.\"", TextRole.Speech, "chr_1"),
                    ("\n\nHis gaze was cold.", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        [Fact]
        public void Unclosed_speech_does_not_leak_past_paragraph_break()
        {
            var line = MarkupParser.Parse("[\"Give me both and walk away\n\nHis gaze was cold.");

            Assert.Equal(
                [
                    ("\"Give me both and walk away\n", TextRole.Speech, null),
                    ("\nHis gaze was cold.", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        [Fact]
        public void Transcript_snippet_with_missing_bracket_does_not_leak_speech_styling_to_subsequent_paragraphs()
        {
            const string source = "The gesture is one of negotiation. [\"Give me both, and this mess ends here. We can settle for nothing more than its contents.\"\n\nHis gaze flicks to [Corvait](chr_2).\n\nWhat do you do?\n1. Choice";
            var line = MarkupParser.Parse(source);

            Assert.Equal(
                [
                    ("The gesture is one of negotiation. ", TextRole.Normal, null),
                    ("\"Give me both, and this mess ends here. We can settle for nothing more than its contents.\"", TextRole.Speech, null),
                    ("\n\nHis gaze flicks to ", TextRole.Normal, null),
                    ("Corvait", TextRole.Character, "chr_2"),
                    (".\n\nWhat do you do?\n1. Choice", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        // ---- Escaping and non-entity brackets ------------------------------------------------

        [Fact]
        public void Two_brackets_are_an_escaped_literal_one()
        {
            Assert.Equal("a [ b", TextOf(MarkupParser.Parse("a [[ b")));
        }

        [Fact]
        public void An_escaped_bracket_does_not_start_a_tag()
        {
            var line = MarkupParser.Parse("[[item](itm_1)");

            Assert.Equal("[item](itm_1)", TextOf(line));
            Assert.All(line.Spans, span => Assert.Equal(TextRole.Normal, span.Role));
        }

        [Fact]
        public void Bracket_without_parenthesis_id_renders_literally()
        {
            var line = MarkupParser.Parse("Take [1] Option or [items] here.");

            Assert.Equal("Take [1] Option or [items] here.", TextOf(line));
            Assert.All(line.Spans, span => Assert.Equal(TextRole.Normal, span.Role));
        }

        [Fact]
        public void A_stray_bracket_cannot_swallow_the_rest_of_the_stream()
        {
            var tail = new string('x', 140);
            var line = MarkupParser.Parse($"[{tail}");

            Assert.Contains(tail[..30], TextOf(line), StringComparison.Ordinal);
            Assert.StartsWith("[", TextOf(line), StringComparison.Ordinal);
        }

        [Fact]
        public void A_second_bracket_means_the_first_was_never_a_tag()
        {
            var line = MarkupParser.Parse("a [not a tag [Rowan](chr_1)");

            Assert.StartsWith("a [not a tag ", TextOf(line), StringComparison.Ordinal);
            Assert.Contains(line.Spans, span => span.Role == TextRole.Character && span.Text == "Rowan" && span.EntityId == "chr_1");
        }

        // ---- Streaming: delta support --------------------------------------------------------

        [Fact]
        public void An_entity_split_across_deltas_still_parses()
        {
            var parser = new MarkupParser();
            var line = new StyledLine();

            parser.Append("saw [Ro", line);
            parser.Append("wan](chr_1) today", line);

            Assert.Equal(
                [
                    ("saw ", TextRole.Normal, null),
                    ("Rowan", TextRole.Character, "chr_1"),
                    (" today", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        [Fact]
        public void An_entity_split_at_the_id_bracket_still_parses()
        {
            var parser = new MarkupParser();
            var line = new StyledLine();

            parser.Append("saw [Rowan]", line);
            parser.Append("(chr_1) today", line);

            Assert.Equal(
                [
                    ("saw ", TextRole.Normal, null),
                    ("Rowan", TextRole.Character, "chr_1"),
                    (" today", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        [Fact]
        public void Speech_split_across_deltas_still_parses()
        {
            var parser = new MarkupParser();
            var line = new StyledLine();

            parser.Append("[\"Hello, ", line);
            parser.Append("world!\"]", line);

            Assert.Equal([("\"Hello, world!\"", TextRole.Speech, null)], SpansOf(line));
        }

        [Fact]
        public void Speech_with_speaker_tag_split_across_deltas_still_parses()
        {
            var parser = new MarkupParser();
            var line = new StyledLine();

            parser.Append("[\"Hello, ", line);
            parser.Append("world!\"]", line);
            parser.Append("(chr_1)", line);

            Assert.Equal([("\"Hello, world!\"", TextRole.Speech, "chr_1")], SpansOf(line));
        }

        [Fact]
        public void Speech_with_speaker_tag_split_inside_tag_still_parses()
        {
            var parser = new MarkupParser();
            var line = new StyledLine();

            parser.Append("[\"Hello!\"](ch", line);
            parser.Append("r_1) said he.", line);

            Assert.Equal(
                [
                    ("\"Hello!\"", TextRole.Speech, "chr_1"),
                    (" said he.", TextRole.Normal, null)
                ],
                SpansOf(line));
        }

        [Fact]
        public void Streaming_produces_the_same_text_as_parsing_the_whole_string()
        {
            const string source = "[The Ford](loc_1) - [\"Mind the [rope](itm_1),\" [Rowan](chr_1) said.\"](chr_2) [[literal]";

            for (var split = 0; split <= source.Length; split++)
            {
                var parser = new MarkupParser();
                var line = new StyledLine();

                parser.Append(source[..split], line);
                parser.Append(source[split..], line);
                parser.Reset(line);

                Assert.Equal(TextOf(MarkupParser.Parse(source)), TextOf(line));
            }
        }

        [Fact]
        public void Resetting_clears_the_speech_state_between_blocks()
        {
            var parser = new MarkupParser();
            var first = new StyledLine();
            parser.Append("[\"unclosed speech", first);

            parser.Reset();

            var second = new StyledLine();
            parser.Append("plain", second);

            Assert.Equal([("plain", TextRole.Normal, null)], SpansOf(second));
        }

        // ---- Nothing throws ------------------------------------------------------------------

        [Theory]
        [InlineData("[")]
        [InlineData("]")]
        [InlineData("[]")]
        [InlineData("()")]
        [InlineData("[]()")]
        [InlineData("[[[[")]
        [InlineData("[\"")]
        [InlineData("\"]")]
        [InlineData("[\"\"]")]
        [InlineData("[a](b)(c)")]
        public void Malformed_markup_never_throws(string source)
        {
            var line = MarkupParser.Parse(source);

            Assert.NotNull(line);
            Assert.Equal(TextOf(line).Length, line.Length);
        }
    }
}
