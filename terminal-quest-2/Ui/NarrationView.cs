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
    internal sealed class NarrationView : ThemedView
    {
        private readonly List<StyledLine> _committed = [];
        private readonly MarkupParser _parser = new();

        private List<StyledLine> _committedRows = [];
        private List<StyledLine> _currentRows = [];

        private StyledLine? _current;

        /// <summary>Width the caches were built for; -1 forces a rebuild.</summary>
        private int _wrapWidth = -1;

        private int _scroll;
        private bool _stickToBottom = true;

        private int TotalRows => _committedRows.Count + _currentRows.Count;

        /// <summary>Appends streamed narration. Safe to call with partial markup tags.</summary>
        public void AppendDelta(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            _current ??= new StyledLine();
            _parser.Append(text, _current);

            _currentRows = Wrap(_current.Spans, _wrapWidth);
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

        public void ScrollBy(int rows)
        {
            var maxScroll = Math.Max(0, TotalRows - Viewport.Height);
            _scroll = Math.Clamp(_scroll + rows, 0, maxScroll);
            _stickToBottom = _scroll >= maxScroll;
            SetNeedsDraw();
        }

        public void ScrollToBottom()
        {
            _stickToBottom = true;
            _scroll = Math.Max(0, TotalRows - Viewport.Height);
            SetNeedsDraw();
        }

        private void AfterContentChanged()
        {
            if (_stickToBottom)
            {
                _scroll = Math.Max(0, TotalRows - Viewport.Height);
            }

            SetNeedsDraw();
        }

        protected override bool OnKeyDown(Key key)
        {
            var page = Math.Max(1, Viewport.Height - 1);

            if (key == Key.PageUp)
            {
                ScrollBy(-page);
                return true;
            }

            if (key == Key.PageDown)
            {
                ScrollBy(page);
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

            var maxScroll = Math.Max(0, TotalRows - height);
            _scroll = Math.Clamp(_scroll, 0, maxScroll);

            for (var y = 0; y < height; y++)
            {
                var index = _scroll + y;
                if (index >= TotalRows)
                {
                    break;
                }

                var row = index < _committedRows.Count
                    ? _committedRows[index]
                    : _currentRows[index - _committedRows.Count];

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

        private void RebuildAllRows()
        {
            _committedRows = [];
            foreach (var line in _committed)
            {
                _committedRows.AddRange(Wrap(line.Spans, _wrapWidth));
            }

            _currentRows = _current is null ? [] : Wrap(_current.Spans, _wrapWidth);

            if (_stickToBottom)
            {
                _scroll = Math.Max(0, TotalRows - Viewport.Height);
            }
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
