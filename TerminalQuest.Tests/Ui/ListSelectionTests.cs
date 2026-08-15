using System.Collections.ObjectModel;
using System.Data;

using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Agents;
using TerminalQuest.Settings;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// Tests for list and suggestion selection handling.
    /// </summary>
    public sealed class ListSelectionTests
    {
        private static readonly SuggestionItem[] Commands =
        [
            new("/look ", "/look", "Look around."),
            new("/inventory ", "/inventory", "What you are carrying."),
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
        public void Suggestion_strip_selection_clamps_at_boundaries()
        {
            using var view = new CommandSuggestionView { Suggestions = Commands };
            view.MoveSelection(-5);
            Assert.Equal(0, view.SelectedItem);

            view.MoveSelection(10);
            Assert.Equal(1, view.SelectedItem);
        }

        [Fact]
        public void ListView_navigates_up_and_down_with_keys()
        {
            using var list = new ListView();
            list.SetSource(new ObservableCollection<string>(["First", "Second", "Third"]));
            list.SelectedItem = 0;

            list.NewKeyDownEvent(Key.CursorDown);
            Assert.Equal(1, list.SelectedItem);

            list.NewKeyDownEvent(Key.CursorDown);
            Assert.Equal(2, list.SelectedItem);

            list.NewKeyDownEvent(Key.CursorUp);
            Assert.Equal(1, list.SelectedItem);
        }

        [Fact]
        public void TableView_navigates_up_and_down_with_keys()
        {
            using var table = new TableView();
            var dt = new DataTable();
            dt.Columns.Add("Col1");
            dt.Rows.Add("Row0");
            dt.Rows.Add("Row1");
            dt.Rows.Add("Row2");
            table.Table = new DataTableSource(dt);
            table.SetSelection(0, 0, false);

            Assert.Equal(0, table.Value?.SelectedCell.Y);

            table.NewKeyDownEvent(Key.CursorDown);
            Assert.Equal(1, table.Value?.SelectedCell.Y);

            table.NewKeyDownEvent(Key.CursorDown);
            Assert.Equal(2, table.Value?.SelectedCell.Y);

            table.NewKeyDownEvent(Key.CursorUp);
            Assert.Equal(1, table.Value?.SelectedCell.Y);
        }

        [Fact]
        public void NewCharacterWindow_tab_cycles_through_all_controls()
        {
            var window = new NewCharacterWindow("TestSave");
            
            var sequence = new System.Collections.Generic.List<Type>();
            window.SetFocus();

            for (var i = 0; i < 9; i++)
            {
                sequence.Add(window.MostFocused!.GetType());
                window.AdvanceFocus(NavigationDirection.Forward, TabBehavior.TabStop);
            }

            Assert.Equal(
                new[]
                {
                    typeof(ListView),
                    typeof(Markdown),
                    typeof(TextField),
                    typeof(TextField),
                    typeof(TextField),
                    typeof(Button),
                    typeof(Button),
                    typeof(Button),
                    typeof(ListView),
                },
                sequence);
        }

        [Fact]
        public void SettingsWindow_selection_vs_picking_behavior()
        {
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            // Find the provider list view (has 2 options: Claude Code & LM Studio)
            var providerList = window.SubViews
                .OfType<Tabs>()
                .Single()
                .SubViews
                .SelectMany(t => t.SubViews)
                .OfType<ListView>()
                .First(lv => lv.Source?.Count == 2);

            Assert.Equal(0, providerList.SelectedItem);

            // Move selection down to LM Studio (1)
            providerList.SelectedItem = 1;

            // Notice: window.Chosen is still null and draft provider has not been saved until picked
            Assert.Null(window.Chosen);

            // Simulate Enter key / Accepting on the provider list to commit pick
            providerList.NewKeyDownEvent(Key.Enter);

            // Save settings via Ctrl+S
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal(AgentProvider.LmStudio, window.Chosen.Provider);
        }
    }
}
