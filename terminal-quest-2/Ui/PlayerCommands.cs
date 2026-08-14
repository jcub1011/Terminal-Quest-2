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
        /// <summary>Whether the input is addressed to the game rather than to the narrator.</summary>
        public static bool IsCommand(string input) => input.StartsWith('/');

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

        private static void Help(List<StyledLine> lines)
        {
            lines.Add(StyledLine.FromText("Commands", TextRole.System));
            Describe(lines, "/story", "everything that has happened");
            Describe(lines, "/inventory", "what you are carrying");
            Describe(lines, "/characters [name]", "who you have met, and what they remember");
            Describe(lines, "/locations [name]", "where you have been, and what happened there");
            Describe(lines, "/saves", "every save on this machine");
            Describe(lines, "/delete <name>", "destroy another save, for good");
            Describe(lines, "/quit", "leave the game");
            lines.Add(StyledLine.FromText("Anything else is spoken to the world.", TextRole.System));
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

        private static void Inventory(List<StyledLine> lines, SaveStore store)
        {
            var items = store.ReadInventory().Items;

            if (items.Count == 0)
            {
                lines.Add(StyledLine.FromText("You are carrying nothing.", TextRole.System));
                return;
            }

            lines.Add(StyledLine.FromText("Carrying", TextRole.System));

            foreach (var item in items)
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

            var playerName = SaveStore.PlayerName(store.ReadCharacters());

            if (argument.Length == 0)
            {
                lines.Add(StyledLine.FromText("Where you have been", TextRole.System));

                foreach (var location in file.Locations)
                {
                    var line = new StyledLine();
                    line.Append("  ", TextRole.System);
                    line.Append(location.Name, TextRole.Place);

                    if (location.Characters.Count > 0)
                    {
                        line.Append($"  {string.Join(", ", location.Characters)}", TextRole.System);
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
