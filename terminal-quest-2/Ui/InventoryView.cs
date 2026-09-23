using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Scrollable, themed inventory view for the Pack & Purse panel that displays items using
    /// entity markup styling and supports click-to-inspect.
    /// </summary>
    internal sealed class InventoryView : ThemedView
    {
        private readonly List<StyledLine> _lines = [];
        private IReadOnlyList<InventoryEntry> _items = [];
        private int _wrapWidth;
        private int _offsetY;

        /// <summary>
        /// The wrapped row the keyboard cursor sits on. Moved by the arrows, inspected by
        /// Enter/Space, and kept on screen by <see cref="EnsureCursorVisible"/>. The mouse does
        /// not need it - a click names its own row - but a click syncs it, so arrowing on from a
        /// clicked row starts where the player is looking rather than back at the top.
        /// </summary>
        private int _cursor;

        public event Action<string>? EntityClicked;

        public InventoryView()
        {
            // Focusable on purpose: Tab reaches the pack from the command box. The keys below
            // are what make that a promise rather than a trap - every mouse action here has had
            // a keyboard twin since.
            CanFocus = true;
        }

        /// <summary>The wrapped row the keyboard cursor is on, for tests.</summary>
        internal int CursorRow => _cursor;

        public void SetItems(IReadOnlyList<InventoryEntry> items)
        {
            _items = items ?? [];
            RebuildLines();
            _cursor = _lines.Count == 0 ? 0 : Math.Clamp(_cursor, 0, _lines.Count - 1);
            _offsetY = _lines.Count == 0 ? 0 : Math.Clamp(_offsetY, 0, Math.Max(0, _lines.Count - 1));
            SetNeedsDraw();
        }

        private void RebuildLines()
        {
            _lines.Clear();

            if (_items.Count == 0)
            {
                var empty = new StyledLine();
                empty.Append("(empty pack)", TextRole.Hint);
                _lines.Add(empty);
                return;
            }

            foreach (var item in _items)
            {
                var markup = item.Id.Length > 0
                    ? $"{item.Quantity}x [{item.Name}]({item.Id})"
                    : $"{item.Quantity}x {item.Name}";

                var parsed = MarkupParser.Parse(markup);

                if (_wrapWidth > 0 && parsed.Length > _wrapWidth)
                {
                    var wrapped = NarrationView.Wrap(parsed.Spans, _wrapWidth);
                    _lines.AddRange(wrapped);
                }
                else
                {
                    _lines.Add(parsed);
                }
            }
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            var height = Viewport.Height;

            if (width <= 0 || height <= 0)
            {
                return true;
            }

            if (width != _wrapWidth)
            {
                _wrapWidth = width;
                RebuildLines();
            }

            var maxOffset = Math.Max(0, _lines.Count - height);
            _offsetY = Math.Clamp(_offsetY, 0, maxOffset);

            // The keyboard cursor is only painted while focused, so mouse users never see a
            // selection they did not ask for.
            var showCursor = HasFocus && _cursor >= 0 && _cursor < _lines.Count;

            for (var y = 0; y < height; y++)
            {
                Move(0, y);
                var index = _offsetY + y;

                if (index < _lines.Count)
                {
                    var line = _lines[index];
                    var drawn = 0;

                    // A focused cursor row borrows the option-selection attribute wholesale,
                    // blank-fill included - the same look as a highlighted narrator choice.
                    var cursorRow = showCursor && index == _cursor;
                    if (cursorRow)
                    {
                        SetAttribute(Theme.OptionSelection);
                    }

                    foreach (var span in line.Spans)
                    {
                        if (drawn >= width)
                        {
                            break;
                        }

                        var text = span.Text.Length > width - drawn ? span.Text[..(width - drawn)] : span.Text;
                        if (!cursorRow)
                        {
                            SetRole(span.Role);
                        }
                        AddStr(text);
                        drawn += text.Length;
                    }

                    if (drawn < width)
                    {
                        if (!cursorRow)
                        {
                            SetRole(TextRole.Normal);
                        }
                        AddStr(Blank(width - drawn));
                    }
                }
                else
                {
                    SetRole(TextRole.Normal);
                    AddStr(Blank(width));
                }
            }

            return true;
        }

        protected override bool OnKeyDown(Key key)
        {
            if (_lines.Count == 0)
            {
                return false;
            }

            var page = Math.Max(1, Viewport.Height - 1);

            if (key == Key.CursorUp)
            {
                MoveCursor(_cursor - 1);
                return true;
            }

            if (key == Key.CursorDown)
            {
                MoveCursor(_cursor + 1);
                return true;
            }

            if (key == Key.PageUp)
            {
                MoveCursor(_cursor - page);
                return true;
            }

            if (key == Key.PageDown)
            {
                MoveCursor(_cursor + page);
                return true;
            }

            if (key == Key.Home)
            {
                MoveCursor(0);
                return true;
            }

            if (key == Key.End)
            {
                MoveCursor(_lines.Count - 1);
                return true;
            }

            // Enter/Space inspects whatever the cursor is on - the keyboard twin of clicking
            // the row, down to the same whole-row fallback for clicks that land between spans.
            if (key == Key.Enter || key == Key.Space)
            {
                return InspectRow(_cursor);
            }

            return base.OnKeyDown(key);
        }

        private void MoveCursor(int target)
        {
            _cursor = Math.Clamp(target, 0, _lines.Count - 1);
            EnsureCursorVisible();
            SetNeedsDraw();
        }

        private void EnsureCursorVisible()
        {
            var height = Viewport.Height;
            if (height <= 0)
            {
                return;
            }

            if (_cursor < _offsetY)
            {
                _offsetY = _cursor;
            }
            else if (_cursor >= _offsetY + height)
            {
                _offsetY = _cursor - height + 1;
            }
        }

        /// <summary>
        /// Raises <see cref="EntityClicked"/> for the first entity on the row, if it names one.
        /// </summary>
        /// <returns>False when the row names nothing, so the key can keep bubbling.</returns>
        private bool InspectRow(int index)
        {
            if (index < 0 || index >= _lines.Count)
            {
                return false;
            }

            var firstEntity = _lines[index].Spans.FirstOrDefault(s => s.EntityId is { Length: > 0 });
            if (firstEntity.EntityId is not { Length: > 0 } entityId)
            {
                return false;
            }

            EntityClicked?.Invoke(entityId);
            return true;
        }

        protected override bool OnMouseEvent(Mouse mouse)
        {
            ArgumentNullException.ThrowIfNull(mouse);

            if (mouse.Flags.HasFlag(MouseFlags.WheeledUp))
            {
                if (_offsetY > 0)
                {
                    _offsetY--;
                    SetNeedsDraw();
                    return true;
                }
            }

            if (mouse.Flags.HasFlag(MouseFlags.WheeledDown))
            {
                var maxOffset = Math.Max(0, _lines.Count - Viewport.Height);
                if (_offsetY < maxOffset)
                {
                    _offsetY++;
                    SetNeedsDraw();
                    return true;
                }
            }

            if (mouse.Flags.HasFlag(MouseFlags.LeftButtonClicked) && mouse.Position is { } pos)
            {
                var index = _offsetY + pos.Y;
                if (index >= 0 && index < _lines.Count)
                {
                    // The keyboard cursor follows the click, so arrowing on from here starts
                    // where the player is looking.
                    _cursor = index;
                    SetNeedsDraw();

                    var line = _lines[index];
                    var col = 0;

                    foreach (var span in line.Spans)
                    {
                        var spanEnd = col + span.Text.Length;
                        if (pos.X >= col && pos.X < spanEnd)
                        {
                            if (span.EntityId is { Length: > 0 } entityId)
                            {
                                EntityClicked?.Invoke(entityId);
                                return true;
                            }
                        }
                        col = spanEnd;
                    }

                    // If clicked anywhere on the row, inspect the entity associated with that row
                    var firstEntity = line.Spans.FirstOrDefault(s => s.EntityId is { Length: > 0 });
                    if (firstEntity.EntityId is { Length: > 0 } lineEntityId)
                    {
                        EntityClicked?.Invoke(lineEntityId);
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
