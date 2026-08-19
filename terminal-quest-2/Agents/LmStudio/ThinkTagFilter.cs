using System.Text;

namespace TerminalQuest.Agents.LmStudio
{
    /// <summary>
    /// Removes reasoning (<c>&lt;think&gt;...&lt;/think&gt;</c>) and extracts narration
    /// (<c>&lt;story&gt;...&lt;/story&gt;</c> or <c>&lt;narration&gt;...&lt;/narration&gt;</c>)
    /// from a stream of text deltas.
    /// <para>
    /// Locally served models routinely output thoughts, planning, and checklist reviews inline
    /// in the content stream. When story tags are present, anything outside them (pre-story thoughts
    /// and post-story rambles) is discarded. When story tags are absent, the stream falls back
    /// to stripping <c>&lt;think&gt;...&lt;/think&gt;</c> blocks.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Tags arrive split across deltas - <c>"&lt;st"</c> then <c>"ory&gt;"</c> - so text that could
    /// still turn out to be the start of a tag is held back until the next delta decides. This is
    /// the same problem <c>MarkupParser</c> solves for the narrator's own markup, and for the same
    /// reason: the boundaries of a token and the boundaries of a delta have nothing to do with
    /// each other.
    /// </remarks>
    internal sealed class ThinkTagFilter
    {
        private const string ThinkOpen = "<think>";
        private const string ThinkClose = "</think>";

        private static readonly string[] StoryOpenTags = ["<story>", "<narration>"];
        private static readonly string[] StoryCloseTags = ["</story>", "</narration>"];
        private static readonly string[] ThinkCloseArray = [ThinkClose];

        private readonly StringBuilder _held = new();

        private bool _insideThink;
        private bool _insideStory;
        private bool _hasSeenStoryTag;

        /// <summary>Filters a complete string through a fresh filter instance.</summary>
        public static string Filter(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var filter = new ThinkTagFilter();
            return filter.Feed(text) + filter.Flush();
        }

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
                if (_insideThink)
                {
                    var close = text.IndexOf(ThinkClose, index, StringComparison.OrdinalIgnoreCase);
                    if (close < 0)
                    {
                        var keep = MaxPartialSuffix(text, index, ThinkCloseArray);
                        index = text.Length - keep;
                        break;
                    }

                    index = close + ThinkClose.Length;
                    _insideThink = false;
                    continue;
                }

                string[] candidateTags;
                if (!_hasSeenStoryTag)
                {
                    candidateTags = [ThinkOpen, .. StoryOpenTags];
                }
                else if (_insideStory)
                {
                    candidateTags = [ThinkOpen, .. StoryCloseTags];
                }
                else
                {
                    candidateTags = [ThinkOpen, .. StoryOpenTags];
                }

                var earliestIndex = -1;
                string? matchedTag = null;

                foreach (var tag in candidateTags)
                {
                    var pos = text.IndexOf(tag, index, StringComparison.OrdinalIgnoreCase);
                    if (pos >= 0 && (earliestIndex < 0 || pos < earliestIndex))
                    {
                        earliestIndex = pos;
                        matchedTag = tag;
                    }
                }

                if (earliestIndex < 0)
                {
                    var keep = MaxPartialSuffix(text, index, candidateTags);
                    var safeLength = text.Length - index - keep;

                    if (safeLength > 0)
                    {
                        if (!_hasSeenStoryTag || _insideStory)
                        {
                            visible.Append(text, index, safeLength);
                        }
                    }

                    index = text.Length - keep;
                    break;
                }

                var segmentLen = earliestIndex - index;
                if (segmentLen > 0)
                {
                    if (!_hasSeenStoryTag || _insideStory)
                    {
                        visible.Append(text, index, segmentLen);
                    }
                }

                index = earliestIndex + matchedTag!.Length;

                if (string.Equals(matchedTag, ThinkOpen, StringComparison.OrdinalIgnoreCase))
                {
                    _insideThink = true;
                }
                else if (IsStoryOpen(matchedTag))
                {
                    _hasSeenStoryTag = true;
                    _insideStory = true;
                }
                else if (IsStoryClose(matchedTag))
                {
                    _insideStory = false;
                }
            }

            _held.Clear();
            _held.Append(text, index, text.Length - index);

            return visible.ToString();
        }

        /// <summary>
        /// Releases whatever is still held once the stream has ended.
        /// </summary>
        public string Flush()
        {
            var remainder = (_insideThink || (_hasSeenStoryTag && !_insideStory))
                ? string.Empty
                : _held.ToString();

            _held.Clear();
            _insideThink = false;
            _insideStory = false;
            _hasSeenStoryTag = false;

            return remainder.ToString();
        }

        private static bool IsStoryOpen(string tag) =>
            StoryOpenTags.Contains(tag, StringComparer.OrdinalIgnoreCase);

        private static bool IsStoryClose(string tag) =>
            StoryCloseTags.Contains(tag, StringComparer.OrdinalIgnoreCase);

        private static int MaxPartialSuffix(string text, int from, string[] tags)
        {
            var max = 0;
            foreach (var tag in tags)
            {
                var suffix = PartialSuffix(text, from, tag);
                if (suffix > max)
                {
                    max = suffix;
                }
            }

            return max;
        }

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
