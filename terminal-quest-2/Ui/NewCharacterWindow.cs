using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The character screen: who you are, before anything is narrated.
    /// <para>
    /// Shown only for a save with nobody in it, and only after
    /// <see cref="SaveMenuWindow"/> has settled which save that is. It runs before the narrator
    /// process exists, which is the point - the answers are the player's, given once, rather than
    /// invented by the model or extracted from them a question at a time in prose.
    /// </para>
    /// <para>
    /// Owns no game logic. It collects answers and stops; the host decides what to write.
    /// </para>
    /// </summary>
    internal sealed class NewCharacterWindow : Window
    {
        private const int FieldHeight = 3;
        private const int HintHeight = 2;

        /// <summary>Long enough for any name worth typing, short enough to stay on one status row.</summary>
        private const int MaxNameLength = 40;

        private const string Hint =
            "Up/Down picks a class.  Enter moves down the form.  Ctrl+G opens an editor.  Ctrl+Enter begins.  Esc quits.";

        private readonly ClassListView _classes;
        private readonly Label _kit;
        private readonly Label _hint;
        private readonly TextField _name;
        private readonly TextField _description;
        private readonly TextField _place;

        public NewCharacterWindow(string saveName)
        {
            Title = $"New Character - {saveName}";
            BorderStyle = LineStyle.Rounded;
            SetScheme(Theme.CreateScheme());

            _classes = new ClassListView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill() - ((FieldHeight * 3) + HintHeight),
            };

            _kit = new Label
            {
                X = 0,
                Y = Pos.Bottom(_classes),
                Width = Dim.Fill(),
                Height = 1,
            };
            _kit.SetScheme(Theme.CreateScheme());

            _hint = new Label
            {
                X = 0,
                Y = Pos.Bottom(_kit),
                Width = Dim.Fill(),
                Height = 1,
                Text = Hint,
            };
            _hint.SetScheme(Theme.CreateScheme());

            // Enter walks down the form; only the last field begins the game.
            _name = MakeField();
            _name.Accepting += OnFieldAccepting;

            _description = MakeField();
            _description.Accepting += OnFieldAccepting;

            _place = MakeField();
            _place.Accepting += OnFieldAccepting;

            var nameFrame = Labelled("name", _name, Pos.Bottom(_hint));
            var descriptionFrame = Labelled("who you are", _description, Pos.Bottom(nameFrame));
            var placeFrame = Labelled("where you begin (optional - blank lets the narrator choose)", _place, Pos.Bottom(descriptionFrame));

            Add(_classes, _kit, _hint, nameFrame, descriptionFrame, placeFrame);

            ShowKit();

            // Focus is taken once the window is part of a running application; asking for it in the
            // constructor is too early to stick.
            Initialized += (_, _) => _name.SetFocus();
        }

        /// <summary>True once the player has settled on a character; false when they quit instead.</summary>
        public bool Confirmed { get; private set; }

        /// <summary>
        /// What Ctrl+G hands the focused field to, or null where there is nothing to hand it to.
        /// </summary>
        /// <remarks>
        /// The description is what this is really for: a sentence or two about who someone is, written
        /// somewhere it can be read back, rather than typed blind into a box one line high.
        /// </remarks>
        public ExternalEditor? Editor { get; init; }

        /// <summary>The name typed, trimmed.</summary>
        public string PlayerName { get; private set; } = string.Empty;

        /// <summary>The prose typed about who they are, trimmed. May be empty.</summary>
        public string Description { get; private set; } = string.Empty;

        /// <summary>The archetype chosen.</summary>
        public ClassTemplate Template { get; private set; } = ClassTemplates.All[0];

        /// <summary>Where they begin, or null when the narrator should decide.</summary>
        public string? StartLocation { get; private set; }

        /// <summary>Raised once the character is settled and the game should start.</summary>
        public event Action? Done;

        /// <summary>Raised when the player leaves without making anyone.</summary>
        public event Action? Cancelled;

        /// <summary>
        /// Lets go of an edit still open, so its answer is not written into a field that has gone.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Editor?.Abandon();
            }

            base.Dispose(disposing);
        }

        protected override bool OnKeyDown(Key key)
        {
            // Nothing else happens while a field is in another program. Esc would quit the character
            // screen outright, taking a description still being written with it.
            if (Editor is { IsBusy: true })
            {
                return true;
            }

            if (key == ExternalEditor.RequestKey && Editor is { } external)
            {
                // MostFocused rather than Focused: Labelled wraps every field in a frame, so Focused
                // is the frame. Nothing to do when the class list has the focus - there is no text on
                // it to edit - and the key is left unhandled to say so.
                if (MostFocused is not TextField field)
                {
                    return false;
                }

                return external.TryBegin(field, SetEditingNotice);
            }

            if (key == Key.Esc || key == Key.Q.WithCtrl)
            {
                Cancelled?.Invoke();
                return true;
            }

            // An escape hatch for anyone who has filled in what they care about and does not want
            // to press Enter down the rest of the form.
            if (key == Key.Enter.WithCtrl)
            {
                Submit();
                return true;
            }

            // The arrows drive the class list wherever focus happens to be, so picking a class
            // never costs a Tab. Single-line text fields have no use for them.
            if (key == Key.CursorUp)
            {
                _classes.MoveSelection(-1);
                ShowKit();
                return true;
            }

            if (key == Key.CursorDown)
            {
                _classes.MoveSelection(1);
                ShowKit();
                return true;
            }

            return base.OnKeyDown(key);
        }

        private static TextField MakeField() => new()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
        };

        private static FrameView Labelled(string title, View content, Pos y)
        {
            var frame = new FrameView
            {
                Title = title,
                X = 0,
                Y = y,
                Width = Dim.Fill(),
                Height = FieldHeight,
                BorderStyle = LineStyle.Rounded,
            };

            frame.Add(content);
            return frame;
        }

        /// <summary>
        /// Enter walks down the form rather than submitting it.
        /// <para>
        /// The save menu has one field, so there Enter can only mean "go". Here there are three,
        /// and a player who fills in the first and presses Enter means "next" - reading it as
        /// "begin" starts the game on a character they had not finished describing.
        /// </para>
        /// </summary>
        private void OnFieldAccepting(object? sender, CommandEventArgs e)
        {
            // Handled either way: Enter must never propagate up and trigger a default accept on
            // the window itself.
            e.Handled = true;

            var next = ReferenceEquals(sender, _name) ? _description
                : ReferenceEquals(sender, _description) ? _place
                : null;

            if (next is null)
            {
                Submit();
                return;
            }

            // Validate as we leave a field, so a name that will be rejected is rejected while it
            // is still the thing being typed.
            if (ReferenceEquals(sender, _name) && !ValidateName(out _))
            {
                return;
            }

            ShowHint();
            next.SetFocus();
        }

        private void Submit()
        {
            if (!ValidateName(out var typedName))
            {
                return;
            }

            if (_classes.Selected is not { } template)
            {
                Fail("Pick a class with Up and Down.");
                return;
            }

            var typedPlace = Read(_place);

            PlayerName = typedName;

            // Read through the editor, so a description written in one reaches the save with its
            // paragraphs rather than as the single line the field could show.
            Description = Read(_description);
            Template = template;
            StartLocation = typedPlace.Length > 0 ? typedPlace : null;
            Confirmed = true;

            Done?.Invoke();
        }

        /// <summary>
        /// What a field really holds: whatever an external edit put there when that is more than the
        /// one line on screen, and what was typed otherwise.
        /// </summary>
        private string Read(TextField field) =>
            (Editor?.Resolve(field) ?? field.Text ?? string.Empty).Trim();

        /// <summary>The one field that must be filled in, checked in one place.</summary>
        private bool ValidateName(out string typedName)
        {
            typedName = Read(_name);

            if (typedName.Length == 0)
            {
                Fail("Type a name first. It is what the world will call you.");
                _name.SetFocus();
                return false;
            }

            if (typedName.Length > MaxNameLength)
            {
                Fail($"That name is too long. Keep it under {MaxNameLength} characters.");
                _name.SetFocus();
                return false;
            }

            return true;
        }

        /// <summary>Shows what the highlighted class starts with, so the choice is an informed one.</summary>
        private void ShowKit()
        {
            _kit.Text = _classes.Selected is { } template
                ? $"Starts with: {template.StartingMoney} coin, " + string.Join(
                    ", ",
                    template.StartingItems.Select(item =>
                        item.Quantity > 1 ? $"{item.Name} x{item.Quantity}" : item.Name))
                : string.Empty;

            _kit.SetNeedsDraw();
        }

        /// <summary>Puts the controls back after an error has been read and acted on.</summary>
        private void ShowHint()
        {
            if (_hint.Text == Hint)
            {
                return;
            }

            _hint.Text = Hint;
            _hint.SetNeedsDraw();
        }

        private void Fail(string message)
        {
            _hint.Text = message;
            _hint.SetNeedsDraw();
        }

        /// <summary>
        /// Says where a field has gone while an external editor holds it, and puts the hint back once
        /// it is done.
        /// </summary>
        private void SetEditingNotice(string? notice)
        {
            if (notice is null)
            {
                ShowHint();
                return;
            }

            Fail(notice);
        }
    }
}
