using System.Text;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Turns the narrator's semantic markup (<c>[item]rusted key[/]</c>) into styled spans.
    /// <para>
    /// This is deliberately incremental: <see cref="Append"/> is fed raw stream deltas, and a
    /// tag split across two deltas (<c>"...[dan"</c> then <c>"ger]..."</c>) must not corrupt
    /// the line. Parser state therefore survives between calls until <see cref="Reset"/>.
    /// </para>
    /// <para>
    /// Input is model-authored, so it is never trusted to be well-formed. Unknown tags,
    /// unbalanced closers, and stray brackets all render as literal text rather than throwing.
    /// Write <c>[[</c> for a literal <c>[</c>.
    /// </para>
    /// </summary>
    internal sealed class MarkupParser
    {
        /// <summary>
        /// Longest plausible tag, including the brackets. A '[' that is not closed within this
        /// many characters is treated as literal text, so one stray bracket in the narration
        /// cannot swallow the rest of the stream.
        /// </summary>
        private const int MaxTagLength = 24;

        private readonly Stack<TextRole> _roles = new();
        private readonly StringBuilder _tag = new();
        private bool _inTag;

        /// <summary>The role that text would currently be emitted with.</summary>
        private TextRole Current => _roles.Count > 0 ? _roles.Peek() : TextRole.Normal;

        /// <summary>Clears all state. Call between narration blocks.</summary>
        public void Reset()
        {
            _roles.Clear();
            _tag.Clear();
            _inTag = false;
        }

        /// <summary>
        /// Parses <paramref name="text"/> and appends the resulting spans to <paramref name="sink"/>.
        /// Any trailing partial tag is retained for the next call.
        /// </summary>
        public void Append(string text, StyledLine sink)
        {
            var run = new StringBuilder();

            foreach (var c in text)
            {
                if (_inTag)
                {
                    HandleTagChar(c, run, sink);
                    continue;
                }

                if (c == '[')
                {
                    _inTag = true;
                    _tag.Clear();
                }
                else
                {
                    run.Append(c);
                }
            }

            Flush(run, sink);
        }

        private void HandleTagChar(char c, StringBuilder run, StyledLine sink)
        {
            // "[[" is an escaped literal '['.
            if (_tag.Length == 0 && c == '[')
            {
                run.Append('[');
                _inTag = false;
                return;
            }

            if (c == ']')
            {
                Flush(run, sink);
                CloseTag(run);
                _inTag = false;
                return;
            }

            // A nested '[' means the previous one was never a tag. Emit it literally and
            // restart tag scanning at this character.
            if (c == '[')
            {
                run.Append('[').Append(_tag);
                _tag.Clear();
                return;
            }

            _tag.Append(c);

            if (_tag.Length > MaxTagLength)
            {
                run.Append('[').Append(_tag);
                _tag.Clear();
                _inTag = false;
            }
        }

        private void CloseTag(StringBuilder run)
        {
            var name = _tag.ToString();
            _tag.Clear();

            if (name.StartsWith('/'))
            {
                CloseRole(name, run);
                return;
            }

            if (TryParseRole(name, out var role))
            {
                _roles.Push(role);
                return;
            }

            // Not a tag we know. Show it as the narrator wrote it.
            run.Append('[').Append(name).Append(']');
        }

        /// <summary>
        /// Handles a closing tag. Both the bare <c>[/]</c> and the named <c>[/place]</c> form are
        /// accepted - models emit either regardless of what the prompt asks for, and an
        /// unrecognised closer would otherwise be printed into the narration as literal text.
        /// </summary>
        private void CloseRole(string name, StringBuilder run)
        {
            var closing = name[1..];

            // Bare "[/]" closes whatever is innermost.
            if (closing.Length == 0)
            {
                if (_roles.Count > 0)
                {
                    _roles.Pop();
                }

                return;
            }

            if (!TryParseRole(closing, out var role))
            {
                // A closer for a tag we never understood; show it as written.
                run.Append('[').Append(name).Append(']');
                return;
            }

            // Unmatched closer - drop it rather than printing it or corrupting the role stack.
            if (!_roles.Contains(role))
            {
                return;
            }

            // Pop through to the named role, so a missing inner closer cannot strand the stack.
            while (_roles.Count > 0 && _roles.Pop() != role)
            {
            }
        }

        /// <summary>
        /// The tags the narrator may write, matched exactly by the markup rules at the head of the
        /// system prompt - the two are a pair and have to be changed together.
        /// </summary>
        /// <remarks>
        /// <see cref="TextRole.Command"/> and <see cref="TextRole.Roll"/> are missing on purpose,
        /// not by oversight. Both are the game's own voice: the first is the player's line echoed
        /// back, the second is drawn from the save. Giving the narrator a <c>[roll]</c> tag would let
        /// it type a roll line - which means inventing a number, or spelling out one it was asked to
        /// keep quiet. An unknown tag renders as literal text, so if it ever tries, the mistake is
        /// visible rather than convincing.
        /// </remarks>
        private static bool TryParseRole(string name, out TextRole role)
        {
            switch (name)
            {
                case "item": role = TextRole.Item; return true;
                case "danger": role = TextRole.Danger; return true;
                case "speech": role = TextRole.Speech; return true;
                case "place": role = TextRole.Place; return true;
                case "system": role = TextRole.System; return true;
                default: role = TextRole.Normal; return false;
            }
        }

        private void Flush(StringBuilder run, StyledLine sink)
        {
            if (run.Length == 0)
            {
                return;
            }

            sink.Append(run.ToString(), Current);
            run.Clear();
        }

        /// <summary>Convenience for parsing a complete, self-contained string.</summary>
        public static StyledLine Parse(string text)
        {
            var line = new StyledLine();
            new MarkupParser().Append(text, line);
            return line;
        }
    }
}
