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

        /// <summary>A model served over an OpenAI-compatible HTTP API (Google, OpenAI, Anthropic, LM Studio, etc.).</summary>
        OpenAiApi = 1,

        /// <summary>Legacy alias for <see cref="OpenAiApi"/>.</summary>
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
                return number == 1 ? AgentProvider.OpenAiApi : AgentProvider.ClaudeCode;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return AgentProvider.ClaudeCode;
                }

                var clean = text.Replace("-", "").Replace("_", "").Trim();
                if (clean.Contains("openai", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("lmstudio", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("google", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("gemini", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(clean, "api", StringComparison.OrdinalIgnoreCase))
                {
                    return AgentProvider.OpenAiApi;
                }

                if (clean.Contains("claude", StringComparison.OrdinalIgnoreCase) ||
                    clean.Contains("anthropic", StringComparison.OrdinalIgnoreCase))
                {
                    return AgentProvider.ClaudeCode;
                }
            }

            return AgentProvider.ClaudeCode;
        }

        public override void Write(Utf8JsonWriter writer, AgentProvider value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value == AgentProvider.OpenAiApi ? "OpenAiApi" : "ClaudeCode");
        }
    }
}
