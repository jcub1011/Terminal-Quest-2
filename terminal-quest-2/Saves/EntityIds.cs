using System.Globalization;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// The opaque identifiers that characters, locations and items are known by on disk.
    /// <para>
    /// A name is what the player and the narrator say; an id is what one record uses to point at
    /// another. Keeping them separate is what makes a rename a single field write rather than a
    /// hunt through every document that spelled the old name out. Nothing here is ever shown to
    /// either audience - see <see cref="WorldIndex"/> for the translation back.
    /// </para>
    /// <para>
    /// Short and prefixed rather than a <see cref="System.Guid"/>, because saves are meant to be
    /// opened and hand-edited - that is much of the point of keeping the world in files - and a
    /// document where every reference is thirty-six characters of hexadecimal is not one anybody
    /// wants to read. The prefix is not parsed for meaning; it is there so a mistake is obvious to
    /// a person reading the file.
    /// </para>
    /// </summary>
    internal static class EntityIds
    {
        public const string Character = "chr_";

        public const string Location = "loc_";

        public const string Item = "itm_";

        /// <summary>
        /// The counter value to resume numbering from, never below what is already in use.
        /// </summary>
        /// <remarks>
        /// A hand-edited save may carry a counter that lags its ids, or no counter at all. Taking
        /// the higher of the two is what stops a freshly allocated id from colliding with one that
        /// is already pointed at - which would silently merge two entities rather than fail.
        /// </remarks>
        /// <param name="prefix">The type prefix being numbered, e.g. <see cref="Character"/>.</param>
        /// <param name="existing">Every id currently in the document. Blanks are ignored.</param>
        /// <param name="counter">The counter as stored, which may be stale or zero.</param>
        public static int Ceiling(string prefix, IEnumerable<string?> existing, int counter)
        {
            ArgumentException.ThrowIfNullOrEmpty(prefix);
            ArgumentNullException.ThrowIfNull(existing);

            var highest = Math.Max(counter, 0);

            foreach (var id in existing)
            {
                if (Number(id, prefix) is { } number && number > highest)
                {
                    highest = number;
                }
            }

            return highest;
        }

        /// <summary>Whether an id is one this scheme could have issued for <paramref name="prefix"/>.</summary>
        public static bool IsWellFormed(string? id, string prefix) => Number(id, prefix) is not null;

        /// <summary>
        /// The number inside an id, or null when it is blank, the wrong type, or not something this
        /// scheme wrote. Tolerant on purpose: a malformed id read from a hand-edited file should
        /// leave numbering alone rather than throw in the middle of a turn.
        /// </summary>
        /// <remarks>
        /// <see cref="NumberStyles.None"/> rather than the default, which is
        /// <see cref="NumberStyles.Integer"/> and permits a leading sign and surrounding
        /// whitespace. <c>chr_+5</c> would otherwise be well formed and count toward the ceiling
        /// while never matching an ordinal id lookup - a record reachable by numbering but not by
        /// reference.
        /// </remarks>
        private static int? Number(string? id, string prefix)
        {
            if (id is not { Length: > 0 } || !id.StartsWith(prefix, StringComparison.Ordinal))
            {
                return null;
            }

            return int.TryParse(
                    id.AsSpan(prefix.Length),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var number)
                && number > 0
                    ? number
                    : null;
        }
    }
}
