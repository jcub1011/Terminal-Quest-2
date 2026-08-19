using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The scrolling transcript pane.
    /// <para>
    /// Draws itself: it keeps the transcript as unwrapped <see cref="StyledLine"/> paragraphs and
    /// emits them per-span via <see cref="View.SetAttribute"/> / <see cref="View.AddStr(string)"/>.
    /// </para>
    /// <para>
    /// <b>Not because <c>TextView</c> cannot colour words</b> - it can, and this comment used to say
    /// otherwise. In v2 a <c>TextView</c> holds <c>Cell</c>s that each carry their own
    /// <c>Attribute</c>, and <c>TextView.Load(List&lt;List&lt;Cell&gt;&gt;)</c> takes them. What it
    /// has no way to do is <em>append</em> coloured text: <c>Load</c> is the only door in, and it
    /// replaces the whole document, clears the undo history, re-wraps every line and calls
    /// <c>ResetPosition</c>. Streaming a token through it would re-wrap the entire transcript and
    /// throw the reader back to the top, once per token. There is no public API to add a run of a
    /// given colour at the end.
    /// </para>
    /// <para>
    /// So this stays a custom <see cref="View"/> - which is the library's own extension point for a
    /// control it does not have - while the game's <em>lists</em> are stock <c>ListView</c>s with a
    /// <see cref="StyledListSource{T}"/>, because <c>IListDataSource.Render</c> is exactly the
    /// per-row colour seam this pane lacks. If <c>TextView</c> ever grows a styled append, this
    /// should go.
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
        /// Shown on the last row while the player is reading back, and clickable to return. Worded
        /// as a way out rather than as a warning: nothing has gone wrong, there is simply more.
        /// </summary>
        private const string MoreBelow = " ▼ more below ";

        private readonly List<StyledLine> _committed = [];
        private readonly MarkupParser _parser = new();

        private List<StyledLine> _committedRows = [];
        private List<StyledLine> _currentRows = [];

        private StyledLine? _current;

        /// <summary>Width the caches were built for; -1 forces a rebuild.</summary>
        private int _wrapWidth = 80;

        private bool _stickToBottom = true;

        private bool _isWaiting;

        /// <summary>
        /// Raised when the player clicks on a rendered entity in the transcript.
        /// </summary>
        public event Action<string>? EntityClicked;

        internal IReadOnlyList<StyledLine> CommittedLines => _committed;

        /// <summary>
        /// Whether a narrator turn is in flight.
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

        /// <summary>
        /// Every row the view can scroll through.
        /// </summary>
        internal int TotalRows => _committedRows.Count + _currentRows.Count;

        /// <summary>
        /// The offset that rests the last row at the foot of the pane - the furthest this can
        /// scroll. The base class clamps to the same bound whenever the offset is assigned, but it
        /// has to be named here too: it is also the test for whether the view is following the
        /// stream, and the base class does not revisit the offset when the row count changes
        /// underneath it.
        /// </summary>
        private int BottomOffset => BottomOffsetFor(TotalRows, Viewport.Height);

        /// <summary>
        /// Whether any of the transcript sits below the foot of the pane.
        /// <para>
        /// Drives the marker drawn on the last row. A pane that has correctly stopped following the
        /// narrator is indistinguishable, from the player's side, from a game that has stopped
        /// working - so the one thing it must not do is go on silently.
        /// </para>
        /// </summary>
        private bool HasMoreBelow => Viewport.Y < BottomOffset;

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
                _parser.Reset(_current);
                _committed.Add(_current);
                _committedRows.AddRange(Wrap(_current.Spans, _wrapWidth));
            }
            else
            {
                _parser.Reset();
            }

            _current = null;
            _currentRows = [];

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

        /// <summary>Inserts a blank spacer row, collapsing duplicate adjacent blank rows.</summary>
        public void AddBlankLine()
        {
            if (_committed.Count > 0 && _committed[^1].Length == 0 && _current is null)
            {
                return;
            }

            AddLine(new StyledLine());
        }

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
            _stickToBottom = AtBottom(Viewport.Y, TotalRows, Viewport.Height);
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

            var target = NextOffset(Viewport.Y, TotalRows, Viewport.Height, _stickToBottom);

            if (target != Viewport.Y)
            {
                // Assigned rather than scrolled by a delta. ScrollVertical declines to do anything
                // at all when the content is exactly as tall as the pane, which is one of the very
                // cases this correction exists for - it would leave the offset stranded mid-way
                // through a transcript that now fits, drawing its last rows at the top of an
                // otherwise blank pane. Assigning goes through the base class's own clamp, which
                // bounds it to BottomOffset exactly as computed here.
                Viewport = Viewport with { Y = target };
            }

            // Recomputed from where the pane actually ended up, never carried forward. Growing the
            // terminal lowers the bottom, and a player who was detached above it can be left sitting
            // on the last row without ever having asked to rejoin; leaving the flag false there
            // would freeze the pane a screen short of the narrator for the rest of the session.
            _stickToBottom = AtBottom(Viewport.Y, TotalRows, Viewport.Height);
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

            // The one key that rejoins the narrator outright, however far back the player has read.
            //
            // Not End, and not Ctrl+End, though both are the obvious spelling: focus lives in the
            // command box, a TextField binds each of them to its own caret and is offered every key
            // first, so neither would ever reach this view. PageUp and PageDown arrive only because
            // a single-line field implements no paging command at all - and Shift+PageDown is the
            // rest of that same gap.
            if (key == Key.PageDown.WithShift)
            {
                ScrollToBottom();
                return true;
            }

            return false;
        }

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

            // Clicking the scrollbar seeks to the proportional offset
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked)
                && mouse.Position is { } clickPos
                && clickPos.X == Viewport.Width - 1
                && TotalRows > Viewport.Height
                && Viewport.Height > 1)
            {
                var maxOffset = TotalRows - Viewport.Height;
                var target = (int)Math.Round((double)clickPos.Y * maxOffset / (Viewport.Height - 1));
                target = Math.Clamp(target, 0, maxOffset);
                Viewport = Viewport with { Y = target };
                _stickToBottom = AtBottom(Viewport.Y, TotalRows, Viewport.Height);
                SetNeedsDraw();
                return true;
            }

            // Clicking the marker rejoins the narrator. The whole row is the target rather than the
            // glyphs alone: it is only offered while the marker is showing, and there is nothing
            // else a click on this pane could have been meant to do.
            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked)
                && HasMoreBelow
                && mouse.Position is { } at
                && at.Y == Viewport.Height - 1)
            {
                ScrollToBottom();
                return true;
            }

            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } pos)
            {
                var rowIndex = Viewport.Y + pos.Y;
                if (rowIndex >= 0 && rowIndex < TotalRows)
                {
                    var row = RowAt(rowIndex);
                    var col = 0;
                    foreach (var span in row.Spans)
                    {
                        var spanEnd = col + span.Text.Length;
                        if (pos.X >= col && pos.X < spanEnd)
                        {
                            if (span.EntityId is { Length: > 0 } entityId)
                            {
                                EntityClicked?.Invoke(entityId);
                                return true;
                            }
                            break;
                        }
                        col = spanEnd;
                    }
                }
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

            var padding = width >= 2 ? 1 : 0;
            var textWidth = Math.Max(1, width - 1 - padding);

            // Terminal was resized: everything must be re-wrapped to the new width.
            if (textWidth != _wrapWidth)
            {
                _wrapWidth = textWidth;
                RebuildAllRows();
            }

            // The pane may have been resized since the row count last changed, which moves the
            // bottom without going through any of the callers that would have re-synced it.
            SyncContentSize();

            var totalRows = TotalRows;
            var showScrollBar = totalRows > height;
            var maxOffset = Math.Max(0, totalRows - height);
            var thumbSize = 1;
            var thumbY = 0;
            if (showScrollBar)
            {
                thumbSize = Math.Max(1, (int)Math.Round((double)height * height / totalRows));
                thumbY = maxOffset > 0 ? (int)Math.Round((double)Viewport.Y * (height - thumbSize) / maxOffset) : 0;
                thumbY = Math.Clamp(thumbY, 0, height - thumbSize);
            }

            for (var y = 0; y < height; y++)
            {
                Move(0, y);
                var index = Viewport.Y + y;

                if (index < totalRows)
                {
                    var row = RowAt(index);
                    var drawn = 0;
                    foreach (var span in row.Spans)
                    {
                        if (drawn >= textWidth)
                        {
                            break;
                        }

                        var text = span.Text.Length > textWidth - drawn ? span.Text[..(textWidth - drawn)] : span.Text;
                        SetRole(span.Role);
                        AddStr(text);
                        drawn += text.Length;
                    }

                    if (drawn < textWidth)
                    {
                        SetRole(TextRole.Normal);
                        AddStr(Blank(textWidth - drawn));
                    }
                }
                else
                {
                    SetRole(TextRole.Normal);
                    AddStr(Blank(textWidth));
                }

                // 1 character padding between text/serpent and scroll bar
                if (padding > 0)
                {
                    SetRole(TextRole.Normal);
                    AddStr(" ");
                }

                // Draw scroll bar column at x = width - 1
                if (showScrollBar)
                {
                    if (y >= thumbY && y < thumbY + thumbSize)
                    {
                        SetRole(TextRole.Normal);
                        AddStr("█");
                    }
                    else
                    {
                        SetRole(TextRole.System);
                        AddStr("│");
                    }
                }
                else
                {
                    SetRole(TextRole.Normal);
                    AddStr(" ");
                }
            }

            DrawMoreBelowMarker(width, height);

            return true;
        }

        /// <summary>
        /// Says, on the last row of the pane, that the transcript carries on below it.
        /// <para>
        /// Right-aligned over the tail of whatever is drawn there, which is the one row least likely
        /// to be the one being read - and it costs those columns only for as long as the player is
        /// away from the foot. Drawn whenever anything is below the fold rather than only during a
        /// turn, so it serves the player who scrolled back between turns just as well.
        /// </para>
        /// </summary>
        private void DrawMoreBelowMarker(int width, int height)
        {
            var scrollBarGutter = TotalRows > height ? 1 : 0;
            if (!HasMoreBelow || width < MoreBelow.Length + scrollBarGutter)
            {
                return;
            }

            Move(width - scrollBarGutter - MoreBelow.Length, height - 1);
            SetRole(TextRole.System);
            AddStr(MoreBelow);
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
            return offset < _currentRows.Count ? _currentRows[offset] : new StyledLine();
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
        /// The offset that rests the last row at the foot of a pane <paramref name="viewportHeight"/>
        /// rows tall.
        /// </summary>
        /// <remarks>
        /// Zero for a pane with no height yet. A view that has never been laid out has no foot to be
        /// away from, and answering anything else here would let a transcript decide it had been
        /// abandoned by the player before it had been drawn once.
        /// </remarks>
        internal static int BottomOffsetFor(int totalRows, int viewportHeight) =>
            viewportHeight <= 0 ? 0 : Math.Max(0, totalRows - viewportHeight);

        /// <summary>
        /// Whether an offset has the last row on screen. This is the whole definition of "following
        /// the narrator": there is no separate intent to remember, only where the pane is sitting.
        /// </summary>
        internal static bool AtBottom(int offsetY, int totalRows, int viewportHeight) =>
            offsetY >= BottomOffsetFor(totalRows, viewportHeight);

        /// <summary>
        /// Where the top of the pane belongs once the transcript has changed underneath it.
        /// <para>
        /// Following means going wherever the end went. Not following means staying exactly where
        /// the player left it - which is the point of the whole mechanism, since the alternative is
        /// dragging someone off the paragraph they are reading every time a token lands. The clamp
        /// only ever applies when the transcript got <em>shorter</em>, as a wider terminal re-wrapping
        /// to fewer rows does, and it is what stops the pane drawing blank space below the last line.
        /// </para>
        /// </summary>
        internal static int NextOffset(int offsetY, int totalRows, int viewportHeight, bool following)
        {
            var bottom = BottomOffsetFor(totalRows, viewportHeight);
            return following ? bottom : Math.Clamp(offsetY, 0, bottom);
        }

        /// <summary>
        /// Greedy word wrap. Preserves span roles and entity IDs across the break, collapses the space that
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
            var word = new List<(char Ch, TextRole Role, string? EntityId)>();

            void CommitRow()
            {
                row.TrimEnd();
                rows.Add(row);
                row = new StyledLine();
            }

            void AppendRun(int start, int count)
            {
                var runStart = start;
                var end = start + count;
                while (runStart < end)
                {
                    var role = word[runStart].Role;
                    var entityId = word[runStart].EntityId;
                    var runEnd = runStart + 1;
                    while (runEnd < end && word[runEnd].Role == role && string.Equals(word[runEnd].EntityId, entityId, StringComparison.Ordinal))
                    {
                        runEnd++;
                    }

                    var runLen = runEnd - runStart;
                    var chars = new char[runLen];
                    for (var j = 0; j < runLen; j++)
                    {
                        chars[j] = word[runStart + j].Ch;
                    }

                    row.Append(new string(chars), role, entityId);
                    runStart = runEnd;
                }
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

                    AppendRun(0, width);
                    word.RemoveRange(0, width);
                    CommitRow();
                }

                if (row.Length > 0 && row.Length + word.Count > width)
                {
                    CommitRow();
                }

                AppendRun(0, word.Count);
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
                                row.Append(" ", span.Role, span.EntityId);
                            }

                            break;

                        default:
                            word.Add((ch, span.Role, span.EntityId));
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
