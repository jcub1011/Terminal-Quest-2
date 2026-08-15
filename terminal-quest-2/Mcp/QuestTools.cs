using System.Text;
using System.Text.Json;

using TerminalQuest.Saves;
using TerminalQuest.Settings;

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

        /// <summary>
        /// The least of the transcript <c>get_transcript</c> reads, however small a window was asked
        /// for. A floor rather than the whole budget: the first line of a byte window is dropped as a
        /// fragment, so a window narrow enough to hold one line would come back empty.
        /// </summary>
        private const int TranscriptTailBytes = 8 * 1024;

        /// <summary>
        /// What a narrator calls itself when it names a speaker it should have left unnamed.
        /// <para>
        /// Not an alias table the model is told about - see <see cref="IsNarration"/> for why these
        /// are read as no speaker at all rather than refused.
        /// </para>
        /// </summary>
        private static readonly string[] NarrationNames =
        [
            "narrator", "narration", "dm", "gm", "game master", "dungeon master", "system", "self", "you",
        ];

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
                "One character: who they are, what they are made of, what they remember, and any secret "
              + "of theirs that is in play. Read this before voicing them. What comes back is what they "
              + "may act on, which is not everything on record about them.",
                """
                {"type":"object",
                 "properties":{"name":{"type":"string","description":"The character's name."}},
                 "required":["name"]}
                """),

            new("upsert_character",
                "Create a character, or change one already on record. This is how someone enters the "
              + "world. Creating the player is the first thing to do in an empty save. An NPC's "
              + "attributes are worth setting here, in the same breath as their health: they are "
              + "what the dice will read when that character acts. A description for somebody already "
              + "on record is added to what it says, never replacing it.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string"},
                   "kind":{"type":"string","enum":["player","npc"],"description":"Defaults to npc. Exactly one character should be the player."},
                   "health":{"type":"integer"},
                   "maxHealth":{"type":"integer"},
                   "description":{"type":"string","description":"Background and aptitude: who they are, what they are good at. For somebody already on record, only what is newly known - this is added to what it says."},
                   "attributes":{"type":"object","additionalProperties":{"type":"integer"},"description":"Starting scores, e.g. {\"Strength\":15,\"Dexterity\":12}. Any of the six you do not name start at 10, which is unremarkable."}},
                 "required":["name"]}
                """),

            new("update_character",
                "Change one property of a character. Use this the moment someone takes damage or "
              + "heals. Renaming is allowed and safe - it does not disturb where they are standing "
              + "or what anyone remembers - but prose already written is not rewritten, so old "
              + "memories will still say the old name. A description is added to rather than replaced, "
              + "for the same reason: the player has already been told who this is.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string","description":"Who to change, by their current name."},
                   "property":{"type":"string","enum":["name","health","maxHealth","description","kind"]},
                   "value":{"type":"string","description":"The new value, absolute rather than a change: to take 3 off 17, send 14, not -3. Health may go above maxHealth - that is overhealing and it stands."}},
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
              + "their tone. Any secret they are holding comes back here too, when it is in play.",
                """
                {"type":"object",
                 "properties":{
                   "character":{"type":"string"},
                   "about":{"type":"string","description":"Narrow to memories mentioning this person, place or thing. Optional."}},
                 "required":["character"]}
                """),

            new("grant_secret",
                "Give a character something they know and others do not: what a witness actually saw, "
              + "who somebody answers to, what a debt is really for. Name it in a few words - that name "
              + "is the handle you use later if the moment comes to give it away, so make it one you "
              + "will recognise. Only the character holding it is ever shown it, and only when you ask "
              + "about them, so grant it to everybody who ought to know. Never say the name or the "
              + "secret to the player.",
                """
                {"type":"object",
                 "properties":{
                   "character":{"type":"string","description":"Who is keeping it."},
                   "name":{"type":"string","description":"A short handle, e.g. \"the sealed cellar\" or \"the innkeeper's brother\"."},
                   "detail":{"type":"string","description":"What the secret actually is, from their vantage point. Use {This} for them and {Player} for the player."}},
                 "required":["character","name","detail"]}
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
                "Create a place, or add to what is known about one. Call this before moving anyone "
              + "somewhere new. A description for a place already on record is added to what it "
              + "already says and never replaces it, so say only what is new.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string"},
                   "description":{"type":"string","description":"For a place already on record, only what is newly known - this is added to what it says."}},
                 "required":["name"]}
                """),

            new("update_location",
                "Rename a place, or add to its description. Renaming is safe - it does not disturb "
              + "who is standing there or what happened there - but prose already written is not "
              + "rewritten, and a description is added to rather than replaced: the player has already "
              + "been told what was there. When something about a place has actually changed, record it "
              + "with add_location_event. Use upsert_location to create somewhere new; this only "
              + "changes a place that already exists.",
                """
                {"type":"object",
                 "properties":{
                   "name":{"type":"string","description":"Which place to change, by its current name."},
                   "property":{"type":"string","enum":["name","description"]},
                   "value":{"type":"string","description":"The new name, or - for a description - only what is newly known, which is added to what it says."}},
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

            new("get_transcript",
                "The end of the last session word for word - what the player typed and the prose you "
              + "wrote back, markup and all. Call this first when a save is resumed: your memory of it "
              + "is gone, and this is the only record of how the scene was actually written. It also "
              + "says whether the player is still owed an answer.",
                """
                {"type":"object",
                 "properties":{
                   "characters":{"type":"integer","description":"Roughly how much prose to return, in characters. Defaults to the player's setting; whole lines are never cut in half."}}}
                """),

            new("record_claims",
                "Write down what this turn's prose is about to assert, then write the prose. Call it on "
              + "every turn that says anything, as the last thing before you speak. One entry for each "
              + "separate thing asserted, not one for the turn: a paragraph naming a price, a road and a "
              + "rumour is three. Say who asserts each one, or leave the speaker out when it is your own "
              + "narration. A character may lie - record it as a lie and the world will hold it as one, to "
              + "be paid off later, rather than reading it as a mistake to be corrected. If a line will "
              + "give away a secret somebody is holding, name that secret and it becomes common knowledge "
              + "from then on.",
                """
                {"type":"object",
                 "properties":{
                   "claims":{"type":"array","description":"One entry per assertion.",
                     "items":{"type":"object",
                       "properties":{
                         "claim":{"type":"string","description":"The assertion in one plain sentence - what it commits the world to, not the prose you wrote. E.g. \"The bridge north of the Ford has been out since the flood.\""},
                         "speaker":{"type":"string","description":"Who asserted it, by name. Leave out for your own narration."},
                         "truth":{"type":"string","enum":["true","lie","mistaken"],"description":"Defaults to true. 'lie' when the speaker knew better; 'mistaken' when they believed it and were wrong."},
                         "reveals":{"type":"string","description":"The name of a secret this gave away, if any."}},
                       "required":["claim"]}}},
                 "required":["claims"]}
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

        /// <summary>Runs one tool call against the save, and records that it ran.</summary>
        /// <remarks>
        /// The recording is here rather than in each handler for the reason
        /// <see cref="AllowedTools"/> is derived rather than written out: a step every handler has to
        /// remember is a step a new handler will not. It also has to see every call, including the
        /// ones that only read, because the rule deciding whether a knowledge fetch may be answered
        /// is a function of which fetches have already been answered this turn.
        /// <para>
        /// After the handler rather than before it, because the outcome is part of what is recorded -
        /// a refused fetch handed nothing over, and the rule above has to be able to leave it out.
        /// Writing ahead would buy nothing: a handler's document write either completed its rename or
        /// never happened.
        /// </para>
        /// </remarks>
        public static ToolOutcome Invoke(SaveStore store, string name, JsonElement arguments)
        {
            ToolOutcome outcome;

            try
            {
                // The gate goes before dispatch, which is the only place it can go: the point of a
                // refusal is that it is never a partial answer, and after the handler there would already
                // be rendered prose to throw away. Inside the try, though - it reads the save, so it can
                // fail, and a call that failed here is still a call the log has to know about.
                outcome = SecretGate.Refusal(store, name, arguments) ?? Dispatch(store, name, arguments);
            }
            catch (Exception ex)
            {
                // Recorded and rethrown unchanged: the hosts turn this into a JSON-RPC error or a
                // sentence for the model and should learn nothing new from here. Deliberately not
                // narrowed to SaveException - "every call is recorded" is the property the divergence
                // rule rests on, and a handler that fails some other way still made a call.
                QuestJournal.Record(store, name, arguments, failed: true, error: ex.Message);
                throw;
            }

            QuestJournal.Record(store, name, arguments, outcome.IsError, error: string.Empty);
            return outcome;
        }

        /// <summary>Routes one call to its handler. Everything around it is <see cref="Invoke"/>'s.</summary>
        private static ToolOutcome Dispatch(SaveStore store, string name, JsonElement arguments) => name switch
        {
            "get_state" => GetState(store),
            "list_characters" => ListCharacters(store),
            "get_character" => GetCharacter(store, arguments),
            "upsert_character" => UpsertCharacter(store, arguments),
            "update_character" => UpdateCharacter(store, arguments),
            "add_memory" => AddMemory(store, arguments),
            "get_memories" => GetMemories(store, arguments),
            "grant_secret" => GrantSecret(store, arguments),
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
            "get_transcript" => GetTranscript(store, arguments),
            "record_claims" => RecordClaims(store, arguments),
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

            if (character is null)
            {
                return ToolOutcome.Fail($"There is no character named '{name}'. Use list_characters to see who exists, or upsert_character to create them.");
            }

            var playerName = SaveStore.PlayerName(file);
            var (held, common) = SecretGate.Release(character, file.Characters);

            // Appended here rather than folded into QuestRender.Character, so that the renderer stays
            // incapable of printing a secret. get_state renders the player through it too, and this is
            // what keeps that call free of secrets without anybody having to remember.
            return ToolOutcome.Ok(Joined(
                QuestRender.Character(character, playerName),
                QuestRender.Secrets(held, common, character.Name, playerName)));
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

            // Not capped at MaxHealth: overhealing is a mechanic, and a value above the ceiling
            // stands. Only the floor is enforced, because nothing that reads health knows what a
            // negative one would mean.
            var health = Number(arguments, "health");
            if (health is { } current)
            {
                character.Health = Math.Max(0, current);
            }
            else if (isNew)
            {
                character.Health = character.MaxHealth;
            }

            if (Text(arguments, "description") is { Length: > 0 } description)
            {
                if (Descriptions.Extend(character.Description, description) is not { } extended)
                {
                    return ToolOutcome.Fail(
                        $"{character.Name} already carries as much description as they can hold. Give them "
                      + "a memory with add_memory, or change what they are made of with set_attribute.");
                }

                character.Description = extended;
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
                $"{(isNew ? "Created" : "Updated")}: {QuestRender.CharacterLine(character)}"
              + HealthNote(character));
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

                    // Deliberately uncapped - see UpsertCharacter. The narrator is told about a
                    // value above the ceiling rather than having it taken away silently, because a
                    // reply that reads like nothing happened is one the model will send again.
                    character.Health = Math.Max(0, health);
                    break;

                case "maxhealth":
                    if (!int.TryParse(value, out var maxHealth))
                    {
                        return ToolOutcome.Fail($"'{value}' is not a number.");
                    }

                    // Health is left where it is. Lowering the ceiling under someone who is
                    // overhealed would spend the overheal as a side effect of a call that never
                    // mentioned it.
                    character.MaxHealth = Math.Max(1, maxHealth);
                    break;

                case "description":
                    // Extended rather than assigned, which also fixes an assignment that would blank the
                    // field outright when handed an empty value.
                    if (Descriptions.Extend(character.Description, value) is not { } grown)
                    {
                        return ToolOutcome.Fail(
                            $"{character.Name} already carries as much description as they can hold. Give "
                          + "them a memory with add_memory, or change what they are made of with set_attribute.");
                    }

                    character.Description = grown;
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
            return ToolOutcome.Ok(QuestRender.CharacterLine(character) + HealthNote(character));
        }

        /// <summary>
        /// What to add to a character line when their health has landed somewhere worth remarking on.
        /// </summary>
        /// <remarks>
        /// Overhealing is legal, so this is not a warning that something was refused - the write
        /// stands either way. It exists because the alternative is a reply the narrator cannot tell
        /// apart from an ordinary one, and a narrator that cannot see what its call did will make the
        /// call again. Empty for the ordinary case, so the common reply stays one line.
        /// </remarks>
        private static string HealthNote(Character character)
        {
            if (character.Health > character.MaxHealth)
            {
                return $"{Environment.NewLine}That is above {character.Name}'s maximum of "
                     + $"{character.MaxHealth}. Overhealing is allowed and it stands - set maxHealth "
                     + "as well only if that should become their new ceiling.";
            }

            return character.Health == 0
                ? $"{Environment.NewLine}{character.Name} is at 0."
                : string.Empty;
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

            // Secrets come back here as well as from get_character, and that is on purpose: the
            // narrator is told to call this before voicing anybody, so this is the fetch at which what
            // a character is sitting on has to arrive. Putting them only on get_character would mean
            // voicing somebody from their memories while never seeing what they are keeping.
            var (held, common) = SecretGate.Release(character, file.Characters);
            var secrets = QuestRender.Secrets(held, common, character.Name, playerName);

            if (matched.Count == 0)
            {
                return ToolOutcome.Ok(Joined(
                    about is { Length: > 0 }
                        ? $"{character.Name} knows nothing about '{about}'."
                        : $"{character.Name} has no memories yet.",
                    secrets));
            }

            var text = new StringBuilder();
            text.AppendLine(about is { Length: > 0 }
                ? $"What {character.Name} knows about '{about}':"
                : $"What {character.Name} knows:");

            foreach (var memory in matched)
            {
                text.AppendLine(QuestRender.Memory(memory, character.Name, playerName));
            }

            return ToolOutcome.Ok(Joined(text.ToString().TrimEnd(), secrets));
        }

        /// <summary>
        /// Gives a character something they know and others do not.
        /// </summary>
        /// <remarks>
        /// Created live rather than asleep, which is the opposite of the stage's own default. Nothing
        /// yet exists to wake a dormant secret, so one granted asleep would be invisible for the rest
        /// of the campaign - worse than never granting it. A person adjudicates by editing the save,
        /// which is what a folder of readable documents is for.
        /// <para>
        /// Not a knowledge fetch, so the lifecycle gate ignores it: granting hands nothing over. It can
        /// still change what the rest of the turn may read, and correctly so - a secret granted now is
        /// one somebody else does not share.
        /// </para>
        /// </remarks>
        private static ToolOutcome GrantSecret(SaveStore store, JsonElement arguments)
        {
            if (Text(arguments, "character") is not { Length: > 0 } who)
            {
                return ToolOutcome.Fail("grant_secret needs a character - a secret is something somebody in particular knows.");
            }

            if (Secrets.CanonicalName(Text(arguments, "name")) is not { } name)
            {
                return ToolOutcome.Fail(
                    "grant_secret needs a short name for the secret, like 'the sealed cellar'. That name "
                  + "is how you will refer to it later, so it cannot be left out.");
            }

            if (Text(arguments, "detail") is not { Length: > 0 } detail || detail.AsSpan().IsWhiteSpace())
            {
                return ToolOutcome.Fail(
                    $"grant_secret needs the detail of '{name}' - what the character actually knows. A "
                  + "name with nothing behind it is a secret you cannot write a scene from.");
            }

            var file = store.ReadCharacters();
            var character = SaveStore.FindCharacter(file, who);

            if (character is null)
            {
                return ToolOutcome.Fail($"There is no character named '{who}'. Use list_characters to see who exists, or upsert_character to create them.");
            }

            // Refused rather than overwritten. A secret already on record has possibly been read and
            // possibly been acted on, and quietly replacing what it says is the one thing the world
            // must never do - canon is extended, never negated.
            if (Secrets.Find(character, name) is { } existing)
            {
                return ToolOutcome.Fail(
                    $"{character.Name} already holds a secret called '{existing.Name}'. Give this one a "
                  + "different name, or add what is new as a memory instead.");
            }

            var granted = Secrets.Grant(character, name, detail, store.CurrentTurn());
            store.WriteCharacters(file);

            // The detail is not echoed. It is already known to whoever just sent it, and repeating it
            // back as though it were news is how a tool result starts reading like something to narrate.
            return ToolOutcome.Ok(
                $"{character.Name} now keeps '{granted.Name}'. Nobody else knows it, and it will only "
              + "come back when you ask about them.");
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
                if (Descriptions.Extend(location.Description, description) is not { } extended)
                {
                    return ToolOutcome.Fail(
                        $"{location.Name} already carries as much description as it can hold. Record what "
                      + "has changed with add_location_event instead - a place's history is where lasting "
                      + "change belongs.");
                }

                location.Description = extended;
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
                    // Extended rather than assigned, which also fixes an assignment that would blank the
                    // field outright when handed an empty value.
                    if (Descriptions.Extend(location.Description, value) is not { } grown)
                    {
                        return ToolOutcome.Fail(
                            $"{location.Name} already carries as much description as it can hold. Record "
                          + "what has changed with add_location_event instead - a place's history is where "
                          + "lasting change belongs.");
                    }

                    location.Description = grown;
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
        /// Hands back the end of the conversation as it was actually written.
        /// </summary>
        /// <remarks>
        /// The one tool that answers with something the model itself wrote rather than with world
        /// state, and the reason it may is that every line of it has already been on the player's
        /// screen. That keeps it clear of the rule <see cref="JournalEntry"/> states - no tool reply,
        /// no secret and no hidden total can reach this file, because nothing writes to it but the
        /// game, and the game only writes what it has drawn.
        /// <para>
        /// The default window is the player's setting, read straight off disk. This handler usually
        /// runs in the state server, a separate process that is handed a save folder and nothing else,
        /// but <c>SettingsStore.Read</c> takes no arguments, resolves a fixed path under
        /// <c>AppDirectory</c> and is documented never to throw - so the preference crosses the
        /// process boundary without any plumbing to keep in step.
        /// </para>
        /// <para>
        /// The byte budget handed to <c>Tail</c> is deliberately larger than the character budget it
        /// feeds: a line costs its prose plus its JSON, and markup is prose the player never sees. Four
        /// times over is slack, not arithmetic - reading a few kilobytes too many costs nothing, and
        /// reading too few would silently shorten the recall.
        /// </para>
        /// </remarks>
        private static ToolOutcome GetTranscript(SaveStore store, JsonElement arguments)
        {
            var characters = TranscriptRecall.Clamp(
                Number(arguments, "characters") ?? SettingsStore.Read().TranscriptRecallCharacters);

            var recent = store.Transcript.Tail(Math.Max(TranscriptTailBytes, characters * 4));

            return ToolOutcome.Ok(QuestRender.Transcript(TranscriptRecall.Tail(recent, characters)));
        }

        /// <summary>
        /// Writes down what the narrator has just asserted, and spends any secret it gave away.
        /// </summary>
        /// <remarks>
        /// The only structured channel the narrator has beside its prose. Emitting this alongside the
        /// text in one call would be cheaper and is not available: prose reaches the game as a stream of
        /// text deltas with inline markup, and there is nothing running next to it. So it costs one
        /// extra round trip per narrated turn, which is the honest price and worth measuring rather than
        /// arguing about.
        /// <para>
        /// Also the one handler that reports a fault without refusing, and the exception is narrow: a
        /// <c>reveals</c> naming nothing anybody holds is a mislabel, and refusing the call over it would
        /// throw away the claims - which were already said to the player and are binding whether or not
        /// this records them.
        /// </para>
        /// </remarks>
        private static ToolOutcome RecordClaims(SaveStore store, JsonElement arguments)
        {
            if (arguments.ValueKind != JsonValueKind.Object
                || !arguments.TryGetProperty("claims", out var claims)
                || claims.ValueKind != JsonValueKind.Array
                || claims.GetArrayLength() == 0)
            {
                return ToolOutcome.Fail(
                    "record_claims needs at least one claim - one entry for each separate thing you "
                  + "asserted this turn.");
            }

            var file = store.ReadCharacters();
            var turn = store.CurrentTurn();

            var entries = new List<LedgerEntry>();
            var position = 0;

            // Built and checked in full before anything is written. Unlike the attribute reader, which
            // skips an entry it cannot make sense of, a bad claim refuses the whole call: an unreadable
            // score costs a number that reads as neutral anyway, whereas a dropped claim is precisely
            // what this exists to hold, and a silently short ledger is worse than a refused call the
            // narrator can retry.
            foreach (var claim in claims.EnumerateArray())
            {
                position++;

                if (Text(claim, "claim") is not { Length: > 0 } text || text.AsSpan().IsWhiteSpace())
                {
                    return ToolOutcome.Fail(
                        $"Claim {position} has no text. Every entry needs the assertion it records, in "
                      + "one plain sentence.");
                }

                var speaker = Text(claim, "speaker");
                var character = speaker is { Length: > 0 } ? SaveStore.FindCharacter(file, speaker) : null;

                // Looked up first, so a character genuinely called Narrator still answers to their
                // own name; the alias list is only consulted for a name nobody has.
                if (speaker is { Length: > 0 } && character is null && !IsNarration(speaker))
                {
                    return ToolOutcome.Fail(
                        $"There is no character named '{speaker}'. Use list_characters to see who exists, "
                      + "or leave the speaker out if that was your own narration.");
                }

                if (!TryTruth(claim, out var truth, out var raw))
                {
                    return ToolOutcome.Fail(
                        $"'{raw}' is not a truth status for claim {position}. Use true, lie or mistaken.");
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

            // Recorded first, spent second, and the order is chosen for which failure is survivable.
            // Spending and then failing to record would leave a secret shared with nothing saying it was
            // ever said - an untraceable leak. Recording and then failing to spend gates the next fetch
            // more strictly than it should, and leaves the discrepancy where a consistency check can see
            // it. Take the recoverable one.
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

        /// <summary>
        /// Whether a speaker name is the narrator naming itself, which is the same as naming nobody.
        /// </summary>
        /// <remarks>
        /// The schema asks for the field to be left out for plain narration, and a model that fills
        /// every field it is shown writes "Narrator" instead. That used to refuse the whole call, so a
        /// turn's entire ledger was lost over a courtesy value - and because the refusal named no way
        /// forward the model could act on, it retried unchanged. Read as no speaker instead: the empty
        /// speaker is exactly what the field being absent already means.
        /// </remarks>
        private static bool IsNarration(string speaker) =>
            NarrationNames.Contains(speaker.Trim(), StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Turns a named secret spent for everybody holding it live, and says what happened.
        /// </summary>
        /// <remarks>
        /// Every holder, not only whoever spoke. Spent means the player knows, which is a fact about the
        /// player rather than about who happened to voice it - and leaving another holder's copy live
        /// would keep the divergence gate refusing fetches over something that is already out.
        /// <para>
        /// A dormant secret of the same name is left alone. The narrator was never handed it, so it
        /// cannot have revealed it; a name that collides with something asleep is a mislabel.
        /// </para>
        /// </remarks>
        private static string Spend(CharacterFile file, string name)
        {
            var spent = file.Characters.Count(character => Secrets.Spend(character, name));

            if (spent > 0)
            {
                return $"'{name}' is common knowledge now - the player has been told, and anyone may speak of it.";
            }

            return file.Characters.Any(character => Secrets.Find(character, name) is not null)
                ? $"'{name}' was already common knowledge, or is not in play. The claims are on record."
                : $"Nothing called '{name}' is a secret anybody is holding, so nothing changed hands. The claims are on record.";
        }

        /// <summary>
        /// A claim's truth status, tolerating the plain <c>true</c> a model sends where the schema asks
        /// for <c>"true"</c> - the same judgement <see cref="Number"/> and <see cref="Bool"/> make.
        /// </summary>
        /// <returns>
        /// False when something was supplied that is not a status, with <paramref name="raw"/> set to
        /// whatever it was so the refusal can quote it back.
        /// </returns>
        private static bool TryTruth(JsonElement claim, out ClaimTruth truth, out string raw)
        {
            raw = string.Empty;

            // Absent means true. The narrator's ordinary assertion is a true one, and demanding the flag
            // on every entry would cost tokens to say what is already the case. Note this differs from
            // the stored default, which is unverified: a line nobody labelled has no such context.
            if (claim.ValueKind != JsonValueKind.Object || !claim.TryGetProperty("truth", out var value))
            {
                truth = ClaimTruth.True;
                return true;
            }

            switch (value.ValueKind)
            {
                case JsonValueKind.True:
                    truth = ClaimTruth.True;
                    return true;

                case JsonValueKind.Null:
                    truth = ClaimTruth.True;
                    return true;

                case JsonValueKind.String when value.GetString() is { Length: > 0 } spelling:
                    raw = spelling;

                    // Only the three the schema offers. Unverified is the game's to write, not the
                    // narrator's, and contradiction is a finding rather than a stance somebody takes.
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

        /// <summary>
        /// Joins the parts of a result, dropping the ones that had nothing to say.
        /// </summary>
        /// <remarks>
        /// So that a character with no secrets reads exactly as they did before secrets existed, rather
        /// than trailing a blank line the model has to decide the meaning of.
        /// </remarks>
        private static string Joined(params string[] parts) =>
            string.Join(Environment.NewLine, parts.Where(part => part.Length > 0));

        internal static string? Text(JsonElement arguments, string propertyName) =>
            arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        /// <summary>
        /// A number argument, tolerating the string form. Models routinely send <c>"30"</c> where
        /// the schema asks for 30, and refusing that would cost a turn to no purpose.
        /// </summary>
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

        /// <summary>
        /// A boolean argument, tolerating the string form for the same reason <see cref="Number"/>
        /// does: models send <c>"true"</c> where the schema asks for <c>true</c>, and refusing that
        /// would cost a turn to no purpose.
        /// </summary>
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

        /// <summary>
        /// A name-to-number map argument, as <c>{"Strength":15}</c>. Values may arrive as strings.
        /// </summary>
        /// <remarks>
        /// An entry whose value is not a number is skipped rather than refused. One unreadable score
        /// should not cost the narrator the whole character it was creating - the rest of the call
        /// is still good, and a missing attribute reads as neutral.
        /// </remarks>
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

        /// <summary>The raw JSON text of an argument, for reporting a value that was not a string.</summary>
        internal static string? RawText(JsonElement arguments, string propertyName) =>
            arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty(propertyName, out var value)
            && value.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined)
                ? value.GetRawText()
                : null;

        /// <summary>A string-array argument, tolerating a single bare string in its place.</summary>
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
