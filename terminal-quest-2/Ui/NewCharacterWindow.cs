using System.Collections.ObjectModel;
using System.Text;

using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Saves;
using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The character creation screen: archetype selection, character name, backstory, and starting place.
    /// Built using Terminal.Gui built-in <see cref="ListView"/>, <see cref="Markdown"/>,
    /// <see cref="FrameView"/>, <see cref="TextField"/>, and <see cref="Button"/>.
    /// </summary>
    internal sealed class NewCharacterWindow : Window
    {
        private const int MaxNameLength = 40;

        private readonly IApplication? _app;
        private readonly AppSettings _settings;
        private readonly List<ClassTemplate> _classes;
        private ClassTemplate _customTemplate;

        private readonly ListView _classList;
        private readonly Markdown _classDetailsText;

        private readonly TextField _nameField;
        private readonly TextField _descriptionField;
        private readonly TextField _placeField;

        private readonly Label _statusLabel;
        private readonly Button _beginButton;
        private readonly Button _customizeButton;
        private readonly Button _promptButton;
        private readonly Button _cancelButton;

        public NewCharacterWindow(string saveName, AppSettings? settings = null, IApplication? app = null)
        {
            _app = app;
            _settings = settings ?? new AppSettings();

            _customTemplate = ClassTemplates.CreateDefaultCustom();
            _classes = [.. ClassTemplates.All, _customTemplate];

            Title = $"New Character - {saveName}";
            BorderStyle = LineStyle.Rounded;
            SetScheme(Theme.CreateScheme());

            // Left Pane: Archetypes List
            _classList = new ListView
            {
                Title = "Archetypes",
                X = 1,
                Y = 0,
                Width = Dim.Percent(40),
                Height = Dim.Fill() - 11,
                BorderStyle = LineStyle.Rounded,
                CanFocus = true,
            };
            _classList.SetScheme(Theme.CreateScheme());
            RefreshClassListSource();
            _classList.SelectedItem = 0;
            _classList.ValueChanged += (_, _) => UpdateClassDetails();

            // Right Pane: Archetype Details & Stats (Scrollable Markdown View)
            _classDetailsText = new Markdown
            {
                Title = "Archetype Details",
                X = Pos.Right(_classList) + 1,
                Y = 0,
                Width = Dim.Fill() - 1,
                Height = Dim.Fill() - 11,
                BorderStyle = LineStyle.Rounded,
                CanFocus = true,
            };
            _classDetailsText.SetScheme(Theme.CreateScheme());

            // Bottom Frame: Character Identity Box
            var identityFrame = new FrameView
            {
                Title = "Character Identity",
                X = 1,
                Y = Pos.Bottom(_classList),
                Width = Dim.Fill() - 1,
                Height = 8,
                BorderStyle = LineStyle.Rounded,
                CanFocus = false,
            };
            identityFrame.SetScheme(Theme.CreateScheme());

            var nameLabel = new Label { Text = "Name:", X = 3, Y = Pos.Bottom(_classList) + 1, CanFocus = false };
            _nameField = new TextField { X = 20, Y = Pos.Bottom(_classList) + 1, Width = Dim.Fill() - 3, CanFocus = true };

            var descLabel = new Label { Text = "Who you are:", X = 3, Y = Pos.Bottom(_classList) + 3, CanFocus = false };
            _descriptionField = new TextField { X = 20, Y = Pos.Bottom(_classList) + 3, Width = Dim.Fill() - 3, CanFocus = true };

            var placeLabel = new Label { Text = "Where you begin:", X = 3, Y = Pos.Bottom(_classList) + 5, CanFocus = false };
            _placeField = new TextField { X = 20, Y = Pos.Bottom(_classList) + 5, Width = Dim.Fill() - 3, CanFocus = true };

            nameLabel.SetScheme(Theme.CreateScheme());
            _nameField.SetScheme(Theme.CreateScheme());
            descLabel.SetScheme(Theme.CreateScheme());
            _descriptionField.SetScheme(Theme.CreateScheme());
            placeLabel.SetScheme(Theme.CreateScheme());
            _placeField.SetScheme(Theme.CreateScheme());

            _nameField.Accepting += (_, _) => _descriptionField.SetFocus();
            _descriptionField.Accepting += (_, _) => _placeField.SetFocus();
            _placeField.Accepting += (_, _) => TryConfirm();

            _classList.Accepting += (_, _) =>
            {
                if (IsCustomSelected)
                {
                    OpenArchetypeBuilder();
                }
                else
                {
                    _nameField.SetFocus();
                }
            };

            // Status message
            _statusLabel = new Label
            {
                X = 1,
                Y = Pos.Bottom(identityFrame),
                Width = Dim.Fill() - 2,
                Height = 1,
                CanFocus = false,
                Text = "Tab: Next Field | Up/Down: Navigate List / Scroll | Enter: Advance | Ctrl+B: Build Custom | Ctrl+G: Editor | Esc: Cancel",
            };
            _statusLabel.SetScheme(Theme.CreateScheme());

            // Bottom Action Buttons
            var btnY = Pos.Bottom(_statusLabel);
            _beginButton = new Button { Text = "Begin Adventure (Enter)", X = 1, Y = btnY, CanFocus = true };
            _customizeButton = new Button { Text = "Build Custom (Ctrl+B)", X = Pos.Right(_beginButton) + 2, Y = btnY, CanFocus = true };
            _promptButton = new Button { Text = "Rewrite Prompt (Ctrl+P)", X = Pos.Right(_customizeButton) + 2, Y = btnY, CanFocus = true };
            _cancelButton = new Button { Text = "Cancel (Esc)", X = Pos.Right(_promptButton) + 2, Y = btnY, CanFocus = true };

            _beginButton.SetScheme(Theme.CreateScheme());
            _customizeButton.SetScheme(Theme.CreateScheme());
            _promptButton.SetScheme(Theme.CreateScheme());
            _cancelButton.SetScheme(Theme.CreateScheme());

            _beginButton.Accepting += (_, _) => TryConfirm();
            _customizeButton.Accepting += (_, _) => OpenArchetypeBuilder();
            _promptButton.Accepting += (_, _) => BeginPromptEdit();
            _cancelButton.Accepting += (_, _) => Cancelled?.Invoke();

            Add(
                _classList,
                _classDetailsText,
                identityFrame,
                nameLabel,
                _nameField,
                descLabel,
                _descriptionField,
                placeLabel,
                _placeField,
                _statusLabel,
                _beginButton,
                _customizeButton,
                _promptButton,
                _cancelButton);

            UpdateClassDetails();

            Initialized += (_, _) => _classList.SetFocus();
        }

        public bool Confirmed { get; private set; }

        public ExternalEditor? Editor { get; init; }

        public string? SystemPromptPath { get; init; }

        public string PlayerName { get; private set; } = string.Empty;

        public string Description { get; private set; } = string.Empty;

        public ClassTemplate Template { get; private set; } = ClassTemplates.All[0];

        public bool IsCustomSelected => (_classList.SelectedItem ?? 0) == _classes.Count - 1;

        public ClassTemplate CustomTemplate => _customTemplate;

        public string? StartLocation { get; private set; }

        public event Action? Done;

        public event Action? Cancelled;

        protected override bool OnKeyDown(Key key)
        {
            if (Editor is { IsBusy: true })
            {
                return true;
            }

            if (key == Key.Esc)
            {
                Cancelled?.Invoke();
                return true;
            }

            if (key == Key.P.WithCtrl)
            {
                BeginPromptEdit();
                return true;
            }

            if (key == Key.B.WithCtrl)
            {
                OpenArchetypeBuilder();
                return true;
            }

            if (key == ExternalEditor.RequestKey && Editor is { } external)
            {
                if (MostFocused is TextField field)
                {
                    return external.TryBegin(field, SetEditingNotice);
                }
            }

            if (key == Key.Enter.WithCtrl)
            {
                TryConfirm();
                return true;
            }

            // Prevent arrow keys from jumping focus between panels
            if (MostFocused is TextField)
            {
                if (key == Key.CursorUp || key == Key.CursorDown)
                {
                    return true; // handled inside text field
                }
            }
            else if (MostFocused is ListView)
            {
                if (key == Key.CursorLeft || key == Key.CursorRight)
                {
                    return true; // handled inside list
                }
            }
            else if (MostFocused is Markdown)
            {
                if (key == Key.CursorLeft || key == Key.CursorRight)
                {
                    return true; // handled inside markdown view
                }
            }

            return base.OnKeyDown(key);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Editor?.Abandon();
            }

            base.Dispose(disposing);
        }

        private void RefreshClassListSource()
        {
            var classNames = _classes.Select((c, i) =>
                i == _classes.Count - 1
                    ? $" [Custom] {c.Name.PadRight(10)} (HP {c.MaxHealth,2})"
                    : $" {c.Name.PadRight(12)} (HP {c.MaxHealth,2})").ToList();
            _classList.SetSource(new ObservableCollection<string>(classNames));
        }

        public void OpenArchetypeBuilder()
        {
            var dialog = new ArchetypeBuilderDialog(_app, _settings, _customTemplate, Editor);

            dialog.Done += () =>
            {
                if (dialog.Confirmed && dialog.ResultTemplate is { } result)
                {
                    _customTemplate = result;
                    _classes[^1] = _customTemplate;
                    RefreshClassListSource();
                    _classList.SelectedItem = _classes.Count - 1;
                    UpdateClassDetails();
                }
            };

            if (_app is not null)
            {
                _app.Run(dialog);
            }

            if (dialog.Confirmed && dialog.ResultTemplate is { } res)
            {
                _customTemplate = res;
                _classes[^1] = _customTemplate;
                RefreshClassListSource();
                _classList.SelectedItem = _classes.Count - 1;
                UpdateClassDetails();
            }
        }

        private void UpdateClassDetails()
        {
            var idx = Math.Clamp(_classList.SelectedItem ?? 0, 0, _classes.Count - 1);
            var archetype = _classes[idx];

            var sb = new StringBuilder();
            sb.AppendLine($"# {archetype.Name}");
            sb.AppendLine();
            sb.AppendLine($"**Hit Points**: {archetype.MaxHealth} HP");
            sb.AppendLine();
            sb.AppendLine($"**Summary**: {archetype.Summary}");
            sb.AppendLine();
            sb.AppendLine($"**Aptitude**: {archetype.Aptitude}");
            sb.AppendLine();
            sb.AppendLine("### Starting Attributes");
            foreach (var attr in archetype.Attributes)
            {
                sb.AppendLine($"- **{attr.Name}**: {attr.Score}");
            }
            sb.AppendLine();
            sb.AppendLine("### Starting Equipment");
            if (archetype.StartingItems.Count > 0)
            {
                foreach (var item in archetype.StartingItems)
                {
                    sb.AppendLine($"- **{item.Quantity}x {item.Name}**: {item.Description}");
                }
            }
            if (archetype.StartingMoney > 0)
            {
                sb.AppendLine($"- **Gold**: {archetype.StartingMoney} gold pieces");
            }

            _classDetailsText.Text = sb.ToString();
        }

        private void SetEditingNotice(string? notice)
        {
            _statusLabel.Text = notice ?? "Tab: Next Field | Up/Down: Navigate List / Scroll | Enter: Advance | Ctrl+G: Editor | Esc: Cancel";
        }

        private void BeginPromptEdit()
        {
            if (SystemPromptPath is not { } path)
            {
                _statusLabel.Text = "No system prompt file exists for this save.";
                return;
            }

            if (Editor is not { } editor)
            {
                _statusLabel.Text = "No external editor configured. Check Settings.";
                return;
            }

            editor.TryBeginFile(path, SetEditingNotice);
        }

        private void TryConfirm()
        {
            var name = (Editor?.Resolve(_nameField) ?? _nameField.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                _statusLabel.Text = "Please enter a character name.";
                _nameField.SetFocus();
                return;
            }

            if (name.Length > MaxNameLength)
            {
                _statusLabel.Text = $"Name is too long (maximum {MaxNameLength} characters).";
                _nameField.SetFocus();
                return;
            }

            var idx = Math.Clamp(_classList.SelectedItem ?? 0, 0, _classes.Count - 1);
            Template = _classes[idx];

            PlayerName = name;
            Description = (Editor?.Resolve(_descriptionField) ?? _descriptionField.Text ?? string.Empty).Trim();

            var place = (Editor?.Resolve(_placeField) ?? _placeField.Text ?? string.Empty).Trim();
            StartLocation = place.Length > 0 ? place : null;

            Confirmed = true;
            Done?.Invoke();
        }
    }
}
