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
    /// submenus under the Engine tab for provider-specific settings, and
    /// <see cref="ListView"/> for list selection with explicit cursor selection vs committed picking.
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
        private readonly View _memoryTabView;
        private readonly View _editorTabView;

        // Engine tab root views
        private readonly View _providerMainView;
        private readonly View _claudeSubmenuView;
        private readonly View _openAiSubmenuView;
        private AgentProvider? _currentSubmenu;

        // Provider list controls
        private readonly ListView _providerList;

        // Claude submenu controls
        private readonly ListView _claudeModelList;
        private readonly TextField _claudeCustomModel;

        // OpenAI API submenu controls
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

        // Action buttons
        private readonly Button _saveButton;
        private readonly Button _cancelButton;
        private readonly Button _defaultsButton;

        public SettingsWindow(IApplication app, AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(settings);

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
                CanFocus = false,
            };
            _tabs.SetScheme(Theme.CreateScheme());

            // Remove all key bindings from Tabs so arrow keys never navigate tabs
            _tabs.KeyBindings.Clear();

            _statusLabel = new Label
            {
                X = 1,
                Y = Pos.Bottom(_tabs),
                Width = Dim.Fill() - 2,
                Height = 1,
                Text = "Right Arrow: Submenu | Up/Down: Navigate | Enter: Select Provider | Tab: Next Tab | Ctrl+S: Save | Esc: Cancel",
            };
            _statusLabel.SetScheme(Theme.CreateScheme());

            // ==========================================
            // 1. ENGINE TAB
            // ==========================================
            _engineTabView = new View
            {
                Title = "Engine",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            _engineTabView.SetScheme(Theme.CreateScheme());

            // 1a. Provider Main View (Engine home)
            _providerMainView = new View
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                Visible = true,
            };
            _providerMainView.SetScheme(Theme.CreateScheme());

            var providerLabel = new Label
            {
                Text = "Select active narrative provider (Enter to select, Right Arrow to configure):",
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
                _statusLabel.Text = $"Picked provider: {(_draft.Provider == AgentProvider.ClaudeCode ? "Claude Code" : "OpenAI API")}";
            };

            _providerList.ValueChanged += (_, _) =>
            {
                _providerList.SetNeedsDraw();
            };

            _providerList.KeyDown += (_, key) =>
            {
                if (key == Key.CursorUp)
                {
                    var current = _providerList.SelectedItem ?? 0;
                    if (current > 0)
                    {
                        _providerList.SelectedItem = current - 1;
                    }
                    key.Handled = true;
                }
                else if (key == Key.CursorDown)
                {
                    var current = _providerList.SelectedItem ?? 0;
                    var max = (_providerList.Source?.Count ?? 1) - 1;
                    if (current < max)
                    {
                        _providerList.SelectedItem = current + 1;
                    }
                    key.Handled = true;
                }
                else if (key == Key.CursorRight)
                {
                    var selected = _providerList.SelectedItem ?? 0;
                    EnterSubmenu(selected == 0 ? AgentProvider.ClaudeCode : AgentProvider.OpenAiApi);
                    key.Handled = true;
                }
                else if (key == Key.CursorLeft)
                {
                    key.Handled = true;
                }
                else if (key == Key.Tab)
                {
                    SwitchToTab(_memoryTabView!);
                    key.Handled = true;
                }
                else if (key == Key.Tab.WithShift)
                {
                    _defaultsButton?.SetFocus();
                    key.Handled = true;
                }
            };

            var providerDesc = new Label
            {
                X = 1,
                Y = 7,
                Width = Dim.Fill() - 2,
                Text = "Claude Code requires the claude CLI to be authenticated on your PATH.\nOpenAI API connects over HTTP to Google AI Studio, OpenAI, Anthropic, LM Studio, Ollama, etc.\nPress Enter to select provider. Press Right Arrow (→) to enter submenu settings.",
            };
            providerDesc.SetScheme(Theme.CreateScheme());

            _providerMainView.Add(providerLabel, _providerList, providerDesc);

            // 1b. Claude Code Submenu View
            _claudeSubmenuView = new View
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                Visible = false,
            };
            _claudeSubmenuView.SetScheme(Theme.CreateScheme());

            var claudeHeader = new Label
            {
                Text = "← Back (Left Arrow / Esc) | Claude Code Settings",
                X = 1,
                Y = 0,
            };
            claudeHeader.SetScheme(Theme.CreateScheme());

            var claudeModelLabel = new Label
            {
                Text = "Choose a preset Claude model (Up/Down to select, Enter to pick):",
                X = 1,
                Y = 2,
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
                Width = 45,
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
                    _statusLabel.Text = $"Picked model: {ClaudeModels.All[selected].Name}";
                }
            };

            _claudeModelList.ValueChanged += (_, _) => _claudeModelList.SetNeedsDraw();

            _claudeModelList.KeyDown += (_, key) =>
            {
                if (key == Key.CursorUp)
                {
                    var current = _claudeModelList.SelectedItem ?? 0;
                    if (current > 0)
                    {
                        _claudeModelList.SelectedItem = current - 1;
                    }
                    key.Handled = true;
                }
                else if (key == Key.CursorDown)
                {
                    var current = _claudeModelList.SelectedItem ?? 0;
                    var max = ClaudeModels.All.Length - 1;
                    if (current < max)
                    {
                        _claudeModelList.SelectedItem = current + 1;
                    }
                    key.Handled = true;
                }
            };

            _claudeSubmenuView.Add(claudeHeader, claudeModelLabel, _claudeModelList, customModelLabel, _claudeCustomModel);

            // 1c. OpenAI API Submenu View
            _openAiSubmenuView = new View
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                Visible = false,
            };
            _openAiSubmenuView.SetScheme(Theme.CreateScheme());

            var openAiHeader = new Label
            {
                Text = "← Back (Left Arrow / Esc) | OpenAI API Settings",
                X = 1,
                Y = 0,
            };
            openAiHeader.SetScheme(Theme.CreateScheme());

            var urlLabel = new Label { Text = "Server Base URL:", X = 1, Y = 2 };
            urlLabel.SetScheme(Theme.CreateScheme());

            _lmStudioBaseUrl = new TextField
            {
                X = 1,
                Y = 3,
                Width = 65,
                Text = _draft.LmStudioBaseUrl,
            };
            _lmStudioBaseUrl.SetScheme(Theme.CreateScheme());

            var presetDropdownLabel = new Label { Text = "Presets (select to apply URL):", X = 1, Y = 5 };
            presetDropdownLabel.SetScheme(Theme.CreateScheme());

            var presetDisplayList = PresetChoices.Select(p => p.Display).ToList();
            _openAiPresetDropDown = new DropDownList
            {
                X = 1,
                Y = 6,
                Width = 65,
                ReadOnly = true,
                Source = new ListWrapper<string>(new ObservableCollection<string>(presetDisplayList)),
            };
            _openAiPresetDropDown.SetScheme(Theme.CreateScheme());

            var apiKeyLabel = new Label { Text = "API Key (optional depending on vendor configuration):", X = 1, Y = 8 };
            apiKeyLabel.SetScheme(Theme.CreateScheme());

            _lmStudioApiKey = new TextField
            {
                X = 1,
                Y = 9,
                Width = 65,
                Text = _draft.LmStudioApiKey,
                Secret = true,
            };
            _lmStudioApiKey.SetScheme(Theme.CreateScheme());

            var modelLabel = new Label { Text = "Model Name / ID (or probe with button below):", X = 1, Y = 11 };
            modelLabel.SetScheme(Theme.CreateScheme());

            _lmStudioModel = new TextField
            {
                X = 1,
                Y = 12,
                Width = 65,
                Text = _draft.LmStudioModel,
            };
            _lmStudioModel.SetScheme(Theme.CreateScheme());

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

            _openAiPresetDropDown.TextChanged += (_, _) =>
            {
                if (_isUpdatingPresetFromUrl) return;

                var currentText = _openAiPresetDropDown.Text?.Trim() ?? string.Empty;
                var matched = PresetChoices.FirstOrDefault(p => string.Equals(p.Display, currentText, StringComparison.OrdinalIgnoreCase));
                if (!string.IsNullOrEmpty(matched.Name))
                {
                    _draft.OpenAiPreset = matched.Name;
                    _draft.LmStudioBaseUrl = matched.Url;
                    _isUpdatingPresetFromUrl = true;
                    try
                    {
                        _lmStudioBaseUrl.Text = matched.Url;
                    }
                    finally
                    {
                        _isUpdatingPresetFromUrl = false;
                    }

                    if (!string.IsNullOrEmpty(matched.DefaultModel) && (string.IsNullOrEmpty(_draft.LmStudioModel) || PresetChoices.Any(p => !string.IsNullOrEmpty(p.DefaultModel) && p.DefaultModel == _draft.LmStudioModel)))
                    {
                        _draft.LmStudioModel = matched.DefaultModel;
                        _lmStudioModel.Text = matched.DefaultModel;
                    }

                    _statusLabel.Text = $"Selected preset: {matched.Name}";
                }
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
            };

            _probeButton = new Button
            {
                X = 1,
                Y = 14,
                Text = "Probe Models",
            };
            _probeButton.SetScheme(Theme.CreateScheme());

            _probeStatus = new Label
            {
                X = Pos.Right(_probeButton) + 2,
                Y = 14,
                Width = Dim.Fill() - 2,
                Text = string.Empty,
            };
            _probeStatus.SetScheme(Theme.CreateScheme());

            _lmStudioModelsList = new ListView
            {
                X = 1,
                Y = 16,
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
                }
            };

            _lmStudioModelsList.ValueChanged += (_, _) => _lmStudioModelsList.SetNeedsDraw();

            _lmStudioModelsList.KeyDown += (_, key) =>
            {
                if (key == Key.CursorUp)
                {
                    var current = _lmStudioModelsList.SelectedItem ?? 0;
                    if (current > 0)
                    {
                        _lmStudioModelsList.SelectedItem = current - 1;
                    }
                    key.Handled = true;
                }
                else if (key == Key.CursorDown)
                {
                    var current = _lmStudioModelsList.SelectedItem ?? 0;
                    var count = _lmStudioModelsList.Source?.Count ?? 0;
                    if (count > 0 && current < count - 1)
                    {
                        _lmStudioModelsList.SelectedItem = current + 1;
                    }
                    key.Handled = true;
                }
            };

            _probeButton.Accepting += async (_, _) =>
            {
                await ProbeLmStudioModelsAsync();
            };

            _openAiSubmenuView.Add(
                openAiHeader,
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

            _engineTabView.Add(_providerMainView, _claudeSubmenuView, _openAiSubmenuView);

            // ==========================================
            // 2. MEMORY TAB
            // ==========================================
            _memoryTabView = new View
            {
                Title = "Memory",
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
            _recallChars.KeyDown += (_, key) =>
            {
                if (key == Key.Tab)
                {
                    SwitchToTab(_editorTabView!);
                    key.Handled = true;
                }
                else if (key == Key.Tab.WithShift)
                {
                    SwitchToTab(_engineTabView!);
                    key.Handled = true;
                }
            };

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
            // 3. EDITOR TAB
            // ==========================================
            _editorTabView = new View
            {
                Title = "Editor",
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
                Width = 45,
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
            _openConfigFolderButton.KeyDown += (_, key) =>
            {
                if (key == Key.Tab)
                {
                    _saveButton?.SetFocus();
                    key.Handled = true;
                }
            };

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

            // Add top-level tabs: [Engine, Memory, Editor]
            _tabs.Add(_engineTabView, _memoryTabView, _editorTabView);
            _tabs.Value = _engineTabView;

            // When switching tabs, automatically focus the active tab's primary control
            _tabs.ValueChanged += (_, e) =>
            {
                if (e.NewValue == _engineTabView)
                {
                    ExitSubmenu();
                    _providerList.SetFocus();
                    _statusLabel.Text = "Right Arrow: Submenu | Up/Down: Navigate | Enter: Select Provider | Tab: Next Tab | Ctrl+S: Save | Esc: Cancel";
                }
                else if (e.NewValue == _memoryTabView)
                {
                    ExitSubmenu();
                    _recallChars.SetFocus();
                    _statusLabel.Text = "Tab: Next Tab | Shift+Tab: Prev Tab | Ctrl+S: Save | Esc: Cancel";
                }
                else if (e.NewValue == _editorTabView)
                {
                    ExitSubmenu();
                    _editorCommand.SetFocus();
                    _statusLabel.Text = "Tab: Next Field | Shift+Tab: Prev Field | Ctrl+S: Save | Esc: Cancel";
                }
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
            _defaultsButton.KeyDown += (_, key) =>
            {
                if (key == Key.Tab)
                {
                    SwitchToTab(_engineTabView!);
                    key.Handled = true;
                }
                else if (key == Key.Tab.WithShift)
                {
                    _cancelButton?.SetFocus();
                    key.Handled = true;
                }
            };

            Add(_tabs, _statusLabel, _saveButton, _cancelButton, _defaultsButton);

            Initialized += (_, _) => _providerList.SetFocus();
        }

        public AppSettings? Chosen { get; private set; }

        public ExternalEditor? Editor { get; init; }

        public AgentProvider? CurrentSubmenu => _currentSubmenu;

        public View EngineTabView => _engineTabView;

        public View MemoryTabView => _memoryTabView;

        public View EditorTabView => _editorTabView;

        public View? ActiveTab => _tabs.Value ?? _engineTabView;

        public event Action? Done;

        public event Action? Cancelled;

        public void SwitchToTab(View tabView)
        {
            ExitSubmenu();
            _tabs.Value = tabView;
            if (tabView == _engineTabView)
            {
                _providerList.SetFocus();
                _statusLabel.Text = "Right Arrow: Submenu | Up/Down: Navigate | Enter: Select Provider | Tab: Next Tab | Ctrl+S: Save | Esc: Cancel";
            }
            else if (tabView == _memoryTabView)
            {
                _recallChars.SetFocus();
                _statusLabel.Text = "Tab: Next Tab | Shift+Tab: Prev Tab | Ctrl+S: Save | Esc: Cancel";
            }
            else if (tabView == _editorTabView)
            {
                _editorCommand.SetFocus();
                _statusLabel.Text = "Tab: Next Field | Shift+Tab: Prev Field | Ctrl+S: Save | Esc: Cancel";
            }
            SetNeedsDraw();
        }

        public void EnterSubmenu(AgentProvider provider)
        {
            _currentSubmenu = provider;
            _providerMainView.Visible = false;

            if (provider == AgentProvider.ClaudeCode)
            {
                _claudeSubmenuView.Visible = true;
                _openAiSubmenuView.Visible = false;
                _claudeModelList.SetFocus();
                _statusLabel.Text = "Left Arrow / Esc: Back to Engine | Up/Down: Navigate | Enter: Pick Model | Tab: Next Field | Ctrl+S: Save";
            }
            else
            {
                _claudeSubmenuView.Visible = false;
                _openAiSubmenuView.Visible = true;
                _lmStudioBaseUrl.SetFocus();
                _statusLabel.Text = "Left Arrow / Esc: Back to Engine | Tab: Next Field | Ctrl+S: Save";
            }

            SetNeedsDraw();
        }

        public void ExitSubmenu()
        {
            _currentSubmenu = null;
            _claudeSubmenuView.Visible = false;
            _openAiSubmenuView.Visible = false;
            _providerMainView.Visible = true;
            _providerList.SetFocus();
            _statusLabel.Text = "Right Arrow: Submenu | Up/Down: Navigate | Enter: Select Provider | Tab: Next Tab | Ctrl+S: Save | Esc: Cancel";
            SetNeedsDraw();
        }

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Esc)
            {
                if (_currentSubmenu != null)
                {
                    ExitSubmenu();
                    return true;
                }

                CancelAndClose();
                return true;
            }

            if (key == Key.S.WithCtrl)
            {
                SaveAndClose();
                return true;
            }

            if (_currentSubmenu != null)
            {
                if (key == Key.CursorLeft)
                {
                    if (MostFocused is TextField tf && (tf.InsertionPoint > 0 || tf.SelectedLength > 0))
                    {
                        return base.OnKeyDown(key);
                    }

                    ExitSubmenu();
                    return true;
                }
            }
            else if (_tabs.Value == null || _tabs.Value == _engineTabView)
            {
                if (key == Key.CursorRight)
                {
                    var selected = _providerList.SelectedItem ?? 0;
                    EnterSubmenu(selected == 0 ? AgentProvider.ClaudeCode : AgentProvider.OpenAiApi);
                    return true;
                }
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
            var baseUrl = _lmStudioBaseUrl.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(baseUrl) || !AppSettings.IsAddress(baseUrl))
            {
                _probeStatus.Text = "Enter a valid server URL before probing.";
                return;
            }

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

            _statusLabel.Text = "Restored all settings to defaults. Press Ctrl+S to save.";
        }

        private void SaveAndClose()
        {
            // Collect and validate values
            _draft.ClaudeModel = _claudeCustomModel.Text?.Trim() ?? string.Empty;

            var baseUrl = _lmStudioBaseUrl.Text?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(baseUrl) && !AppSettings.IsAddress(baseUrl))
            {
                _statusLabel.Text = "OpenAI API Base URL must be a valid http:// or https:// address.";
                return;
            }
            _draft.LmStudioBaseUrl = baseUrl;
            _draft.LmStudioApiKey = _lmStudioApiKey.Text?.Trim() ?? string.Empty;
            _draft.LmStudioModel = _lmStudioModel.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(_draft.OpenAiPreset))
            {
                _draft.OpenAiPreset = OpenAiPresets.DetectPreset(baseUrl).Name;
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
                SettingsStore.Write(_draft);
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
