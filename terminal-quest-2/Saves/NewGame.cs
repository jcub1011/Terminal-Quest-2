namespace TerminalQuest.Saves
{
    /// <summary>
    /// Seeds a fresh save with the character the player described.
    /// <para>
    /// The only place the game process writes story data itself. Everywhere else the narrator owns
    /// the world and the TUI only reads it - but who the player is is the player's answer, not the
    /// model's, and asking for it in prose costs a turn and comes back different every time.
    /// </para>
    /// <para>
    /// Written before the narrator process exists, so there is no second writer to race with here.
    /// </para>
    /// </summary>
    internal static class NewGame
    {
        /// <summary>
        /// Writes the player, their kit and - when one was named - where they begin.
        /// </summary>
        /// <param name="store">The save to seed. Expected to hold no characters yet.</param>
        /// <param name="name">The player's name. Permanent: the narrator cannot rename anyone.</param>
        /// <param name="description">Free prose about who they are. May be empty.</param>
        /// <param name="template">The archetype chosen, which decides health, coin and the kit.</param>
        /// <param name="startLocation">
        /// Where they begin, or null/blank to leave it to the narrator.
        /// </param>
        /// <exception cref="SaveException">A document could not be written.</exception>
        public static void Create(
            SaveStore store,
            string name,
            string description,
            ClassTemplate template,
            string? startLocation)
        {
            ArgumentNullException.ThrowIfNull(store);
            ArgumentNullException.ThrowIfNull(template);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            var trimmedName = name.Trim();

            var characters = store.ReadCharacters();
            characters.Characters.Add(new Character
            {
                Name = trimmedName,
                Kind = CharacterKind.Player,
                MaxHealth = template.MaxHealth,
                Health = template.MaxHealth,
                Description = ComposeDescription(description, template),
            });
            store.WriteCharacters(characters);

            var inventory = store.ReadInventory();
            inventory.Money = template.StartingMoney;

            foreach (var item in template.StartingItems)
            {
                // Copied, not shared: the templates are static and the narrator edits items in
                // place, so handing one out would spend the next save's kit.
                inventory.Items.Add(new Item
                {
                    Name = item.Name,
                    Quantity = item.Quantity,
                    Description = item.Description,
                });
            }

            store.WriteInventory(inventory);

            if (startLocation is not { Length: > 0 } || startLocation.AsSpan().IsWhiteSpace())
            {
                return;
            }

            var place = startLocation.Trim();

            var locations = store.ReadLocations();
            locations.Locations.Add(new Location
            {
                Name = place,

                // Left empty on purpose. The narrator writes what the place looks like on the first
                // turn; a description invented here would be one the story never agreed to.
                Description = string.Empty,
            });
            store.WriteLocations(locations);

            store.MoveCharacter(trimmedName, place);
        }

        /// <summary>
        /// The player's own words followed by what their class makes them good at, either half
        /// tolerated as empty.
        /// </summary>
        private static string ComposeDescription(string? description, ClassTemplate template)
        {
            var typed = description?.Trim() ?? string.Empty;

            if (typed.Length == 0)
            {
                return template.Aptitude;
            }

            // A description that already ends in punctuation should not gain a second full stop.
            var separator = char.IsPunctuation(typed[^1]) ? " " : ". ";

            return typed + separator + template.Aptitude;
        }
    }
}
