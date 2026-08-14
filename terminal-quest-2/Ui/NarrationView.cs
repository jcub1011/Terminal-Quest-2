using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The scrolling transcript pane.
    /// <para>
    /// Terminal.Gui's <c>TextView</c> applies a single scheme to the whole control and cannot
    /// colour individual words, so this draws itself: it keeps the transcript as unwrapped
    /// <see cref="StyledLine"/> paragraphs and emits them per-span via
    /// <see cref="View.SetAttribute"/> / <see cref="View.AddStr(string)"/>.
    /// </para>
    /// <para>
    /// Wrapping is cached in two parts. Committed paragraphs are wrapped once and only re-wrapped
    /// when the terminal width changes; the in-progress paragraph is re-wrapped on every delta.
    /// Without that split, streaming would re-wrap the entire transcript per token.
    /// </para>
    /// </summary>
    internal sealed class NarrationView : ThemedView, INarrationSink
    {
        /// <summary>Rows the wheel moves per notch.</summary>
        private const int WheelRows = 4;

        /// <summary>
        /// Stands in for the narration until the first token of it arrives, in the place that
        /// narration will occupy. It belongs here rather than in the status pane: the player is
        /// waiting on prose, and this is where the prose appears.
        /// </summary>
        private static readonly StyledLine WaitingRow = StyledLine.FromText("...thinking", TextRole.Speech);

        private readonly List<StyledLine> _committed = [];
        private readonly MarkupParser _parser = new();

        private List<StyledLine> _committedRows = [];
        private List<StyledLine> _currentRows = [];

        private StyledLine? _current;

        /// <summary>Width the caches were built for; -1 forces a rebuild.</summary>
        private int _wrapWidth = -1;

        private bool _stickToBottom = true;

        private bool _isWaiting;

        /// <summary>
        /// Whether a narrator turn is in flight with nothing streamed back yet. While it is, a
        /// placeholder row sits at the end of the transcript; the first delta replaces it in the
        /// same spot, because by then <see cref="_currentRows"/> is no longer empty.
        /// </summary>
        public bool IsWaiting
        {
            get => _isWaiting;
            set
            {
                if (_isWaiting == value)
                {
                    return;
                }

                _isWaiting = value;
                AfterContentChanged();
            }
        }

        /// <summary>Whether the placeholder is currently one of the rows.</summary>
        private bool ShowWaiting => _isWaiting && _currentRows.Count == 0;

        /// <summary>
        /// Every row the view can scroll through, the placeholder included, so scrolling and
        /// clamping need no special case for it.
        /// </summary>
        private int TotalRows => _committedRows.Count + _currentRows.Count + (ShowWaiting ? 1 : 0);

        /// <summary>
        /// The offset that rests the last row at the foot of the pane - the furthest this can
        /// scroll. The base class clamps to the same bound whenever the offset is assigned, but it
        /// has to be named here too: it is also the test for whether the view is following the
        /// stream, and the base class does not revisit the offset when the row count changes
        /// underneath it.
        /// </summary>
        private int BottomOffset => Math.Max(0, TotalRows - Viewport.Height);

        /// <summary>Appends streamed narration. Safe to call with partial markup tags.</summary>
        public void AppendDelta(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _current ??= new StyledLine();
            _parser.Append(text, _current);

            // A delta that was nothing but the start of a markup tag has produced no visible text
            // yet, and must not yield a blank row - that row would replace the waiting placeholder
            // with nothing at all.
            _currentRows = _current.Length > 0 ? Wrap(_current.Spans, _wrapWidth) : [];
            AfterContentChanged();
        }

        /// <summary>Ends the in-progress paragraph and clears parser state.</summary>
        public void CommitBlock()
        {
            if (_current is { Length: > 0 })
            {
                _committed.Add(_current);
                _committedRows.AddRange(Wrap(_current.Spans, _wrapWidth));
            }

            _current = null;
            _currentRows = [];
            _parser.Reset();

            // The paragraph being waited for has arrived, so the placeholder is spent. Cleared here
            // rather than left to the host, which flips IsBusy back a moment later on another
            // marshalled call - long enough for the placeholder to be drawn again under the finished
            // prose in between.
            _isWaiting = false;

            AfterContentChanged();
        }

        /// <summary>Adds a complete paragraph that is not part of the narration stream.</summary>
        public void AddLine(StyledLine line)
        {
            _committed.Add(line);
            _committedRows.AddRange(Wrap(line.Spans, _wrapWidth));
            AfterContentChanged();
        }

        public void AddLine(string text, TextRole role) => AddLine(StyledLine.FromText(text, role));

        /// <summary>Inserts a blank spacer row.</summary>
        public void AddBlankLine() => AddLine(new StyledLine());

        public void ScrollToBottom()
        {
            _stickToBottom = true;
            SyncContentSize();
            SetNeedsDraw();
        }

        /// <summary>
        /// Scrolls by whole rows. The scroll offset is <see cref="View.Viewport"/>'s, so the base
        /// class does the clamping; this only has to keep the follow-the-stream flag in step, so
        /// that wheeling up during a turn detaches from the stream and wheeling back down rejoins
        /// it.
        /// </summary>
        private void Scroll(int rows)
        {
            ScrollVertical(rows);
            _stickToBottom = Viewport.Y >= BottomOffset;
            SetNeedsDraw();
        }

        /// <summary>
        /// Publishes the row count as the view's content height, which is what lets the base class
        /// own the scroll offset, and then puts the offset where it belongs.
        /// <para>
        /// The base class clamps the offset when it is assigned but never revisits it on its own,
        /// so the offset has to be pulled back into range here: re-wrapping at a wider terminal
        /// yields fewer rows and can leave it stranded past the end, which would draw the pane with
        /// blank rows below the last line. Assigned only when it actually moves, so that a redraw
        /// that changed nothing stays free.
        /// </para>
        /// </summary>
        private void SyncContentSize()
        {
            SetContentHeight(TotalRows);

            var bottom = BottomOffset;
            var target = _stickToBottom ? bottom : Math.Min(Viewport.Y, bottom);

            if (target != Viewport.Y)
            {
                ScrollVertical(target - Viewport.Y);
            }
        }

        private void AfterContentChanged()
        {
            SyncContentSize();
            SetNeedsDraw();
        }

        protected override bool OnKeyDown(Key key)
        {
            var page = Math.Max(1, Viewport.Height - 1);

            if (key == Key.PageUp)
            {
                Scroll(-page);
                return true;
            }

            if (key == Key.PageDown)
            {
                Scroll(page);
                return true;
            }

            return false;
        }

        /// <summary>Scrolls on the wheel. <see cref="Scroll"/> clamps and tracks the stream.</summary>
        protected override bool OnMouseEvent(Mouse mouse)
        {
            ArgumentNullException.ThrowIfNull(mouse);

            if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
            {
                Scroll(-WheelRows);
                return true;
            }

            if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
            {
                Scroll(WheelRows);
                return true;
            }

            return false;
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            var height = Viewport.Height;

            if (width <= 0 || height <= 0)
            {
                return true;
            }

            // Terminal was resized: everything must be re-wrapped to the new width.
            if (width != _wrapWidth)
            {
                _wrapWidth = width;
                RebuildAllRows();
            }

            BeginPaint(width, height);

            // The pane may have been resized since the row count last changed, which moves the
            // bottom without going through any of the callers that would have re-synced it.
            SyncContentSize();

            for (var y = 0; y < height; y++)
            {
                var index = Viewport.Y + y;
                if (index >= TotalRows)
                {
                    break;
                }

                var row = RowAt(index);

                if (row.Spans.Count == 0)
                {
                    continue;
                }

                Move(0, y);
                foreach (var span in row.Spans)
                {
                    SetRole(span.Role);
                    AddStr(span.Text);
                }
            }

            return true;
        }

        /// <summary>
        /// The row at a scroll index: committed paragraphs, then the paragraph being streamed, then
        /// the waiting placeholder if it is showing.
        /// </summary>
        private StyledLine RowAt(int index)
        {
            if (index < _committedRows.Count)
            {
                return _committedRows[index];
            }

            var offset = index - _committedRows.Count;
            return offset < _currentRows.Count ? _currentRows[offset] : WaitingRow;
        }

        private void RebuildAllRows()
        {
            _committedRows = [];
            foreach (var line in _committed)
            {
                _committedRows.AddRange(Wrap(line.Spans, _wrapWidth));
            }

            _currentRows = _current is { Length: > 0 } ? Wrap(_current.Spans, _wrapWidth) : [];

            // The offset is left to the SyncContentSize call that follows every rebuild.
        }

        /// <summary>
        /// Greedy word wrap. Preserves span roles across the break, collapses the space that
        /// falls at a wrap point, and hard-breaks words longer than a full line.
        /// </summary>
        internal static List<StyledLine> Wrap(IReadOnlyList<StyledSpan> spans, int width)
        {
            var rows = new List<StyledLine>();
            if (width <= 0)
            {
                return rows;
            }

            var row = new StyledLine();
            var word = new List<(char Ch, TextRole Role)>();

            void CommitRow()
            {
                row.TrimEnd();
                rows.Add(row);
                row = new StyledLine();
            }

            void FlushWord()
            {
                if (word.Count == 0)
                {
                    return;
                }

                // A word wider than the pane can never fit; break it at the margin.
                while (word.Count > width)
                {
                    if (row.Length > 0)
                    {
                        CommitRow();
                    }

                    for (var i = 0; i < width; i++)
                    {
                        row.Append(word[i].Ch.ToString(), word[i].Role);
                    }

                    word.RemoveRange(0, width);
                    CommitRow();
                }

                if (row.Length > 0 && row.Length + word.Count > width)
                {
                    CommitRow();
                }

                foreach (var (ch, role) in word)
                {
                    row.Append(ch.ToString(), role);
                }

                word.Clear();
            }

            foreach (var span in spans)
            {
                foreach (var ch in span.Text)
                {
                    switch (ch)
                    {
                        case '\r':
                            break;

                        case '\n':
                            FlushWord();
                            CommitRow();
                            break;

                        case ' ':
                            FlushWord();
                            // Drop the space entirely if it lands at a wrap point, so wrapped
                            // rows do not start with stray indentation.
                            if (row.Length > 0 && row.Length < width)
                            {
                                row.Append(" ", span.Role);
                            }

                            break;

                        default:
                            word.Add((ch, span.Role));
                            break;
                    }
                }
            }

            FlushWord();

            if (row.Length > 0 || rows.Count == 0)
            {
                row.TrimEnd();
                rows.Add(row);
            }

            return rows;
        }
    }
}
