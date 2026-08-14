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

        /// <summary>The kit line and the two hint rows below it.</summary>
        private const int HintHeight = 3;

        /// <summary>Long enough for any name worth typing, short enough to stay on one status row.</summary>
        private const int MaxNameLength = 40;

        private const string Hint =
            "Up/Down picks a class.  Enter moves down the form.  Ctrl+G opens an editor.  Ctrl+Enter begins.  Esc quits.";

        /// <summary>
        /// A row of its own, because what it offers is not another key for filling in this form.
        /// <para>
        /// This is the one place the narrator's brief can be rewritten before it has ever been read.
        /// Once the session starts the prompt is fixed for its whole life, so a player who wants a
        /// different kind of game entirely - a different tone, different rules, no dice at all - wants
        /// to know about it here rather than after the first scene has been narrated at them.
        /// </para>
        /// </summary>
        private const string PromptHint =
            "Ctrl+P rewrites the narrator's instructions for this save.";

        private readonly ClassListView _classes;
        private readonly Label _kit;
        private readonly Label _hint;
        private readonly Label _promptHint;
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

            _promptHint = new Label
            {
                X = 0,
                Y = Pos.Bottom(_hint),
                Width = Dim.Fill(),
                Height = 1,
                Text = PromptHint,
            };
            _promptHint.SetScheme(Theme.CreateScheme());

            // Enter walks down the form; only the last field begins the game.
            _name = MakeField();
            _name.Accepting += OnFieldAccepting;

            _description = MakeField();
            _description.Accepting += OnFieldAccepting;

            _place = MakeField();
            _place.Accepting += OnFieldAccepting;

            var nameFrame = Labelled("name", _name, Pos.Bottom(_promptHint));
            var descriptionFrame = Labelled("who you are", _description, Pos.Bottom(nameFrame));
            var placeFrame = Labelled("where you begin (optional - blank lets the narrator choose)", _place, Pos.Bottom(descriptionFrame));

            Add(_classes, _kit, _hint, _promptHint, nameFrame, descriptionFrame, placeFrame);

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

        /// <summary>
        /// The save's <c>system-prompt.txt</c>, which Ctrl+P opens, or null to offer nothing.
        /// </summary>
        /// <remarks>
        /// A path rather than the text, and edited in place rather than collected like the answers
        /// above it. This window still owns no game logic: it does not read the file, write it, or
        /// know what is in it - the host settles all of that before this screen opens, and the editor
        /// does the rest.
        /// </remarks>
        public string? SystemPromptPath { get; init; }

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

            // Ctrl+P rather than another Ctrl+G on a fourth field: the prompt is thousands of words of
            // paragraphs, and the field-and-shadow route Ctrl+G takes would show it as one line and
            // lose all of it the moment a key was pressed. The file is edited where it lives instead.
            //
            // Free for the same reason Ctrl+G is: a single-line text field claims neither, so the
            // chord reaches this window while a field holds the focus.
            if (key == Key.P.WithCtrl
                && Editor is { } prompts
                && SystemPromptPath is { Length: > 0 } promptPath)
            {
                return prompts.TryBeginFile(promptPath, SetEditingNotice);
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

        /// <summary>
        /// The wheel drives the class list, for the same reason the arrows do: it is the only list
        /// on the screen, so there is nothing else the wheel could mean.
        /// <para>
        /// Handled here rather than on the list itself because moving the highlight is never only
        /// that - the kit below it describes the highlighted class, and a wheel that moved one
        /// without the other would leave the two disagreeing.
        /// </para>
        /// </summary>
        protected override bool OnMouseEvent(Mouse mouse)
        {
            ArgumentNullException.ThrowIfNull(mouse);

            // Nothing moves while a field is in another program, matching OnKeyDown.
            if (Editor is { IsBusy: true })
            {
                return true;
            }

            var delta = Wheel(mouse);
            if (delta == 0)
            {
                return false;
            }

            _classes.MoveSelection(delta);
            ShowKit();
            return true;
        }

        /// <summary>Rows the wheel asks for: one per notch, or none when it was not the wheel.</summary>
        private static int Wheel(Mouse mouse) =>
            mouse.Flags.HasFlag(MouseFlags.WheeledUp) ? -1
            : mouse.Flags.HasFlag(MouseFlags.WheeledDown) ? 1
            : 0;

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

            // The form does not move while an editor is open, matching OnKeyDown. A read-only field
            // still takes Enter, and the key never reaches OnKeyDown because the field handles it
            // first - so on the last field this would begin the game while another program still held
            // the description, or the narrator's instructions.
            if (Editor is { IsBusy: true })
            {
                return;
            }

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
            // Every field, not the one being edited. OnKeyDown swallows the chords while an editor is
            // open, but a key the focused field handles itself never reaches it - so without this a
            // player could type into the form, or press Enter on the last field and begin the game,
            // while another program still holds the narrator's instructions. Ctrl+G's own edit marks
            // its one field read-only inside the editor; a file edit has no field to mark.
            var editing = Editor is { IsBusy: true };

            _name.ReadOnly = editing;
            _description.ReadOnly = editing;
            _place.ReadOnly = editing;

            if (notice is null)
            {
                ShowHint();
                return;
            }

            Fail(notice);
        }
    }
}
