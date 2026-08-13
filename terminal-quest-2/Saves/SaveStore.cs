using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// Read and write access to the documents inside one save folder.
    /// <para>
    /// Two processes use this at once. The TUI reads to refresh the status pane; the MCP server,
    /// launched as a child of the <c>claude</c> CLI, reads and writes on the narrator's behalf.
    /// That is why every write goes to a temporary file and is then moved into place: a reader
    /// must never observe a half-written document. It is also why nothing is cached - the file on
    /// disk is the only authority, and this process may not be the one that last changed it.
    /// </para>
    /// <para>
    /// A missing document reads as an empty one, so a freshly created save needs no seeding. A
    /// document that exists but cannot be parsed throws <see cref="SaveException"/>: that is a
    /// real problem and silently replacing it with an empty file would destroy a playthrough.
    /// </para>
    /// </summary>
    internal sealed class SaveStore
    {
        private const string CharactersFileName = "characters.json";
        private const string LocationsFileName = "locations.json";
        private const string InventoryFileName = "inventory.json";
        private const string StoryFileName = "story.json";
        private const string MetadataFileName = "save.json";

        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        public SaveStore(string directory)
        {
            ArgumentException.ThrowIfNullOrEmpty(directory);
            Directory = Path.GetFullPath(directory);
        }

        /// <summary>The save folder. Every document lives directly inside it.</summary>
        public string Directory { get; }

        /// <summary>The folder name, which is also the save's name.</summary>
        public string Name => Path.GetFileName(Directory);

        public CharacterFile ReadCharacters() => Read(CharactersFileName, SaveJsonContext.Readable.CharacterFile);

        public void WriteCharacters(CharacterFile file) => Write(CharactersFileName, file, SaveJsonContext.Readable.CharacterFile);

        public LocationFile ReadLocations() => Read(LocationsFileName, SaveJsonContext.Readable.LocationFile);

        public void WriteLocations(LocationFile file) => Write(LocationsFileName, file, SaveJsonContext.Readable.LocationFile);

        public InventoryFile ReadInventory() => Read(InventoryFileName, SaveJsonContext.Readable.InventoryFile);

        public void WriteInventory(InventoryFile file) => Write(InventoryFileName, file, SaveJsonContext.Readable.InventoryFile);

        public StoryFile ReadStory() => Read(StoryFileName, SaveJsonContext.Readable.StoryFile);

        public void WriteStory(StoryFile file) => Write(StoryFileName, file, SaveJsonContext.Readable.StoryFile);

        public SaveMetadata ReadMetadata() => Read(MetadataFileName, SaveJsonContext.Readable.SaveMetadata);

        public void WriteMetadata(SaveMetadata metadata) => Write(MetadataFileName, metadata, SaveJsonContext.Readable.SaveMetadata);

        /// <summary>
        /// The turn number memories and events are stamped with. Read from <c>save.json</c>, which
        /// the TUI updates after each turn - the MCP server has no other way to know what turn it
        /// is, since it is a fresh process per tool call's parent session.
        /// </summary>
        public int CurrentTurn() => ReadMetadata().Turn;

        /// <summary>Stamps the save as played, recording the turn reached.</summary>
        public void Touch(int turn)
        {
            var metadata = ReadMetadata();

            if (metadata.Name.Length == 0)
            {
                metadata.Name = Name;
            }

            if (metadata.Created == default)
            {
                metadata.Created = DateTimeOffset.Now;
            }

            metadata.LastPlayed = DateTimeOffset.Now;
            metadata.Turn = turn;

            WriteMetadata(metadata);
        }

        /// <summary>Finds a character by name, case-insensitively. Null when there is no such character.</summary>
        public static Character? FindCharacter(CharacterFile file, string? name) =>
            name is { Length: > 0 }
                ? file.Characters.Find(character => Matches(character.Name, name))
                : null;

        /// <summary>Finds a location by name, case-insensitively.</summary>
        public static Location? FindLocation(LocationFile file, string? name) =>
            name is { Length: > 0 }
                ? file.Locations.Find(location => Matches(location.Name, name))
                : null;

        /// <summary>The name of the character marked <see cref="CharacterKind.Player"/>, if any.</summary>
        public static string? PlayerName(CharacterFile file) =>
            file.Characters.Find(static character => character.Kind == CharacterKind.Player)?.Name;

        /// <summary>The location holding the named character, if any holds them.</summary>
        public static Location? LocationOf(LocationFile file, string? characterName) =>
            characterName is { Length: > 0 }
                ? file.Locations.Find(location =>
                    location.Characters.Exists(present => Matches(present, characterName)))
                : null;

        /// <summary>
        /// Moves a character to a location, clearing them out of wherever they were.
        /// <para>
        /// The only supported way to change presence, and the reason it lives on the store rather
        /// than on <see cref="Location"/>: it spans two records, and splitting it into an add and
        /// a remove would eventually leave someone standing in two places at once.
        /// </para>
        /// </summary>
        /// <returns>False when the destination does not exist; the file is left untouched.</returns>
        public bool MoveCharacter(string characterName, string locationName)
        {
            var file = ReadLocations();

            var destination = FindLocation(file, locationName);
            if (destination is null)
            {
                return false;
            }

            foreach (var location in file.Locations)
            {
                location.Characters.RemoveAll(present => Matches(present, characterName));
            }

            // The spelling in characters.json wins over whatever the caller typed, so the roster
            // does not accumulate three casings of the same name.
            var canonical = FindCharacter(ReadCharacters(), characterName)?.Name ?? characterName.Trim();
            destination.Characters.Add(canonical);

            WriteLocations(file);
            return true;
        }

        /// <summary>The next identifier for a list that stamps its entries with one.</summary>
        public static int NextId<T>(List<T> items, Func<T, int> id) =>
            items.Count == 0 ? 1 : items.Max(id) + 1;

        /// <summary>Case-insensitive name comparison, the rule everywhere a name is looked up.</summary>
        public static bool Matches(string? left, string? right) =>
            string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

        private T Read<T>(string fileName, JsonTypeInfo<T> typeInfo)
            where T : new()
        {
            var path = Path.Combine(Directory, fileName);

            if (!File.Exists(path))
            {
                return new T();
            }

            string text;
            try
            {
                text = File.ReadAllText(path, Utf8NoBom);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveException($"Could not read {fileName}: {ex.Message}", ex);
            }

            // An empty file is what a crash mid-write used to leave behind; treat it as absent
            // rather than as corruption the player has to repair by hand.
            if (text.AsSpan().IsWhiteSpace())
            {
                return new T();
            }

            try
            {
                return JsonSerializer.Deserialize(text, typeInfo) ?? new T();
            }
            catch (JsonException ex)
            {
                throw new SaveException($"{fileName} is not valid JSON: {ex.Message}", ex);
            }
        }

        private void Write<T>(string fileName, T value, JsonTypeInfo<T> typeInfo)
        {
            var path = Path.Combine(Directory, fileName);
            var temporary = path + ".tmp";

            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                File.WriteAllText(temporary, JsonSerializer.Serialize(value, typeInfo), Utf8NoBom);

                // Move rather than write in place: the other process may be reading this file
                // right now, and a rename is the closest thing to an atomic swap available here.
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                TryDelete(temporary);
                throw new SaveException($"Could not write {fileName}: {ex.Message}", ex);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
                // Best effort: the write already failed and is being reported.
            }
        }
    }
}
