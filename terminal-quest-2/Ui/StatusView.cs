using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The fixed status pane. Like <see cref="NarrationView"/> it draws itself so that each field
    /// can carry its own colour, but it needs no wrapping or scrolling - it is a short fixed list.
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
            var healthRole = _state.Health <= _state.MaxHealth / 4 ? TextRole.Danger : TextRole.Normal;
            DrawField(ref row, width, height, "HP", $"{_state.Health}/{_state.MaxHealth}", healthRole);
            DrawField(ref row, width, height, "Gold", _state.Gold.ToString(), TextRole.Item);
            DrawField(ref row, width, height, "Turn", _state.Turn.ToString(), TextRole.Normal);

            DrawSeparator(ref row, width, height);

            if (_state.Inventory.Count == 0)
            {
                DrawText(ref row, width, height, "(empty)", TextRole.System);
            }
            else
            {
                foreach (var item in _state.Inventory)
                {
                    DrawText(ref row, width, height, item, TextRole.Item);
                }
            }

            DrawSeparator(ref row, width, height);

            if (_state.IsBusy)
            {
                DrawText(ref row, width, height, "...thinking", TextRole.Speech);
            }

            DrawText(ref row, width, height, $"${_state.CostUsd:F4}", TextRole.System);

            if (_state.LastDurationMs > 0)
            {
                DrawText(ref row, width, height, $"{_state.LastDurationMs}ms", TextRole.System);
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

        private void DrawText(ref int row, int width, int height, string text, TextRole role)
        {
            if (row >= height)
            {
                return;
            }

            Move(0, row);
            SetRole(role);
            AddStr(Fit(text, width));
            row++;
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

        /// <summary>Truncates to the pane width so a long item name cannot overflow the viewport.</summary>
        private static string Fit(string text, int width) =>
            text.Length <= width ? text : text[..Math.Max(0, width)];
    }
}
