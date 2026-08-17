using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TerminalQuest.Mcp;
using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Modal dialog displaying details, descriptions, stats, and memories for a clicked entity.
    /// </summary>
    internal static class EntityDetailsDialog
    {
        public static void Show(IApplication? app, SaveStore? store, string entityId)
        {
            if (app is null || store is null || string.IsNullOrWhiteSpace(entityId))
            {
                return;
            }

            var (title, content) = FormatEntityDetails(store, entityId.Trim());

            var lines = content.Split('\n');
            var dialogHeight = Math.Clamp(lines.Length + 6, 12, 24);
            var maxLineWidth = lines.Length > 0 ? lines.Max(l => l.Length) : 40;
            var dialogWidth = Math.Clamp(maxLineWidth + 6, 50, 76);

            var dialog = new Dialog
            {
                Title = title,
                Width = dialogWidth,
                Height = dialogHeight,
                BorderStyle = LineStyle.Rounded,
            };
            dialog.SetScheme(Theme.CreateScheme());

#pragma warning disable CS0618 // TextView is obsolete in Terminal.Gui v2
            var textView = new TextView
            {
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill() - 2,
                Text = content,
                ReadOnly = true,
                WordWrap = true,
            };
#pragma warning restore CS0618
            textView.SetScheme(Theme.CreateScheme());

            var closeButton = new Button
            {
                Text = "Close",
                IsDefault = true,
            };
            closeButton.SetScheme(Theme.CreateScheme());
            closeButton.Accepting += (_, _) => app.RequestStop(dialog);

            dialog.KeyDown += (_, key) =>
            {
                if (key == Key.Esc)
                {
                    app.RequestStop(dialog);
                }
            };

            dialog.Add(textView);
            dialog.AddButton(closeButton);

            dialog.Initialized += (_, _) => closeButton.SetFocus();

            app.Run(dialog);
        }

        public static (string Title, string Content) FormatEntityDetails(SaveStore store, string entityId)
        {
            var characters = store.ReadCharacters();
            var locations = store.ReadLocations();
            var items = store.ReadItems();
            var inventory = store.ReadInventory();
            var storyEvents = store.Story.Read().Entries;
            var index = WorldIndex.Build(characters, locations, items);

            // 1. Check Character
            var character = SaveStore.FindCharacterById(characters, entityId)
                ?? SaveStore.FindCharacter(characters, entityId);

            if (character is not null)
            {
                return FormatCharacter(character, inventory.Find(character.Id), items, storyEvents);
            }

            // 2. Check Location
            var location = SaveStore.FindLocationById(locations, entityId)
                ?? SaveStore.FindLocation(locations, entityId);

            if (location is not null)
            {
                return FormatLocation(location, index, items, storyEvents);
            }

            // 3. Check Item
            var item = SaveStore.FindItemById(items, entityId)
                ?? SaveStore.FindItem(items, entityId);

            if (item is not null)
            {
                return FormatItem(item, characters, locations, inventory, storyEvents);
            }

            return ($"Entity: {entityId}", $"No entity found on record with identifier '{entityId}'.");
        }

        private static (string Title, string Content) FormatCharacter(
            Character character,
            CharacterInventory? charInv,
            ItemFile itemFile,
            IReadOnlyList<StoryEvent> allEvents)
        {
            var title = $"Character: {character.Name}";
            var sb = new StringBuilder();

            sb.AppendLine($"{character.Name} ({QuestRender.Kind(character.Kind)})");
            sb.AppendLine($"Health: {character.Health}/{character.MaxHealth}");

            if (!string.IsNullOrWhiteSpace(character.Description))
            {
                sb.AppendLine();
                sb.AppendLine(character.Description.Trim());
            }

            sb.AppendLine();
            sb.AppendLine(QuestRender.Attributes(character));

            if (charInv is not null)
            {
                var invText = QuestRender.Inventory(charInv, itemFile);
                if (!string.IsNullOrWhiteSpace(invText))
                {
                    sb.AppendLine();
                    sb.AppendLine(invText);
                }
            }

            var events = allEvents
                .Where(ev => ev.CharacterIds.Contains(character.Id, StringComparer.Ordinal)
                    || ev.Title.Contains(character.Name, StringComparison.OrdinalIgnoreCase)
                    || ev.Detail.Contains(character.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            sb.AppendLine();
            sb.AppendLine("--- Memories & Story Events ---");
            if (events.Count == 0)
            {
                sb.AppendLine("No recorded memories or events.");
            }
            else
            {
                foreach (var ev in events)
                {
                    sb.AppendLine($"[Turn {ev.Turn}] {ev.Title}");
                    if (!string.IsNullOrWhiteSpace(ev.Detail))
                    {
                        sb.AppendLine($"  {ev.Detail.Trim()}");
                    }
                }
            }

            return (title, sb.ToString().TrimEnd());
        }

        private static (string Title, string Content) FormatLocation(
            Location location,
            WorldIndex index,
            ItemFile itemFile,
            IReadOnlyList<StoryEvent> allEvents)
        {
            var title = $"Location: {location.Name}";
            var sb = new StringBuilder();

            sb.AppendLine(location.Name);
            if (!string.IsNullOrWhiteSpace(location.Description))
            {
                sb.AppendLine();
                sb.AppendLine(location.Description.Trim());
            }

            sb.AppendLine();
            var roster = string.Join(", ", index.NamesOf(location.CharacterIds));
            sb.AppendLine(roster.Length == 0 ? "Here now: nobody." : $"Here now: {roster}.");

            if (location.Items.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Items here:");
                foreach (var stack in location.Items)
                {
                    var def = SaveStore.FindItemById(itemFile, stack.ItemId);
                    if (def is not null)
                    {
                        sb.AppendLine(QuestRender.Item(def, stack.Quantity));
                    }
                }
            }

            var events = allEvents
                .Where(ev => ev.LocationIds.Contains(location.Id, StringComparer.Ordinal)
                    || ev.Title.Contains(location.Name, StringComparison.OrdinalIgnoreCase)
                    || ev.Detail.Contains(location.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            sb.AppendLine();
            sb.AppendLine("--- Events Here ---");
            if (events.Count == 0)
            {
                sb.AppendLine("No recorded events here.");
            }
            else
            {
                foreach (var ev in events)
                {
                    sb.AppendLine($"[Turn {ev.Turn}] {ev.Title}");
                    if (!string.IsNullOrWhiteSpace(ev.Detail))
                    {
                        sb.AppendLine($"  {ev.Detail.Trim()}");
                    }
                }
            }

            return (title, sb.ToString().TrimEnd());
        }

        private static (string Title, string Content) FormatItem(
            ItemDefinition item,
            CharacterFile characters,
            LocationFile locations,
            InventoryFile inventoryFile,
            IReadOnlyList<StoryEvent> allEvents)
        {
            var title = $"Item: {item.Name}";
            var sb = new StringBuilder();

            sb.AppendLine(item.Name);
            if (!string.IsNullOrWhiteSpace(item.Description))
            {
                sb.AppendLine();
                sb.AppendLine(item.Description.Trim());
            }

            var holders = new List<string>();

            bool ItemMatches(ItemStack stack) =>
                string.Equals(stack.ItemId, item.Id, StringComparison.OrdinalIgnoreCase)
                || SaveStore.Matches(stack.ItemId, item.Id)
                || SaveStore.Matches(stack.ItemId, item.Name);

            foreach (var charInv in inventoryFile.Inventories)
            {
                var stack = charInv.Items.FirstOrDefault(ItemMatches);
                if (stack is not null)
                {
                    var owner = SaveStore.FindCharacterById(characters, charInv.CharacterId)
                        ?? SaveStore.FindCharacter(characters, charInv.CharacterId);
                    var ownerName = owner?.Name ?? (string.Equals(charInv.CharacterId, "player", StringComparison.OrdinalIgnoreCase) ? (SaveStore.Player(characters)?.Name ?? "the player") : charInv.CharacterId);
                    holders.Add($"Carried by {ownerName} (x{stack.Quantity})");
                }
            }

            foreach (var loc in locations.Locations)
            {
                var stack = loc.Items.FirstOrDefault(ItemMatches);
                if (stack is not null)
                {
                    holders.Add($"At {loc.Name} (x{stack.Quantity})");
                }
            }

            sb.AppendLine();
            sb.AppendLine("Location / Possession:");
            if (holders.Count == 0)
            {
                sb.AppendLine("  Not currently carried by anyone or placed at a known location.");
            }
            else
            {
                foreach (var h in holders)
                {
                    sb.AppendLine($"  {h}");
                }
            }

            var events = allEvents
                .Where(ev => ev.ItemIds.Contains(item.Id, StringComparer.Ordinal)
                    || ev.Title.Contains(item.Name, StringComparison.OrdinalIgnoreCase)
                    || ev.Detail.Contains(item.Name, StringComparison.OrdinalIgnoreCase))
                .ToList();

            sb.AppendLine();
            sb.AppendLine("--- Story Events Involving Item ---");
            if (events.Count == 0)
            {
                sb.AppendLine("No recorded events.");
            }
            else
            {
                foreach (var ev in events)
                {
                    sb.AppendLine($"[Turn {ev.Turn}] {ev.Title}");
                    if (!string.IsNullOrWhiteSpace(ev.Detail))
                    {
                        sb.AppendLine($"  {ev.Detail.Trim()}");
                    }
                }
            }

            return (title, sb.ToString().TrimEnd());
        }
    }
}
