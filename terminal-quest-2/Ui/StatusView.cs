using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The fixed status pane. Like <see cref="NarrationView"/> it draws itself so that each field
    /// can carry its own colour, and it borrows that view's word wrap so an item name wider than
    /// the pane runs onto a second row instead of being cut off at the margin.
    /// </summary>
    internal sealed class StatusView : ThemedView
    {
        private readonly GameState _state;

        public StatusView(GameState state)
        {
            _state = state;
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            var height = Viewport.Height;

            if (width <= 0 || height <= 0)
            {
                return true;
            }

            BeginPaint(width, height);

            var row = 0;

            // Health is the one field that changes meaning as it drops, so it changes colour too.
            // A save with no player character yet has no bar to draw rather than a misleading 0/0.
            var hasPlayer = _state.MaxHealth > 0;
            var healthRole = hasPlayer && _state.Health <= _state.MaxHealth / 4 ? TextRole.Danger : TextRole.Normal;
            DrawField(ref row, width, height, "HP", hasPlayer ? $"{_state.Health}/{_state.MaxHealth}" : "-", healthRole);
            DrawField(ref row, width, height, "Turn", _state.Turn.ToString(), TextRole.Normal);

            DrawSeparator(ref row, width, height);

            // Above the item list rather than below it, so a full pack cannot push the purse off
            // the bottom of the pane. Shown at nought too - "no money" is worth knowing.
            DrawField(ref row, width, height, "Money", _state.Money.ToString(), TextRole.Item);

            if (_state.Inventory.Count == 0)
            {
                DrawWrapped(ref row, width, height, StyledLine.FromText("(empty)", TextRole.System));
            }
            else
            {
                foreach (var entry in _state.Inventory)
                {
                    // Quantity first: it reads as a tally of what is carried, and it lines the
                    // names up down the left of the pane whatever the counts are.
                    var line = new StyledLine();
                    line.Append($"{entry.Quantity}x ", TextRole.System);
                    line.Append(entry.Name, TextRole.Item);
                    DrawWrapped(ref row, width, height, line);
                }
            }

            DrawSeparator(ref row, width, height);

            DrawWrapped(ref row, width, height, StyledLine.FromText($"${_state.CostUsd:F4}", TextRole.System));

            if (_state.LastDurationMs > 0)
            {
                DrawWrapped(ref row, width, height, StyledLine.FromText($"{_state.LastDurationMs}ms", TextRole.System));
            }

            return true;
        }

        /// <summary>Draws a left-aligned label with its value pushed to the right margin.</summary>
        private void DrawField(ref int row, int width, int height, string label, string value, TextRole valueRole)
        {
            if (row >= height)
            {
                return;
            }

            Move(0, row);
            SetRole(TextRole.System);
            AddStr(Fit(label, width));

            var valueText = Fit(value, width);
            var col = width - valueText.Length;
            if (col > label.Length)
            {
                Move(col, row);
                SetRole(valueRole);
                AddStr(valueText);
            }

            row++;
        }

        /// <summary>
        /// Draws one logical line, wrapped to the pane width. Continuation rows are not indented:
        /// the wrap is there so nothing is lost, and a hanging indent would cost the columns that
        /// made the wrap unnecessary.
        /// </summary>
        private void DrawWrapped(ref int row, int width, int height, StyledLine line)
        {
            foreach (var wrapped in NarrationView.Wrap(line.Spans, width))
            {
                if (row >= height)
                {
                    return;
                }

                Move(0, row);
                foreach (var span in wrapped.Spans)
                {
                    SetRole(span.Role);
                    AddStr(span.Text);
                }

                row++;
            }
        }

        private void DrawSeparator(ref int row, int width, int height)
        {
            if (row >= height)
            {
                return;
            }

            Move(0, row);
            SetRole(TextRole.System);
            AddStr(new string('─', width));
            row++;
        }

        /// <summary>
        /// Truncates to the pane width. Only the right-aligned fields need this - they are short
        /// values with nowhere to wrap to, and a two-row "HP" would read worse than a clipped one.
        /// </summary>
        private static string Fit(string text, int width) =>
            text.Length <= width ? text : text[..Math.Max(0, width)];
    }
}
