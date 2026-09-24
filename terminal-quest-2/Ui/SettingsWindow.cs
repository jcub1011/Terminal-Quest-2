using System.Collections.ObjectModel;

using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Agents;
using TerminalQuest.Agents.Anthropic;
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

        private static readonly string[] SectionNames = ["Provider", "Preferences"];

        /// <summary>The providers in provider-list order.</summary>
        private static readonly AgentProvider[] ProviderOrder =
        [
            AgentProvider.ClaudeCode,
            AgentProvider.Google,
            AgentProvider.OpenAI,
            AgentProvider.Anthropic,
            AgentProvider.Custom,
        ];

        /// <summary>Fixed heights: inside a scrolling view, Fill would resolve to the viewport
        /// and there would be nothing to scroll to.</summary>
        private const int ClaudeBoxHeight = 5;
        private const int EndpointBoxHeight = 14;

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
        /// A dropdown that offers keys to the window before its own handling, so pane
        /// switching (Tab) and field movement (Left/Right) never get trapped inside.
        /// Up/Down, Space, F4 and Enter stay native: browsing, opening and picking.
        /// </summary>
        private sealed class NavDropDownList : DropDownList
        {
            public Func<Key, bool>? BeforeKey { get; set; }

            /// <summary>Whether the dropdown list is currently open above the form.</summary>
            public Func<bool>? IsPopoverOpen { get; set; }

            /// <summary>
            /// Whether Enter should accept instead of opening: typed text or a browsed row
            /// the draft does not hold yet. A clean box opens its list on Enter.
            /// </summary>
            public Func<bool>? HasUncommitted { get; set; }

            protected override bool OnKeyDown(Key key)
            {
                // Enter begins selecting: open the list when there is nothing new to accept.
                // F4 toggles natively (Space might type in an editable box); the window's
                // Esc guard guarantees the opened list can always be cancelled.
                if (key == Key.Enter && IsPopoverOpen?.Invoke() == false && HasUncommitted?.Invoke() != true)
                {
                    base.OnKeyDown(Key.F4);
                    return true;
                }

                return (BeforeKey?.Invoke(key) ?? false) || base.OnKeyDown(key);
            }
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

        // Section forms (only one visible at a time). The provider section carries two
        // panels at once: the picker on top, the picked provider's settings beneath it.
        private readonly View _providerSection;
        private readonly View _prefsSection;
        private SettingsSection _activeSection = SettingsSection.Provider;
        private View? _lastFormFocus;

        // Provider section controls. The picker is a collapsed dropdown (one row) so
        // the provider list can grow without pushing the settings off the screen.
        private NavDropDownList _providerCombo = null!;
        private readonly ObservableCollection<string> _providerItems = [];

        // Provider settings: one box per provider kind, toggled by the picked provider.
        // Not readonly: built inside BuildProviderSection rather than the constructor body.
        private View _claudeBox = null!;
        private View _endpointBox = null!;
        private View _providerScroll = null!;

        // Claude settings controls: one editable dropdown merging the preset list and a
        // custom id. The parallel entries map each row back to the id stored in the draft.
        private NavDropDownList _claudeCombo = null!;
        private readonly ObservableCollection<string> _claudeItems = [];
        private List<ClaudeModels.Entry> _claudeEntries = [];
        private bool _claudeRefreshed;
        private bool _isLoadingClaude;
        private bool _claudeTextTyped;
        private string? _pendingClaudeId;

        // Endpoint settings controls (one set of fields, rebound to each provider's slot).
        // The model control is a single editable dropdown: typing sets the id, opening it
        // probes the server and offers what answers.
        private Label _builtinUrlLabel = null!;
        private NavTextField _endpointBaseUrl = null!;
        private Label _endpointMatchHint = null!;
        private Label _endpointVendorHint = null!;
        private Label _apiKeyLabel = null!;
        private NavTextField _endpointApiKey = null!;
        private NavDropDownList _endpointModelCombo = null!;
        private readonly ObservableCollection<string> _endpointModelItems = [];
        private string _endpointAcceptedText = string.Empty;
        private Label _probeStatus = null!;
        private readonly List<string> _probedModels = [];
        private string? _lastProbeKey;
        private bool _isProbing;
        private CancellationTokenSource? _probe;
        private bool _isLoadingEndpoint;

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
            // provider stands out bright; the endpoint detail beneath recedes into help grey.
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
            _prefsSection = BuildPrefsSection();
            _formFrame.Add(_providerSection, _prefsSection);

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
                control.HasFocusChanged += (_, _) =>
                {
                    UpdatePaneChrome();
                    EnsureControlVisible(control);
                };
            }
            foreach (var button in _actionButtons)
            {
                button.HasFocusChanged += (_, _) => UpdatePaneChrome();
            }

            RefreshProviderSettings();
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

        public DropDownList ProviderCombo => _providerCombo;

        internal IReadOnlyList<string> ProviderNames => _providerItems;

        public DropDownList ClaudeModelCombo => _claudeCombo;

        internal IReadOnlyList<string> ClaudeLabels => _claudeItems;

        /// <summary>
        /// The endpoint model field: an editable dropdown whose list is the last probe.
        /// </summary>
        public DropDownList ModelField => _endpointModelCombo;

        /// <summary>The ids the last probe returned, in the order they are offered.</summary>
        public IReadOnlyList<string> ProbedModels => _probedModels;

        public TextField BaseUrlField => _endpointBaseUrl;

        public TextField ApiKeyField => _endpointApiKey;

        public Label BuiltinUrlLabel => _builtinUrlLabel;

        public TextField RecallField => _recallChars;

        public SettingsSection ActiveSection => _activeSection;

        /// <summary>Whether the Claude model controls are the ones showing in Provider Settings.</summary>
        public bool IsClaudeSettingsVisible => _claudeBox.Visible;

        /// <summary>Whether the endpoint controls are the ones showing in Provider Settings.</summary>
        public bool IsEndpointSettingsVisible => _endpointBox.Visible;

        /// <summary>The continuous scrolling provider page: picker above, settings below.</summary>
        public View ProviderScroll => _providerScroll;

        public event Action? Done;

        public event Action? Cancelled;

        public void SwitchToSection(SettingsSection section)
        {
            _sectionsList.SelectedItem = (int)section;
            ShowSection(section);
        }

        private static string ProviderDisplayName(AgentProvider provider) => provider switch
        {
            AgentProvider.ClaudeCode => "Claude Code (local CLI - Anthropic)",
            AgentProvider.Google => "Google (Gemini - OpenAI-compatible API)",
            AgentProvider.OpenAI => "OpenAI (GPT - API)",
            AgentProvider.Anthropic => "Anthropic (Claude - OpenAI-compatible API)",
            _ => "Custom (manual endpoint - OpenAI-compatible API only)",
        };

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
                Text = "Narrative Provider (Up/Down: browse, Enter: open/pick, Esc: close):",
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            providerLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            // A collapsed dropdown: one row however long the provider list grows. Browsing
            // (ValueChanged) only moves the cursor; Enter (Accepting) picks into the draft.
            _providerCombo = new NavDropDownList
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = 1,
                ReadOnly = true,
            };
            _providerCombo.SetScheme(Theme.CreateScheme());
            foreach (var name in ProviderOrder.Select(ProviderDisplayName))
            {
                _providerItems.Add(name);
            }
            _providerCombo.Source = new ListWrapper<string>(_providerItems);
            _providerCombo.BeforeKey = FormListKey;
            _providerCombo.IsPopoverOpen = IsSelecting;
            // Dirty means browsed-but-unpicked: Enter picks it. Clean means Enter opens.
            _providerCombo.HasUncommitted = () =>
                _providerItems.IndexOf(_providerCombo.Value ?? string.Empty)
                    != Array.IndexOf(ProviderOrder, AppSettings.EffectiveProvider(_draft.Provider));
            SyncProviderComboToDraft();
            _providerCombo.ValueChanged += (_, _) => _providerCombo.SetNeedsDraw();
            _providerCombo.Accepting += (_, _) => PickProviderFromCombo();
            // A pick made inside the open list commits the same way; it never advances.
            _providerCombo.Accepted += (_, _) => PickProviderFromCombo();
            _providerCombo.HasFocusChanged += (_, _) =>
            {
                // Abandoning a browsed-but-unpicked row restores the picked provider's name,
                // so the box never disagrees with the panels beneath it.
                if (!_providerCombo.HasFocus)
                {
                    SyncProviderComboToDraft();
                }
            };

            // One continuous scrollable page: the picker on top, the picked provider's
            // settings below it, with a blank row and a header between them. No sub-panels:
            // everything scrolls together when the window is too short to fit it all.
            var settingsHeader = new Label
            {
                Text = "Provider Settings",
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            settingsHeader.SetScheme(Theme.LabelScheme(TextRole.Item));

            _providerScroll = MakeScrollable();
            _claudeBox = BuildClaudeBox();
            _endpointBox = BuildEndpointBox();
            _providerScroll.Add(providerLabel, _providerCombo, settingsHeader, _claudeBox, _endpointBox);
            UpdateProviderScrollHeight();

            section.Add(_providerScroll);
            return section;
        }

        /// <summary>
        /// Whether a dropdown list is currently open above the form. Defensive about a
        /// non-running host: headless tests never open popovers and read this as closed.
        /// </summary>
        private bool IsSelecting() =>
            _app.Popovers is { } popovers && popovers.GetActivePopover() is not null;

        /// <summary>
        /// Shows the picked provider's name in the dropdown without touching the draft.
        /// Browsing is display-only; only <see cref="PickProvider"/> writes the draft.
        /// </summary>
        private void SyncProviderComboToDraft()
        {
            var name = ProviderDisplayName(AppSettings.EffectiveProvider(_draft.Provider));
            if (!string.Equals(_providerCombo.Value, name, StringComparison.Ordinal))
            {
                _providerCombo.Value = name;
            }
        }

        /// <summary>Maps the dropdown's picked display name back to its provider.</summary>
        private void PickProviderFromCombo() =>
            PickProvider(_providerItems.IndexOf(_providerCombo.Value ?? string.Empty));

        /// <summary>
        /// Picks the highlighted provider into the draft on Enter, then shows its settings.
        /// </summary>
        private void PickProvider(int selected)
        {
            if (selected < 0 || selected >= ProviderOrder.Length)
            {
                return;
            }

            // The fields belong to the old provider until now: flush them into its slot
            // before the rebind, or a half-typed URL would follow the player across.
            FlushEndpointControls();
            _draft.Provider = ProviderOrder[selected];
            SyncProviderComboToDraft();
            RefreshProviderSettings();
            UpdateSummary();
            var pickedName = _draft.Provider == AgentProvider.ClaudeCode
                ? "Claude Code"
                : OpenAiPresets.ForProvider(AppSettings.EffectiveProvider(_draft.Provider)).Name;
            Say(_messageLabel, $"Active provider set to: {pickedName}", TextRole.Place);
        }

        /// <summary>
        /// A plain view whose content scrolls vertically once it outgrows the space.
        /// Focusable so the fields inside can take focus: a non-focusable container
        /// swallows SetFocus for its whole subtree. Pane movement stays key-driven,
        /// so this never changes which pane Tab walks.
        /// </summary>
        private static View MakeScrollable()
        {
            var scroll = new View
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = true,
            };
            scroll.SetScheme(Theme.CreateScheme());

            // Both are needed: the flag draws the bar, Auto shows it only on overflow,
            // and the content height below is what makes overflow detectable at all.
            // Without SetContentHeight the viewport always "fits" and nothing scrolls.
            scroll.ViewportSettings |= ViewportSettingsFlags.HasVerticalScrollBar;
            scroll.VerticalScrollBar.VisibilityMode = ScrollBarVisibilityMode.Auto;
            return scroll;
        }

        /// <summary>
        /// Tells the provider page how tall its content is, so the scrollbar appears exactly
        /// when the window is too short. Called whenever the visible box changes: the boxes
        /// have fixed heights, so this is arithmetic, not layout.
        /// </summary>
        private void UpdateProviderScrollHeight()
        {
            if (_providerScroll is null || _claudeBox is null || _endpointBox is null)
            {
                return;
            }

            var boxHeight = _claudeBox.Visible ? _claudeBox.Frame.Height : _endpointBox.Frame.Height;
            if (boxHeight <= 0)
            {
                boxHeight = _claudeBox.Visible ? ClaudeBoxHeight : EndpointBoxHeight;
            }

            // Boxes sit at Y=4 inside the scroll view; one spare row beneath.
            _providerScroll.SetContentHeight(4 + boxHeight + 1);
        }

        /// <summary>
        /// Scrolls a panel just far enough to show the newly focused control. Wired to
        /// focus changes because panel content is taller than the panel.
        /// </summary>
        private static void EnsureVisible(View scroll, View control)
        {
            var y = 0;
            for (var current = control; current is not null && current != scroll; current = current.SuperView)
            {
                y += current.Frame.Y;
            }

            var top = scroll.Viewport.Y;
            var visibleHeight = Math.Max(1, scroll.Viewport.Height);
            if (y < top)
            {
                scroll.ScrollVertical(y - top);
            }
            else if (y + 1 > top + visibleHeight)
            {
                scroll.ScrollVertical(y + 1 - (top + visibleHeight));
            }
        }

        private void EnsureControlVisible(View control)
        {
            if (!control.HasFocus)
            {
                return;
            }

            for (var current = (View?)control; current is not null; current = current.SuperView)
            {
                if (current == _providerScroll)
                {
                    EnsureVisible(current, control);
                    return;
                }
            }
        }

        private View BuildClaudeBox()
        {
            var box = new View
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill() - 2,
                Height = ClaudeBoxHeight,
                CanFocus = true,
                Visible = false,
            };
            box.SetScheme(Theme.CreateScheme());

            var claudeModelLabel = new Label
            {
                Text = "Preset Claude Models (Up/Down: browse, Enter: open/pick, Esc: close):",
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            claudeModelLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            // One editable dropdown merges the preset list and the custom id: picking a row
            // offers its id, typing sets one freehand. Live ids merge in on first focus.
            _claudeEntries = [.. ClaudeModels.All];
            _claudeCombo = new NavDropDownList
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = 1,
            };
            _claudeCombo.SetScheme(Theme.CreateScheme());
            _claudeCombo.Source = new ListWrapper<string>(_claudeItems);
            RefreshClaudeItems();
            _claudeCombo.BeforeKey = FormListKey;
            _claudeCombo.IsPopoverOpen = IsSelecting;
            // Dirty means typed text or a browsed row the draft does not hold yet.
            _claudeCombo.HasUncommitted = () =>
                _claudeTextTyped
                || (_pendingClaudeId is { } pending
                    && !string.Equals(pending, _draft.ClaudeModel?.Trim() ?? string.Empty, StringComparison.OrdinalIgnoreCase));

            _isLoadingClaude = true;
            try
            {
                // Show the stored id as its row label where known, raw otherwise.
                _claudeCombo.Text = ClaudeDisplayForId(_draft.ClaudeModel);
                SyncClaudeSelection();
            }
            finally
            {
                _isLoadingClaude = false;
            }

            // Value and Text are coupled: picking a row shows its label, typing shows free
            // text. Neither writes the draft here. A row pick arms the pending id for Enter;
            // free text updates the draft live; selection echoes are recognised and ignored.
            _claudeCombo.TextChanged += (_, _) =>
            {
                if (_isLoadingClaude)
                {
                    return;
                }

                var typed = _claudeCombo.Text?.Trim() ?? string.Empty;
                var known = ClaudeEntryIndexForText(typed);
                if (known >= 0)
                {
                    _pendingClaudeId = _claudeEntries[known].Id;
                    _claudeTextTyped = false;
                }
                else
                {
                    _draft.ClaudeModel = typed;
                    _pendingClaudeId = null;
                    _claudeTextTyped = true;
                    UpdateSummary();
                }
            };
            _claudeCombo.ValueChanged += (_, _) =>
            {
                if (_isLoadingClaude)
                {
                    return;
                }

                var selected = ClaudeEntryIndexForValue(_claudeCombo.Value);
                _pendingClaudeId = selected >= 0 ? _claudeEntries[selected].Id : null;
                _claudeTextTyped = false;
                _claudeCombo.SetNeedsDraw();
            };
            _claudeCombo.Accepting += (_, _) => CommitClaudePick(advance: true);
            // A pick made inside the open list commits the same way but stays put: the
            // list just closed onto this box, so advancing would yank focus away.
            _claudeCombo.Accepted += (_, _) => CommitClaudePick(advance: false);
            _claudeCombo.HasFocusChanged += (_, _) =>
            {
                if (_claudeCombo.HasFocus)
                {
                    _ = EnsureClaudeFreshAsync();
                }
            };

            var claudeNote = new Label
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 2,
                CanFocus = false,
                Text = "Note: Claude Code requires the 'claude' CLI command to be installed and authenticated on your PATH.",
            };
            claudeNote.SetScheme(Theme.LabelScheme(TextRole.System));

            box.Add(claudeModelLabel, _claudeCombo, claudeNote);
            return box;
        }

        private static string ClaudeLabel(ClaudeModels.Entry entry) =>
            string.IsNullOrEmpty(entry.Id) ? $"{entry.Name} ({entry.Detail})" : $"{entry.Name} - {entry.Id} ({entry.Detail})";

        /// <summary>The row label for a stored id, or the id itself when no row owns it.</summary>
        private string ClaudeDisplayForId(string? id)
        {
            var wanted = id?.Trim() ?? string.Empty;
            foreach (var entry in _claudeEntries)
            {
                if (string.Equals(entry.Id, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return ClaudeLabel(entry);
                }
            }

            return wanted;
        }

        /// <summary>Rebuilds the Claude dropdown rows from the current entries.</summary>
        private void RefreshClaudeItems()
        {
            _claudeItems.Clear();
            foreach (var entry in _claudeEntries)
            {
                _claudeItems.Add(ClaudeLabel(entry));
            }
        }

        /// <summary>Moves the row highlight to the stored id where it is a known row.</summary>
        private void SyncClaudeSelection()
        {
            var wanted = _draft.ClaudeModel?.Trim() ?? string.Empty;
            for (var i = 0; i < _claudeEntries.Count; i++)
            {
                if (string.Equals(_claudeEntries[i].Id, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    var label = ClaudeLabel(_claudeEntries[i]);
                    if (!string.Equals(_claudeCombo.Value, label, StringComparison.Ordinal))
                    {
                        _claudeCombo.Value = label;
                    }

                    return;
                }
            }
        }

        /// <summary>
        /// Maps the dropdown's value back to an entry: the value may be a row label (from a
        /// pick) or a raw id (restored text). -1 when it matches neither.
        /// </summary>
        private int ClaudeEntryIndexForValue(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                for (var i = 0; i < _claudeEntries.Count; i++)
                {
                    if (_claudeEntries[i].Id.Length == 0)
                    {
                        return i;
                    }
                }

                return -1;
            }

            for (var i = 0; i < _claudeEntries.Count; i++)
            {
                if (string.Equals(ClaudeLabel(_claudeEntries[i]), value, StringComparison.Ordinal)
                    || string.Equals(_claudeEntries[i].Id, value, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Maps dropdown text back to an entry: the text may be a row label (from a pick)
        /// or a raw id (restored or typed text). -1 when it matches neither.
        /// </summary>
        private int ClaudeEntryIndexForText(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                for (var i = 0; i < _claudeEntries.Count; i++)
                {
                    if (_claudeEntries[i].Id.Length == 0)
                    {
                        return i;
                    }
                }

                return -1;
            }

            for (var i = 0; i < _claudeEntries.Count; i++)
            {
                if (string.Equals(_claudeEntries[i].Id, text, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(ClaudeLabel(_claudeEntries[i]), text, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Commits the box on Enter: the picked row's id, or the typed text
        /// resolved to a known row where it names one, or raw for a custom id. The box keeps
        /// showing the row label so it agrees with the list.</summary>
        private void CommitClaudePick(bool advance)
        {
            var typed = _claudeCombo.Text?.Trim() ?? string.Empty;
            string id;
            if (!_claudeTextTyped && _pendingClaudeId is { } pending)
            {
                id = pending;
            }
            else if (ClaudeEntryIndexForText(typed) is { } known && known >= 0)
            {
                id = _claudeEntries[known].Id;
            }
            else
            {
                id = typed;
            }

            _pendingClaudeId = null;
            _claudeTextTyped = false;
            _draft.ClaudeModel = id;
            _isLoadingClaude = true;
            try
            {
                _claudeCombo.Text = ClaudeDisplayForId(id);
                SyncClaudeSelection();
            }
            finally
            {
                _isLoadingClaude = false;
            }

            UpdateSummary();
            Say(_messageLabel, $"Picked model: {(string.IsNullOrEmpty(id) ? "CLI Default" : ClaudeModels.Describe(id))}", TextRole.Place);
            if (advance)
            {
                MoveFormFocus(1);
            }
        }

        /// <summary>
        /// Merges ids newer than this build into the Claude dropdown, once per window. Runs
        /// only when a key is available and never blanks the curated list on failure.
        /// </summary>
        private async Task EnsureClaudeFreshAsync()
        {
            if (_claudeRefreshed)
            {
                return;
            }

            _claudeRefreshed = true;

            var apiKey = AppSettings.GetEnvironmentApiKey(AgentProvider.Anthropic)
                ?? _draft.EndpointFor(AgentProvider.Anthropic).ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return;
            }

            IReadOnlyList<string> live;
            try
            {
                live = await AnthropicModels.ListAsync(apiKey, ProbeTimeout).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (live.Count == 0)
            {
                return;
            }

            _app.Invoke(() =>
            {
                var currentId = _pendingClaudeId
                    ?? (ClaudeEntryIndexForText(_claudeCombo.Text?.Trim() ?? string.Empty) is { } known && known >= 0
                        ? _claudeEntries[known].Id
                        : _claudeCombo.Text?.Trim() ?? string.Empty);
                _claudeEntries = [.. ClaudeModels.WithLiveIds(live)];
                RefreshClaudeItems();
                _isLoadingClaude = true;
                try
                {
                    _claudeCombo.Text = ClaudeDisplayForId(currentId);
                    _pendingClaudeId = null;
                    _claudeTextTyped = false;
                    SyncClaudeSelection();
                }
                finally
                {
                    _isLoadingClaude = false;
                }
            });
        }

        private View BuildEndpointBox()
        {
            var box = new View
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill() - 2,
                Height = EndpointBoxHeight,
                CanFocus = true,
                Visible = false,
            };
            box.SetScheme(Theme.CreateScheme());

            _builtinUrlLabel = new Label
            {
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            _builtinUrlLabel.SetScheme(Theme.LabelScheme(TextRole.Normal));

            var urlLabel = new Label
            {
                Text = "Server Base URL (OpenAI-compatible only, http:// or https://):",
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            urlLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            _endpointBaseUrl = new NavTextField
            {
                X = 1,
                Y = 2,
                Width = Dim.Fill() - 2,
            };
            _endpointBaseUrl.SetScheme(Theme.CreateScheme());
            _endpointBaseUrl.BeforeKey = FormFieldKey;

            _endpointMatchHint = new Label
            {
                X = 1,
                Y = 3,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            _endpointMatchHint.SetScheme(Theme.LabelScheme(TextRole.System));

            _endpointVendorHint = new Label
            {
                X = 1,
                Y = 4,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            _endpointVendorHint.SetScheme(Theme.LabelScheme(TextRole.System));

            _apiKeyLabel = new Label
            {
                X = 1,
                Y = 6,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            _apiKeyLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            _endpointApiKey = new NavTextField
            {
                X = 1,
                Y = 7,
                Width = Dim.Fill() - 2,
                Secret = true,
            };
            _endpointApiKey.SetScheme(Theme.CreateScheme());
            _endpointApiKey.BeforeKey = FormFieldKey;

            var modelLabel = new Label
            {
                Text = "Model Name / ID (type, Enter: pick / open probed list, Esc: close):",
                X = 1,
                Y = 9,
                Width = Dim.Fill() - 2,
                CanFocus = false,
            };
            modelLabel.SetScheme(Theme.LabelScheme(TextRole.Item));

            // One editable dropdown is the whole model picker: typing sets the id, opening
            // it probes the server and offers what answers. The list holds raw ids, so the
            // box text is always the id itself.
            _endpointModelCombo = new NavDropDownList
            {
                X = 1,
                Y = 10,
                Width = Dim.Fill() - 2,
                Height = 1,
            };
            _endpointModelCombo.SetScheme(Theme.CreateScheme());
            _endpointModelCombo.Source = new ListWrapper<string>(_endpointModelItems);
            _endpointModelCombo.IsPopoverOpen = IsSelecting;
            // Dirty means the box no longer shows the last confirmed id: Enter confirms
            // it. Clean means Enter opens the probed list instead.
            _endpointModelCombo.HasUncommitted = () =>
                !string.Equals(_endpointModelCombo.Text?.Trim() ?? string.Empty, _endpointAcceptedText, StringComparison.Ordinal);
            _endpointModelCombo.BeforeKey = key =>
            {
                // Opening the list probes first so it never opens onto stale results.
                if (key == Key.F4 || key == Key.Space)
                {
                    _ = EnsureProbedAsync();
                }

                return FormListKey(key);
            };

            var apiNote = new Label
            {
                X = 1,
                Y = 11,
                Width = Dim.Fill() - 2,
                CanFocus = false,
                Text = "Note: these providers support only OpenAI-compatible chat APIs (/v1).",
            };
            apiNote.SetScheme(Theme.LabelScheme(TextRole.System));

            _probeStatus = new Label
            {
                X = 1,
                Y = 12,
                Width = Dim.Fill() - 2,
                Height = 2,
                CanFocus = false,
                Text = string.Empty,
            };
            _probeStatus.SetScheme(Theme.CreateScheme());

            // Typing is explicit: the draft follows the fields; the match hint follows the URL.
            // A URL or key edit marks the probe stale without probing on every keystroke.
            _endpointBaseUrl.TextChanged += (_, _) =>
            {
                if (_isLoadingEndpoint)
                {
                    return;
                }

                _draft.EndpointFor(_draft.Provider).BaseUrl = _endpointBaseUrl.Text?.Trim() ?? string.Empty;
                _lastProbeKey = null;
                UpdateMatchHint();
                UpdateSummary();
            };
            _endpointBaseUrl.Accepting += (_, _) => MoveFormFocus(1);
            _endpointApiKey.TextChanged += (_, _) =>
            {
                if (_isLoadingEndpoint)
                {
                    return;
                }

                _draft.EndpointFor(_draft.Provider).SetApiKey(_endpointApiKey.Text);
                _lastProbeKey = null;
            };
            _endpointApiKey.Accepting += (_, _) => MoveFormFocus(1);
            _endpointModelCombo.TextChanged += (_, _) =>
            {
                if (_isLoadingEndpoint)
                {
                    return;
                }

                _draft.EndpointFor(_draft.Provider).Model = _endpointModelCombo.Text?.Trim() ?? string.Empty;
                UpdateSummary();
            };
            _endpointModelCombo.ValueChanged += (_, _) =>
            {
                if (_isLoadingEndpoint)
                {
                    return;
                }

                // Picking a probed row writes its id like typing would; Enter confirms it.
                var picked = _endpointModelCombo.Value?.Trim() ?? string.Empty;
                if (picked.Length > 0)
                {
                    _draft.EndpointFor(_draft.Provider).Model = picked;
                    UpdateSummary();
                }

                _endpointModelCombo.SetNeedsDraw();
            };
            _endpointModelCombo.Accepting += (_, _) => AcceptEndpointModel(advance: true);
            // A pick made inside the open list confirms the same way but stays put.
            _endpointModelCombo.Accepted += (_, _) => AcceptEndpointModel(advance: false);
            _endpointModelCombo.HasFocusChanged += (_, _) =>
            {
                // Opening the field probes the server, so the list is fresh when asked for.
                if (_endpointModelCombo.HasFocus)
                {
                    _ = EnsureProbedAsync();
                }
            };

            box.Add(
                _builtinUrlLabel,
                urlLabel,
                _endpointBaseUrl,
                _endpointMatchHint,
                _endpointVendorHint,
                _apiKeyLabel,
                _endpointApiKey,
                modelLabel,
                _endpointModelCombo,
                apiNote,
                _probeStatus);
            return box;
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
        /// Shows the picked provider's settings: the Claude model list for the CLI, or the
        /// endpoint fields rebound to the picked provider's own slot for the rest.
        /// </summary>
        private void RefreshProviderSettings()
        {
            var isClaude = !AppSettings.IsOpenAiProvider(_draft.Provider);
            _claudeBox.Visible = isClaude;
            _endpointBox.Visible = !isClaude;

            // Populated even while hidden so every label reads truthfully whenever it is shown.
            LoadEndpointControls();

            SyncProviderComboToDraft();
            UpdateProviderScrollHeight();
            _lastFormFocus = null;
        }

        /// <summary>
        /// Rebinds the shared endpoint fields to the picked provider's slot.
        /// </summary>
        private void LoadEndpointControls()
        {
            var provider = AppSettings.EffectiveProvider(_draft.Provider);
            var preset = OpenAiPresets.ForProvider(provider);
            var slot = _draft.EndpointFor(provider);

            _isLoadingEndpoint = true;
            try
            {
                _builtinUrlLabel.Text = $"Built-in endpoint: {preset.BaseUrl}";
                _endpointBaseUrl.Text = slot.BaseUrl;
                _endpointApiKey.Text = slot.ResolveApiKey();
                _endpointModelCombo.Text = slot.Model;
                _endpointAcceptedText = slot.Model?.Trim() ?? string.Empty;
                _apiKeyLabel.Text = $"API Key (sealed on this machine; {EnvVarName(provider)} wins when set):";
                _endpointVendorHint.Text = preset.Description;
                _probedModels.Clear();
                _endpointModelItems.Clear();
                _lastProbeKey = null;
                _probeStatus.Text = string.Empty;
            }
            finally
            {
                _isLoadingEndpoint = false;
            }

            UpdateMatchHint();
        }

        private static string EnvVarName(AgentProvider provider) => AppSettings.EffectiveProvider(provider) switch
        {
            AgentProvider.Google => "TQ2_GOOGLE_API_KEY",
            AgentProvider.OpenAI => "TQ2_OPENAI_API_KEY",
            AgentProvider.Anthropic => "TQ2_ANTHROPIC_API_KEY",
            _ => "TQ2_CUSTOM_API_KEY",
        };

        /// <summary>
        /// Writes the visible endpoint fields back into the picked provider's slot. Called before
        /// the provider changes and before saving, so a half-typed value never leaks sideways.
        /// </summary>
        private void FlushEndpointControls()
        {
            if (!_endpointBox.Visible)
            {
                return;
            }

            var slot = _draft.EndpointFor(_draft.Provider);
            slot.BaseUrl = _endpointBaseUrl.Text?.Trim() ?? string.Empty;
            slot.Model = _endpointModelCombo.Text?.Trim() ?? string.Empty;
            slot.SetApiKey(_endpointApiKey.Text);
        }

        /// <summary>
        /// The focusable controls of the visible section, in top-to-bottom order.
        /// Hidden controls (like the probe results before probing) and disabled controls
        /// (like the probe button while probing) are skipped.
        /// </summary>
        private List<View> ActiveFormControls()
        {
            // The provider section holds the picker first, then the picked provider's
            // settings, so arrows walk from one into the other.
            List<View> controls = _activeSection switch
            {
                SettingsSection.Provider => _claudeBox.Visible
                    ? [_providerCombo, _claudeCombo]
                    : [_providerCombo, _endpointBaseUrl, _endpointApiKey, _endpointModelCombo],
                _ => [_recallChars, _editorCommand, _testEditorButton, _openConfigFolderButton],
            };

            return controls.Where(c => c.Visible && c.Enabled).ToList();
        }

        private IEnumerable<View> AllFormControls() =>
        [
            _providerScroll,
            _providerCombo,
            _claudeCombo,
            _endpointBaseUrl, _endpointApiKey, _endpointModelCombo,
            _recallChars, _editorCommand, _testEditorButton, _openConfigFolderButton,
        ];

        private bool IsNavFocused() => MostFocused == _sectionsList;

        private bool IsFooterFocused() => MostFocused is Button focused && _actionButtons.Contains(focused);

        private bool IsFormFocused() => MostFocused is { } focused && ActiveFormControls().Contains(focused);

        /// <summary>
        /// Returns focus to the form after a dropdown list closes, unless focus already
        /// sits on a known pane (the list may have returned it to the box itself).
        /// </summary>
        private void RestoreFormFocus()
        {
            if (!IsNavFocused() && !IsFormFocused() && !IsFooterFocused())
            {
                FocusForm();
            }
        }

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
                (_, true, _) => "Up/Down: fields (browse in dropdowns) | Left/Right: fields | Enter: open/pick | Esc: close list | Tab: next pane | Ctrl+S: save",
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
        /// In a form dropdown, Up/Down browse options natively while Left/Right move between
        /// fields so the focus can leave the dropdown without reaching for Tab.
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
            // A dropdown list open above the form owns Esc and Tab. Esc closes just the
            // list (never the window: cancelling a pick must not cancel settings), Tab
            // closes it and then moves panes as usual. Native close is idempotent, so this
            // also swallows a bubble from a list that closed itself.
            if (_app.Popovers is { } popovers && popovers.GetActivePopover() is { } open)
            {
                if (key == Key.Esc)
                {
                    popovers.Hide(open);
                    RestoreFormFocus();
                    return true;
                }

                if (key == Key.Tab || key == Key.Tab.WithShift)
                {
                    popovers.Hide(open);
                    RestoreFormFocus();
                }
            }

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
            _prefsSection.Visible = section == SettingsSection.Preferences;
            UpdatePaneChrome();
            SetNeedsDraw();
        }

        private void UpdateSummary()
        {
            if (!AppSettings.IsOpenAiProvider(_draft.Provider))
            {
                var claudeDesc = string.IsNullOrEmpty(_draft.ClaudeModel)
                    ? "CLI Default"
                    : ClaudeModels.Describe(_draft.ClaudeModel);
                _summaryActiveLabel.Text = $"Current Configuration: Claude Code [{claudeDesc}]";
                _summaryStandbyLabel.Text = "Runs the 'claude' CLI locally — no endpoint, no key.";
                return;
            }

            var provider = AppSettings.EffectiveProvider(_draft.Provider);
            var preset = OpenAiPresets.ForProvider(provider);
            var slot = _draft.EndpointFor(provider);
            var modelDesc = string.IsNullOrEmpty(slot.Model) ? "default model" : slot.Model;
            _summaryActiveLabel.Text = $"Current Configuration: {preset.Name} [{modelDesc}]";
            var keyNote = AppSettings.GetEnvironmentApiKey(provider) is not null ? " (key from environment)" : string.Empty;
            _summaryStandbyLabel.Text = $"Endpoint: {slot.BaseUrl} — OpenAI-compatible API only{keyNote}.";
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

        private void UpdateMatchHint()
        {
            var url = _endpointBaseUrl.Text?.Trim() ?? string.Empty;
            var matched = OpenAiPresets.DetectPreset(url);
            _endpointMatchHint.Text = matched.IsCustom
                ? "Custom endpoint address."
                : $"URL matches the {matched.Name} built-in endpoint.";
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

        /// <summary>
        /// Confirms the model box on Enter: typed text or the picked row's id. The open
        /// list stays on this box afterwards; a closed box advances like other fields.
        /// </summary>
        private void AcceptEndpointModel(bool advance)
        {
            var modelName = _endpointModelCombo.Text?.Trim() ?? string.Empty;
            if (modelName.Length > 0)
            {
                _draft.EndpointFor(_draft.Provider).Model = modelName;
                _endpointAcceptedText = modelName;
                Say(_probeStatus, $"Picked: {modelName}", TextRole.Place);
                UpdateSummary();
            }

            if (advance)
            {
                MoveFormFocus(1);
            }
        }

        /// <summary>
        /// What the probe answers for: provider, URL, and whether a key is present (length
        /// only, never the secret). A URL or key edit marks the last probe stale without
        /// probing on every keystroke; the next focus or open probes again.
        /// </summary>
        private string CurrentProbeKey()
        {
            var url = _endpointBaseUrl.Text?.Trim() ?? string.Empty;
            var keyLength = _endpointApiKey.Text?.Length ?? 0;
            return $"{_draft.Provider}\n{url}\n{keyLength}";
        }

        /// <summary>
        /// Probes on focus or open when the results are stale or missing. Concurrent entries
        /// collapse into the running probe; a failed probe stays stale so refocusing retries.
        /// </summary>
        private async Task EnsureProbedAsync()
        {
            if (_isProbing || !_endpointBox.Visible)
            {
                return;
            }

            var key = CurrentProbeKey();
            if (key == _lastProbeKey && _probedModels.Count > 0)
            {
                return;
            }

            await ProbeEndpointModelsAsync(key).ConfigureAwait(false);
        }

        private async Task ProbeEndpointModelsAsync(string? probeKey = null)
        {
            probeKey ??= CurrentProbeKey();
            if (_isProbing)
            {
                return;
            }

            var rawUrl = _endpointBaseUrl.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(rawUrl) || !AppSettings.IsAddress(rawUrl))
            {
                Say(_probeStatus, "Enter a valid server URL, then open the list to probe.", TextRole.Danger);
                return;
            }

            var baseUrl = AppSettings.NormalizeBaseUrl(rawUrl);
            var typedKey = _endpointApiKey.Text?.Trim();
            var apiKey = string.IsNullOrEmpty(typedKey)
                ? AppSettings.GetEnvironmentApiKey(_draft.Provider)
                : typedKey;

            _probe?.Cancel();
            _probe?.Dispose();
            _probe = new CancellationTokenSource(ProbeTimeout);
            _isProbing = true;

            Say(_probeStatus, "Connecting to API endpoint...");

            try
            {
                var models = await LmStudioModels.ListAsync(baseUrl, apiKey, ProbeTimeout, _probe.Token);

                _app.Invoke(() =>
                {
                    if (models.Count == 0)
                    {
                        Say(_probeStatus, "Connected, but no models found.");
                        _probedModels.Clear();
                        _endpointModelItems.Clear();
                        _lastProbeKey = probeKey;
                    }
                    else
                    {
                        var sortedModels = models.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
                        Say(_probeStatus, $"Found {sortedModels.Count} model(s). Open the list, Up/Down to browse, Enter to pick:");
                        var currentText = _endpointModelCombo.Text;
                        _isLoadingEndpoint = true;
                        try
                        {
                            _probedModels.Clear();
                            _probedModels.AddRange(sortedModels);
                            _endpointModelItems.Clear();
                            foreach (var model in sortedModels)
                            {
                                _endpointModelItems.Add(model);
                            }

                            // The box text is the id: keep what was typed, not the first hit.
                            _endpointModelCombo.Text = currentText;
                        }
                        finally
                        {
                            _isLoadingEndpoint = false;
                        }

                        _lastProbeKey = probeKey;
                    }
                });
            }
            catch (Exception ex)
            {
                _app.Invoke(() =>
                {
                    var firstLine = ex.Message.IndexOf('\n') > 0 ? ex.Message[..ex.Message.IndexOf('\n')] : ex.Message;
                    Say(_probeStatus, $"Probe failed: {firstLine}", TextRole.Danger);
                    _probedModels.Clear();
                    _endpointModelItems.Clear();
                });
            }
            finally
            {
                _isProbing = false;
            }
        }

        private void RestoreDefaults()
        {
            var defaults = new AppSettings();
            _draft.CopyFrom(defaults);

            _isLoadingClaude = true;
            try
            {
                _claudeCombo.Text = ClaudeDisplayForId(defaults.ClaudeModel);
                _pendingClaudeId = null;
                _claudeTextTyped = false;
            }
            finally
            {
                _isLoadingClaude = false;
            }

            RefreshProviderSettings();
            SyncClaudeSelection();

            _recallChars.Text = defaults.TranscriptRecallCharacters.ToString();
            _editorCommand.Text = defaults.EditorCommand;
            _editorStatus.Text = string.Empty;

            UpdateSummary();
            Say(_messageLabel, "Restored all settings to defaults. Press Ctrl+S to save.", TextRole.Place);
            SetNeedsDraw();
        }

        private void SaveAndClose()
        {
            FlushEndpointControls();

            if (AppSettings.IsOpenAiProvider(_draft.Provider))
            {
                var slot = _draft.EndpointFor(_draft.Provider);
                var baseUrl = slot.BaseUrl?.Trim() ?? string.Empty;
                if (!string.IsNullOrEmpty(baseUrl) && !AppSettings.IsAddress(baseUrl))
                {
                    Say(_messageLabel, $"{OpenAiPresets.ForProvider(AppSettings.EffectiveProvider(_draft.Provider)).Name} Base URL must be a valid http:// or https:// address.", TextRole.Danger);
                    SwitchToSection(SettingsSection.Provider);
                    _endpointBaseUrl.SetFocus();
                    return;
                }

                slot.BaseUrl = AppSettings.NormalizeBaseUrl(baseUrl);
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
