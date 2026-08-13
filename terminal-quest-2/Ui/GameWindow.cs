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

        private readonly TextField _input;

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

            var inputFrame = new FrameView
            {
                Title = "command",
                X = 0,
                Y = Pos.Bottom(Narration),
                Width = Dim.Fill(),
                Height = InputHeight,
                BorderStyle = LineStyle.Rounded,
            };
            inputFrame.Add(_input);

            _input.Accepting += OnInputAccepting;

            Add(Narration, Status, inputFrame);
        }

        public NarrationView Narration { get; }

        public StatusView Status { get; }

        public GameState State { get; }

        /// <summary>Raised on the UI thread when the player submits a non-empty command.</summary>
        public event Action<string>? CommandEntered;

        /// <summary>
        /// Blocks input while a turn is in flight, so a second command cannot be submitted into
        /// a session that is still streaming a reply.
        /// </summary>
        public bool InputEnabled
        {
            get => _input.Enabled;
            set
            {
                _input.Enabled = value;
                State.IsBusy = !value;
                Status.SetNeedsDraw();

                if (value)
                {
                    _input.SetFocus();
                }
            }
        }

        /// <summary>Raised when the player asks to quit. The host owns the actual shutdown.</summary>
        public event Action? QuitRequested;

        public void FocusInput() => _input.SetFocus();

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Esc || key == Key.Q.WithCtrl)
            {
                QuitRequested?.Invoke();
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

            _input.Text = string.Empty;

            Narration.AddBlankLine();
            Narration.AddLine(StyledLine.FromText($"> {text}", TextRole.Command));
            Narration.AddBlankLine();
            Narration.ScrollToBottom();

            CommandEntered?.Invoke(text);
        }
    }
}
