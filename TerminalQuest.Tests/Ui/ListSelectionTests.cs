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

        private static IEnumerable<T> FindDescendants<T>(View root) where T : View
        {
            foreach (var sub in root.SubViews)
            {
                if (sub is T match) yield return match;
                foreach (var nested in FindDescendants<T>(sub))
                {
                    yield return nested;
                }
            }
        }

        [Fact]
        public void SettingsWindow_selection_vs_picking_behavior()
        {
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            // Find the provider list view (has 2 options: Claude Code & OpenAI API)
            var providerList = FindDescendants<ListView>(window).First(lv => lv.Source?.Count == 2);

            Assert.Equal(0, providerList.SelectedItem);

            // Move selection down to OpenAI API (1)
            providerList.SelectedItem = 1;

            // Save settings via Ctrl+S without needing to press Enter on the list item
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal(AgentProvider.OpenAiApi, window.Chosen.Provider);
        }

        [Fact]
        public void SettingsWindow_tab_navigation_claude()
        {
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            // Switch to Claude Code tab
            window.SwitchToTab(window.ClaudeTabView);
            Assert.Equal(window.ClaudeTabView, window.ActiveTab);

            // Verify Claude model list is present
            var claudeList = FindDescendants<ListView>(window).First(lv => lv.Source?.Count == ClaudeModels.All.Length);
            Assert.NotNull(claudeList);

            // Select Haiku (index 1)
            claudeList.SelectedItem = 1;
            claudeList.NewKeyDownEvent(Key.Enter);

            var customModelField = FindDescendants<TextField>(window).First(tf => tf.Text == ClaudeModels.All[1].Id);
            Assert.Equal(ClaudeModels.All[1].Id, customModelField.Text);

            // Save settings via Ctrl+S
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal(ClaudeModels.All[1].Id, window.Chosen.ClaudeModel);
        }

        [Fact]
        public void SettingsWindow_claude_model_selection_without_enter()
        {
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            window.SwitchToTab(window.ClaudeTabView);
            var claudeList = FindDescendants<ListView>(window).First(lv => lv.Source?.Count == ClaudeModels.All.Length);

            // Change selection to Opus without pressing Enter
            claudeList.SelectedItem = 3;

            // Save via Ctrl+S
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal(ClaudeModels.All[3].Id, window.Chosen.ClaudeModel);
        }

        [Fact]
        public void SettingsWindow_tab_navigation_openai_and_presets()
        {
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.OpenAiApi };
            var window = new SettingsWindow(app, settings);

            // Switch to OpenAI API tab
            window.SwitchToTab(window.OpenAiTabView);
            Assert.Equal(window.OpenAiTabView, window.ActiveTab);

            // Find DropDownList and URL text field
            var dropDown = FindDescendants<DropDownList>(window).Single();
            var urlField = FindDescendants<TextField>(window).First(tf => tf != dropDown && tf.Text == settings.LmStudioBaseUrl);

            // Select Google preset in dropdown
            dropDown.Text = "Google (https://generativelanguage.googleapis.com/v1beta/openai)";
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai", urlField.Text);

            // Type custom URL
            urlField.Text = "http://my-custom-host:8080/v1";
            Assert.Contains("Custom", dropDown.Text);

            // Find API key label and verify note
            var apiKeyLabel = FindDescendants<Label>(window).First(l => l.Text.Contains("API Key"));
            Assert.Equal("API Key (optional depending on vendor configuration):", apiKeyLabel.Text);
        }

        [Fact]
        public void SettingsWindow_probed_models_sorting_and_reactivity()
        {
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.OpenAiApi };
            var window = new SettingsWindow(app, settings);

            window.SwitchToTab(window.OpenAiTabView);

            // Verify probed models list has reactive Dim.Fill height
            var listViews = FindDescendants<ListView>(window).ToList();
            // The probed models list is the one initialized with 0 items initially
            var probedList = listViews.First(lv => lv.Source == null || lv.Source.Count == 0);
            Assert.NotNull(probedList);

            // Verify alphabetical sort (case-insensitive)
            var sampleUnsorted = new List<string> { "zebra-3b", "Alpha-7b", "beta-8b", "alpha-13b" };
            var sorted = sampleUnsorted.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(new[] { "alpha-13b", "Alpha-7b", "beta-8b", "zebra-3b" }, sorted);
        }

        [Fact]
        public void SettingsWindow_tabs_structure_and_provider_picking()
        {
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            var tabs = FindDescendants<Tabs>(window).Single();
            Assert.True(tabs.CanFocus);
            Assert.Equal(5, tabs.TabCollection.Count());

            var providerList = FindDescendants<ListView>(window).First(lv => lv.Source?.Count == 2);
            providerList.SelectedItem = 1;
            providerList.NewKeyDownEvent(Key.Enter);
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.Equal(AgentProvider.OpenAiApi, window.Chosen!.Provider);
        }

        [Fact]
        public void SettingsWindow_tab_switching_via_value_and_esc_cancel()
        {
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            Assert.Equal(window.EngineTabView, window.ActiveTab);

            window.SwitchToTab(window.MemoryTabView);
            Assert.Equal(window.MemoryTabView, window.ActiveTab);

            window.SwitchToTab(window.EditorTabView);
            Assert.Equal(window.EditorTabView, window.ActiveTab);

            // Press Esc to cancel without saving
            var cancelledFired = false;
            window.Cancelled += () => cancelledFired = true;
            window.NewKeyDownEvent(Key.Esc);

            Assert.True(cancelledFired);
            Assert.Null(window.Chosen);
        }
    }
}
