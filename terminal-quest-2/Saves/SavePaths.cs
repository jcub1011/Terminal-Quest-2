using System.Text.Json;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// Where saves live on disk, and the rules for naming one.
    /// <para>
    /// A save is nothing more than a named folder holding the documents that belong to it, so
    /// creating one is a <c>mkdir</c> and listing them is an enumeration. There is no index file
    /// to fall out of step with what is actually on disk.
    /// </para>
    /// </summary>
    internal static class SavePaths
    {
        /// <summary>
        /// The longest name a save may have. Well inside what any filesystem takes; the limit is
        /// there so a name stays readable in a menu column.
        /// </summary>
        private const int MaxNameLength = 64;

        /// <summary>
        /// The names Windows reserves for devices.
        /// </summary>
        /// <remarks>
        /// None of them contains an invalid character, so without this table they pass
        /// <see cref="IsValidName"/> and then fail at <c>Directory.CreateDirectory</c> well after
        /// validation said yes - the player is told the name is fine and handed an IO error. Held
        /// on every platform rather than behind an OS check: a save folder is meant to be copied
        /// between machines, and one of these could never be restored on Windows.
        /// </remarks>
        private static readonly HashSet<string> ReservedNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
            };

        /// <summary>
        /// The saves directory under <see cref="PathProvider.Root"/>.
        /// </summary>
        public static string Root => PathProvider.Saves;

        /// <summary>Existing saves, most recently played first.</summary>
        /// <remarks>
        /// A folder with unreadable or missing metadata is still listed - it is a save that needs
        /// looking at, not one to hide from the menu.
        /// </remarks>
        public static IReadOnlyList<SaveEntry> List()
        {
            var root = Root;
            if (!Directory.Exists(root))
            {
                return [];
            }

            var saves = new List<SaveEntry>();

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(directory);
                if (name.Length == 0)
                {
                    continue;
                }

                SaveMetadata metadata;
                try
                {
                    metadata = new SaveStore(directory).ReadMetadata();
                }
                catch (SaveException)
                {
                    // The remark above is the rule, and this is the case it was written for: a
                    // save whose document cannot be read is one to show the player, not one to
                    // hide. Left unguarded, a single unparseable save.json takes every other save
                    // out of the menu with it. The defaults sort it to the bottom, which is where
                    // a save that needs looking at belongs.
                    metadata = new SaveMetadata();
                }

                // The folder name wins over whatever the document says its name is: the folder is
                // what the game opens, and a stale Name in save.json would offer a save that
                // cannot be reached.
                saves.Add(new SaveEntry(name, metadata.LastPlayed, metadata.Turn, Measure(directory)));
            }

            saves.Sort(static (left, right) => right.LastPlayed.CompareTo(left.LastPlayed));
            return saves;
        }

        /// <summary>
        /// What a save folder costs on disk. Zero when it cannot be measured - a size is a nicety,
        /// and a permission problem on one file is no reason to leave the save out of the menu.
        /// </summary>
        private static long Measure(string directory)
        {
            try
            {
                var total = 0L;

                foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                {
                    total += new FileInfo(file).Length;
                }

                return total;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return 0;
            }
        }

        /// <summary>
        /// The folder a save's name means, whether or not it is there. Unlike <see cref="Open"/>
        /// this creates nothing - it is for showing a save's files to the player, not for reading
        /// them.
        /// </summary>
        /// <exception cref="ArgumentException">The name could never have been a save.</exception>
        public static string Folder(string name) => Resolve(name, nameof(name));

        /// <summary>Whether a save folder of this name already exists.</summary>
        public static bool Exists(string name) =>
            IsValidName(name) && Directory.Exists(Path.Combine(Root, name.Trim()));

        /// <summary>Opens an existing save, creating and stamping it when it is new.</summary>
        public static SaveStore Open(string name)
        {
            var directory = Resolve(name, nameof(name));
            var isNew = !Directory.Exists(directory);

            Directory.CreateDirectory(directory);

            var store = new SaveStore(directory);

            if (isNew)
            {
                var now = DateTimeOffset.Now;
                store.WriteMetadata(new SaveMetadata
                {
                    SchemaVersion = SaveStore.CurrentSchemaVersion,
                    Name = name.Trim(),
                    Created = now,
                    LastPlayed = now,
                    Turn = 0,
                });
            }

            return store;
        }

        /// <summary>
        /// Deletes a save and everything in it. There is no undo and no recycle bin, so the caller
        /// is expected to have confirmed with the player first.
        /// </summary>
        /// <remarks>
        /// The path is rebuilt from <see cref="Root"/> and the validated name rather than taken
        /// from the caller - see <see cref="Resolve"/>. A recursive delete is the operation where
        /// that matters most: accepting a path from elsewhere could reach outside the saves folder
        /// entirely.
        /// </remarks>
        /// <returns>False when there was no such save; it was already gone.</returns>
        /// <exception cref="ArgumentException">The name could never have been a save.</exception>
        /// <exception cref="SaveException">The folder exists but could not be removed.</exception>
        public static bool Delete(string name)
        {
            var directory = Resolve(name, nameof(name));

            if (!Directory.Exists(directory))
            {
                return false;
            }

            try
            {
                Directory.Delete(directory, recursive: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveException($"Could not delete '{name}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Renames a save, folder and document together.
        /// <para>
        /// The folder is the save, so this is a <c>Directory.Move</c>; <c>save.json</c> is updated
        /// afterwards only so the document does not contradict the folder it sits in. A rename
        /// that changes nothing but the casing goes through a temporary name, because Windows
        /// treats the two as the same folder and would refuse the move outright.
        /// </para>
        /// </summary>
        /// <exception cref="ArgumentException">Either name could never have been a save.</exception>
        /// <exception cref="SaveException">
        /// There was no such save, the new name is taken, or the folder could not be moved.
        /// </exception>
        public static void Rename(string name, string newName)
        {
            var from = Resolve(name, nameof(name));
            var to = Resolve(newName, nameof(newName));

            if (string.Equals(from, to, StringComparison.Ordinal))
            {
                return;
            }

            if (!Directory.Exists(from))
            {
                throw new SaveException($"There is no save called '{name.Trim()}'.");
            }

            var isRecase = string.Equals(from, to, StringComparison.OrdinalIgnoreCase);

            if (!isRecase && Directory.Exists(to))
            {
                throw new SaveException($"There is already a save called '{newName.Trim()}'.");
            }

            try
            {
                if (isRecase)
                {
                    var staging = Free(to + ".renaming");
                    Directory.Move(from, staging);
                    Directory.Move(staging, to);
                }
                else
                {
                    Directory.Move(from, to);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveException($"Could not rename '{name.Trim()}': {ex.Message}", ex);
            }

            Stamp(to, metadata => metadata.Name = Path.GetFileName(to));
        }

        /// <summary>
        /// Copies a save under a free name, and reports the name it settled on.
        /// </summary>
        /// <remarks>
        /// The copy keeps the original's <see cref="SaveMetadata.LastPlayed"/> so it lands beside
        /// its source in the menu rather than at the top of it: duplicating a save is taking a
        /// backup, and it should not become the one <c>Continue</c> offers.
        /// </remarks>
        /// <exception cref="ArgumentException">The name could never have been a save.</exception>
        /// <exception cref="SaveException">There was no such save, or the copy failed.</exception>
        public static string Duplicate(string name)
        {
            var from = Resolve(name, nameof(name));

            if (!Directory.Exists(from))
            {
                throw new SaveException($"There is no save called '{name.Trim()}'.");
            }

            var copyName = FreeCopyName(Path.GetFileName(from));
            var to = Path.Combine(Root, copyName);

            try
            {
                Directory.CreateDirectory(to);

                foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                {
                    // A .tmp is half of a write that SaveStore has not finished moving into place.
                    // Copying one would hand the duplicate a document that was never valid.
                    if (Path.GetExtension(file) is ".tmp")
                    {
                        continue;
                    }

                    var destination = Path.Combine(to, Path.GetRelativePath(from, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.Copy(file, destination, overwrite: true);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveException($"Could not copy '{name.Trim()}': {ex.Message}", ex);
            }

            Stamp(to, metadata =>
            {
                metadata.Name = copyName;
                metadata.Created = DateTimeOffset.Now;
            });

            return copyName;
        }

        /// <summary>
        /// Resets a save back to just after the player defined their character and starting location,
        /// clearing all subsequent story and gameplay data.
        /// </summary>
        /// <exception cref="ArgumentException">The name could never have been a save.</exception>
        /// <exception cref="SaveException">The save does not exist or has no player character.</exception>
        public static void Reset(string name)
        {
            var directory = Resolve(name, nameof(name));

            if (!Directory.Exists(directory))
            {
                throw new SaveException($"There is no save called '{name.Trim()}'.");
            }

            var store = new SaveStore(directory);

            var characters = store.ReadCharacters();
            var player = SaveStore.Player(characters);
            if (player is null)
            {
                throw new SaveException($"'{name.Trim()}' has no player character to reset.");
            }

            // Identify the ClassTemplate from the player's description
            var template = ClassTemplates.All.FirstOrDefault(t =>
                player.Description.EndsWith(t.Aptitude, StringComparison.Ordinal));

            // Determine if the starting location was defined by the player or generated by narrator
            var locations = store.ReadLocations();
            var loc1 = locations.Locations.Find(l => string.Equals(l.Id, "loc_1", StringComparison.Ordinal))
                ?? locations.Locations.FirstOrDefault();

            var startLocationWasPlayerDefined = false;
            if (loc1 is not null)
            {
                var hadNarratorUpsertOnTurn1 = false;
                try
                {
                    foreach (var entry in store.Journal.Read().Entries)
                    {
                        if (entry.Turn == 1 && string.Equals(entry.Tool, "upsert_location", StringComparison.OrdinalIgnoreCase))
                        {
                            if (entry.Arguments.ValueKind == JsonValueKind.Object
                                && entry.Arguments.TryGetProperty("name", out var nameProp)
                                && SaveStore.Matches(nameProp.GetString(), loc1.Name))
                            {
                                hadNarratorUpsertOnTurn1 = true;
                                break;
                            }
                        }
                    }
                }
                catch (SaveException)
                {
                    // If journal is unreadable, treat as not upserted by narrator
                }

                startLocationWasPlayerDefined = !hadNarratorUpsertOnTurn1;
            }

            // 1. Reset player character
            if (template is not null)
            {
                player.MaxHealth = template.MaxHealth;
                player.Health = template.MaxHealth;
                player.Attributes.Clear();
                CharacterAttributes.Seed(player, template.Attributes);
            }
            else
            {
                player.Health = player.MaxHealth;
            }

            player.Secrets.Clear();

            characters.Characters.Clear();
            characters.Characters.Add(player);
            characters.NextId = 1;
            store.WriteCharacters(characters);

            // 2. Reset inventory and items
            var itemFile = new ItemFile();
            var inventory = new InventoryFile();
            var playerInv = inventory.GetOrCreate(player.Id);
            if (template is not null)
            {
                playerInv.Money = template.StartingMoney;
                foreach (var item in template.StartingItems)
                {
                    var itemDef = new ItemDefinition
                    {
                        Id = itemFile.TakeId(),
                        Name = item.Name,
                        Description = item.Description,
                    };
                    itemFile.Items.Add(itemDef);
                    playerInv.Items.Add(new ItemStack
                    {
                        ItemId = itemDef.Id,
                        Quantity = item.Quantity,
                    });
                }
            }
            store.WriteItems(itemFile);
            store.WriteInventory(inventory);

            // 3. Reset locations
            var resetLocations = new LocationFile();
            if (startLocationWasPlayerDefined && loc1 is not null)
            {
                var start = new Location
                {
                    Id = loc1.Id.Length > 0 ? loc1.Id : resetLocations.TakeId(),
                    Name = loc1.Name,
                    Description = string.Empty,
                    CharacterIds = [player.Id],
                };
                resetLocations.Locations.Add(start);
                resetLocations.NextId = 1;
            }
            store.WriteLocations(resetLocations);

            // 4. Delete logs, story, rolls, and temp files
            var logs = new[]
            {
                "journal.jsonl",
                "ledger.jsonl",
                "transcript.jsonl",
                "diagnostics.jsonl",
                "story.jsonl",
                "rolls.jsonl",
                "story.json",
                "rolls.json",
            };

            foreach (var logFile in logs)
            {
                var logPath = Path.Combine(directory, logFile);
                if (File.Exists(logPath))
                {
                    try
                    {
                        File.Delete(logPath);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // Best effort
                    }
                }
            }

            // Remove any temporary files (*.tmp) left behind
            try
            {
                foreach (var tmpFile in Directory.EnumerateFiles(directory, "*.tmp", SearchOption.TopDirectoryOnly))
                {
                    try { File.Delete(tmpFile); } catch { }
                }
            }
            catch { }

            // 6. Reset metadata
            var metadata = store.ReadMetadata();
            metadata.SchemaVersion = SaveStore.CurrentSchemaVersion;
            metadata.Name = Path.GetFileName(directory);
            metadata.Turn = 0;
            metadata.LastPlayed = DateTimeOffset.Now;
            store.WriteMetadata(metadata);
        }

        /// <summary>
        /// Whether a name can be a folder. Rejects path separators and reserved characters rather
        /// than silently rewriting them, so the name in the menu is always the name on disk.
        /// </summary>
        public static bool IsValidName(string? name)
        {
            if (name is null)
            {
                return false;
            }

            var trimmed = name.Trim();

            return trimmed.Length is > 0 and <= MaxNameLength
                && trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && trimmed is not ("." or "..")
                && !IsReserved(trimmed);
        }

        /// <summary>
        /// Whether a name is one of <see cref="ReservedNames"/>. The stem before the first dot is
        /// what Windows reserves, so <c>CON.txt</c> is refused along with <c>CON</c>.
        /// </summary>
        private static bool IsReserved(string trimmed)
        {
            var dot = trimmed.IndexOf('.');
            var stem = dot < 0 ? trimmed : trimmed[..dot];

            return ReservedNames.Contains(stem);
        }

        /// <summary>
        /// The folder a validated name means. The path is always rebuilt from <see cref="Root"/>
        /// rather than taken from a caller, so nothing here can be pointed outside the saves
        /// folder - the rule <see cref="Delete"/> already follows, kept in one place now that
        /// three operations need it.
        /// </summary>
        private static string Resolve(string name, string parameterName)
        {
            if (!IsValidName(name))
            {
                throw new ArgumentException($"'{name}' is not a usable save name.", parameterName);
            }

            return Path.Combine(Root, name.Trim());
        }

        /// <summary>
        /// The first free name of the form <c>X (copy)</c>, <c>X (copy 2)</c> and so on. The base
        /// is trimmed as the suffix grows so the result stays a name <see cref="IsValidName"/>
        /// would accept.
        /// </summary>
        private static string FreeCopyName(string name)
        {
            for (var attempt = 1; ; attempt++)
            {
                var suffix = attempt == 1 ? " (copy)" : $" (copy {attempt})";
                var stem = name.Length + suffix.Length <= MaxNameLength
                    ? name
                    : name[..(MaxNameLength - suffix.Length)].TrimEnd();

                var candidate = stem + suffix;

                if (!Directory.Exists(Path.Combine(Root, candidate)))
                {
                    return candidate;
                }
            }
        }

        /// <summary>A path like <paramref name="path"/> that nothing is using yet.</summary>
        private static string Free(string path)
        {
            var candidate = path;

            for (var attempt = 2; Directory.Exists(candidate) || File.Exists(candidate); attempt++)
            {
                candidate = $"{path}{attempt}";
            }

            return candidate;
        }

        /// <summary>
        /// Edits a save's metadata in place. Failures are swallowed on purpose: the folder move or
        /// copy has already happened and succeeded, and the folder name is what the game reads
        /// anyway - refusing the whole operation over a document that agrees with it would be
        /// worse than leaving the document stale.
        /// </summary>
        private static void Stamp(string directory, Action<SaveMetadata> edit)
        {
            try
            {
                var store = new SaveStore(directory);
                var metadata = store.ReadMetadata();
                edit(metadata);
                store.WriteMetadata(metadata);
            }
            catch (SaveException)
            {
                // The save is already where it should be; its save.json can be repaired by playing it.
            }
        }
    }
}
