using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The full-screen layout: narration on the left, status on the right, input pinned to the
    /// bottom. Owns no game logic - it raises <see cref="CommandEntered"/> and lets the host
    /// decide what a command means.
    /// </summary>
    internal sealed class GameWindow : Window
    {
        private const int StatusWidth = 16;
        private const int InputHeight = 3;

        private const string IdleTitle = "command";
        private const string BusyTitle = "command - narrator speaking";

        /// <summary>
        /// How much of the transcript the suggestions may cover. A bare <c>/</c> matches every
        /// command there is, and burying the last thing the narrator said under the whole list is
        /// a worse trade than scrolling the few rows that do not fit.
        /// </summary>
        private const int MaxSuggestionRows = 8;

        private readonly TextField _input;
        private readonly FrameView _inputFrame;
        private readonly CommandSuggestionView _suggestions;

        public GameWindow(GameState state)
        {
            State = state;

            Title = "Terminal Quest";
            BorderStyle = LineStyle.Rounded;

            // Applied to the window so every stock control inside it (the input field, its frame,
            // the borders) inherits a transparent background instead of Terminal.Gui's defaults.
            SetScheme(Theme.CreateScheme());

            Narration = new NarrationView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill() - StatusWidth,
                Height = Dim.Fill() - InputHeight,
            };

            Status = new StatusView(state)
            {
                X = Pos.Right(Narration) + 1,
                Y = 0,
                Width = StatusWidth - 1,
                Height = Dim.Fill() - InputHeight,
            };

            _input = new TextField
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
            };

            _inputFrame = new FrameView
            {
                Title = IdleTitle,
                X = 0,
                Y = Pos.Bottom(Narration),
                Width = Dim.Fill(),
                Height = InputHeight,
                BorderStyle = LineStyle.Rounded,
            };
            _inputFrame.Add(_input);

            // Sized and placed only when there is something to show, since both depend on how many
            // commands the half-typed word still matches.
            _suggestions = new CommandSuggestionView
            {
                X = 0,
                Width = Dim.Fill() - StatusWidth,
                Visible = false,
            };

            _input.Accepting += OnInputAccepting;
            _input.ValueChanged += (_, _) => RefreshSuggestions();

            // The suggestions are added last so they draw over the foot of the transcript, the
            // same layering the settings screen uses to drop its editor onto a drawn row.
            Add(Narration, Status, _inputFrame, _suggestions);
        }

        public NarrationView Narration { get; }

        public StatusView Status { get; }

        public GameState State { get; }

        /// <summary>Raised on the UI thread when the player submits a non-empty command.</summary>
        public event Action<string>? CommandEntered;

        /// <summary>
        /// Asked before a submitted line is taken. Returning false abandons the submission: the
        /// text is left in the field and nothing is echoed, so the player loses neither what they
        /// typed nor their place. Whoever says no owns saying why.
        /// </summary>
        /// <remarks>
        /// A predicate the host sets rather than a rule this window enforces, because the reasons
        /// a line cannot be taken - a turn already in flight, and which lines are exempt from that -
        /// are the host's business. This window still does not know what a command means.
        /// </remarks>
        public Func<string, bool>? CanSubmit { get; set; }

        /// <summary>
        /// Whether a narrator turn is in flight.
        /// <para>
        /// The input field deliberately stays live while it is: a turn can take minutes, and
        /// disabling the field takes the player's own commands - <c>/story</c>, <c>/inventory</c>,
        /// <c>/quit</c> - away with it, which leaves nothing to press. What a turn in flight
        /// actually forbids is a second turn, and that is <see cref="CanSubmit"/>'s job.
        /// </para>
        /// </summary>
        public bool IsBusy
        {
            get => State.IsBusy;
            set
            {
                State.IsBusy = value;
                Status.SetNeedsDraw();

                _inputFrame.Title = value ? BusyTitle : IdleTitle;
                _inputFrame.SetNeedsDraw();

                // Nothing re-focuses here, unlike the property this replaced. That one had to,
                // because disabling the field moved the focus off it; this never disables it, so
                // the caret stays where the player left it - including mid-word, mid-turn.
            }
        }

        /// <summary>
        /// Raised when the player asks to leave this save. The host owns what happens next, which
        /// is a return to the save menu rather than the end of the program.
        /// </summary>
        public event Action? LeaveRequested;

        public void FocusInput() => _input.SetFocus();

        /// <summary>
        /// Keys the input field had no use for.
        /// <para>
        /// Terminal.Gui offers a key to the focused subview first, and focus lives in the input
        /// field, so everything printable - and Enter - is gone before this runs. What reaches
        /// here is what a single-line text field ignores, which is exactly the set the suggestions
        /// need: the arrows to move through them and Esc to put them away.
        /// </para>
        /// </summary>
        protected override bool OnKeyDown(Key key)
        {
            if (_suggestions.Visible)
            {
                // Esc closes the list and stops, so one press cannot dismiss the suggestions and
                // walk out of the save underneath them at the same time. Only while it is a list,
                // though: once the command is settled the strip is a reminder that blocks nothing,
                // and taking a press off leaving would cost more than the reminder is worth - it
                // is on screen for most of the time any command is being typed.
                if (key == Key.Esc && _suggestions.IsChoosing)
                {
                    HideSuggestions();
                    return true;
                }

                // Only while there is a choice to move through. A settled command is one row that
                // nothing selects, and swallowing the arrows there would take them from the
                // transcript for the whole time an argument is being typed.
                if (_suggestions.IsChoosing && (key == Key.CursorUp || key == Key.CursorDown))
                {
                    _suggestions.MoveSelection(key == Key.CursorUp ? -1 : 1);
                    return true;
                }

                // Tab is claimed before it can move the focus. Nothing else here is focusable, so
                // the only thing it could otherwise do is nothing at all.
                //
                // Right only ever arrives here with the caret at the end of the line - a text
                // field handles the key itself anywhere else, and stops handling it once there is
                // nothing to its right. So this takes the suggestion exactly when there is no text
                // left to walk through, and leaves Right as the caret key the rest of the time,
                // without having to ask where the caret is.
                if ((key == Key.Tab || key == Key.CursorRight) && _suggestions.Selected is { } completion)
                {
                    Complete(completion);
                    return true;
                }
            }

            // Both mean the same thing here: leave this save. Quitting the program is the save
            // menu's to offer, one screen further out.
            if (key == Key.Esc || key == Key.Q.WithCtrl)
            {
                LeaveRequested?.Invoke();
                return true;
            }

            // PgUp/PgDn scroll the transcript even though focus lives in the input field.
            if (key == Key.PageUp || key == Key.PageDown)
            {
                return Narration.NewKeyDownEvent(key);
            }

            return base.OnKeyDown(key);
        }

        private void OnInputAccepting(object? sender, CommandEventArgs e)
        {
            // Mark handled either way, so Enter never propagates up and triggers a default
            // "accept" on the window itself.
            e.Handled = true;

            var text = _input.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                return;
            }

            // Enter is the field's before it is the window's, so the suggestions have to take
            // their turn at it here. A half-typed name is completed rather than run - /ch is not
            // a command, and running it would only produce an error the player can see coming.
            //
            // A line that already names a command is run instead. The test is whether the typed
            // word is a command at all, not whether it is the highlighted one: /inv is a command
            // in its own right and is also a prefix of /inventory, so asking only about the
            // highlight would answer no and turn a finished command into a completion.
            if (_suggestions.Visible
                && _suggestions.Selected is { } completion
                && PlayerCommands.Describing(text) is null)
            {
                Complete(completion);
                return;
            }

            // Asked before anything is cleared or echoed, so a refused line is left exactly as
            // the player typed it and Enter can simply be pressed again once it will be taken -
            // suggestions and all.
            if (CanSubmit is { } canSubmit && !canSubmit(text))
            {
                return;
            }

            _input.Text = string.Empty;
            HideSuggestions();

            Narration.AddBlankLine();
            Narration.AddLine(StyledLine.FromText($"> {text}", TextRole.Command));
            Narration.AddBlankLine();
            Narration.ScrollToBottom();

            CommandEntered?.Invoke(text);
        }

        /// <summary>
        /// Puts the list in step with what has been typed, after every keystroke.
        /// <para>
        /// Nothing here looks at whether a turn is in flight. The input field stays live while the
        /// narrator speaks so the player's own commands still work, and a list of the commands
        /// that still work is exactly what the suggestions are.
        /// </para>
        /// </summary>
        private void RefreshSuggestions()
        {
            var text = _input.Text ?? string.Empty;

            // Two questions in order: which command might this be, and failing that, which command
            // is it already. The second is what keeps the strip up once the name is settled - it
            // goes away when the line is submitted or the slash is deleted, not when the player
            // reaches the argument.
            var matches = PlayerCommands.Matching(text);
            var choosing = matches.Count > 0;

            if (!choosing)
            {
                matches = PlayerCommands.Describing(text) is { } named ? [named] : [];
            }

            if (matches.Count == 0)
            {
                HideSuggestions();
                return;
            }

            var height = Math.Min(matches.Count, MaxSuggestionRows);

            // Set before the rows, which is where the cursor is put back to the top.
            _suggestions.IsChoosing = choosing;
            _suggestions.Suggestions = matches;
            _suggestions.Height = height;

            // Measured up from the input frame rather than down from the top, so the list always
            // sits against the box it is about to fill and grows away from it.
            _suggestions.Y = Pos.Bottom(Narration) - height;
            _suggestions.Visible = true;
            _suggestions.SetNeedsDraw();
        }

        /// <summary>Puts the list away, and gives back the rows of transcript it was covering.</summary>
        private void HideSuggestions()
        {
            if (!_suggestions.Visible)
            {
                return;
            }

            _suggestions.Visible = false;
            _suggestions.Suggestions = [];

            // The transcript underneath has not changed, so nothing else would think to redraw it,
            // and the rows the list was sitting on would keep its text.
            Narration.SetNeedsDraw();
        }

        /// <summary>
        /// Takes a suggestion: the typed word is replaced outright by the command it stood for,
        /// with a trailing space so the caret is where an argument would go.
        /// <para>
        /// The list does not close, it settles: the space ends the choosing, and what is left is
        /// the one command, still saying what it takes while the argument is typed.
        /// </para>
        /// </summary>
        private void Complete(PlayerCommandInfo command)
        {
            _input.Text = $"/{command.Name} ";
            _input.InsertionPoint = _input.Text.Length;

            // Belt and braces: assigning the text raises the change that refreshes the strip, but
            // this must not depend on that having happened.
            RefreshSuggestions();
        }
    }
}
