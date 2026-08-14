using System.Text;

using TerminalQuest.Saves;

namespace TerminalQuest.Mcp
{
    /// <summary>
    /// Renders save records as the plain text a tool call returns.
    /// <para>
    /// Text rather than JSON, deliberately. The consumer is a language model that is about to
    /// write prose from this, and it reads a line like <c>Bess (npc) - HP 12/12</c> more reliably
    /// than the same fact wrapped in braces and quotes - for a fraction of the tokens, on every
    /// call, for the whole session.
    /// </para>
    /// <para>
    /// Every path here resolves placeholders. The model wrote <c>{This}</c> going in; it should
    /// never have to think about it coming back out.
    /// </para>
    /// </summary>
    internal static class QuestRender
    {
        /// <summary>A character's headline: name, kind and health.</summary>
        public static string CharacterLine(Character character) =>
            $"{character.Name} ({Kind(character.Kind)}) - HP {character.Health}/{character.MaxHealth}";

        /// <summary>Full character record, memories included.</summary>
        public static string Character(Character character, string? playerName)
        {
            var text = new StringBuilder();
            text.AppendLine(CharacterLine(character));

            if (character.Description.Length > 0)
            {
                text.AppendLine(character.Description);
            }

            text.AppendLine(Attributes(character));

            if (character.Memories.Count == 0)
            {
                text.AppendLine("Memories: none yet.");
            }
            else
            {
                text.AppendLine("Memories:");
                foreach (var memory in character.Memories)
                {
                    text.AppendLine(Memory(memory, character.Name, playerName));
                }
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// What a character is made of, on one line, each score with the modifier a roll would
        /// actually apply.
        /// </summary>
        /// <remarks>
        /// The modifier is spelled out beside the score rather than left to be worked out. The
        /// narrator does not need it - the resolver applies it - but it is what makes the line
        /// readable as a description of somebody, and a model that can see "Strength 16 (+3)" is far
        /// less tempted to invent a bonus than one handed a bare 16.
        /// <para>
        /// Names in full, not the three-letter forms everyone knows the six by. The saving would be
        /// some forty tokens on a response that is cached after the first turn, against the narrator
        /// echoing "DEX" back as an argument - which the lookup forgives, but a shape the model never
        /// sees is a shape it never guesses wrong.
        /// </para>
        /// </remarks>
        public static string Attributes(Character character)
        {
            var parts = CharacterAttributes.All(character)
                .Select(attribute =>
                    $"{attribute.Name} {attribute.Score} ({CharacterAttributes.Sign(CharacterAttributes.Modifier(attribute.Score))})");

            return $"Attributes: {string.Join(", ", parts)}";
        }

        /// <summary>
        /// One roll, as the narrator reads it back.
        /// </summary>
        /// <remarks>
        /// The total is here whether or not the roll was hidden. Hiding governs what the
        /// <em>player</em> is told; a narrator that could not see its own dice could not describe
        /// what they did, which is the one job it has left once the resolver owns the number.
        /// </remarks>
        public static string Roll(DiceRoll roll, string? rollerName)
        {
            var who = rollerName is { Length: > 0 } ? rollerName : "Something";
            var faces = roll.Faces.Count > 0 ? $" [{string.Join(", ", roll.Faces)}]" : string.Empty;

            var modifier = roll.Attribute is { Length: > 0 }
                ? $" {CharacterAttributes.Sign(roll.Modifier)} {roll.Attribute}"
                : string.Empty;

            return $"{who} rolled {roll.Notation} for {roll.Reason}:{faces}{modifier} = {roll.Total}.";
        }

        /// <summary>One memory, resolved, stamped with the turn it was formed on.</summary>
        public static string Memory(Memory memory, string owner, string? playerName) =>
            $"  [turn {memory.Turn}] {Placeholders.Resolve(memory.Text, owner, playerName)}";

        /// <summary>
        /// The secret block of a knowledge fetch: what this character is holding, then what the player
        /// has already been told and anybody may now speak of. Empty when there is neither.
        /// </summary>
        /// <remarks>
        /// Takes the two lists rather than the character, and that is the point of the signature: a
        /// character carries dormant secrets, and this must be incapable of printing one. The only
        /// producer of either argument is <see cref="SecretGate.Release"/>.
        /// <para>
        /// The shared block is worded as permission rather than as information, because the narrator's
        /// question about a spent secret is not what it says - it may well have just said it - but
        /// whether the thing still has to be handled carefully.
        /// </para>
        /// </remarks>
        public static string Secrets(
            IReadOnlyList<Secret> held,
            IReadOnlyList<Secret> common,
            string owner,
            string? playerName)
        {
            if (held.Count == 0 && common.Count == 0)
            {
                return string.Empty;
            }

            var text = new StringBuilder();

            if (held.Count > 0)
            {
                text.AppendLine($"Holding in secret - {owner} may act on these, and nobody else knows them:");

                foreach (var secret in held)
                {
                    text.AppendLine(Secret(secret, owner, playerName));
                }
            }

            if (common.Count > 0)
            {
                text.AppendLine("Common knowledge now - the player has been told these, and anyone may speak of them:");

                foreach (var secret in common)
                {
                    text.AppendLine(Secret(secret, owner, playerName));
                }
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// One secret: its name, the turn it was granted on, and what it is. The name comes first
        /// because it is the handle the narrator has to say back when a line gives the secret away.
        /// </summary>
        private static string Secret(Secret secret, string owner, string? playerName) =>
            $"  {secret.Name} - [turn {secret.Turn}] {Placeholders.Resolve(secret.Text, owner, playerName)}";

        /// <summary>A location's headline: name and who is standing in it.</summary>
        /// <param name="index">
        /// Resolves the roster, which holds ids. The model must never see one, so this is required
        /// rather than optional - see <see cref="WorldIndex"/>.
        /// </param>
        public static string LocationLine(Location location, WorldIndex index)
        {
            var roster = Roster(location, index);

            return $"{location.Name} ({(roster.Length == 0 ? "nobody here" : roster)})";
        }

        /// <summary>Full location record, history included.</summary>
        public static string Location(Location location, WorldIndex index, string? playerName)
        {
            var text = new StringBuilder();
            text.AppendLine(location.Name);

            if (location.Description.Length > 0)
            {
                text.AppendLine(location.Description);
            }

            var roster = Roster(location, index);

            text.AppendLine(roster.Length == 0
                ? "Here now: nobody."
                : $"Here now: {roster}.");

            if (location.Events.Count == 0)
            {
                text.AppendLine("History: nothing has happened here yet.");
            }
            else
            {
                text.AppendLine("History:");
                foreach (var entry in location.Events)
                {
                    text.AppendLine(LocationEvent(entry, location.Name, playerName));
                }
            }

            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// Who is present, by name. Empty when nobody is - which an id that resolves to nobody also
        /// counts as, since a reference to a character no longer on record describes an empty room
        /// more truthfully than it describes anything else.
        /// </summary>
        private static string Roster(Location location, WorldIndex index) =>
            string.Join(", ", index.NamesOf(location.CharacterIds));

        /// <summary>One durable change to a place, resolved.</summary>
        public static string LocationEvent(LocationEvent entry, string owner, string? playerName) =>
            $"  [turn {entry.Turn}] {Placeholders.Resolve(entry.Text, owner, playerName)}";

        /// <summary>The purse, worded so that nought reads as a fact rather than a missing value.</summary>
        public static string Money(int amount) =>
            amount == 0 ? "Money: none." : $"Money: {amount} coin.";

        public static string Item(Item item) =>
            item.Description.Length > 0
                ? $"  {item.Name} x{item.Quantity} - {item.Description}"
                : $"  {item.Name} x{item.Quantity}";

        public static string StoryEvent(StoryEvent entry)
        {
            var line = $"  [turn {entry.Turn}] {entry.Title}";
            return entry.Detail.Length > 0 ? $"{line} - {entry.Detail}" : line;
        }

        /// <summary>
        /// The recalled conversation, oldest last, and whose move it is.
        /// </summary>
        /// <remarks>
        /// The only renderer here that resolves no placeholders, because there are none to resolve:
        /// this is prose as it was shown, not a record written with <c>{This}</c> in it. Markup is left
        /// alone for the same reason - the narrator wrote those tags and reads them back as its own
        /// hand, which is most of what makes a recalled scene worth having.
        /// <para>
        /// The closing line is the point of the whole call as much as the prose is. A resumed session
        /// that left the narrator mid-sentence has a player line nobody answered, and without being
        /// told so the narrator opens a fresh scene over the top of it.
        /// </para>
        /// </remarks>
        public static string Transcript(IReadOnlyList<TranscriptEntry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);

            if (entries.Count == 0)
            {
                return "Nothing of the last session was recorded. Set the scene from the world itself.";
            }

            var text = new StringBuilder();
            text.AppendLine("The end of the last session, oldest first.");
            text.AppendLine();

            foreach (var entry in entries)
            {
                text.AppendLine(
                    $"{(entry.Voice == TranscriptVoice.Player ? "PLAYER" : "NARRATOR")}: {entry.Text}");
            }

            text.AppendLine();
            text.AppendLine(TranscriptRecall.AwaitingNarrator(entries)
                ? "The player's last line has not been answered. The session ended while you were still "
                + "speaking and what you had written was discarded, so answer that line rather than "
                + "opening a new scene over it."
                : "The player has not replied to that yet.");

            return text.ToString().TrimEnd();
        }

        /// <summary>The lowercase wire spelling of a kind, matching what the JSON holds.</summary>
        public static string Kind(CharacterKind kind) => kind == CharacterKind.Player ? "player" : "npc";
    }
}
