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
    /// Built with Terminal.Gui's built-in <see cref="Tabs"/> for tabbed navigation and
    /// <see cref="ListView"/> for list selection with explicit cursor selection vs committed picking.
    /// </summary>
    internal sealed class SettingsWindow : Window
    {
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

        private static readonly Attribute PickedAndSelectedAttr = new(new Color("#1b5e20"), Color.White);
        private static readonly Attribute PickedAttr = new(new Color("#8fb26a"), Color.None);
        private static readonly Attribute SelectedAttr = new(Color.Black, Color.White);
        private static readonly Attribute NormalAttr = new(new Color("#d7d2c4"), Color.None);

        private readonly IApplication _app;
        private readonly AppSettings _original;
        private readonly AppSettings _draft;

        private readonly Tabs _tabs;
        private readonly Label _statusLabel;

        // Provider tab controls
        private readonly ListView _providerList;

        // Claude tab controls
        private readonly ListView _claudeModelList;
        private readonly TextField _claudeCustomModel;

        // OpenAI API tab controls
        private readonly ListView _openAiPresetList;
        private readonly Label _openAiPresetDesc;
        private readonly TextField _lmStudioBaseUrl;
        private readonly TextField _lmStudioApiKey;
        private readonly TextField _lmStudioModel;
        private readonly ListView _lmStudioModelsList;
        private readonly Button _probeButton;
        private readonly Label _probeStatus;
        private readonly List<string> _probedModels = [];
        private CancellationTokenSource? _probe;

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
            };
            _tabs.SetScheme(Theme.CreateScheme());

            // Remove vertical arrow keys from Tabs so Up/Down navigates lists and controls instead of switching tabs
            _tabs.KeyBindings.Remove(Key.CursorUp);
            _tabs.KeyBindings.Remove(Key.CursorDown);

            _statusLabel = new Label
            {
                X = 1,
                Y = Pos.Bottom(_tabs),
                Width = Dim.Fill() - 2,
                Height = 1,
                Text = "Left/Right: Switch Tab | Up/Down: Select Option | Enter: Pick Option | Tab: Next Field | Ctrl+S: Save | Esc: Cancel",
            };
            _statusLabel.SetScheme(Theme.CreateScheme());

            // 1. Engine / Provider Tab
            var providerView = new View
            {
                Title = "Engine",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            providerView.SetScheme(Theme.CreateScheme());

            var providerLabel = new Label { Text = "Select active narrative provider (Up/Down to select, Enter to pick):", X = 1, Y = 1 };
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
                var selected = _providerList.SelectedItem ?? 0;
                _draft.Provider = selected == 0 ? AgentProvider.ClaudeCode : AgentProvider.OpenAiApi;
                _providerList.SetNeedsDraw();
            };

            var providerDesc = new Label
            {
                X = 1,
                Y = 7,
                Width = Dim.Fill() - 2,
                Text = "Claude Code requires the claude CLI to be authenticated on your PATH.\nOpenAI API connects over HTTP to Google AI Studio, OpenAI, Anthropic, LM Studio, Ollama, etc.\nUse Left/Right to change tabs, Up/Down to select, Enter to pick.",
            };
            providerDesc.SetScheme(Theme.CreateScheme());

            providerView.Add(providerLabel, _providerList, providerDesc);

            // 2. Claude Tab
            var claudeView = new View
            {
                Title = "Claude",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            claudeView.SetScheme(Theme.CreateScheme());

            var claudeModelLabel = new Label { Text = "Choose a preset Claude model (Up/Down to select, Enter to pick):", X = 1, Y = 1 };
            claudeModelLabel.SetScheme(Theme.CreateScheme());

            var modelListLabels = ClaudeModels.All
                .Select(m => string.IsNullOrEmpty(m.Id) ? $"{m.Name} ({m.Detail})" : $"{m.Name} - {m.Id} ({m.Detail})")
                .ToList();

            _claudeModelList = new ListView
            {
                X = 1,
                Y = 2,
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

            var customModelLabel = new Label { Text = "Or custom model identifier:", X = 1, Y = 9 };
            customModelLabel.SetScheme(Theme.CreateScheme());

            _claudeCustomModel = new TextField
            {
                X = 1,
                Y = 10,
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

            claudeView.Add(claudeModelLabel, _claudeModelList, customModelLabel, _claudeCustomModel);

            // 3. OpenAI API Tab
            var openAiView = new View
            {
                Title = "OpenAI API",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            openAiView.SetScheme(Theme.CreateScheme());

            var presetLabel = new Label { Text = "Provider Preset (Up/Down to select, Enter to pick):", X = 1, Y = 1 };
            presetLabel.SetScheme(Theme.CreateScheme());

            var presetNames = OpenAiPresets.All.Select(p => p.Name).ToList();

            _openAiPresetList = new ListView
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 2,
                Height = 4,
            };
            _openAiPresetList.SetScheme(Theme.CreateScheme());
            _openAiPresetList.SetSource(new ObservableCollection<string>(presetNames));

            var currentPreset = OpenAiPresets.FindByName(_draft.OpenAiPreset);
            var initialPresetIdx = Array.IndexOf(OpenAiPresets.All, currentPreset);
            if (initialPresetIdx < 0)
            {
                currentPreset = OpenAiPresets.DetectPreset(_draft.LmStudioBaseUrl);
                initialPresetIdx = Array.IndexOf(OpenAiPresets.All, currentPreset);
            }
            _openAiPresetList.SelectedItem = initialPresetIdx >= 0 ? initialPresetIdx : 3;

            _openAiPresetDesc = new Label
            {
                X = 1,
                Y = 6,
                Width = Dim.Fill() - 2,
                Height = 1,
                Text = currentPreset.Description,
            };
            _openAiPresetDesc.SetScheme(Theme.CreateScheme());

            var urlLabel = new Label { Text = "Server Base URL:", X = 1, Y = 7 };
            urlLabel.SetScheme(Theme.CreateScheme());

            _lmStudioBaseUrl = new TextField
            {
                X = 1,
                Y = 8,
                Width = 55,
                Text = _draft.LmStudioBaseUrl,
            };
            _lmStudioBaseUrl.SetScheme(Theme.CreateScheme());

            var apiKeyLabel = new Label { Text = "API Key (optional for local, required for cloud):", X = 1, Y = 10 };
            apiKeyLabel.SetScheme(Theme.CreateScheme());

            _lmStudioApiKey = new TextField
            {
                X = 1,
                Y = 11,
                Width = 55,
                Text = _draft.LmStudioApiKey,
                Secret = true,
            };
            _lmStudioApiKey.SetScheme(Theme.CreateScheme());

            var modelLabel = new Label { Text = "Model Name / ID (or probe with button below):", X = 1, Y = 13 };
            modelLabel.SetScheme(Theme.CreateScheme());

            _lmStudioModel = new TextField
            {
                X = 1,
                Y = 14,
                Width = 55,
                Text = _draft.LmStudioModel,
            };
            _lmStudioModel.SetScheme(Theme.CreateScheme());

            _probeButton = new Button
            {
                X = 1,
                Y = 16,
                Text = "Probe Models",
            };
            _probeButton.SetScheme(Theme.CreateScheme());

            _probeStatus = new Label
            {
                X = Pos.Right(_probeButton) + 2,
                Y = 16,
                Width = Dim.Fill() - 2,
                Text = string.Empty,
            };
            _probeStatus.SetScheme(Theme.CreateScheme());

            _lmStudioModelsList = new ListView
            {
                X = 1,
                Y = 18,
                Width = Dim.Fill() - 2,
                Height = 4,
                Visible = false,
            };
            _lmStudioModelsList.SetScheme(Theme.CreateScheme());

            _openAiPresetList.RowRender += (_, e) =>
            {
                var isPicked = e.Row >= 0 && e.Row < OpenAiPresets.All.Length
                    && string.Equals(OpenAiPresets.All[e.Row].Name, _draft.OpenAiPreset, StringComparison.OrdinalIgnoreCase);
                var isSelected = e.Row == _openAiPresetList.SelectedItem;

                if (isSelected)
                {
                    e.RowAttribute = isPicked ? PickedAndSelectedAttr : SelectedAttr;
                }
                else
                {
                    e.RowAttribute = isPicked ? PickedAttr : NormalAttr;
                }
            };

            _openAiPresetList.Accepting += (_, _) =>
            {
                var selected = _openAiPresetList.SelectedItem ?? -1;
                if (selected >= 0 && selected < OpenAiPresets.All.Length)
                {
                    var preset = OpenAiPresets.All[selected];
                    _draft.OpenAiPreset = preset.Name;
                    _openAiPresetDesc.Text = preset.Description;

                    if (!preset.IsCustom)
                    {
                        _draft.LmStudioBaseUrl = preset.BaseUrl;
                        _lmStudioBaseUrl.Text = preset.BaseUrl;
                        if (string.IsNullOrEmpty(_draft.LmStudioModel) || OpenAiPresets.All.Any(p => p.DefaultModel == _draft.LmStudioModel))
                        {
                            _draft.LmStudioModel = preset.DefaultModel;
                            _lmStudioModel.Text = preset.DefaultModel;
                        }
                    }
                    else if (OpenAiPresets.All.Any(p => !p.IsCustom && string.Equals(p.BaseUrl, _lmStudioBaseUrl.Text?.Trim(), StringComparison.OrdinalIgnoreCase)))
                    {
                        _draft.LmStudioBaseUrl = preset.BaseUrl;
                        _lmStudioBaseUrl.Text = preset.BaseUrl;
                    }

                    _openAiPresetList.SetNeedsDraw();
                    _statusLabel.Text = $"Picked preset: {preset.Name}";
                }
            };

            _openAiPresetList.ValueChanged += (_, _) =>
            {
                var selected = _openAiPresetList.SelectedItem ?? -1;
                if (selected >= 0 && selected < OpenAiPresets.All.Length)
                {
                    _openAiPresetDesc.Text = OpenAiPresets.All[selected].Description;
                }
                _openAiPresetList.SetNeedsDraw();
            };

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

            _probeButton.Accepting += async (_, _) =>
            {
                await ProbeLmStudioModelsAsync();
            };

            openAiView.Add(
                presetLabel,
                _openAiPresetList,
                _openAiPresetDesc,
                urlLabel,
                _lmStudioBaseUrl,
                apiKeyLabel,
                _lmStudioApiKey,
                modelLabel,
                _lmStudioModel,
                _probeButton,
                _probeStatus,
                _lmStudioModelsList);

            // 4. Memory Tab
            var memoryView = new View
            {
                Title = "Memory",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            memoryView.SetScheme(Theme.CreateScheme());

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

            memoryView.Add(recallLabel, _recallChars, recallDesc);

            // 5. Editor Tab
            var editorView = new View
            {
                Title = "Editor",
                Width = Dim.Fill(),
                Height = Dim.Fill(),
            };
            editorView.SetScheme(Theme.CreateScheme());

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

            editorView.Add(editorLabel, _editorCommand, _testEditorButton, _openConfigFolderButton, _editorStatus);

            // Add tabs to Tabs control
            _tabs.Add(providerView, claudeView, openAiView, memoryView, editorView);

            // When switching tabs, automatically focus the active tab's primary control
            _tabs.ValueChanged += (_, e) =>
            {
                if (e.NewValue == providerView) _providerList.SetFocus();
                else if (e.NewValue == claudeView) _claudeModelList.SetFocus();
                else if (e.NewValue == openAiView) _openAiPresetList.SetFocus();
                else if (e.NewValue == memoryView) _recallChars.SetFocus();
                else if (e.NewValue == editorView) _editorCommand.SetFocus();
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

            Initialized += (_, _) => _providerList.SetFocus();
        }

        public AppSettings? Chosen { get; private set; }

        public ExternalEditor? Editor { get; init; }

        public event Action? Done;

        public event Action? Cancelled;

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
                        _probeStatus.Text = $"Found {models.Count} model(s). Select with Up/Down, Enter to pick:";
                        _probedModels.Clear();
                        _probedModels.AddRange(models);
                        _lmStudioModelsList.SetSource(new ObservableCollection<string>(models));
                        _lmStudioModelsList.Visible = true;
                        _lmStudioModelsList.Height = Math.Clamp(models.Count, 2, 6);
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

            var presetIdx = Array.IndexOf(OpenAiPresets.All, OpenAiPresets.Custom);
            if (presetIdx >= 0)
            {
                _openAiPresetList.SelectedItem = presetIdx;
            }
            _openAiPresetDesc.Text = OpenAiPresets.Custom.Description;
            _openAiPresetList.SetNeedsDraw();

            _lmStudioBaseUrl.Text = defaults.LmStudioBaseUrl;
            _lmStudioApiKey.Text = defaults.LmStudioApiKey;
            _lmStudioModel.Text = defaults.LmStudioModel;

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
