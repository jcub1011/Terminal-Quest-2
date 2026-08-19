using System.Buffers;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using TerminalQuest.Agents.Claude;
using TerminalQuest.Agents.LmStudio;
using TerminalQuest.Saves;
using TerminalQuest.Settings;

namespace TerminalQuest.Agents
{
    /// <summary>
    /// Holds the three generated starting equipment categories for character selection.
    /// </summary>
    internal sealed record GeneratedItemSets(
        IReadOnlyList<Item> Weapons,
        IReadOnlyList<Item> Offhands,
        IReadOnlyList<Item> Specials);

    /// <summary>
    /// Generates starting inventory item options using the active LLM provider.
    /// </summary>
    internal static class LlmItemGenerator
    {
        private static readonly MediaTypeHeaderValue JsonMediaType = new("application/json");

        public static GeneratedItemSets GetDefaultItems() =>
            new(
                [
                    new() { Name = "iron broadsword", Quantity = 1, Description = "Well-balanced steel with a crossguard wrapped in cord." },
                    new() { Name = "curved hunting bow", Quantity = 1, Description = "Steamed ash strung with waxed linen, worn smooth at the grip." },
                    new() { Name = "notched war pick", Quantity = 1, Description = "Stout iron head on blackened oak, made to punch through heavy mail." },
                    new() { Name = "carved ashwood staff", Quantity = 1, Description = "Shod in brass at both ends and banded with worn protective runes." },
                    new() { Name = "hollow-ground daggers", Quantity = 2, Description = "A matched pair in oiled scabbards, light and keen." },
                ],
                [
                    new() { Name = "wooden roundshield", Quantity = 1, Description = "Planks of pine bound in brass, light enough for a skirmish." },
                    new() { Name = "warded horn lantern", Quantity = 1, Description = "Pierced iron casting patterned light through thin-scraped horn." },
                    new() { Name = "tome of forgotten rites", Quantity = 1, Description = "Bound in pigskin and closed with an iron clasp." },
                    new() { Name = "steel buckler", Quantity = 1, Description = "A small steel disc bearing a defaced heraldic sigil." },
                    new() { Name = "scrimshaw bone focus", Quantity = 1, Description = "Etched whalebone that hums faintly when grasped tight." },
                ],
                [
                    new() { Name = "traveler's charm", Quantity = 1, Description = "A carved river stone hung from a loop of braided leather." },
                    new() { Name = "thieves' velvet pouch", Quantity = 1, Description = "Muffled tools, wire picks, and a small pry-iron in dark velvet." },
                    new() { Name = "vial of quicksilver", Quantity = 1, Description = "Heavy and cold, sealed under green wax." },
                    new() { Name = "brass pocket astrolabe", Quantity = 1, Description = "Graduated rings that spin smoothly against a central pin." },
                    new() { Name = "grappling hook and cord", Quantity = 1, Description = "Three-pronged iron hook on thirty feet of braided silk line." },
                ]);

        public static async Task<GeneratedItemSets> GenerateAsync(
            AppSettings settings,
            string summary,
            string aptitude,
            CancellationToken cancellationToken = default,
            HttpMessageHandler? handler = null)
        {
            ArgumentNullException.ThrowIfNull(settings);

            var prompt = ItemGeneratorPromptFile.Compose(summary, aptitude);

            try
            {
                if (settings.Provider == AgentProvider.OpenAiApi)
                {
                    return await GenerateOpenAiAsync(settings, prompt, cancellationToken, handler).ConfigureAwait(false);
                }

                return await GenerateClaudeAsync(settings, prompt, cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                return GetDefaultItems();
            }
        }

        private static async Task<GeneratedItemSets> GenerateOpenAiAsync(
            AppSettings settings,
            string prompt,
            CancellationToken cancellationToken,
            HttpMessageHandler? handler)
        {
            var rawUrl = settings.LmStudioBaseUrl?.Trim();
            if (string.IsNullOrEmpty(rawUrl) || !AppSettings.IsAddress(rawUrl))
            {
                return GetDefaultItems();
            }

            var baseUrl = AppSettings.NormalizeBaseUrl(rawUrl);
            var model = settings.LmStudioModel?.Trim();
            var apiKey = settings.LmStudioApiKey?.Trim();

            using var client = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null)
            {
                Timeout = TimeSpan.FromSeconds(15),
            };

            if (apiKey is { Length: > 0 })
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                client.DefaultRequestHeaders.TryAddWithoutValidation("x-goog-api-key", apiKey);
            }

            var endpoint = $"{baseUrl.TrimEnd('/')}/chat/completions";
            var requestBody = BuildOpenAiRequestBody(model, prompt);

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = new ByteArrayContent(requestBody),
            };
            request.Content.Headers.ContentType = JsonMediaType;

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return GetDefaultItems();
            }

            var jsonText = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var extractedResponse = ExtractAssistantContentFromOpenAiResponse(jsonText);

            if (string.IsNullOrWhiteSpace(extractedResponse))
            {
                return GetDefaultItems();
            }

            return ParseItemsJson(extractedResponse);
        }

        private static async Task<GeneratedItemSets> GenerateClaudeAsync(
            AppSettings settings,
            string prompt,
            CancellationToken cancellationToken)
        {
            var options = new ClaudeSessionOptions
            {
                Model = settings.ClaudeModel?.Trim() is { Length: > 0 } m ? m : null,
                SystemPrompt = "You are a specialized equipment generator for a text RPG. Output ONLY valid JSON matching the requested schema.",
                TurnTimeout = TimeSpan.FromSeconds(20),
            };

            await using var session = new ClaudeSession(options);
            try
            {
                await session.StartAsync(cancellationToken).ConfigureAwait(false);
                var result = await session.SendAsync(prompt, cancellationToken).ConfigureAwait(false);
                if (result.IsError || string.IsNullOrWhiteSpace(result.Text))
                {
                    return GetDefaultItems();
                }

                return ParseItemsJson(result.Text);
            }
            catch
            {
                return GetDefaultItems();
            }
        }

        private static byte[] BuildOpenAiRequestBody(string? model, string prompt)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                if (!string.IsNullOrWhiteSpace(model))
                {
                    writer.WriteString("model", model);
                }
                writer.WriteBoolean("stream", false);
                writer.WriteNumber("temperature", 0.7);

                writer.WriteStartArray("messages");
                writer.WriteStartObject();
                writer.WriteString("role", "system");
                writer.WriteString("content", "You are an equipment generator. Return ONLY valid JSON with weapons, offhands, and specials arrays.");
                writer.WriteEndObject();

                writer.WriteStartObject();
                writer.WriteString("role", "user");
                writer.WriteString("content", prompt);
                writer.WriteEndObject();
                writer.WriteEndArray();

                writer.WriteEndObject();
            }

            return buffer.WrittenSpan.ToArray();
        }

        private static string? ExtractAssistantContentFromOpenAiResponse(string responseJson)
        {
            try
            {
                using var doc = JsonDocument.Parse(responseJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
                {
                    var first = choices[0];
                    if (first.TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content))
                    {
                        return content.GetString();
                    }
                }
            }
            catch
            {
            }

            return null;
        }

        public static GeneratedItemSets ParseItemsJson(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
            {
                return GetDefaultItems();
            }

            var cleanJson = CleanJsonText(rawJson);

            try
            {
                using var doc = JsonDocument.Parse(cleanJson);
                var root = doc.RootElement;

                var weapons = ReadItemList(root, "weapons");
                var offhands = ReadItemList(root, "offhands");
                var specials = ReadItemList(root, "specials");

                var defaults = GetDefaultItems();

                return new GeneratedItemSets(
                    weapons.Count > 0 ? weapons : defaults.Weapons,
                    offhands.Count > 0 ? offhands : defaults.Offhands,
                    specials.Count > 0 ? specials : defaults.Specials);
            }
            catch
            {
                return GetDefaultItems();
            }
        }

        private static List<Item> ReadItemList(JsonElement root, string propertyName)
        {
            var list = new List<Item>();
            if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(propertyName, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in array.EnumerateArray())
                {
                    if (element.ValueKind != JsonValueKind.Object) continue;

                    var name = element.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var desc = element.TryGetProperty("description", out var d) ? d.GetString() : null;

                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        list.Add(new Item
                        {
                            Name = name.Trim(),
                            Quantity = 1,
                            Description = desc?.Trim() ?? string.Empty,
                        });
                    }
                }
            }

            return list;
        }

        private static string CleanJsonText(string text)
        {
            var trimmed = text.Trim();

            // Strip markdown ```json ... ``` blocks
            if (trimmed.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewline = trimmed.IndexOf('\n');
                if (firstNewline >= 0)
                {
                    trimmed = trimmed[(firstNewline + 1)..];
                }
                if (trimmed.EndsWith("```", StringComparison.Ordinal))
                {
                    trimmed = trimmed[..^3].TrimEnd();
                }
            }

            var firstBrace = trimmed.IndexOf('{');
            var lastBrace = trimmed.LastIndexOf('}');
            if (firstBrace >= 0 && lastBrace > firstBrace)
            {
                return trimmed.Substring(firstBrace, lastBrace - firstBrace + 1);
            }

            return trimmed;
        }
    }
}
