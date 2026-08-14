using System.Text;

namespace TerminalQuest.Agents.LmStudio
{
    /// <summary>
    /// Removes <c>&lt;think&gt;...&lt;/think&gt;</c> spans from a stream of text deltas.
    /// <para>
    /// Locally served reasoning models routinely put their chain of thought inline in the content
    /// rather than in a separate field, and none of it is story. Left in, it reaches the narration
    /// pane as prose, because it is prose as far as everything downstream can tell.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Tags arrive split across deltas - <c>"&lt;th"</c> then <c>"ink&gt;"</c> - so text that could
    /// still turn out to be the start of a tag is held back until the next delta decides. This is
    /// the same problem <c>MarkupParser</c> solves for the narrator's own markup, and for the same
    /// reason: the boundaries of a token and the boundaries of a delta have nothing to do with
    /// each other.
    /// </remarks>
    internal sealed class ThinkTagFilter
    {
        private const string Open = "<think>";
        private const string Close = "</think>";

        private readonly StringBuilder _held = new();

        private bool _inside;

        /// <summary>Feeds one delta in and returns the part of it that is safe to show.</summary>
        public string Feed(string chunk)
        {
            if (chunk.Length == 0)
            {
                return string.Empty;
            }

            _held.Append(chunk);

            var text = _held.ToString();
            var visible = new StringBuilder(text.Length);
            var index = 0;

            while (index < text.Length)
            {
                if (_inside)
                {
                    var close = text.IndexOf(Close, index, StringComparison.OrdinalIgnoreCase);
                    if (close < 0)
                    {
                        // Everything from here is reasoning, apart from what might be a partial
                        // closing tag - discard the rest and keep only that.
                        index = text.Length - PartialSuffix(text, index, Close);
                        break;
                    }

                    index = close + Close.Length;
                    _inside = false;
                    continue;
                }

                var open = text.IndexOf(Open, index, StringComparison.OrdinalIgnoreCase);
                if (open < 0)
                {
                    var keep = PartialSuffix(text, index, Open);
                    visible.Append(text, index, text.Length - index - keep);
                    index = text.Length - keep;
                    break;
                }

                visible.Append(text, index, open - index);
                index = open + Open.Length;
                _inside = true;
            }

            _held.Clear();
            _held.Append(text, index, text.Length - index);

            return visible.ToString();
        }

        /// <summary>
        /// Releases whatever is still held once the stream has ended. A held fragment at that point
        /// was never a tag, so it is ordinary text - unless the model opened a think block and
        /// never closed it, in which case it stays reasoning and stays dropped.
        /// </summary>
        public string Flush()
        {
            var remainder = _inside ? string.Empty : _held.ToString();

            _held.Clear();
            _inside = false;

            return remainder;
        }

        /// <summary>
        /// How many characters at the end of <paramref name="text"/> could be the beginning of
        /// <paramref name="tag"/>. Zero when the tail cannot become one however the stream
        /// continues.
        /// </summary>
        private static int PartialSuffix(string text, int from, string tag)
        {
            var longest = Math.Min(tag.Length - 1, text.Length - from);

            for (var length = longest; length > 0; length--)
            {
                if (string.Compare(text, text.Length - length, tag, 0, length, StringComparison.OrdinalIgnoreCase) == 0)
                {
                    return length;
                }
            }

            return 0;
        }
    }
}
