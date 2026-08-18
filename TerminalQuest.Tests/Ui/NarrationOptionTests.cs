using TerminalQuest.Ui;
using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class NarrationOptionTests
    {
        [Fact]
        public void Empty_options_yields_zero_height_and_empty_list()
        {
            var view = new OptionsView();
            view.SetOptions(Array.Empty<string>());

            Assert.Empty(view.Options);
            Assert.False(view.Visible);
            Assert.Equal(0, view.CalculateRequiredHeight(80));
        }

        [Fact]
        public void Standard_options_are_indexed_from_one()
        {
            var view = new OptionsView();
            view.SetOptions([
                "Approach the iron fountain",
                "Inspect the heavy wooden gate",
                "Call out into the mist"
            ]);

            Assert.Equal(3, view.Options.Count);
            Assert.Equal(1, view.Options[0].Number);
            Assert.Equal("Approach the iron fountain", view.Options[0].Text);

            Assert.Equal(2, view.Options[1].Number);
            Assert.Equal("Inspect the heavy wooden gate", view.Options[1].Text);

            Assert.Equal(3, view.Options[2].Number);
            Assert.Equal("Call out into the mist", view.Options[2].Text);
        }

        [Fact]
        public void Blank_and_whitespace_options_are_filtered()
        {
            var view = new OptionsView();
            view.SetOptions([
                "Option 1",
                "  ",
                "",
                "Option 2"
            ]);

            Assert.Equal(2, view.Options.Count);
            Assert.Equal(1, view.Options[0].Number);
            Assert.Equal("Option 1", view.Options[0].Text);
            Assert.Equal(2, view.Options[1].Number);
            Assert.Equal("Option 2", view.Options[1].Text);
        }

        [Fact]
        public void Required_height_calculates_wrapped_lines_correctly()
        {
            var view = new OptionsView();
            view.SetOptions([
                "Short option",
                "A very long option text that exceeds the small available width and must wrap onto multiple lines to display properly"
            ]);

            // With ample width (e.g. 200), each option takes 1 line -> total 2
            Assert.Equal(2, view.CalculateRequiredHeight(200));

            // With small width (e.g. 30), the second option will wrap into several lines
            var smallHeight = view.CalculateRequiredHeight(30);
            Assert.True(smallHeight > 2);
        }
    }
}
