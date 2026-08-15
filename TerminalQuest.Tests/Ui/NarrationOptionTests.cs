using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class NarrationOptionTests
    {
        private static StyledLine Line(string text, TextRole role = TextRole.Normal) =>
            StyledLine.FromText(text, role);

        [Fact]
        public void Empty_rows_yields_no_options()
        {
            var options = NarrationOptionDetector.Detect([]);
            Assert.Empty(options);
        }

        [Fact]
        public void Standard_numbered_choices_are_detected()
        {
            List<StyledLine> rows =
            [
                Line("You stand in a dimly lit courtyard."),
                Line("What do you do?"),
                Line("1. Approach the iron fountain"),
                Line("2. Inspect the heavy wooden gate"),
                Line("3. Call out into the mist"),
            ];

            var options = NarrationOptionDetector.Detect(rows);

            Assert.Equal(3, options.Count);
            Assert.Equal(1, options[0].Number);
            Assert.Equal("Approach the iron fountain", options[0].Text);
            Assert.Equal([2], options[0].RowIndices);

            Assert.Equal(2, options[1].Number);
            Assert.Equal("Inspect the heavy wooden gate", options[1].Text);
            Assert.Equal([3], options[1].RowIndices);

            Assert.Equal(3, options[2].Number);
            Assert.Equal("Call out into the mist", options[2].Text);
            Assert.Equal([4], options[2].RowIndices);
        }

        [Theory]
        [InlineData("1) Go north", "2) Go south")]
        [InlineData("[1] Go north", "[2] Go south")]
        [InlineData("1 - Go north", "2 - Go south")]
        [InlineData("1: Go north", "2: Go south")]
        public void Alternative_numbering_formats_are_supported(string opt1, string opt2)
        {
            List<StyledLine> rows =
            [
                Line("What do you do?"),
                Line(opt1),
                Line(opt2),
            ];

            var options = NarrationOptionDetector.Detect(rows);

            Assert.Equal(2, options.Count);
            Assert.Equal(1, options[0].Number);
            Assert.Equal("Go north", options[0].Text);
            Assert.Equal(2, options[1].Number);
            Assert.Equal("Go south", options[1].Text);
        }

        [Fact]
        public void Multi_line_wrapped_choice_rows_are_grouped()
        {
            List<StyledLine> rows =
            [
                Line("What do you do?"),
                Line("1. Inspect the ancient desk carefully to"),
                Line("   see if there are hidden drawers"),
                Line("2. Try opening the rusty lock"),
            ];

            var options = NarrationOptionDetector.Detect(rows);

            Assert.Equal(2, options.Count);
            Assert.Equal(1, options[0].Number);
            Assert.Equal([1, 2], options[0].RowIndices);

            Assert.Equal(2, options[1].Number);
            Assert.Equal([3], options[1].RowIndices);
        }

        [Fact]
        public void Choices_with_semantic_tags_are_parsed_cleanly()
        {
            var line1 = new StyledLine();
            line1.Append("1. Take the ", TextRole.Normal);
            line1.Append("iron key", TextRole.Item);

            var line2 = new StyledLine();
            line2.Append("2. Flee from the ", TextRole.Normal);
            line2.Append("dire wolf", TextRole.Danger);

            List<StyledLine> rows =
            [
                Line("What do you do?"),
                line1,
                line2,
            ];

            var options = NarrationOptionDetector.Detect(rows);

            Assert.Equal(2, options.Count);
            Assert.Equal(1, options[0].Number);
            Assert.Equal("Take the iron key", options[0].Text);
            Assert.Equal(2, options[1].Number);
            Assert.Equal("Flee from the dire wolf", options[1].Text);
        }

        [Fact]
        public void Recalled_transcript_system_markers_are_skipped()
        {
            List<StyledLine> rows =
            [
                Line("What do you do?"),
                Line("1. Open the door"),
                Line("2. Search the chest"),
                Line(""),
                Line("--- you were here ---", TextRole.System),
                Line(""),
                Line("The narrator is ready.", TextRole.System),
                Line(""),
            ];

            var options = NarrationOptionDetector.Detect(rows);

            Assert.Equal(2, options.Count);
            Assert.Equal(1, options[0].Number);
            Assert.Equal("Open the door", options[0].Text);
            Assert.Equal(2, options[1].Number);
            Assert.Equal("Search the chest", options[1].Text);
        }

        [Fact]
        public void Prose_ending_without_choices_yields_no_options()
        {
            List<StyledLine> rows =
            [
                Line("You open your pouch and find 45 gold coins and some rations."),
                Line(""),
            ];

            var options = NarrationOptionDetector.Detect(rows);

            Assert.Empty(options);
        }

        [Fact]
        public void Superseded_choices_after_player_command_yield_no_options()
        {
            List<StyledLine> rows =
            [
                Line("What do you do?"),
                Line("1. Go north"),
                Line("2. Go south"),
                Line(""),
                Line("> 1", TextRole.Command),
                Line(""),
            ];

            var options = NarrationOptionDetector.Detect(rows);

            Assert.Empty(options);
        }

        [Fact]
        public void Choices_remain_active_after_player_slash_command()
        {
            List<StyledLine> rows =
            [
                Line("What do you do?"),
                Line("1. Go north"),
                Line("2. Go south"),
                Line(""),
                Line("> /character", TextRole.Command),
                Line(""),
                Line("Who you know", TextRole.System),
                Line("  Rowan  10/10  (you)", TextRole.Normal),
                Line(""),
            ];

            var options = NarrationOptionDetector.Detect(rows);

            Assert.Equal(2, options.Count);
            Assert.Equal(1, options[0].Number);
            Assert.Equal("Go north", options[0].Text);
            Assert.Equal([1], options[0].RowIndices);

            Assert.Equal(2, options[1].Number);
            Assert.Equal("Go south", options[1].Text);
            Assert.Equal([2], options[1].RowIndices);
        }

        [Fact]
        public void Choices_remain_active_after_multiple_player_slash_commands()
        {
            List<StyledLine> rows =
            [
                Line("What do you do?"),
                Line("1. Go north"),
                Line("2. Go south"),
                Line(""),
                Line("> /character", TextRole.Command),
                Line("Who you know", TextRole.System),
                Line(""),
                Line("> /location", TextRole.Command),
                Line("Where you have been", TextRole.System),
                Line("  The Ford", TextRole.Place),
                Line(""),
            ];

            var options = NarrationOptionDetector.Detect(rows);

            Assert.Equal(2, options.Count);
            Assert.Equal(1, options[0].Number);
            Assert.Equal("Go north", options[0].Text);
            Assert.Equal([1], options[0].RowIndices);

            Assert.Equal(2, options[1].Number);
            Assert.Equal("Go south", options[1].Text);
            Assert.Equal([2], options[1].RowIndices);
        }

        [Fact]
        public void Non_consecutive_numbers_are_ignored()
        {
            List<StyledLine> rows =
            [
                Line("The 1st legion fought with the 2nd legion in 1999."),
                Line("3. A standalone numbered list that is not choices"),
            ];

            var options = NarrationOptionDetector.Detect(rows);

            Assert.Empty(options);
        }
    }
}
