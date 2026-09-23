using System.Data;
using System.Text;

using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The startup screen: continue, load, manage saves, or open settings.
    /// Modernized to use Terminal.Gui built-in <see cref="TableView"/>, <see cref="FrameView"/>,
    /// <see cref="Button"/>, <see cref="Dialog"/>, and <see cref="MessageBox"/>.
    /// Keyboard-first: the saves table and the grouped action bar are two panes. Tab toggles
    /// between them, arrows move within the focused pane, Enter runs. The footer buttons stay
    /// clickable for the mouse.
    /// Visual language: the header reads bright, the focused pane wears a blue border (with
    /// a <c>*</c> marker so focus never depends on colour alone), destructive hotkeys read
    /// red, feedback reads green/red, and help text recedes into dim grey.
    /// </summary>
    internal sealed class SaveMenuWindow : Window
    {
        private readonly IApplication _app;
        private readonly string _narrator;
        private List<SaveEntry> _saves = [];

        /// <summary>
        /// The saves list. Offers every key to the menu's hotkeys before its own handling,
        /// because <see cref="TableView"/> otherwise eats letters as type-ahead selection and
        /// the footer shortcuts would only work with focus already in the footer.
        /// Navigation keys are never hotkeys, so the table keeps those for itself.
        /// </summary>
        private sealed class HotkeyTableView : TableView
        {
            public Func<Key, bool>? InterceptHotkey { get; set; }

            protected override bool OnKeyDown(Key key) =>
                (InterceptHotkey?.Invoke(key) ?? false) || base.OnKeyDown(key);
        }

        private readonly Label _headerLabel;
        private readonly FrameView _savesFrame;
        private readonly HotkeyTableView _savesTable;
        private readonly FrameView _detailsFrame;
        private readonly Label _detailsText;
        private readonly Label _messageLabel;
        private readonly FrameView _actionsFrame;
        private readonly Label _hintLabel;

        private readonly Button _loadButton;
        private readonly Button _newSaveButton;
        private readonly Button _renameButton;
        private readonly Button _duplicateButton;
        private readonly Button _resetButton;
        private readonly Button _updatePromptsButton;
        private readonly Button _revealButton;
        private readonly Button _deleteButton;
        private readonly Button _settingsButton;
        private readonly Button _quitButton;

        private readonly List<Button> _actionButtons;
        private int _lastActionIndex;

        public SaveMenuWindow(IApplication app, string narrator)
        {
            _app = app ?? throw new ArgumentNullException(nameof(app));
            _narrator = narrator;

            Title = "Terminal Quest";
            BorderStyle = LineStyle.Rounded;
            SetScheme(Theme.CreateScheme());

            _headerLabel = new Label
            {
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                Height = 1,
                CanFocus = false,
                Text = $"Narrator: {narrator} | Choose a save or create a new character",
            };
            // Title level: bright, so it reads above the Normal table body.
            _headerLabel.SetScheme(Theme.LabelScheme(TextRole.Command));

            // Left pane: Saves Table inside a frame so the focused pane has a visible cue.
            _savesFrame = new FrameView
            {
                Title = "Saves",
                X = 1,
                Y = 2,
                Width = Dim.Percent(60),
                Height = Dim.Fill() - 8,
                BorderStyle = LineStyle.Rounded,
            };
            _savesFrame.SetScheme(Theme.CreateScheme());

            _savesTable = new HotkeyTableView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                FullRowSelect = true,
                MultiSelect = false,
            };
            _savesTable.SetScheme(Theme.CreateScheme());
            _savesTable.ValueChanged += (_, _) => UpdateDetails();
            _savesTable.Accepting += (_, _) => OpenSelected();
            _savesTable.InterceptHotkey = TryHandleMenuHotkey;
            _savesFrame.Add(_savesTable);

            // Right pane: Save Details Frame (never focusable, for the mouse only).
            _detailsFrame = new FrameView
            {
                Title = "Save Details",
                X = Pos.Right(_savesFrame) + 1,
                Y = 2,
                Width = Dim.Fill() - 1,
                Height = Dim.Fill() - 8,
                BorderStyle = LineStyle.Rounded,
            };
            _detailsFrame.SetScheme(Theme.CreateScheme());

            _detailsText = new Label
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill(),
                CanFocus = false,
            };
            // Read-only info body: plain Normal, never the bright Input ink.
            _detailsText.SetScheme(Theme.LabelScheme(TextRole.Normal));
            _detailsFrame.Add(_detailsText);

            // Feedback line: errors and confirmations only, so they never wipe the hints.
            _messageLabel = new Label
            {
                X = 1,
                Y = Pos.Bottom(_savesFrame),
                Width = Dim.Fill() - 2,
                Height = 1,
                CanFocus = false,
                Text = string.Empty,
            };
            _messageLabel.SetScheme(Theme.LabelScheme(TextRole.Normal));

            // Bottom pane: grouped actions. Row 0 is Play + core Manage, row 1 is
            // extra Manage + System. Both rows fit an 80-column terminal.
            _actionsFrame = new FrameView
            {
                Title = "Actions",
                X = 1,
                Y = Pos.Bottom(_messageLabel),
                Width = Dim.Fill() - 2,
                Height = 4,
                BorderStyle = LineStyle.Rounded,
            };
            _actionsFrame.SetScheme(Theme.CreateScheme());

            // Load is the primary action: the default button, so it carries extra weight.
            _loadButton = new Button { Text = "Load (Enter)", X = 0, Y = 0, IsDefault = true };
            _newSaveButton = new Button { Text = "New (N)", X = Pos.Right(_loadButton) + 1, Y = 0 };
            var playManageSeparator = new Label { Text = "│", CanFocus = false, X = Pos.Right(_newSaveButton) + 1, Y = 0, Width = 1, Height = 1 };
            playManageSeparator.SetScheme(Theme.LabelScheme(TextRole.Hint));
            _renameButton = new Button { Text = "Rename (R)", X = Pos.Right(playManageSeparator) + 1, Y = 0 };
            _duplicateButton = new Button { Text = "Duplicate (D)", X = Pos.Right(_renameButton) + 1, Y = 0 };
            _resetButton = new Button { Text = "Reset (Ctrl+R)", X = Pos.Right(_duplicateButton) + 1, Y = 0 };

            _updatePromptsButton = new Button { Text = "Prompts (U)", X = 0, Y = 1 };
            _revealButton = new Button { Text = "Folder (F)", X = Pos.Right(_updatePromptsButton) + 1, Y = 1 };
            _deleteButton = new Button { Text = "Delete (Del)", X = Pos.Right(_revealButton) + 1, Y = 1 };
            var manageSystemSeparator = new Label { Text = "│", CanFocus = false, X = Pos.Right(_deleteButton) + 1, Y = 1, Width = 1, Height = 1 };
            manageSystemSeparator.SetScheme(Theme.LabelScheme(TextRole.Hint));
            _settingsButton = new Button { Text = "Settings (S)", X = Pos.Right(manageSystemSeparator) + 1, Y = 1 };
            _quitButton = new Button { Text = "Quit (Q)", X = Pos.Right(_settingsButton) + 1, Y = 1 };

            _loadButton.SetScheme(Theme.CreateScheme());
            _newSaveButton.SetScheme(Theme.CreateScheme());
            _renameButton.SetScheme(Theme.CreateScheme());
            _duplicateButton.SetScheme(Theme.CreateScheme());
            // Destructive actions wear a red hotkey while keeping the standard focus ink,
            // so Delete/Reset stand apart without losing keyboard-focus visibility.
            _resetButton.SetScheme(Theme.DangerButtonScheme());
            _updatePromptsButton.SetScheme(Theme.CreateScheme());
            _revealButton.SetScheme(Theme.CreateScheme());
            _deleteButton.SetScheme(Theme.DangerButtonScheme());
            _settingsButton.SetScheme(Theme.CreateScheme());
            _quitButton.SetScheme(Theme.CreateScheme());
            // Group separators stay dim Hint grey: they divide, never compete.

            _actionButtons =
            [
                _loadButton,
                _newSaveButton,
                _renameButton,
                _duplicateButton,
                _resetButton,
                _updatePromptsButton,
                _revealButton,
                _deleteButton,
                _settingsButton,
                _quitButton,
            ];

            _loadButton.Accepting += (_, _) => OpenSelected();
            _newSaveButton.Accepting += (_, _) => ShowNewSaveDialog();
            _renameButton.Accepting += (_, _) =>
            {
                if (SelectedSave is { } save)
                {
                    ShowRenameDialog(save);
                }
            };
            _duplicateButton.Accepting += (_, _) =>
            {
                if (SelectedSave is { } save)
                {
                    Duplicate(save);
                }
            };
            _resetButton.Accepting += (_, _) =>
            {
                if (SelectedSave is { } save)
                {
                    ConfirmReset(save);
                }
            };
            _updatePromptsButton.Accepting += (_, _) =>
            {
                if (SelectedSave is { } save)
                {
                    ConfirmUpdatePrompts(save);
                }
            };
            _revealButton.Accepting += (_, _) =>
            {
                if (SelectedSave is { } save)
                {
                    Reveal(save);
                }
            };
            _deleteButton.Accepting += (_, _) =>
            {
                if (SelectedSave is { } save)
                {
                    ConfirmDelete(save);
                }
            };
            _settingsButton.Accepting += (_, _) => SettingsRequested?.Invoke();
            _quitButton.Accepting += (_, _) => Cancelled?.Invoke();

            _actionsFrame.Add(
                _loadButton,
                _newSaveButton,
                playManageSeparator,
                _renameButton,
                _duplicateButton,
                _resetButton,
                _updatePromptsButton,
                _revealButton,
                _deleteButton,
                manageSystemSeparator,
                _settingsButton,
                _quitButton);

            // Static hints: never overwritten by feedback, which has its own line above.
            _hintLabel = new Label
            {
                X = 1,
                Y = Pos.Bottom(_actionsFrame),
                Width = Dim.Fill() - 2,
                Height = 1,
                CanFocus = false,
                Text = "Up/Down: saves | Enter: load | N: new | Tab: actions | S: settings | Q: quit",
            };
            // Help text recedes into dim grey: read last, never first.
            _hintLabel.SetScheme(Theme.LabelScheme(TextRole.Hint));

            Add(
                _headerLabel,
                _savesFrame,
                _detailsFrame,
                _messageLabel,
                _actionsFrame,
                _hintLabel);

            // A mouse click lands focus directly, bypassing the Tab toggle, so the pane
            // titles follow the actual focus rather than only the toggle path.
            _savesTable.HasFocusChanged += (_, _) => UpdatePaneTitles();
            foreach (var button in _actionButtons)
            {
                button.HasFocusChanged += (_, _) => UpdatePaneTitles();
            }

            Reload();

            Initialized += (_, _) =>
            {
                _savesTable.SetFocus();
                UpdatePaneTitles();
            };
        }

        public SaveStore? Chosen { get; private set; }

        public ExternalEditor? Editor { get; init; }

        public event Action? Done;

        public event Action? Cancelled;

        public event Action? SettingsRequested;

        private SaveEntry? SelectedSave
        {
            get
            {
                var row = _savesTable.Value?.SelectedCell.Y ?? 0;
                if (row >= 0 && row < _saves.Count)
                {
                    return _saves[row];
                }
                return _saves.Count > 0 ? _saves[0] : null;
            }
        }

        /// <summary>
        /// Whether focus currently sits on one of the footer action buttons.
        /// </summary>
        private bool IsActionFocused() => MostFocused is Button focused && _actionButtons.Contains(focused);

        /// <summary>
        /// Jumps between the two panes: the saves table and the action bar.
        /// </summary>
        private void TogglePane()
        {
            if (IsActionFocused())
            {
                _savesTable.SetFocus();
            }
            else
            {
                FocusActions();
            }
        }

        /// <summary>
        /// Focuses the action bar, returning to the button used last where possible.
        /// </summary>
        private void FocusActions()
        {
            var index = Math.Clamp(_lastActionIndex, 0, _actionButtons.Count - 1);
            _actionButtons[index].SetFocus();
        }

        /// <summary>
        /// Moves focus linearly through the action buttons, wrapping at both ends so the
        /// focus never leaks back to the table except through <see cref="TogglePane"/>.
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
        }

        /// <summary>
        /// Marks the focused pane three ways: a <c>*</c> title marker (reads without
        /// colour), a blue frame border on the active pane with the idle pane dimmed grey,
        /// and hints for the pane in focus. The feedback line is separate and never touched here.
        /// </summary>
        private void UpdatePaneTitles()
        {
            if (MostFocused is Button focused)
            {
                var index = _actionButtons.IndexOf(focused);
                if (index >= 0)
                {
                    _lastActionIndex = index;
                }
            }

            var inActions = IsActionFocused();
            _savesFrame.Title = inActions ? "Saves" : "* Saves";
            _actionsFrame.Title = inActions ? "* Actions" : "Actions";
            _savesFrame.SetScheme(inActions ? Theme.FrameScheme(TextRole.Hint) : Theme.FrameScheme(TextRole.Button));
            _actionsFrame.SetScheme(inActions ? Theme.FrameScheme(TextRole.Button) : Theme.FrameScheme(TextRole.Hint));

            _hintLabel.Text = inActions
                ? "Left/Right: actions | Enter: run | Tab: saves | N: new | S: settings | Q: quit"
                : "Up/Down: saves | Enter: load | N: new | Tab: actions | S: settings | Q: quit";
        }

        /// <summary>
        /// Writes the feedback line in the role matching its severity: green for
        /// confirmations, red for errors, plain for passing information.
        /// </summary>
        private void Say(string text, TextRole role = TextRole.Normal)
        {
            _messageLabel.Text = text;
            _messageLabel.SetScheme(Theme.LabelScheme(role));
        }

        protected override bool OnKeyDown(Key key)
        {
            // Claimed before anything else, so Tab never walks the ten buttons one by one.
            if (key == Key.Tab || key == Key.Tab.WithShift)
            {
                TogglePane();
                return true;
            }

            // Inside the footer the arrows stay in the footer: they cycle the buttons.
            if (IsActionFocused())
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

            if (TryHandleMenuHotkey(key))
            {
                return true;
            }

            return base.OnKeyDown(key);
        }

        /// <summary>
        /// Runs the footer action for a menu hotkey, from whichever pane has focus. The table
        /// calls this before its own handling (see <see cref="HotkeyTableView"/>); the window
        /// calls it for keys that bubbled up from the footer.
        /// </summary>
        /// <returns>True when the key named a menu action, even one with no save to act on.</returns>
        private bool TryHandleMenuHotkey(Key key)
        {
            if (Letter(key, Key.Q) || key == Key.Esc || key == Key.Q.WithCtrl)
            {
                Cancelled?.Invoke();
                return true;
            }

            if (Letter(key, Key.S))
            {
                SettingsRequested?.Invoke();
                return true;
            }

            if (Letter(key, Key.N))
            {
                ShowNewSaveDialog();
                return true;
            }

            if (Letter(key, Key.C))
            {
                OpenSelected();
                return true;
            }

            if (Letter(key, Key.R))
            {
                if (SelectedSave is { } save)
                {
                    ShowRenameDialog(save);
                }
                return true;
            }

            if (key == Key.R.WithCtrl)
            {
                if (SelectedSave is { } save)
                {
                    ConfirmReset(save);
                }
                return true;
            }

            if (Letter(key, Key.U))
            {
                if (SelectedSave is { } save)
                {
                    ConfirmUpdatePrompts(save);
                }
                return true;
            }

            if (Letter(key, Key.D))
            {
                if (SelectedSave is { } save)
                {
                    Duplicate(save);
                }
                return true;
            }

            if (Letter(key, Key.F))
            {
                if (SelectedSave is { } save)
                {
                    Reveal(save);
                }
                return true;
            }

            if (Letter(key, Key.X) || key == Key.Delete)
            {
                if (SelectedSave is { } save)
                {
                    ConfirmDelete(save);
                }
                return true;
            }

            return false;
        }

        private static bool Letter(Key key, Key letter) => key == letter || key == letter.WithShift;

        private void Reload()
        {
            try
            {
                _saves = [.. SavePaths.List()];
            }
            catch (Exception ex)
            {
                Say($"Error reading saves: {ex.Message}", TextRole.Danger);
                _saves = [];
            }

            _headerLabel.Text = _saves.Count switch
            {
                0 => $"Narrator: {_narrator} | No saves yet - press N for a new character",
                1 => $"Narrator: {_narrator} | 1 save - Enter loads, Tab reaches actions",
                _ => $"Narrator: {_narrator} | {_saves.Count} saves - Enter loads, Tab reaches actions",
            };

            var table = new DataTable();
            table.Columns.Add("Save Name", typeof(string));
            table.Columns.Add("Last Played", typeof(string));
            table.Columns.Add("Turns", typeof(int));
            table.Columns.Add("Size", typeof(string));

            foreach (var save in _saves)
            {
                var played = save.LastPlayed == DateTimeOffset.MinValue
                    ? "Never"
                    : save.LastPlayed.LocalDateTime.ToString("yyyy-MM-dd HH:mm");

                table.Rows.Add(save.Name, played, save.Turn, FormatSize(save.SizeBytes));
            }

            _savesTable.Table = new DataTableSource(table);

            if (_saves.Count > 0)
            {
                _savesTable.SetSelection(0, 0, false);
            }

            UpdateDetails();
        }

        private void SelectSave(string name)
        {
            for (var index = 0; index < _saves.Count; index++)
            {
                if (SaveStore.Matches(_saves[index].Name, name))
                {
                    _savesTable.SetSelection(0, index, false);
                    UpdateDetails();
                    return;
                }
            }
        }

        private void UpdateDetails()
        {
            if (SelectedSave is not { } save)
            {
                _detailsFrame.Title = "Save Details";
                // Guidance, not content: dimmed so the eye skips to the actions below.
                _detailsText.SetScheme(Theme.LabelScheme(TextRole.Hint));
                _detailsText.Text = "No saves found.\n\nPress [N] or pick New below to begin your adventure.";
                return;
            }

            _detailsText.SetScheme(Theme.LabelScheme(TextRole.Normal));

            _detailsFrame.Title = $"Details - {save.Name}";

            try
            {
                var store = new SaveStore(SavePaths.Folder(save.Name));
                var chars = store.ReadCharacters();
                var player = SaveStore.Player(chars);
                var locs = store.ReadLocations();
                var loc = SaveStore.WhereIs(locs, player?.Id);

                var sb = new StringBuilder();
                sb.AppendLine($"Save Name:  {save.Name}");
                sb.AppendLine($"Turns:      {save.Turn}");
                sb.AppendLine($"Disk Size:  {FormatSize(save.SizeBytes)}");
                sb.AppendLine($"Last Saved: {(save.LastPlayed == DateTimeOffset.MinValue ? "Never" : save.LastPlayed.LocalDateTime.ToString("g"))}");
                sb.AppendLine();
                sb.AppendLine("--- Character ---");
                if (player is not null)
                {
                    sb.AppendLine($"Name:       {player.Name}");
                    sb.AppendLine($"Health:     {player.Health} / {player.MaxHealth} HP");
                    if (player.Attributes.Count > 0)
                    {
                        sb.AppendLine($"Attributes: {string.Join(", ", player.Attributes.Select(a => $"{a.Name} {a.Score}"))}");
                    }
                }
                else
                {
                    sb.AppendLine("(New save - character not created yet)");
                }
                sb.AppendLine();
                sb.AppendLine("--- Location ---");
                sb.AppendLine(loc is not null ? loc.Name : "(No location set)");

                _detailsText.Text = sb.ToString();
            }
            catch
            {
                _detailsText.Text = $"Save Name: {save.Name}\nTurns:     {save.Turn}\nSize:      {FormatSize(save.SizeBytes)}";
            }
        }

        private void OpenSelected()
        {
            if (SelectedSave is not { } save)
            {
                ShowNewSaveDialog();
                return;
            }

            Open(save.Name);
        }

        private void Open(string name)
        {
            try
            {
                Chosen = new SaveStore(SavePaths.Folder(name));
                Done?.Invoke();
            }
            catch (Exception ex)
            {
                Say($"Could not open save: {ex.Message}", TextRole.Danger);
            }
        }

        private void ShowNewSaveDialog()
        {
            var dialog = new Dialog
            {
                Title = "New Save",
                Width = 50,
                Height = 10,
                BorderStyle = LineStyle.Rounded,
            };
            dialog.SetScheme(Theme.CreateScheme());

            // Field prompt in gold, input bright on focus, errors red: input, help,
            // and feedback each read differently at a glance.
            var label = new Label { Text = "Enter name for new save:", X = 1, Y = 1 };
            label.SetScheme(Theme.LabelScheme(TextRole.Item));

            var nameField = new TextField { X = 1, Y = 3, Width = Dim.Fill() - 2 };
            nameField.SetScheme(Theme.CreateScheme());

            var errorLabel = new Label { X = 1, Y = 5, Width = Dim.Fill() - 2, Text = string.Empty };
            errorLabel.SetScheme(Theme.LabelScheme(TextRole.Danger));

            var okButton = new Button { Text = "Create", IsDefault = true };
            var cancelButton = new Button { Text = "Cancel" };
            okButton.SetScheme(Theme.CreateScheme());
            cancelButton.SetScheme(Theme.CreateScheme());

            void TryCreate()
            {
                var name = (Editor?.Resolve(nameField) ?? nameField.Text ?? string.Empty).Trim();
                if (name.Length == 0)
                {
                    errorLabel.Text = "Please enter a save name.";
                    nameField.SetFocus();
                    return;
                }
                if (!SavePaths.IsValidName(name))
                {
                    errorLabel.Text = "Invalid folder name. Avoid \\ / : * ? \" < > |";
                    nameField.SetFocus();
                    return;
                }
                if (SavePaths.Exists(name))
                {
                    errorLabel.Text = $"Save '{name}' already exists.";
                    nameField.SetFocus();
                    return;
                }

                _app.RequestStop(dialog);
                Open(name);
            }

            nameField.Accepting += (_, _) => TryCreate();
            okButton.Accepting += (_, _) => TryCreate();
            cancelButton.Accepting += (_, _) => _app.RequestStop(dialog);

            dialog.KeyDown += (_, key) =>
            {
                if (key == Key.Esc)
                {
                    _app.RequestStop(dialog);
                }
            };

            dialog.Add(label, nameField, errorLabel);
            dialog.AddButton(okButton);
            dialog.AddButton(cancelButton);

            dialog.Initialized += (_, _) => nameField.SetFocus();

            _app.Run(dialog);
        }

        private void ShowRenameDialog(SaveEntry save)
        {
            var dialog = new Dialog
            {
                Title = $"Rename '{save.Name}'",
                Width = 50,
                Height = 10,
                BorderStyle = LineStyle.Rounded,
            };
            dialog.SetScheme(Theme.CreateScheme());

            var label = new Label { Text = "Enter new name:", X = 1, Y = 1 };
            label.SetScheme(Theme.LabelScheme(TextRole.Item));

            var nameField = new TextField { X = 1, Y = 3, Width = Dim.Fill() - 2, Text = save.Name };
            nameField.SetScheme(Theme.CreateScheme());

            var errorLabel = new Label { X = 1, Y = 5, Width = Dim.Fill() - 2, Text = string.Empty };
            errorLabel.SetScheme(Theme.LabelScheme(TextRole.Danger));

            var okButton = new Button { Text = "Rename", IsDefault = true };
            var cancelButton = new Button { Text = "Cancel" };
            okButton.SetScheme(Theme.CreateScheme());
            cancelButton.SetScheme(Theme.CreateScheme());

            void TryRename()
            {
                var newName = (Editor?.Resolve(nameField) ?? nameField.Text ?? string.Empty).Trim();
                if (newName.Length == 0)
                {
                    errorLabel.Text = "Please enter a name.";
                    nameField.SetFocus();
                    return;
                }
                if (!SavePaths.IsValidName(newName))
                {
                    errorLabel.Text = "Invalid name. Avoid \\ / : * ? \" < > |";
                    nameField.SetFocus();
                    return;
                }
                if (string.Equals(save.Name, newName, StringComparison.Ordinal))
                {
                    _app.RequestStop(dialog);
                    return;
                }

                try
                {
                    SavePaths.Rename(save.Name, newName);
                    _app.RequestStop(dialog);
                    Reload();
                    SelectSave(newName);
                }
                catch (Exception ex)
                {
                    errorLabel.Text = ex.Message;
                    nameField.SetFocus();
                }
            }

            nameField.Accepting += (_, _) => TryRename();
            okButton.Accepting += (_, _) => TryRename();
            cancelButton.Accepting += (_, _) => _app.RequestStop(dialog);

            dialog.KeyDown += (_, key) =>
            {
                if (key == Key.Esc)
                {
                    _app.RequestStop(dialog);
                }
            };

            dialog.Add(label, nameField, errorLabel);
            dialog.AddButton(okButton);
            dialog.AddButton(cancelButton);

            dialog.Initialized += (_, _) => nameField.SetFocus();

            _app.Run(dialog);
        }

        private void Duplicate(SaveEntry save)
        {
            try
            {
                var copyName = SavePaths.Duplicate(save.Name);
                Reload();
                SelectSave(copyName);
                Say($"Duplicated save '{save.Name}' as '{copyName}'.", TextRole.Place);
            }
            catch (Exception ex)
            {
                Say($"Duplicate failed: {ex.Message}", TextRole.Danger);
            }
        }

        private void ConfirmReset(SaveEntry save)
        {
            var result = MessageBox.Query(
                _app,
                "Reset Save",
                $"Reset save '{save.Name}' back to turn 0?\nCharacter details are preserved, but turn history and claims will be reset.",
                "Reset",
                "Cancel");

            if (result == 0)
            {
                try
                {
                    SavePaths.Reset(save.Name);
                    Reload();
                    Say($"Reset save '{save.Name}' to turn 0.", TextRole.Place);
                }
                catch (Exception ex)
                {
                    Say($"Reset failed: {ex.Message}", TextRole.Danger);
                }
            }
        }

        private void ConfirmUpdatePrompts(SaveEntry save)
        {
            var result = MessageBox.Query(
                _app,
                "Update Prompts",
                $"Update story prompts in '{save.Name}' to the latest asset defaults?\nCustom changes in narrator-story.txt and director-story.txt will be overwritten.",
                "Update",
                "Cancel");

            if (result == 0)
            {
                try
                {
                    SavePaths.UpdatePrompts(save.Name);
                    Reload();
                    Say($"Updated story prompts in '{save.Name}' to latest defaults.", TextRole.Place);
                }
                catch (Exception ex)
                {
                    Say($"Update failed: {ex.Message}", TextRole.Danger);
                }
            }
        }

        private void ConfirmDelete(SaveEntry save)
        {
            var result = MessageBox.Query(
                _app,
                "Delete Save",
                $"Are you sure you want to delete save '{save.Name}'?\nThis action cannot be undone.",
                "Delete",
                "Cancel");

            if (result == 0)
            {
                try
                {
                    SavePaths.Delete(save.Name);
                    Reload();
                    Say($"Deleted save '{save.Name}'.", TextRole.Place);
                }
                catch (Exception ex)
                {
                    Say($"Delete failed: {ex.Message}", TextRole.Danger);
                }
            }
        }

        private void Reveal(SaveEntry save)
        {
            var folder = SavePaths.Folder(save.Name);
            if (!FileExplorer.TryOpen(folder, out var reason))
            {
                Say(reason ?? "Could not open save folder.", TextRole.Danger);
            }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 1024)
            {
                return $"{bytes} B";
            }
            if (bytes < 1024 * 1024)
            {
                return $"{bytes / 1024.0:F1} KB";
            }
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        }
    }
}
