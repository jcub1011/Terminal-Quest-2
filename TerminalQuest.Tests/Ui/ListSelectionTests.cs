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

            // Browse to Google without pressing Enter: the draft is untouched.
            window.ProviderCombo.Value = window.ProviderNames[1];

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

            window.ProviderCombo.Value = window.ProviderNames[1];
            window.ProviderCombo.NewKeyDownEvent(Key.Enter);

            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal(AgentProvider.Google, window.Chosen.Provider);
        }

        [Fact]
        public void SettingsWindow_provider_list_offers_every_provider()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            Assert.Equal(5, window.ProviderCombo.Source!.Count);

            // Each row picks its provider into the draft.
            var expected = new[]
            {
                AgentProvider.ClaudeCode,
                AgentProvider.Google,
                AgentProvider.OpenAI,
                AgentProvider.Anthropic,
                AgentProvider.Custom,
            };
            for (var i = 0; i < expected.Length; i++)
            {
                window.ProviderCombo.Value = window.ProviderNames[i];
                window.ProviderCombo.NewKeyDownEvent(Key.Enter);
                window.NewKeyDownEvent(Key.S.WithCtrl);
                Assert.Equal(expected[i], window.Chosen!.Provider);
            }
        }

        [Fact]
        public void SettingsWindow_claude_enter_picks_model_and_advances()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            // Switch to Provider Settings with Claude Code picked
            window.SwitchToSection(SettingsSection.Provider);
            Assert.Equal(SettingsSection.Provider, window.ActiveSection);

            window.SetFocus();
            window.ClaudeModelCombo.SetFocus();

            // Select Haiku (index 1) and press Enter to pick it: the box shows the row
            // label while the draft takes the id.
            window.ClaudeModelCombo.Value = window.ClaudeLabels[1];
            window.ClaudeModelCombo.NewKeyDownEvent(Key.Enter);

            Assert.Equal(window.ClaudeLabels[1], window.ClaudeModelCombo.Text);

            // Enter advances to the next field, wrapping to the provider picker
            Assert.Equal(window.ProviderCombo, window.MostFocused);

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

            window.SwitchToSection(SettingsSection.Provider);

            // Browse to Opus without pressing Enter
            window.ClaudeModelCombo.Value = window.ClaudeLabels[3];

            // Save via Ctrl+S: the browsed row must not leak into the draft
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal(ClaudeModels.All[0].Id, window.Chosen.ClaudeModel);
        }

        [Fact]
        public void SettingsWindow_claude_typing_custom_id_saves_without_enter()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            window.SwitchToSection(SettingsSection.Provider);

            // Typing is explicit: a hand-typed id reaches the draft without Enter.
            window.ClaudeModelCombo.Text = "my-custom-model";

            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal("my-custom-model", window.Chosen.ClaudeModel);
        }

        [Fact]
        public void SettingsWindow_provider_browse_leaves_panels_and_summary_alone()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            window.SwitchToSection(SettingsSection.Provider);

            // Browsing the collapsed dropdown changes the display only: the Claude panel
            // stays, and the summary still names the picked provider.
            window.ProviderCombo.Value = window.ProviderNames[1];

            Assert.True(window.IsClaudeSettingsVisible);
            Assert.False(window.IsEndpointSettingsVisible);
            var labels = FindDescendants<Label>(window).ToList();
            Assert.Contains(labels, l => $"{l.Text}".StartsWith("Current Configuration: Claude Code"));
        }

        [Fact]
        public void SettingsWindow_enter_on_clean_dropdown_opens_instead_of_picking()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            window.SwitchToSection(SettingsSection.Provider);
            window.SetFocus();
            window.ProviderCombo.SetFocus();

            // Clean (highlight == picked): Enter begins selecting rather than picking, so
            // no confirmation appears and focus stays in the box.
            window.ProviderCombo.NewKeyDownEvent(Key.Enter);

            var labels = FindDescendants<Label>(window).ToList();
            Assert.DoesNotContain(labels, l => $"{l.Text}".Contains("Active provider set to"));
            Assert.Equal(window.ProviderCombo, window.MostFocused);

            // ...while a browsed row still picks on Enter.
            window.ProviderCombo.Value = window.ProviderNames[1];
            window.ProviderCombo.NewKeyDownEvent(Key.Enter);
            Assert.Contains(labels, l => $"{l.Text}".Contains("Active provider set to: Google"));
        }

        [Fact]
        public void SettingsWindow_enter_on_clean_model_boxes_opens_instead_of_advancing()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode, ClaudeModel = ClaudeModels.All[0].Id };
            var window = new SettingsWindow(app, settings);

            window.SwitchToSection(SettingsSection.Provider);
            window.SetFocus();
            window.ClaudeModelCombo.SetFocus();

            // Clean: Enter opens rather than advancing to the next field.
            window.ClaudeModelCombo.NewKeyDownEvent(Key.Enter);
            Assert.Equal(window.ClaudeModelCombo, window.MostFocused);

            // Dirty (typed): Enter still accepts and advances.
            window.ClaudeModelCombo.Text = "my-custom-model";
            window.ClaudeModelCombo.NewKeyDownEvent(Key.Enter);
            Assert.Equal(window.ProviderCombo, window.MostFocused);
        }

        [Fact]
        public void SettingsWindow_enter_on_endpoint_model_confirms_and_advances_when_dirty()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.Google };
            var window = new SettingsWindow(app, settings);

            window.SwitchToSection(SettingsSection.Provider);
            window.SetFocus();
            window.ModelField.SetFocus();

            // Clean (matches the loaded slot): Enter opens rather than confirming.
            window.ModelField.NewKeyDownEvent(Key.Enter);
            Assert.Equal(window.ModelField, window.MostFocused);
            var labels = FindDescendants<Label>(window).ToList();
            Assert.DoesNotContain(labels, l => $"{l.Text}".StartsWith("Picked:"));

            // Dirty (typed): Enter confirms and advances.
            window.ModelField.Text = "gemini-2-0-test";
            window.ModelField.NewKeyDownEvent(Key.Enter);
            Assert.Contains(labels, l => $"{l.Text}".StartsWith("Picked: gemini-2-0-test"));
            Assert.Equal(window.ProviderCombo, window.MostFocused);
        }

        [Fact]
        public void SettingsWindow_provider_settings_rebinds_per_provider()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.Google };
            var window = new SettingsWindow(app, settings);

            // Switch to Provider Settings: Google's built-in endpoint shows, fields rebound.
            window.SwitchToSection(SettingsSection.Provider);
            Assert.Equal(SettingsSection.Provider, window.ActiveSection);

            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai", window.BaseUrlField.Text);
            Assert.Contains("https://generativelanguage.googleapis.com/v1beta/openai", $"{window.BuiltinUrlLabel.Text}");

            // Typing a custom URL updates the match hint without saving yet.
            window.BaseUrlField.Text = "http://my-custom-host:8080/v1";
            var matchHint = FindDescendants<Label>(window).First(l => $"{l.Text}".Contains("endpoint address"));
            Assert.Contains("Custom", $"{matchHint.Text}");

            // The API key field is present and masked.
            var apiKeyLabel = FindDescendants<Label>(window).First(l => $"{l.Text}".StartsWith("API Key"));
            Assert.Contains("TQ2_GOOGLE_API_KEY", $"{apiKeyLabel.Text}");
            Assert.True(window.ApiKeyField.Secret);
        }

        [Fact]
        public void SettingsWindow_each_provider_keeps_its_own_values()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.Google };
            var window = new SettingsWindow(app, settings);

            window.SwitchToSection(SettingsSection.Provider);

            // Type a Google model, then switch to Custom: the fields rebind to Custom's slot.
            window.ModelField.Text = "gemini-2-0-test";
            window.SwitchToSection(SettingsSection.Provider);
            window.ProviderCombo.Value = window.ProviderNames[4];
            window.ProviderCombo.NewKeyDownEvent(Key.Enter);
            window.SwitchToSection(SettingsSection.Provider);

            Assert.Equal("http://localhost:1234/v1", window.BaseUrlField.Text);

            // A Custom URL persists on save without touching Google's slot.
            window.BaseUrlField.Text = "http://my-custom-host:8080/v1";
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.NotNull(window.Chosen);
            Assert.Equal(AgentProvider.Custom, window.Chosen.Provider);
            Assert.Equal("http://my-custom-host:8080/v1", window.Chosen.Custom.BaseUrl);
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai", window.Chosen.Google.BaseUrl);
            Assert.Equal("gemini-2-0-test", window.Chosen.Google.Model);
        }

        [Fact]
        public void SettingsWindow_claude_box_shows_for_claude_and_endpoint_for_the_rest()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            window.SwitchToSection(SettingsSection.Provider);

            // Both panels live in the provider section at once: the picker stays put while
            // the settings beneath it follow the picked provider.
            Assert.Equal(SettingsSection.Provider, window.ActiveSection);
            Assert.True(window.ProviderCombo.Visible);

            // Claude Code picked: the Claude model controls show, the endpoint ones hide.
            Assert.True(window.IsClaudeSettingsVisible);
            Assert.False(window.IsEndpointSettingsVisible);

            // Pick Anthropic (API): the endpoint fields return, the Claude ones hide.
            window.SwitchToSection(SettingsSection.Provider);
            window.ProviderCombo.Value = window.ProviderNames[3];
            window.ProviderCombo.NewKeyDownEvent(Key.Enter);
            window.SwitchToSection(SettingsSection.Provider);

            Assert.False(window.IsClaudeSettingsVisible);
            Assert.True(window.IsEndpointSettingsVisible);
            Assert.Equal("https://api.anthropic.com/v1", window.BaseUrlField.Text);
        }

        [Fact]
        public void SettingsWindow_probed_models_sorting_and_reactivity()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.Google };
            var window = new SettingsWindow(app, settings);

            window.SwitchToSection(SettingsSection.Provider);

            // The probed models start empty until a probe runs
            Assert.Empty(window.ProbedModels);
            Assert.Equal(0, window.ModelField.Source!.Count);

            // Verify alphabetical sort (case-insensitive)
            var sampleUnsorted = new List<string> { "zebra-3b", "Alpha-7b", "beta-8b", "alpha-13b" };
            var sorted = sampleUnsorted.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(new[] { "alpha-13b", "Alpha-7b", "beta-8b", "zebra-3b" }, sorted);
        }

        [Fact]
        public void SettingsWindow_provider_page_is_one_continuous_scroll()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            // One scrolling page, no sub-panels: picker and settings share the scroll view.
            Assert.Empty(FindDescendants<FrameView>(window.ProviderScroll));
            Assert.Same(window.ProviderScroll, window.ProviderCombo.SuperView);
            Assert.Same(window.ProviderScroll, window.ClaudeModelCombo.SuperView!.SuperView);

            // Picker above, header between, settings below: one compact page of collapsed
            // dropdowns rather than stacked lists. (Order in SubViews, not frames: frames
            // only resolve once laid out.)
            var kids = window.ProviderScroll.SubViews.ToList();
            var header = FindDescendants<Label>(window.ProviderScroll).First(l => $"{l.Text}" == "Provider Settings");
            Assert.True(kids.IndexOf(window.ProviderCombo) < kids.IndexOf(header));
            Assert.True(kids.IndexOf(header) < kids.IndexOf(window.ClaudeModelCombo.SuperView!));

            // A headed gap separates the picker from the settings below.
            Assert.Contains(
                FindDescendants<Label>(window.ProviderScroll),
                l => $"{l.Text}" == "Provider Settings");

            // The scroll view knows its content height: Claude box at Y=4, five rows tall.
            Assert.Equal(4 + 5 + 1, window.ProviderScroll.GetContentHeight());
        }

        [Fact]
        public void SettingsWindow_provider_page_scrolls_when_content_overflows()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.Google };
            var window = new SettingsWindow(app, settings);

            // A vertical scrollbar that only appears when the content overflows, and a
            // content height that actually covers the settings below the picker:
            // endpoint box at Y=4, fourteen rows tall.
            Assert.True(window.ProviderScroll.ViewportSettings.HasFlag(ViewportSettingsFlags.HasVerticalScrollBar));
            Assert.Equal(4 + 14 + 1, window.ProviderScroll.GetContentHeight());

            // A control buried deep in the settings still takes focus through the nesting.
            window.SwitchToSection(SettingsSection.Provider);
            window.SetFocus();
            window.ModelField.SetFocus();

            Assert.Same(window.ModelField, window.MostFocused);
        }

        [Fact]
        public void SettingsWindow_sections_structure()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            Assert.Equal(2, window.SectionsList.Source?.Count);
            Assert.Equal(SettingsSection.Provider, window.ActiveSection);

            // Each row maps to its section in order. Provider Settings rides along inside
            // the provider section rather than taking a row of its own.
            window.SectionsList.SelectedItem = 1;
            Assert.Equal(SettingsSection.Preferences, window.ActiveSection);
            window.SectionsList.SelectedItem = 0;
            Assert.Equal(SettingsSection.Provider, window.ActiveSection);

            window.ProviderCombo.Value = window.ProviderNames[1];
            window.ProviderCombo.NewKeyDownEvent(Key.Enter);
            window.NewKeyDownEvent(Key.S.WithCtrl);
            Assert.Equal(AgentProvider.Google, window.Chosen!.Provider);
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

            window.SwitchToSection(SettingsSection.Provider);
            Assert.Equal(SettingsSection.Provider, window.ActiveSection);

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
                "Narrative Provider",
                "Preset Claude Models",
                "Server Base URL",
                "API Key",
                "Model Name / ID",
                "Transcript Recall Characters:",
                "External Editor Command",
            })
            {
                var label = labels.First(l => $"{l.Text}".StartsWith(prefix));
                Assert.Equal(Theme.Attr(TextRole.Item), label.GetAttributeForRole(VisualRole.Normal));
            }

            // ...while help text recedes into grey.
            foreach (var fragment in new[]
            {
                "support only OpenAI-compatible",
                "requires the 'claude' CLI",
                "Boundaries:",
            })
            {
                var label = labels.First(l => $"{l.Text}".Contains(fragment));
                Assert.Equal(Theme.Attr(TextRole.System), label.GetAttributeForRole(VisualRole.Normal));
            }

            // The built-in endpoint readout is information, not an input: plain text.
            var builtin = labels.First(l => $"{l.Text}".StartsWith("Built-in endpoint:"));
            Assert.Equal(Theme.Attr(TextRole.Normal), builtin.GetAttributeForRole(VisualRole.Normal));
        }

        [Fact]
        public void SettingsWindow_summary_marks_active_config_and_standby()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            var labels = FindDescendants<Label>(window).ToList();

            var active = labels.First(l => $"{l.Text}".StartsWith("Current Configuration:"));
            Assert.Equal(Theme.Attr(TextRole.Command), active.GetAttributeForRole(VisualRole.Normal));

            var detail = labels.First(l => $"{l.Text}".StartsWith("Runs the 'claude' CLI"));
            Assert.Equal(Theme.Attr(TextRole.System), detail.GetAttributeForRole(VisualRole.Normal));
        }

        [Fact]
        public void SettingsWindow_feedback_uses_severity_colors()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var settings = new AppSettings { Provider = AgentProvider.ClaudeCode };
            var window = new SettingsWindow(app, settings);

            // A confirmation reads green.
            window.ProviderCombo.Value = window.ProviderNames[1];
            window.ProviderCombo.NewKeyDownEvent(Key.Enter);
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
            Assert.Equal(window.ProviderCombo, window.MostFocused);

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

        [Fact]
        public void SaveMenu_header_help_and_separators_use_distinct_roles()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var window = new SaveMenuWindow(app, "test-narrator");

            var labels = FindDescendants<Label>(window).ToList();

            // Title level reads bright; help text recedes.
            var header = labels.Single(l => $"{l.Text}".StartsWith("Narrator:", StringComparison.Ordinal));
            Assert.Equal(Theme.Attr(TextRole.Command), header.GetAttributeForRole(VisualRole.Normal));

            var hints = labels.Single(l => $"{l.Text}".Contains("Tab:", StringComparison.Ordinal));
            Assert.Equal(Theme.Attr(TextRole.Hint), hints.GetAttributeForRole(VisualRole.Normal));

            // Group dividers stay dim rather than competing with the buttons.
            var separators = labels.Where(l => $"{l.Text}" == "│").ToList();
            Assert.Equal(2, separators.Count);
            foreach (var separator in separators)
            {
                Assert.Equal(Theme.Attr(TextRole.Hint), separator.GetAttributeForRole(VisualRole.Normal));
            }
        }

        [Fact]
        public void SaveMenu_destructive_buttons_wear_a_danger_hotkey()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var window = new SaveMenuWindow(app, "test-narrator");

            var buttons = FindDescendants<Button>(window).ToList();
            Assert.Equal(10, buttons.Count);

            Button Named(string text) => buttons.Single(b => $"{b.Text}" == text);

            // Delete/Reset stand apart in red; safe actions keep the standard blue hotkey.
            Assert.Equal(Theme.Attr(TextRole.Danger), Named("Delete (Del)").GetAttributeForRole(VisualRole.HotNormal));
            Assert.Equal(Theme.Attr(TextRole.Danger), Named("Reset (Ctrl+R)").GetAttributeForRole(VisualRole.HotNormal));
            Assert.Equal(Theme.Attr(TextRole.Button), Named("Load (Enter)").GetAttributeForRole(VisualRole.HotNormal));
            Assert.Equal(Theme.Attr(TextRole.Button), Named("New (N)").GetAttributeForRole(VisualRole.HotNormal));
        }

        [Fact]
        public void SaveMenu_focused_pane_border_and_hints_follow_focus()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var window = new SaveMenuWindow(app, "test-narrator");

            var table = FindDescendants<TableView>(window).Single();
            window.SetFocus();
            table.SetFocus();

            FrameView Frame(string title) =>
                FindDescendants<FrameView>(window).Single(f => $"{f.Title}" == title);
            var hints = FindDescendants<Label>(window).Single(l => $"{l.Text}".Contains("Tab:", StringComparison.Ordinal));

            // Saves focused: blue border there, dimmed actions, saves hints.
            Assert.Equal("* Saves", $"{Frame("* Saves").Title}");
            Assert.Equal(Theme.Attr(TextRole.Button), Frame("* Saves").GetAttributeForRole(VisualRole.Normal));
            Assert.Equal(Theme.Attr(TextRole.Hint), Frame("Actions").GetAttributeForRole(VisualRole.Normal));
            Assert.Contains("Up/Down: saves", $"{hints.Text}", StringComparison.Ordinal);

            // Tab into the action bar: the border and hints move with focus.
            window.NewKeyDownEvent(Key.Tab);

            Assert.Equal("* Actions", $"{Frame("* Actions").Title}");
            Assert.Equal(Theme.Attr(TextRole.Button), Frame("* Actions").GetAttributeForRole(VisualRole.Normal));
            Assert.Equal(Theme.Attr(TextRole.Hint), Frame("Saves").GetAttributeForRole(VisualRole.Normal));
            Assert.Contains("Left/Right: actions", $"{hints.Text}", StringComparison.Ordinal);
        }

        [Fact]
        public void SaveMenu_empty_details_recede_as_guidance()
        {
            using var root = new SavesRoot();
            var app = Application.Create();
            var window = new SaveMenuWindow(app, "test-narrator");

            var detailsFrame = FindDescendants<FrameView>(window).Single(f => $"{f.Title}" == "Save Details");
            var details = detailsFrame.SubViews.OfType<Label>().Single();

            Assert.Equal(Theme.Attr(TextRole.Hint), details.GetAttributeForRole(VisualRole.Normal));
        }
    }
}
