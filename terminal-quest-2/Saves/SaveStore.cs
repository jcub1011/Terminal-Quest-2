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
        private const string RollsFileName = "rolls.json";
        private const string MetadataFileName = "save.json";
        private const string JournalFileName = "journal.jsonl";
        private const string LedgerFileName = "ledger.jsonl";
        private const string TranscriptFileName = "transcript.jsonl";
        private const string DiagnosticsFileName = "diagnostics.jsonl";

        /// <summary>
        /// Shared with <see cref="AppendLog{TEntry}"/> so that the documents and the logs cannot come
        /// to disagree about encoding. A preamble written into the middle of a line-oriented file
        /// corrupts exactly one line while being invisible in every editor.
        /// </summary>
        internal static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// The save shape this build writes and can read.
        /// <para>
        /// 1 was the original, where a character's name was their identity. 2 gave characters,
        /// locations and items opaque ids and turned rosters and memory subjects into references to
        /// them; the two are not interchangeable, and a version 1 save is not converted.
        /// </para>
        /// </summary>
        public const int CurrentSchemaVersion = 2;

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

        public RollFile ReadRolls() => Read(RollsFileName, SaveJsonContext.Readable.RollFile);

        public void WriteRolls(RollFile file) => Write(RollsFileName, file, SaveJsonContext.Readable.RollFile);

        public SaveMetadata ReadMetadata() => Read(MetadataFileName, SaveJsonContext.Readable.SaveMetadata);

        public void WriteMetadata(SaveMetadata metadata) => Write(MetadataFileName, metadata, SaveJsonContext.Readable.SaveMetadata);

        /// <summary>
        /// The turn number memories and events are stamped with. Read from <c>save.json</c>, which
        /// the TUI updates after each turn - the MCP server has no other way to know what turn it
        /// is, since it is a fresh process per tool call's parent session.
        /// </summary>
        public int CurrentTurn() => ReadMetadata().Turn;

        /// <summary>
        /// Every tool call, in order, numbered. The version counter the rest of the design stamps
        /// itself against, and the log a consistency check runs over.
        /// </summary>
        /// <remarks>
        /// Lazy, and not a cache: an <see cref="AppendLog{TEntry}"/> holds a path and a converter and
        /// never file content, so this does not weaken the rule that the file on disk is the only
        /// authority.
        /// </remarks>
        public AppendLog<JournalEntry> Journal =>
            field ??= new(Path.Combine(Directory, JournalFileName), LogJsonContext.Readable.JournalEntry);

        /// <summary>Every assertion made to the player, in order, and by whom.</summary>
        public AppendLog<LedgerEntry> Ledger =>
            field ??= new(Path.Combine(Directory, LedgerFileName), LogJsonContext.Readable.LedgerEntry);

        /// <summary>
        /// The conversation itself, word for word: what the player typed and what the narrator wrote
        /// back. What a resumed session is shown and what a cold narrator reads to find its voice
        /// again.
        /// </summary>
        /// <remarks>
        /// A third log rather than more columns on the ledger, because the two answer different
        /// questions and are read by different things. The ledger holds one sentence per assertion so
        /// a consistency check has something to join on; this holds whole paragraphs so a scene can be
        /// drawn again exactly as it was. Folding them together would give the checker prose to wade
        /// through and the replay a summary to work from, and serve neither.
        /// </remarks>
        public AppendLog<TranscriptEntry> Transcript =>
            field ??= new(Path.Combine(Directory, TranscriptFileName), LogJsonContext.Readable.TranscriptEntry);

        /// <summary>
        /// What went wrong that the player was not told about: a turn that narrated and forgot to
        /// record its claims, a log line that would not write.
        /// </summary>
        /// <remarks>
        /// Kept per save rather than in one file for the machine, because a finding is worth little
        /// on its own and a good deal beside the tool calls, claims and prose of the same turn. It
        /// travels with the save for the same reason, so a folder handed to somebody else arrives
        /// with the evidence in it.
        /// <para>
        /// Absent from a save where nothing has gone wrong. <see cref="AppendLog{TEntry}"/> creates
        /// no file until something is written, so the presence of this one is itself the signal.
        /// </para>
        /// </remarks>
        public AppendLog<DiagnosticEntry> Diagnostics =>
            field ??= new(Path.Combine(Directory, DiagnosticsFileName), LogJsonContext.Readable.DiagnosticEntry);

        /// <summary>
        /// Throws unless this save is one this build can play.
        /// <para>
        /// Called before anything reads the world and, crucially, before the narrator is started:
        /// a narrator pointed at a save this build misreads would see a half-empty world through
        /// <c>get_state</c> and cheerfully write a new one on top of it.
        /// </para>
        /// <para>
        /// A save with nobody in it is adopted rather than rejected, whatever its metadata says.
        /// There is no playthrough in it to lose, and an empty folder is what both the save menu
        /// and an older build leave behind.
        /// </para>
        /// </summary>
        /// <exception cref="SaveException">
        /// The save predates <see cref="CurrentSchemaVersion"/>, or postdates this build.
        /// </exception>
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

        /// <summary>Stamps the save as played, recording the turn reached.</summary>
        /// <remarks>
        /// Deliberately does not stamp <see cref="SaveMetadata.SchemaVersion"/>, though it repairs
        /// <see cref="SaveMetadata.Name"/> and <see cref="SaveMetadata.Created"/>. Filling the
        /// version in here would quietly promote a genuine old save to the current shape on the
        /// first turn - which is the exact thing <see cref="RequireSupportedSchema"/> exists to stop.
        /// </remarks>
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

        /// <summary>
        /// Finds a character by name, case-insensitively. Null when there is no such character.
        /// </summary>
        /// <remarks>
        /// The name half of the seam: what the narrator and the player say, turned into the record
        /// that holds the id everything else points at. Look up by <see cref="FindCharacterById"/>
        /// once you are past it.
        /// </remarks>
        public static Character? FindCharacter(CharacterFile file, string? name) =>
            name is { Length: > 0 }
                ? file.Characters.Find(character => Matches(character.Name, name))
                : null;

        /// <summary>Finds a location by name, case-insensitively.</summary>
        public static Location? FindLocation(LocationFile file, string? name) =>
            name is { Length: > 0 }
                ? file.Locations.Find(location => Matches(location.Name, name))
                : null;

        /// <summary>Finds a character by id. Null when nothing on record answers to it.</summary>
        public static Character? FindCharacterById(CharacterFile file, string? id) =>
            id is { Length: > 0 }
                ? file.Characters.Find(character => string.Equals(character.Id, id, StringComparison.Ordinal))
                : null;

        /// <summary>Finds a location by id.</summary>
        public static Location? FindLocationById(LocationFile file, string? id) =>
            id is { Length: > 0 }
                ? file.Locations.Find(location => string.Equals(location.Id, id, StringComparison.Ordinal))
                : null;

        /// <summary>The character marked <see cref="CharacterKind.Player"/>, if there is one.</summary>
        public static Character? Player(CharacterFile file) =>
            file.Characters.Find(static character => character.Kind == CharacterKind.Player);

        /// <summary>The name of the character marked <see cref="CharacterKind.Player"/>, if any.</summary>
        public static string? PlayerName(CharacterFile file) => Player(file)?.Name;

        /// <summary>
        /// The location holding a character, if any holds them.
        /// </summary>
        /// <remarks>
        /// Takes an id, not a name. Named <c>WhereIs</c> rather than the <c>LocationOf</c> it
        /// replaced on purpose: the parameter changed meaning without changing type, so the
        /// compiler could not have caught a call site that kept passing a name - it would simply
        /// have stopped matching anything. A new name forces every caller to be looked at.
        /// </remarks>
        public static Location? WhereIs(LocationFile file, string? characterId) =>
            characterId is { Length: > 0 }
                ? file.Locations.Find(location =>
                    location.CharacterIds.Contains(characterId, StringComparer.Ordinal))
                : null;

        /// <summary>
        /// Moves a character to a location, clearing them out of wherever they were.
        /// <para>
        /// The only supported way to change presence, and the reason it lives on the store rather
        /// than on <see cref="Location"/>: it spans two records, and splitting it into an add and
        /// a remove would eventually leave someone standing in two places at once.
        /// </para>
        /// </summary>
        /// <param name="characterId">Who is moving. An id - see <see cref="WhereIs"/> on why.</param>
        /// <param name="destinationId">Where they are going.</param>
        /// <returns>False when the destination does not exist; the file is left untouched.</returns>
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
                text = ReadShared(path);
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

        /// <summary>
        /// Reads a document in a way that does not stop the other process replacing it.
        /// </summary>
        /// <remarks>
        /// <see cref="File.ReadAllText(string)"/> would be the obvious call and is the wrong one.
        /// It opens with <c>FileShare.Read</c>, which permits other readers and forbids everything
        /// else - including the delete that Windows performs as the first half of replacing a file.
        /// <see cref="Write{T}"/> ends with exactly that replacement, so a reader holding such a
        /// handle does not protect itself: it makes the <em>narrator's write fail</em>, in the other
        /// process, and the model is told its tool refused.
        /// <para>
        /// That was a race measured in microseconds while the only reader was the once-a-turn status
        /// refresh. The transcript now watches for rolls several times a second for the whole of a
        /// turn, which is what turns a theoretical race into one that happens.
        /// </para>
        /// <para>
        /// Sharing the file is most of the fix; the retry covers the rest. There is still an instant
        /// after the destination has been deleted and before the replacement is in place where there
        /// is no file to open at all, and losing that race should cost a few milliseconds rather than
        /// a turn.
        /// </para>
        /// </remarks>
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
                    // Deliberately blocking. Every caller of this is either a tool call the model is
                    // already waiting on or a redraw, and neither has anything useful to do in the
                    // twenty milliseconds it takes the other process to finish its rename.
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
