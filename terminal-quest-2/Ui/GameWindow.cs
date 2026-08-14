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

        private readonly TextField _input;
        private readonly FrameView _inputFrame;

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

            _input.Accepting += OnInputAccepting;

            Add(Narration, Status, _inputFrame);
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

        protected override bool OnKeyDown(Key key)
        {
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

            // Asked before anything is cleared or echoed, so a refused line is left exactly as
            // the player typed it and Enter can simply be pressed again once it will be taken.
            if (CanSubmit is { } canSubmit && !canSubmit(text))
            {
                return;
            }

            _input.Text = string.Empty;

            Narration.AddBlankLine();
            Narration.AddLine(StyledLine.FromText($"> {text}", TextRole.Command));
            Narration.AddBlankLine();
            Narration.ScrollToBottom();

            CommandEntered?.Invoke(text);
        }
    }
}
