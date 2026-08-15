using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The player's own commands, answered by the game rather than forwarded to the narrator.
    /// </summary>
    internal static class PlayerCommands
    {
        public static readonly IReadOnlyList<PlayerCommandInfo> All =
        [
            new("story", "", "everything that has happened"),
            new("rolls", "", "every die the world has thrown"),
            new("inventory", "", "what you are carrying"),
            new("inv", "", "what you are carrying", IsAlias: true),
            new("character", "[name]", "who you have met, and what happened with them"),
            new("characters", "[name]", "who you have met, and what happened with them", IsAlias: true),
            new("who", "[name]", "who you have met, and what happened with them", IsAlias: true),
            new("location", "[name]", "where you have been, and what happened there"),
            new("locations", "[name]", "where you have been, and what happened there", IsAlias: true),
            new("where", "[name]", "where you have been, and what happened there", IsAlias: true),
            new("saves", "", "every save on this machine"),
            new("delete", "<name>", "destroy another save, for good"),
            new("system-prompt", "", "rewrite the narrator's instructions (ends the session)"),
            new("help", "", "this list"),
            new("quit", "", "leave this save and go back to the menu"),
            new("exit", "", "leave this save and go back to the menu", IsAlias: true),
        ];

        public static bool IsCommand(string input) => input.StartsWith('/');

        public static IReadOnlyList<PlayerCommandInfo> Matching(string input)
        {
            if (!IsCommand(input) || input.Contains(' '))
            {
                return [];
            }

            var typed = input[1..];

            return All
                .Where(command => command.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        public static (IReadOnlyList<SuggestionItem> Suggestions, bool IsChoosing) GetSuggestions(
            string input,
            SaveStore? store)
        {
            if (!IsCommand(input))
            {
                return ([], false);
            }

            if (!input.Contains(' '))
            {
                var matches = Matching(input);
                if (matches.Count > 0)
                {
                    var items = matches
                        .Select(c => new SuggestionItem(
                            InsertText: $"/{c.Name} ",
                            DisplayText: c.Usage,
                            Summary: c.Summary,
                            Role: TextRole.Command))
                        .ToArray();

                    return (items, true);
                }

                var named = Describing(input);
                if (named is not null)
                {
                    return ([
                        new SuggestionItem(
                            InsertText: $"/{named.Value.Name} ",
                            DisplayText: named.Value.Usage,
                            Summary: named.Value.Summary,
                            Role: TextRole.Command)
                    ], false);
                }

                return ([], false);
            }

            var parts = input.Split(' ', 2, StringSplitOptions.TrimEntries);
            var commandName = parts.Length > 0 ? parts[0].TrimStart('/').ToLowerInvariant() : string.Empty;
            var argPrefix = parts.Length > 1 ? parts[1] : string.Empty;

            var command = All.FirstOrDefault(c => c.Name.Equals(commandName, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(command.Name))
            {
                return ([], false);
            }

            var argMatches = GetArgumentSuggestions(commandName, argPrefix, store);
            if (argMatches.Count > 0)
            {
                return (argMatches, true);
            }

            return ([
                new SuggestionItem(
                    InsertText: string.Empty,
                    DisplayText: command.Usage,
                    Summary: command.Summary,
                    Role: TextRole.Command)
            ], false);
        }

        private static IReadOnlyList<SuggestionItem> GetArgumentSuggestions(
            string commandName,
            string argPrefix,
            SaveStore? store)
        {
            switch (commandName)
            {
                case "character":
                case "characters":
                case "who":
                    if (store is null) return [];
                    try
                    {
                        var chars = store.ReadCharacters().Characters;
                        return chars
                            .Where(c => c.Name.StartsWith(argPrefix, StringComparison.OrdinalIgnoreCase))
                            .Select(c => new SuggestionItem(
                                InsertText: $"/{commandName} {c.Name}",
                                DisplayText: c.Name,
                                Summary: c.Kind == CharacterKind.Player
                                    ? "(you)"
                                    : (c.Description.Length > 0 ? c.Description : $"Health {c.Health}/{c.MaxHealth}"),
                                Role: TextRole.Normal))
                            .ToArray();
                    }
                    catch (SaveException)
                    {
                        return [];
                    }

                case "location":
                case "locations":
                case "where":
                    if (store is null) return [];
                    try
                    {
                        var locs = store.ReadLocations().Locations;
                        return locs
                            .Where(l => l.Name.StartsWith(argPrefix, StringComparison.OrdinalIgnoreCase))
                            .Select(l => new SuggestionItem(
                                InsertText: $"/{commandName} {l.Name}",
                                DisplayText: l.Name,
                                Summary: l.Description,
                                Role: TextRole.Place))
                            .ToArray();
                    }
                    catch (SaveException)
                    {
                        return [];
                    }

                case "delete":
                    try
                    {
                        var saves = SavePaths.List();
                        return saves
                            .Where(s => !SaveStore.Matches(s.Name, store?.Name)
                                && s.Name.StartsWith(argPrefix, StringComparison.OrdinalIgnoreCase))
                            .Select(s => new SuggestionItem(
                                InsertText: $"/{commandName} {s.Name}",
                                DisplayText: s.Name,
                                Summary: $"turn {s.Turn}  {s.SizeText}",
                                Role: TextRole.Normal))
                            .ToArray();
                    }
                    catch
                    {
                        return [];
                    }

                default:
                    return [];
            }
        }

        public static PlayerCommandInfo? Describing(string input)
        {
            if (!IsCommand(input))
            {
                return null;
            }

            var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var name = parts.Length > 0 ? parts[0].TrimStart('/') : string.Empty;

            foreach (var command in All)
            {
                if (command.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    return command;
                }
            }

            return null;
        }

        public static PlayerCommandResult Execute(string input, SaveStore store)
        {
            var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var name = parts.Length > 0 ? parts[0].TrimStart('/').ToLowerInvariant() : string.Empty;
            var argument = parts.Length > 1 ? parts[1] : string.Empty;

            var lines = new List<StyledLine>();

            try
            {
                switch (name)
                {
                    case "help":
                        Help(lines);
                        break;
                    case "story":
                        Story(lines, store);
                        break;
                    case "rolls":
                        Rolls(lines, store);
                        break;
                    case "inventory":
                    case "inv":
                        Inventory(lines, store);
                        break;
                    case "character":
                    case "characters":
                    case "who":
                        Characters(lines, store, argument);
                        break;
                    case "location":
                    case "locations":
                    case "where":
                        Locations(lines, store, argument);
                        break;
                    case "saves":
                        Saves(lines, store);
                        break;
                    case "delete":
                        Delete(lines, store, argument);
                        break;
                    case "system-prompt":
                        return SystemPrompt(lines);
                    case "quit":
                    case "exit":
                        return new PlayerCommandResult { Lines = lines, Quit = true };
                    default:
                        lines.Add(StyledLine.FromText($"There is no command '/{name}'. Try /help.", TextRole.Danger));
                        break;
                }
            }
            catch (SaveException ex)
            {
                lines.Add(StyledLine.FromText(ex.Message, TextRole.Danger));
            }

            return new PlayerCommandResult { Lines = lines };
        }

        private static void Help(List<StyledLine> lines)
        {
            lines.Add(StyledLine.FromText("Commands", TextRole.System));

            foreach (var command in All)
            {
                if (!command.IsAlias)
                {
                    Describe(lines, command.Usage, command.Summary);
                }
            }

            lines.Add(StyledLine.FromText("Anything else is spoken to the world.", TextRole.System));
            lines.Add(StyledLine.FromText("Keys", TextRole.System));

            Describe(lines, "Ctrl+= / Ctrl+-", "your terminal's own text size");
            Describe(lines, "PgUp / PgDn", "scroll the transcript");
            Describe(lines, "Ctrl+G", "write this line in an editor");
            Describe(lines, "Esc", "back to the menu");
        }

        private static void Story(List<StyledLine> lines, SaveStore store)
        {
            var events = store.Story.Read().Entries;

            if (events.Count == 0)
            {
                lines.Add(StyledLine.FromText("Nothing has happened yet.", TextRole.System));
                return;
            }

            lines.Add(StyledLine.FromText("The story so far", TextRole.System));

            foreach (var entry in events)
            {
                var line = new StyledLine();
                line.Append($"  {entry.Turn,4}  ", TextRole.System);
                line.Append(entry.Title, TextRole.Normal);
                lines.Add(line);

                if (entry.Detail.Length > 0)
                {
                    lines.Add(StyledLine.FromText($"        {entry.Detail}", TextRole.System));
                }
            }
        }

        private static void Rolls(List<StyledLine> lines, SaveStore store)
        {
            var allEntries = store.Rolls.Read().Entries;

            if (allEntries.Count == 0)
            {
                lines.Add(StyledLine.FromText("No dice have been thrown yet.", TextRole.System));
                return;
            }

            var revealedSeqs = new HashSet<int>();
            foreach (var r in allEntries)
            {
                if (r.RevealsSeq > 0)
                {
                    revealedSeqs.Add(r.RevealsSeq);
                }
            }

            var rolls = new List<DiceRoll>();
            foreach (var r in allEntries)
            {
                if (r.RevealsSeq > 0)
                {
                    continue;
                }

                if (revealedSeqs.Contains(r.Seq))
                {
                    r.Revealed = true;
                }

                rolls.Add(r);
            }

            lines.Add(StyledLine.FromText("The dice so far", TextRole.System));

            var characters = store.ReadCharacters();

            foreach (var roll in rolls)
            {
                var line = new StyledLine();
                line.Append($"  {roll.Turn,4}  ", TextRole.System);

                foreach (var span in RollWatcher.Line(roll, SaveStore.FindCharacterById(characters, roll.CharacterId)?.Name).Spans)
                {
                    line.Append(span);
                }

                lines.Add(line);

                if (roll.Attribute is { Length: > 0 } && roll.Reason is { Length: > 0 })
                {
                    lines.Add(StyledLine.FromText($"        {roll.Reason}", TextRole.System));
                }
            }
        }

        private static void Inventory(List<StyledLine> lines, SaveStore store)
        {
            var characters = store.ReadCharacters();
            var player = SaveStore.Player(characters);
            var inventoryFile = store.ReadInventory();
            var playerInv = player is not null ? inventoryFile.Find(player.Id) : null;
            var itemFile = store.ReadItems();

            var money = playerInv?.Money ?? 0;
            var purse = new StyledLine();
            purse.Append("Money  ", TextRole.System);
            purse.Append(money.ToString(), TextRole.Item);
            lines.Add(purse);

            var items = playerInv?.Items ?? [];
            if (items.Count == 0)
            {
                lines.Add(StyledLine.FromText("You are carrying nothing else.", TextRole.System));
                return;
            }

            lines.Add(StyledLine.FromText("Carrying", TextRole.System));

            foreach (var stack in items)
            {
                var def = SaveStore.FindItemById(itemFile, stack.ItemId);
                if (def is null)
                {
                    continue;
                }

                var line = new StyledLine();
                line.Append("  ", TextRole.System);
                line.Append(def.Name, TextRole.Item);

                if (stack.Quantity > 1)
                {
                    line.Append($" x{stack.Quantity}", TextRole.System);
                }

                lines.Add(line);

                if (def.Description.Length > 0)
                {
                    lines.Add(StyledLine.FromText($"      {def.Description}", TextRole.System));
                }
            }
        }

        private static void Characters(List<StyledLine> lines, SaveStore store, string argument)
        {
            var file = store.ReadCharacters();

            if (file.Characters.Count == 0)
            {
                lines.Add(StyledLine.FromText("You have met nobody.", TextRole.System));
                return;
            }

            if (argument.Length == 0)
            {
                lines.Add(StyledLine.FromText("Who you know", TextRole.System));

                foreach (var character in file.Characters)
                {
                    var line = new StyledLine();
                    line.Append("  ", TextRole.System);
                    line.Append(character.Name, TextRole.Normal);
                    line.Append(
                        $"  {character.Health}/{character.MaxHealth}",
                        character.Health <= character.MaxHealth / 4 ? TextRole.Danger : TextRole.System);

                    if (character.Kind == CharacterKind.Player)
                    {
                        line.Append("  (you)", TextRole.System);
                    }

                    lines.Add(line);
                }

                lines.Add(StyledLine.FromText("/character <name> for what someone remembers.", TextRole.System));
                return;
            }

            var found = SaveStore.FindCharacter(file, argument);

            if (found is null)
            {
                lines.Add(StyledLine.FromText($"You know nobody called '{argument}'.", TextRole.Danger));
                return;
            }

            lines.Add(StyledLine.FromText(found.Name, TextRole.Normal));

            if (found.Description.Length > 0)
            {
                lines.Add(StyledLine.FromText($"  {found.Description}", TextRole.System));
            }

            var attributes = new StyledLine();
            attributes.Append("  ", TextRole.System);

            foreach (var attribute in CharacterAttributes.All(found))
            {
                if (attributes.Length > 2)
                {
                    attributes.Append("   ", TextRole.System);
                }

                attributes.Append($"{attribute.Name} ", TextRole.System);
                attributes.Append(attribute.Score.ToString(), TextRole.Normal);
                attributes.Append(
                    $" ({CharacterAttributes.Sign(CharacterAttributes.Modifier(attribute.Score))})",
                    TextRole.System);
            }

            lines.Add(attributes);

            var storyEvents = store.Story.Read().Entries
                .Where(ev => ev.CharacterIds.Contains(found.Id, StringComparer.Ordinal)
                    || ev.Title.Contains(found.Name, StringComparison.OrdinalIgnoreCase)
                    || ev.Detail.Contains(found.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (storyEvents.Count == 0)
            {
                lines.Add(StyledLine.FromText("  Nothing recorded in the story yet.", TextRole.System));
                return;
            }

            foreach (var ev in storyEvents)
            {
                var line = new StyledLine();
                line.Append($"  {ev.Turn,4}  ", TextRole.System);
                line.Append(ev.Title, TextRole.Speech);
                lines.Add(line);
                if (ev.Detail.Length > 0)
                {
                    lines.Add(StyledLine.FromText($"        {ev.Detail}", TextRole.System));
                }
            }
        }

        private static void Locations(List<StyledLine> lines, SaveStore store, string argument)
        {
            var file = store.ReadLocations();

            if (file.Locations.Count == 0)
            {
                lines.Add(StyledLine.FromText("You have been nowhere.", TextRole.System));
                return;
            }

            var characters = store.ReadCharacters();
            var index = WorldIndex.Build(characters);

            if (argument.Length == 0)
            {
                lines.Add(StyledLine.FromText("Where you have been", TextRole.System));

                foreach (var location in file.Locations)
                {
                    var line = new StyledLine();
                    line.Append("  ", TextRole.System);
                    line.Append(location.Name, TextRole.Place);

                    var present = string.Join(", ", index.NamesOf(location.CharacterIds));
                    if (present.Length > 0)
                    {
                        line.Append($"  {present}", TextRole.System);
                    }

                    lines.Add(line);
                }

                lines.Add(StyledLine.FromText("/location <name> for what happened somewhere.", TextRole.System));
                return;
            }

            var found = SaveStore.FindLocation(file, argument);

            if (found is null)
            {
                lines.Add(StyledLine.FromText($"You know nowhere called '{argument}'.", TextRole.Danger));
                return;
            }

            lines.Add(StyledLine.FromText(found.Name, TextRole.Place));

            if (found.Description.Length > 0)
            {
                lines.Add(StyledLine.FromText($"  {found.Description}", TextRole.System));
            }

            var itemFile = store.ReadItems();
            if (found.Items.Count > 0)
            {
                lines.Add(StyledLine.FromText("  Items here:", TextRole.System));
                foreach (var stack in found.Items)
                {
                    var def = SaveStore.FindItemById(itemFile, stack.ItemId);
                    if (def is not null)
                    {
                        var line = new StyledLine();
                        line.Append($"    {def.Name}", TextRole.Item);
                        if (stack.Quantity > 1) line.Append($" x{stack.Quantity}", TextRole.System);
                        lines.Add(line);
                    }
                }
            }

            var events = store.Story.Read().Entries
                .Where(ev => ev.LocationIds.Contains(found.Id, StringComparer.Ordinal)
                    || ev.Title.Contains(found.Name, StringComparison.OrdinalIgnoreCase)
                    || ev.Detail.Contains(found.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (events.Count == 0)
            {
                lines.Add(StyledLine.FromText("  Nothing has happened here yet.", TextRole.System));
                return;
            }

            foreach (var entry in events)
            {
                var line = new StyledLine();
                line.Append($"  {entry.Turn,4}  ", TextRole.System);
                line.Append(entry.Title, TextRole.Normal);
                lines.Add(line);
                if (entry.Detail.Length > 0)
                {
                    lines.Add(StyledLine.FromText($"        {entry.Detail}", TextRole.System));
                }
            }
        }

        private static void Saves(List<StyledLine> lines, SaveStore store)
        {
            var saves = SavePaths.List();

            lines.Add(StyledLine.FromText("Saves", TextRole.System));

            foreach (var save in saves)
            {
                var line = new StyledLine();
                line.Append("  ", TextRole.System);
                line.Append(save.Name, save.Name == store.Name ? TextRole.Item : TextRole.Normal);
                line.Append($"  turn {save.Turn}  {save.SizeText}", TextRole.System);
                lines.Add(line);
            }

            lines.Add(StyledLine.FromText(SavePaths.Root, TextRole.System));
        }

        private static void Delete(List<StyledLine> lines, SaveStore store, string argument)
        {
            if (argument.Length == 0)
            {
                lines.Add(StyledLine.FromText("Name the save to delete: /delete <name>.", TextRole.System));
                lines.Add(StyledLine.FromText("/saves lists them. There is no undo.", TextRole.System));
                return;
            }

            if (SaveStore.Matches(argument, store.Name))
            {
                lines.Add(StyledLine.FromText(
                    "That is the save you are playing. Leave with /quit and delete it from the menu with Del.",
                    TextRole.Danger));
                return;
            }

            if (!SavePaths.IsValidName(argument))
            {
                lines.Add(StyledLine.FromText($"'{argument}' was never a save name.", TextRole.Danger));
                return;
            }

            if (!SavePaths.Delete(argument))
            {
                lines.Add(StyledLine.FromText($"There is no save called '{argument}'.", TextRole.Danger));
                return;
            }

            lines.Add(StyledLine.FromText($"Deleted '{argument}'.", TextRole.System));
        }

        private static PlayerCommandResult SystemPrompt(List<StyledLine> lines)
        {
            lines.Add(StyledLine.FromText(
                "Editing the narrator's instructions ends this session: the narrator is built once, when the save opens.",
                TextRole.System));

            lines.Add(StyledLine.FromText(
                "Save and close the editor to leave for the menu, and open the save again to play with what you wrote.",
                TextRole.System));

            return new PlayerCommandResult { Lines = lines, EditSystemPrompt = true };
        }

        private static void Describe(List<StyledLine> lines, string command, string meaning)
        {
            var line = new StyledLine();
            line.Append($"  {command,-20}", TextRole.Command);
            line.Append(meaning, TextRole.System);
            lines.Add(line);
        }
    }
}
