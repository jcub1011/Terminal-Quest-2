using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// Source-generated serialization for the append-only logs.
    /// <para>
    /// Separate from <see cref="SaveJsonContext"/> over one setting that admits no compromise: that
    /// context writes indented, on purpose, because a save document is meant to be opened and
    /// hand-edited. These files are one entry per line, so an indented entry is not a formatting
    /// preference - it is a corrupt log. Both contexts hand out the same shape of type info, so a
    /// call site reaching for the wrong one would compile and then silently write something nothing
    /// can read back. Two classes is what makes that mistake unavailable.
    /// </para>
    /// <para>
    /// The ignore condition differs too, and less absolutely. A document is written once and read by a
    /// person editing it, so a field spelled out at its default costs nothing there; on every line of a
    /// log it is noise, and an absent field already means the default coming back in.
    /// </para>
    /// <para>
    /// Be clear about how far that gets, because it is less far than it looks: nothing in the save layer
    /// is nullable - "none" is an empty string everywhere - and <em>neither</em> ignore condition
    /// suppresses an empty string, since both compare against null. So this trims the default value
    /// types, which is why a successful call's line carries no outcome flag, and an unused string still
    /// appears as <c>""</c>. Making those properties nullable would trim them too, and is not worth the
    /// null checks it would push onto every reader for the twenty-odd bytes it saves.
    /// </para>
    /// </summary>
    [JsonSourceGenerationOptions(
        WriteIndented = false,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
        UseStringEnumConverter = true)]
    [JsonSerializable(typeof(JournalEntry))]
    [JsonSerializable(typeof(LedgerEntry))]
    internal sealed partial class LogJsonContext : JsonSerializerContext
    {
        /// <summary>
        /// The context the logs actually use, differing from <see cref="Default"/> only in that it
        /// does not escape characters for HTML safety.
        /// <para>
        /// For <see cref="SaveJsonContext.Readable"/>'s reason - a log full of <c>&amp;#x27;</c> is
        /// tedious to read and worse to search - and for one of its own: a description recorded here
        /// has to match the one in <c>characters.json</c> byte for byte, because comparing the two is
        /// how a description that was overwritten rather than extended gets caught.
        /// </para>
        /// <para>
        /// This stays safe for a line-oriented format. The relaxed encoder declines to escape
        /// HTML-sensitive and non-ASCII characters; it does not decline to escape control characters,
        /// because a raw newline inside a JSON string is not valid JSON in the first place. So a line
        /// can hold narrator prose with paragraph breaks in it and still be one line.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Built on first use rather than in a field initializer, for the reason
        /// <see cref="SaveJsonContext.Readable"/> is: <see cref="Default"/> is set up by generated
        /// code in this same class, and a field initializer here would be free to run before it -
        /// reading a null and failing at startup rather than at compile time.
        /// </remarks>
        public static LogJsonContext Readable => field ??= CreateReadable();

        private static LogJsonContext CreateReadable() =>
            new(new JsonSerializerOptions(Default.Options)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
    }
}
