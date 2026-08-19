using TerminalQuest.Ui;
using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class CommandTitleViewTests
    {
        [Fact]
        public void BuildLine_when_idle_displays_command_text_with_command_role()
        {
            var line = CommandTitleView.BuildLine(isBusy: false, step: 0, width: 80);

            Assert.Equal(80, line.Length);
            var text = string.Concat(line.Spans.Select(s => s.Text));
            Assert.StartsWith("Command", text);
            Assert.Equal(TextRole.Command, line.Spans[0].Role);
        }

        [Fact]
        public void BuildLine_when_busy_displays_command_prefix_and_serpent()
        {
            // At step 12, lane starts at col 8 ("Command ").
            // RelCol 12 (abs col 20) is head ⪢, relCols 0..11 (abs cols 8..19) are 12 body cells.
            var line = CommandTitleView.BuildLine(isBusy: true, step: 12, width: 80);

            Assert.Equal(80, line.Length);
            var text = string.Concat(line.Spans.Select(s => s.Text));
            Assert.StartsWith("Command ~≈~≈~≈~≈~≈~≈⪢", text);

            // Spans verification
            Assert.Equal(TextRole.Command, line.Spans[0].Role);
            Assert.Equal("Command", line.Spans[0].Text);

            Assert.Equal(TextRole.Normal, line.Spans[1].Role);
            Assert.Equal(" ", line.Spans[1].Text);

            Assert.Equal(TextRole.Place, line.Spans[2].Role);
            Assert.Equal("~≈~≈~≈~≈~≈~≈", line.Spans[2].Text);

            Assert.Equal(TextRole.Item, line.Spans[3].Role);
            Assert.Equal("⪢", line.Spans[3].Text);
        }

        [Fact]
        public void BuildLine_centered_thinking_text_is_displayed_and_obscured_by_serpent()
        {
            // Width = 80: laneWidth = 72, thinking text ("narrator thinking...") is centered at cols 34..53.
            var lineIdleStep0 = CommandTitleView.BuildLine(isBusy: true, step: 0, width: 80);
            var text0 = string.Concat(lineIdleStep0.Spans.Select(s => s.Text));

            // Thinking text is present in the middle
            Assert.Contains("narrator thinking...", text0);
            Assert.Contains(lineIdleStep0.Spans, s => s.Role == TextRole.Speech && s.Text == "narrator thinking...");

            // At step 26: head is at column 34, obscuring the 'n' of "narrator thinking..."
            var lineStep26 = CommandTitleView.BuildLine(isBusy: true, step: 26, width: 80);
            var text26 = string.Concat(lineStep26.Spans.Select(s => s.Text));
            Assert.Contains("⪢arrator thinking...", text26);

            // At step 30: head is at column 38 (obscuring 'a'), body covers cols 34..37 ("narr")
            var lineStep30 = CommandTitleView.BuildLine(isBusy: true, step: 30, width: 80);
            var text30 = string.Concat(lineStep30.Spans.Select(s => s.Text));
            Assert.Contains("⪢tor thinking...", text30);
        }

        [Fact]
        public void BuildLine_characters_are_consistent_per_column_across_steps()
        {
            var line0 = CommandTitleView.BuildLine(isBusy: true, step: 12, width: 50);
            var line1 = CommandTitleView.BuildLine(isBusy: true, step: 13, width: 50);
            var line2 = CommandTitleView.BuildLine(isBusy: true, step: 14, width: 50);

            Assert.Equal(50, line0.Length);
            Assert.Equal(50, line1.Length);
            Assert.Equal(50, line2.Length);

            var text0 = string.Concat(line0.Spans.Select(s => s.Text));
            var text1 = string.Concat(line1.Spans.Select(s => s.Text));
            var text2 = string.Concat(line2.Spans.Select(s => s.Text));

            Assert.StartsWith("Command ~≈~≈~≈~≈~≈~≈⪢", text0);
            Assert.StartsWith("Command  ≈~≈~≈~≈~≈~≈~⪢", text1);
            Assert.StartsWith("Command   ~≈~≈~≈~≈~≈~≈⪢", text2);

            // Column 9 (first after space in step 13) is '≈' in step 12 and 13, then ' ' in 14
            Assert.Equal('≈', text0[9]);
            Assert.Equal('≈', text1[9]);
            Assert.Equal(' ', text2[9]);

            // Column 10 is '~' in step 12, 13, and 14
            Assert.Equal('~', text0[10]);
            Assert.Equal('~', text1[10]);
            Assert.Equal('~', text2[10]);

            // Column 11 is '≈' in step 12, 13, and 14
            Assert.Equal('≈', text0[11]);
            Assert.Equal('≈', text1[11]);
            Assert.Equal('≈', text2[11]);
        }

        [Fact]
        public void BuildLine_continuous_toroidal_cell_wrap()
        {
            const int width = 18; // Prefix "Command " (8 chars) + lane (10 chars)
            // laneWidth = 10, serpentBodyLen = 9.
            // step 9: head at relCol 9 (absCol 17, right edge), body at relCols 0..8 (absCols 8..16)
            var lineAtEdge = CommandTitleView.BuildLine(isBusy: true, step: 9, width: width);
            // step 10: head wraps to relCol 0 (absCol 8), body at relCols 1..9 (absCols 9..17)
            var lineHeadWrapped = CommandTitleView.BuildLine(isBusy: true, step: 10, width: width);
            // step 11: head at relCol 1 (absCol 9), body at relCol 0 (absCol 8) and relCols 2..9 (absCols 10..17)
            var lineNext = CommandTitleView.BuildLine(isBusy: true, step: 11, width: width);

            Assert.Equal(width, lineAtEdge.Length);
            Assert.Equal(width, lineHeadWrapped.Length);
            Assert.Equal(width, lineNext.Length);

            var textAtEdge = string.Concat(lineAtEdge.Spans.Select(s => s.Text));
            var textHeadWrapped = string.Concat(lineHeadWrapped.Spans.Select(s => s.Text));
            var textNext = string.Concat(lineNext.Spans.Select(s => s.Text));

            Assert.Equal("Command ~≈~≈~≈~≈~⪢", textAtEdge);
            Assert.Equal("Command ⪢≈~≈~≈~≈~≈", textHeadWrapped);
            Assert.Equal("Command ~⪢~≈~≈~≈~≈", textNext);
        }

        [Fact]
        public void BuildLine_when_notice_present_displays_notice()
        {
            var line = CommandTitleView.BuildLine(isBusy: false, step: 0, width: 50, notice: "Editing in external editor...");

            Assert.Equal(50, line.Length);
            var text = string.Concat(line.Spans.Select(s => s.Text));
            Assert.StartsWith("Editing in external editor...", text);
            Assert.Equal(TextRole.Command, line.Spans[0].Role);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(5)]
        [InlineData(8)]
        [InlineData(10)]
        [InlineData(18)]
        [InlineData(40)]
        [InlineData(80)]
        [InlineData(120)]
        public void BuildLine_handles_various_widths_without_exceeding_width(int width)
        {
            for (var step = 0; step < 25; step++)
            {
                var idleLine = CommandTitleView.BuildLine(isBusy: false, step, width);
                var busyLine = CommandTitleView.BuildLine(isBusy: true, step, width);

                Assert.True(idleLine.Length <= Math.Max(0, width), $"idleLine.Length {idleLine.Length} exceeded width {width}");
                Assert.True(busyLine.Length <= Math.Max(0, width), $"busyLine.Length {busyLine.Length} exceeded width {width} at step {step}");
            }
        }

        [Fact]
        public void IsBusy_controls_animation_and_step()
        {
            using var view = new CommandTitleView();
            Assert.False(view.IsBusy);
            Assert.Equal(0, view.AnimationStep);

            view.IsBusy = true;
            Assert.True(view.IsBusy);

            view.TickAnimation();
            Assert.Equal(1, view.AnimationStep);

            view.TickAnimation();
            Assert.Equal(2, view.AnimationStep);

            view.IsBusy = false;
            Assert.False(view.IsBusy);
            Assert.Equal(0, view.AnimationStep);
        }
    }
}
