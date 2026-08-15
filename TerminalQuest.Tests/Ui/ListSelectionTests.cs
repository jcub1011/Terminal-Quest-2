using TerminalQuest.Saves;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// Where the highlight goes when a list is refilled - and, the reason this exists, where it goes
    /// when a list is refilled with nothing.
    /// <para>
    /// Terminal.Gui refuses an index into an empty list, zero included, so every one of these
    /// setters used to throw the moment its collection came back empty. The suggestion strip did it
    /// on every keystroke that closed it; the save list would have done it the first time the player
    /// opened the load menu with no saves.
    /// </para>
    /// <para>
    /// These construct views but never draw them, so none of this needs a terminal.
    /// </para>
    /// </summary>
    public sealed class ListSelectionTests
    {
        private static readonly PlayerCommandInfo[] Commands =
        [
            new("look", string.Empty, "Look around."),
            new("inventory", string.Empty, "What you are carrying."),
        ];

        private static readonly MenuRow[] Rows =
        [
            new("Adapter", "Claude"),
            new("Model", "opus"),
        ];

        private static readonly SaveEntry[] Saves =
        [
            new("Alice", DateTimeOffset.UnixEpoch, 3, 1024),
            new("Bob", DateTimeOffset.UnixEpoch, 7, 2048),
        ];

        [Fact]
        public void Emptying_the_suggestion_strip_takes_the_highlight_off_rather_than_throwing()
        {
            using var view = new CommandSuggestionView { Suggestions = Commands };
            Assert.Equal(0, view.SelectedItem);

            view.Suggestions = [];

            Assert.Null(view.SelectedItem);
            Assert.Null(view.Selected);
        }

        [Fact]
        public void A_refilled_suggestion_strip_offers_its_first_command_again()
        {
            using var view = new CommandSuggestionView { Suggestions = Commands };
            view.MoveSelection(1);
            Assert.Equal(1, view.SelectedItem);

            // Closed and reopened: the cursor belongs at the top of the new list, not wherever it
            // was left in the old one.
            view.Suggestions = [];
            view.Suggestions = Commands;

            Assert.Equal(0, view.SelectedItem);
            Assert.Equal(Commands[0], view.Selected);
        }

        [Fact]
        public void An_empty_save_list_has_no_highlight_to_move_or_place()
        {
            using var view = new SaveListView { Saves = Saves };
            view.SelectedIndex = 1;

            view.Saves = [];

            Assert.Null(view.SelectedItem);
            Assert.Null(view.Selected);

            // The save menu keeps driving the list whether or not there is anything in it.
            view.MoveSelection(1);
            view.SelectedIndex = 0;
            view.Select("Alice");

            Assert.Null(view.SelectedItem);
        }

        [Fact]
        public void An_empty_menu_page_has_no_highlight_to_move_or_place()
        {
            using var view = new MenuListView { Rows = Rows };
            view.SelectedIndex = 1;

            view.Rows = [];

            Assert.Null(view.SelectedItem);

            // SettingsWindow restores the remembered cursor straight after replacing the rows.
            view.SelectedIndex = 1;
            view.MoveSelection(-1);

            Assert.Null(view.SelectedItem);
            Assert.Equal(0, view.SelectedIndex);
        }

        [Fact]
        public void An_empty_class_list_has_no_highlight()
        {
            using var view = new ClassListView();
            Assert.Equal(0, view.SelectedItem);

            view.Classes = [];

            Assert.Null(view.SelectedItem);
            Assert.Null(view.Selected);
        }

        [Fact]
        public void A_highlight_past_the_end_of_a_shrunken_list_comes_back_into_range()
        {
            using var view = new MenuListView { Rows = Rows };
            view.SelectedIndex = 1;

            view.Rows = [Rows[0]];

            Assert.Equal(0, view.SelectedItem);
        }
    }
}
