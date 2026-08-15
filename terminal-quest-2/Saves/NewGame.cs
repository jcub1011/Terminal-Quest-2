namespace TerminalQuest.Saves
{
    /// <summary>
    /// Seeds a fresh save with the character the player described.
    /// </summary>
    internal static class NewGame
    {
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

            var metadata = store.ReadMetadata();
            metadata.SchemaVersion = SaveStore.CurrentSchemaVersion;
            store.WriteMetadata(metadata);

            var characters = store.ReadCharacters();
            var player = new Character
            {
                Id = characters.TakeId(),
                Name = trimmedName,
                Kind = CharacterKind.Player,
                MaxHealth = template.MaxHealth,
                Health = template.MaxHealth,
                Description = ComposeDescription(description, template),
            };

            CharacterAttributes.Seed(player, template.Attributes);

            characters.Characters.Add(player);
            store.WriteCharacters(characters);

            var itemFile = store.ReadItems();
            var inventory = store.ReadInventory();
            var playerInventory = inventory.GetOrCreate(player.Id);
            playerInventory.Money = template.StartingMoney;

            foreach (var item in template.StartingItems)
            {
                var definition = SaveStore.FindItem(itemFile, item.Name);
                if (definition is null)
                {
                    definition = new ItemDefinition
                    {
                        Id = itemFile.TakeId(),
                        Name = item.Name,
                        Description = item.Description,
                    };
                    itemFile.Items.Add(definition);
                }

                playerInventory.Items.Add(new ItemStack
                {
                    ItemId = definition.Id,
                    Quantity = item.Quantity,
                });
            }

            store.WriteItems(itemFile);
            store.WriteInventory(inventory);

            if (startLocation is not { Length: > 0 } || startLocation.AsSpan().IsWhiteSpace())
            {
                return;
            }

            var place = startLocation.Trim();

            var locations = store.ReadLocations();
            var start = new Location
            {
                Id = locations.TakeId(),
                Name = place,
                Description = string.Empty,
            };
            locations.Locations.Add(start);
            store.WriteLocations(locations);

            store.MoveCharacter(player.Id, start.Id);
        }

        private static string ComposeDescription(string? description, ClassTemplate template)
        {
            var typed = description?.Trim() ?? string.Empty;

            if (typed.Length == 0)
            {
                return template.Aptitude;
            }

            var separator = char.IsPunctuation(typed[^1]) ? " " : ". ";

            return typed + separator + template.Aptitude;
        }
    }
}
