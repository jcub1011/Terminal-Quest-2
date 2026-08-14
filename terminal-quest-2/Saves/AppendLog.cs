using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// One newline-delimited JSON log inside a save folder, written by appending and never rewritten.
    /// <para>
    /// Separate from <see cref="SaveStore"/> rather than more methods on it, because the two follow
    /// <em>contradictory</em> file-sharing disciplines. A document write there ends in a rename and
    /// therefore lets other writers alone; an append here has to exclude them, because two appenders
    /// allocating a sequence number at once would issue the same one. Both in the same class would
    /// mean every future reader had to work out which rule a given method follows. The store exposes
    /// this through <see cref="SaveStore.Journal"/> and <see cref="SaveStore.Ledger"/>, so call sites
    /// still read as though it were one thing.
    /// </para>
    /// <para>
    /// There is no temporary file anywhere here, which is the point: the temp-file-plus-rename
    /// pattern that makes a document write atomic cannot append, and a read-modify-write of a
    /// growing log by two processes is precisely the lost update the sequence scan exists to
    /// prevent.
    /// </para>
    /// <para>
    /// Holds a path and a converter, never file content. The store's rule that "the file on disk is
    /// the only authority, and this process may not be the one that last changed it" is untouched.
    /// </para>
    /// </summary>
    internal sealed class AppendLog<TEntry>
        where TEntry : class, ILogEntry
    {
        /// <summary>
        /// How much of the end of the log is read to find the highest sequence. Roughly fifty lines.
        /// </summary>
        /// <remarks>
        /// A window rather than the whole file because this is the one unbounded read on the path of
        /// <em>every</em> tool call. A long campaign runs to thousands of lines, and rescanning all
        /// of them per append turns into latency the player can feel late in a game while buying
        /// nothing: appends are in order, so the highest sequence is at the end.
        /// <para>
        /// The hole this leaves is worth stating rather than hiding. A hand-edit that puts a higher
        /// sequence further back than this would let the next append reissue a number. Nothing here
        /// detects that; the batch consistency test does, by asserting the sequence climbs.
        /// </para>
        /// </remarks>
        private const int TailBytes = 8 * 1024;

        /// <summary>
        /// Tries at the lock before giving up, and the pause between them.
        /// </summary>
        /// <remarks>
        /// More attempts than <see cref="SaveStore"/>'s read retry, and for a different reason.
        /// Losing that race costs a redraw; losing this one loses a line of history for good. The
        /// holder's critical section is a small read and a small write, so ten is generous rather
        /// than hopeful.
        /// </remarks>
        private const int Attempts = 10;

        private const int RetryMilliseconds = 20;

        private readonly JsonTypeInfo<TEntry> _typeInfo;

        public AppendLog(string path, JsonTypeInfo<TEntry> typeInfo)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            ArgumentNullException.ThrowIfNull(typeInfo);

            Path = System.IO.Path.GetFullPath(path);
            _typeInfo = typeInfo;
        }

        /// <summary>The file this log is. It need not exist; an absent log is an empty one.</summary>
        public string Path { get; }

        /// <summary>The file name alone, for a message a person has to read.</summary>
        public string Name => System.IO.Path.GetFileName(Path);

        /// <summary>
        /// Appends one entry, stamping <see cref="ILogEntry.Seq"/> on the way, and reports the
        /// sequence it was given.
        /// </summary>
        /// <remarks>
        /// The sequence is allocated and the line is written under one file handle. That is the whole
        /// of the cross-process safety: the handle <em>is</em> the lock, so no second appender can
        /// read the same highest-sequence and write beside it.
        /// <para>
        /// A named mutex was the obvious alternative and is the wrong one - .NET named mutexes are
        /// not cross-process on every platform this could run on - and a lock file would be a second
        /// piece of state to fall out of step with the log it guards.
        /// </para>
        /// </remarks>
        /// <exception cref="SaveException">The line could not be written.</exception>
        public int Append(TEntry entry)
        {
            ArgumentNullException.ThrowIfNull(entry);

            var directory = System.IO.Path.GetDirectoryName(Path);

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (directory is { Length: > 0 })
                    {
                        System.IO.Directory.CreateDirectory(directory);
                    }

                    // FileAccess.ReadWrite because the sequence has to be read before it is
                    // allocated. FileShare.Read admits a reader using SaveStore's sharing
                    // convention - which asks only for read access - while refusing another
                    // appender, who would need ReadWrite and gets an IOException to retry on.
                    using var stream = new FileStream(
                        Path,
                        FileMode.OpenOrCreate,
                        FileAccess.ReadWrite,
                        FileShare.Read | FileShare.Delete);

                    entry.Seq = Highest(stream) + 1;

                    // Bytes rather than a StreamWriter: a writer would be free to emit an encoding
                    // preamble, and a preamble landing in the middle of a jsonl file corrupts
                    // exactly one line while being invisible in every editor.
                    var line = SaveStore.Utf8NoBom.GetBytes(JsonSerializer.Serialize(entry, _typeInfo));

                    stream.Seek(0, SeekOrigin.End);

                    if (EndsMidLine(stream))
                    {
                        // A process killed mid-append leaves a line with no newline after it.
                        // Closing it costs one byte and turns a torn line into a skipped one;
                        // without this, the new entry grafts onto the end of it and both are lost.
                        stream.WriteByte((byte)'\n');
                    }

                    stream.Write(line);

                    // A bare line feed, never the platform's newline: a log written by the game on
                    // one machine and read by the tool server on another should not differ by which
                    // wrote it.
                    stream.WriteByte((byte)'\n');
                    stream.Flush();

                    return entry.Seq;
                }
                catch (IOException) when (attempt < Attempts)
                {
                    // Another appender holds the handle. Blocking, for the reason SaveStore's read
                    // retry blocks: the caller is a tool call the model is already waiting on, and
                    // it has nothing else to be doing for twenty milliseconds.
                    Thread.Sleep(RetryMilliseconds);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // UnauthorizedAccessException is not retried, unlike in SaveStore. There it can
                    // mean a file caught halfway through being replaced; a log is never renamed
                    // over, so here it means the target genuinely refuses us.
                    throw new SaveException($"Could not append to {Name}: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// The highest sequence on record. Zero when the log is absent or holds nothing readable.
        /// </summary>
        /// <remarks>
        /// The version number in §8's sense: what a decision generated against this log can record,
        /// so that it can later be told whether anything has happened since.
        /// </remarks>
        /// <exception cref="SaveException">The log exists and could not be read.</exception>
        public int Head()
        {
            if (!File.Exists(Path))
            {
                return 0;
            }

            try
            {
                using var stream = Open();
                return Highest(stream);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveException($"Could not read {Name}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Every readable entry, oldest first, with a count of the lines that were not readable.
        /// An absent log reads as empty and is not created by asking.
        /// </summary>
        /// <exception cref="SaveException">The log exists and could not be read.</exception>
        public LogRead<TEntry> Read()
        {
            if (!File.Exists(Path))
            {
                return new LogRead<TEntry>([], 0);
            }

            string text;
            try
            {
                using var stream = Open();
                using var reader = new StreamReader(stream, SaveStore.Utf8NoBom);
                text = reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveException($"Could not read {Name}: {ex.Message}", ex);
            }

            var entries = new List<TEntry>();
            var malformed = 0;

            foreach (var range in text.AsSpan().Split('\n'))
            {
                var line = text.AsSpan(range).TrimEnd('\r');

                // A blank line is what a hand-edited file has in it, and what the end of a
                // well-formed log looks like. Not a fault, so not counted as one.
                if (line.IsWhiteSpace())
                {
                    continue;
                }

                try
                {
                    if (JsonSerializer.Deserialize(line, _typeInfo) is { } entry)
                    {
                        entries.Add(entry);
                    }
                    else
                    {
                        // A line holding a literal null. Readable JSON, unusable entry.
                        malformed++;
                    }
                }
                catch (JsonException)
                {
                    malformed++;
                }
            }

            return new LogRead<TEntry>(entries, malformed);
        }

        /// <summary>The entries stamped with one turn, oldest first.</summary>
        /// <remarks>
        /// Scanned backwards from the end and stopped as soon as the turn number drops below the one
        /// asked for, rather than read whole and filtered. That is sound because entries are appended in
        /// order and a turn only ever climbs, so one turn's entries are contiguous.
        /// <para>
        /// Worth the extra code because this is on the hot path: the gate asks it several times a turn,
        /// and filtering a full read would cost a deserialization per line of the whole campaign each
        /// time - work proportional to how long the save has been played rather than to how much has
        /// happened this turn. Reading the file is still sequential and whole; it is the parsing that is
        /// bounded, and the parsing is the expensive half.
        /// </para>
        /// </remarks>
        public IReadOnlyList<TEntry> ForTurn(int turn)
        {
            if (!File.Exists(Path))
            {
                return [];
            }

            string text;
            try
            {
                using var stream = Open();
                using var reader = new StreamReader(stream, SaveStore.Utf8NoBom);
                text = reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                throw new SaveException($"Could not read {Name}: {ex.Message}", ex);
            }

            var lines = text.Split('\n');
            var found = new List<TEntry>();

            for (var index = lines.Length - 1; index >= 0; index--)
            {
                var line = lines[index].AsSpan().TrimEnd('\r');

                if (line.IsWhiteSpace())
                {
                    continue;
                }

                TEntry? entry;
                try
                {
                    entry = JsonSerializer.Deserialize(line, _typeInfo);
                }
                catch (JsonException)
                {
                    // A torn line at the very end is routine, and one in the middle is somebody's
                    // hand-edit. Neither is a reason to stop looking, and neither can be dated.
                    continue;
                }

                if (entry is null)
                {
                    continue;
                }

                if (entry.Turn < turn)
                {
                    break;
                }

                if (entry.Turn == turn)
                {
                    found.Add(entry);
                }
            }

            found.Reverse();
            return found;
        }

        /// <summary>
        /// Opens the log for reading without stopping the other process appending to it.
        /// </summary>
        /// <remarks>
        /// The sharing convention is <see cref="SaveStore"/>'s, and for its reason: a reader that
        /// asks for more than it needs does not protect itself, it makes the <em>writer</em> fail,
        /// in another process, and the model is told its tool refused.
        /// </remarks>
        private FileStream Open() =>
            new(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

        /// <summary>
        /// Whether the log's last byte is something other than a newline - that is, whether the
        /// previous append did not finish. False for an empty file, which is not mid-line.
        /// </summary>
        /// <remarks>Leaves the stream positioned at the end, ready to be written to.</remarks>
        private static bool EndsMidLine(FileStream stream)
        {
            if (stream.Length == 0)
            {
                return false;
            }

            stream.Seek(-1, SeekOrigin.End);
            var last = stream.ReadByte();

            return last != '\n';
        }

        /// <summary>
        /// The highest sequence in the log, found from the end.
        /// </summary>
        /// <remarks>
        /// Reads <see cref="TailBytes"/> from the end and takes the greatest sequence over every
        /// line in it, rather than trusting the last line: the last line may be torn, and a
        /// hand-edit may have left the tail out of order. Falls back to the whole file when nothing
        /// in the window parses, which is what happens when a single entry is larger than the
        /// window - one long description is enough to do it.
        /// </remarks>
        private int Highest(FileStream stream)
        {
            if (stream.Length == 0)
            {
                return 0;
            }

            var window = (int)Math.Min(TailBytes, stream.Length);
            var whole = window == stream.Length;

            // The window almost certainly begins in the middle of a line - and if it begins in the
            // middle of a multi-byte character, decoding gives that fragment a replacement
            // character rather than failing. Either way the fragment is dropped, not parsed.
            var highest = HighestIn(Range(stream, stream.Length - window, window), skipFirst: !whole);

            if (highest > 0 || whole)
            {
                return highest;
            }

            stream.Seek(0, SeekOrigin.Begin);
            using var reader = new StreamReader(stream, SaveStore.Utf8NoBom, leaveOpen: true);

            return HighestIn(reader.ReadToEnd(), skipFirst: false);
        }

        /// <summary>The greatest sequence among the lines of one chunk of log.</summary>
        private int HighestIn(string text, bool skipFirst)
        {
            var highest = 0;
            var first = true;

            foreach (var range in text.AsSpan().Split('\n'))
            {
                var line = text.AsSpan(range).TrimEnd('\r');

                if (first)
                {
                    first = false;

                    if (skipFirst)
                    {
                        continue;
                    }
                }

                if (line.IsWhiteSpace())
                {
                    continue;
                }

                try
                {
                    // A line that will not parse is exactly the case this is tolerant of: a torn
                    // tail must not stop the sequence climbing past it.
                    if (JsonSerializer.Deserialize(line, _typeInfo) is { Seq: var seq } && seq > highest)
                    {
                        highest = seq;
                    }
                }
                catch (JsonException)
                {
                    // Skipped, and not counted here - Read reports the count.
                }
            }

            return highest;
        }

        /// <summary>Part of the log as text, decoded from wherever it happens to begin.</summary>
        private static string Range(FileStream stream, long offset, int count)
        {
            stream.Seek(offset, SeekOrigin.Begin);

            var buffer = new byte[count];
            var read = stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false);

            return Encoding.UTF8.GetString(buffer, 0, read);
        }
    }
}
