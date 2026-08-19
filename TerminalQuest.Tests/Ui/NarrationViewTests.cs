using TerminalQuest.Ui;
using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class NarrationViewTests
    {
        [Fact]
        public void BuildWaitingRow_creates_correct_serpent_at_standard_width()
        {
            // At step 12, head is at column 12 and the 12 body cells occupy columns 0..11
            var row = NarrationView.BuildWaitingRow(12, 80);

            Assert.Equal(80, row.Length);
            var text = string.Concat(row.Spans.Select(s => s.Text));
            Assert.StartsWith("~≈~≈~≈~≈~≈~≈⪢", text);

            // Verify spans and roles
            Assert.Equal(TextRole.Place, row.Spans[0].Role); // Body
            Assert.Equal("~≈~≈~≈~≈~≈~≈", row.Spans[0].Text);

            Assert.Equal(TextRole.Item, row.Spans[1].Role); // Head
            Assert.Equal("⪢", row.Spans[1].Text);
        }

        [Fact]
        public void BuildWaitingRow_characters_are_consistent_per_column_across_steps()
        {
            // At step 12: columns 0..11 are body, col 12 is head ⪢
            // At step 13: col 0 is space, cols 1..12 are body, col 13 is head ⪢
            // At step 14: cols 0..1 are space, cols 2..13 are body, col 14 is head ⪢
            var row0 = NarrationView.BuildWaitingRow(12, 50);
            var row1 = NarrationView.BuildWaitingRow(13, 50);
            var row2 = NarrationView.BuildWaitingRow(14, 50);

            Assert.Equal(50, row0.Length);
            Assert.Equal(50, row1.Length);
            Assert.Equal(50, row2.Length);

            var text0 = string.Concat(row0.Spans.Select(s => s.Text));
            var text1 = string.Concat(row1.Spans.Select(s => s.Text));
            var text2 = string.Concat(row2.Spans.Select(s => s.Text));

            Assert.StartsWith("~≈~≈~≈~≈~≈~≈⪢", text0);
            Assert.StartsWith(" ≈~≈~≈~≈~≈~≈~⪢", text1);
            Assert.StartsWith("  ~≈~≈~≈~≈~≈~≈⪢", text2);

            // Column 1 is '≈' in step 12 and step 13, then ' ' in step 14
            Assert.Equal('≈', text0[1]);
            Assert.Equal('≈', text1[1]);
            Assert.Equal(' ', text2[1]);

            // Column 2 is '~' in step 12, step 13, and step 14
            Assert.Equal('~', text0[2]);
            Assert.Equal('~', text1[2]);
            Assert.Equal('~', text2[2]);

            // Column 3 is '≈' in step 12, step 13, and step 14
            Assert.Equal('≈', text0[3]);
            Assert.Equal('≈', text1[3]);
            Assert.Equal('≈', text2[3]);
        }

        [Fact]
        public void BuildWaitingRow_continuous_toroidal_cell_wrap()
        {
            const int width = 10;
            // width = 10, serpentBodyLen = 9.
            // step 9: head at column 9 (right edge), body at cols 0..8
            var rowAtEdge = NarrationView.BuildWaitingRow(9, width);
            // step 10: head wraps to column 0, body at cols 1..9
            var rowHeadWrapped = NarrationView.BuildWaitingRow(10, width);
            // step 11: head at column 1, body at col 0 and cols 2..9
            var rowNext = NarrationView.BuildWaitingRow(11, width);

            Assert.Equal(width, rowAtEdge.Length);
            Assert.Equal(width, rowHeadWrapped.Length);
            Assert.Equal(width, rowNext.Length);

            var textAtEdge = string.Concat(rowAtEdge.Spans.Select(s => s.Text));
            var textHeadWrapped = string.Concat(rowHeadWrapped.Spans.Select(s => s.Text));
            var textNext = string.Concat(rowNext.Spans.Select(s => s.Text));

            // Column by column verification:
            // step 9: "~≈~≈~≈~≈~⪢"
            Assert.Equal("~≈~≈~≈~≈~⪢", textAtEdge);
            // step 10: "⪢≈~≈~≈~≈~≈" (head is at 0, body occupies 1..9 seamlessly)
            Assert.Equal("⪢≈~≈~≈~≈~≈", textHeadWrapped);
            // step 11: "~⪢~≈~≈~≈~≈" (head is at 1, col 0 has body ~, cols 2..9 have body)
            Assert.Equal("~⪢~≈~≈~≈~≈", textNext);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(5)]
        [InlineData(10)]
        [InlineData(15)]
        [InlineData(20)]
        [InlineData(40)]
        [InlineData(80)]
        [InlineData(120)]
        public void BuildWaitingRow_handles_various_widths_without_exceeding_width(int width)
        {
            for (var step = 0; step < 25; step++)
            {
                var row = NarrationView.BuildWaitingRow(step, width);
                Assert.True(row.Length <= Math.Max(0, width), $"row.Length {row.Length} exceeded width {width} at step {step}");
            }
        }

        [Fact]
        public void IsWaiting_increases_total_rows_and_shows_serpent_row()
        {
            using var view = new NarrationView();
            view.AddLine("First line of story", TextRole.Normal);

            Assert.Equal(1, view.TotalRows);

            view.IsWaiting = true;
            Assert.Equal(2, view.TotalRows);

            var waitingLine = view.CommittedLines;
            // Committed lines do not include transient waiting line
            Assert.Single(waitingLine);

            view.IsWaiting = false;
            Assert.Equal(1, view.TotalRows);
        }

        [Fact]
        public void AppendDelta_hides_waiting_row()
        {
            using var view = new NarrationView();
            view.IsWaiting = true;
            Assert.Equal(1, view.TotalRows);

            view.AppendDelta("The ancient serpent speaks...");
            Assert.Equal(1, view.TotalRows); // Waiting row replaced by the streamed delta row

            view.CommitBlock();
            Assert.Equal(1, view.TotalRows);
            Assert.False(view.IsWaiting);
        }

        [Fact]
        public void TickAnimation_advances_animation_step()
        {
            using var view = new NarrationView();
            view.IsWaiting = true;
            Assert.Equal(0, view.AnimationStep);

            view.TickAnimation();
            Assert.Equal(1, view.AnimationStep);

            view.TickAnimation();
            Assert.Equal(2, view.AnimationStep);
        }
    }
}
