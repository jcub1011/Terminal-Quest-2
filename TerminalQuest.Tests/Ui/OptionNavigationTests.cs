using System.Drawing;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Color = Terminal.Gui.Drawing.Color;
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
        public void OptionsView_exposes_and_updates_highlighted_option()
        {
            var view = new OptionsView();
            view.SetOptions(["Go North", "Go South"]);

            Assert.Equal(2, view.Options.Count);
            Assert.Null(view.HighlightedOption);

            view.HighlightedOption = 1;
            Assert.Equal(1, view.HighlightedOption);

            view.HighlightedOption = 2;
            Assert.Equal(2, view.HighlightedOption);

            view.HighlightedOption = null;
            Assert.Null(view.HighlightedOption);
        }

        [Fact]
        public void ClearOptions_hides_and_empties_OptionsView()
        {
            var view = new OptionsView();
            view.SetOptions(["Go North", "Go South"]);
            Assert.True(view.Visible);
            Assert.Equal(2, view.Options.Count);

            view.ClearOptions();
            Assert.False(view.Visible);
            Assert.Empty(view.Options);
            Assert.Null(view.HighlightedOption);
            Assert.Equal(0, view.Height);
        }

        [Fact]
        public void Down_arrow_selects_first_option_and_populates_textbox_with_full_text()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.SetOptions(["Explore the ruins", "Return to the village", "Rest at the campfire"]);

            var inputField = window.SubViews.OfType<TextField>().First();

            // Press Down arrow -> Option 1
            var handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(1, window.Options.HighlightedOption);
            Assert.Equal("Explore the ruins", inputField.Text);

            // Press Down arrow again -> Option 2
            handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(2, window.Options.HighlightedOption);
            Assert.Equal("Return to the village", inputField.Text);

            // Press Down arrow again -> Option 3
            handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(3, window.Options.HighlightedOption);
            Assert.Equal("Rest at the campfire", inputField.Text);

            // Press Down arrow again -> clamped at Option 3
            handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(3, window.Options.HighlightedOption);
            Assert.Equal("Rest at the campfire", inputField.Text);

            // Press Up arrow -> Option 2
            handled = window.NewKeyDownEvent(Key.CursorUp);
            Assert.True(handled);
            Assert.Equal(2, window.Options.HighlightedOption);
            Assert.Equal("Return to the village", inputField.Text);

            // Press Up arrow -> Option 1
            handled = window.NewKeyDownEvent(Key.CursorUp);
            Assert.True(handled);
            Assert.Equal(1, window.Options.HighlightedOption);
            Assert.Equal("Explore the ruins", inputField.Text);

            // Press Up arrow -> clamped at Option 1
            handled = window.NewKeyDownEvent(Key.CursorUp);
            Assert.True(handled);
            Assert.Equal(1, window.Options.HighlightedOption);
            Assert.Equal("Explore the ruins", inputField.Text);
        }

        [Fact]
        public void Up_arrow_from_unselected_selects_last_option_with_full_text()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.SetOptions(["Explore the ruins", "Return to the village", "Rest at the campfire"]);

            var inputField = window.SubViews.OfType<TextField>().First();

            // Press Up arrow from empty input
            var handled = window.NewKeyDownEvent(Key.CursorUp);
            Assert.True(handled);

            Assert.Equal(3, window.Options.HighlightedOption);
            Assert.Equal("Rest at the campfire", inputField.Text);
        }

        [Fact]
        public void Typing_option_number_or_text_manually_updates_highlight()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.SetOptions(["Explore the ruins", "Return to the village"]);

            var inputField = window.SubViews.OfType<TextField>().First();

            // Typing "2" highlights Option 2
            inputField.Text = "2";
            Assert.Equal(2, window.Options.HighlightedOption);

            // Typing "1" highlights Option 1
            inputField.Text = "1";
            Assert.Equal(1, window.Options.HighlightedOption);

            // Typing full option text highlights Option 1
            inputField.Text = "Explore the ruins";
            Assert.Equal(1, window.Options.HighlightedOption);

            // Typing non-matching number "99" clears highlight
            inputField.Text = "99";
            Assert.Null(window.Options.HighlightedOption);

            // Typing custom text clears highlight
            inputField.Text = "look around";
            Assert.Null(window.Options.HighlightedOption);

            // Clearing text clears highlight
            inputField.Text = string.Empty;
            Assert.Null(window.Options.HighlightedOption);
        }

        [Fact]
        public void Arrows_do_nothing_when_no_options_present()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            var handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.False(handled);
            Assert.Null(window.Options.HighlightedOption);
        }

        [Fact]
        public void Navigating_after_manual_typing_continues_from_matching_option()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.SetOptions(["Explore the ruins", "Return to the village", "Rest at the campfire"]);

            var inputField = window.SubViews.OfType<TextField>().First();

            // Type "2" manually
            inputField.Text = "2";
            Assert.Equal(2, window.Options.HighlightedOption);

            // Press Down arrow -> moves to Option 3 with full text
            var handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(3, window.Options.HighlightedOption);
            Assert.Equal("Rest at the campfire", inputField.Text);

            // Press Up arrow -> moves back to Option 2 with full text
            handled = window.NewKeyDownEvent(Key.CursorUp);
            Assert.True(handled);
            Assert.Equal(2, window.Options.HighlightedOption);
            Assert.Equal("Return to the village", inputField.Text);
        }

        [Fact]
        public void Clicking_an_option_selects_it_and_populates_textbox()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.SetOptions(["Explore the ruins", "Return to the village", "Rest at the campfire"]);

            var inputField = window.SubViews.OfType<TextField>().First();

            // Simulate clicking row 1 (Option 2)
            window.Options.NewMouseEvent(new Mouse
            {
                Flags = MouseFlags.LeftButtonClicked,
                Position = new Point(5, 1),
            });

            Assert.Equal(2, window.Options.HighlightedOption);
            Assert.Equal("Return to the village", inputField.Text);
        }

        [Fact]
        public void Submitting_world_command_clears_options_and_highlight()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.SetOptions(["Explore the ruins", "Return to the village"]);

            string? entered = null;
            window.CommandEntered += cmd => entered = cmd;

            // Select Option 1
            window.NewKeyDownEvent(Key.CursorDown);
            Assert.Equal(1, window.Options.HighlightedOption);

            var inputField = window.SubViews.OfType<TextField>().First();
            Assert.Equal("Explore the ruins", inputField.Text);

            // Submit via Enter on input field
            inputField.NewKeyDownEvent(Key.Enter);

            Assert.Equal("Explore the ruins", entered);
            Assert.Null(window.Options.HighlightedOption);
            Assert.Empty(window.Options.Options);
            Assert.Equal(string.Empty, inputField.Text);
        }

        [Fact]
        public void Option_navigation_works_after_running_player_command()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            window.SetOptions(["Explore the ruins", "Return to the village"]);

            // Simulate executing a player command (/character)
            window.Narration.AddBlankLine();
            window.Narration.AddLine("> /character", TextRole.Command);
            window.Narration.AddLine("Who you know", TextRole.System);
            window.Narration.AddLine("  Rowan  10/10  (you)", TextRole.Normal);
            window.Narration.AddBlankLine();

            // Options should still be active and visible at the bottom
            Assert.Equal(2, window.Options.Options.Count);

            // Press Down arrow -> should select Option 1
            var handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(1, window.Options.HighlightedOption);

            var inputField = window.SubViews.OfType<TextField>().First();
            Assert.Equal("Explore the ruins", inputField.Text);

            // Press Down arrow again -> Option 2
            handled = window.NewKeyDownEvent(Key.CursorDown);
            Assert.True(handled);
            Assert.Equal(2, window.Options.HighlightedOption);
            Assert.Equal("Return to the village", inputField.Text);
        }

        [Fact]
        public void OptionSelection_attribute_is_defined_in_theme()
        {
            Assert.Equal(Color.White, Theme.OptionSelection.Background);
            Assert.Equal(Color.Black, Theme.OptionSelection.Foreground);
        }

        [Fact]
        public void Layout_and_dimensions_are_correct_when_options_are_set()
        {
            var state = new GameState();
            using var window = new GameWindow(state);

            Assert.False(window.Options.Visible);
            Assert.Equal(0, window.Options.Height);

            window.SetOptions([
                "Kneel by the warding circle and examine what's disturbing it.",
                "Go to the window for a longer look at the wreck on the shoal.",
                "Grab the staff and head down into the village toward the bell.",
                "Consult the spellbook for anything on wards reacting like this."
            ]);

            Assert.True(window.Options.Visible);
            Assert.Equal(4, window.Options.Options.Count);
            Assert.True(window.Options.CalculateRequiredHeight(80) >= 4);
        }

        [Fact]
        public void NarrationView_scrollbar_click_seeks_proportionally()
        {
            var view = new NarrationView
            {
                Width = 40,
                Height = 10,
            };
            view.Viewport = new Rectangle(0, 0, 40, 10);

            for (var i = 1; i <= 50; i++)
            {
                view.AddLine($"Line {i}", TextRole.Normal);
            }

            Assert.True(view.TotalRows > 10);

            // Click at top of scrollbar column (x=39, y=0)
            view.NewMouseEvent(new Mouse
            {
                Flags = MouseFlags.LeftButtonClicked,
                Position = new Point(39, 0),
            });
            Assert.Equal(0, view.Viewport.Y);

            // Click at bottom of scrollbar column (x=39, y=9)
            view.NewMouseEvent(new Mouse
            {
                Flags = MouseFlags.LeftButtonClicked,
                Position = new Point(39, 9),
            });
            Assert.Equal(view.TotalRows - 10, view.Viewport.Y);
        }
    }
}
