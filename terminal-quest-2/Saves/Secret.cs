namespace TerminalQuest.Saves
{
    /// <summary>
    /// Something one character knows that the world is not ready for everybody to know.
    /// <para>
    /// Distinct from a <see cref="Memory"/>, and the distinction is the whole point: a memory is
    /// returned to anyone who asks for it, and a secret is returned only when its
    /// <see cref="SecretStage"/> allows. Same substrate, different gate.
    /// </para>
    /// <para>
    /// <b>A secret has a name and no id, and that is a house rule rather than an oversight.</b> Ids
    /// never leave the save layer - see <see cref="EntityIds"/> - so an id could appear in neither a
    /// ledger entry nor anything else the model reads: the narrator would have no handle to use, and
    /// no way to report which secret a line had given away. So a secret gets a short name the way
    /// everything the model talks about does. "The innkeeper's brother", "the sealed cellar".
    /// </para>
    /// <para>
    /// The cost of that is worth stating: a name is not an identity. Two characters holding a secret
    /// of the same name hold, for every purpose here, the same secret - which is usually what was
    /// meant, and is the mechanism by which several people can be in on one thing. Renaming a secret
    /// by hand orphans any ledger entry that named it, and nothing detects that.
    /// </para>
    /// </summary>
    internal sealed class Secret
    {
        /// <summary>
        /// The short handle everybody says out loud - the narrator when reporting what a line
        /// revealed, a person when editing the file. A label, not prose.
        /// </summary>
        /// <remarks>
        /// Matched case-insensitively on <see cref="SaveStore.Matches"/>, the rule every other name
        /// lookup in the save follows.
        /// </remarks>
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// How far along it is, and therefore who may be told. Defaults to
        /// <see cref="SecretStage.Dormant"/>, which is silent - see the enum on why that direction.
        /// </summary>
        public SecretStage Stage { get; set; }

        /// <summary>
        /// What the secret actually is, as prose, with <c>{This}</c> and <c>{Player}</c> left
        /// unresolved exactly as a <see cref="Memory"/> leaves them.
        /// <para>
        /// The only string in a save with no player-facing render path anywhere in the program. The
        /// transcript does not print it, the status pane does not, and the player commands do not. If
        /// something is ever added that does, that addition is the leak - not this field.
        /// </para>
        /// </summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// The turn it was granted on, so a new pressure can be weighed against an old one the way a
        /// recent memory is weighed against an old one.
        /// </summary>
        public int Turn { get; set; }
    }

    /// <summary>
    /// The operations on a character's secrets, and the one place the stage rules are written down.
    /// <para>
    /// Deliberately without the <c>All</c> and <c>Seed</c> that <see cref="CharacterAttributes"/> has,
    /// and the difference is not an inconsistency. The six attributes are present in effect whether or
    /// not the file says so, because a check that had to invent the attribute it needed would be a
    /// check the model decided the terms of. A character with no secrets, by contrast, simply has
    /// none: there is nothing to fill in, and filling anything in would be inventing plot.
    /// </para>
    /// </summary>
    internal static class Secrets
    {
        /// <summary>The name as it should be stored: trimmed, or null when nothing usable was asked for.</summary>
        public static string? CanonicalName(string? name) =>
            name is { Length: > 0 } && !name.AsSpan().IsWhiteSpace() ? name.Trim() : null;

        /// <summary>
        /// One of a character's secrets by name, whatever stage it is at. Null when they hold no such
        /// thing.
        /// </summary>
        public static Secret? Find(Character character, string? name)
        {
            ArgumentNullException.ThrowIfNull(character);

            return CanonicalName(name) is { } wanted
                ? character.Secrets.Find(secret => SaveStore.Matches(secret.Name, wanted))
                : null;
        }

        /// <summary>
        /// Whether a character knows a secret well enough to act on it - live or spent, never dormant.
        /// </summary>
        /// <remarks>
        /// That a dormant secret does <em>not</em> count is the load-bearing line in this file. A
        /// holder of a dormant secret behaves as though unaware, so for every purpose that asks this
        /// question they are unaware: reading one character and then another is still refused when the
        /// second one's copy of the secret is asleep. Counting it would let a hand-edit that meant to
        /// keep somebody ignorant quietly open the gate instead.
        /// </remarks>
        public static bool Holds(Character character, string? name) =>
            Find(character, name) is { Stage: SecretStage.Live or SecretStage.Spent };

        /// <summary>Their secrets at one stage, in the order the file holds them.</summary>
        public static IEnumerable<Secret> AtStage(Character character, SecretStage stage)
        {
            ArgumentNullException.ThrowIfNull(character);

            return character.Secrets.Where(secret => secret.Stage == stage);
        }

        /// <summary>
        /// Gives a character a secret, live, stamped with the turn. The caller writes the file.
        /// </summary>
        /// <remarks>
        /// Live rather than dormant, which is the opposite of the default the stage itself carries.
        /// Nothing yet exists to wake a dormant secret, and one created asleep with nothing able to
        /// rouse it would be invisible for the rest of the campaign - worse than no secret at all. A
        /// human adjudicates by editing the save, which is what the file format was built for.
        /// </remarks>
        public static Secret Grant(Character character, string name, string text, int turn)
        {
            ArgumentNullException.ThrowIfNull(character);

            var secret = new Secret
            {
                Name = CanonicalName(name) ?? string.Empty,
                Stage = SecretStage.Live,
                Text = text.Trim(),
                Turn = turn,
            };

            character.Secrets.Add(secret);
            return secret;
        }

        /// <summary>
        /// Turns one of a character's live secrets spent. The caller writes the file.
        /// </summary>
        /// <returns>
        /// False when they hold no such secret, or hold it dormant, or it was spent already - a
        /// secret the narrator was never handed is one it cannot have revealed.
        /// </returns>
        public static bool Spend(Character character, string? name)
        {
            if (Find(character, name) is not { Stage: SecretStage.Live } secret)
            {
                return false;
            }

            secret.Stage = SecretStage.Spent;
            return true;
        }
    }
}
