using System.Drawing;

using Terminal.Gui.Input;
using Terminal.Gui.Views;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// Keyboard-first game screen: every mouse action on the choices, transcript and pack has
    /// a keyboard twin, and Tab reaches all three.
    /// </summary>
    public sealed class GameScreenKeyboardTests
    {
        private static GameWindow WindowWithOptions(params string[] options)
        {
            var window = new GameWindow(new GameState());
            window.SetOptions(options);
            return window;
        }

        private static TextField InputOf(GameWindow window) =>
            window.SubViews.OfType<TextField>().First();

        [Fact]
        public void Alt_digit_fills_input_and_highlights_choice()
        {
            using var window = WindowWithOptions("Explore the ruins", "Return to the village");
            var input = InputOf(window);

            var handled = window.NewKeyDownEvent(Key.D2.WithAlt);

            Assert.True(handled);
            Assert.Equal(2, window.Options.HighlightedOption);
            Assert.Equal("Return to the village", input.Text);
        }

        [Fact]
        public void Alt_digit_with_no_such_choice_is_ignored()
        {
            using var window = WindowWithOptions("Explore the ruins");
            var input = InputOf(window);

            var handled = window.NewKeyDownEvent(Key.D9.WithAlt);

            Assert.False(handled);
            Assert.Null(window.Options.HighlightedOption);
            Assert.Equal(string.Empty, input.Text);
        }

        [Fact]
        public void Typing_choice_number_and_pressing_enter_sends_its_words()
        {
            using var window = WindowWithOptions("Explore the ruins", "Return to the village");
            var input = InputOf(window);

            string? entered = null;
            window.CommandEntered += cmd => entered = cmd;

            input.Text = "2";
            Assert.Equal(2, window.Options.HighlightedOption);

            input.NewKeyDownEvent(Key.Enter);

            Assert.Equal("Return to the village", entered);
        }

        [Fact]
        public void Options_list_enter_submits_highlighted_choice()
        {
            using var window = WindowWithOptions("Explore the ruins", "Return to the village");

            string? entered = null;
            window.CommandEntered += cmd => entered = cmd;

            window.NewKeyDownEvent(Key.CursorDown);
            Assert.Equal(1, window.Options.HighlightedOption);

            var handled = window.Options.NewKeyDownEvent(Key.Enter);

            Assert.True(handled);
            Assert.Equal("Explore the ruins", entered);
        }

        [Fact]
        public void Options_list_digits_arrows_home_end_and_esc()
        {
            var view = new OptionsView();
            view.SetOptions(["First", "Second", "Third"]);

            string? highlighted = null;
            view.OptionHighlighted += opt => highlighted = opt.Text;

            var exited = false;
            view.ExitRequested += () => exited = true;

            Assert.True(view.NewKeyDownEvent(Key.D3));
            Assert.Equal(3, view.HighlightedOption);
            Assert.Equal("Third", highlighted);

            Assert.True(view.NewKeyDownEvent(Key.Home));
            Assert.Equal(1, view.HighlightedOption);

            Assert.True(view.NewKeyDownEvent(Key.End));
            Assert.Equal(3, view.HighlightedOption);

            Assert.True(view.NewKeyDownEvent(Key.CursorUp));
            Assert.Equal(2, view.HighlightedOption);

            Assert.True(view.NewKeyDownEvent(Key.Esc));
            Assert.True(exited);
        }

        private static void FillScrollableTranscript(GameWindow window)
        {
            window.Narration.Viewport = new Rectangle(0, 0, 40, 10);
            for (var i = 1; i <= 50; i++)
            {
                window.Narration.AddLine($"Line {i}", TextRole.Normal);
            }
        }

        [Fact]
        public void Ctrl_arrows_reach_transcript_from_window()
        {
            using var window = WindowWithOptions("Explore the ruins");
            FillScrollableTranscript(window);

            Assert.True(window.NewKeyDownEvent(Key.CursorUp.WithCtrl));
            Assert.True(window.NewKeyDownEvent(Key.CursorDown.WithCtrl));
            Assert.True(window.NewKeyDownEvent(Key.Home.WithCtrl));
            Assert.True(window.NewKeyDownEvent(Key.End.WithCtrl));
            Assert.True(window.NewKeyDownEvent(Key.PageUp.WithCtrl));
            Assert.True(window.NewKeyDownEvent(Key.PageDown.WithCtrl));
        }

        [Fact]
        public void Ctrl_Up_Down_scroll_without_moving_caret_while_editing()
        {
            using var window = WindowWithOptions("Explore the ruins");
            FillScrollableTranscript(window);
            var input = InputOf(window);
            input.Text = "hello world foo bar";
            input.InsertionPoint = 5;

            Assert.True(input.NewKeyDownEvent(Key.CursorUp.WithCtrl));

            // The key scrolled instead of editing: the caret never moved.
            Assert.Equal(5, input.InsertionPoint);
            Assert.Equal("hello world foo bar", input.Text);

            Assert.True(input.NewKeyDownEvent(Key.CursorDown.WithCtrl));
            Assert.Equal(5, input.InsertionPoint);
        }

        [Fact]
        public void Ctrl_scroll_with_nowhere_to_scroll_falls_through_to_field()
        {
            using var window = WindowWithOptions("Explore the ruins");
            var input = InputOf(window);
            input.Text = "hello world foo bar";
            input.InsertionPoint = 5;

            // Empty transcript: nothing to scroll, so the key keeps its editing meaning.
            Assert.False(window.Narration.NewKeyDownEvent(Key.CursorUp.WithCtrl));
            Assert.False(window.Narration.NewKeyDownEvent(Key.Home.WithCtrl));
        }

        [Fact]
        public void Ctrl_Left_Right_keep_word_jump_while_editing()
        {
            using var window = WindowWithOptions("Explore the ruins");
            var input = InputOf(window);
            input.Text = "hello world foo bar";
            input.InsertionPoint = 5;

            Assert.True(input.NewKeyDownEvent(Key.CursorRight.WithCtrl));
            Assert.Equal(6, input.InsertionPoint);

            Assert.True(input.NewKeyDownEvent(Key.CursorLeft.WithCtrl));
            Assert.Equal(0, input.InsertionPoint);
        }

        [Fact]
        public void CommandInputField_scroll_set_matches_window_forwarding()
        {
            Assert.True(CommandInputField.IsScrollKey(Key.CursorUp.WithCtrl));
            Assert.True(CommandInputField.IsScrollKey(Key.CursorDown.WithCtrl));
            Assert.True(CommandInputField.IsScrollKey(Key.Home.WithCtrl));
            Assert.True(CommandInputField.IsScrollKey(Key.End.WithCtrl));
            Assert.True(CommandInputField.IsScrollKey(Key.PageUp.WithCtrl));
            Assert.True(CommandInputField.IsScrollKey(Key.PageDown.WithCtrl));

            Assert.False(CommandInputField.IsScrollKey(Key.CursorLeft.WithCtrl));
            Assert.False(CommandInputField.IsScrollKey(Key.CursorRight.WithCtrl));
            Assert.False(CommandInputField.IsScrollKey(Key.CursorUp));
            Assert.False(CommandInputField.IsScrollKey(Key.PageDown));
        }

        [Fact]
        public void Transcript_ctrl_keys_scroll_linewise_and_rejoin()
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

            var bottom = view.Viewport.Y;
            Assert.True(bottom > 0);
            Assert.False(view.IsDetached);

            Assert.True(view.NewKeyDownEvent(Key.CursorUp.WithCtrl));
            Assert.Equal(bottom - 1, view.Viewport.Y);
            Assert.True(view.IsDetached);

            Assert.True(view.NewKeyDownEvent(Key.CursorDown.WithCtrl));
            Assert.Equal(bottom, view.Viewport.Y);
            Assert.False(view.IsDetached);

            Assert.True(view.NewKeyDownEvent(Key.Home.WithCtrl));
            Assert.Equal(0, view.Viewport.Y);
            Assert.True(view.IsDetached);

            Assert.True(view.NewKeyDownEvent(Key.End.WithCtrl));
            Assert.Equal(bottom, view.Viewport.Y);
            Assert.False(view.IsDetached);
        }

        [Fact]
        public void Transcript_ctrl_page_keys_scroll_a_page()
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

            var bottom = view.Viewport.Y;

            Assert.True(view.NewKeyDownEvent(Key.PageUp.WithCtrl));
            Assert.Equal(bottom - 9, view.Viewport.Y);
            Assert.True(view.IsDetached);

            Assert.True(view.NewKeyDownEvent(Key.PageDown.WithCtrl));
            Assert.Equal(bottom, view.Viewport.Y);
            Assert.False(view.IsDetached);
        }

        [Fact]
        public void Inventory_arrows_move_cursor_and_enter_inspects()
        {
            var view = new InventoryView();
            view.Viewport = new Rectangle(0, 0, 24, 10);
            view.SetItems([
                new InventoryEntry(1, "Rusted Key", "itm_1"),
                new InventoryEntry(2, "Healing Potion", "itm_2"),
            ]);

            Assert.Equal(0, view.CursorRow);

            string? inspected = null;
            view.EntityClicked += id => inspected = id;

            Assert.True(view.NewKeyDownEvent(Key.CursorDown));
            Assert.Equal(1, view.CursorRow);

            Assert.True(view.NewKeyDownEvent(Key.Enter));
            Assert.Equal("itm_2", inspected);

            Assert.True(view.NewKeyDownEvent(Key.Home));
            Assert.Equal(0, view.CursorRow);

            Assert.True(view.NewKeyDownEvent(Key.End));
            Assert.Equal(1, view.CursorRow);
        }

        [Fact]
        public void Inventory_enter_on_row_without_entity_is_ignored()
        {
            var view = new InventoryView();
            view.Viewport = new Rectangle(0, 0, 24, 10);
            view.SetItems([]);

            var raised = false;
            view.EntityClicked += _ => raised = true;

            Assert.False(view.NewKeyDownEvent(Key.Enter));
            Assert.False(raised);
        }

        [Fact]
        public void Entity_details_dialog_content_is_focusable_for_reading()
        {
            using var dialog = EntityDetailsDialog.CreateDialog(null, "Title", "line one\nline two");

            var content = dialog.SubViews.OfType<EntityDetailsContentView>().Single();
            Assert.True(content.CanFocus);
        }
    }
}
