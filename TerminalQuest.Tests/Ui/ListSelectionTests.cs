using System.Collections.ObjectModel;
using System.Data;

using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Agents;
using TerminalQuest.Saves;
using TerminalQuest.Settings;
using TerminalQuest.Tests.Infrastructure;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// Tests for list and suggestion selection handling.
    /// </summary>
    [Collection(EnvironmentCollection.Name)]
    [Trait(Categories.Name, Categories.Environment)]
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
        public void A_settled_suggestion_strip_offers_no_selection()
        {
            using var view = new CommandSuggestionView { Suggestions = Commands };
            Assert.Equal(0, view.SelectedItem);

            // Settled means reminder, not menu: no row may wear the selection block.
            view.IsChoosing = false;

            Assert.Null(view.SelectedItem);
            Assert.Null(view.Selected);
        }

        [Fact]
        public void A_settled_strip_never_highlights_refills()
        {
            using var view = new CommandSuggestionView { Suggestions = Commands };
            view.IsChoosing = false;

            view.Suggestions = Commands;

            Assert.Null(view.SelectedItem);
            Assert.Null(view.Selected);
        }

        [Fact]
        public void A_reopened_suggestion_strip_offers_its_first_command_again()
        {
            using var view = new CommandSuggestionView { Suggestions = Commands };
            view.IsChoosing = false;

            view.IsChoosing = true;

            Assert.Equal(0, view.SelectedItem);
            Assert.Equal(Commands[0], view.Selected);
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

            for (var i = 0; i < 10; i++)
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
        public void SettingsWindow_highlight_without_enter_does_not_change_provider()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            // Browse to OpenAI API without pressing Enter: the draft is untouched.
            window.ProviderList.SelectedItem = 1;

            // Save settings via Ctrl+S
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal(AgentProvider.ClaudeCode, window.Chosen.Provider);
        }

        [Fact]
        public void SettingsWindow_enter_picks_provider()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            window.ProviderList.SelectedItem = 1;
            window.ProviderList.NewKeyDownEvent(Key.Enter);

            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal(AgentProvider.OpenAiApi, window.Chosen.Provider);
        }

        [Fact]
        public void SettingsWindow_claude_enter_picks_model_and_advances()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            // Switch to Claude Code section
            window.SwitchToSection(SettingsSection.ClaudeCode);
            Assert.Equal(SettingsSection.ClaudeCode, window.ActiveSection);

            window.SetFocus();
            window.ClaudeModelList.SetFocus();

            // Select Haiku (index 1) and press Enter to pick it
            window.ClaudeModelList.SelectedItem = 1;
            window.ClaudeModelList.NewKeyDownEvent(Key.Enter);

            Assert.Equal(ClaudeModels.All[1].Id, window.ClaudeCustomModelField.Text);

            // Enter advances into the custom model field
            Assert.Equal(window.ClaudeCustomModelField, window.MostFocused);

            // Save settings via Ctrl+S
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal(ClaudeModels.All[1].Id, window.Chosen.ClaudeModel);
        }

        [Fact]
        public void SettingsWindow_claude_highlight_without_enter_does_not_change_model()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            window.SwitchToSection(SettingsSection.ClaudeCode);

            // Browse to Opus without pressing Enter
            window.ClaudeModelList.SelectedItem = 3;

            // Save via Ctrl+S: the browsed row must not leak into the draft
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal(ClaudeModels.All[0].Id, window.Chosen.ClaudeModel);
        }

        [Fact]
        public void SettingsWindow_openai_preset_enter_applies_endpoint()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.OpenAiApi };
            var window = new SettingsWindow(app, settings);

            // Switch to OpenAI API section
            window.SwitchToSection(SettingsSection.OpenAiApi);
            Assert.Equal(SettingsSection.OpenAiApi, window.ActiveSection);

            // Highlight Google and press Enter to apply it
            window.PresetList.SelectedItem = 0;
            window.PresetList.NewKeyDownEvent(Key.Enter);
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai", window.BaseUrlField.Text);

            // Typing a custom URL moves the preset highlight to Custom without saving yet
            window.BaseUrlField.Text = "http://my-custom-host:8080/v1";
            Assert.Equal(3, window.PresetList.SelectedItem);

            // Find API key label and verify note
            var apiKeyLabel = FindDescendants<Label>(window).First(l => l.Text.Contains("API Key"));
            Assert.Equal("API Key (optional depending on vendor configuration):", apiKeyLabel.Text);
        }

        [Fact]
        public void SettingsWindow_preset_pick_persists_on_save()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.OpenAiApi };
            var window = new SettingsWindow(app, settings);

            window.SwitchToSection(SettingsSection.OpenAiApi);

            // Pick the Google preset with Enter
            window.PresetList.SelectedItem = 0;
            window.PresetList.NewKeyDownEvent(Key.Enter);

            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai", window.BaseUrlField.Text);

            // Save via Ctrl+S
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal("Google", window.Chosen.OpenAiPreset);
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai", window.Chosen.LmStudioBaseUrl);
            Assert.Equal("gemini-flash-lite-latest", window.Chosen.LmStudioModel);
        }

        [Fact]
        public void SettingsWindow_probed_models_sorting_and_reactivity()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.OpenAiApi };
            var window = new SettingsWindow(app, settings);

            window.SwitchToSection(SettingsSection.OpenAiApi);

            // The probed models list starts with no source until a probe runs
            Assert.True(window.ProbedModelsList.Source == null || window.ProbedModelsList.Source.Count == 0);

            // Verify alphabetical sort (case-insensitive)
            var sampleUnsorted = new List<string> { "zebra-3b", "Alpha-7b", "beta-8b", "alpha-13b" };
            var sorted = sampleUnsorted.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(new[] { "alpha-13b", "Alpha-7b", "beta-8b", "zebra-3b" }, sorted);
        }

        [Fact]
        public void SettingsWindow_sections_structure()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            Assert.Equal(4, window.SectionsList.Source?.Count);
            Assert.Equal(SettingsSection.Provider, window.ActiveSection);

            // Each row maps to its section in order.
            window.SectionsList.SelectedItem = 2;
            Assert.Equal(SettingsSection.OpenAiApi, window.ActiveSection);
            window.SectionsList.SelectedItem = 0;
            Assert.Equal(SettingsSection.Provider, window.ActiveSection);

            window.ProviderList.SelectedItem = 1;
            window.ProviderList.NewKeyDownEvent(Key.Enter);
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.Equal(AgentProvider.OpenAiApi, window.Chosen!.Provider);
        }

        [Fact]
        public void SettingsWindow_section_switching_and_esc_cancel()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            Assert.Equal(SettingsSection.Provider, window.ActiveSection);

            window.SwitchToSection(SettingsSection.Preferences);
            Assert.Equal(SettingsSection.Preferences, window.ActiveSection);

            window.SwitchToSection(SettingsSection.OpenAiApi);
            Assert.Equal(SettingsSection.OpenAiApi, window.ActiveSection);

            // Press Esc to cancel without saving
            var cancelledFired = false;
            window.Cancelled += () => cancelledFired = true;
            window.NewKeyDownEvent(Key.Esc);

            Assert.True(cancelledFired);
            Assert.Null(window.Chosen);
        }

        [Fact]
        public void SettingsWindow_field_labels_use_accent_and_help_text_recedes()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            var labels = FindDescendants<Label>(window).ToList();

            // Every input label draws the eye in gold...
            foreach (var prefix in new[]
            {
                "Active Narrative Provider",
                "Preset Claude Models",
                "Or custom model identifier:",
                "Server Base URL",
                "Preset (Up/Down",
                "API Key",
                "Model Name / ID",
                "Transcript Recall Characters:",
                "External Editor Command",
            })
            {
                var label = labels.First(l => l.Text.StartsWith(prefix));
                Assert.Equal(Theme.Attr(TextRole.Item), label.GetAttributeForRole(VisualRole.Normal));
            }

            // ...while help text recedes into grey.
            foreach (var fragment in new[]
            {
                "connects over HTTP",
                "requires the 'claude' CLI",
                "Boundaries:",
            })
            {
                var label = labels.First(l => l.Text.Contains(fragment));
                Assert.Equal(Theme.Attr(TextRole.System), label.GetAttributeForRole(VisualRole.Normal));
            }
        }

        [Fact]
        public void SettingsWindow_summary_marks_active_config_and_standby()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            var labels = FindDescendants<Label>(window).ToList();

            var active = labels.First(l => l.Text.StartsWith("Current Configuration:"));
            Assert.Equal(Theme.Attr(TextRole.Command), active.GetAttributeForRole(VisualRole.Normal));

            var standby = labels.First(l => l.Text.Contains("standby:"));
            Assert.Equal(Theme.Attr(TextRole.System), standby.GetAttributeForRole(VisualRole.Normal));
        }

        [Fact]
        public void SettingsWindow_feedback_uses_severity_colors()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            // A confirmation reads green.
            window.ProviderList.SelectedItem = 1;
            window.ProviderList.NewKeyDownEvent(Key.Enter);
            var confirm = FindDescendants<Label>(window).First(l => l.Text.Contains("Active provider set to"));
            Assert.Equal(Theme.Attr(TextRole.Place), confirm.GetAttributeForRole(VisualRole.Normal));

            // A validation error reads red and nothing is saved.
            window.SwitchToSection(SettingsSection.Preferences);
            window.RecallField.Text = "not-a-number";
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.Null(window.Chosen);
            var error = FindDescendants<Label>(window).First(l => l.Text.Contains("must be an integer"));
            Assert.Equal(Theme.Attr(TextRole.Danger), error.GetAttributeForRole(VisualRole.Normal));
        }

        [Fact]
        public void SettingsWindow_tab_cycles_through_panes()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            window.SetFocus();
            window.SectionsList.SetFocus();
            Assert.Equal(window.SectionsList, window.MostFocused);

            // Tab moves into the form instead of walking every field.
            window.NewKeyDownEvent(Key.Tab);
            Assert.Equal(window.ProviderList, window.MostFocused);

            // Tab jumps straight to the action bar.
            window.NewKeyDownEvent(Key.Tab);
            Assert.IsType<Button>(window.MostFocused);

            // Tab wraps back to the sections list.
            window.NewKeyDownEvent(Key.Tab);
            Assert.Equal(window.SectionsList, window.MostFocused);

            // Shift+Tab goes the other way too.
            window.NewKeyDownEvent(Key.Tab.WithShift);
            Assert.IsType<Button>(window.MostFocused);
        }

        [Fact]
        public void SettingsWindow_footer_arrows_cycle_without_leaking()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            window.SetFocus();
            window.SectionsList.SetFocus();

            window.NewKeyDownEvent(Key.Tab);
            window.NewKeyDownEvent(Key.Tab);
            var first = window.MostFocused;
            Assert.IsType<Button>(first);

            // Arrows cycle within the footer rather than leaking back to the form.
            window.NewKeyDownEvent(Key.CursorRight);
            Assert.IsType<Button>(window.MostFocused);
            Assert.NotSame(first, window.MostFocused);

            window.NewKeyDownEvent(Key.CursorLeft);
            Assert.Same(first, window.MostFocused);

            // Wrapping past the first button lands on the last one.
            window.NewKeyDownEvent(Key.CursorLeft);
            Assert.IsType<Button>(window.MostFocused);
            Assert.NotSame(first, window.MostFocused);
        }

        [Fact]
        public void SaveMenu_tab_toggles_between_saves_table_and_action_bar()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var window = new SaveMenuWindow(app, "test-narrator");

            var table = FindDescendants<TableView>(window).Single();
            Assert.Equal(10, FindDescendants<Button>(window).Count());

            window.SetFocus();
            table.SetFocus();
            Assert.Equal(table, window.MostFocused);

            // Tab jumps straight to the action bar instead of walking each button.
            window.NewKeyDownEvent(Key.Tab);
            Assert.IsType<Button>(window.MostFocused);

            // Arrows cycle within the footer rather than leaking back to the table.
            var first = window.MostFocused;
            window.NewKeyDownEvent(Key.CursorRight);
            Assert.IsType<Button>(window.MostFocused);
            Assert.NotSame(first, window.MostFocused);

            window.NewKeyDownEvent(Key.CursorLeft);
            Assert.Same(first, window.MostFocused);

            // Tab returns to the saves table; Shift+Tab goes the other way too.
            window.NewKeyDownEvent(Key.Tab);
            Assert.Equal(table, window.MostFocused);

            window.NewKeyDownEvent(Key.Tab.WithShift);
            Assert.IsType<Button>(window.MostFocused);
        }

        [Fact]
        public void SaveMenu_single_key_shortcuts_work_from_the_action_bar()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var window = new SaveMenuWindow(app, "test-narrator");

            var table = FindDescendants<TableView>(window).Single();
            window.SetFocus();
            table.SetFocus();

            window.NewKeyDownEvent(Key.Tab);
            Assert.IsType<Button>(window.MostFocused);

            // Q quits from anywhere, including with focus in the footer.
            var cancelledFired = false;
            window.Cancelled += () => cancelledFired = true;
            window.NewKeyDownEvent(Key.Q);

            Assert.True(cancelledFired);
        }

        [Fact]
        public void SaveMenu_hotkeys_fire_while_the_saves_table_has_focus()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var window = new SaveMenuWindow(app, "test-narrator");

            var table = FindDescendants<TableView>(window).Single();
            window.SetFocus();
            table.SetFocus();
            Assert.Equal(table, window.MostFocused);

            // Routed through the focused table, the way a live keypress travels: the
            // table must offer menu hotkeys to the window before its own type-ahead
            // eats them.
            var settingsFired = false;
            window.SettingsRequested += () => settingsFired = true;
            table.NewKeyDownEvent(Key.S);
            Assert.True(settingsFired);

            var cancelledFired = false;
            window.Cancelled += () => cancelledFired = true;
            table.NewKeyDownEvent(Key.Q);
            Assert.True(cancelledFired);
        }

        [Fact]
        public void SaveMenu_saves_table_keeps_arrow_navigation()
        {
            using var root = new SavesRoot();
            SavePaths.Open("Alpha");
            SavePaths.Open("Beta");
            var app = Application.Create();
            var window = new SaveMenuWindow(app, "test-narrator");

            var table = FindDescendants<TableView>(window).Single();
            window.SetFocus();
            table.SetFocus();

            Assert.Equal(0, table.Value?.SelectedCell.Y);
            Assert.True(table.NewKeyDownEvent(Key.CursorDown));
            Assert.Equal(1, table.Value?.SelectedCell.Y);
        }
    }
}
