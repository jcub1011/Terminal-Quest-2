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
    /// The settings screen: who narrates, model options, and application preferences.
    /// Built with Terminal.Gui's built-in <see cref="Tabs"/> for tabbed navigation,
    /// offering dedicated tabs for Engine selection, Claude Code, OpenAI API, Memory, and Editor settings.
    /// </summary>
    internal sealed class SettingsWindow : Window
    {
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

        private static readonly Attribute PickedAndSelectedAttr = new(new Color("#1b5e20"), Color.White);
        private static readonly Attribute PickedAttr = new(new Color("#8fb26a"), Color.None);
        private static readonly Attribute SelectedAttr = new(Color.Black, Color.White);
        private static readonly Attribute NormalAttr = new(new Color("#d7d2c4"), Color.None);

        private static readonly (string Name, string Display, string Url, string DefaultModel)[] PresetChoices =
        [
            ("Google", "Google (https://generativelanguage.googleapis.com/v1beta/openai)", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-flash-lite-latest"),
            ("OpenAI", "OpenAI (https://api.openai.com/v1)", "https://api.openai.com/v1", "gpt-4o-mini"),
            ("Anthropic", "Anthropic (https://api.anthropic.com/v1)", "https://api.anthropic.com/v1", "claude-3-5-sonnet-20241022"),
            ("Custom", "Custom / Local (http://localhost:1234/v1)", "http://localhost:1234/v1", string.Empty),
        ];

        private readonly IApplication _app;
        private readonly AppSettings _original;
        private readonly AppSettings _draft;

        private readonly Tabs _tabs;
        private readonly Label _statusLabel;

        // Top-level tab views
        private readonly View _engineTabView;
        private readonly View _claudeTabView;
        private readonly View _openAiTabView;
        private readonly View _memoryTabView;
        private readonly View _editorTabView;

        // Engine tab controls
        private readonly ListView _providerList;
        private readonly Label _engineSummaryLabel;

        // Claude tab controls
        private readonly ListView _claudeModelList;
        private readonly TextField _claudeCustomModel;

        // OpenAI API tab controls
        private readonly TextField _lmStudioBaseUrl;
        private readonly DropDownList _openAiPresetDropDown;
        private readonly TextField _lmStudioApiKey;
        private readonly TextField _lmStudioModel;
        private readonly ListView _lmStudioModelsList;
        private readonly Button _probeButton;
        private readonly Label _probeStatus;
        private readonly List<string> _probedModels = [];
        private CancellationTokenSource? _probe;
        private bool _isUpdatingPresetFromUrl;

        // Memory tab controls
        private readonly TextField _recallChars;

        // Editor tab controls
        private readonly TextField _editorCommand;
        private readonly Button _testEditorButton;
        private readonly Button _openConfigFolderButton;
        private readonly Label _editorStatus;

        // Bottom action buttons
        private readonly Button _saveButton;
        private readonly Button _cancelButton;
        private readonly Button _defaultsButton;

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

            _tabs = new Tabs
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill() - 3,
                CanFocus = true,
                TabStop = TabBehavior.TabGroup,
            };
            _tabs.SetScheme(Theme.CreateScheme());

            _statusLabel = new Label
            {
                X = 1,
                Y = Pos.Bottom(_tabs),
                Width = Dim.Fill() - 2,
                Height = 1,
                Text = "Left/Right: Switch Tabs | Down: Enter Tab | Tab: Next Field | Ctrl+S: Save | Esc: Cancel",
            };
            _statusLabel.SetScheme(Theme.CreateScheme());

            // ==========================================
            // 1. ENGINE TAB
            // ==========================================
            _engineTabView = new View
            {
                Title = "_Engine",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            _engineTabView.SetScheme(Theme.CreateScheme());

            var providerLabel = new Label
            {
                Text = "Active Narrative Provider (Up/Down to navigate, Enter to select):",
                X = 1,
                Y = 1,
            };
            providerLabel.SetScheme(Theme.CreateScheme());

            _providerList = new ListView
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 2,
                Height = 3,
            };
            _providerList.SetScheme(Theme.CreateScheme());
            _providerList.SetSource(new ObservableCollection<string>(["Claude Code (Anthropic CLI)", "OpenAI API (Google, OpenAI, Anthropic, LM Studio, etc.)"]));
            _providerList.SelectedItem = _draft.Provider == AgentProvider.ClaudeCode ? 0 : 1;

            _providerList.RowRender += (_, e) =>
            {
                var isPicked = (e.Row == 0 && _draft.Provider == AgentProvider.ClaudeCode)
                            || (e.Row == 1 && _draft.Provider == AgentProvider.OpenAiApi);
                var isSelected = e.Row == _providerList.SelectedItem;

                if (isSelected)
                {
                    e.RowAttribute = isPicked ? PickedAndSelectedAttr : SelectedAttr;
                }
                else
                {
                    e.RowAttribute = isPicked ? PickedAttr : NormalAttr;
                }
            };

            _providerList.Accepting += (_, _) =>
            {
                var selected = _providerList.SelectedItem ?? 0;
                _draft.Provider = selected == 0 ? AgentProvider.ClaudeCode : AgentProvider.OpenAiApi;
                _providerList.SetNeedsDraw();
                UpdateEngineSummary();
                _statusLabel.Text = $"Active provider set to: {(_draft.Provider == AgentProvider.ClaudeCode ? "Claude Code" : "OpenAI API")}";
            };

            _providerList.ValueChanged += (_, _) =>
            {
                var selected = _providerList.SelectedItem ?? 0;
                _draft.Provider = selected == 0 ? AgentProvider.ClaudeCode : AgentProvider.OpenAiApi;
                UpdateEngineSummary();
                _providerList.SetNeedsDraw();
            };

            _engineSummaryLabel = new Label
            {
                X = 1,
                Y = 7,
                Width = Dim.Fill() - 2,
                Height = 2,
            };
            _engineSummaryLabel.SetScheme(Theme.CreateScheme());

            var engineDesc = new Label
            {
                X = 1,
                Y = 10,
                Width = Dim.Fill() - 2,
                Text = "• Claude Code runs the 'claude' CLI locally on your PATH.\n• OpenAI API connects over HTTP to Google AI Studio, OpenAI, Anthropic, LM Studio, Ollama, etc.\n• Select the 'Claude Code' or 'OpenAI API' tabs above to configure each provider.",
            };
            engineDesc.SetScheme(Theme.CreateScheme());

            _engineTabView.Add(providerLabel, _providerList, _engineSummaryLabel, engineDesc);

            // ==========================================
            // 2. CLAUDE CODE TAB
            // ==========================================
            _claudeTabView = new View
            {
                Title = "_Claude Code",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            _claudeTabView.SetScheme(Theme.CreateScheme());

            var claudeModelLabel = new Label
            {
                Text = "Preset Claude Models (Select with Up/Down, press Enter to pick):",
                X = 1,
                Y = 1,
            };
            claudeModelLabel.SetScheme(Theme.CreateScheme());

            var modelListLabels = ClaudeModels.All
                .Select(m => string.IsNullOrEmpty(m.Id) ? $"{m.Name} ({m.Detail})" : $"{m.Name} - {m.Id} ({m.Detail})")
                .ToList();

            _claudeModelList = new ListView
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 2,
                Height = 6,
            };
            _claudeModelList.SetScheme(Theme.CreateScheme());
            _claudeModelList.SetSource(new ObservableCollection<string>(modelListLabels));

            var currentModelIndex = ClaudeModels.IndexOf(_draft.ClaudeModel);
            if (currentModelIndex >= 0)
            {
                _claudeModelList.SelectedItem = currentModelIndex;
            }

            var customModelLabel = new Label { Text = "Or custom model identifier:", X = 1, Y = 10 };
            customModelLabel.SetScheme(Theme.CreateScheme());

            _claudeCustomModel = new TextField
            {
                X = 1,
                Y = 11,
                Width = 50,
                Text = _draft.ClaudeModel,
            };
            _claudeCustomModel.SetScheme(Theme.CreateScheme());

            _claudeCustomModel.TextChanged += (_, _) =>
            {
                _draft.ClaudeModel = _claudeCustomModel.Text?.Trim() ?? string.Empty;
                var idx = ClaudeModels.IndexOf(_draft.ClaudeModel);
                if (idx >= 0)
                {
                    _claudeModelList.SelectedItem = idx;
                }
                _claudeModelList.SetNeedsDraw();
                UpdateEngineSummary();
            };

            _claudeModelList.RowRender += (_, e) =>
            {
                var isPicked = e.Row >= 0 && e.Row < ClaudeModels.All.Length
                    && string.Equals(ClaudeModels.All[e.Row].Id, _draft.ClaudeModel, StringComparison.OrdinalIgnoreCase);
                var isSelected = e.Row == _claudeModelList.SelectedItem;

                if (isSelected)
                {
                    e.RowAttribute = isPicked ? PickedAndSelectedAttr : SelectedAttr;
                }
                else
                {
                    e.RowAttribute = isPicked ? PickedAttr : NormalAttr;
                }
            };

            _claudeModelList.Accepting += (_, _) =>
            {
                var selected = _claudeModelList.SelectedItem ?? -1;
                if (selected >= 0 && selected < ClaudeModels.All.Length)
                {
                    _draft.ClaudeModel = ClaudeModels.All[selected].Id;
                    _claudeCustomModel.Text = _draft.ClaudeModel;
                    _claudeModelList.SetNeedsDraw();
                    UpdateEngineSummary();
                    _statusLabel.Text = $"Picked model: {ClaudeModels.All[selected].Name}";
                }
            };

            _claudeModelList.ValueChanged += (_, _) =>
            {
                var selected = _claudeModelList.SelectedItem ?? -1;
                if (selected >= 0 && selected < ClaudeModels.All.Length)
                {
                    _draft.ClaudeModel = ClaudeModels.All[selected].Id;
                    _claudeCustomModel.Text = _draft.ClaudeModel;
                    UpdateEngineSummary();
                }
                _claudeModelList.SetNeedsDraw();
            };

            var claudeNote = new Label
            {
                X = 1,
                Y = 13,
                Width = Dim.Fill() - 2,
                Text = "Note: Claude Code requires the 'claude' CLI command to be installed and authenticated on your PATH.",
            };
            claudeNote.SetScheme(Theme.CreateScheme());

            _claudeTabView.Add(claudeModelLabel, _claudeModelList, customModelLabel, _claudeCustomModel, claudeNote);

            // ==========================================
            // 3. OPENAI API TAB
            // ==========================================
            _openAiTabView = new View
            {
                Title = "_OpenAI API",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            _openAiTabView.SetScheme(Theme.CreateScheme());

            var urlLabel = new Label { Text = "Server Base URL (http:// or https://):", X = 1, Y = 1 };
            urlLabel.SetScheme(Theme.CreateScheme());

            _lmStudioBaseUrl = new TextField
            {
                X = 1,
                Y = 2,
                Width = 65,
                Text = _draft.LmStudioBaseUrl,
            };
            _lmStudioBaseUrl.SetScheme(Theme.CreateScheme());

            var presetDropdownLabel = new Label { Text = "Presets (choose to auto-populate endpoint URL):", X = 1, Y = 4 };
            presetDropdownLabel.SetScheme(Theme.CreateScheme());

            var presetDisplayList = PresetChoices.Select(p => p.Display).ToList();
            _openAiPresetDropDown = new DropDownList
            {
                X = 1,
                Y = 5,
                Width = 65,
                ReadOnly = true,
                Source = new ListWrapper<string>(new ObservableCollection<string>(presetDisplayList)),
            };
            _openAiPresetDropDown.SetScheme(Theme.CreateScheme());

            var apiKeyLabel = new Label { Text = "API Key (optional depending on vendor configuration):", X = 1, Y = 7 };
            apiKeyLabel.SetScheme(Theme.CreateScheme());

            _lmStudioApiKey = new TextField
            {
                X = 1,
                Y = 8,
                Width = 65,
                Text = _draft.LmStudioApiKey,
                Secret = true,
            };
            _lmStudioApiKey.SetScheme(Theme.CreateScheme());

            var modelLabel = new Label { Text = "Model Name / ID (or probe with button below):", X = 1, Y = 10 };
            modelLabel.SetScheme(Theme.CreateScheme());

            _lmStudioModel = new TextField
            {
                X = 1,
                Y = 11,
                Width = 65,
                Text = _draft.LmStudioModel,
            };
            _lmStudioModel.SetScheme(Theme.CreateScheme());

            _lmStudioModel.TextChanged += (_, _) =>
            {
                _draft.LmStudioModel = _lmStudioModel.Text?.Trim() ?? string.Empty;
                UpdateEngineSummary();
            };

            // Determine initial preset display
            var initialPreset = PresetChoices.FirstOrDefault(p => string.Equals(p.Name, _draft.OpenAiPreset, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(initialPreset.Name))
            {
                initialPreset = PresetChoices.FirstOrDefault(p => string.Equals(p.Url.TrimEnd('/'), _draft.LmStudioBaseUrl.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
            }
            if (string.IsNullOrEmpty(initialPreset.Name))
            {
                initialPreset = PresetChoices.First(p => p.Name == "Custom");
            }
            _openAiPresetDropDown.Text = initialPreset.Display;

            _openAiPresetDropDown.ValueChanged += (_, _) =>
            {
                ApplyPresetSelection(_openAiPresetDropDown.Text);
            };

            _openAiPresetDropDown.TextChanged += (_, _) =>
            {
                ApplyPresetSelection(_openAiPresetDropDown.Text);
            };

            _lmStudioBaseUrl.TextChanged += (_, _) =>
            {
                if (_isUpdatingPresetFromUrl) return;

                var url = _lmStudioBaseUrl.Text?.Trim() ?? string.Empty;
                _draft.LmStudioBaseUrl = url;

                var matched = PresetChoices.FirstOrDefault(p => p.Name != "Custom" && string.Equals(p.Url.TrimEnd('/'), url.TrimEnd('/'), StringComparison.OrdinalIgnoreCase));
                var targetPreset = !string.IsNullOrEmpty(matched.Name) ? matched : PresetChoices.First(p => p.Name == "Custom");

                _draft.OpenAiPreset = targetPreset.Name;
                _isUpdatingPresetFromUrl = true;
                try
                {
                    _openAiPresetDropDown.Text = targetPreset.Display;
                }
                finally
                {
                    _isUpdatingPresetFromUrl = false;
                }

                UpdateEngineSummary();
            };

            _probeButton = new Button
            {
                X = 1,
                Y = 13,
                Text = "Probe Models",
            };
            _probeButton.SetScheme(Theme.CreateScheme());

            _probeStatus = new Label
            {
                X = Pos.Right(_probeButton) + 2,
                Y = 13,
                Width = Dim.Fill() - 2,
                Text = string.Empty,
            };
            _probeStatus.SetScheme(Theme.CreateScheme());

            _lmStudioModelsList = new ListView
            {
                X = 1,
                Y = 15,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill(1),
                Visible = false,
            };
            _lmStudioModelsList.SetScheme(Theme.CreateScheme());

            _lmStudioModelsList.RowRender += (_, e) =>
            {
                var isPicked = e.Row >= 0 && e.Row < _probedModels.Count
                    && string.Equals(_probedModels[e.Row], _draft.LmStudioModel, StringComparison.OrdinalIgnoreCase);
                var isSelected = e.Row == _lmStudioModelsList.SelectedItem;

                if (isSelected)
                {
                    e.RowAttribute = isPicked ? PickedAndSelectedAttr : SelectedAttr;
                }
                else
                {
                    e.RowAttribute = isPicked ? PickedAttr : NormalAttr;
                }
            };

            _lmStudioModelsList.Accepting += (_, _) =>
            {
                var selected = _lmStudioModelsList.SelectedItem ?? -1;
                if (selected >= 0 && selected < _probedModels.Count)
                {
                    var modelName = _probedModels[selected];
                    _lmStudioModel.Text = modelName;
                    _draft.LmStudioModel = modelName;
                    _probeStatus.Text = $"Picked: {modelName}";
                    _lmStudioModelsList.SetNeedsDraw();
                    UpdateEngineSummary();
                }
            };

            _lmStudioModelsList.ValueChanged += (_, _) => _lmStudioModelsList.SetNeedsDraw();

            _probeButton.Accepting += async (_, _) =>
            {
                await ProbeLmStudioModelsAsync();
            };

            _openAiTabView.Add(
                urlLabel,
                _lmStudioBaseUrl,
                presetDropdownLabel,
                _openAiPresetDropDown,
                apiKeyLabel,
                _lmStudioApiKey,
                modelLabel,
                _lmStudioModel,
                _probeButton,
                _probeStatus,
                _lmStudioModelsList);

            // ==========================================
            // 4. MEMORY TAB
            // ==========================================
            _memoryTabView = new View
            {
                Title = "_Memory",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            _memoryTabView.SetScheme(Theme.CreateScheme());

            var recallLabel = new Label { Text = "Transcript Recall Characters:", X = 1, Y = 1 };
            recallLabel.SetScheme(Theme.CreateScheme());

            _recallChars = new TextField
            {
                X = 1,
                Y = 2,
                Width = 15,
                Text = _draft.TranscriptRecallCharacters.ToString(),
            };
            _recallChars.SetScheme(Theme.CreateScheme());

            var recallDesc = new Label
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill() - 2,
                Text = $"Sets how much text from the previous session is re-read when continuing a save.\nBoundaries: {TranscriptRecall.MinCharacters} to {TranscriptRecall.MaxCharacters} characters.\nDefault: {TranscriptRecall.DefaultCharacters} characters.",
            };
            recallDesc.SetScheme(Theme.CreateScheme());

            _memoryTabView.Add(recallLabel, _recallChars, recallDesc);

            // ==========================================
            // 5. EDITOR TAB
            // ==========================================
            _editorTabView = new View
            {
                Title = "E_ditor",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            _editorTabView.SetScheme(Theme.CreateScheme());

            var editorLabel = new Label { Text = "External Editor Command (for Ctrl+G):", X = 1, Y = 1 };
            editorLabel.SetScheme(Theme.CreateScheme());

            _editorCommand = new TextField
            {
                X = 1,
                Y = 2,
                Width = 50,
                Text = _draft.EditorCommand,
            };
            _editorCommand.SetScheme(Theme.CreateScheme());

            _testEditorButton = new Button
            {
                X = 1,
                Y = 4,
                Text = "Test Editor",
            };
            _testEditorButton.SetScheme(Theme.CreateScheme());

            _openConfigFolderButton = new Button
            {
                X = Pos.Right(_testEditorButton) + 2,
                Y = 4,
                Text = "Open Config Folder",
            };
            _openConfigFolderButton.SetScheme(Theme.CreateScheme());

            _editorStatus = new Label
            {
                X = 1,
                Y = 6,
                Width = Dim.Fill() - 2,
                Text = string.Empty,
            };
            _editorStatus.SetScheme(Theme.CreateScheme());

            _testEditorButton.Accepting += (_, _) =>
            {
                var cmd = _editorCommand.Text?.Trim() ?? string.Empty;
                if (EditorCommandLine.TryParse(cmd, out var parsed, out var reason))
                {
                    _editorStatus.Text = $"Editor found: {parsed.Display}";
                }
                else
                {
                    _editorStatus.Text = $"Editor error: {reason}";
                }
            };

            _openConfigFolderButton.Accepting += (_, _) =>
            {
                var dir = PathProvider.Root;
                Directory.CreateDirectory(dir);
                if (!FileExplorer.TryOpen(dir, out var reason))
                {
                    _editorStatus.Text = reason ?? "Could not open folder.";
                }
            };

            _editorTabView.Add(editorLabel, _editorCommand, _testEditorButton, _openConfigFolderButton, _editorStatus);

            // Add all top-level tabs: [Engine, Claude Code, OpenAI API, Memory, Editor]
            _tabs.Add(_engineTabView, _claudeTabView, _openAiTabView, _memoryTabView, _editorTabView);
            _tabs.Value = _engineTabView;

            _tabs.ValueChanged += (_, e) =>
            {
                UpdateStatusForTab(e.NewValue);
            };

            // Bottom action buttons
            _saveButton = new Button
            {
                X = 1,
                Y = Pos.Bottom(_statusLabel),
                Text = "Save (Ctrl+S)",
            };
            _saveButton.SetScheme(Theme.CreateScheme());
            _saveButton.Accepting += (_, _) => SaveAndClose();

            _cancelButton = new Button
            {
                X = Pos.Right(_saveButton) + 2,
                Y = Pos.Bottom(_statusLabel),
                Text = "Cancel (Esc)",
            };
            _cancelButton.SetScheme(Theme.CreateScheme());
            _cancelButton.Accepting += (_, _) => CancelAndClose();

            _defaultsButton = new Button
            {
                X = Pos.Right(_cancelButton) + 2,
                Y = Pos.Bottom(_statusLabel),
                Text = "Restore Defaults",
            };
            _defaultsButton.SetScheme(Theme.CreateScheme());
            _defaultsButton.Accepting += (_, _) => RestoreDefaults();

            Add(_tabs, _statusLabel, _saveButton, _cancelButton, _defaultsButton);

            UpdateEngineSummary();
        }

        public AppSettings? Chosen { get; private set; }

        public ExternalEditor? Editor { get; init; }

        public Tabs TabsControl => _tabs;

        public View EngineTabView => _engineTabView;

        public View ClaudeTabView => _claudeTabView;

        public View OpenAiTabView => _openAiTabView;

        public View MemoryTabView => _memoryTabView;

        public View EditorTabView => _editorTabView;

        public View? ActiveTab => _tabs.Value ?? _engineTabView;

        public event Action? Done;

        public event Action? Cancelled;

        public void SwitchToTab(View tabView)
        {
            _tabs.Value = tabView;
            tabView.SetFocus();
            UpdateStatusForTab(tabView);
            SetNeedsDraw();
        }

        private void UpdateStatusForTab(View? tab)
        {
            if (tab == _engineTabView)
            {
                _statusLabel.Text = "Left/Right: Switch Tabs | Up/Down: Select Provider | Enter: Pick Provider | Ctrl+S: Save | Esc: Cancel";
            }
            else if (tab == _claudeTabView)
            {
                _statusLabel.Text = "Left/Right: Switch Tabs | Up/Down: Select Model | Enter: Pick Model | Tab: Next Field | Ctrl+S: Save | Esc: Cancel";
            }
            else if (tab == _openAiTabView)
            {
                _statusLabel.Text = "Left/Right: Switch Tabs | Tab: Next Field | Ctrl+S: Save | Esc: Cancel";
            }
            else if (tab == _memoryTabView)
            {
                _statusLabel.Text = "Left/Right: Switch Tabs | Tab: Next Field | Ctrl+S: Save | Esc: Cancel";
            }
            else if (tab == _editorTabView)
            {
                _statusLabel.Text = "Left/Right: Switch Tabs | Tab: Next Field | Ctrl+S: Save | Esc: Cancel";
            }
            else
            {
                _statusLabel.Text = "Left/Right: Switch Tabs | Down: Enter Tab | Tab: Next Field | Ctrl+S: Save | Esc: Cancel";
            }
        }

        private void UpdateEngineSummary()
        {
            var claudeDesc = string.IsNullOrEmpty(_draft.ClaudeModel)
                ? "CLI Default"
                : ClaudeModels.Describe(_draft.ClaudeModel);

            var openAiDesc = string.IsNullOrEmpty(_draft.LmStudioModel)
                ? $"{_draft.OpenAiPreset} (Default loaded model)"
                : $"{_draft.OpenAiPreset} ({_draft.LmStudioModel})";

            if (_draft.Provider == AgentProvider.ClaudeCode)
            {
                _engineSummaryLabel.Text = $"Current Configuration: Claude Code [{claudeDesc}]\nOpenAI API standby: [{openAiDesc}]";
            }
            else
            {
                _engineSummaryLabel.Text = $"Current Configuration: OpenAI API [{openAiDesc}]\nClaude Code standby: [{claudeDesc}]";
            }
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

            return base.OnKeyDown(key);
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
                _probeStatus.Text = "Enter a valid server URL before probing.";
                return;
            }

            var baseUrl = AppSettings.NormalizeBaseUrl(rawUrl);

            _probe?.Cancel();
            _probe?.Dispose();
            _probe = new CancellationTokenSource(ProbeTimeout);

            _probeButton.Enabled = false;
            _probeStatus.Text = "Connecting to API endpoint...";

            try
            {
                var models = await LmStudioModels.ListAsync(baseUrl, _lmStudioApiKey.Text?.Trim(), ProbeTimeout, _probe.Token);

                _app.Invoke(() =>
                {
                    if (models.Count == 0)
                    {
                        _probeStatus.Text = "Connected, but no models found.";
                        _lmStudioModelsList.Visible = false;
                        _probedModels.Clear();
                    }
                    else
                    {
                        var sortedModels = models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
                        _probeStatus.Text = $"Found {sortedModels.Count} model(s). Select with Up/Down, Enter to pick:";
                        _probedModels.Clear();
                        _probedModels.AddRange(sortedModels);
                        _lmStudioModelsList.SetSource(new ObservableCollection<string>(sortedModels));
                        _lmStudioModelsList.Visible = true;
                        _lmStudioModelsList.Height = Dim.Fill(1);
                        _lmStudioModelsList.SetFocus();
                    }
                });
            }
            catch (Exception ex)
            {
                _app.Invoke(() =>
                {
                    var msg = ex is AgentException ? ex.Message : ex.Message;
                    var firstLine = msg.IndexOf('\n') > 0 ? msg[..msg.IndexOf('\n')] : msg;
                    _probeStatus.Text = $"Probe failed: {firstLine}";
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

            var defaultPreset = PresetChoices.First(p => p.Name == "Custom");
            _isUpdatingPresetFromUrl = true;
            try
            {
                _openAiPresetDropDown.Text = defaultPreset.Display;
            }
            finally
            {
                _isUpdatingPresetFromUrl = false;
            }

            _lmStudioBaseUrl.Text = defaults.LmStudioBaseUrl;
            _lmStudioApiKey.Text = defaults.LmStudioApiKey;
            _lmStudioModel.Text = defaults.LmStudioModel;
            _probedModels.Clear();
            _lmStudioModelsList.Visible = false;
            _probeStatus.Text = string.Empty;

            _recallChars.Text = defaults.TranscriptRecallCharacters.ToString();
            _editorCommand.Text = defaults.EditorCommand;

            UpdateEngineSummary();
            _statusLabel.Text = "Restored all settings to defaults. Press Ctrl+S to save.";
            SetNeedsDraw();
        }

        private void ApplyPresetSelection(string? presetNameOrDisplay)
        {
            if (_isUpdatingPresetFromUrl) return;

            var current = presetNameOrDisplay?.Trim() ?? string.Empty;
            var matched = PresetChoices.FirstOrDefault(p =>
                string.Equals(p.Display, current, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, current, StringComparison.OrdinalIgnoreCase));

            if (string.IsNullOrEmpty(matched.Name))
            {
                return;
            }

            _draft.OpenAiPreset = matched.Name;
            _draft.LmStudioBaseUrl = matched.Url;
            _isUpdatingPresetFromUrl = true;
            try
            {
                _lmStudioBaseUrl.Text = matched.Url;
                _openAiPresetDropDown.Text = matched.Display;
            }
            finally
            {
                _isUpdatingPresetFromUrl = false;
            }

            if (!string.IsNullOrEmpty(matched.DefaultModel) &&
                (string.IsNullOrEmpty(_draft.LmStudioModel) || PresetChoices.Any(p => !string.IsNullOrEmpty(p.DefaultModel) && p.DefaultModel == _draft.LmStudioModel)))
            {
                _draft.LmStudioModel = matched.DefaultModel;
                _lmStudioModel.Text = matched.DefaultModel;
            }

            UpdateEngineSummary();
            _statusLabel.Text = $"Selected preset: {matched.Name}";
        }

        private void SaveAndClose()
        {
            // Collect and validate values
            var selectedProvider = _providerList.SelectedItem ?? 0;
            _draft.Provider = selectedProvider == 0 ? AgentProvider.ClaudeCode : AgentProvider.OpenAiApi;

            var claudeCustom = _claudeCustomModel.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(claudeCustom))
            {
                _draft.ClaudeModel = claudeCustom;
            }
            else if (_claudeModelList.SelectedItem is { } selectedClaudeIdx && selectedClaudeIdx >= 0 && selectedClaudeIdx < ClaudeModels.All.Length)
            {
                _draft.ClaudeModel = ClaudeModels.All[selectedClaudeIdx].Id;
            }
            else
            {
                _draft.ClaudeModel = string.Empty;
            }

            var baseUrl = _lmStudioBaseUrl.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(baseUrl) && !AppSettings.IsAddress(baseUrl))
            {
                _statusLabel.Text = "OpenAI API Base URL must be a valid http:// or https:// address.";
                return;
            }
            _draft.LmStudioBaseUrl = AppSettings.NormalizeBaseUrl(baseUrl);
            _draft.LmStudioApiKey = _lmStudioApiKey.Text?.Trim() ?? string.Empty;
            _draft.LmStudioModel = _lmStudioModel.Text?.Trim() ?? string.Empty;

            var matchedPreset = PresetChoices.FirstOrDefault(p =>
                string.Equals(p.Display, _openAiPresetDropDown.Text?.Trim(), StringComparison.OrdinalIgnoreCase) ||
                string.Equals(p.Name, _openAiPresetDropDown.Text?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(matchedPreset.Name))
            {
                _draft.OpenAiPreset = matchedPreset.Name;
            }
            else if (string.IsNullOrWhiteSpace(_draft.OpenAiPreset))
            {
                _draft.OpenAiPreset = OpenAiPresets.DetectPreset(_draft.LmStudioBaseUrl).Name;
            }

            var recallText = _recallChars.Text?.Trim() ?? string.Empty;
            if (!int.TryParse(recallText, out var recall)
                || recall < TranscriptRecall.MinCharacters
                || recall > TranscriptRecall.MaxCharacters)
            {
                _statusLabel.Text = $"Transcript Recall must be an integer between {TranscriptRecall.MinCharacters} and {TranscriptRecall.MaxCharacters}.";
                return;
            }
            _draft.TranscriptRecallCharacters = recall;

            var editorCmd = _editorCommand.Text?.Trim() ?? string.Empty;
            _draft.EditorCommand = editorCmd;

            // Commit and save to disk
            try
            {
                SettingsStore.Write(_draft, _settingsPath);
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Could not save settings file: {ex.Message}";
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
