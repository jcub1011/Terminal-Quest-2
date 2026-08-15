using System.Text;
using System.Text.Json;

using TerminalQuest.Saves;
using TerminalQuest.Settings;

namespace TerminalQuest.Mcp
{
    /// <summary>
    /// The tools the narrator uses to read and write the world, and the dispatch behind them.
    /// </summary>
    internal static class QuestTools
    {
        public const string ServerName = "quest";

        private const int DefaultStoryLimit = 20;

        private const int DefaultWordCount = 3;

        private const int MaxWordCount = 10;

        private const int TranscriptTailBytes = 8 * 1024;

        private static readonly string[] NarrationNames =
        [
            "narrator", "narration", "dm", "gm", "game master", "dungeon master", "system", "self", "you",
        ];

        public static IReadOnlyList<QuestTool> Definitions { get; } =
        [
            new("get_state",
                "Read the whole world at once: the player, where they are, who is with them, "
              + "the player's inventory and purse, recent story events, and other characters/places on record. "
              + "Call this before narrating the first scene of a session.",
                """{"type":"object","properties":{}}"""),

            new("get_transcript",
                "The end of the last session word for word - what the player typed and the prose you "
              + "wrote back, markup and all. Call this first when a save is resumed.",
                """
                {"type":"object",
                 "properties":{
                   "characters":{"type":"integer","description":"Roughly how much prose to return, in characters."}}}
                """),

            new("set_character",
                "Create or update a character's state, health, description, or attributes. "
              + "Creating the player is the first thing to do in an empty save. "
              + "Health changes can be given as an absolute new health or as a health_delta (e.g. -3 for damage, +5 for heal). "
              + "Descriptions are appended to what is already on record.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string","description":"Who to create or change, by name."},
                   "kind":{"type":"string","enum":["player","npc"],"description":"Defaults to npc. Exactly one character is the player."},
                   "health":{"type":"integer","description":"Set absolute health score."},
                   "health_delta":{"type":"integer","description":"Relative change to health (e.g. -4 for damage or 5 for healing)."},
                   "max_health":{"type":"integer","description":"Set maximum health."},
                   "description":{"type":"string","description":"Background, aptitude, or newly discovered details (appended)."},
                   "attributes":{"type":"object","description":"Scores map, e.g. {\"Strength\":15,\"Dexterity\":12}."},
                   "new_name":{"type":"string","description":"If renaming this character."}},
                 "required":["name"]}
                """),

            new("get_character",
                "Look up one character: who they are, their health, attributes, what they carry, "
              + "and any secret of theirs in play. Read this before voicing them.",
                """
                {"type":"object",
                 "properties":{"name":{"type":"string","description":"The character's name."}},
                 "required":["name"]}
                """),

            new("grant_secret",
                "Give a character something they know and others do not: what a witness saw, "
              + "who someone answers to, what a debt is really for. "
              + "Never say the name or secret directly to the player.",
                """
                {"type":"object",
                 "properties":{
                   "character":{"type":"string","description":"Who is keeping it."},
                   "name":{"type":"string","description":"A short handle, e.g. 'the sealed cellar'."},
                   "detail":{"type":"string","description":"What the secret actually is. Use {This} for them and {Player} for the player."}},
                 "required":["character","name","detail"]}
                """),

            new("set_location",
                "Create a place, append to its description, or rename it. Call this before moving anyone somewhere new.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string","description":"Which place to create or change."},
                   "description":{"type":"string","description":"Newly discovered sensory details (appended to what it says)."},
                   "new_name":{"type":"string","description":"If renaming the place."}},
                 "required":["name"]}
                """),

            new("get_location",
                "One place in full: its description, who is standing there, items present, and recent events there.",
                """
                {"type":"object",
                 "properties":{"name":{"type":"string","description":"The location's name."}},
                 "required":["name"]}
                """),

            new("move_character",
                "Put a character in a place, taking them out of wherever they were. Call whenever anyone travels, player included.",
                """
                {"type":"object",
                 "properties":{
                   "character":{"type":"string","description":"Who is moving."},
                   "location":{"type":"string","description":"Destination location name."}},
                 "required":["character","location"]}
                """),

            new("modify_item",
                "Add, give, take, or remove items. Positive quantity adds to stack; negative quantity removes. "
              + "Defaults to the player's inventory if character is omitted.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string","description":"The item name."},
                   "quantity":{"type":"integer","description":"Quantity to add (positive) or take (negative). Defaults to 1."},
                   "character":{"type":"string","description":"Whose inventory to modify. Defaults to the player."},
                   "description":{"type":"string","description":"Sensory description for newly introduced items."},
                   "location":{"type":"string","description":"Location if placing or taking from the ground/container instead of a person."}},
                 "required":["name"]}
                """),

            new("modify_money",
                "Give or take coin. Positive amount adds coin; negative amount spends/takes coin. "
              + "Refused if the character cannot afford it. Defaults to player if character is omitted.",
                """
                {"type":"object",
                 "properties":{
                   "amount":{"type":"integer","description":"Amount of coin to add (+) or spend (-)."},
                   "character":{"type":"string","description":"Whose purse to modify. Defaults to player."}},
                 "required":["amount"]}
                """),

            new("record_event",
                "Log an event, memory, or milestone in the story. Name all characters, locations, and items involved. "
              + "This is what restores continuity and what the player reads back with /story.",
                """
                {"type":"object",
                 "properties":{
                   "title":{"type":"string","description":"A short headline, e.g. 'Bess accused Rowan of theft'."},
                   "detail":{"type":"string","description":"What happened, from the perspective of who saw it or what changed."},
                   "characters":{"type":"array","items":{"type":"string"},"description":"Names of characters involved or witnessing."},
                   "locations":{"type":"array","items":{"type":"string"},"description":"Names of places involved."},
                   "items":{"type":"array","items":{"type":"string"},"description":"Names of items involved."},
                   "tags":{"type":"array","items":{"type":"string"},"description":"Optional topic tags."}},
                 "required":["title","detail"]}
                """),

            new("recall",
                "Search past events and memories involving a specific character, place, or item. "
              + "Read this before voicing a character or entering an established place.",
                """
                {"type":"object",
                 "properties":{
                   "character":{"type":"string","description":"Look up events and memories involving this character."},
                   "location":{"type":"string","description":"Look up events that happened at this location."},
                   "item":{"type":"string","description":"Look up events involving this item."},
                   "query":{"type":"string","description":"Optional keyword search in event text."},
                   "limit":{"type":"integer","description":"Max events to return. Defaults to 10."}}}
                """),

            new("roll",
                "Settle an uncertain outcome with dice rather than deciding it yourself. "
              + "Call before narrating and obey the number.",
                """
                {"type":"object",
                 "properties":{
                   "notation":{"type":"string","description":"Standard dice notation: 1d20, 2d6+3, 2d20kh1 for advantage, 2d20kl1 for disadvantage."},
                   "reason":{"type":"string","description":"What is being decided, e.g. 'leaping the chasm'."},
                   "character":{"type":"string","description":"Who is rolling, by name. Omit for traps or the world."},
                   "attribute":{"type":"string","description":"Attribute supplying the modifier, e.g. Dexterity."},
                   "hidden":{"type":"boolean","description":"True keeps the total from the player."}},
                 "required":["notation","reason"]}
                """),

            new("reveal_roll",
                "Show the player the result of a roll previously kept hidden.",
                """
                {"type":"object",
                 "properties":{
                   "character":{"type":"string","description":"Narrow to rolls this character made. Optional."},
                   "reason":{"type":"string","description":"Narrow to the roll whose reason contains this. Optional."}}}
                """),

            new("record_claims",
                "Write down what this turn's prose will assert, called right before you narrate prose.",
                """
                {"type":"object",
                 "properties":{
                   "claims":{"type":"array","description":"One entry per assertion.",
                     "items":{"type":"object",
                       "properties":{
                         "claim":{"type":"string","description":"The assertion in one plain sentence."},
                         "speaker":{"type":"string","description":"Who asserted it, by name. Leave blank for narration."},
                         "truth":{"type":"string","enum":["true","lie","mistaken"],"description":"Defaults to true."},
                         "reveals":{"type":"string","description":"Name of a secret this gave away, if any."}},
                       "required":["claim"]}}},
                 "required":["claims"]}
                """),

            new("random_noun",
                "Draw ordinary words at random to start somewhere you would not have chosen. Seeds only.",
                """
                {"type":"object",
                 "properties":{"count":{"type":"integer","description":"How many words. Defaults to 3, at most 10."}}}
                """),

            new("random_adjective",
                "Draw qualities at random. Pair with a noun to seed new ideas. Seeds only.",
                """
                {"type":"object",
                 "properties":{"count":{"type":"integer","description":"How many words. Defaults to 3, at most 10."}}}
                """),
        ];

        public static string AllowedTools() =>
            string.Join(',', Definitions.Select(tool => $"mcp__{ServerName}__{tool.Name}"));

        public static ToolOutcome Invoke(SaveStore store, string name, JsonElement arguments)
        {
            ToolOutcome outcome;

            try
            {
                outcome = SecretGate.Refusal(store, name, arguments) ?? Dispatch(store, name, arguments);
            }
            catch (Exception ex)
            {
                QuestJournal.Record(store, name, arguments, failed: true, error: ex.Message);
                throw;
            }

            QuestJournal.Record(store, name, arguments, outcome.IsError, error: string.Empty);
            return outcome;
        }

        private static ToolOutcome Dispatch(SaveStore store, string name, JsonElement arguments) => name switch
        {
            "get_state" => GetState(store),
            "get_transcript" => GetTranscript(store, arguments),
            "set_character" => SetCharacter(store, arguments),
            "get_character" => GetCharacter(store, arguments),
            "grant_secret" => GrantSecret(store, arguments),
            "set_location" => SetLocation(store, arguments),
            "get_location" => GetLocation(store, arguments),
            "move_character" => MoveCharacter(store, arguments),
            "modify_item" => ModifyItem(store, arguments),
            "modify_money" => ModifyMoney(store, arguments),
            "record_event" => RecordEvent(store, arguments),
            "recall" => Recall(store, arguments),
            "roll" => Roll(store, arguments),
            "reveal_roll" => RevealRoll(store, arguments),
            "record_claims" => RecordClaims(store, arguments),
            "random_noun" => RandomWords(arguments, WordBank.Nouns),
            "random_adjective" => RandomWords(arguments, WordBank.Adjectives),
            _ => ToolOutcome.Fail($"There is no tool called '{name}'."),
        };

        private static ToolOutcome GetState(SaveStore store)
        {
            var characters = store.ReadCharacters();
            var locations = store.ReadLocations();
            var items = store.ReadItems();
            var inventory = store.ReadInventory();
            var story = store.Story.Read().Entries;
            var metadata = store.ReadMetadata();

            var player = SaveStore.Player(characters);

            var text = new StringBuilder();
            text.AppendLine($"Save '{store.Name}', turn {metadata.Turn}.");
            text.AppendLine();

            if (player is null)
            {
                text.AppendLine(
                    "There is no player on record, which should not happen - the player character "
                  + "is created before the session starts. Say so plainly rather than inventing "
                  + "one, and do not narrate a scene.");
                return ToolOutcome.Ok(text.ToString().TrimEnd());
            }

            var index = WorldIndex.Build(characters, locations, items);
            var playerInv = inventory.Find(player.Id);

            text.AppendLine("PLAYER");
            text.AppendLine(QuestRender.Character(player, playerInv, items));
            text.AppendLine();

            text.AppendLine("WHERE THEY ARE");
            var here = SaveStore.WhereIs(locations, player.Id);
            if (here is null)
            {
                text.AppendLine("Nowhere on record. Call set_location and move_character.");
            }
            else
            {
                var recentHere = story
                    .Where(ev => ev.LocationIds.Contains(here.Id, StringComparer.Ordinal))
                    .TakeLast(5)
                    .ToList();
                text.AppendLine(QuestRender.Location(here, index, items, recentHere));
            }

            text.AppendLine();
            text.AppendLine("STORY SO FAR");
            if (story.Count == 0)
            {
                text.AppendLine("  (nothing recorded yet)");
            }
            else
            {
                foreach (var entry in story.TakeLast(DefaultStoryLimit))
                {
                    text.AppendLine(QuestRender.StoryEvent(entry));
                }
            }

            text.AppendLine();
            text.AppendLine("OTHERS ON RECORD");
            var others = characters.Characters
                .Where(character => !string.Equals(character.Id, player.Id, StringComparison.Ordinal))
                .ToList();
            text.AppendLine(others.Count == 0
                ? "  (nobody yet)"
                : "  " + string.Join(", ", others.Select(QuestRender.CharacterLine)));

            text.AppendLine();
            text.AppendLine("PLACES ON RECORD");
            text.AppendLine(locations.Locations.Count == 0
                ? "  (nowhere yet)"
                : "  " + string.Join(", ", locations.Locations.Select(location => location.Name)));

            return ToolOutcome.Ok(text.ToString().TrimEnd());
        }

        private static ToolOutcome GetTranscript(SaveStore store, JsonElement arguments)
        {
            var characters = TranscriptRecall.Clamp(
                Number(arguments, "characters") ?? SettingsStore.Read().TranscriptRecallCharacters);

            var recent = store.Transcript.Tail(Math.Max(TranscriptTailBytes, characters * 4));

            return ToolOutcome.Ok(QuestRender.Transcript(TranscriptRecall.Tail(recent, characters)));
        }

        private static ToolOutcome SetCharacter(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("set_character needs a name.");
            }

            var file = store.ReadCharacters();
            var character = SaveStore.FindCharacter(file, name);
            var isNew = character is null;

            if (character is null)
            {
                character = new Character { Id = file.TakeId(), Name = name.Trim() };
                file.Characters.Add(character);
            }
            else if (character.Id.Length == 0)
            {
                character.Id = file.TakeId();
            }

            if (Text(arguments, "new_name") is { Length: > 0 } newName && !SaveStore.Matches(character.Name, newName))
            {
                var proposed = newName.Trim();
                if (SaveStore.FindCharacter(file, proposed) is { } clash && !ReferenceEquals(clash, character))
                {
                    return ToolOutcome.Fail($"There is already a character called '{clash.Name}'.");
                }
                character.Name = proposed;
            }

            if (Text(arguments, "kind") is { Length: > 0 } kind)
            {
                if (!TryParseKind(kind, out var parsed))
                {
                    return ToolOutcome.Fail($"'{kind}' is not a valid kind. Use 'player' or 'npc'.");
                }
                character.Kind = parsed;
            }

            var maxHealth = Number(arguments, "max_health") ?? Number(arguments, "maxHealth");
            if (maxHealth is { } max)
            {
                character.MaxHealth = Math.Max(1, max);
            }
            else if (isNew)
            {
                character.MaxHealth = 20;
            }

            var health = Number(arguments, "health");
            var healthDelta = Number(arguments, "health_delta") ?? Number(arguments, "healthDelta");

            if (health is { } current)
            {
                character.Health = Math.Max(0, current);
            }
            else if (healthDelta is { } delta)
            {
                character.Health = Math.Max(0, character.Health + delta);
            }
            else if (isNew)
            {
                character.Health = character.MaxHealth;
            }

            if (Text(arguments, "description") is { Length: > 0 } description)
            {
                if (Descriptions.Extend(character.Description, description) is not { } extended)
                {
                    return ToolOutcome.Fail($"{character.Name} already carries as much description as they can hold.");
                }
                character.Description = extended;
            }

            if (isNew)
            {
                CharacterAttributes.Seed(character, null);
            }

            foreach (var (attribute, score) in Scores(arguments, "attributes"))
            {
                CharacterAttributes.Set(character, attribute, score);
            }

            store.WriteCharacters(file);

            return ToolOutcome.Ok(
                $"{(isNew ? "Created" : "Updated")}: {QuestRender.CharacterLine(character)}"
              + HealthNote(character));
        }

        private static ToolOutcome GetCharacter(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("get_character needs a name.");
            }

            var characters = store.ReadCharacters();
            var character = SaveStore.FindCharacter(characters, name);

            if (character is null)
            {
                return ToolOutcome.Fail($"There is no character named '{name}'. Use set_character to create them.");
            }

            var (held, common) = SecretGate.Release(character, characters.Characters);
            var inventory = store.ReadInventory().Find(character.Id);
            var itemFile = store.ReadItems();

            return ToolOutcome.Ok(Joined(
                QuestRender.Character(character, inventory, itemFile),
                QuestRender.Secrets(held, common, character.Name, SaveStore.PlayerName(characters))));
        }

        private static ToolOutcome GrantSecret(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "character") is not { Length: > 0 } who)
            {
                return ToolOutcome.Fail("grant_secret needs a character.");
            }

            if (Secrets.CanonicalName(Text(arguments, "name")) is not { } name)
            {
                return ToolOutcome.Fail("grant_secret needs a short name for the secret.");
            }

            if (Text(arguments, "detail") is not { Length: > 0 } detail || detail.AsSpan().IsWhiteSpace())
            {
                return ToolOutcome.Fail($"grant_secret needs the detail of '{name}'.");
            }

            var file = store.ReadCharacters();
            var character = SaveStore.FindCharacter(file, who);

            if (character is null)
            {
                return ToolOutcome.Fail($"There is no character named '{who}'. Create them with set_character first.");
            }

            if (Secrets.Find(character, name) is { } existing)
            {
                return ToolOutcome.Fail($"{character.Name} already holds a secret called '{existing.Name}'.");
            }

            var granted = Secrets.Grant(character, name, detail, store.CurrentTurn());
            store.WriteCharacters(file);

            return ToolOutcome.Ok(
                $"{character.Name} now keeps '{granted.Name}'. Nobody else knows it.");
        }

        private static ToolOutcome SetLocation(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("set_location needs a name.");
            }

            var file = store.ReadLocations();
            var location = SaveStore.FindLocation(file, name);
            var isNew = location is null;

            if (location is null)
            {
                location = new Saves.Location { Id = file.TakeId(), Name = name.Trim() };
                file.Locations.Add(location);
            }
            else if (location.Id.Length == 0)
            {
                location.Id = file.TakeId();
            }

            if (Text(arguments, "new_name") is { Length: > 0 } newName && !SaveStore.Matches(location.Name, newName))
            {
                var proposed = newName.Trim();
                if (SaveStore.FindLocation(file, proposed) is { } clash && !ReferenceEquals(clash, location))
                {
                    return ToolOutcome.Fail($"There is already a place called '{clash.Name}'.");
                }
                location.Name = proposed;
            }

            if (Text(arguments, "description") is { Length: > 0 } description)
            {
                if (Descriptions.Extend(location.Description, description) is not { } extended)
                {
                    return ToolOutcome.Fail($"{location.Name} already carries as much description as it can hold.");
                }
                location.Description = extended;
            }

            store.WriteLocations(file);

            return ToolOutcome.Ok($"{(isNew ? "Created" : "Updated")}: {location.Name}");
        }

        private static ToolOutcome GetLocation(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("get_location needs a name.");
            }

            var file = store.ReadLocations();
            var location = SaveStore.FindLocation(file, name);

            if (location is null)
            {
                return ToolOutcome.Fail($"There is no place named '{name}'. Create it with set_location first.");
            }

            var characters = store.ReadCharacters();
            var items = store.ReadItems();
            var index = WorldIndex.Build(characters, file, items);
            var story = store.Story.Read().Entries;
            var recentEvents = story
                .Where(ev => ev.LocationIds.Contains(location.Id, StringComparer.Ordinal))
                .TakeLast(5)
                .ToList();

            return ToolOutcome.Ok(QuestRender.Location(location, index, items, recentEvents));
        }

        private static ToolOutcome MoveCharacter(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "character") is not { Length: > 0 } characterName)
            {
                return ToolOutcome.Fail("move_character needs a character.");
            }

            if (Text(arguments, "location") is not { Length: > 0 } locationName)
            {
                return ToolOutcome.Fail("move_character needs a location.");
            }

            var characters = store.ReadCharacters();
            var character = SaveStore.FindCharacter(characters, characterName);

            if (character is null)
            {
                return ToolOutcome.Fail($"There is no character named '{characterName}'. Create them with set_character first.");
            }

            var locations = store.ReadLocations();
            var destination = SaveStore.FindLocation(locations, locationName);

            if (destination is null)
            {
                return ToolOutcome.Fail($"There is no place named '{locationName}'. Create it with set_location first.");
            }

            if (!store.MoveCharacter(character.Id, destination.Id))
            {
                return ToolOutcome.Fail($"There is no place named '{locationName}'.");
            }

            var moved = SaveStore.FindLocationById(store.ReadLocations(), destination.Id)!;
            return ToolOutcome.Ok($"Moved. {QuestRender.LocationLine(moved, WorldIndex.Build(characters, locations, store.ReadItems()))}");
        }

        private static ToolOutcome ModifyItem(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("modify_item needs an item name.");
            }

            var quantity = Number(arguments, "quantity") ?? 1;
            if (quantity == 0)
            {
                return ToolOutcome.Fail("modify_item needs a non-zero quantity.");
            }

            var itemFile = store.ReadItems();
            var itemDef = SaveStore.FindItem(itemFile, name);

            if (itemDef is null)
            {
                if (quantity < 0)
                {
                    return ToolOutcome.Fail($"No item named '{name}' exists to remove.");
                }

                itemDef = new ItemDefinition
                {
                    Id = itemFile.TakeId(),
                    Name = name.Trim(),
                    Description = Text(arguments, "description") ?? string.Empty,
                };
                itemFile.Items.Add(itemDef);
                store.WriteItems(itemFile);
            }
            else if (Text(arguments, "description") is { Length: > 0 } newDesc)
            {
                itemDef.Description = newDesc;
                store.WriteItems(itemFile);
            }

            var targetLocationName = Text(arguments, "location");
            if (targetLocationName is { Length: > 0 })
            {
                var locFile = store.ReadLocations();
                var location = SaveStore.FindLocation(locFile, targetLocationName);
                if (location is null)
                {
                    return ToolOutcome.Fail($"There is no location named '{targetLocationName}'.");
                }

                var stack = location.Items.Find(s => string.Equals(s.ItemId, itemDef.Id, StringComparison.Ordinal));
                if (quantity > 0)
                {
                    if (stack is null)
                    {
                        location.Items.Add(new ItemStack { ItemId = itemDef.Id, Quantity = quantity });
                    }
                    else
                    {
                        stack.Quantity += quantity;
                    }
                }
                else
                {
                    if (stack is null)
                    {
                        return ToolOutcome.Fail($"'{location.Name}' does not hold '{itemDef.Name}'.");
                    }
                    stack.Quantity += quantity;
                    if (stack.Quantity <= 0)
                    {
                        location.Items.Remove(stack);
                    }
                }

                store.WriteLocations(locFile);
                return ToolOutcome.Ok($"Updated {location.Name} items: {QuestRender.Item(itemDef, Math.Max(0, stack?.Quantity ?? quantity))}");
            }

            var characters = store.ReadCharacters();
            var targetCharName = Text(arguments, "character");
            var character = targetCharName is { Length: > 0 }
                ? SaveStore.FindCharacter(characters, targetCharName)
                : SaveStore.Player(characters);

            if (character is null)
            {
                return ToolOutcome.Fail(targetCharName is { Length: > 0 }
                    ? $"There is no character named '{targetCharName}'."
                    : "No player character on record.");
            }

            var inventoryFile = store.ReadInventory();
            var charInv = inventoryFile.GetOrCreate(character.Id);
            var charStack = charInv.Items.Find(s => string.Equals(s.ItemId, itemDef.Id, StringComparison.Ordinal));

            if (quantity > 0)
            {
                if (charStack is null)
                {
                    charInv.Items.Add(new ItemStack { ItemId = itemDef.Id, Quantity = quantity });
                }
                else
                {
                    charStack.Quantity += quantity;
                }
            }
            else
            {
                if (charStack is null)
                {
                    return ToolOutcome.Fail($"{character.Name} is not carrying '{itemDef.Name}'.");
                }

                charStack.Quantity += quantity;
                if (charStack.Quantity <= 0)
                {
                    charInv.Items.Remove(charStack);
                }
            }

            store.WriteInventory(inventoryFile);
            return ToolOutcome.Ok(
                $"{character.Name}: {(quantity > 0 ? "Gained" : "Lost")} {QuestRender.Item(itemDef, Math.Abs(quantity))}.");
        }

        private static ToolOutcome ModifyMoney(SaveStore store, JsonElement arguments)
        {
            if (Number(arguments, "amount") is not { } amount || amount == 0)
            {
                return ToolOutcome.Fail("modify_money needs a non-zero amount.");
            }

            var characters = store.ReadCharacters();
            var targetCharName = Text(arguments, "character");
            var character = targetCharName is { Length: > 0 }
                ? SaveStore.FindCharacter(characters, targetCharName)
                : SaveStore.Player(characters);

            if (character is null)
            {
                return ToolOutcome.Fail(targetCharName is { Length: > 0 }
                    ? $"There is no character named '{targetCharName}'."
                    : "No player character on record.");
            }

            var inventoryFile = store.ReadInventory();
            var charInv = inventoryFile.GetOrCreate(character.Id);

            if (amount < 0 && charInv.Money < -amount)
            {
                return ToolOutcome.Fail($"{character.Name} cannot afford that. {QuestRender.Money(charInv.Money)}");
            }

            charInv.Money += amount;
            store.WriteInventory(inventoryFile);

            return ToolOutcome.Ok(
                $"{character.Name} {(amount > 0 ? "received" : "spent")} {Math.Abs(amount)} coin. {QuestRender.Money(charInv.Money)}");
        }

        private static ToolOutcome RecordEvent(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "title") is not { Length: > 0 } title)
            {
                return ToolOutcome.Fail("record_event needs a title.");
            }

            var detail = Text(arguments, "detail") ?? string.Empty;

            var characters = store.ReadCharacters();
            var locations = store.ReadLocations();
            var items = store.ReadItems();

            var characterIds = new List<string>();
            foreach (var name in Strings(arguments, "characters"))
            {
                if (SaveStore.FindCharacter(characters, name) is { } c && !characterIds.Contains(c.Id))
                {
                    characterIds.Add(c.Id);
                }
            }

            var locationIds = new List<string>();
            foreach (var name in Strings(arguments, "locations"))
            {
                if (SaveStore.FindLocation(locations, name) is { } l && !locationIds.Contains(l.Id))
                {
                    locationIds.Add(l.Id);
                }
            }

            var itemIds = new List<string>();
            foreach (var name in Strings(arguments, "items"))
            {
                if (SaveStore.FindItem(items, name) is { } i && !itemIds.Contains(i.Id))
                {
                    itemIds.Add(i.Id);
                }
            }

            var entry = new StoryEvent
            {
                Turn = store.CurrentTurn(),
                Title = title.Trim(),
                Detail = detail.Trim(),
                CharacterIds = characterIds,
                LocationIds = locationIds,
                ItemIds = itemIds,
                Tags = Strings(arguments, "tags"),
            };

            store.Story.Append(entry);

            return ToolOutcome.Ok($"Recorded.{Environment.NewLine}{QuestRender.StoryEvent(entry)}");
        }

        private static ToolOutcome Recall(SaveStore store, JsonElement arguments)
        {
            var characters = store.ReadCharacters();
            var locations = store.ReadLocations();
            var items = store.ReadItems();

            var charName = Text(arguments, "character");
            var locName = Text(arguments, "location");
            var itemName = Text(arguments, "item");
            var query = Text(arguments, "query");

            var charId = charName is { Length: > 0 } ? SaveStore.FindCharacter(characters, charName)?.Id : null;
            var locId = locName is { Length: > 0 } ? SaveStore.FindLocation(locations, locName)?.Id : null;
            var itemId = itemName is { Length: > 0 } ? SaveStore.FindItem(items, itemName)?.Id : null;

            var events = store.Story.Read().Entries.AsEnumerable();

            if (charId is not null || charName is { Length: > 0 })
            {
                events = events.Where(ev =>
                    (charId is not null && ev.CharacterIds.Contains(charId, StringComparer.Ordinal))
                    || (charName is not null && (ev.Title.Contains(charName, StringComparison.OrdinalIgnoreCase) || ev.Detail.Contains(charName, StringComparison.OrdinalIgnoreCase))));
            }

            if (locId is not null || locName is { Length: > 0 })
            {
                events = events.Where(ev =>
                    (locId is not null && ev.LocationIds.Contains(locId, StringComparer.Ordinal))
                    || (locName is not null && (ev.Title.Contains(locName, StringComparison.OrdinalIgnoreCase) || ev.Detail.Contains(locName, StringComparison.OrdinalIgnoreCase))));
            }

            if (itemId is not null || itemName is { Length: > 0 })
            {
                events = events.Where(ev =>
                    (itemId is not null && ev.ItemIds.Contains(itemId, StringComparer.Ordinal))
                    || (itemName is not null && (ev.Title.Contains(itemName, StringComparison.OrdinalIgnoreCase) || ev.Detail.Contains(itemName, StringComparison.OrdinalIgnoreCase))));
            }

            if (query is { Length: > 0 })
            {
                events = events.Where(ev =>
                    ev.Title.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || ev.Detail.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            var limit = Number(arguments, "limit") ?? 10;
            var matched = limit > 0 ? events.TakeLast(limit).ToList() : events.ToList();

            string secrets = string.Empty;
            if (charName is { Length: > 0 } && SaveStore.FindCharacter(characters, charName) is { } character)
            {
                var (held, common) = SecretGate.Release(character, characters.Characters);
                secrets = QuestRender.Secrets(held, common, character.Name, SaveStore.PlayerName(characters));
            }

            if (matched.Count == 0)
            {
                return ToolOutcome.Ok(Joined("Nothing matching on record.", secrets));
            }

            var text = new StringBuilder();
            text.AppendLine("Recalled events:");
            foreach (var ev in matched)
            {
                text.AppendLine(QuestRender.StoryEvent(ev));
            }

            return ToolOutcome.Ok(Joined(text.ToString().TrimEnd(), secrets));
        }

        private static ToolOutcome Roll(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "notation") is not { Length: > 0 } notation)
            {
                return ToolOutcome.Fail("roll needs a notation, like 2d6+3 or d20.");
            }

            if (Text(arguments, "reason") is not { Length: > 0 } reason)
            {
                return ToolOutcome.Fail("roll needs a reason.");
            }

            var characters = store.ReadCharacters();
            Character? roller = null;

            if (Text(arguments, "character") is { Length: > 0 } name)
            {
                roller = SaveStore.FindCharacter(characters, name);
                if (roller is null)
                {
                    return ToolOutcome.Fail($"There is no character named '{name}'.");
                }
            }

            var modifier = 0;
            var attributeName = string.Empty;

            if (Text(arguments, "attribute") is { Length: > 0 } attribute)
            {
                if (roller is null)
                {
                    return ToolOutcome.Fail("An attribute belongs to somebody. Name the character rolling.");
                }

                var found = CharacterAttributes.Find(roller, attribute)
                    ?? (CharacterAttributes.IsCore(attribute)
                        ? new CharacterAttribute
                        {
                            Name = CharacterAttributes.CanonicalName(attribute)!,
                            Score = CharacterAttributes.Neutral,
                        }
                        : null);

                if (found is null)
                {
                    var has = string.Join(", ", CharacterAttributes.All(roller).Select(entry => entry.Name));
                    return ToolOutcome.Fail($"{roller.Name} has no attribute called '{attribute}'. They have {has}.");
                }

                if (CarriesFlatTerm(notation))
                {
                    return ToolOutcome.Fail($"{notation} already carries a flat bonus and you also named {found.Name}.");
                }

                attributeName = found.Name;
                modifier = CharacterAttributes.Modifier(found.Score);
            }

            var outcome = Dice.TryRoll(notation, Random.Shared, out var error);
            if (outcome is null)
            {
                return ToolOutcome.Fail(error);
            }

            var roll = new DiceRoll
            {
                Turn = store.CurrentTurn(),
                CharacterId = roller?.Id ?? string.Empty,
                Reason = reason.Trim(),
                Attribute = attributeName,
                Modifier = modifier,
                Notation = outcome.Notation,
                Faces = [.. outcome.Faces],
                Total = outcome.Total + modifier,
                Hidden = Bool(arguments, "hidden") ?? false,
            };

            store.Rolls.Append(roll);

            var text = QuestRender.Roll(roll, roller?.Name);

            return ToolOutcome.Ok(roll.Hidden
                ? text + Environment.NewLine + "Hidden: the player has been shown the notation and reason, but not the number."
                : text);
        }

        private static ToolOutcome RevealRoll(SaveStore store, JsonElement arguments)
        {
            var rolls = store.Rolls.Read().Entries;
            var characters = store.ReadCharacters();

            var wanted = Text(arguments, "character");
            var roller = wanted is { Length: > 0 } ? SaveStore.FindCharacter(characters, wanted) : null;

            if (wanted is { Length: > 0 } && roller is null)
            {
                return ToolOutcome.Fail($"There is no character named '{wanted}'.");
            }

            var reason = Text(arguments, "reason");

            var alreadyRevealed = new HashSet<int>();
            foreach (var r in rolls)
            {
                if (r.RevealsSeq > 0)
                {
                    alreadyRevealed.Add(r.RevealsSeq);
                }
                else if (r.Revealed)
                {
                    alreadyRevealed.Add(r.Seq);
                }
            }

            for (var index = rolls.Count - 1; index >= 0; index--)
            {
                var roll = rolls[index];

                if (!roll.Hidden || roll.RevealsSeq > 0 || alreadyRevealed.Contains(roll.Seq))
                {
                    continue;
                }

                if (roller is not null && !string.Equals(roll.CharacterId, roller.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (reason is { Length: > 0 } && !roll.Reason.Contains(reason, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var reveal = new DiceRoll
                {
                    Turn = store.CurrentTurn(),
                    RevealsSeq = roll.Seq,
                    CharacterId = roll.CharacterId,
                    Reason = roll.Reason,
                    Attribute = roll.Attribute,
                    Modifier = roll.Modifier,
                    Notation = roll.Notation,
                    Faces = [.. roll.Faces],
                    Total = roll.Total,
                    Hidden = true,
                    Revealed = true,
                };

                store.Rolls.Append(reveal);

                var name = SaveStore.FindCharacterById(characters, roll.CharacterId)?.Name;
                return ToolOutcome.Ok($"Revealed.{Environment.NewLine}{QuestRender.Roll(reveal, name)}");
            }

            return ToolOutcome.Fail("There is no hidden roll left to reveal that matches that.");
        }

        private static ToolOutcome RecordClaims(SaveStore store, JsonElement arguments)
        {
            if (arguments.ValueKind != JsonValueKind.Object
                || !arguments.TryGetProperty("claims", out var claims)
                || claims.ValueKind != JsonValueKind.Array
                || claims.GetArrayLength() == 0)
            {
                return ToolOutcome.Fail("record_claims needs at least one claim.");
            }

            var file = store.ReadCharacters();
            var turn = store.CurrentTurn();
            var entries = new List<LedgerEntry>();
            var position = 0;

            foreach (var claim in claims.EnumerateArray())
            {
                position++;

                if (Text(claim, "claim") is not { Length: > 0 } text || text.AsSpan().IsWhiteSpace())
                {
                    return ToolOutcome.Fail($"Claim {position} has no text.");
                }

                var speaker = Text(claim, "speaker");
                var character = speaker is { Length: > 0 } ? SaveStore.FindCharacter(file, speaker) : null;

                if (speaker is { Length: > 0 } && character is null && !IsNarration(speaker))
                {
                    return ToolOutcome.Fail($"There is no character named '{speaker}'. Name someone on record or leave the speaker out.");
                }

                if (!TryTruth(claim, out var truth, out var raw))
                {
                    return ToolOutcome.Fail($"'{raw}' is not a truth status for claim {position}. Use true, lie or mistaken.");
                }

                entries.Add(new LedgerEntry
                {
                    Turn = turn,
                    Speaker = character?.Name ?? string.Empty,
                    SpeakerId = character?.Id ?? string.Empty,
                    Claim = text.Trim(),
                    Truth = truth,
                    Reveals = Secrets.CanonicalName(Text(claim, "reveals")) ?? string.Empty,
                });
            }

            foreach (var entry in entries)
            {
                store.Ledger.Append(entry);
            }

            var result = new StringBuilder();
            result.Append($"Recorded {entries.Count} claim{(entries.Count == 1 ? string.Empty : "s")}.");

            foreach (var name in entries
                .Select(entry => entry.Reveals)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                result.Append(Environment.NewLine);
                result.Append(Spend(file, name));
            }

            store.WriteCharacters(file);
            return ToolOutcome.Ok(result.ToString());
        }

        private static bool IsNarration(string speaker) =>
            NarrationNames.Contains(speaker.Trim(), StringComparer.OrdinalIgnoreCase);

        private static string Spend(CharacterFile file, string name)
        {
            var spent = file.Characters.Count(character => Secrets.Spend(character, name));

            if (spent > 0)
            {
                return $"'{name}' is common knowledge now - the player has been told, and anyone may speak of it.";
            }

            return file.Characters.Any(character => Secrets.Find(character, name) is not null)
                ? $"'{name}' was already common knowledge, or is not in play."
                : $"Nothing called '{name}' is a secret anybody is holding; nothing changed hands.";
        }

        private static bool TryTruth(JsonElement claim, out ClaimTruth truth, out string raw)
        {
            raw = string.Empty;

            if (claim.ValueKind != JsonValueKind.Object || !claim.TryGetProperty("truth", out var value))
            {
                truth = ClaimTruth.True;
                return true;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.True:
                case JsonValueKind.Null:
                    truth = ClaimTruth.True;
                    return true;

                case JsonValueKind.String when value.GetString() is { Length: > 0 } spelling:
                    raw = spelling;
                    truth = spelling.Trim().ToLowerInvariant() switch
                    {
                        "true" => ClaimTruth.True,
                        "lie" or "false" => ClaimTruth.Lie,
                        "mistaken" => ClaimTruth.Mistaken,
                        _ => ClaimTruth.Unverified,
                    };
                    return truth != ClaimTruth.Unverified;

                default:
                    raw = RawText(claim, "truth") ?? string.Empty;
                    truth = ClaimTruth.Unverified;
                    return false;
            }
        }

        private static ToolOutcome RandomWords(JsonElement arguments, string[] words)
        {
            var count = Math.Clamp(Number(arguments, "count") ?? DefaultWordCount, 1, MaxWordCount);

            var text = new StringBuilder();
            text.AppendLine("Seeds, not vocabulary. Let these suggest something, then discard them.");
            text.AppendLine();

            foreach (var word in WordBank.Pick(words, count, Random.Shared))
            {
                text.AppendLine(word);
            }

            return ToolOutcome.Ok(text.ToString().TrimEnd());
        }

        internal static bool TryParseKind(string value, out CharacterKind kind)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "player":
                    kind = CharacterKind.Player;
                    return true;
                case "npc":
                    kind = CharacterKind.Npc;
                    return true;
                default:
                    kind = CharacterKind.Npc;
                    return false;
            }
        }

        internal static bool CarriesFlatTerm(string notation)
        {
            var hasDigit = false;
            var hasLetter = false;

            foreach (var c in notation)
            {
                if (char.IsWhiteSpace(c))
                {
                    continue;
                }

                if (c is '+' or '-')
                {
                    if (hasDigit && !hasLetter)
                    {
                        return true;
                    }

                    hasDigit = false;
                    hasLetter = false;
                    continue;
                }

                if (char.IsAsciiDigit(c))
                {
                    hasDigit = true;
                }
                else
                {
                    hasLetter = true;
                }
            }

            return hasDigit && !hasLetter;
        }

        private static string HealthNote(Character character)
        {
            if (character.Health > character.MaxHealth)
            {
                return $"{Environment.NewLine}That is above {character.Name}'s maximum of {character.MaxHealth}. Overhealing stands.";
            }

            return character.Health == 0
                ? $"{Environment.NewLine}{character.Name} is at 0."
                : string.Empty;
        }

        private static string Joined(params string[] parts) =>
            string.Join(Environment.NewLine, parts.Where(part => part.Length > 0));

        internal static string? Text(JsonElement arguments, string propertyName) =>
            arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        internal static int? Number(JsonElement arguments, string propertyName)
        {
            if (arguments.ValueKind != JsonValueKind.Object
                || !arguments.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.Number when value.TryGetInt32(out var number) => number,
                JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
                _ => null,
            };
        }

        internal static bool? Bool(JsonElement arguments, string propertyName)
        {
            if (arguments.ValueKind != JsonValueKind.Object
                || !arguments.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
                _ => null,
            };
        }

        internal static List<(string Name, int Score)> Scores(JsonElement arguments, string propertyName)
        {
            var scores = new List<(string, int)>();

            if (arguments.ValueKind != JsonValueKind.Object
                || !arguments.TryGetProperty(propertyName, out var value)
                || value.ValueKind != JsonValueKind.Object)
            {
                return scores;
            }

            foreach (var property in value.EnumerateObject())
            {
                var score = property.Value.ValueKind switch
                {
                    JsonValueKind.Number when property.Value.TryGetInt32(out var number) => number,
                    JsonValueKind.String when int.TryParse(property.Value.GetString(), out var parsed) => parsed,
                    _ => (int?)null,
                };

                if (score is { } found && property.Name.Length > 0)
                {
                    scores.Add((property.Name, found));
                }
            }

            return scores;
        }

        internal static string? RawText(JsonElement arguments, string propertyName) =>
            arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(propertyName, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? value.GetRawText()
                : null;

        internal static List<string> Strings(JsonElement arguments, string propertyName)
        {
            var values = new List<string>();

            if (arguments.ValueKind != JsonValueKind.Object
                || !arguments.TryGetProperty(propertyName, out var value))
            {
                return values;
            }

            if (value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } single)
            {
                values.Add(single);
                return values;
            }

            if (value.ValueKind != JsonValueKind.Array)
            {
                return values;
            }

            foreach (var element in value.EnumerateArray())
            {
                if (element.ValueKind == JsonValueKind.String && element.GetString() is { Length: > 0 } entry)
                {
                    values.Add(entry);
                }
            }

            return values;
        }
    }
}
