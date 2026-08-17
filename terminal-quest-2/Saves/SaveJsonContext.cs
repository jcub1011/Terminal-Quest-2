using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// Source-generated serialization for every save document.
    /// </summary>
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        UseStringEnumConverter = true)]
    [JsonSerializable(typeof(CharacterFile))]
    [JsonSerializable(typeof(LocationFile))]
    [JsonSerializable(typeof(ItemFile))]
    [JsonSerializable(typeof(InventoryFile))]
    [JsonSerializable(typeof(SaveMetadata))]
    [JsonSerializable(typeof(DirectiveFile))]
    internal sealed partial class SaveJsonContext : JsonSerializerContext
    {
        public static SaveJsonContext Readable => field ??= CreateReadable();

        private static SaveJsonContext CreateReadable() =>
            new(new JsonSerializerOptions(Default.Options)
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
    }
}
