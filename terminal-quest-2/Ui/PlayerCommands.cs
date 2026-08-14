using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The player's own commands, answered by the game rather than forwarded to the narrator.
    /// <para>
    /// These read the same files the narrator writes through its tools, so what the player sees
    /// here is the record itself and not the narrator's recollection of it. That is the whole
    /// point: <c>/inventory</c> is checkable in a way asking "what am I carrying?" is not.
    /// </para>
    /// <para>
    /// Parsing is positional and space-delimited - <c>[0]</c> is the command, the rest is its
    /// argument - so no command name may contain a space. Output goes into the transcript rather
    /// than a modal, because the transcript already scrolls, wraps and colours by role.
    /// </para>
    /// </summary>
    internal static class PlayerCommands
    {
        /// <summary>
        /// Every command the game answers, in the order <c>/help</c> lists them, each alias
        /// directly under the command it stands for.
        /// <para>
        /// This table and the switch in <see cref="Execute"/> are a pair and have to be changed
        /// together: the table is what the player is shown and offered, the switch is what
        /// actually runs. A name in one and not the other is either a command nobody can find or
        /// a suggestion that errors when taken.
        /// </para>
        /// </summary>
        public static readonly IReadOnlyList<PlayerCommandInfo> All =
        [
            new("story", "", "everything that has happened"),
            new("rolls", "", "every die the world has thrown"),
            new("inventory", "", "what you are carrying"),
            new("inv", "", "what you are carrying", IsAlias: true),
            new("characters", "[name]", "who you have met, and what they remember"),
            new("who", "[name]", "who you have met, and what they remember", IsAlias: true),
            new("locations", "[name]", "where you have been, and what happened there"),
            new("where", "[name]", "where you have been, and what happened there", IsAlias: true),
            new("saves", "", "every save on this machine"),
            new("delete", "<name>", "destroy another save, for good"),
            new("help", "", "this list"),
            new("quit", "", "leave this save and go back to the menu"),
            new("exit", "", "leave this save and go back to the menu", IsAlias: true),
        ];

        /// <summary>Whether the input is addressed to the game rather than to the narrator.</summary>
        public static bool IsCommand(string input) => input.StartsWith('/');

        /// <summary>
        /// Every command the input could still turn out to be, for the suggestions above the box.
        /// <para>
        /// Only a bare command word is completed. Once a space has been typed the player has moved
        /// on to the argument, and a list of commands is no longer an answer to anything - so this
        /// returns nothing rather than going on offering names that can no longer be reached.
        /// </para>
        /// </summary>
        public static IReadOnlyList<PlayerCommandInfo> Matching(string input)
        {
            if (!IsCommand(input) || input.Contains(' '))
            {
                return [];
            }

            var typed = input[1..];

            // Case-insensitive to match Execute, which lower-cases the name before dispatching:
            // /INV runs, so /IN has to be offered /inventory.
            return All
                .Where(command => command.Name.StartsWith(typed, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        /// <summary>
        /// The command an input is already addressed to by name, or null when it names none.
        /// <para>
        /// Where <see cref="Matching"/> answers "which of these might you mean", this answers
        /// "here is what the one you have named takes" - so the hint above the box outlives the
        /// space that settles the name, and <c>/delete</c> is still saying it wants one while the
        /// player is typing it.
        /// </para>
        /// </summary>
        public static PlayerCommandInfo? Describing(string input)
        {
            if (!IsCommand(input))
            {
                return null;
            }

            // Split exactly as Execute does, so the command this claims to describe is the command
            // that would actually run.
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

        /// <summary>Runs a command. Only call this when <see cref="IsCommand"/> is true.</summary>
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
                    case "characters":
                    case "who":
                        Characters(lines, store, argument);
                        break;
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
                    case "quit":
                    case "exit":
                        return new PlayerCommandResult { Lines = lines, Quit = true };
                    default:
                        // Not forwarded to the narrator: a typo must never quietly become a story
                        // prompt, which is exactly what would happen if this fell through.
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

        /// <summary>
        /// The list of commands, read out of <see cref="All"/> rather than written out again here,
        /// so a command added to the table cannot go unmentioned. Aliases are left out: they are
        /// spellings of rows already on the list, not further things the game does.
        /// <para>
        /// The keys underneath are written out, because they are not commands and have no table:
        /// <see cref="All"/> is paired with the switch in <see cref="Execute"/>, and a row added
        /// there for a key would be a suggestion that errors when taken.
        /// </para>
        /// </summary>
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

            // First on the list, and named as the terminal's rather than the game's, because that
            // is what it is: the game draws characters and never chooses their size.
            Describe(lines, "Ctrl+= / Ctrl+-", "your terminal's own text size");
            Describe(lines, "PgUp / PgDn", "scroll the transcript");
            Describe(lines, "Ctrl+G", "write this line in an editor");
            Describe(lines, "Esc", "back to the menu");
        }

        private static void Story(List<StyledLine> lines, SaveStore store)
        {
            var events = store.ReadStory().Events;

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

        /// <summary>
        /// Every roll on record, oldest first.
        /// </summary>
        /// <remarks>
        /// A hidden roll is listed - the player is always told a die was thrown - but its result is
        /// not printed, here or ever, unless the narrator has since revealed it. That has to hold in
        /// both places: a number the player could read back afterwards was never hidden at all, and
        /// a command that quietly undid the concealment would make hiding pointless.
        /// <para>
        /// Safe to run mid-turn, like every command here. The narrator writes through a temporary
        /// file that is renamed over the real one, so a reader never sees half a document even while
        /// the other process is writing.
        /// </para>
        /// </remarks>
        private static void Rolls(List<StyledLine> lines, SaveStore store)
        {
            var rolls = store.ReadRolls().Rolls;

            if (rolls.Count == 0)
            {
                lines.Add(StyledLine.FromText("No dice have been thrown yet.", TextRole.System));
                return;
            }

            lines.Add(StyledLine.FromText("The dice so far", TextRole.System));

            // Read once for the whole list: rolls hold ids, and every name on screen comes from here.
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

                // The reason gets a line of its own only when the headline showed the attribute
                // instead, so nothing the player was told at the time is missing from the record.
                if (roll.Attribute is { Length: > 0 } && roll.Reason is { Length: > 0 })
                {
                    lines.Add(StyledLine.FromText($"        {roll.Reason}", TextRole.System));
                }
            }
        }

        private static void Inventory(List<StyledLine> lines, SaveStore store)
        {
            var file = store.ReadInventory();

            // Money first, and always: it is spent rather than carried, and an empty purse is worth
            // reading before the player goes looking for something to buy.
            var purse = new StyledLine();
            purse.Append("Money  ", TextRole.System);
            purse.Append(file.Money.ToString(), TextRole.Item);
            lines.Add(purse);

            if (file.Items.Count == 0)
            {
                lines.Add(StyledLine.FromText("You are carrying nothing else.", TextRole.System));
                return;
            }

            lines.Add(StyledLine.FromText("Carrying", TextRole.System));

            foreach (var item in file.Items)
            {
                var line = new StyledLine();
                line.Append("  ", TextRole.System);
                line.Append(item.Name, TextRole.Item);

                if (item.Quantity > 1)
                {
                    line.Append($" x{item.Quantity}", TextRole.System);
                }

                lines.Add(line);

                if (item.Description.Length > 0)
                {
                    lines.Add(StyledLine.FromText($"      {item.Description}", TextRole.System));
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

            var playerName = SaveStore.PlayerName(file);

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

                lines.Add(StyledLine.FromText("/characters <name> for what someone remembers.", TextRole.System));
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

            // The one place a freeform attribute is visible to the player: the status pane has room
            // for the six and no more, and the roll line only ever names the one it applied.
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

            if (found.Memories.Count == 0)
            {
                lines.Add(StyledLine.FromText("  Remembers nothing of note.", TextRole.System));
                return;
            }

            foreach (var memory in found.Memories)
            {
                var line = new StyledLine();
                line.Append($"  {memory.Turn,4}  ", TextRole.System);
                line.Append(Placeholders.Resolve(memory.Text, found.Name, playerName), TextRole.Speech);
                lines.Add(line);
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

            // Read once and kept: the roster below holds ids, so it needs the same document the
            // player's name came from to render anybody at all.
            var characters = store.ReadCharacters();
            var playerName = SaveStore.PlayerName(characters);
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

                lines.Add(StyledLine.FromText("/locations <name> for what happened somewhere.", TextRole.System));
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

            if (found.Events.Count == 0)
            {
                lines.Add(StyledLine.FromText("  Nothing has happened here.", TextRole.System));
                return;
            }

            foreach (var entry in found.Events)
            {
                var line = new StyledLine();
                line.Append($"  {entry.Turn,4}  ", TextRole.System);
                line.Append(Placeholders.Resolve(entry.Text, found.Name, playerName), TextRole.Normal);
                lines.Add(line);
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

        /// <summary>
        /// Destroys another save.
        /// <para>
        /// The name has to be typed out in full - there is no highlighted thing to mean, and no
        /// undo afterwards, so the typing is the confirmation. The save being played is refused
        /// outright: the narrator's state server has its folder open, and pulling the files out
        /// from under a live session would corrupt the very playthrough the player is in.
        /// </para>
        /// </summary>
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

        private static void Describe(List<StyledLine> lines, string command, string meaning)
        {
            var line = new StyledLine();
            line.Append($"  {command,-20}", TextRole.Command);
            line.Append(meaning, TextRole.System);
            lines.Add(line);
        }
    }
}
