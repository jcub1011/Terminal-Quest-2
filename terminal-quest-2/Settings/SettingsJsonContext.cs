using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerminalQuest.Settings
{
    /// <summary>
    /// Source-generated serialization for <see cref="AppSettings"/>.
    /// <para>
    /// Separate from <c>SaveJsonContext</c>, which is scoped to the documents that make up a save.
    /// The reason for generating either is the same: the project publishes with <c>PublishAot</c>,
    /// where a reflection-based <see cref="JsonSerializer"/> call emits trim warnings and can fail
    /// outright once the metadata it needed has been trimmed away.
    /// </para>
    /// </summary>
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        UseStringEnumConverter = false)]
    [JsonSerializable(typeof(AppSettings))]
    [JsonSerializable(typeof(AgentProvider))]
    internal sealed partial class SettingsJsonContext : JsonSerializerContext;
}
