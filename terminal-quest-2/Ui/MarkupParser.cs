using System.Text;
using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Turns the narrator's semantic markup (<c>[Entity Name](entity_id)</c> and <c>["Speech"](speaker_id)</c>) into styled spans.
    /// <para>
    /// This is deliberately incremental: <see cref="Append"/> is fed raw stream deltas, and tags/entities
    /// split across deltas must not corrupt the line. Parser state survives between calls until <see cref="Reset"/>.
    /// </para>
    /// <para>
    /// Input is model-authored, so it is never trusted to be well-formed. Stray brackets, incomplete entities,
    /// or unclosed tags render gracefully as literal text rather than throwing.
    /// Write <c>[[</c> for a literal <c>[</c>.
    /// </para>
    /// </summary>
    internal sealed class MarkupParser
    {
        private const int MaxEntityNameLength = 120;
        private const int MaxEntityIdLength = 60;

        private enum ParserState
        {
            Normal,
            AfterOpenBracket,
            EntityName,
            AfterCloseBracket,
            EntityId,
            AfterQuoteInSpeech,
            AfterCloseSpeechBracket,
            SpeechSpeakerId,
        }

        private ParserState _state = ParserState.Normal;
        private bool _inSpeech;
        private int _speechStartSpanIndex = -1;
        private int _consecutiveNewlines;

        private readonly StringBuilder _entityName = new();
        private readonly StringBuilder _entityId = new();
        private readonly StringBuilder _speechSpeakerId = new();

        /// <summary>Current baseline text role when outside an entity link.</summary>
        private TextRole CurrentRole => _inSpeech ? TextRole.Speech : TextRole.Normal;

        /// <summary>Clears all state. Call between narration blocks.</summary>
        public void Reset(StyledLine? sink = null)
        {
            if (sink is not null)
            {
                FlushIncompleteState(sink);
            }

            _state = ParserState.Normal;
            _inSpeech = false;
            _speechStartSpanIndex = -1;
            _consecutiveNewlines = 0;
            _entityName.Clear();
            _entityId.Clear();
            _speechSpeakerId.Clear();
        }

        public void Append(string text, StyledLine sink)
        {
            ArgumentNullException.ThrowIfNull(sink);

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            var run = new StringBuilder();

            for (var i = 0; i < text.Length; i++)
            {
                var c = text[i];
                ProcessChar(c, run, sink);
            }

            FlushRun(run, sink);
        }

        private void ProcessChar(char c, StringBuilder run, StyledLine sink)
        {
            switch (_state)
            {
                case ParserState.Normal:
                    if (c == '[')
                    {
                        _consecutiveNewlines = 0;
                        FlushRun(run, sink);
                        _state = ParserState.AfterOpenBracket;
                    }
                    else if (_inSpeech && c == '"')
                    {
                        _consecutiveNewlines = 0;
                        FlushRun(run, sink);
                        _state = ParserState.AfterQuoteInSpeech;
                    }
                    else
                    {
                        if (c == '\n')
                        {
                            _consecutiveNewlines++;
                            if (_inSpeech && _consecutiveNewlines >= 2)
                            {
                                // A blank line / paragraph break ends unclosed speech so it does not leak into future paragraphs.
                                FlushRun(run, sink);
                                _inSpeech = false;
                                _speechStartSpanIndex = -1;
                            }
                        }
                        else if (c != '\r' && c != ' ' && c != '\t')
                        {
                            _consecutiveNewlines = 0;
                        }

                        run.Append(c);
                    }
                    break;

                case ParserState.AfterOpenBracket:
                    if (c == '[')
                    {
                        // "[[" is an escaped literal '['.
                        run.Append('[');
                        _state = ParserState.Normal;
                    }
                    else if (c == '"')
                    {
                        // '["' starts speech.
                        if (!_inSpeech)
                        {
                            FlushRun(run, sink);
                            _inSpeech = true;
                            _speechStartSpanIndex = sink.Spans.Count;
                            run.Append('"');
                        }
                        else
                        {
                            // Already in speech, emit literal '["'
                            run.Append("[\"");
                        }
                        _state = ParserState.Normal;
                    }
                    else if (c == ']')
                    {
                        // "[]" is empty entity name, not a valid entity. Emit "[]".
                        run.Append("[]");
                        _state = ParserState.Normal;
                    }
                    else
                    {
                        _entityName.Clear();
                        _entityName.Append(c);
                        _state = ParserState.EntityName;
                    }
                    break;

                case ParserState.EntityName:
                    if (c == ']')
                    {
                        _state = ParserState.AfterCloseBracket;
                    }
                    else if (c == '[')
                    {
                        // Nested '[' inside unclosed '['. Emit previous as literal.
                        run.Append('[').Append(_entityName);
                        _entityName.Clear();
                        _state = ParserState.AfterOpenBracket;
                    }
                    else if (c == '\n' || _entityName.Length > MaxEntityNameLength)
                    {
                        // Abort entity parsing and emit literal text.
                        run.Append('[').Append(_entityName).Append(c);
                        _entityName.Clear();
                        _state = ParserState.Normal;
                    }
                    else
                    {
                        _entityName.Append(c);
                    }
                    break;

                case ParserState.AfterCloseBracket:
                    if (c == '(')
                    {
                        _entityId.Clear();
                        _state = ParserState.EntityId;
                    }
                    else
                    {
                        // Not followed by '(', so '[EntityName]' was literal text.
                        run.Append('[').Append(_entityName).Append(']');
                        _entityName.Clear();
                        _state = ParserState.Normal;
                        // Re-process current character in Normal state
                        ProcessChar(c, run, sink);
                    }
                    break;

                case ParserState.EntityId:
                    if (c == ')')
                    {
                        FlushRun(run, sink);
                        var name = _entityName.ToString();
                        var id = _entityId.ToString().Trim();
                        var role = RoleForEntityId(id);

                        sink.Append(name, role, id.Length > 0 ? id : null);

                        _entityName.Clear();
                        _entityId.Clear();
                        _state = ParserState.Normal;
                    }
                    else if (c == '(' || c == '[' || c == '\n' || _entityId.Length > MaxEntityIdLength)
                    {
                        // Malformed ID. Emit literal text.
                        run.Append('[').Append(_entityName).Append("](").Append(_entityId).Append(c);
                        _entityName.Clear();
                        _entityId.Clear();
                        _state = ParserState.Normal;
                    }
                    else
                    {
                        _entityId.Append(c);
                    }
                    break;

                case ParserState.AfterQuoteInSpeech:
                    if (c == ']')
                    {
                        // '"]' ends speech.
                        run.Append('"');
                        FlushRun(run, sink);
                        _inSpeech = false;
                        _state = ParserState.AfterCloseSpeechBracket;
                    }
                    else if (c == '\n' || c == '\r')
                    {
                        // Quote before newline closes speech (missing closing ']').
                        run.Append('"');
                        FlushRun(run, sink);
                        _inSpeech = false;
                        _speechStartSpanIndex = -1;
                        _state = ParserState.Normal;
                        ProcessChar(c, run, sink);
                    }
                    else if (c == '(')
                    {
                        // Quote before '(' starts speaker tag (missing closing ']').
                        run.Append('"');
                        FlushRun(run, sink);
                        _inSpeech = false;
                        _speechSpeakerId.Clear();
                        _state = ParserState.SpeechSpeakerId;
                    }
                    else
                    {
                        // '"' was just a regular quote inside speech.
                        run.Append('"');
                        _state = ParserState.Normal;
                        ProcessChar(c, run, sink);
                    }
                    break;

                case ParserState.AfterCloseSpeechBracket:
                    if (c == '(')
                    {
                        _speechSpeakerId.Clear();
                        _state = ParserState.SpeechSpeakerId;
                    }
                    else
                    {
                        _speechStartSpanIndex = -1;
                        _state = ParserState.Normal;
                        ProcessChar(c, run, sink);
                    }
                    break;

                case ParserState.SpeechSpeakerId:
                    if (c == ')')
                    {
                        var speakerId = _speechSpeakerId.ToString().Trim();
                        if (speakerId.Length > 0 && _speechStartSpanIndex >= 0)
                        {
                            sink.TagSpeechSpans(_speechStartSpanIndex, speakerId);
                        }

                        _speechSpeakerId.Clear();
                        _speechStartSpanIndex = -1;
                        _state = ParserState.Normal;
                    }
                    else if (c == '(' || c == '[' || c == '\n' || _speechSpeakerId.Length > MaxEntityIdLength)
                    {
                        run.Append('(').Append(_speechSpeakerId).Append(c);
                        _speechSpeakerId.Clear();
                        _speechStartSpanIndex = -1;
                        _state = ParserState.Normal;
                    }
                    else
                    {
                        _speechSpeakerId.Append(c);
                    }
                    break;
            }
        }

        private void FlushIncompleteState(StyledLine sink)
        {
            var run = new StringBuilder();

            switch (_state)
            {
                case ParserState.AfterOpenBracket:
                    run.Append('[');
                    break;
                case ParserState.EntityName:
                    run.Append('[').Append(_entityName);
                    break;
                case ParserState.AfterCloseBracket:
                    run.Append('[').Append(_entityName).Append(']');
                    break;
                case ParserState.EntityId:
                    run.Append('[').Append(_entityName).Append("](").Append(_entityId);
                    break;
                case ParserState.AfterQuoteInSpeech:
                    run.Append('"');
                    break;
                case ParserState.AfterCloseSpeechBracket:
                    break;
                case ParserState.SpeechSpeakerId:
                    run.Append('(').Append(_speechSpeakerId);
                    break;
            }

            FlushRun(run, sink);
        }

        private static TextRole RoleForEntityId(string entityId)
        {
            if (entityId.StartsWith(EntityIds.Character, StringComparison.OrdinalIgnoreCase))
            {
                return TextRole.Character;
            }

            if (entityId.StartsWith(EntityIds.Location, StringComparison.OrdinalIgnoreCase))
            {
                return TextRole.Place;
            }

            if (entityId.StartsWith(EntityIds.Item, StringComparison.OrdinalIgnoreCase))
            {
                return TextRole.Item;
            }

            return TextRole.Normal;
        }

        private void FlushRun(StringBuilder run, StyledLine sink)
        {
            if (run.Length == 0)
            {
                return;
            }

            sink.Append(run.ToString(), CurrentRole);
            run.Clear();
        }

        /// <summary>Convenience for parsing a complete, self-contained string.</summary>
        public static StyledLine Parse(string text)
        {
            var line = new StyledLine();
            var parser = new MarkupParser();
            parser.Append(text, line);
            parser.Reset(line);
            return line;
        }
    }
}
