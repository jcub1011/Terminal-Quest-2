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

        /// <summary>One memory, resolved, stamped with the turn it was formed on.</summary>
        public static string Memory(Memory memory, string owner, string? playerName) =>
            $"  [turn {memory.Turn}] {Placeholders.Resolve(memory.Text, owner, playerName)}";

        /// <summary>A location's headline: name and who is standing in it.</summary>
        public static string LocationLine(Location location)
        {
            var present = location.Characters.Count == 0
                ? "nobody here"
                : string.Join(", ", location.Characters);

            return $"{location.Name} ({present})";
        }

        /// <summary>Full location record, history included.</summary>
        public static string Location(Location location, string? playerName)
        {
            var text = new StringBuilder();
            text.AppendLine(location.Name);

            if (location.Description.Length > 0)
            {
                text.AppendLine(location.Description);
            }

            text.AppendLine(location.Characters.Count == 0
                ? "Here now: nobody."
                : $"Here now: {string.Join(", ", location.Characters)}.");

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

        /// <summary>The lowercase wire spelling of a kind, matching what the JSON holds.</summary>
        public static string Kind(CharacterKind kind) => kind == CharacterKind.Player ? "player" : "npc";
    }
}
