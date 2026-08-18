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

        public event Action<string>? EntityClicked;

        public InventoryView()
        {
            CanFocus = true;
        }

        public void SetItems(IReadOnlyList<InventoryEntry> items)
        {
            _items = items ?? [];
            RebuildLines();
            SetNeedsDraw();
        }

        private void RebuildLines()
        {
            _lines.Clear();

            if (_items.Count == 0)
            {
                var empty = new StyledLine();
                empty.Append("(empty pack)", TextRole.Normal);
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

            for (var y = 0; y < height; y++)
            {
                Move(0, y);
                var index = _offsetY + y;

                if (index < _lines.Count)
                {
                    var line = _lines[index];
                    var drawn = 0;

                    foreach (var span in line.Spans)
                    {
                        if (drawn >= width)
                        {
                            break;
                        }

                        var text = span.Text.Length > width - drawn ? span.Text[..(width - drawn)] : span.Text;
                        SetRole(span.Role);
                        AddStr(text);
                        drawn += text.Length;
                    }

                    if (drawn < width)
                    {
                        SetRole(TextRole.Normal);
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
