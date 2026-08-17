using System.Text;

using TerminalQuest.Saves;

namespace TerminalQuest.Mcp
{
    /// <summary>
    /// Renders save records as the plain text a tool call returns.
    /// </summary>
    internal static class QuestRender
    {
        public static string CharacterLine(Character character) =>
            $"{character.Name} ({Kind(character.Kind)}) - HP {character.Health}/{character.MaxHealth}";

        public static string Character(
            Character character,
            CharacterInventory? inventory = null,
            ItemFile? itemFile = null)
        {
            var text = new StringBuilder();
            text.AppendLine(CharacterLine(character));

            if (character.Description.Length > 0)
            {
                text.AppendLine(character.Description);
            }

            text.AppendLine(Attributes(character));

            if (inventory is not null && itemFile is not null)
            {
                text.AppendLine(Inventory(inventory, itemFile));
            }

            return text.ToString().TrimEnd();
        }

        public static string Attributes(Character character)
        {
            var parts = CharacterAttributes.All(character)
                .Select(attribute =>
                    $"{attribute.Name} {attribute.Score} ({CharacterAttributes.Sign(CharacterAttributes.Modifier(attribute.Score))})");

            return $"Attributes: {string.Join(", ", parts)}";
        }

        public static string Inventory(CharacterInventory inventory, ItemFile itemFile)
        {
            var text = new StringBuilder();
            text.AppendLine(Money(inventory.Money));

            if (inventory.Items.Count == 0)
            {
                text.AppendLine("Carrying: (nothing else)");
            }
            else
            {
                text.AppendLine("Carrying:");
                foreach (var stack in inventory.Items)
                {
                    var def = SaveStore.FindItemById(itemFile, stack.ItemId);
                    if (def is not null)
                    {
                        text.AppendLine(Item(def, stack.Quantity));
                    }
                }
            }

            return text.ToString().TrimEnd();
        }

        public static string Roll(DiceRoll roll, string? rollerName)
        {
            var who = rollerName is { Length: > 0 } ? rollerName : "Something";
            var faces = roll.Faces.Count > 0 ? $" [{string.Join(", ", roll.Faces)}]" : string.Empty;

            var modifier = roll.Attribute is { Length: > 0 }
                ? $" {CharacterAttributes.Sign(roll.Modifier)} {roll.Attribute}"
                : string.Empty;

            var situational = roll.SituationalModifier != 0
                ? $" {CharacterAttributes.Sign(roll.SituationalModifier)} situational"
                : string.Empty;

            return $"{who} rolled {roll.Notation} for {roll.Reason}:{faces}{modifier}{situational} = {roll.Total}.";
        }

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

        private static string Secret(Secret secret, string owner, string? playerName) =>
            $"  {secret.Name} - [turn {secret.Turn}] {Placeholders.Resolve(secret.Text, owner, playerName)}";

        public static string LocationLine(Location location, WorldIndex index)
        {
            var roster = Roster(location, index);

            return $"{location.Name} ({(roster.Length == 0 ? "nobody here" : roster)})";
        }

        public static string Location(
            Location location,
            WorldIndex index,
            ItemFile? itemFile = null,
            IReadOnlyList<StoryEvent>? recentEvents = null)
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

            if (location.Items.Count > 0 && itemFile is not null)
            {
                text.AppendLine("Items here:");
                foreach (var stack in location.Items)
                {
                    var def = SaveStore.FindItemById(itemFile, stack.ItemId);
                    if (def is not null)
                    {
                        text.AppendLine(Item(def, stack.Quantity));
                    }
                }
            }

            if (recentEvents is { Count: > 0 })
            {
                text.AppendLine("Recent history here:");
                foreach (var ev in recentEvents)
                {
                    text.AppendLine(StoryEvent(ev));
                }
            }

            return text.ToString().TrimEnd();
        }

        private static string Roster(Location location, WorldIndex index) =>
            string.Join(", ", index.NamesOf(location.CharacterIds));

        public static string Money(int amount) =>
            amount == 0 ? "Money: none." : $"Money: {amount} coin.";

        public static string Item(ItemDefinition item, int quantity) =>
            item.Description.Length > 0
                ? $"  {item.Name} x{quantity} - {item.Description}"
                : $"  {item.Name} x{quantity}";

        public static string StoryEvent(StoryEvent entry)
        {
            var line = $"  [turn {entry.Turn}] {entry.Title}";
            return entry.Detail.Length > 0 ? $"{line} - {entry.Detail}" : line;
        }

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

        public static string Directive(DirectiveFile directive)
        {
            ArgumentNullException.ThrowIfNull(directive);

            var text = new StringBuilder();
            text.AppendLine("[DIRECTIVE from Director]");

            if (!string.IsNullOrWhiteSpace(directive.Tone))
            {
                text.AppendLine($"Tone/Tension: {directive.Tone.Trim()}");
            }

            if (!string.IsNullOrWhiteSpace(directive.PacingNote))
            {
                text.AppendLine($"Pacing Guidance: {directive.PacingNote.Trim()}");
            }

            if (directive.SecretPromotions.Count > 0)
            {
                text.AppendLine($"Activated Secrets in Play: {string.Join(", ", directive.SecretPromotions)}");
            }

            return text.ToString().TrimEnd();
        }

        public static string UnratifiedClaims(IReadOnlyList<LedgerEntry> claims)
        {
            ArgumentNullException.ThrowIfNull(claims);

            if (claims.Count == 0)
            {
                return "No unratified claims on record.";
            }

            var text = new StringBuilder();
            text.AppendLine("Unratified claims on record:");

            foreach (var claim in claims)
            {
                var speaker = string.IsNullOrWhiteSpace(claim.Speaker) ? "Narrator" : claim.Speaker;
                text.AppendLine($"  #{claim.Seq} [turn {claim.Turn}] {speaker}: \"{claim.Claim}\"");
            }

            return text.ToString().TrimEnd();
        }

        public static string Kind(CharacterKind kind) => kind == CharacterKind.Player ? "player" : "npc";
    }
}
