using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.Views;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class OptionNavigationTests
    {
        [Fact]
        public void NarrationView_exposes_and_updates_highlighted_option()
        {
            var view = new NarrationView();
            view.AddLine("What do you do?", TextRole.Normal);
            view.AddLine("1. Go North", TextRole.Normal);
            view.AddLine("2. Go South", TextRole.Normal);

            var options = view.GetActiveOptions();
            Assert.Equal(2, options.Count);

            Assert.Null(view.HighlightedOption);

            view.HighlightedOption = 1;
            Assert.Equal(1, view.HighlightedOption);

            view.HighlightedOption = 2;
            Assert.Equal(2, view.HighlightedOption);

            view.HighlightedOption = null;
            Assert.Null(view.HighlightedOption);
        }

        [Fact]
        public void Adding_a_line_clears_the_highlighted_option()
        {
            var view = new NarrationView();
            view.AddLine("1. Go North", TextRole.Normal);
            view.HighlightedOption = 1;
            Assert.Equal(1, view.HighlightedOption);

            view.AddLine("> 1", TextRole.Command);
            Assert.Null(view.HighlightedOption);
        }

        [Fact]
        public void Appending_a_delta_clears_the_highlighted_option()
        {
            var view = new NarrationView();
            view.AddLine("1. Go North", TextRole.Normal);
            view.HighlightedOption = 1;
            Assert.Equal(1, view.HighlightedOption);

            view.AppendDelta("New turn starting...");
            Assert.Null(view.HighlightedOption);
        }

        [Fact]
        public void Down_arrow_selects_first_option_and_populates_textbox()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.Narration.AddLine("What do you do?", TextRole.Normal);
            window.Narration.AddLine("1. Explore the ruins", TextRole.Normal);
            window.Narration.AddLine("2. Return to the village", TextRole.Normal);
            window.Narration.AddLine("3. Rest at the campfire", TextRole.Normal);

            // Press Down arrow
            var handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(1, window.Narration.HighlightedOption);

            // Press Down arrow again -> Option 2
            handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(2, window.Narration.HighlightedOption);

            // Press Down arrow again -> Option 3
            handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(3, window.Narration.HighlightedOption);

            // Press Down arrow again -> clamped at Option 3
            handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(3, window.Narration.HighlightedOption);

            // Press Up arrow -> Option 2
            handled = window.NewKeyDownEvent(Key.CursorUp);
            Assert.True(handled);
            Assert.Equal(2, window.Narration.HighlightedOption);

            // Press Up arrow -> Option 1
            handled = window.NewKeyDownEvent(Key.CursorUp);
            Assert.True(handled);
            Assert.Equal(1, window.Narration.HighlightedOption);

            // Press Up arrow -> clamped at Option 1
            handled = window.NewKeyDownEvent(Key.CursorUp);
            Assert.True(handled);
            Assert.Equal(1, window.Narration.HighlightedOption);
        }

        [Fact]
        public void Up_arrow_from_unselected_selects_last_option()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.Narration.AddLine("What do you do?", TextRole.Normal);
            window.Narration.AddLine("1. Explore the ruins", TextRole.Normal);
            window.Narration.AddLine("2. Return to the village", TextRole.Normal);
            window.Narration.AddLine("3. Rest at the campfire", TextRole.Normal);

            // Press Up arrow from empty input
            var handled = window.NewKeyDownEvent(Key.CursorUp);
            Assert.True(handled);

            Assert.Equal(3, window.Narration.HighlightedOption);
        }

        [Fact]
        public void Typing_option_number_manually_updates_highlight()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.Narration.AddLine("What do you do?", TextRole.Normal);
            window.Narration.AddLine("1. Explore the ruins", TextRole.Normal);
            window.Narration.AddLine("2. Return to the village", TextRole.Normal);

            var inputField = window.SubViews.OfType<TextField>().First();

            // Typing "2" highlights Option 2
            inputField.Text = "2";
            Assert.Equal(2, window.Narration.HighlightedOption);

            // Typing "1" highlights Option 1
            inputField.Text = "1";
            Assert.Equal(1, window.Narration.HighlightedOption);

            // Typing non-matching number "99" clears highlight
            inputField.Text = "99";
            Assert.Null(window.Narration.HighlightedOption);

            // Typing custom text clears highlight
            inputField.Text = "look around";
            Assert.Null(window.Narration.HighlightedOption);

            // Clearing text clears highlight
            inputField.Text = string.Empty;
            Assert.Null(window.Narration.HighlightedOption);
        }

        [Fact]
        public void Arrows_do_nothing_when_no_options_present()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.Narration.AddLine("No choices here, just narration.", TextRole.Normal);

            var handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.False(handled);
            Assert.Null(window.Narration.HighlightedOption);
        }

        [Fact]
        public void Navigating_after_manual_typing_continues_from_typed_option()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.Narration.AddLine("What do you do?", TextRole.Normal);
            window.Narration.AddLine("1. Explore the ruins", TextRole.Normal);
            window.Narration.AddLine("2. Return to the village", TextRole.Normal);
            window.Narration.AddLine("3. Rest at the campfire", TextRole.Normal);

            var inputField = window.SubViews.OfType<TextField>().First();

            // Type "2" manually
            inputField.Text = "2";
            Assert.Equal(2, window.Narration.HighlightedOption);

            // Press Down arrow -> moves to Option 3
            var handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(3, window.Narration.HighlightedOption);
            Assert.Equal("3", inputField.Text);

            // Press Up arrow -> moves back to Option 2
            handled = window.NewKeyDownEvent(Key.CursorUp);
            Assert.True(handled);
            Assert.Equal(2, window.Narration.HighlightedOption);
            Assert.Equal("2", inputField.Text);
        }

        [Fact]
        public void Submitting_command_clears_highlight()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.Narration.AddLine("What do you do?", TextRole.Normal);
            window.Narration.AddLine("1. Explore the ruins", TextRole.Normal);
            window.Narration.AddLine("2. Return to the village", TextRole.Normal);

            string? entered = null;
            window.CommandEntered += cmd => entered = cmd;

            // Select Option 1
            window.NewKeyDownEvent(Key.CursorDown);
            Assert.Equal(1, window.Narration.HighlightedOption);

            var inputField = window.SubViews.OfType<TextField>().First();
            Assert.Equal("1", inputField.Text);

            // Submit via Enter on input field
            inputField.NewKeyDownEvent(Key.Enter);

            Assert.Equal("1", entered);
            Assert.Null(window.Narration.HighlightedOption);
            Assert.Equal(string.Empty, inputField.Text);
        }

        [Fact]
        public void Option_navigation_works_after_running_player_command()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.Narration.AddLine("What do you do?", TextRole.Normal);
            window.Narration.AddLine("1. Explore the ruins", TextRole.Normal);
            window.Narration.AddLine("2. Return to the village", TextRole.Normal);

            // Simulate executing a player command (/character)
            window.Narration.AddBlankLine();
            window.Narration.AddLine("> /character", TextRole.Command);
            window.Narration.AddLine("Who you know", TextRole.System);
            window.Narration.AddLine("  Rowan  10/10  (you)", TextRole.Normal);
            window.Narration.AddBlankLine();

            // Press Down arrow -> should select Option 1 from earlier narration turn
            var handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(1, window.Narration.HighlightedOption);

            var inputField = window.SubViews.OfType<TextField>().First();
            Assert.Equal("1", inputField.Text);

            // Press Down arrow again -> Option 2
            handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(2, window.Narration.HighlightedOption);
            Assert.Equal("2", inputField.Text);
        }

        [Fact]
        public void OptionSelection_attribute_is_defined_in_theme()
        {
            Assert.Equal(Color.White, Theme.OptionSelection.Background);
            Assert.Equal(Color.Black, Theme.OptionSelection.Foreground);
        }
    }
}
