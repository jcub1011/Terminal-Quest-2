using TerminalQuest.Tests.Infrastructure;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// The two text rules behind the external editor's shadow map.
    /// </summary>
    /// <remarks>
    /// Both guard the same failure: a character described in three paragraphs reaching the save as
    /// one line.
    /// </remarks>
    public sealed class EditorTextTests
    {
        // ---- Flatten -----------------------------------------------------------------------

        [Fact]
        public void A_single_line_is_unchanged()
        {
            Assert.Equal("A quiet sort.", EditorText.Flatten("A quiet sort."));
        }

        [Fact]
        public void A_line_break_becomes_one_space()
        {
            Assert.Equal("one two", EditorText.Flatten("one\ntwo"));
        }

        [Fact]
        public void A_paragraph_break_is_one_gap_not_two()
        {
            Assert.Equal("one two", EditorText.Flatten("one\r\n\r\ntwo"));
        }

        [Fact]
        public void A_long_run_of_breaks_is_still_one_gap()
        {
            Assert.Equal("one two", EditorText.Flatten("one\n\n\n\n\ntwo"));
        }

        [Fact]
        public void A_leading_break_produces_no_leading_space()
        {
            // A pending break is only ever flushed by a character that follows it.
            Assert.Equal("one", EditorText.Flatten("\n\none"));
        }

        [Fact]
        public void A_trailing_break_produces_nothing()
        {
            Assert.Equal("one", EditorText.Flatten("one\n\n"));
        }

        [Fact]
        public void Other_control_characters_are_treated_as_breaks_too()
        {
            Assert.Equal("one two", EditorText.Flatten("one\ttwo"));
        }

        [Fact]
        public void Ordinary_spaces_are_left_alone()
        {
            Assert.Equal("one  two", EditorText.Flatten("one  two"));
        }

        [Fact]
        public void Empty_text_flattens_to_nothing()
        {
            Assert.Equal(string.Empty, EditorText.Flatten(string.Empty));
        }

        [Fact]
        public void Text_of_nothing_but_breaks_flattens_to_nothing()
        {
            Assert.Equal(string.Empty, EditorText.Flatten("\r\n\r\n"));
        }

        [Fact]
        public void A_flattened_value_never_contains_a_control_character()
        {
            const string source = "one\ntwo\r\n\r\nthree\tfour\n";

            Assert.DoesNotContain(EditorText.Flatten(source), c => char.IsControl(c));
        }

        [Fact]
        public void Null_text_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(() => EditorText.Flatten(null!));
        }

        // ---- Resolve ------------------------------------------------------------------------------

        [Fact]
        public void The_whole_value_is_reported_while_the_field_still_shows_the_joined_form()
        {
            const string raw = "First paragraph.\n\nSecond paragraph.";
            var flattened = EditorText.Flatten(raw);

            Assert.Equal(raw, EditorText.Resolve(flattened, raw, flattened));
        }

        [Fact]
        public void What_the_player_typed_wins_the_moment_they_type_over_it()
        {
            // Otherwise a field the player has edited would still commit the editor's old text.
            const string raw = "First paragraph.\n\nSecond paragraph.";
            var flattened = EditorText.Flatten(raw);

            Assert.Equal("something else", EditorText.Resolve("something else", raw, flattened));
        }

        [Fact]
        public void An_empty_field_reports_itself_rather_than_the_old_value()
        {
            const string raw = "First.\n\nSecond.";

            Assert.Equal(string.Empty, EditorText.Resolve(string.Empty, raw, EditorText.Flatten(raw)));
        }

        [Fact]
        public void A_single_line_edit_resolves_to_itself()
        {
            Assert.Equal("A quiet sort.", EditorText.Resolve("A quiet sort.", "A quiet sort.", "A quiet sort."));
        }

        [Fact]
        public void A_round_trip_through_the_editor_never_loses_a_paragraph()
        {
            // The end-to-end property the pair exists for.
            const string raw = "He was born by the river.\n\nHe never left it.\n\nThat was the whole of it.";
            var shown = EditorText.Flatten(raw);

            var committed = EditorText.Resolve(shown, raw, shown);

            Assert.Equal(3, committed.Split("\n\n").Length);
            Assert.Equal(raw, committed);
        }
    }
}
