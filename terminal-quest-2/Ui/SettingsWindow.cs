using System.Collections.ObjectModel;

using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Agents;
using TerminalQuest.Agents.LmStudio;
using TerminalQuest.Saves;
using TerminalQuest.Settings;

using Attribute = Terminal.Gui.Drawing.Attribute;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Which settings group the right-hand form is showing.
    /// </summary>
    internal enum SettingsSection
    {
        Provider,
        ClaudeCode,
        OpenAiApi,
        Preferences,
    }

    /// <summary>
    /// The settings screen: who narrates, model options, and application preferences.
    /// Two panes like the save menu: a sections list on the left, the active section's
    /// form on the right, and a grouped action bar at the bottom. Tab switches panes,
    /// arrows move within the focused pane, Enter picks or advances. Highlighting a row
    /// never changes anything; only Enter picks it into the pending draft.
    /// </summary>
    internal sealed class SettingsWindow : Window
    {
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

        private static readonly Attribute PickedAndSelectedAttr = new(new Color("#1b5e20"), Color.White);
        private static readonly Attribute PickedAttr = new(new Color("#8fb26a"), Color.None);
        private static readonly Attribute SelectedAttr = new(Color.Black, Color.White);
        private static readonly Attribute NormalAttr = new(new Color("#d7d2c4"), Color.None);

        private static readonly string[] SectionNames = ["Provider", "Claude Code", "OpenAI API", "Preferences"];

        private readonly IApplication _app;
        private readonly AppSettings _original;
        private readonly AppSettings _draft;

        /// <summary>
        /// A list that offers keys to the window before its own handling, so pane
        /// switching (Tab) and field movement (Left/Right) never get trapped inside.
        /// Up/Down and Enter stay native: rows and picking.
        /// </summary>
        private sealed class NavListView : ListView
        {
            public Func<Key, bool>? BeforeKey { get; set; }

            protected override bool OnKeyDown(Key key) =>
                (BeforeKey?.Invoke(key) ?? false) || base.OnKeyDown(key);
        }

        /// <summary>
        /// A text field that offers keys to the window before its own handling, so pane
        /// switching (Tab) and field movement (Up/Down) never get trapped inside.
        /// Left/Right and typing stay native; Enter advances via <see cref="Accepting"/>.
        /// </summary>
        private sealed class NavTextField : TextField
        {
            public Func<Key, bool>? BeforeKey { get; set; }

            protected override bool OnKeyDown(Key key) =>
                (BeforeKey?.Invoke(key) ?? false) || base.OnKeyDown(key);
        }

        // Panes
        private readonly Label _summaryActiveLabel;
        private readonly Label _summaryStandbyLabel;
        private readonly FrameView _navFrame;
        private readonly NavListView _sectionsList;
        private readonly FrameView _formFrame;
        private readonly Label _messageLabel;
        private readonly FrameView _actionsFrame;
        private readonly Label _hintLabel;

        // Section forms (only one visible at a time)
        private readonly View _providerSection;
        private readonly View _claudeSection;
        private readonly View _openAiSection;
        private readonly View _prefsSection;
        private SettingsSection _activeSection = SettingsSection.Provider;
        private View? _lastFormFocus;

        // Provider section controls
        private NavListView _providerList = null!;

        // Claude section controls
        private NavListView _claudeModelList = null!;
        private NavTextField _claudeCustomModel = null!;

        // OpenAI API section controls
        private NavTextField _lmStudioBaseUrl = null!;
        private NavListView _presetList = null!;
        private Label _presetDetails = null!;
        private NavTextField _lmStudioApiKey = null!;
        private NavTextField _lmStudioModel = null!;
        private NavListView _lmStudioModelsList = null!;
        private Button _probeButton = null!;
        private Label _probeStatus = null!;
        private readonly List<string> _probedModels = [];
        private CancellationTokenSource? _probe;
        private bool _isApplyingPreset;

        // Preferences section controls
        private NavTextField _recallChars = null!;
        private NavTextField _editorCommand = null!;
        private Button _testEditorButton = null!;
        private Button _openConfigFolderButton = null!;
        private Label _editorStatus = null!;

        // Bottom action buttons
        private readonly Button _saveButton;
        private readonly Button _cancelButton;
        private readonly Button _defaultsButton;
        private readonly List<Button> _actionButtons;
        private int _lastActionIndex;

        private readonly string _settingsPath;

        public SettingsWindow(IApplication app, AppSettings settings, string? settingsPath = null)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(settings);

            _settingsPath = settingsPath ?? SettingsStore.Path;

            _app = app;
            _original = settings;

            _draft = new AppSettings();
            _draft.CopyFrom(settings);

            Editor = new ExternalEditor(app, () => _draft.EditorCommand);

            Title = "Settings";
            BorderStyle = LineStyle.Rounded;
            SetScheme(Theme.CreateScheme());

            // Header: always-visible summary of the pending configuration. The active
            // provider stands out bright; the standby one recedes into help grey.
            _summaryActiveLabel = new Label
            {
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                Height = 1,
                CanFocus = false,
            };
            _summaryActiveLabel.SetScheme(Theme.LabelScheme(TextRole.Command));
            _summaryStandbyLabel = new Label
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = 1,
                CanFocus = false,
            };
            _summaryStandbyLabel.SetScheme(Theme.LabelScheme(TextRole.System));

            // Left pane: section navigation.
            _navFrame = new FrameView
            {
                Title = "Sections",
                X = 1,
                Y = 2,
                Width = 16,
                Height = Dim.Fill() - 7,
                BorderStyle = LineStyle.Rounded,
            };
            _navFrame.SetScheme(Theme.CreateScheme());

            _sectionsList = new NavListView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            _sectionsList.SetScheme(Theme.CreateScheme());
            _sectionsList.SetSource(new ObservableCollection<string>(SectionNames));
            _sectionsList.SelectedItem = 0;
            _sectionsList.BeforeKey = NavListKey;
            _sectionsList.ValueChanged += (_, _) => ShowSection((SettingsSection)(_sectionsList.SelectedItem ?? 0));
            _sectionsList.Accepting += (_, _) => FocusForm();
            _navFrame.Add(_sectionsList);

            // Right pane: the active section's form.
            _formFrame = new FrameView
            {
                Title = SectionNames[0],
                X = Pos.Right(_navFrame) + 1,
                Y = 2,
                Width = Dim.Fill() - 1,
                Height = Dim.Fill() - 7,
                BorderStyle = LineStyle.Rounded,
            };
            _formFrame.SetScheme(Theme.CreateScheme());

            _providerSection = BuildProviderSection();
            _claudeSection = BuildClaudeSection();
            _openAiSection = BuildOpenAiSection();
            _prefsSection = BuildPrefsSection();
            _formFrame.Add(_providerSection, _claudeSection, _openAiSection, _prefsSection);

            // Feedback line: errors and confirmations only, so they never wipe the hints.
            _messageLabel = new Label
            {
                X = 1,
                Y = Pos.Bottom(_formFrame),
                Width = Dim.Fill() - 2,
                Height = 1,
                CanFocus = false,
                Text = string.Empty,
            };
            _messageLabel.SetScheme(Theme.CreateScheme());

            // Bottom pane: grouped actions.
            _actionsFrame = new FrameView
            {
                Title = "Actions",
                X = 1,
                Y = Pos.Bottom(_messageLabel),
                Width = Dim.Fill() - 2,
                Height = 3,
                BorderStyle = LineStyle.Rounded,
            };
            _actionsFrame.SetScheme(Theme.CreateScheme());

            _saveButton = new Button { Text = "Save (Ctrl+S)", X = 0, Y = 0 };
            _cancelButton = new Button { Text = "Cancel (Esc)", X = Pos.Right(_saveButton) + 2, Y = 0 };
            _defaultsButton = new Button { Text = "Restore Defaults", X = Pos.Right(_cancelButton) + 2, Y = 0 };
            _saveButton.SetScheme(Theme.CreateScheme());
            _cancelButton.SetScheme(Theme.CreateScheme());
            _defaultsButton.SetScheme(Theme.CreateScheme());
            _saveButton.Accepting += (_, _) => SaveAndClose();
            _cancelButton.Accepting += (_, _) => CancelAndClose();
            _defaultsButton.Accepting += (_, _) => RestoreDefaults();
            _actionButtons = [_saveButton, _cancelButton, _defaultsButton];
            _actionsFrame.Add(_saveButton, _cancelButton, _defaultsButton);

            // Static hints: never overwritten by feedback, which has its own line above.
            _hintLabel = new Label
            {
                X = 1,
                Y = Pos.Bottom(_actionsFrame),
                Width = Dim.Fill() - 2,
                Height = 1,
                CanFocus = false,
                Text = "Up/Down: section | Enter: edit section | Tab: next pane | Ctrl+S: save | Esc: cancel",
            };
            _hintLabel.SetScheme(Theme.CreateScheme());

            Add(_summaryActiveLabel, _summaryStandbyLabel, _navFrame, _formFrame, _messageLabel, _actionsFrame, _hintLabel);

            // The pane titles follow the actual focus, including mouse clicks that
            // bypass the Tab cycle, and the form remembers where it was.
            _sectionsList.HasFocusChanged += (_, _) => UpdatePaneChrome();
            foreach (var control in AllFormControls())
            {
                control.HasFocusChanged += (_, _) => UpdatePaneChrome();
            }
            foreach (var button in _actionButtons)
            {
                button.HasFocusChanged += (_, _) => UpdatePaneChrome();
            }

            ShowSection(SettingsSection.Provider);
            UpdateSummary();

            Initialized += (_, _) =>
            {
                _sectionsList.SetFocus();
                UpdatePaneChrome();
            };
        }

        public AppSettings? Chosen { get; private set; }

        public ExternalEditor? Editor { get; init; }

        public ListView SectionsList => _sectionsList;

        public ListView ProviderList => _providerList;

        public ListView ClaudeModelList => _claudeModelList;

        public ListView PresetList => _presetList;

        public ListView ProbedModelsList => _lmStudioModelsList;

        public TextField ClaudeCustomModelField => _claudeCustomModel;

        public TextField BaseUrlField => _lmStudioBaseUrl;

        public TextField RecallField => _recallChars;

        public SettingsSection ActiveSection => _activeSection;

        public event Action? Done;

        public event Action? Cancelled;

        public void SwitchToSection(SettingsSection section)
        {
            _sectionsList.SelectedItem = (int)section;
            ShowSection(section);
        }

        private View BuildProviderSection()
        {
            var section = new View
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = true,
                Visible = true,
            };
            section.SetScheme(Theme.CreateScheme());

            var providerLabel = new Label
            {
                Text = "Active Narrative Provider (Up/Down: highlight, Enter: pick):",
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            providerLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            _providerList = new NavListView
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = 2,
            };
            _providerList.SetScheme(Theme.CreateScheme());
            _providerList.SetSource(new ObservableCollection<string>(["Claude Code (Anthropic CLI)", "OpenAI API (Google, OpenAI, Anthropic, LM Studio, etc.)"]));
            _providerList.SelectedItem = _draft.Provider == AgentProvider.ClaudeCode ? 0 : 1;
            _providerList.BeforeKey = FormListKey;

            // Highlighting only moves the cursor; Enter picks into the draft.
            _providerList.RowRender += (_, e) =>
            {
                var isPicked = (e.Row == 0 && _draft.Provider == AgentProvider.ClaudeCode)
                            || (e.Row == 1 && _draft.Provider == AgentProvider.OpenAiApi);
                var isSelected = e.Row == _providerList.SelectedItem;

                e.RowAttribute = (isSelected, isPicked) switch
                {
                    (true, true) => PickedAndSelectedAttr,
                    (true, false) => SelectedAttr,
                    (false, true) => PickedAttr,
                    _ => NormalAttr,
                };
            };
            _providerList.ValueChanged += (_, _) => _providerList.SetNeedsDraw();
            _providerList.Accepting += (_, _) =>
            {
                var selected = _providerList.SelectedItem ?? 0;
                _draft.Provider = selected == 0 ? AgentProvider.ClaudeCode : AgentProvider.OpenAiApi;
                _providerList.SetNeedsDraw();
                UpdateSummary();
                Say(_messageLabel, $"Active provider set to: {(_draft.Provider == AgentProvider.ClaudeCode ? "Claude Code" : "OpenAI API")}", TextRole.Place);
            };

            var providerDesc = new Label
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill() - 2,
                CanFocus = false,
                Text = "• Claude Code runs the 'claude' CLI locally on your PATH.\n• OpenAI API connects over HTTP to Google AI Studio, OpenAI, Anthropic, LM Studio, Ollama, etc.",
            };
            providerDesc.SetScheme(Theme.LabelScheme(TextRole.System));

            section.Add(providerLabel, _providerList, providerDesc);
            return section;
        }

        private View BuildClaudeSection()
        {
            var section = new View
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = true,
                Visible = false,
            };
            section.SetScheme(Theme.CreateScheme());

            var claudeModelLabel = new Label
            {
                Text = "Preset Claude Models (Up/Down: highlight, Enter: pick):",
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            claudeModelLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            var modelListLabels = ClaudeModels.All
                .Select(m => string.IsNullOrEmpty(m.Id) ? $"{m.Name} ({m.Detail})" : $"{m.Name} - {m.Id} ({m.Detail})")
                .ToList();

            _claudeModelList = new NavListView
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = ClaudeModels.All.Length,
            };
            _claudeModelList.SetScheme(Theme.CreateScheme());
            _claudeModelList.SetSource(new ObservableCollection<string>(modelListLabels));
            _claudeModelList.BeforeKey = FormListKey;

            var currentModelIndex = ClaudeModels.IndexOf(_draft.ClaudeModel);
            if (currentModelIndex >= 0)
            {
                _claudeModelList.SelectedItem = currentModelIndex;
            }

            var customModelLabel = new Label
            {
                Text = "Or custom model identifier:",
                X = 1,
                Y = 7,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            customModelLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            _claudeCustomModel = new NavTextField
            {
                X = 1,
                Y = 8,
                Width = Dim.Fill() - 2,
                Text = _draft.ClaudeModel,
            };
            _claudeCustomModel.SetScheme(Theme.CreateScheme());
            _claudeCustomModel.BeforeKey = FormFieldKey;

            // Typing is explicit, so it updates the draft; the list highlight follows.
            _claudeCustomModel.TextChanged += (_, _) =>
            {
                _draft.ClaudeModel = _claudeCustomModel.Text?.Trim() ?? string.Empty;
                var idx = ClaudeModels.IndexOf(_draft.ClaudeModel);
                if (idx >= 0)
                {
                    _claudeModelList.SelectedItem = idx;
                }
                _claudeModelList.SetNeedsDraw();
                UpdateSummary();
            };
            _claudeCustomModel.Accepting += (_, _) => MoveFormFocus(1);

            _claudeModelList.RowRender += (_, e) =>
            {
                var isPicked = e.Row >= 0 && e.Row < ClaudeModels.All.Length
                    && string.Equals(ClaudeModels.All[e.Row].Id, _draft.ClaudeModel, StringComparison.OrdinalIgnoreCase);
                var isSelected = e.Row == _claudeModelList.SelectedItem;

                e.RowAttribute = (isSelected, isPicked) switch
                {
                    (true, true) => PickedAndSelectedAttr,
                    (true, false) => SelectedAttr,
                    (false, true) => PickedAttr,
                    _ => NormalAttr,
                };
            };
            _claudeModelList.ValueChanged += (_, _) => _claudeModelList.SetNeedsDraw();
            _claudeModelList.Accepting += (_, _) =>
            {
                var selected = _claudeModelList.SelectedItem ?? -1;
                if (selected >= 0 && selected < ClaudeModels.All.Length)
                {
                    _draft.ClaudeModel = ClaudeModels.All[selected].Id;
                    _claudeCustomModel.Text = _draft.ClaudeModel;
                    _claudeModelList.SetNeedsDraw();
                    UpdateSummary();
                    Say(_messageLabel, $"Picked model: {ClaudeModels.All[selected].Name}", TextRole.Place);
                    MoveFormFocus(1);
                }
            };

            var claudeNote = new Label
            {
                X = 1,
                Y = 10,
                Width = Dim.Fill() - 2,
                CanFocus = false,
                Text = "Note: Claude Code requires the 'claude' CLI command to be installed and authenticated on your PATH.",
            };
            claudeNote.SetScheme(Theme.LabelScheme(TextRole.System));

            section.Add(claudeModelLabel, _claudeModelList, customModelLabel, _claudeCustomModel, claudeNote);
            return section;
        }

        private View BuildOpenAiSection()
        {
            var section = new View
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = true,
                Visible = false,
            };
            section.SetScheme(Theme.CreateScheme());

            var urlLabel = new Label
            {
                Text = "Server Base URL (http:// or https://):",
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            urlLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            _lmStudioBaseUrl = new NavTextField
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Text = _draft.LmStudioBaseUrl,
            };
            _lmStudioBaseUrl.SetScheme(Theme.CreateScheme());
            _lmStudioBaseUrl.BeforeKey = FormFieldKey;

            var presetLabel = new Label
            {
                Text = "Preset (Up/Down: highlight, Enter: apply endpoint):",
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            presetLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            _presetList = new NavListView
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill() - 2,
                Height = OpenAiPresets.All.Length,
            };
            _presetList.SetScheme(Theme.CreateScheme());
            _presetList.SetSource(new ObservableCollection<string>(OpenAiPresets.All.Select(p => p.Name).ToList()));
            _presetList.BeforeKey = FormListKey;

            _presetDetails = new Label
            {
                X = 1,
                Y = 9,
                Width = Dim.Fill() - 2,
                Height = 1,
                CanFocus = false,
            };
            _presetDetails.SetScheme(Theme.LabelScheme(TextRole.Normal));

            var apiKeyLabel = new Label
            {
                Text = "API Key (optional depending on vendor configuration):",
                X = 1,
                Y = 11,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            apiKeyLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            _lmStudioApiKey = new NavTextField
            {
                X = 1,
                Y = 12,
                Width = Dim.Fill() - 2,
                Text = _draft.LmStudioApiKey,
                Secret = true,
            };
            _lmStudioApiKey.SetScheme(Theme.CreateScheme());
            _lmStudioApiKey.BeforeKey = FormFieldKey;

            var modelLabel = new Label
            {
                Text = "Model Name / ID (or probe with button below):",
                X = 1,
                Y = 14,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            modelLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            _lmStudioModel = new NavTextField
            {
                X = 1,
                Y = 15,
                Width = Dim.Fill() - 2,
                Text = _draft.LmStudioModel,
            };
            _lmStudioModel.SetScheme(Theme.CreateScheme());
            _lmStudioModel.BeforeKey = FormFieldKey;

            _probeButton = new Button
            {
                X = 1,
                Y = 17,
                Text = "Probe Models",
            };
            _probeButton.SetScheme(Theme.CreateScheme());

            _probeStatus = new Label
            {
                X = Pos.Right(_probeButton) + 2,
                Y = 17,
                Width = Dim.Fill() - 2,
                CanFocus = false,
                Text = string.Empty,
            };
            _probeStatus.SetScheme(Theme.CreateScheme());

            _lmStudioModelsList = new NavListView
            {
                X = 1,
                Y = 19,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill(1),
                Visible = false,
            };
            _lmStudioModelsList.SetScheme(Theme.CreateScheme());
            _lmStudioModelsList.BeforeKey = FormListKey;

            // Typing is explicit: the draft follows the URL, and the preset highlight
            // follows the URL without touching the draft.
            _lmStudioBaseUrl.TextChanged += (_, _) =>
            {
                if (_isApplyingPreset)
                {
                    return;
                }

                var url = _lmStudioBaseUrl.Text?.Trim() ?? string.Empty;
                _draft.LmStudioBaseUrl = url;
                _draft.OpenAiPreset = OpenAiPresets.DetectPreset(url).Name;
                SyncPresetHighlight();
                UpdateSummary();
            };
            _lmStudioBaseUrl.Accepting += (_, _) => MoveFormFocus(1);
            _lmStudioApiKey.TextChanged += (_, _) => _draft.LmStudioApiKey = _lmStudioApiKey.Text?.Trim() ?? string.Empty;
            _lmStudioApiKey.Accepting += (_, _) => MoveFormFocus(1);
            _lmStudioModel.TextChanged += (_, _) =>
            {
                _draft.LmStudioModel = _lmStudioModel.Text?.Trim() ?? string.Empty;
                UpdateSummary();
            };
            _lmStudioModel.Accepting += (_, _) => MoveFormFocus(1);

            _presetList.RowRender += (_, e) =>
            {
                var isPicked = e.Row >= 0 && e.Row < OpenAiPresets.All.Length
                    && string.Equals(OpenAiPresets.All[e.Row].Name, _draft.OpenAiPreset, StringComparison.OrdinalIgnoreCase);
                var isSelected = e.Row == _presetList.SelectedItem;

                e.RowAttribute = (isSelected, isPicked) switch
                {
                    (true, true) => PickedAndSelectedAttr,
                    (true, false) => SelectedAttr,
                    (false, true) => PickedAttr,
                    _ => NormalAttr,
                };
            };
            _presetList.ValueChanged += (_, _) =>
            {
                UpdatePresetDetails();
                _presetList.SetNeedsDraw();
            };
            _presetList.Accepting += (_, _) =>
            {
                if (ApplyPresetSelection(_presetList.SelectedItem ?? -1))
                {
                    MoveFormFocus(1);
                }
            };

            _lmStudioModelsList.RowRender += (_, e) =>
            {
                var isPicked = e.Row >= 0 && e.Row < _probedModels.Count
                    && string.Equals(_probedModels[e.Row], _draft.LmStudioModel, StringComparison.OrdinalIgnoreCase);
                var isSelected = e.Row == _lmStudioModelsList.SelectedItem;

                e.RowAttribute = (isSelected, isPicked) switch
                {
                    (true, true) => PickedAndSelectedAttr,
                    (true, false) => SelectedAttr,
                    (false, true) => PickedAttr,
                    _ => NormalAttr,
                };
            };
            _lmStudioModelsList.ValueChanged += (_, _) => _lmStudioModelsList.SetNeedsDraw();
            _lmStudioModelsList.Accepting += (_, _) =>
            {
                var selected = _lmStudioModelsList.SelectedItem ?? -1;
                if (selected >= 0 && selected < _probedModels.Count)
                {
                    var modelName = _probedModels[selected];
                    _lmStudioModel.Text = modelName;
                    _draft.LmStudioModel = modelName;
                    Say(_probeStatus, $"Picked: {modelName}", TextRole.Place);
                    _lmStudioModelsList.SetNeedsDraw();
                    UpdateSummary();
                    MoveFormFocus(1);
                }
            };

            _probeButton.Accepting += async (_, _) =>
            {
                await ProbeLmStudioModelsAsync();
            };

            SyncPresetHighlight();
            UpdatePresetDetails();

            section.Add(
                urlLabel,
                _lmStudioBaseUrl,
                presetLabel,
                _presetList,
                _presetDetails,
                apiKeyLabel,
                _lmStudioApiKey,
                modelLabel,
                _lmStudioModel,
                _probeButton,
                _probeStatus,
                _lmStudioModelsList);
            return section;
        }

        private View BuildPrefsSection()
        {
            var section = new View
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = true,
                Visible = false,
            };
            section.SetScheme(Theme.CreateScheme());

            var recallLabel = new Label
            {
                Text = "Transcript Recall Characters:",
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            recallLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            _recallChars = new NavTextField
            {
                X = 1,
                Y = 1,
                Width = 15,
                Text = _draft.TranscriptRecallCharacters.ToString(),
            };
            _recallChars.SetScheme(Theme.CreateScheme());
            _recallChars.BeforeKey = FormFieldKey;
            _recallChars.Accepting += (_, _) => MoveFormFocus(1);

            var recallDesc = new Label
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 2,
                CanFocus = false,
                Text = $"Sets how much text from the previous session is re-read when continuing a save.\nBoundaries: {TranscriptRecall.MinCharacters} to {TranscriptRecall.MaxCharacters} characters.\nDefault: {TranscriptRecall.DefaultCharacters} characters.",
            };
            recallDesc.SetScheme(Theme.LabelScheme(TextRole.System));

            var editorLabel = new Label
            {
                Text = "External Editor Command (for Ctrl+G):",
                X = 1,
                Y = 7,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            editorLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            _editorCommand = new NavTextField
            {
                X = 1,
                Y = 8,
                Width = Dim.Fill() - 2,
                Text = _draft.EditorCommand,
            };
            _editorCommand.SetScheme(Theme.CreateScheme());
            _editorCommand.BeforeKey = FormFieldKey;
            _editorCommand.TextChanged += (_, _) => _draft.EditorCommand = _editorCommand.Text?.Trim() ?? string.Empty;
            _editorCommand.Accepting += (_, _) => MoveFormFocus(1);

            _testEditorButton = new Button
            {
                X = 1,
                Y = 10,
                Text = "Test Editor",
            };
            _testEditorButton.SetScheme(Theme.CreateScheme());

            _openConfigFolderButton = new Button
            {
                X = Pos.Right(_testEditorButton) + 2,
                Y = 10,
                Text = "Open Config Folder",
            };
            _openConfigFolderButton.SetScheme(Theme.CreateScheme());

            _editorStatus = new Label
            {
                X = 1,
                Y = 12,
                Width = Dim.Fill() - 2,
                CanFocus = false,
                Text = string.Empty,
            };
            _editorStatus.SetScheme(Theme.CreateScheme());

            _testEditorButton.Accepting += (_, _) =>
            {
                var cmd = _editorCommand.Text?.Trim() ?? string.Empty;
                if (EditorCommandLine.TryParse(cmd, out var parsed, out var reason))
                {
                    Say(_editorStatus, $"Editor found: {parsed.Display}", TextRole.Place);
                }
                else
                {
                    Say(_editorStatus, $"Editor error: {reason}", TextRole.Danger);
                }
            };

            _openConfigFolderButton.Accepting += (_, _) =>
            {
                var dir = PathProvider.Root;
                Directory.CreateDirectory(dir);
                if (!FileExplorer.TryOpen(dir, out var reason))
                {
                    Say(_editorStatus, reason ?? "Could not open folder.", TextRole.Danger);
                }
            };

            section.Add(recallLabel, _recallChars, recallDesc, editorLabel, _editorCommand, _testEditorButton, _openConfigFolderButton, _editorStatus);
            return section;
        }

        /// <summary>
        /// The focusable controls of the visible section, in top-to-bottom order.
        /// Hidden controls (like the probe results before probing) and disabled controls
        /// (like the probe button while probing) are skipped.
        /// </summary>
        private List<View> ActiveFormControls()
        {
            List<View> controls = _activeSection switch
            {
                SettingsSection.Provider => [_providerList],
                SettingsSection.ClaudeCode => [_claudeModelList, _claudeCustomModel],
                SettingsSection.OpenAiApi => (_lmStudioModelsList.Visible
                    ? [_lmStudioBaseUrl, _presetList, _lmStudioApiKey, _lmStudioModel, _probeButton, _lmStudioModelsList]
                    : [_lmStudioBaseUrl, _presetList, _lmStudioApiKey, _lmStudioModel, _probeButton]),
                _ => [_recallChars, _editorCommand, _testEditorButton, _openConfigFolderButton],
            };

            return controls.Where(c => c.Visible && c.Enabled).ToList();
        }

        private IEnumerable<View> AllFormControls() =>
        [
            _providerList,
            _claudeModelList, _claudeCustomModel,
            _lmStudioBaseUrl, _presetList, _lmStudioApiKey, _lmStudioModel, _probeButton, _lmStudioModelsList,
            _recallChars, _editorCommand, _testEditorButton, _openConfigFolderButton,
        ];

        private bool IsNavFocused() => MostFocused == _sectionsList;

        private bool IsFooterFocused() => MostFocused is Button focused && _actionButtons.Contains(focused);

        private bool IsFormFocused() => MostFocused is { } focused && ActiveFormControls().Contains(focused);

        /// <summary>
        /// Moves focus through the panes: sections list, form, footer. Claimed before
        /// anything else, so Tab never walks the form's fields one by one.
        /// </summary>
        private void CyclePane(int delta)
        {
            var current = IsNavFocused() ? 0 : IsFormFocused() ? 1 : IsFooterFocused() ? 2 : (delta > 0 ? 2 : 0);
            var next = (current + delta + 3) % 3;

            if (next == 0)
            {
                _sectionsList.SetFocus();
            }
            else if (next == 1)
            {
                FocusForm();
            }
            else
            {
                FocusActions();
            }

            UpdatePaneChrome();
        }

        /// <summary>
        /// Focuses the form, returning to the control used last where possible.
        /// </summary>
        private void FocusForm()
        {
            var controls = ActiveFormControls();
            if (_lastFormFocus is { } last && controls.Contains(last) && last.Visible && last.Enabled)
            {
                last.SetFocus();
            }
            else if (controls.Count > 0)
            {
                controls[0].SetFocus();
            }

            UpdatePaneChrome();
        }

        /// <summary>
        /// Moves focus through the visible section's controls, wrapping at both ends so
        /// the focus never leaks into another pane except through <see cref="CyclePane"/>.
        /// </summary>
        private void MoveFormFocus(int delta)
        {
            var controls = ActiveFormControls();
            if (controls.Count == 0)
            {
                return;
            }

            var current = MostFocused is { } focused ? controls.IndexOf(focused) : -1;
            if (current < 0)
            {
                current = delta < 0 ? controls.Count - 1 : 0;
            }
            else
            {
                current = (current + delta + controls.Count) % controls.Count;
            }

            _lastFormFocus = controls[current];
            controls[current].SetFocus();
            UpdatePaneChrome();
        }

        /// <summary>
        /// Focuses the action bar, returning to the button used last where possible.
        /// </summary>
        private void FocusActions()
        {
            var index = Math.Clamp(_lastActionIndex, 0, _actionButtons.Count - 1);
            _actionButtons[index].SetFocus();
            UpdatePaneChrome();
        }

        /// <summary>
        /// Moves focus linearly through the action buttons, wrapping at both ends so the
        /// focus never leaks back to the form except through <see cref="CyclePane"/>.
        /// </summary>
        private void MoveActionFocus(int delta)
        {
            var current = MostFocused is Button focused ? _actionButtons.IndexOf(focused) : _lastActionIndex;
            if (current < 0)
            {
                current = delta < 0 ? _actionButtons.Count - 1 : 0;
            }
            else
            {
                current = (current + delta + _actionButtons.Count) % _actionButtons.Count;
            }

            _lastActionIndex = current;
            _actionButtons[current].SetFocus();
            UpdatePaneChrome();
        }

        /// <summary>
        /// Marks the focused pane in the frame titles so it reads without colour, and
        /// shows that pane's hints. The feedback line is separate and never touched here.
        /// </summary>
        private void UpdatePaneChrome()
        {
            if (MostFocused is Button focused)
            {
                var index = _actionButtons.IndexOf(focused);
                if (index >= 0)
                {
                    _lastActionIndex = index;
                }
            }

            if (MostFocused is { } form && ActiveFormControls().Contains(form))
            {
                _lastFormFocus = form;
            }

            var inNav = IsNavFocused();
            var inForm = IsFormFocused();
            var inFooter = IsFooterFocused();

            _navFrame.Title = inNav ? "* Sections" : "Sections";
            _formFrame.Title = inForm ? $"* {SectionNames[(int)_activeSection]}" : SectionNames[(int)_activeSection];
            _actionsFrame.Title = inFooter ? "* Actions" : "Actions";

            _hintLabel.Text = (inNav, inForm, inFooter) switch
            {
                (true, _, _) => "Up/Down: section | Enter: edit section | Tab: next pane | Ctrl+S: save | Esc: cancel",
                (_, true, _) => "Up/Down: fields (rows in lists) | Left/Right: fields in lists | Enter: pick / next field | Tab: next pane | Ctrl+S: save",
                (_, _, true) => "Left/Right: choose action | Enter: run | Tab: next pane | Ctrl+S: save | Esc: cancel",
                _ => "Tab: switch pane | Up/Down: move | Enter: pick | Ctrl+S: save | Esc: cancel",
            };
        }

        /// <summary>
        /// The sections list keeps Up/Down (switching sections changes nothing but the
        /// visible form) while Tab and Right move on and Left stays put.
        /// </summary>
        private bool NavListKey(Key key)
        {
            if (key == Key.Tab)
            {
                CyclePane(1);
                return true;
            }

            if (key == Key.Tab.WithShift)
            {
                CyclePane(-1);
                return true;
            }

            if (key == Key.CursorRight)
            {
                FocusForm();
                return true;
            }

            if (key == Key.CursorLeft)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// In a form list, Up/Down navigate rows natively while Left/Right move between
        /// fields so the focus can leave the list without reaching for Tab.
        /// </summary>
        private bool FormListKey(Key key)
        {
            if (key == Key.Tab)
            {
                CyclePane(1);
                return true;
            }

            if (key == Key.Tab.WithShift)
            {
                CyclePane(-1);
                return true;
            }

            if (key == Key.CursorLeft)
            {
                MoveFormFocus(-1);
                return true;
            }

            if (key == Key.CursorRight)
            {
                MoveFormFocus(1);
                return true;
            }

            return false;
        }

        /// <summary>
        /// In a form text field, Left/Right edit natively while Up/Down move between
        /// fields and Enter advances (wired per field via <see cref="Accepting"/>).
        /// </summary>
        private bool FormFieldKey(Key key)
        {
            if (key == Key.Tab)
            {
                CyclePane(1);
                return true;
            }

            if (key == Key.Tab.WithShift)
            {
                CyclePane(-1);
                return true;
            }

            if (key == Key.CursorUp)
            {
                MoveFormFocus(-1);
                return true;
            }

            if (key == Key.CursorDown)
            {
                MoveFormFocus(1);
                return true;
            }

            return false;
        }

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Esc)
            {
                CancelAndClose();
                return true;
            }

            if (key == Key.S.WithCtrl)
            {
                SaveAndClose();
                return true;
            }

            // Claimed before anything else, so Tab never walks the form's fields one by one.
            if (key == Key.Tab || key == Key.Tab.WithShift)
            {
                CyclePane(key == Key.Tab ? 1 : -1);
                return true;
            }

            // Inside the footer the arrows stay in the footer: they cycle the buttons.
            if (IsFooterFocused())
            {
                if (key == Key.CursorLeft || key == Key.CursorUp)
                {
                    MoveActionFocus(-1);
                    return true;
                }

                if (key == Key.CursorRight || key == Key.CursorDown)
                {
                    MoveActionFocus(1);
                    return true;
                }

                if (key == Key.Home)
                {
                    _lastActionIndex = 0;
                    _actionButtons[0].SetFocus();
                    return true;
                }

                if (key == Key.End)
                {
                    _lastActionIndex = _actionButtons.Count - 1;
                    _actionButtons[^1].SetFocus();
                    return true;
                }
            }

            // Inside the form, buttons bubble here (lists and fields handle their own
            // keys first): the arrows move between the section's fields.
            if (IsFormFocused() && MostFocused is Button)
            {
                if (key == Key.CursorLeft || key == Key.CursorUp)
                {
                    MoveFormFocus(-1);
                    return true;
                }

                if (key == Key.CursorRight || key == Key.CursorDown)
                {
                    MoveFormFocus(1);
                    return true;
                }
            }

            return base.OnKeyDown(key);
        }

        private void ShowSection(SettingsSection section)
        {
            _activeSection = section;
            _providerSection.Visible = section == SettingsSection.Provider;
            _claudeSection.Visible = section == SettingsSection.ClaudeCode;
            _openAiSection.Visible = section == SettingsSection.OpenAiApi;
            _prefsSection.Visible = section == SettingsSection.Preferences;
            UpdatePaneChrome();
            SetNeedsDraw();
        }

        private void UpdateSummary()
        {
            var claudeDesc = string.IsNullOrEmpty(_draft.ClaudeModel)
                ? "CLI Default"
                : ClaudeModels.Describe(_draft.ClaudeModel);

            var openAiDesc = string.IsNullOrEmpty(_draft.LmStudioModel)
                ? $"{_draft.OpenAiPreset} (Default loaded model)"
                : $"{_draft.OpenAiPreset} ({_draft.LmStudioModel})";

            if (_draft.Provider == AgentProvider.ClaudeCode)
            {
                _summaryActiveLabel.Text = $"Current Configuration: Claude Code [{claudeDesc}]";
                _summaryStandbyLabel.Text = $"OpenAI API standby: [{openAiDesc}]";
            }
            else
            {
                _summaryActiveLabel.Text = $"Current Configuration: OpenAI API [{openAiDesc}]";
                _summaryStandbyLabel.Text = $"Claude Code standby: [{claudeDesc}]";
            }
        }

        /// <summary>
        /// Writes a feedback line in the role that matches its severity: green for
        /// confirmations, red for errors, plain for passing information.
        /// </summary>
        private static void Say(Label line, string text, TextRole role = TextRole.Normal)
        {
            line.Text = text;
            line.SetScheme(Theme.LabelScheme(role));
            line.SetNeedsDraw();
        }

        private void SyncPresetHighlight()
        {
            var detected = OpenAiPresets.DetectPreset(_draft.LmStudioBaseUrl);
            var index = Array.FindIndex(OpenAiPresets.All, p => p.Name == detected.Name);
            if (index >= 0)
            {
                _presetList.SelectedItem = index;
            }
            UpdatePresetDetails();
            _presetList.SetNeedsDraw();
        }

        private void UpdatePresetDetails()
        {
            var highlighted = _presetList.SelectedItem ?? -1;
            if (highlighted >= 0 && highlighted < OpenAiPresets.All.Length)
            {
                var preset = OpenAiPresets.All[highlighted];
                _presetDetails.Text = $"Endpoint: {preset.BaseUrl}";
            }
            else
            {
                _presetDetails.Text = string.Empty;
            }
        }

        private bool ApplyPresetSelection(int index)
        {
            if (index < 0 || index >= OpenAiPresets.All.Length)
            {
                return false;
            }

            var matched = OpenAiPresets.All[index];
            _draft.OpenAiPreset = matched.Name;
            _draft.LmStudioBaseUrl = matched.BaseUrl;

            _isApplyingPreset = true;
            try
            {
                _lmStudioBaseUrl.Text = matched.BaseUrl;
            }
            finally
            {
                _isApplyingPreset = false;
            }

            if (!string.IsNullOrEmpty(matched.DefaultModel) &&
                (string.IsNullOrEmpty(_draft.LmStudioModel) || OpenAiPresets.All.Any(p => !string.IsNullOrEmpty(p.DefaultModel) && p.DefaultModel == _draft.LmStudioModel)))
            {
                _draft.LmStudioModel = matched.DefaultModel;
                _lmStudioModel.Text = matched.DefaultModel;
            }

            SyncPresetHighlight();
            UpdateSummary();
            Say(_messageLabel, $"Selected preset: {matched.Name}", TextRole.Place);
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _probe?.Cancel();
                _probe?.Dispose();
                _probe = null;
                Editor?.Abandon();
            }

            base.Dispose(disposing);
        }

        private async Task ProbeLmStudioModelsAsync()
        {
            var rawUrl = _lmStudioBaseUrl.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(rawUrl) || !AppSettings.IsAddress(rawUrl))
            {
                Say(_probeStatus, "Enter a valid server URL before probing.", TextRole.Danger);
                return;
            }

            var baseUrl = AppSettings.NormalizeBaseUrl(rawUrl);

            _probe?.Cancel();
            _probe?.Dispose();
            _probe = new CancellationTokenSource(ProbeTimeout);

            _probeButton.Enabled = false;
            if (MostFocused == _probeButton)
            {
                _lmStudioModel.SetFocus();
                UpdatePaneChrome();
            }

            Say(_probeStatus, "Connecting to API endpoint...");

            try
            {
                var models = await LmStudioModels.ListAsync(baseUrl, _lmStudioApiKey.Text?.Trim(), ProbeTimeout, _probe.Token);

                _app.Invoke(() =>
                {
                    if (models.Count == 0)
                    {
                        Say(_probeStatus, "Connected, but no models found.");
                        _lmStudioModelsList.Visible = false;
                        _probedModels.Clear();
                    }
                    else
                    {
                        var sortedModels = models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
                        Say(_probeStatus, $"Found {sortedModels.Count} model(s). Select with Up/Down, Enter to pick:");
                        _probedModels.Clear();
                        _probedModels.AddRange(sortedModels);
                        _lmStudioModelsList.SetSource(new ObservableCollection<string>(sortedModels));
                        _lmStudioModelsList.Visible = true;
                        _lmStudioModelsList.SetFocus();
                    }
                });
            }
            catch (Exception ex)
            {
                _app.Invoke(() =>
                {
                    var firstLine = ex.Message.IndexOf('\n') > 0 ? ex.Message[..ex.Message.IndexOf('\n')] : ex.Message;
                    Say(_probeStatus, $"Probe failed: {firstLine}", TextRole.Danger);
                    _lmStudioModelsList.Visible = false;
                    _probedModels.Clear();
                });
            }
            finally
            {
                _app.Invoke(() =>
                {
                    _probeButton.Enabled = true;
                });
            }
        }

        private void RestoreDefaults()
        {
            var defaults = new AppSettings();
            _draft.CopyFrom(defaults);

            _providerList.SelectedItem = defaults.Provider == AgentProvider.ClaudeCode ? 0 : 1;
            _providerList.SetNeedsDraw();

            var modelIdx = ClaudeModels.IndexOf(defaults.ClaudeModel);
            if (modelIdx >= 0)
            {
                _claudeModelList.SelectedItem = modelIdx;
            }
            _claudeCustomModel.Text = defaults.ClaudeModel;
            _claudeModelList.SetNeedsDraw();

            _lmStudioBaseUrl.Text = defaults.LmStudioBaseUrl;
            _lmStudioApiKey.Text = defaults.LmStudioApiKey;
            _lmStudioModel.Text = defaults.LmStudioModel;
            _draft.OpenAiPreset = OpenAiPresets.DetectPreset(defaults.LmStudioBaseUrl).Name;
            SyncPresetHighlight();
            _probedModels.Clear();
            _lmStudioModelsList.Visible = false;
            _probeStatus.Text = string.Empty;

            _recallChars.Text = defaults.TranscriptRecallCharacters.ToString();
            _editorCommand.Text = defaults.EditorCommand;
            _editorStatus.Text = string.Empty;

            UpdateSummary();
            Say(_messageLabel, "Restored all settings to defaults. Press Ctrl+S to save.", TextRole.Place);
            SetNeedsDraw();
        }

        private void SaveAndClose()
        {
            var baseUrl = _lmStudioBaseUrl.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(baseUrl) && !AppSettings.IsAddress(baseUrl))
            {
                Say(_messageLabel, "OpenAI API Base URL must be a valid http:// or https:// address.", TextRole.Danger);
                SwitchToSection(SettingsSection.OpenAiApi);
                _lmStudioBaseUrl.SetFocus();
                return;
            }
            _draft.LmStudioBaseUrl = AppSettings.NormalizeBaseUrl(baseUrl);
            _draft.LmStudioApiKey = _lmStudioApiKey.Text?.Trim() ?? string.Empty;
            _draft.LmStudioModel = _lmStudioModel.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_draft.OpenAiPreset))
            {
                _draft.OpenAiPreset = OpenAiPresets.DetectPreset(_draft.LmStudioBaseUrl).Name;
            }

            var recallText = _recallChars.Text?.Trim() ?? string.Empty;
            if (!int.TryParse(recallText, out var recall)
                || recall < TranscriptRecall.MinCharacters
                || recall > TranscriptRecall.MaxCharacters)
            {
                Say(_messageLabel, $"Transcript Recall must be an integer between {TranscriptRecall.MinCharacters} and {TranscriptRecall.MaxCharacters}.", TextRole.Danger);
                SwitchToSection(SettingsSection.Preferences);
                _recallChars.SetFocus();
                return;
            }
            _draft.TranscriptRecallCharacters = recall;

            var editorCmd = _editorCommand.Text?.Trim() ?? string.Empty;
            _draft.EditorCommand = editorCmd;

            // Provider and Claude model come from the draft, which lists only update
            // on Enter and text fields update as typed: browsing never leaks in.
            // Commit and save to disk
            try
            {
                SettingsStore.Write(_draft, _settingsPath);
            }
            catch (Exception ex)
            {
                Say(_messageLabel, $"Could not save settings file: {ex.Message}", TextRole.Danger);
                return;
            }

            Chosen = _draft;
            Done?.Invoke();
        }

        private void CancelAndClose()
        {
            Cancelled?.Invoke();
        }
    }
}
