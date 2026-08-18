using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// Read and write access to the documents inside one save folder.
    /// </summary>
    internal sealed class SaveStore
    {
        private const string CharactersFileName = "characters.json";
        private const string LocationsFileName = "locations.json";
        private const string ItemsFileName = "items.json";
        private const string InventoryFileName = "inventory.json";
        private const string StoryFileName = "story.jsonl";
        private const string RollsFileName = "rolls.jsonl";
        private const string MetadataFileName = "save.json";
        private const string JournalFileName = "journal.jsonl";
        private const string LedgerFileName = "ledger.jsonl";
        private const string TranscriptFileName = "transcript.jsonl";
        private const string DiagnosticsFileName = "diagnostics.jsonl";

        private const string SystemPromptFileName = "system-prompt.txt";
        private const string DirectorPromptFileName = "director-prompt.txt";
        private const string DirectiveFileName = "directive.json";

        internal static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        public const int CurrentSchemaVersion = 2;

        public SaveStore(string directory)
        {
            ArgumentException.ThrowIfNullOrEmpty(directory);
            Directory = Path.GetFullPath(directory);
        }

        public string Directory { get; }

        public string Name => Path.GetFileName(Directory);

        public CharacterFile ReadCharacters() => Read(CharactersFileName, SaveJsonContext.Readable.CharacterFile);

        public void WriteCharacters(CharacterFile file) => Write(CharactersFileName, file, SaveJsonContext.Readable.CharacterFile);

        public LocationFile ReadLocations() => Read(LocationsFileName, SaveJsonContext.Readable.LocationFile);

        public void WriteLocations(LocationFile file) => Write(LocationsFileName, file, SaveJsonContext.Readable.LocationFile);

        public ItemFile ReadItems() => Read(ItemsFileName, SaveJsonContext.Readable.ItemFile);

        public void WriteItems(ItemFile file) => Write(ItemsFileName, file, SaveJsonContext.Readable.ItemFile);

        public InventoryFile ReadInventory() => Read(InventoryFileName, SaveJsonContext.Readable.InventoryFile);

        public void WriteInventory(InventoryFile file) => Write(InventoryFileName, file, SaveJsonContext.Readable.InventoryFile);

        public SaveMetadata ReadMetadata() => Read(MetadataFileName, SaveJsonContext.Readable.SaveMetadata);

        public void WriteMetadata(SaveMetadata metadata) => Write(MetadataFileName, metadata, SaveJsonContext.Readable.SaveMetadata);

        public DirectiveFile ReadDirective() => Read(DirectiveFileName, SaveJsonContext.Readable.DirectiveFile);

        public void WriteDirective(DirectiveFile directive) => Write(DirectiveFileName, directive, SaveJsonContext.Readable.DirectiveFile);

        public int CurrentTurn() => ReadMetadata().Turn;

        public string SystemPromptPath => Path.Combine(Directory, SystemPromptFileName);

        public string? ReadSystemPrompt() => ReadText(SystemPromptFileName);

        public void WriteSystemPrompt(string text) => WriteText(SystemPromptFileName, text);

        public string DirectorPromptPath => Path.Combine(Directory, DirectorPromptFileName);

        public string? ReadDirectorPrompt() => ReadText(DirectorPromptFileName);

        public void WriteDirectorPrompt(string text) => WriteText(DirectorPromptFileName, text);

        public AppendLog<StoryEvent> Story =>
            field ??= new(Path.Combine(Directory, StoryFileName), LogJsonContext.Readable.StoryEvent);

        public AppendLog<DiceRoll> Rolls =>
            field ??= new(Path.Combine(Directory, RollsFileName), LogJsonContext.Readable.DiceRoll);

        public AppendLog<JournalEntry> Journal =>
            field ??= new(Path.Combine(Directory, JournalFileName), LogJsonContext.Readable.JournalEntry);

        public AppendLog<LedgerEntry> Ledger =>
            field ??= new(Path.Combine(Directory, LedgerFileName), LogJsonContext.Readable.LedgerEntry);

        public AppendLog<TranscriptEntry> Transcript =>
            field ??= new(Path.Combine(Directory, TranscriptFileName), LogJsonContext.Readable.TranscriptEntry);

        public AppendLog<DiagnosticEntry> Diagnostics =>
            field ??= new(Path.Combine(Directory, DiagnosticsFileName), LogJsonContext.Readable.DiagnosticEntry);

        public void RequireSupportedSchema()
        {
            var version = ReadMetadata().SchemaVersion;

            if (version == CurrentSchemaVersion)
            {
                return;
            }

            if (version == 0 && ReadCharacters().Characters.Count == 0)
            {
                return;
            }

            throw new SaveException(version < CurrentSchemaVersion
                ? $"'{Name}' was saved before characters, places and items had stable identifiers, "
                + "and cannot be opened by this version. Older saves are not converted - start a new game."
                : $"'{Name}' was saved by a newer version of Terminal Quest. Update the game, or start a new game.");
        }

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

        public static Character? FindCharacter(CharacterFile file, string? name) =>
            name is { Length: > 0 }
                ? file.Characters.Find(character => Matches(character.Name, name))
                : null;

        public static Location? FindLocation(LocationFile file, string? name) =>
            name is { Length: > 0 }
                ? file.Locations.Find(location => Matches(location.Name, name))
                : null;

        public static Character? FindCharacterById(CharacterFile file, string? id) =>
            id is { Length: > 0 }
                ? file.Characters.Find(character => string.Equals(character.Id, id, StringComparison.Ordinal))
                : null;

        public static Location? FindLocationById(LocationFile file, string? id) =>
            id is { Length: > 0 }
                ? file.Locations.Find(location => string.Equals(location.Id, id, StringComparison.Ordinal))
                : null;

        public static ItemDefinition? FindItem(ItemFile file, string? name) =>
            name is { Length: > 0 }
                ? file.Items.Find(item => Matches(item.Name, name))
                : null;

        public static ItemDefinition? FindItemById(ItemFile file, string? id) =>
            id is { Length: > 0 }
                ? file.Items.Find(item => string.Equals(item.Id, id, StringComparison.Ordinal))
                : null;

        public static Character? Player(CharacterFile file) =>
            file.Characters.Find(static character => character.Kind == CharacterKind.Player);

        public static string? PlayerName(CharacterFile file) => Player(file)?.Name;

        public static Location? WhereIs(LocationFile file, string? characterId) =>
            characterId is { Length: > 0 }
                ? file.Locations.Find(location =>
                    location.CharacterIds.Contains(characterId, StringComparer.Ordinal))
                : null;

        public bool MoveCharacter(string characterId, string destinationId)
        {
            ArgumentException.ThrowIfNullOrEmpty(characterId);

            var file = ReadLocations();

            var destination = FindLocationById(file, destinationId);
            if (destination is null)
            {
                return false;
            }

            foreach (var location in file.Locations)
            {
                location.CharacterIds.RemoveAll(present =>
                    string.Equals(present, characterId, StringComparison.Ordinal));
            }

            destination.CharacterIds.Add(characterId);

            WriteLocations(file);
            return true;
        }

        public bool SetPlayer(string characterIdOrName)
        {
            ArgumentException.ThrowIfNullOrEmpty(characterIdOrName);

            var file = ReadCharacters();
            var target = FindCharacterById(file, characterIdOrName) ?? FindCharacter(file, characterIdOrName);
            if (target is null)
            {
                return false;
            }

            foreach (var character in file.Characters)
            {
                character.Kind = string.Equals(character.Id, target.Id, StringComparison.Ordinal)
                    ? CharacterKind.Player
                    : CharacterKind.Npc;
            }

            WriteCharacters(file);
            return true;
        }

        public static int NextId<T>(List<T> items, Func<T, int> id) =>
            items.Count == 0 ? 1 : items.Max(id) + 1;

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
                text = ReadShared(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveException($"Could not read {fileName}: {ex.Message}", ex);
            }

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

        private string? ReadText(string fileName)
        {
            var path = Path.Combine(Directory, fileName);

            if (!File.Exists(path))
            {
                return null;
            }

            try
            {
                return ReadShared(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveException($"Could not read {fileName}: {ex.Message}", ex);
            }
        }

        private void WriteText(string fileName, string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            var path = Path.Combine(Directory, fileName);
            var temporary = path + ".tmp";

            try
            {
                System.IO.Directory.CreateDirectory(Directory);

                File.WriteAllText(temporary, text, Utf8NoBom);
                File.Move(temporary, path, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                TryDelete(temporary);
                throw new SaveException($"Could not write {fileName}: {ex.Message}", ex);
            }
        }

        private static string ReadShared(string path)
        {
            const int attempts = 3;

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var stream = new FileStream(
                        path,
                        FileMode.Open,
                        FileAccess.Read,
                        FileShare.ReadWrite | FileShare.Delete);

                    using var reader = new StreamReader(stream, Utf8NoBom);
                    return reader.ReadToEnd();
                }
                catch (Exception ex) when (attempt < attempts && ex is IOException or UnauthorizedAccessException)
                {
                    Thread.Sleep(20);
                }
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
            }
        }
    }
}
