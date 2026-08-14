namespace TerminalQuest.Saves
{
    /// <summary>
    /// The two tokens a memory or location event may contain, and the substitution applied on the
    /// way out of the store.
    /// <para>
    /// They are stored raw and resolved only when read. Writing resolved names to disk would make
    /// the record wrong the moment a character is renamed, and it would lose the distinction
    /// between "Rowan" the name and "the character this memory belongs to" - which is exactly what
    /// makes a memory portable between characters who witnessed the same thing.
    /// </para>
    /// <para>
    /// These two are also the only names in stored prose that a rename reaches. Ids keep every
    /// reference one record makes to another correct, but the text of a memory is free prose, and a
    /// third party it spells out by name still says the old one afterwards. That is accepted rather
    /// than fixed: rewriting it would mean substituting a name across everything anybody remembers,
    /// which cannot tell a character called Bess from the word "largesse", cannot be undone, and
    /// would quietly edit records the player may have written by hand.
    /// </para>
    /// </summary>
    internal static class Placeholders
    {
        /// <summary>The character or location that owns the record.</summary>
        public const string This = "{This}";

        /// <summary>Whichever character is <see cref="CharacterKind.Player"/>.</summary>
        public const string Player = "{Player}";

        /// <summary>
        /// Substitutes both tokens. Comparison is ordinal and case-insensitive, so a narrator that
        /// writes <c>{this}</c> is not silently ignored.
        /// </summary>
        /// <param name="text">Raw text as stored.</param>
        /// <param name="owner">Name to substitute for <see cref="This"/>.</param>
        /// <param name="playerName">Name to substitute for <see cref="Player"/>, when one is known.</param>
        public static string Resolve(string text, string owner, string? playerName)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            var resolved = text.Replace(This, owner, StringComparison.OrdinalIgnoreCase);

            if (playerName is { Length: > 0 })
            {
                resolved = resolved.Replace(Player, playerName, StringComparison.OrdinalIgnoreCase);
            }

            return resolved;
        }

        /// <summary>
        /// Whether <paramref name="text"/> refers to <paramref name="entity"/>, tolerating either
        /// spelling: a narrator may write the player as <c>{Player}</c> in one record and by name
        /// in the next, and a filter that missed one of those would be worse than no filter.
        /// </summary>
        /// <remarks>
        /// Both blanks are answered false rather than left to <see cref="string.Contains(string)"/>,
        /// which is wrong in opposite directions on each. A memory with no text is a hand-editable
        /// state and should read as "mentions nobody" the way <see cref="Resolve"/> reads it as
        /// empty, not throw mid-turn. An empty entity is contained by every string, so without the
        /// guard a blank subject matches everything - which on the memory-filter path means handing
        /// back a character's entire history instead of nothing.
        /// </remarks>
        public static bool Mentions(string? text, string entity, string owner, string? playerName)
        {
            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(entity))
            {
                return false;
            }

            return text.Contains(entity, StringComparison.OrdinalIgnoreCase)
                || Resolve(text, owner, playerName).Contains(entity, StringComparison.OrdinalIgnoreCase);
        }
    }
}
