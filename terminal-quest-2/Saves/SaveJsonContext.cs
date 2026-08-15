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
    [JsonSerializable(typeof(StoryFile))]
    [JsonSerializable(typeof(RollFile))]
    [JsonSerializable(typeof(SaveMetadata))]
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
