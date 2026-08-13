using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// Source-generated serialization for every save document.
    /// <para>
    /// The project publishes with <c>PublishAot</c>, so a reflection-based
    /// <see cref="JsonSerializer"/> call would emit trim warnings and can fail outright once the
    /// unused metadata has been trimmed away. Generating the converters at compile time keeps the
    /// save layer trim-safe while still letting the models stay plain classes.
    /// </para>
    /// <para>
    /// The wire protocol in <c>TerminalQuest.Mcp</c> deliberately does not go through here: it
    /// builds its frames with <c>Utf8JsonWriter</c>, matching <c>ClaudeSession</c>. Stable
    /// documents get a context; ad-hoc protocol shapes get a writer.
    /// </para>
    /// </summary>
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UseStringEnumConverter = true)]
    [JsonSerializable(typeof(CharacterFile))]
    [JsonSerializable(typeof(LocationFile))]
    [JsonSerializable(typeof(InventoryFile))]
    [JsonSerializable(typeof(StoryFile))]
    [JsonSerializable(typeof(SaveMetadata))]
    internal sealed partial class SaveJsonContext : JsonSerializerContext
    {
        /// <summary>
        /// The context the store actually uses, differing from <see cref="Default"/> only in that
        /// it does not escape characters for HTML safety.
        /// <para>
        /// Saves are meant to be opened and read - that is much of the point of keeping the world
        /// in files. The default encoder turns every apostrophe into <c>'</c>, which makes a
        /// character's description tedious to read and worse to hand-edit. Nothing here is ever
        /// embedded in a web page, so the relaxed encoder costs nothing.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Built on first use rather than in a field initializer. <see cref="Default"/> is set up
        /// by generated code in this same class, and a field initializer here would be free to run
        /// before it - reading a null and failing at startup rather than at compile time.
        /// </remarks>
        public static SaveJsonContext Readable => field ??= CreateReadable();

        private static SaveJsonContext CreateReadable() =>
            new(new JsonSerializerOptions(Default.Options)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
    }
}
