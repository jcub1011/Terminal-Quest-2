using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The title label above the command input field. Displays "Command" when idle,
    /// and animates a gliding fantasy serpent across the row when the narrator is speaking.
    /// </summary>
    internal sealed class CommandTitleView : ThemedView
    {
        private const string IdleTitle = "Command";
        private const string Prefix = "Command ";
        private const string ThinkingText = "narrator thinking...";
        private const string SerpentHead = "⪢";

        private bool _isBusy;
        private int _animationStep;
        private CancellationTokenSource? _animationCts;
        private readonly object _animationGate = new();

        public CommandTitleView()
        {
            CanFocus = false;
            Height = 1;
            SetScheme(Theme.CreateScheme());
        }

        /// <summary>
        /// Action used to marshal animation frame updates onto the UI thread.
        /// </summary>
        public Action<Action>? AppInvoke { get; set; }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                if (_isBusy == value)
                {
                    return;
                }

                _isBusy = value;
                if (value)
                {
                    StartAnimation();
                }
                else
                {
                    StopAnimation();
                }

                SetNeedsDraw();
            }
        }

        internal int AnimationStep => _animationStep;

        internal void TickAnimation()
        {
            if (!_isBusy)
            {
                return;
            }

            _animationStep++;
            SetNeedsDraw();
        }

        private void StartAnimation()
        {
            lock (_animationGate)
            {
                StopAnimation();

                _animationStep = 0;
                var cts = new CancellationTokenSource();
                _animationCts = cts;
                var token = cts.Token;

                _ = Task.Run(async () =>
                {
                    using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
                    try
                    {
                        while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                        {
                            if (token.IsCancellationRequested)
                            {
                                break;
                            }

                            var invoke = AppInvoke ?? (App is { } app ? app.Invoke : null);
                            if (invoke is { })
                            {
                                invoke(TickAnimation);
                            }
                            else
                            {
                                TickAnimation();
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                    }
                }, token);
            }
        }

        private void StopAnimation()
        {
            lock (_animationGate)
            {
                if (_animationCts is { } cts)
                {
                    cts.Cancel();
                    cts.Dispose();
                    _animationCts = null;
                }

                _animationStep = 0;
            }
        }

        private string? _notice;

        public string? Notice
        {
            get => _notice;
            set
            {
                if (string.Equals(_notice, value, StringComparison.Ordinal))
                {
                    return;
                }

                _notice = value;
                SetNeedsDraw();
            }
        }

        /// <summary>
        /// Builds the single-row line for the given horizontal animation step and available width.
        /// </summary>
        internal static StyledLine BuildLine(bool isBusy, int step, int width, string? notice = null)
        {
            var line = new StyledLine();
            if (width <= 0)
            {
                return line;
            }

            if (notice is { Length: > 0 })
            {
                var text = width >= notice.Length ? notice : notice[..width];
                line.Append(text, TextRole.Command);
                if (width > text.Length)
                {
                    line.Append(new string(' ', width - text.Length), TextRole.Normal);
                }

                return line;
            }

            if (!isBusy)
            {
                var text = width >= IdleTitle.Length ? IdleTitle : IdleTitle[..width];
                line.Append(text, TextRole.Command);
                if (width > text.Length)
                {
                    line.Append(new string(' ', width - text.Length), TextRole.Normal);
                }

                return line;
            }

            if (width <= Prefix.Length)
            {
                var text = width >= IdleTitle.Length ? IdleTitle : IdleTitle[..width];
                line.Append(text, TextRole.Command);
                if (width > text.Length)
                {
                    line.Append(new string(' ', width - text.Length), TextRole.Normal);
                }

                return line;
            }

            line.Append(IdleTitle, TextRole.Command);
            line.Append(" ", TextRole.Normal);

            var laneWidth = width - Prefix.Length;
            var safeStep = Math.Abs(step);
            var headCol = safeStep % laneWidth;
            var serpentBodyLen = Math.Min(12, Math.Max(1, laneWidth - 1));

            var displayText = ThinkingText;
            if (laneWidth < displayText.Length)
            {
                displayText = laneWidth >= 11 ? "thinking..." : "...";
            }

            var textOffset = Math.Max(0, (laneWidth - displayText.Length) / 2);
            var textStart = Prefix.Length + textOffset;
            var textEnd = textStart + Math.Min(displayText.Length, laneWidth - textOffset);

            TextRole GetRole(int relCol, int absCol)
            {
                if (relCol == headCol)
                {
                    return TextRole.Item;
                }

                var dist = (headCol - relCol + laneWidth) % laneWidth;
                if (dist >= 1 && dist <= serpentBodyLen)
                {
                    return TextRole.Place;
                }

                if (absCol >= textStart && absCol < textEnd)
                {
                    return TextRole.Speech;
                }

                return TextRole.Normal;
            }

            char GetChar(int relCol, int absCol)
            {
                if (relCol == headCol)
                {
                    return SerpentHead[0];
                }

                var dist = (headCol - relCol + laneWidth) % laneWidth;
                if (dist >= 1 && dist <= serpentBodyLen)
                {
                    return (absCol % 2 == 0) ? '~' : '≈';
                }

                if (absCol >= textStart && absCol < textEnd)
                {
                    return displayText[absCol - textStart];
                }

                return ' ';
            }

            var relIndex = 0;
            while (relIndex < laneWidth)
            {
                var absIndex = Prefix.Length + relIndex;
                var role = GetRole(relIndex, absIndex);
                var runEnd = relIndex + 1;
                while (runEnd < laneWidth && GetRole(runEnd, Prefix.Length + runEnd) == role)
                {
                    runEnd++;
                }

                var count = runEnd - relIndex;
                var chars = new char[count];
                for (var i = 0; i < count; i++)
                {
                    chars[i] = GetChar(relIndex + i, absIndex + i);
                }

                line.Append(new string(chars), role);
                relIndex = runEnd;
            }

            return line;
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            var height = Viewport.Height;

            if (width <= 0 || height <= 0)
            {
                return true;
            }

            Move(0, 0);
            var line = BuildLine(_isBusy, _animationStep, width, _notice);
            foreach (var span in line.Spans)
            {
                SetRole(span.Role);
                AddStr(span.Text);
            }

            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopAnimation();
            }

            base.Dispose(disposing);
        }
    }
}
