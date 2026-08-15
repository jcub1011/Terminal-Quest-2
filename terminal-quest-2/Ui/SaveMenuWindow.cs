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
    /// </summary>
    internal sealed class SaveMenuWindow : Window
    {
        private readonly IApplication _app;
        private readonly string _narrator;
        private List<SaveEntry> _saves = [];

        private readonly Label _headerLabel;
        private readonly TableView _savesTable;
        private readonly FrameView _detailsFrame;
        private readonly Label _detailsText;
        private readonly Label _statusLabel;

        private readonly Button _loadButton;
        private readonly Button _newSaveButton;
        private readonly Button _renameButton;
        private readonly Button _duplicateButton;
        private readonly Button _resetButton;
        private readonly Button _revealButton;
        private readonly Button _deleteButton;
        private readonly Button _settingsButton;
        private readonly Button _quitButton;

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
                Text = $"Narrator: {narrator} | Choose a save or create a new character",
            };
            _headerLabel.SetScheme(Theme.CreateScheme());

            // Left: Saves Table
            _savesTable = new TableView
            {
                X = 1,
                Y = 2,
                Width = Dim.Percent(60),
                Height = Dim.Fill() - 5,
                FullRowSelect = true,
                MultiSelect = false,
            };
            _savesTable.SetScheme(Theme.CreateScheme());
            _savesTable.ValueChanged += (_, _) => UpdateDetails();
            _savesTable.Accepting += (_, _) => OpenSelected();

            // Right: Save Details Frame
            _detailsFrame = new FrameView
            {
                Title = "Save Details",
                X = Pos.Right(_savesTable) + 1,
                Y = 2,
                Width = Dim.Fill() - 1,
                Height = Dim.Fill() - 5,
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
            _detailsText.SetScheme(Theme.CreateScheme());
            _detailsFrame.Add(_detailsText);

            // Status message line
            _statusLabel = new Label
            {
                X = 1,
                Y = Pos.Bottom(_savesTable),
                Width = Dim.Fill() - 2,
                Height = 1,
                Text = "Enter: Load | N: New | R: Rename | D: Duplicate | Ctrl+R: Reset | F: Folder | Del: Delete | S: Settings | Q: Quit",
            };
            _statusLabel.SetScheme(Theme.CreateScheme());

            // Bottom action buttons: Row 1 (Primary Actions)
            var row1Y = Pos.Bottom(_statusLabel);

            _loadButton = new Button { Text = "Load (Enter)", X = 1, Y = row1Y };
            _newSaveButton = new Button { Text = "New Save (N)", X = Pos.Right(_loadButton) + 1, Y = row1Y };
            _renameButton = new Button { Text = "Rename (R)", X = Pos.Right(_newSaveButton) + 1, Y = row1Y };
            _duplicateButton = new Button { Text = "Duplicate (D)", X = Pos.Right(_renameButton) + 1, Y = row1Y };
            _resetButton = new Button { Text = "Reset (Ctrl+R)", X = Pos.Right(_duplicateButton) + 1, Y = row1Y };

            // Bottom action buttons: Row 2 (Manage & System)
            var row2Y = Pos.Bottom(_loadButton);

            _revealButton = new Button { Text = "Folder (F)", X = 1, Y = row2Y };
            _deleteButton = new Button { Text = "Delete (Del)", X = Pos.Right(_revealButton) + 1, Y = row2Y };
            _settingsButton = new Button { Text = "Settings (S)", X = Pos.Right(_deleteButton) + 1, Y = row2Y };
            _quitButton = new Button { Text = "Quit (Q)", X = Pos.Right(_settingsButton) + 1, Y = row2Y };

            _loadButton.SetScheme(Theme.CreateScheme());
            _newSaveButton.SetScheme(Theme.CreateScheme());
            _renameButton.SetScheme(Theme.CreateScheme());
            _duplicateButton.SetScheme(Theme.CreateScheme());
            _resetButton.SetScheme(Theme.CreateScheme());
            _revealButton.SetScheme(Theme.CreateScheme());
            _deleteButton.SetScheme(Theme.CreateScheme());
            _settingsButton.SetScheme(Theme.CreateScheme());
            _quitButton.SetScheme(Theme.CreateScheme());

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

            Add(
                _headerLabel,
                _savesTable,
                _detailsFrame,
                _statusLabel,
                _loadButton,
                _newSaveButton,
                _renameButton,
                _duplicateButton,
                _resetButton,
                _revealButton,
                _deleteButton,
                _settingsButton,
                _quitButton);

            Reload();

            Initialized += (_, _) => _savesTable.SetFocus();
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

        protected override bool OnKeyDown(Key key)
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

            return base.OnKeyDown(key);
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
                _statusLabel.Text = $"Error reading saves: {ex.Message}";
                _saves = [];
            }

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
                _detailsText.Text = "No saves found.\n\nPress [N] or click 'New Save' to begin your adventure.";
                return;
            }

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
                _statusLabel.Text = $"Could not open save: {ex.Message}";
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

            var label = new Label { Text = "Enter name for new save:", X = 1, Y = 1 };
            label.SetScheme(Theme.CreateScheme());

            var nameField = new TextField { X = 1, Y = 3, Width = Dim.Fill() - 2 };
            nameField.SetScheme(Theme.CreateScheme());

            var errorLabel = new Label { X = 1, Y = 5, Width = Dim.Fill() - 2, Text = string.Empty };
            errorLabel.SetScheme(Theme.CreateScheme());

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
            label.SetScheme(Theme.CreateScheme());

            var nameField = new TextField { X = 1, Y = 3, Width = Dim.Fill() - 2, Text = save.Name };
            nameField.SetScheme(Theme.CreateScheme());

            var errorLabel = new Label { X = 1, Y = 5, Width = Dim.Fill() - 2, Text = string.Empty };
            errorLabel.SetScheme(Theme.CreateScheme());

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
                _statusLabel.Text = $"Duplicated save '{save.Name}' as '{copyName}'.";
            }
            catch (Exception ex)
            {
                _statusLabel.Text = $"Duplicate failed: {ex.Message}";
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
                    _statusLabel.Text = $"Reset save '{save.Name}' to turn 0.";
                }
                catch (Exception ex)
                {
                    _statusLabel.Text = $"Reset failed: {ex.Message}";
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
                    _statusLabel.Text = $"Deleted save '{save.Name}'.";
                }
                catch (Exception ex)
                {
                    _statusLabel.Text = $"Delete failed: {ex.Message}";
                }
            }
        }

        private void Reveal(SaveEntry save)
        {
            var folder = SavePaths.Folder(save.Name);
            if (!FileExplorer.TryOpen(folder, out var reason))
            {
                _statusLabel.Text = reason ?? "Could not open save folder.";
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
