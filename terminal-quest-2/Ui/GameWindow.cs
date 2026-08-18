using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The full-screen layout: a title row across the top, narration on the left, status on the
    /// right, input pinned to the bottom. Owns no game logic - it raises
    /// <see cref="CommandEntered"/> and lets the host decide what a command means.
    /// </summary>
    internal sealed class GameWindow : Window
    {
        /// <summary>
        /// Columns given to the status pane, one of which is the gutter between it and the
        /// narration. Wide enough that most item lines fit on a single row now that the pane wraps
        /// rather than truncates.
        /// </summary>
        private const int StatusWidth = 28;

        private const int CommandAreaHeight = 3;
        private const int TitleHeight = 1;

        private const string IdleTitle = "Command";
        private const string BusyTitle = "Command - narrator speaking";

        /// <summary>
        /// How much of the transcript the suggestions may cover. A bare <c>/</c> matches every
        /// command there is, and burying the last thing the narrator said under the whole list is
        /// a worse trade than scrolling the few rows that do not fit.
        /// </summary>
        private const int MaxSuggestionRows = 8;

        private readonly TextField _input;
        private readonly Label _commandTitleLabel;
        private readonly CommandSuggestionView _suggestions;

        public GameWindow(GameState state)
        {
            State = state;

            // No border title: the title row below draws it, because a border title takes a single
            // colour and the place name has to stay green while the rest of the row does not.
            BorderStyle = LineStyle.Rounded;

            // Said out loud now that the mouse is reported to the application: this screen fills the
            // terminal, and dragging its border about would only ever be an accident.
            Arrangement = ViewArrangement.Fixed;

            // Applied to the window so every stock control inside it (the input field, its frame,
            // the borders) inherits a transparent background instead of Terminal.Gui's defaults.
            SetScheme(Theme.CreateScheme());

            TitleBar = new TitleBarView(state)
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = TitleHeight,
            };

            Narration = new NarrationView
            {
                X = 0,
                Y = Pos.Bottom(TitleBar),
                Width = Dim.Fill() - StatusWidth,
                Height = Dim.Fill() - CommandAreaHeight,
            };

            Options = new OptionsView
            {
                X = 0,
                Y = Pos.Bottom(Narration),
                Width = Dim.Fill() - StatusWidth,
                Height = 0,
                Visible = false,
            };

            Status = new StatusView(state)
            {
                X = Pos.Right(Narration) + 1,
                Y = Pos.Bottom(TitleBar),
                Width = StatusWidth - 1,
                Height = Dim.Fill() - CommandAreaHeight,
            };

            _commandTitleLabel = new Label
            {
                Text = IdleTitle,
                X = 0,
                Y = Pos.AnchorEnd(2),
                Width = Dim.Fill(),
                Height = 1,
            };
            _commandTitleLabel.SetScheme(Theme.CreateScheme());

            _input = new TextField
            {
                X = 0,
                Y = Pos.AnchorEnd(1),
                Width = Dim.Fill(),
                Height = 1,
            };
            _input.SetScheme(Theme.CreateScheme());

            // Sized and placed only when there is something to show, since both depend on how many
            // commands the half-typed word still matches.
            _suggestions = new CommandSuggestionView
            {
                X = 0,
                Width = Dim.Fill() - StatusWidth,
                Visible = false,
            };

            _input.Accepting += OnInputAccepting;
            _input.ValueChanged += (_, _) =>
            {
                RefreshSuggestions();
                SyncOptionHighlight();
            };

            Options.OptionClicked += opt =>
            {
                _input.Text = opt.Text;
                _input.InsertionPoint = _input.Text.Length;
                _input.SetFocus();
                SyncOptionHighlight();
            };

            Options.OptionDoubleClicked += opt =>
            {
                _input.Text = opt.Text;
                _input.InsertionPoint = _input.Text.Length;
                SyncOptionHighlight();
                SubmitInput();
            };

            Narration.EntityClicked += OnEntityClicked;
            Status.EntityClicked += OnEntityClicked;

            // The suggestions are added last so they draw over the foot of the transcript, the
            // same layering the settings screen uses to drop its editor onto a drawn row.
            Add(TitleBar, Narration, Status, Options, _commandTitleLabel, _input, _suggestions);
        }

        public TitleBarView TitleBar { get; }

        public NarrationView Narration { get; }

        public OptionsView Options { get; }

        public StatusView Status { get; }

        public GameState State { get; }

        /// <summary>
        /// The save store backing the session, used for command argument completions.
        /// </summary>
        public SaveStore? Store { get; init; }

        /// <summary>
        /// What Ctrl+G hands the command line to, or null where there is nothing to hand it to.
        /// </summary>
        public ExternalEditor? Editor { get; init; }

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

                // The wait is shown in the transcript, where the narration will land, rather than
                // off to the side in the status pane.
                Narration.IsWaiting = value;

                if (value)
                {
                    Options.HighlightedOption = null;
                }
                else
                {
                    SyncOptionHighlight();
                }

                _commandTitleLabel.Text = value ? BusyTitle : IdleTitle;
                _commandTitleLabel.SetNeedsDraw();

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
        /// Opens a file in the player's editor, saying so where this screen says things.
        /// </summary>
        /// <param name="path">The file to edit, in place.</param>
        /// <param name="saved">Called once the editor has closed having actually changed it.</param>
        /// <returns>False when there is no editor to open it with, which is the caller's to report.</returns>
        /// <remarks>
        /// A method rather than exposing the notice, so <see cref="ShowEditingNotice"/> stays this
        /// window's own business - the command box's title is the only place there is to say anything
        /// here. What is being edited and what to do afterwards remain the host's.
        /// </remarks>
        public bool BeginExternalEdit(string path, Action? saved = null)
        {
            if (Editor is not { } editor)
            {
                return false;
            }

            // For the reason Ctrl+G hides them: the suggestions are for a command being typed, and
            // they would be drawing over the notice.
            HideSuggestions();

            return editor.TryBeginFile(path, ShowEditingNotice, saved);
        }

        /// <summary>
        /// Redraws everything fed by <see cref="GameState"/>, after the host has re-read the save.
        /// One call rather than two, so a new field cannot be added to the state and then quietly
        /// left stale on screen.
        /// </summary>
        public void RefreshState()
        {
            TitleBar.Refresh();
            Status.Refresh();
        }

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

        /// <summary>
        /// Says where the command has gone while an external editor holds it.
        /// <para>
        /// The title of the box the text would be in, because that is the only place this screen has
        /// to say anything - there is no hint line here, and the transcript belongs to the narrator.
        /// Null puts the ordinary title back, whichever of the two it is by then.
        /// </para>
        /// </summary>
        private void ShowEditingNotice(string? notice)
        {
            _commandTitleLabel.Text = notice ?? (IsBusy ? BusyTitle : IdleTitle);
            _commandTitleLabel.SetNeedsDraw();

            // The command box stops taking text for as long as an editor is open, and this is the only
            // place that can be said for both kinds of edit. Ctrl+G's own edit is of this field and the
            // editor marks it read-only itself; a file edit has no field to mark, so without this the
            // player could type a line - or press Enter and start a turn - while another program holds
            // the save's prompt and the session is a keystroke away from ending.
            _input.ReadOnly = Editor is { IsBusy: true };
        }

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
            // Nothing else happens while the line is in another program. Esc and Ctrl+Q would leave
            // the save, taking a command the player is still writing with them.
            if (Editor is { IsBusy: true })
            {
                return true;
            }

            if (key == ExternalEditor.RequestKey && Editor is { } editor)
            {
                // The suggestions are for a command being typed, and it is not being typed here any
                // more. They would also be drawing over the notice.
                HideSuggestions();
                return editor.TryBegin(_input, ShowEditingNotice);
            }

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

            if (!_suggestions.Visible && !IsBusy && (key == Key.CursorUp || key == Key.CursorDown))
            {
                if (NavigateOptions(key == Key.CursorUp ? -1 : 1))
                {
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

            // PgUp/PgDn scroll the transcript even though focus lives in the input field, and
            // Shift+PgDn returns to the narrator from wherever the player has read back to. All
            // three only arrive because a single-line TextField implements no paging command of its
            // own; End and Ctrl+End, which would be the obvious spelling of that last one, are the
            // field's caret keys and never get this far.
            if (key == Key.PageUp || key == Key.PageDown || key == Key.PageDown.WithShift)
            {
                return Narration.NewKeyDownEvent(key);
            }

            return base.OnKeyDown(key);
        }

        /// <summary>
        /// Sends the wheel to the transcript wherever the pointer happens to be.
        /// <para>
        /// Terminal.Gui offers a mouse event to the view under the pointer first, so the transcript
        /// already handles its own wheel. This catches the rest of the window - the status pane, the
        /// command box, the border - because the transcript is the only thing here that scrolls and
        /// the pointer is not what the player is aiming with.
        /// </para>
        /// </summary>
        protected override bool OnMouseEvent(Mouse mouse)
        {
            ArgumentNullException.ThrowIfNull(mouse);

            if (mouse.Flags.HasFlag(MouseFlags.WheeledUp) || mouse.Flags.HasFlag(MouseFlags.WheeledDown))
            {
                // Null means the transcript did not claim it, which for the wheel means there was
                // nowhere left to scroll. Either way the event is spent - nothing else here wants it.
                return Narration.NewMouseEvent(mouse) ?? false;
            }

            return base.OnMouseEvent(mouse);
        }

        private void OnInputAccepting(object? sender, CommandEventArgs e)
        {
            // Mark handled either way, so Enter never propagates up and triggers a default
            // "accept" on the window itself.
            e.Handled = true;
            SubmitInput();
        }

        /// <summary>
        /// Submits the current command line text to the game session.
        /// </summary>
        public void SubmitInput()
        {
            // Nothing is submitted while an editor is open. ReadOnly stops the field taking text but
            // not taking Enter, and OnKeyDown never sees the key because the field handles it first -
            // so this is the only place the rule can be applied. Without it, Enter on the line that
            // was already in the box would start a turn against a scene the player is elsewhere
            // rewriting the instructions for.
            if (Editor is { IsBusy: true })
            {
                return;
            }

            // Through the editor rather than off the field, so a command written in Notepad reaches
            // the narrator with its line breaks intact rather than as the one line shown here.
            var text = (Editor?.Resolve(_input) ?? _input.Text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return;
            }

            // Enter is the field's before it is the window's, so the suggestions have to take
            // their turn at it here. A half-typed command or incomplete argument is completed
            // rather than run if the current text is not already a valid command invocation.
            if (_suggestions.Visible
                && _suggestions.Selected is { } completion
                && ShouldCompleteOnEnter(text))
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

            // The line has been taken, so anything an external edit left standing for it is spent.
            // Left in place it would come back to life the moment the player typed that same line
            // out by hand, and be submitted as the longer text it once stood for.
            Editor?.Forget(_input);

            HideSuggestions();
            Options.HighlightedOption = null;

            if (!PlayerCommands.IsCommand(text))
            {
                ClearOptions();
            }

            Narration.AddBlankLine();
            Narration.AddLine(StyledLine.FromText($"> {text}", TextRole.Command));
            Narration.ScrollToBottom();

            CommandEntered?.Invoke(text);
        }

        /// <summary>
        /// Decides whether pressing Enter on the current text should take the highlighted suggestion
        /// rather than executing the line.
        /// </summary>
        private bool ShouldCompleteOnEnter(string text)
        {
            if (!PlayerCommands.IsCommand(text))
            {
                return false;
            }

            var parts = text.Split(' ', 2, StringSplitOptions.TrimEntries);
            var commandName = parts.Length > 0 ? parts[0].TrimStart('/').ToLowerInvariant() : string.Empty;
            var argument = parts.Length > 1 ? parts[1] : string.Empty;

            // If the command name itself is incomplete (e.g. "/ch"), complete it.
            if (PlayerCommands.Describing(text) is null)
            {
                return true;
            }

            // If an argument prefix was typed (e.g. "/character Ro" or "/delete Sav"), check if it is
            // already an exact match for a known entity. If not, complete it.
            if (argument.Length > 0 && Store is { } store)
            {
                switch (commandName)
                {
                    case "character":
                    case "characters":
                    case "who":
                        return SaveStore.FindCharacter(store.ReadCharacters(), argument) is null;

                    case "location":
                    case "locations":
                    case "where":
                        return SaveStore.FindLocation(store.ReadLocations(), argument) is null;

                    case "delete":
                        return !SavePaths.List().Any(s => SaveStore.Matches(s.Name, argument) && !SaveStore.Matches(s.Name, store.Name));
                }
            }

            return false;
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

            var (suggestions, choosing) = PlayerCommands.GetSuggestions(text, Store);

            if (suggestions.Count == 0)
            {
                HideSuggestions();
                return;
            }

            var height = Math.Min(suggestions.Count, MaxSuggestionRows);

            // Set before the rows, which is where the cursor is put back to the top.
            _suggestions.IsChoosing = choosing;
            _suggestions.Suggestions = suggestions;
            _suggestions.Height = height;

            // Measured up from the input frame rather than down from the top, so the list always
            // sits against the box it is about to fill and grows away from it.
            _suggestions.Y = Pos.AnchorEnd(CommandAreaHeight) - height;
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
        /// Takes a suggestion: the input is replaced by the suggestion text.
        /// </summary>
        private void Complete(SuggestionItem item)
        {
            if (string.IsNullOrEmpty(item.InsertText))
            {
                return;
            }

            _input.Text = item.InsertText;
            _input.InsertionPoint = _input.Text.Length;

            // Belt and braces: assigning the text raises the change that refreshes the strip, but
            // this must not depend on that having happened.
            RefreshSuggestions();
        }

        /// <summary>
        /// Updates the options displayed below the transcript.
        /// </summary>
        public void SetOptions(IReadOnlyList<string> options)
        {
            Options.SetOptions(options);
            UpdateOptionsLayout();
        }

        /// <summary>
        /// Sets the active narration options directly.
        /// </summary>
        public void SetOptions(IReadOnlyList<NarrationOption> options)
        {
            Options.SetOptions(options);
            UpdateOptionsLayout();
        }

        /// <summary>
        /// Clears all active options and collapses the options pane.
        /// </summary>
        public void ClearOptions()
        {
            Options.ClearOptions();
            UpdateOptionsLayout();
        }

        public const int OptionGapHeight = 1;

        public void UpdateOptionsLayout()
        {
            var width = Viewport.Width > 0 ? Math.Max(1, Viewport.Width - StatusWidth) : (Narration.Viewport.Width > 0 ? Narration.Viewport.Width : 80);
            var requiredHeight = Options.CalculateRequiredHeight(width);

            if (requiredHeight > 0 && Options.Options.Count > 0)
            {
                Narration.Height = Dim.Fill() - CommandAreaHeight - requiredHeight - OptionGapHeight;
                Options.Y = Pos.Bottom(Narration) + OptionGapHeight;
                Options.Height = requiredHeight;
                Options.Visible = true;
            }
            else
            {
                Narration.Height = Dim.Fill() - CommandAreaHeight;
                Options.Height = 0;
                Options.Visible = false;
            }

            SetNeedsDraw();
        }

        /// <summary>
        /// The active choices currently presented to the player.
        /// </summary>
        public IReadOnlyList<NarrationOption> GetActiveOptions() => Options.Options;

        /// <summary>
        /// Moves the selection through the choices currently offered by the narrator,
        /// populating the input field with the chosen option text and highlighting it in the options pane.
        /// </summary>
        private bool NavigateOptions(int delta)
        {
            var options = Options.Options;
            if (options.Count == 0)
            {
                return false;
            }

            var text = _input.Text?.Trim() ?? string.Empty;
            var currentIndex = -1;

            if (Options.HighlightedOption is { } highlighted && highlighted >= 1 && highlighted <= options.Count)
            {
                currentIndex = highlighted - 1;
            }
            else
            {
                for (var i = 0; i < options.Count; i++)
                {
                    if (string.Equals(options[i].Text, text, StringComparison.OrdinalIgnoreCase)
                        || options[i].Number.ToString() == text)
                    {
                        currentIndex = i;
                        break;
                    }
                }
            }

            int nextIndex;
            if (currentIndex < 0)
            {
                nextIndex = delta < 0 ? options.Count - 1 : 0;
            }
            else
            {
                nextIndex = Math.Clamp(currentIndex + delta, 0, options.Count - 1);
            }

            var selected = options[nextIndex];
            _input.Text = selected.Text;
            _input.InsertionPoint = _input.Text.Length;
            Options.HighlightedOption = selected.Number;
            return true;
        }

        /// <summary>
        /// Keeps the highlighted choice in step with whatever the player types into the command box.
        /// A matching option text or number highlights that option; anything else clears the highlight.
        /// </summary>
        private void SyncOptionHighlight()
        {
            if (IsBusy)
            {
                Options.HighlightedOption = null;
                return;
            }

            var text = _input.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                Options.HighlightedOption = null;
                return;
            }

            var options = Options.Options;
            var match = options.FirstOrDefault(o =>
                string.Equals(o.Text, text, StringComparison.OrdinalIgnoreCase)
                || (int.TryParse(text, out var parsed) && o.Number == parsed));

            Options.HighlightedOption = match?.Number;
        }

        private void OnEntityClicked(string entityId)
        {
            if (App is null || Store is null)
            {
                return;
            }

            EntityDetailsDialog.Show(App, Store, entityId);
        }
    }
}
