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

        /// <summary>Words returned by <c>random_noun</c> and <c>random_adjective</c> when unasked.</summary>
        /// <remarks>
        /// More than one, because a single word has to be used or wasted, and a narrator handed one
        /// awkward seed will force it rather than draw again. Three is enough to choose from without
        /// becoming a list to work through.
        /// </remarks>
        private const int DefaultWordCount = 3;

        /// <summary>Ceiling on a word draw. Past this the seed stops narrowing anything.</summary>
        private const int MaxWordCount = 10;

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
              + "world. Creating the player is the first thing to do in an empty save. An NPC's "
              + "attributes are worth setting here, in the same breath as their health: they are "
              + "what the dice will read when that character acts.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string"},
                   "kind":{"type":"string","enum":["player","npc"],"description":"Defaults to npc. Exactly one character should be the player."},
                   "health":{"type":"integer"},
                   "maxHealth":{"type":"integer"},
                   "description":{"type":"string","description":"Background and aptitude: who they are, what they are good at."},
                   "attributes":{"type":"object","additionalProperties":{"type":"integer"},"description":"Starting scores, e.g. {\"Strength\":15,\"Dexterity\":12}. Any of the six you do not name start at 10, which is unremarkable."}},
                 "required":["name"]}
                """),

            new("update_character",
                "Change one property of a character. Use this the moment someone takes damage or "
              + "heals. Renaming is allowed and safe - it does not disturb where they are standing "
              + "or what anyone remembers - but prose already written is not rewritten, so old "
              + "memories will still say the old name.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string","description":"Who to change, by their current name."},
                   "property":{"type":"string","enum":["name","health","maxHealth","description","kind"]},
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

            new("roll",
                "Settle something with dice rather than deciding it yourself. Reach for this "
              + "whenever an outcome is genuinely in doubt - a leap, a lie, a lock, a blow struck - "
              + "and read the total before you write what happened. The number is the world's "
              + "answer, not a suggestion: narrate it even when it goes against the scene you had "
              + "in mind. Name who is rolling and which of their attributes applies, and the "
              + "modifier is added for you; a bonus you add yourself is not a bonus, it is a guess. "
              + "The player is always shown that you rolled and what for, so do not roll for things "
              + "nobody is attempting, and do not roll twice for one attempt.",
                """
                {"type":"object",
                 "properties":{
                   "notation":{"type":"string","description":"Standard dice notation: 2d6+3, d20, 4d6kh3, 2d20kh1 for advantage, 2d20kl1 for disadvantage. Terms may be added or subtracted: 1d8+1d4+2. Leave the modifier out when you name an attribute - it supplies its own."},
                   "reason":{"type":"string","description":"What is being decided, in a few words: \"leaping the chasm\", \"whether the guard believes her\". The player is shown this."},
                   "character":{"type":"string","description":"Who is rolling, by name. Omit for a roll nobody makes - a trap, the weather, the world."},
                   "attribute":{"type":"string","description":"An attribute of theirs whose modifier is added to the total, e.g. Dexterity. Omit when nothing about them applies."},
                   "hidden":{"type":"boolean","description":"Defaults to false. True keeps the result from the player; they still see that a roll was made and what for. Use it when knowing the number would tell them something their character does not know."}},
                 "required":["notation","reason"]}
                """),

            new("reveal_roll",
                "Show the player the result of a roll you kept from them, once it no longer matters "
              + "- the trap is sprung, the lie is found out, the search is over. The roll reappears "
              + "in front of them with its number. Only reach for this when the concealment has "
              + "served its purpose.",
                """
                {"type":"object",
                 "properties":{
                   "character":{"type":"string","description":"Narrow to rolls this character made. Optional."},
                   "reason":{"type":"string","description":"Narrow to the roll whose reason contains this. Optional."}}}
                """),

            new("set_attribute",
                "Raise or lower what a character is made of, when the story has earned it - a season "
              + "of hard training, a curse, a wound that healed wrong, a reputation won or lost. Not "
              + "a reward for a good roll: this is a lasting change to who somebody is, and it "
              + "should be rare. It is also how you invent an attribute the core six cannot carry - "
              + "standing in a guild, a god's favour - which then works exactly like the rest, "
              + "modifier and all.",
                """
                {"type":"object",
                 "properties":{
                   "character":{"type":"string"},
                   "attribute":{"type":"string","description":"Strength, Dexterity, Constitution, Intelligence, Wisdom or Charisma - or any name you like for something the story has grown, e.g. \"Guild standing\"."},
                   "score":{"type":"integer","description":"The new score, 1 to 30. Ten is unremarkable; every two points above or below is one point of bonus or penalty."},
                   "change":{"type":"integer","description":"How much to add or subtract instead, e.g. 1 or -2. Give this or score, not both."}},
                 "required":["character","attribute"]}
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

            new("update_location",
                "Rename a place, or rewrite its description. Renaming is safe - it does not disturb "
              + "who is standing there or what happened there - but prose already written is not "
              + "rewritten. Use upsert_location to create somewhere new; this only changes a place "
              + "that already exists.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string","description":"Which place to change, by its current name."},
                   "property":{"type":"string","enum":["name","description"]},
                   "value":{"type":"string"}},
                 "required":["name","property","value"]}
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
                "What the player is carrying, and how much coin they have. Never guess at this.",
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

            new("add_money",
                "Give the player coin - a reward, a sale, a purse found. Money is counted, not "
              + "carried: never add it with add_item.",
                """
                {"type":"object",
                 "properties":{
                   "amount":{"type":"integer","description":"How much to add. Must be positive."}},
                 "required":["amount"]}
                """),

            new("remove_money",
                "Take coin from the player - a price paid, a toll, a theft. Refused when they "
              + "cannot afford it, so check the answer before narrating the purchase.",
                """
                {"type":"object",
                 "properties":{
                   "amount":{"type":"integer","description":"How much to take. Must be positive."}},
                 "required":["amount"]}
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

            new("random_noun",
                "Draw ordinary words at random, to start somewhere you would not have chosen. Call "
              + "it before inventing a place, a person or a thing. They are seeds, not vocabulary: "
              + "let a word suggest something, then throw the word away. Never say one to the "
              + "player and never use one as a name. A word that suggests nothing is not a puzzle - "
              + "draw again.",
                """
                {"type":"object",
                 "properties":{"count":{"type":"integer","description":"How many words. Defaults to 3, at most 10."}}}
                """),

            new("random_adjective",
                "As random_noun, but qualities rather than things. Pair one with a noun when a "
              + "place or person is coming out like every other: the join between two unrelated "
              + "words is where the idea is. Seeds only - never say them to the player.",
                """
                {"type":"object",
                 "properties":{"count":{"type":"integer","description":"How many words. Defaults to 3, at most 10."}}}
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
            "roll" => Roll(store, arguments),
            "reveal_roll" => RevealRoll(store, arguments),
            "set_attribute" => SetAttribute(store, arguments),
            "list_locations" => ListLocations(store),
            "get_location" => GetLocation(store, arguments),
            "upsert_location" => UpsertLocation(store, arguments),
            "update_location" => UpdateLocation(store, arguments),
            "move_character" => MoveCharacter(store, arguments),
            "add_location_event" => AddLocationEvent(store, arguments),
            "get_inventory" => GetInventory(store),
            "add_item" => AddItem(store, arguments),
            "remove_item" => RemoveItem(store, arguments),
            "add_money" => AddMoney(store, arguments),
            "remove_money" => RemoveMoney(store, arguments),
            "record_event" => RecordEvent(store, arguments),
            "get_story" => GetStory(store, arguments),
            "random_noun" => RandomWords(arguments, WordBank.Nouns),
            "random_adjective" => RandomWords(arguments, WordBank.Adjectives),
            _ => ToolOutcome.Fail($"There is no tool called '{name}'."),
        };

        private static ToolOutcome GetState(SaveStore store)
        {
            var characters = store.ReadCharacters();
            var locations = store.ReadLocations();
            var metadata = store.ReadMetadata();

            var player = SaveStore.Player(characters);

            var text = new StringBuilder();
            text.AppendLine($"Save '{store.Name}', turn {metadata.Turn}.");
            text.AppendLine();

            if (player is null)
            {
                // Normally unreachable: the player character is made on the character screen before
                // a session starts. A save that reaches here has lost its roster, so say so rather
                // than inviting the narrator to quietly replace whoever used to be here.
                text.AppendLine(
                    "There is no player on record, which should not happen - the player character "
                  + "is created before the session starts. Say so plainly rather than inventing "
                  + "one, and do not narrate a scene.");
                return ToolOutcome.Ok(text.ToString().TrimEnd());
            }

            var playerName = player.Name;
            var index = WorldIndex.Build(characters, locations);

            text.AppendLine("PLAYER");
            text.AppendLine(QuestRender.Character(player, playerName));
            text.AppendLine();

            text.AppendLine("WHERE THEY ARE");
            var here = SaveStore.WhereIs(locations, player.Id);
            text.AppendLine(here is null
                ? "Nowhere on record. Call upsert_location and move_character."
                : QuestRender.Location(here, index, playerName));
            text.AppendLine();

            text.AppendLine("INVENTORY");
            var inventory = store.ReadInventory();
            text.AppendLine($"  {QuestRender.Money(inventory.Money)}");

            if (inventory.Items.Count == 0)
            {
                text.AppendLine("  (nothing else)");
            }
            else
            {
                foreach (var item in inventory.Items)
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
            // By id, not by name: two characters sharing a name is not supposed to happen, but if it
            // ever did, filtering on the name would hide the wrong one from the narrator entirely.
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
                character = new Character { Id = file.TakeId(), Name = name.Trim() };
                file.Characters.Add(character);
            }
            else if (character.Id.Length == 0)
            {
                // Self-repair for a record added by hand. Everything that points at a character
                // points at its id, so one without an id can never be stood anywhere or remembered.
                character.Id = file.TakeId();
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

            // Seeded before the named scores are applied, so a new character always leaves here with
            // all six however few the narrator troubled to state - the dice have to have something
            // to read when this NPC acts, and a missing attribute would otherwise be an argument
            // about what it should have been.
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

                // Renaming is a single field write: rosters and memory subjects point at the id, not
                // the name. Returns here rather than falling through, because the message wants the
                // name that is being left behind.
                case "name":
                {
                    var proposed = value.Trim();

                    if (proposed.Length == 0)
                    {
                        return ToolOutcome.Fail("A character needs a name.");
                    }

                    // Names stay unique even though they are no longer identity: the narrator asks
                    // for characters by name, and two answering to one name would resolve to
                    // whichever came first - worse than refusing, because it would look like it
                    // worked. No suffixing either; a silently altered name is one the prose will
                    // not match.
                    if (SaveStore.FindCharacter(file, proposed) is { } clash
                        && !ReferenceEquals(clash, character))
                    {
                        return ToolOutcome.Fail(
                            $"There is already a character called '{clash.Name}'. Pick a name nobody "
                          + "else has, or update that one instead.");
                    }

                    var former = character.Name;
                    character.Name = proposed;
                    store.WriteCharacters(file);

                    return ToolOutcome.Ok(
                        $"{former} is now {QuestRender.CharacterLine(character)}. Rosters and memory "
                      + $"subjects follow automatically, but memories written before now still spell "
                      + $"out '{former}' in their prose - they are not rewritten.");
                }

                // Attributes are deliberately not on this list. This tool is one property and one
                // value; an attribute needs a name and a value, which would have to be smuggled in
                // as "Strength=14" - a second grammar inside the tool whose whole shape is the
                // first. The model will still try it, so the way out is named here.
                default:
                    return ToolOutcome.Fail(
                        $"'{property}' is not a character property. Use name, health, maxHealth, "
                      + "description or kind. Attributes like Strength or Guild standing are changed "
                      + "with set_attribute.");
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

            // Subjects arrive as names and are stored as ids, so the index still points at the right
            // people once they have been renamed. Anything naming nothing on record is dropped
            // rather than kept as raw text: a list holding both ids and prose would be ambiguous in
            // exactly the way ids were introduced to stop. Nothing is lost, because the memory's own
            // text is searched too and was always the authority.
            var index = WorldIndex.Build(file, store.ReadLocations(), store.ReadInventory());
            var subjectIds = new List<string>();
            var unresolved = new List<string>();

            foreach (var subject in Strings(arguments, "subjects"))
            {
                if (index.IdOf(subject) is { } id)
                {
                    if (!subjectIds.Contains(id, StringComparer.Ordinal))
                    {
                        subjectIds.Add(id);
                    }
                }
                else if (subject.Trim() is { Length: > 0 } trimmed)
                {
                    unresolved.Add(trimmed);
                }
            }

            var memory = new Saves.Memory
            {
                Id = SaveStore.NextId(character.Memories, static entry => entry.Id),
                Turn = store.CurrentTurn(),
                Text = memoryText,
                SubjectIds = subjectIds,
            };

            character.Memories.Add(memory);
            store.WriteCharacters(file);

            var result = new StringBuilder();
            result.Append($"{character.Name} will remember:{Environment.NewLine}");
            result.Append(QuestRender.Memory(memory, character.Name, SaveStore.PlayerName(file)));

            // Said only when something was dropped, so the narrator learns to put a thing on record
            // before indexing memories against it.
            if (unresolved.Count > 0)
            {
                result.Append(
                    $"{Environment.NewLine}(Not on record, so not indexed: {string.Join(", ", unresolved)}. "
                  + "The memory itself is still searched by its text.)");
            }

            return ToolOutcome.Ok(result.ToString());
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
                var index = WorldIndex.Build(file, store.ReadLocations(), store.ReadInventory());

                // Null when the narrator asked about something nobody has put on record - "the
                // storm" - which is fine: the prose branch below still answers for it.
                var aboutId = index.IdOf(about);
                var currentName = index.NameOf(aboutId);

                memories = memories.Where(memory =>
                    // The index, and the reason it holds ids: a memory tagged with somebody answers
                    // for whatever they are called today, which matching on names never could.
                    (aboutId is not null && memory.SubjectIds.Contains(aboutId, StringComparer.Ordinal))

                    // Subjects are the index, but the prose is authoritative: a memory that names
                    // someone only in its text still answers "what do you know about them".
                    || Placeholders.Mentions(memory.Text, about, character.Name, playerName)

                    // And by the name they go by now, so asking about somebody by their current
                    // name still finds prose written when they were called something else - or
                    // rather, finds the prose that spells out the name the asker did not use.
                    || (currentName is not null
                        && !SaveStore.Matches(currentName, about)
                        && Placeholders.Mentions(memory.Text, currentName, character.Name, playerName)));
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

        /// <summary>
        /// Throws dice and writes down what they said.
        /// </summary>
        /// <remarks>
        /// The one handler that refuses things the fiction would allow. Everywhere else this class
        /// validates structure and never second-guesses the story - but a roll is the one place the
        /// model is not trusted, because the whole point of it is to take a decision away from the
        /// model. So an expression that would let it choose its own bonus is turned back, with the
        /// expression that would not.
        /// </remarks>
        private static ToolOutcome Roll(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "notation") is not { Length: > 0 } notation)
            {
                return ToolOutcome.Fail("roll needs a notation, like 2d6+3 or d20.");
            }

            if (Text(arguments, "reason") is not { Length: > 0 } reason)
            {
                return ToolOutcome.Fail(
                    "roll needs a reason - the player is shown what the roll was for, and a roll "
                  + "they cannot account for is worse than one they never saw.");
            }

            var characters = store.ReadCharacters();
            Character? roller = null;

            if (Text(arguments, "character") is { Length: > 0 } name)
            {
                roller = SaveStore.FindCharacter(characters, name);

                if (roller is null)
                {
                    return ToolOutcome.Fail(
                        $"There is no character named '{name}'. Use list_characters to see who exists.");
                }
            }

            var modifier = 0;
            var attributeName = string.Empty;

            if (Text(arguments, "attribute") is { Length: > 0 } attribute)
            {
                if (roller is null)
                {
                    return ToolOutcome.Fail(
                        "An attribute belongs to somebody. Name the character rolling, or drop the attribute.");
                }

                // A roll must not change the world, so an attribute nobody has is refused rather
                // than created. Rolling and gaining a trait in one call would also mean the narrator
                // could invent whatever bonus it wanted at the moment it needed one.
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

                    return ToolOutcome.Fail(
                        $"{roller.Name} has no attribute called '{attribute}'. They have {has}. Use one "
                      + "of those, or create it with set_attribute first.");
                }

                // Two sources for one number is the ambiguity the resolver exists to remove, and the
                // line the player is shown has one modifier slot precisely so they can see where the
                // number came from.
                if (CarriesFlatTerm(notation))
                {
                    return ToolOutcome.Fail(
                        $"{notation} already carries a flat bonus and you also named {found.Name}. Roll "
                      + "the dice alone and let the attribute supply the modifier, or drop the "
                      + "attribute and keep the flat bonus.");
                }

                attributeName = found.Name;
                modifier = CharacterAttributes.Modifier(found.Score);
            }

            // Random.Shared rather than an instance of our own: this process rolls for one session
            // and shares the sequence with nobody, so there is nothing to own. Dice.TryRoll takes it
            // as a parameter, which is the seam if a seeded sequence is ever wanted.
            var outcome = Dice.TryRoll(notation, Random.Shared, out var error);

            if (outcome is null)
            {
                return ToolOutcome.Fail(error);
            }

            var file = store.ReadRolls();

            var roll = new DiceRoll
            {
                Id = SaveStore.NextId(file.Rolls, static existing => existing.Id),
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

            file.Rolls.Add(roll);
            store.WriteRolls(file);

            var text = QuestRender.Roll(roll, roller?.Name);

            return ToolOutcome.Ok(roll.Hidden
                ? text
                + Environment.NewLine
                + "Hidden: the player has been shown the notation and what it was for, but not the "
                + "number. Narrate the consequence and never the total. Call reveal_roll later if "
                + "the moment comes when they should know."
                : text);
        }

        /// <summary>
        /// Un-hides a roll, so the player finally sees what it came to.
        /// </summary>
        /// <remarks>
        /// Chosen by description rather than by id. Ids never leave the save layer - see
        /// <see cref="EntityIds"/> - and the narrator has no handle on a past roll anyway, so it
        /// names the one it means the way it would in prose: whose it was, what it was for, or
        /// simply the last one still hidden.
        /// </remarks>
        private static ToolOutcome RevealRoll(SaveStore store, JsonElement arguments)
        {
            var file = store.ReadRolls();
            var characters = store.ReadCharacters();

            var wanted = Text(arguments, "character");
            var roller = wanted is { Length: > 0 } ? SaveStore.FindCharacter(characters, wanted) : null;

            if (wanted is { Length: > 0 } && roller is null)
            {
                return ToolOutcome.Fail($"There is no character named '{wanted}'.");
            }

            var reason = Text(arguments, "reason");

            // Backwards: the most recent match is the one a narrator saying "reveal it now" means.
            for (var index = file.Rolls.Count - 1; index >= 0; index--)
            {
                var roll = file.Rolls[index];

                if (!roll.Hidden || roll.Revealed)
                {
                    continue;
                }

                if (roller is not null && !string.Equals(roll.CharacterId, roller.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (reason is { Length: > 0 }
                    && !roll.Reason.Contains(reason, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                roll.Revealed = true;
                store.WriteRolls(file);

                var name = SaveStore.FindCharacterById(characters, roll.CharacterId)?.Name;

                return ToolOutcome.Ok(
                    $"Revealed. The player has now been shown it.{Environment.NewLine}{QuestRender.Roll(roll, name)}");
            }

            return ToolOutcome.Fail("There is no hidden roll left to reveal that matches that.");
        }

        /// <summary>Changes what a character is made of, or gives them something new to be made of.</summary>
        private static ToolOutcome SetAttribute(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "character") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("set_attribute needs a character.");
            }

            if (Text(arguments, "attribute") is not { Length: > 0 } attribute)
            {
                return ToolOutcome.Fail("set_attribute needs an attribute.");
            }

            var score = Number(arguments, "score");
            var change = Number(arguments, "change");

            if (score is not null && change is not null)
            {
                return ToolOutcome.Fail(
                    "set_attribute takes score or change, not both. Give the new score, or how much to move it by.");
            }

            if (score is null && change is null)
            {
                return ToolOutcome.Fail(
                    "set_attribute needs a score or a change. Give the new score, or how much to move it by.");
            }

            var file = store.ReadCharacters();
            var character = SaveStore.FindCharacter(file, name);

            if (character is null)
            {
                return ToolOutcome.Fail(
                    $"There is no character named '{name}'. Use list_characters to see who exists.");
            }

            var canonical = CharacterAttributes.CanonicalName(attribute)!;
            var existing = CharacterAttributes.Find(character, canonical);

            // An attribute nobody has yet starts at neutral before a change is applied, so "+1 Guild
            // standing" on somebody who never had any lands just above nothing - which is what
            // gaining a little standing should mean - rather than at 1.
            var current = existing?.Score ?? CharacterAttributes.Neutral;
            var wanted = score ?? current + change!.Value;

            var written = CharacterAttributes.Set(character, canonical, wanted);
            store.WriteCharacters(file);

            var clamped = written.Score != wanted
                ? $" (asked for {wanted}, which is outside {CharacterAttributes.MinScore}-{CharacterAttributes.MaxScore})"
                : string.Empty;

            return ToolOutcome.Ok(
                $"{character.Name}: {written.Name} is now {written.Score} "
              + $"({CharacterAttributes.Sign(CharacterAttributes.Modifier(written.Score))}){clamped}."
              + $"{Environment.NewLine}{QuestRender.Attributes(character)}");
        }

        /// <summary>
        /// Whether an expression adds or subtracts a flat number - the thing an attribute must not
        /// be doubled up with. Dice terms are stepped over rather than parsed; this only has to know
        /// whether a bare number stands anywhere in the sum.
        /// </summary>
        private static bool CarriesFlatTerm(string notation)
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
                    // A term has ended. Digits and no letter means it was a bare number - the '6' in
                    // "2d6" does not count, because the 'd' beside it makes that term dice.
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

        private static ToolOutcome ListLocations(SaveStore store)
        {
            var file = store.ReadLocations();

            if (file.Locations.Count == 0)
            {
                return ToolOutcome.Ok("Nowhere on record yet.");
            }

            // Rosters hold ids, so who is standing where cannot be rendered without the characters.
            var index = WorldIndex.Build(store.ReadCharacters());

            var text = new StringBuilder();
            foreach (var location in file.Locations)
            {
                text.AppendLine(QuestRender.LocationLine(location, index));
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

            if (location is null)
            {
                return ToolOutcome.Fail($"There is no place named '{name}'. Use list_locations, or upsert_location to create it.");
            }

            var characters = store.ReadCharacters();

            return ToolOutcome.Ok(
                QuestRender.Location(location, WorldIndex.Build(characters), SaveStore.PlayerName(characters)));
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
                location = new Saves.Location { Id = file.TakeId(), Name = name.Trim() };
                file.Locations.Add(location);
            }
            else if (location.Id.Length == 0)
            {
                // Self-repair for a place added by hand; nobody can be stood in one without an id.
                location.Id = file.TakeId();
            }

            if (Text(arguments, "description") is { Length: > 0 } description)
            {
                location.Description = description;
            }

            store.WriteLocations(file);

            return ToolOutcome.Ok($"{(isNew ? "Created" : "Updated")}: {location.Name}");
        }

        /// <summary>
        /// Changes one property of a place that already exists.
        /// <para>
        /// Separate from <see cref="UpsertLocation"/> rather than folded into it, because an upsert
        /// that renamed would be indistinguishable from a mistyped name that should have created
        /// somewhere new - and the wrong guess either loses a place or invents one.
        /// </para>
        /// </summary>
        private static ToolOutcome UpdateLocation(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "name") is not { Length: > 0 } name)
            {
                return ToolOutcome.Fail("update_location needs a name.");
            }

            if (Text(arguments, "property") is not { Length: > 0 } property)
            {
                return ToolOutcome.Fail("update_location needs a property.");
            }

            // Text first: RawText alone would keep the quotes a JSON string is delimited by. The
            // fallback is for a model that sends a bare number or boolean where a string was asked
            // for, matching UpdateCharacter.
            var value = Text(arguments, "value") ?? RawText(arguments, "value") ?? string.Empty;

            var file = store.ReadLocations();
            var location = SaveStore.FindLocation(file, name);

            if (location is null)
            {
                return ToolOutcome.Fail($"There is no place named '{name}'. Create it with upsert_location first.");
            }

            switch (property.Trim().ToLowerInvariant())
            {
                case "name":
                {
                    var proposed = value.Trim();

                    if (proposed.Length == 0)
                    {
                        return ToolOutcome.Fail("A place needs a name.");
                    }

                    if (SaveStore.FindLocation(file, proposed) is { } clash
                        && !ReferenceEquals(clash, location))
                    {
                        return ToolOutcome.Fail(
                            $"There is already a place called '{clash.Name}'. Pick a name nothing "
                          + "else has, or update that one instead.");
                    }

                    var former = location.Name;
                    location.Name = proposed;
                    store.WriteLocations(file);

                    return ToolOutcome.Ok(
                        $"{former} is now called {location.Name}. Who is standing there and what "
                      + $"happened there are unchanged, but prose written before now still says "
                      + $"'{former}' - it is not rewritten.");
                }

                case "description":
                    location.Description = value;
                    break;

                default:
                    return ToolOutcome.Fail(
                        $"'{property}' is not a place property. Use name or description.");
            }

            store.WriteLocations(file);
            return ToolOutcome.Ok($"Updated: {location.Name}");
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

            // Both names are resolved to records here, at the edge, and only ids go any further in.
            var characters = store.ReadCharacters();
            var character = SaveStore.FindCharacter(characters, characterName);

            if (character is null)
            {
                return ToolOutcome.Fail($"There is no character named '{characterName}'. Create them with upsert_character first.");
            }

            var destination = SaveStore.FindLocation(store.ReadLocations(), locationName);

            if (destination is null)
            {
                return ToolOutcome.Fail($"There is no place named '{locationName}'. Create it with upsert_location first.");
            }

            if (!store.MoveCharacter(character.Id, destination.Id))
            {
                return ToolOutcome.Fail($"There is no place named '{locationName}'. Create it with upsert_location first.");
            }

            // Re-read: the roster this prints is the one the move just wrote.
            var moved = SaveStore.FindLocationById(store.ReadLocations(), destination.Id)!;
            return ToolOutcome.Ok($"Moved. {QuestRender.LocationLine(moved, WorldIndex.Build(characters))}");
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
            var file = store.ReadInventory();

            var text = new StringBuilder();
            text.AppendLine(QuestRender.Money(file.Money));

            if (file.Items.Count == 0)
            {
                text.AppendLine("Carrying nothing else.");
                return ToolOutcome.Ok(text.ToString().TrimEnd());
            }

            text.AppendLine("Carrying:");
            foreach (var item in file.Items)
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

            // Still merged by name: two stacks of "iron key" should be one stack, and that is a
            // judgement about what the thing is rather than about which record it lives in.
            if (item is null)
            {
                item = new Item { Id = file.TakeId(), Name = name.Trim(), Quantity = 0 };
                file.Items.Add(item);
            }
            else if (item.Id.Length == 0)
            {
                item.Id = file.TakeId();
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

        private static ToolOutcome AddMoney(SaveStore store, JsonElement arguments)
        {
            if (Number(arguments, "amount") is not { } amount)
            {
                return ToolOutcome.Fail("add_money needs an amount.");
            }

            if (amount <= 0)
            {
                return ToolOutcome.Fail("add_money needs a positive amount. Use remove_money to take coin away.");
            }

            var file = store.ReadInventory();
            file.Money += amount;
            store.WriteInventory(file);

            return ToolOutcome.Ok($"Paid in. {QuestRender.Money(file.Money)}");
        }

        private static ToolOutcome RemoveMoney(SaveStore store, JsonElement arguments)
        {
            if (Number(arguments, "amount") is not { } amount)
            {
                return ToolOutcome.Fail("remove_money needs an amount.");
            }

            if (amount <= 0)
            {
                return ToolOutcome.Fail("remove_money needs a positive amount.");
            }

            var file = store.ReadInventory();

            // Refused rather than clamped: the narrator is about to describe a purchase, and it
            // needs to know the player cannot afford it before it writes that they bought it.
            if (file.Money < amount)
            {
                return ToolOutcome.Fail(
                    $"The player cannot afford that. {QuestRender.Money(file.Money)}");
            }

            file.Money -= amount;
            store.WriteInventory(file);

            return ToolOutcome.Ok($"Paid out. {QuestRender.Money(file.Money)}");
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

        /// <summary>
        /// Backs both word tools. The only handler that neither reads nor writes the save - it
        /// exists to widen what the narrator invents, not to record anything it invented.
        /// </summary>
        /// <remarks>
        /// The count is clamped rather than refused. A count of zero or ninety is not a mistake
        /// about the fiction, and answering it with an error would cost a turn to no purpose - the
        /// same judgement <see cref="Number"/> already makes about <c>"3"</c> arriving for 3.
        /// <para>
        /// The framing line is repeated on every call on purpose. It is the only thing standing
        /// between a seed and the model dropping the literal word into its prose, and it is cheap
        /// next to the turn it saves.
        /// </para>
        /// </remarks>
        private static ToolOutcome RandomWords(JsonElement arguments, string[] words)
        {
            var count = Math.Clamp(Number(arguments, "count") ?? DefaultWordCount, 1, MaxWordCount);

            var text = new StringBuilder();
            text.AppendLine("Seeds, not vocabulary. Let these suggest something, then discard them.");
            text.AppendLine();

            // Random.Shared for the same reason the dice use it: one session, one stream.
            foreach (var word in WordBank.Pick(words, count, Random.Shared))
            {
                text.AppendLine(word);
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

        /// <summary>
        /// A boolean argument, tolerating the string form for the same reason <see cref="Number"/>
        /// does: models send <c>"true"</c> where the schema asks for <c>true</c>, and refusing that
        /// would cost a turn to no purpose.
        /// </summary>
        private static bool? Bool(JsonElement arguments, string propertyName)
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

        /// <summary>
        /// A name-to-number map argument, as <c>{"Strength":15}</c>. Values may arrive as strings.
        /// </summary>
        /// <remarks>
        /// An entry whose value is not a number is skipped rather than refused. One unreadable score
        /// should not cost the narrator the whole character it was creating - the rest of the call
        /// is still good, and a missing attribute reads as neutral.
        /// </remarks>
        private static List<(string Name, int Score)> Scores(JsonElement arguments, string propertyName)
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
