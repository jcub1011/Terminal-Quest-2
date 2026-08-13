using System.Text;
using System.Text.Json;

using TerminalQuest.Saves;

namespace TerminalQuest.Mcp
{
    /// <summary>
    /// The tools the narrator uses to read and write the world, and the dispatch behind them.
    /// <para>
    /// The model is trusted here. It decides what happens in the story, so it decides what gets
    /// written; these handlers validate structure - is there a character by that name, is that a
    /// number - and never second-guess the fiction. What they refuse, they refuse with a message
    /// the model can act on.
    /// </para>
    /// </summary>
    internal static class QuestTools
    {
        /// <summary>The MCP server name, which prefixes every tool the model sees.</summary>
        public const string ServerName = "quest";

        /// <summary>Recent story lines returned by <c>get_state</c> and by an unbounded <c>get_story</c>.</summary>
        private const int DefaultStoryLimit = 20;

        public static IReadOnlyList<QuestTool> Definitions { get; } =
        [
            new("get_state",
                "Read the whole world at once: the player, where they are, who is with them, what "
              + "that place has been through, the inventory and the recent story. Call this before "
              + "narrating the first scene of a session.",
                """{"type":"object","properties":{}}"""),

            new("list_characters",
                "Every character on record, with health. Use it to check whether someone already "
              + "exists before inventing them.",
                """{"type":"object","properties":{}}"""),

            new("get_character",
                "One character in full, including everything they know. Read this before voicing them.",
                """
                {"type":"object",
                 "properties":{"name":{"type":"string","description":"The character's name."}},
                 "required":["name"]}
                """),

            new("upsert_character",
                "Create a character or overwrite an existing one. This is how someone enters the "
              + "world. Creating the player is the first thing to do in an empty save.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string"},
                   "kind":{"type":"string","enum":["player","npc"],"description":"Defaults to npc. Exactly one character should be the player."},
                   "health":{"type":"integer"},
                   "maxHealth":{"type":"integer"},
                   "description":{"type":"string","description":"Background and aptitude: who they are, what they are good at."}},
                 "required":["name"]}
                """),

            new("update_character",
                "Change one property of a character. Use this the moment someone takes damage or heals.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string"},
                   "property":{"type":"string","enum":["health","maxHealth","description","kind"]},
                   "value":{"type":"string","description":"The new value. Numbers may be given as digits."}},
                 "required":["name","property","value"]}
                """),

            new("add_memory",
                "Give a character something they now know. Not only what happened to them - what "
              + "they witnessed, overheard or concluded. Everyone who perceived an event should get "
              + "their own memory of it, written from their vantage point. Write {This} for the "
              + "character remembering and {Player} for the player.",
                """
                {"type":"object",
                 "properties":{
                   "character":{"type":"string","description":"Who is remembering this."},
                   "text":{"type":"string","description":"E.g. \"{This} watched {Player} kill Bess in the market square.\""},
                   "subjects":{"type":"array","items":{"type":"string"},"description":"Who or what this memory is about, so it can be found later. Optional."}},
                 "required":["character","text"]}
                """),

            new("get_memories",
                "What a character knows, optionally narrowed to what they know about someone or "
              + "somewhere. Read this before writing their dialogue: what they remember decides "
              + "their tone.",
                """
                {"type":"object",
                 "properties":{
                   "character":{"type":"string"},
                   "about":{"type":"string","description":"Narrow to memories mentioning this person, place or thing. Optional."}},
                 "required":["character"]}
                """),

            new("list_locations",
                "Every known place and who is standing in each.",
                """{"type":"object","properties":{}}"""),

            new("get_location",
                "One place in full, including its history. Call this on arrival and describe the "
              + "place as it now stands - what happened here has not been undone.",
                """
                {"type":"object",
                 "properties":{"name":{"type":"string"}},
                 "required":["name"]}
                """),

            new("upsert_location",
                "Create a place or rewrite its description. Call this before moving anyone somewhere new.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string"},
                   "description":{"type":"string"}},
                 "required":["name"]}
                """),

            new("move_character",
                "Put a character in a place, taking them out of wherever they were. The only way "
              + "presence changes - call it whenever anyone travels, including the player.",
                """
                {"type":"object",
                 "properties":{
                   "character":{"type":"string"},
                   "location":{"type":"string"}},
                 "required":["character","location"]}
                """),

            new("add_location_event",
                "Record a lasting change to a place - something a visitor would still see later. "
              + "Write {This} for the place itself and {Player} for the player.",
                """
                {"type":"object",
                 "properties":{
                   "location":{"type":"string"},
                   "text":{"type":"string","description":"E.g. \"A dragon destroyed the left span of {This}.\""}},
                 "required":["location","text"]}
                """),

            new("get_inventory",
                "What the player is carrying. Never guess at this.",
                """{"type":"object","properties":{}}"""),

            new("add_item",
                "Give the player an item, or add to a stack they already carry.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string"},
                   "quantity":{"type":"integer","description":"Defaults to 1."},
                   "description":{"type":"string","description":"Only replaces the existing description when supplied."}},
                 "required":["name"]}
                """),

            new("remove_item",
                "Take an item away. The entry disappears once nothing is left.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string"},
                   "quantity":{"type":"integer","description":"Defaults to 1."}},
                 "required":["name"]}
                """),

            new("record_event",
                "Log a beat in the player's story - arriving somewhere, meeting someone, a bargain "
              + "struck. This is what the player reads back with /story and what restores continuity "
              + "when a save is loaded.",
                """
                {"type":"object",
                 "properties":{
                   "title":{"type":"string","description":"A short headline, e.g. \"Entered the Hollow Gate\"."},
                   "detail":{"type":"string","description":"Optional elaboration."},
                   "tags":{"type":"array","items":{"type":"string"},"description":"Optional."}},
                 "required":["title"]}
                """),

            new("get_story",
                "The story so far, oldest first.",
                """
                {"type":"object",
                 "properties":{"limit":{"type":"integer","description":"How many of the most recent events to return. Defaults to 20."}}}
                """),
        ];

        /// <summary>
        /// The value for the CLI's <c>--tools</c> flag: every quest tool, fully qualified, and
        /// nothing else. Built from <see cref="Definitions"/> so a tool cannot be added and then
        /// silently left unavailable.
        /// </summary>
        public static string AllowedTools() =>
            string.Join(',', Definitions.Select(tool => $"mcp__{ServerName}__{tool.Name}"));

        /// <summary>Runs one tool call against the save.</summary>
        public static ToolOutcome Invoke(SaveStore store, string name, JsonElement arguments) => name switch
        {
            "get_state" => GetState(store),
            "list_characters" => ListCharacters(store),
            "get_character" => GetCharacter(store, arguments),
            "upsert_character" => UpsertCharacter(store, arguments),
            "update_character" => UpdateCharacter(store, arguments),
            "add_memory" => AddMemory(store, arguments),
            "get_memories" => GetMemories(store, arguments),
            "list_locations" => ListLocations(store),
            "get_location" => GetLocation(store, arguments),
            "upsert_location" => UpsertLocation(store, arguments),
            "move_character" => MoveCharacter(store, arguments),
            "add_location_event" => AddLocationEvent(store, arguments),
            "get_inventory" => GetInventory(store),
            "add_item" => AddItem(store, arguments),
            "remove_item" => RemoveItem(store, arguments),
            "record_event" => RecordEvent(store, arguments),
            "get_story" => GetStory(store, arguments),
            _ => ToolOutcome.Fail($"There is no tool called '{name}'."),
        };

        private static ToolOutcome GetState(SaveStore store)
        {
            var characters = store.ReadCharacters();
            var locations = store.ReadLocations();
            var metadata = store.ReadMetadata();

            var playerName = SaveStore.PlayerName(characters);

            var text = new StringBuilder();
            text.AppendLine($"Save '{store.Name}', turn {metadata.Turn}.");
            text.AppendLine();

            if (playerName is null)
            {
                text.AppendLine(
                    "This save is empty. Create the player with upsert_character (kind: player), "
                  + "create their starting place with upsert_location, then move_character them "
                  + "into it before narrating.");
                return ToolOutcome.Ok(text.ToString().TrimEnd());
            }

            var player = SaveStore.FindCharacter(characters, playerName)!;
            text.AppendLine("PLAYER");
            text.AppendLine(QuestRender.Character(player, playerName));
            text.AppendLine();

            text.AppendLine("WHERE THEY ARE");
            var here = SaveStore.LocationOf(locations, playerName);
            text.AppendLine(here is null
                ? "Nowhere on record. Call upsert_location and move_character."
                : QuestRender.Location(here, playerName));
            text.AppendLine();

            text.AppendLine("INVENTORY");
            var items = store.ReadInventory().Items;
            if (items.Count == 0)
            {
                text.AppendLine("  (empty)");
            }
            else
            {
                foreach (var item in items)
                {
                    text.AppendLine(QuestRender.Item(item));
                }
            }

            text.AppendLine();
            text.AppendLine("STORY SO FAR");
            var story = store.ReadStory().Events;
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
                .Where(character => !SaveStore.Matches(character.Name, playerName))
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

        private static ToolOutcome ListCharacters(SaveStore store)
        {
            var file = store.ReadCharacters();

            if (file.Characters.Count == 0)
            {
                return ToolOutcome.Ok("Nobody on record yet.");
            }

            var text = new StringBuilder();
            foreach (var character in file.Characters)
            {
                text.AppendLine(QuestRender.CharacterLine(character));
            }

            return ToolOutcome.Ok(text.ToString().TrimEnd());
        }

        private static ToolOutcome GetCharacter(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("get_character needs a name.");
            }

            var file = store.ReadCharacters();
            var character = SaveStore.FindCharacter(file, name);

            return character is null
                ? ToolOutcome.Fail($"There is no character named '{name}'. Use list_characters to see who exists, or upsert_character to create them.")
                : ToolOutcome.Ok(QuestRender.Character(character, SaveStore.PlayerName(file)));
        }

        private static ToolOutcome UpsertCharacter(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("upsert_character needs a name.");
            }

            var file = store.ReadCharacters();
            var character = SaveStore.FindCharacter(file, name);
            var isNew = character is null;

            if (character is null)
            {
                character = new Character { Name = name.Trim() };
                file.Characters.Add(character);
            }

            if (Text(arguments, "kind") is { Length: > 0 } kind)
            {
                if (!TryParseKind(kind, out var parsed))
                {
                    return ToolOutcome.Fail($"'{kind}' is not a kind. Use 'player' or 'npc'.");
                }

                character.Kind = parsed;
            }

            // A new character with no health given still needs a bar the status pane can draw.
            var maxHealth = Number(arguments, "maxHealth");
            if (maxHealth is { } max)
            {
                character.MaxHealth = Math.Max(1, max);
            }
            else if (isNew)
            {
                character.MaxHealth = 20;
            }

            var health = Number(arguments, "health");
            if (health is { } current)
            {
                character.Health = Math.Clamp(current, 0, character.MaxHealth);
            }
            else if (isNew)
            {
                character.Health = character.MaxHealth;
            }

            if (Text(arguments, "description") is { Length: > 0 } description)
            {
                character.Description = description;
            }

            store.WriteCharacters(file);

            return ToolOutcome.Ok(
                $"{(isNew ? "Created" : "Updated")}: {QuestRender.CharacterLine(character)}");
        }

        private static ToolOutcome UpdateCharacter(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("update_character needs a name.");
            }

            if (Text(arguments, "property") is not { Length: > 0 } property)
            {
                return ToolOutcome.Fail("update_character needs a property.");
            }

            var value = Text(arguments, "value") ?? RawText(arguments, "value") ?? string.Empty;

            var file = store.ReadCharacters();
            var character = SaveStore.FindCharacter(file, name);

            if (character is null)
            {
                return ToolOutcome.Fail($"There is no character named '{name}'.");
            }

            switch (property.Trim().ToLowerInvariant())
            {
                case "health":
                    if (!int.TryParse(value, out var health))
                    {
                        return ToolOutcome.Fail($"'{value}' is not a number.");
                    }

                    character.Health = Math.Clamp(health, 0, Math.Max(1, character.MaxHealth));
                    break;

                case "maxhealth":
                    if (!int.TryParse(value, out var maxHealth))
                    {
                        return ToolOutcome.Fail($"'{value}' is not a number.");
                    }

                    character.MaxHealth = Math.Max(1, maxHealth);
                    character.Health = Math.Min(character.Health, character.MaxHealth);
                    break;

                case "description":
                    character.Description = value;
                    break;

                case "kind":
                    if (!TryParseKind(value, out var kind))
                    {
                        return ToolOutcome.Fail($"'{value}' is not a kind. Use 'player' or 'npc'.");
                    }

                    character.Kind = kind;
                    break;

                // Renaming would strand the old name in every location roster and every memory
                // that spells it out, so it is refused rather than half-done.
                case "name":
                    return ToolOutcome.Fail("Characters cannot be renamed.");

                default:
                    return ToolOutcome.Fail(
                        $"'{property}' is not a character property. Use health, maxHealth, description or kind.");
            }

            store.WriteCharacters(file);
            return ToolOutcome.Ok(QuestRender.CharacterLine(character));
        }

        private static ToolOutcome AddMemory(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "character") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("add_memory needs a character.");
            }

            if (Text(arguments, "text") is not { Length: > 0 } memoryText)
            {
                return ToolOutcome.Fail("add_memory needs the memory text.");
            }

            var file = store.ReadCharacters();
            var character = SaveStore.FindCharacter(file, name);

            if (character is null)
            {
                return ToolOutcome.Fail($"There is no character named '{name}'. Create them with upsert_character first.");
            }

            var memory = new Saves.Memory
            {
                Id = SaveStore.NextId(character.Memories, static entry => entry.Id),
                Turn = store.CurrentTurn(),
                Text = memoryText,
                Subjects = Strings(arguments, "subjects"),
            };

            character.Memories.Add(memory);
            store.WriteCharacters(file);

            return ToolOutcome.Ok(
                $"{character.Name} will remember:{Environment.NewLine}"
              + QuestRender.Memory(memory, character.Name, SaveStore.PlayerName(file)));
        }

        private static ToolOutcome GetMemories(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "character") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("get_memories needs a character.");
            }

            var file = store.ReadCharacters();
            var character = SaveStore.FindCharacter(file, name);

            if (character is null)
            {
                return ToolOutcome.Fail($"There is no character named '{name}'.");
            }

            var playerName = SaveStore.PlayerName(file);
            var about = Text(arguments, "about");

            var memories = character.Memories.AsEnumerable();

            if (about is { Length: > 0 })
            {
                // Subjects are the index, but the prose is authoritative: a memory that names
                // someone only in its text still answers "what do you know about them".
                memories = memories.Where(memory =>
                    memory.Subjects.Exists(subject =>
                        Placeholders.Mentions(subject, about, character.Name, playerName))
                    || Placeholders.Mentions(memory.Text, about, character.Name, playerName));
            }

            var matched = memories.ToList();

            if (matched.Count == 0)
            {
                return ToolOutcome.Ok(about is { Length: > 0 }
                    ? $"{character.Name} knows nothing about '{about}'."
                    : $"{character.Name} has no memories yet.");
            }

            var text = new StringBuilder();
            text.AppendLine(about is { Length: > 0 }
                ? $"What {character.Name} knows about '{about}':"
                : $"What {character.Name} knows:");

            foreach (var memory in matched)
            {
                text.AppendLine(QuestRender.Memory(memory, character.Name, playerName));
            }

            return ToolOutcome.Ok(text.ToString().TrimEnd());
        }

        private static ToolOutcome ListLocations(SaveStore store)
        {
            var file = store.ReadLocations();

            if (file.Locations.Count == 0)
            {
                return ToolOutcome.Ok("Nowhere on record yet.");
            }

            var text = new StringBuilder();
            foreach (var location in file.Locations)
            {
                text.AppendLine(QuestRender.LocationLine(location));
            }

            return ToolOutcome.Ok(text.ToString().TrimEnd());
        }

        private static ToolOutcome GetLocation(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("get_location needs a name.");
            }

            var file = store.ReadLocations();
            var location = SaveStore.FindLocation(file, name);

            return location is null
                ? ToolOutcome.Fail($"There is no place named '{name}'. Use list_locations, or upsert_location to create it.")
                : ToolOutcome.Ok(QuestRender.Location(location, SaveStore.PlayerName(store.ReadCharacters())));
        }

        private static ToolOutcome UpsertLocation(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("upsert_location needs a name.");
            }

            var file = store.ReadLocations();
            var location = SaveStore.FindLocation(file, name);
            var isNew = location is null;

            if (location is null)
            {
                location = new Saves.Location { Name = name.Trim() };
                file.Locations.Add(location);
            }

            if (Text(arguments, "description") is { Length: > 0 } description)
            {
                location.Description = description;
            }

            store.WriteLocations(file);

            return ToolOutcome.Ok($"{(isNew ? "Created" : "Updated")}: {location.Name}");
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

            if (SaveStore.FindCharacter(store.ReadCharacters(), characterName) is null)
            {
                return ToolOutcome.Fail($"There is no character named '{characterName}'. Create them with upsert_character first.");
            }

            if (!store.MoveCharacter(characterName, locationName))
            {
                return ToolOutcome.Fail($"There is no place named '{locationName}'. Create it with upsert_location first.");
            }

            var destination = SaveStore.FindLocation(store.ReadLocations(), locationName)!;
            return ToolOutcome.Ok($"Moved. {QuestRender.LocationLine(destination)}");
        }

        private static ToolOutcome AddLocationEvent(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "location") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("add_location_event needs a location.");
            }

            if (Text(arguments, "text") is not { Length: > 0 } eventText)
            {
                return ToolOutcome.Fail("add_location_event needs the event text.");
            }

            var file = store.ReadLocations();
            var location = SaveStore.FindLocation(file, name);

            if (location is null)
            {
                return ToolOutcome.Fail($"There is no place named '{name}'. Create it with upsert_location first.");
            }

            var entry = new Saves.LocationEvent
            {
                Id = SaveStore.NextId(location.Events, static existing => existing.Id),
                Turn = store.CurrentTurn(),
                Text = eventText,
            };

            location.Events.Add(entry);
            store.WriteLocations(file);

            return ToolOutcome.Ok(
                $"{location.Name} will carry this:{Environment.NewLine}"
              + QuestRender.LocationEvent(entry, location.Name, SaveStore.PlayerName(store.ReadCharacters())));
        }

        private static ToolOutcome GetInventory(SaveStore store)
        {
            var items = store.ReadInventory().Items;

            if (items.Count == 0)
            {
                return ToolOutcome.Ok("The player is carrying nothing.");
            }

            var text = new StringBuilder();
            text.AppendLine("Carrying:");
            foreach (var item in items)
            {
                text.AppendLine(QuestRender.Item(item));
            }

            return ToolOutcome.Ok(text.ToString().TrimEnd());
        }

        private static ToolOutcome AddItem(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("add_item needs a name.");
            }

            var quantity = Number(arguments, "quantity") ?? 1;
            if (quantity <= 0)
            {
                return ToolOutcome.Fail("add_item needs a positive quantity. Use remove_item to take things away.");
            }

            var file = store.ReadInventory();
            var item = file.Items.Find(candidate => SaveStore.Matches(candidate.Name, name));

            if (item is null)
            {
                item = new Item { Name = name.Trim(), Quantity = 0 };
                file.Items.Add(item);
            }

            item.Quantity += quantity;

            // Only overwrite the description when one is offered: a second "add rope" should not
            // blank out the description the first one established.
            if (Text(arguments, "description") is { Length: > 0 } description)
            {
                item.Description = description;
            }

            store.WriteInventory(file);
            return ToolOutcome.Ok($"Added.{Environment.NewLine}{QuestRender.Item(item)}");
        }

        private static ToolOutcome RemoveItem(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("remove_item needs a name.");
            }

            var quantity = Number(arguments, "quantity") ?? 1;
            if (quantity <= 0)
            {
                return ToolOutcome.Fail("remove_item needs a positive quantity.");
            }

            var file = store.ReadInventory();
            var item = file.Items.Find(candidate => SaveStore.Matches(candidate.Name, name));

            if (item is null)
            {
                return ToolOutcome.Fail($"The player is not carrying '{name}'.");
            }

            item.Quantity -= quantity;

            if (item.Quantity <= 0)
            {
                file.Items.Remove(item);
                store.WriteInventory(file);
                return ToolOutcome.Ok($"{item.Name} is gone.");
            }

            store.WriteInventory(file);
            return ToolOutcome.Ok($"Removed.{Environment.NewLine}{QuestRender.Item(item)}");
        }

        private static ToolOutcome RecordEvent(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "title") is not { Length: > 0 } title)
            {
                return ToolOutcome.Fail("record_event needs a title.");
            }

            var file = store.ReadStory();

            var entry = new StoryEvent
            {
                Id = SaveStore.NextId(file.Events, static existing => existing.Id),
                Turn = store.CurrentTurn(),
                Title = title,
                Detail = Text(arguments, "detail") ?? string.Empty,
                Tags = Strings(arguments, "tags"),
            };

            file.Events.Add(entry);
            store.WriteStory(file);

            return ToolOutcome.Ok($"Recorded.{Environment.NewLine}{QuestRender.StoryEvent(entry)}");
        }

        private static ToolOutcome GetStory(SaveStore store, JsonElement arguments)
        {
            var events = store.ReadStory().Events;

            if (events.Count == 0)
            {
                return ToolOutcome.Ok("Nothing has been recorded yet.");
            }

            var limit = Number(arguments, "limit") ?? DefaultStoryLimit;
            var selected = limit > 0 ? events.TakeLast(limit) : events;

            var text = new StringBuilder();
            text.AppendLine("The story so far:");
            foreach (var entry in selected)
            {
                text.AppendLine(QuestRender.StoryEvent(entry));
            }

            return ToolOutcome.Ok(text.ToString().TrimEnd());
        }

        private static bool TryParseKind(string value, out CharacterKind kind)
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

        private static string? Text(JsonElement arguments, string propertyName) =>
            arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        /// <summary>
        /// A number argument, tolerating the string form. Models routinely send <c>"30"</c> where
        /// the schema asks for 30, and refusing that would cost a turn to no purpose.
        /// </summary>
        private static int? Number(JsonElement arguments, string propertyName)
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

        /// <summary>The raw JSON text of an argument, for reporting a value that was not a string.</summary>
        private static string? RawText(JsonElement arguments, string propertyName) =>
            arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(propertyName, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? value.GetRawText()
                : null;

        /// <summary>A string-array argument, tolerating a single bare string in its place.</summary>
        private static List<string> Strings(JsonElement arguments, string propertyName)
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
