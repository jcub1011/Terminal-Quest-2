namespace TerminalQuest.Ui
{
    /// <summary>A run of text sharing a single <see cref="TextRole"/> and optional entity ID.</summary>
    internal readonly record struct StyledSpan(string Text, TextRole Role, string? EntityId = null)
    {
        public string ToMarkup() => Theme.Format(Text, Role);
    }

    /// <summary>
    /// One logical paragraph of the transcript. Holds unwrapped spans; wrapping to the
    /// terminal width happens at draw time, so a resize only needs a re-wrap and never
    /// loses or re-flows the underlying text.
    /// </summary>
    internal sealed class StyledLine
    {
        private readonly List<StyledSpan> _spans = [];

        public IReadOnlyList<StyledSpan> Spans => _spans;

        /// <summary>Total character count across all spans.</summary>
        public int Length { get; private set; }

        /// <summary>
        /// Appends text, merging into the trailing span when the role and entity ID match so that a
        /// token-by-token stream does not produce one span per token.
        /// </summary>
        public void Append(string text, TextRole role, string? entityId = null)
        {
            if (text.Length == 0)
            {
                return;
            }

            if (_spans.Count > 0)
            {
                var last = _spans[^1];
                if (last.Role == role && string.Equals(last.EntityId, entityId, StringComparison.Ordinal))
                {
                    _spans[^1] = last with { Text = last.Text + text };
                    Length += text.Length;
                    return;
                }
            }

            _spans.Add(new StyledSpan(text, role, entityId));
            Length += text.Length;
        }

        public void Append(StyledSpan span) => Append(span.Text, span.Role, span.EntityId);

        /// <summary>
        /// Drops trailing spaces. Used when a wrapped row is committed so that the space sitting
        /// at the break point does not survive as invisible padding at the end of the row.
        /// </summary>
        public void TrimEnd()
        {
            while (_spans.Count > 0)
            {
                var last = _spans[^1];
                var trimmed = last.Text.TrimEnd(' ');

                if (trimmed.Length == last.Text.Length)
                {
                    return;
                }

                Length -= last.Text.Length - trimmed.Length;

                if (trimmed.Length > 0)
                {
                    _spans[^1] = last with { Text = trimmed };
                    return;
                }

                // The span was nothing but spaces; drop it and check the one before it.
                _spans.RemoveAt(_spans.Count - 1);
            }
        }

        public string ToPlainText() =>
            string.Concat(_spans.Select(s => s.Text));

        public string ToMarkup() =>
            string.Concat(_spans.Select(s => s.ToMarkup()));

        public static StyledLine FromText(string text, TextRole role = TextRole.Normal)
        {
            var line = new StyledLine();
            line.Append(text, role);
            return line;
        }
    }
}
