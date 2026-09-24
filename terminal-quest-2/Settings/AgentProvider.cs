using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerminalQuest.Settings
{
    /// <summary>Who answers as the narrator.</summary>
    [JsonConverter(typeof(AgentProviderJsonConverter))]
    internal enum AgentProvider
    {
        /// <summary>The <c>claude</c> CLI, driven as a child process.</summary>
        ClaudeCode = 0,

        /// <summary>
        /// Legacy aggregate for every OpenAI-compatible HTTP endpoint. Never offered and never
        /// written; a file that still says this is migrated on load to the provider named by its
        /// old preset field. Kept so the number <c>1</c> and the old names keep meaning something.
        /// </summary>
        [Obsolete("Read-only migration marker; pick a specific provider instead.")]
        OpenAiApi = 1,

        /// <summary>Google AI Studio (Gemini) over its OpenAI-compatible endpoint.</summary>
        Google = 2,

        /// <summary>OpenAI over its API.</summary>
        OpenAI = 3,

        /// <summary>Anthropic over its OpenAI-compatible API. Distinct from <see cref="ClaudeCode"/>, which drives the local CLI.</summary>
        Anthropic = 4,

        /// <summary>A manually configured OpenAI-compatible endpoint (LM Studio, Ollama, vLLM, Jan, etc.).</summary>
        Custom = 5,

        /// <summary>Legacy alias for <see cref="OpenAiApi"/>.</summary>
        [Obsolete("Read-only migration marker; pick a specific provider instead.")]
        LmStudio = OpenAiApi,
    }

    /// <summary>
    /// Resilient JSON converter for <see cref="AgentProvider"/> that supports numbers, legacy aliases,
    /// case-insensitive names, and safely falls back to default instead of throwing.
    /// </summary>
    internal sealed class AgentProviderJsonConverter : JsonConverter<AgentProvider>
    {
        public override AgentProvider Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            {
                return number switch
                {
                    0 => AgentProvider.ClaudeCode,
                    // The legacy aggregate marker: the preset field beside it decides the real
                    // provider during migration. Without one it behaves as Custom.
#pragma warning disable CS0612, CS0618 // Intentional: legacy file support.
                    1 => AgentProvider.OpenAiApi,
#pragma warning restore CS0612, CS0618
                    2 => AgentProvider.Google,
                    3 => AgentProvider.OpenAI,
                    4 => AgentProvider.Anthropic,
                    5 => AgentProvider.Custom,
                    _ => AgentProvider.ClaudeCode,
                };
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return AgentProvider.ClaudeCode;
                }

                var clean = text.Replace("-", "").Replace("_", "").Trim();
                if (string.Equals(clean, "claudecode", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clean, "claude", StringComparison.OrdinalIgnoreCase))
                {
                    return AgentProvider.ClaudeCode;
                }

                if (string.Equals(clean, "google", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clean, "gemini", StringComparison.OrdinalIgnoreCase))
                {
                    return AgentProvider.Google;
                }

                if (string.Equals(clean, "openai", StringComparison.OrdinalIgnoreCase))
                {
                    return AgentProvider.OpenAI;
                }

                if (string.Equals(clean, "anthropic", StringComparison.OrdinalIgnoreCase))
                {
                    return AgentProvider.Anthropic;
                }

                if (string.Equals(clean, "custom", StringComparison.OrdinalIgnoreCase))
                {
                    return AgentProvider.Custom;
                }

#pragma warning disable CS0612, CS0618 // Intentional: legacy file support.
                if (string.Equals(clean, "openaiapi", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clean, "lmstudio", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clean, "api", StringComparison.OrdinalIgnoreCase))
                {
                    return AgentProvider.OpenAiApi;
                }
#pragma warning restore CS0612, CS0618
            }

            return AgentProvider.ClaudeCode;
        }

        public override void Write(Utf8JsonWriter writer, AgentProvider value, JsonSerializerOptions options)
        {
            // The legacy marker is never written on purpose: Normalize runs on every load, so by
            // write time it has already become a real provider. Anything unrecognised (including
            // the marker, should it slip through) writes as Custom, which is always constructible.
            writer.WriteStringValue(value switch
            {
                AgentProvider.ClaudeCode => "ClaudeCode",
                AgentProvider.Google => "Google",
                AgentProvider.OpenAI => "OpenAI",
                AgentProvider.Anthropic => "Anthropic",
                _ => "Custom",
            });
        }
    }
}
