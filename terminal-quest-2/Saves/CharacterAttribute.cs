namespace TerminalQuest.Saves
{
    /// <summary>
    /// One thing a character is made of, and how much of it they have.
    /// <para>
    /// Not named <c>Attribute</c>: that word is taken by <see cref="System.Attribute"/> and again by
    /// Terminal.Gui's drawing attribute, which <c>Ui/Theme.cs</c> already has to alias its way
    /// around. The JSON still reads <c>"attributes"</c>, because the naming policy works from the
    /// property on <see cref="Character"/> and not from the name of the element type.
    /// </para>
    /// </summary>
    internal sealed class CharacterAttribute
    {
        /// <summary>
        /// What it is called. One of <see cref="CharacterAttributes.Core"/> for the six everybody
        /// has, or whatever the narrator named a thing the story grew.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// How much of it they have, from <see cref="CharacterAttributes.MinScore"/> to
        /// <see cref="CharacterAttributes.MaxScore"/>. Ten is unremarkable - see
        /// <see cref="CharacterAttributes.Modifier"/> for what the number is actually worth.
        /// </summary>
        public int Score { get; set; }
    }

    /// <summary>
    /// The rules that turn a score into a number the dice can use, and the six attributes every
    /// character has whether or not their save says so.
    /// <para>
    /// The six exist so the narrator always has something to roll against: a check that has to
    /// invent the attribute it needs is a check the model decided the terms of. Everything beyond
    /// them is named by the story - standing in a guild, a god's favour - and works exactly the
    /// same way once it exists.
    /// </para>
    /// </summary>
    internal static class CharacterAttributes
    {
        /// <summary>The score that is worth nothing either way, and what an unstated attribute is.</summary>
        public const int Neutral = 10;

        public const int MinScore = 1;

        public const int MaxScore = 30;

        /// <summary>
        /// The six everybody has, in the order they are always listed - to the narrator, to the
        /// player, and on disk. A fixed order is what stops a rewrite of one attribute from
        /// reshuffling the rest of the document.
        /// </summary>
        public static IReadOnlyList<string> Core { get; } =
        [
            "Strength",
            "Dexterity",
            "Constitution",
            "Intelligence",
            "Wisdom",
            "Charisma",
        ];

        /// <summary>
        /// Short forms the narrator will use whichever way it is asked. The renderer spells names
        /// out in full, so the model should never see these - but it will type them anyway, and one
        /// small table is cheaper than a refused tool call and the turn it costs.
        /// </summary>
        private static readonly Dictionary<string, string> Abbreviations = new(StringComparer.OrdinalIgnoreCase)
        {
            ["str"] = "Strength",
            ["dex"] = "Dexterity",
            ["con"] = "Constitution",
            ["int"] = "Intelligence",
            ["wis"] = "Wisdom",
            ["cha"] = "Charisma",
        };

        /// <summary>
        /// What a score is worth on a roll: nothing at <see cref="Neutral"/>, and one point either
        /// way for every two above or below it.
        /// </summary>
        /// <remarks>
        /// Floored, not truncated, and written the long way round on purpose. The obvious
        /// <c>(score - Neutral) / 2</c> rounds towards zero, so a score of 7 comes out at -1 where
        /// the rule says -2 - wrong across the whole below-average half of the range, and right
        /// everywhere a hurried test would look.
        /// </remarks>
        public static int Modifier(int score) => (int)Math.Floor((score - Neutral) / 2.0);

        /// <summary>A modifier with its sign always shown, since <c>+0</c> reads as a fact and <c>0</c> reads as a gap.</summary>
        public static string Sign(int modifier) => modifier >= 0 ? $"+{modifier}" : modifier.ToString();

        /// <summary>Whether this is one of the six everybody has.</summary>
        public static bool IsCore(string? name) => CanonicalCore(name) is not null;

        /// <summary>
        /// The name as it should be stored: the canonical spelling for one of the six, and a
        /// trimmed version of whatever was asked for otherwise. Null only when nothing was asked
        /// for at all.
        /// </summary>
        public static string? CanonicalName(string? name)
        {
            if (name is not { Length: > 0 } || name.AsSpan().IsWhiteSpace())
            {
                return null;
            }

            return CanonicalCore(name) ?? name.Trim();
        }

        /// <summary>
        /// One of a character's attributes by name, core or not. Null when they have no such thing
        /// stored - which for one of the six means only that nobody has changed it from
        /// <see cref="Neutral"/>; see <see cref="All"/>.
        /// </summary>
        public static CharacterAttribute? Find(Character character, string? name)
        {
            ArgumentNullException.ThrowIfNull(character);

            if (CanonicalName(name) is not { } wanted)
            {
                return null;
            }

            return character.Attributes.Find(attribute => SaveStore.Matches(attribute.Name, wanted));
        }

        /// <summary>
        /// Everything a character is made of: the six first in <see cref="Core"/> order, then
        /// whatever the story added, in the order it was added.
        /// </summary>
        /// <remarks>
        /// A core attribute the save does not mention is yielded at <see cref="Neutral"/> rather
        /// than skipped, and <b>nothing is written</b>. A save made before attributes existed, or
        /// one edited by hand, therefore reads as a complete character without being quietly
        /// rewritten the first time anybody looks at it - the same instinct as the blank-id repair
        /// in <c>upsert_character</c>, minus the write.
        /// </remarks>
        public static IEnumerable<CharacterAttribute> All(Character character)
        {
            ArgumentNullException.ThrowIfNull(character);

            foreach (var name in Core)
            {
                yield return Find(character, name) ?? new CharacterAttribute { Name = name, Score = Neutral };
            }

            foreach (var attribute in character.Attributes)
            {
                if (!IsCore(attribute.Name))
                {
                    yield return attribute;
                }
            }
        }

        /// <summary>
        /// Gives a character the six, and whatever else was handed over, without disturbing
        /// anything they already had.
        /// </summary>
        /// <param name="character">Who is being filled in.</param>
        /// <param name="from">
        /// Scores to start from - a class's spread, typically. Copied rather than stored: these are
        /// shared statics and the narrator edits attributes in place, so handing one out would spend
        /// the next character's. The same hazard <see cref="NewGame"/> already documents for a kit.
        /// </param>
        public static void Seed(Character character, IReadOnlyList<CharacterAttribute>? from)
        {
            ArgumentNullException.ThrowIfNull(character);

            if (from is not null)
            {
                foreach (var source in from)
                {
                    Set(character, source.Name, source.Score);
                }
            }

            // After the spread, not before: a class that states a score should win over the
            // baseline, and a class that says nothing about an attribute still leaves it present.
            foreach (var name in Core)
            {
                if (Find(character, name) is null)
                {
                    character.Attributes.Add(new CharacterAttribute { Name = name, Score = Neutral });
                }
            }
        }

        /// <summary>
        /// Writes a score, creating the attribute when the character has none by that name. The
        /// caller writes the file.
        /// </summary>
        /// <returns>The attribute as it now stands, so a caller can report what it became.</returns>
        public static CharacterAttribute Set(Character character, string name, int score)
        {
            ArgumentNullException.ThrowIfNull(character);

            var canonical = CanonicalName(name)
                ?? throw new ArgumentException("An attribute needs a name.", nameof(name));

            var attribute = Find(character, canonical);

            if (attribute is null)
            {
                attribute = new CharacterAttribute { Name = canonical };
                character.Attributes.Add(attribute);
            }

            attribute.Score = Math.Clamp(score, MinScore, MaxScore);
            return attribute;
        }

        /// <summary>The canonical spelling of one of the six, or null when the name is not one of them.</summary>
        private static string? CanonicalCore(string? name)
        {
            if (name is not { Length: > 0 })
            {
                return null;
            }

            var trimmed = name.Trim();

            foreach (var core in Core)
            {
                if (string.Equals(core, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return core;
                }
            }

            return Abbreviations.GetValueOrDefault(trimmed);
        }
    }
}
