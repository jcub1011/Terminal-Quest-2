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
        /// <summary>
        /// Where the context gauge turns red. Chosen to leave a turn or two of warning rather than to
        /// mark the wall: the point of the gauge is to be acted on before the session runs out, and
        /// what the player does about it - leave and reopen the save - costs them a turn.
        /// </summary>
        private const int ContextDangerPercent = 85;

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

            // Two to a row, so the six take three rows rather than six and leave the pack its space.
            // Scores without their modifiers: the modifier is derivable, /characters spells it out,
            // and the roll line already shows the one that actually applied.
            for (var index = 0; index < _state.Attributes.Count; index += 2)
            {
                var line = new StyledLine();

                for (var column = 0; column < 2 && index + column < _state.Attributes.Count; column++)
                {
                    var attribute = _state.Attributes[index + column];

                    if (column > 0)
                    {
                        line.Append("  ", TextRole.System);
                    }

                    line.Append($"{attribute.Label} ", TextRole.System);
                    line.Append($"{attribute.Score,2}", TextRole.Normal);
                }

                DrawWrapped(ref row, width, height, line);
            }

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

            DrawContextGauge(ref row, width, height);

            DrawWrapped(ref row, width, height, StyledLine.FromText($"${_state.CostUsd:F4}", TextRole.System));

            if (_state.LastDurationMs > 0)
            {
                DrawWrapped(ref row, width, height, StyledLine.FromText($"{_state.LastDurationMs}ms", TextRole.System));
            }

            return true;
        }

        /// <summary>
        /// Draws how full the narrator's context is - the count, the share of the window, and a bar
        /// underneath for reading without reading a number.
        /// </summary>
        /// <remarks>
        /// Down here with the cost and the turn time because it is a fact about the sitting rather than
        /// about the character, and because putting it above the pack would push the pack down.
        /// <para>
        /// Nothing is drawn until a turn has reported a figure. A gauge at nought before the first turn
        /// would claim an empty context, when in truth the system prompt and the tool schemas are
        /// already in there and simply have not been counted yet.
        /// </para>
        /// </remarks>
        private void DrawContextGauge(ref int row, int width, int height)
        {
            var used = _state.ContextTokens;
            if (used <= 0)
            {
                return;
            }

            // A window nobody could establish leaves the count standing on its own. It still tells a
            // player who knows their own model something, which an invented denominator would not.
            var window = _state.ContextWindowTokens;
            if (window <= 0)
            {
                DrawField(ref row, width, height, "Context", FormatTokens(used), TextRole.Normal);
                return;
            }

            var percent = (int)Math.Clamp(used * 100L / window, 0, 100);
            var role = percent >= ContextDangerPercent ? TextRole.Danger : TextRole.Normal;

            DrawField(ref row, width, height, "Context", $"{FormatTokens(used)}  {percent}%", role);

            if (row >= height)
            {
                return;
            }

            var fill = BarFill(used, window, width);

            Move(0, row);
            SetRole(role);
            AddStr(new string('█', fill));
            SetRole(TextRole.System);
            AddStr(new string('░', width - fill));

            row++;
        }

        /// <summary>
        /// Abbreviates a token count to five columns at most, which is all the pane can spare beside a
        /// label and a percentage.
        /// </summary>
        internal static string FormatTokens(int tokens)
        {
            if (tokens < 1_000)
            {
                return tokens.ToString();
            }

            if (tokens < 1_000_000)
            {
                return $"{tokens / 1_000}k";
            }

            // The decimal is worth a column while the leading digit is alone, and costs one too many
            // once it is not: a context of 2,147,483,647 would otherwise format to seven.
            return tokens < 10_000_000
                ? $"{tokens / 1_000_000.0:F1}M"
                : $"{tokens / 1_000_000}M";
        }

        /// <summary>
        /// How many of <paramref name="width"/> cells to fill for <paramref name="used"/> tokens of
        /// <paramref name="window"/>.
        /// </summary>
        /// <remarks>
        /// Rounded, then held off both ends. A bar that reads empty while there is something in the
        /// context, or full while there is still room, misleads about the one thing it exists to say;
        /// keeping a cell back at each end costs a percent of accuracy and buys that.
        /// </remarks>
        internal static int BarFill(int used, int window, int width)
        {
            if (used <= 0 || window <= 0 || width <= 0)
            {
                return 0;
            }

            if (used >= window)
            {
                return width;
            }

            // One column has no room for a partial reading, and "full" is already taken above.
            if (width == 1)
            {
                return 0;
            }

            var fill = (int)Math.Round((double)used / window * width, MidpointRounding.AwayFromZero);

            return Math.Clamp(fill, 1, width - 1);
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
