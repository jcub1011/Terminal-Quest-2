using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// The transcript's word wrap. Unwrapped paragraphs are the model and the rows are derived, so
    /// this runs on every resize and behind every status line - and nothing else pins it down.
    /// </summary>
    public sealed class WrapTests
    {
        private static List<StyledLine> Wrap(int width, params (string Text, TextRole Role)[] spans)
        {
            var line = new StyledLine();
            foreach (var (text, role) in spans)
            {
                line.Append(text, role);
            }

            return NarrationView.Wrap(line.Spans, width);
        }

        private static List<StyledLine> Wrap(string text, int width) =>
            Wrap(width, (text, TextRole.Normal));

        /// <summary>Each row as plain text, which is what most of these tests are about.</summary>
        private static List<string> Rows(List<StyledLine> rows) =>
            [.. rows.Select(row => string.Concat(row.Spans.Select(span => span.Text)))];

        /// <summary>One row's spans as "role:text", for the tests that care about colour.</summary>
        private static List<string> Runs(StyledLine row) =>
            [.. row.Spans.Select(span => $"{span.Role}:{span.Text}")];

        [Fact]
        public void Text_that_fits_is_one_row()
        {
            Assert.Equal(["hello"], Rows(Wrap("hello", 10)));
        }

        [Fact]
        public void Text_breaks_at_a_space()
        {
            Assert.Equal(["aaa", "bbb"], Rows(Wrap("aaa bbb", 3)));
        }

        /// <summary>
        /// The space that falls at the break is dropped rather than carried, so a wrapped row does not
        /// start with stray indentation.
        /// </summary>
        [Fact]
        public void The_space_at_a_break_is_dropped()
        {
            var rows = Wrap("aaa bbb", 3);

            Assert.Equal("aaa", string.Concat(rows[0].Spans.Select(span => span.Text)));
            Assert.Equal("bbb", string.Concat(rows[1].Spans.Select(span => span.Text)));
        }

        [Fact]
        public void A_space_inside_a_row_is_kept()
        {
            Assert.Equal(["a b"], Rows(Wrap("a b", 3)));
        }

        [Fact]
        public void A_trailing_space_is_trimmed()
        {
            var rows = Wrap("aa ", 10);

            Assert.Equal(["aa"], Rows(rows));
            Assert.Equal(2, rows[0].Length);
        }

        [Fact]
        public void A_newline_forces_a_break()
        {
            Assert.Equal(["a", "b"], Rows(Wrap("a\nb", 10)));
        }

        [Fact]
        public void A_carriage_return_is_ignored()
        {
            Assert.Equal(["ab"], Rows(Wrap("a\rb", 10)));
        }

        /// <summary>A word with nowhere to break is broken at the margin rather than overflowing.</summary>
        [Fact]
        public void A_word_wider_than_the_pane_is_hard_broken()
        {
            Assert.Equal(["abc", "def"], Rows(Wrap("abcdef", 3)));
        }

        [Fact]
        public void A_word_far_wider_than_the_pane_breaks_as_many_times_as_it_takes()
        {
            Assert.Equal(["ab", "cd", "ef", "g"], Rows(Wrap("abcdefg", 2)));
        }

        /// <summary>
        /// <c>AddBlankLine</c> depends on this: an empty paragraph is one empty row, not no rows,
        /// because it is a spacer the transcript scrolls through.
        /// </summary>
        [Fact]
        public void An_empty_paragraph_is_one_empty_row()
        {
            var rows = NarrationView.Wrap(new StyledLine().Spans, 10);

            Assert.Single(rows);
            Assert.Empty(rows[0].Spans);
        }

        [Fact]
        public void A_pane_with_no_width_yields_no_rows()
        {
            Assert.Empty(NarrationView.Wrap(StyledLine.FromText("anything").Spans, 0));
        }

        // ---- Roles ---------------------------------------------------------------------------------

        /// <summary>
        /// Two roles inside one row stay two runs, in order. This is what the per-run append has to
        /// preserve: a role change mid-word must not be flattened into one colour.
        /// </summary>
        [Fact]
        public void Roles_within_a_row_are_kept_as_separate_runs()
        {
            var rows = Wrap(10, ("ab", TextRole.Normal), ("cd", TextRole.Item));

            Assert.Single(rows);
            Assert.Equal(["Normal:ab", "Item:cd"], Runs(rows[0]));
        }

        [Fact]
        public void One_role_across_a_row_is_a_single_run()
        {
            var rows = Wrap(10, ("ab", TextRole.Item), ("cd", TextRole.Item));

            Assert.Equal(["Item:abcd"], Runs(rows[0]));
        }

        [Fact]
        public void Roles_survive_a_break()
        {
            var rows = Wrap(3, ("aaa ", TextRole.Normal), ("bbb", TextRole.Item));

            Assert.Equal(["Normal:aaa"], Runs(rows[0]));
            Assert.Equal(["Item:bbb"], Runs(rows[1]));
        }

        [Fact]
        public void Roles_survive_a_hard_break()
        {
            var rows = Wrap(2, ("ab", TextRole.Danger), ("cd", TextRole.Speech));

            Assert.Equal(["Danger:ab"], Runs(rows[0]));
            Assert.Equal(["Speech:cd"], Runs(rows[1]));
        }

        /// <summary>A role changing mid-word is carried through the margin it is broken at.</summary>
        [Fact]
        public void A_role_change_inside_a_hard_broken_word_is_kept()
        {
            var rows = Wrap(3, ("ab", TextRole.Normal), ("cd", TextRole.Item));

            Assert.Equal(["Normal:ab", "Item:c"], Runs(rows[0]));
            Assert.Equal(["Item:d"], Runs(rows[1]));
        }

        // ---- Lengths -------------------------------------------------------------------------------

        [Fact]
        public void No_row_is_wider_than_the_pane()
        {
            const string Prose =
                "The lantern gutters, and something in the dark shifts its weight from one foot to "
                + "the other. A door you do not remember opening stands open.";

            foreach (var width in (int[])[1, 2, 3, 7, 13, 40, 80])
            {
                foreach (var row in Wrap(Prose, width))
                {
                    Assert.True(row.Length <= width, $"row of {row.Length} exceeds width {width}");
                }
            }
        }

        [Fact]
        public void A_rows_length_matches_its_spans()
        {
            foreach (var row in Wrap("some prose that will wrap more than once here", 9))
            {
                Assert.Equal(row.Spans.Sum(span => span.Text.Length), row.Length);
            }
        }

        /// <summary>Nothing is lost and nothing is invented: the words come back in order.</summary>
        [Fact]
        public void Wrapping_preserves_the_words()
        {
            const string Prose = "one two three four five six seven eight nine ten";

            foreach (var width in (int[])[5, 6, 11, 20, 100])
            {
                var wrapped = string.Join(" ", Rows(Wrap(Prose, width)));

                Assert.Equal(Prose, wrapped);
            }
        }

        [Fact]
        public void AddBlankLine_collapses_consecutive_blank_lines()
        {
            var view = new NarrationView();
            view.AddLine("Line 1", TextRole.Normal);
            view.AddBlankLine();
            view.AddBlankLine();
            view.AddBlankLine();
            view.AddLine("Line 2", TextRole.Normal);

            // Line 1, 1 blank line, Line 2 = 3 lines total
            Assert.Equal(3, view.CommittedLines.Count);
            Assert.Equal("Line 1", view.CommittedLines[0].Spans[0].Text);
            Assert.Equal(0, view.CommittedLines[1].Length);
            Assert.Equal("Line 2", view.CommittedLines[2].Spans[0].Text);
        }
    }
}
