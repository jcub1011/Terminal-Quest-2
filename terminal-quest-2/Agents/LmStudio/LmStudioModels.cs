using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TerminalQuest.Agents.LmStudio
{
    /// <summary>
    /// Asks an OpenAI-compatible server what it is serving.
    /// </summary>
    internal static class LmStudioModels
    {
        /// <summary>The model ids the server lists, in the order it lists them.</summary>
        /// <exception cref="AgentException">The server could not be reached or refused the request.</exception>
        public static async Task<IReadOnlyList<string>> ListAsync(
            string baseUrl,
            string? apiKey,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            HttpMessageHandler? handler = null)
        {
            var trimmedUrl = baseUrl.TrimEnd('/');
            string address;

            if (trimmedUrl.EndsWith("/models", StringComparison.OrdinalIgnoreCase))
            {
                address = trimmedUrl;
            }
            else if (trimmedUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
                     trimmedUrl.EndsWith("/openai", StringComparison.OrdinalIgnoreCase))
            {
                address = $"{trimmedUrl}/models";
            }
            else
            {
                // If baseUrl is a bare host without path (e.g. http://localhost:1234 or http://127.0.0.1:57073),
                // target the OpenAI compatibility endpoint /v1/models
                address = $"{trimmedUrl}/v1/models";
            }

            using var client = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            if (apiKey is { Length: > 0 } key)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
                client.DefaultRequestHeaders.TryAddWithoutValidation("x-goog-api-key", key);
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            string body;
            try
            {
                using var response = await client.GetAsync(address, deadline.Token).ConfigureAwait(false);
                body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    var msg = TryExtractErrorMessage(body);
                    var detail = msg ?? $"HTTP {(int)response.StatusCode} ({(response.StatusCode == HttpStatusCode.Unauthorized ? "Unauthorized - check API key" : response.ReasonPhrase)})";
                    throw new AgentException($"{baseUrl} returned error: {detail}", body, (int)response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AgentException($"{baseUrl} did not answer within {timeout.TotalSeconds:0} seconds.");
            }
            catch (HttpRequestException ex)
            {
                throw new AgentException($"Could not reach {baseUrl}. Is the server running?", ex.Message);
            }

            return Parse(body);
        }

        /// <summary>
        /// Asks how many tokens the served model can hold, or null where that cannot be established.
        /// </summary>
        public static async Task<int?> ContextLengthAsync(
            string baseUrl,
            string? modelId,
            string? apiKey,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            HttpMessageHandler? handler = null)
        {
            var trimmed = baseUrl.TrimEnd('/');
            var root = trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                ? trimmed[..^3]
                : (trimmed.EndsWith("/openai", StringComparison.OrdinalIgnoreCase) ? trimmed[..^7] : trimmed);
            var address = $"{root.TrimEnd('/')}/api/v0/models";

            using var client = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            if (apiKey is { Length: > 0 } key)
            {
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            string body;
            try
            {
                using var response = await client.GetAsync(address, deadline.Token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                return null;
            }

            return ParseContextLength(body, modelId);
        }

        internal static int? ParseContextLength(string body, string? modelId)
        {
            try
            {
                using var document = JsonDocument.Parse(body);

                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                {
                    return null;
                }

                foreach (var entry in data.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!entry.TryGetProperty("id", out var id)
                        || id.ValueKind != JsonValueKind.String
                        || id.GetString() is not { Length: > 0 } name)
                    {
                        continue;
                    }

                    var matches = string.IsNullOrEmpty(modelId)
                        ? entry.TryGetProperty("state", out var state) && string.Equals(state.GetString(), "loaded", StringComparison.OrdinalIgnoreCase)
                        : string.Equals(name, modelId, StringComparison.OrdinalIgnoreCase);

                    if (matches)
                    {
                        if (entry.TryGetProperty("loaded_context_length", out var loaded)
                            && loaded.ValueKind == JsonValueKind.Number
                            && loaded.TryGetInt32(out var loadedValue)
                            && loadedValue > 0)
                        {
                            return loadedValue;
                        }

                        if (entry.TryGetProperty("max_context_length", out var max)
                            && max.ValueKind == JsonValueKind.Number
                            && max.TryGetInt32(out var maxValue)
                            && maxValue > 0)
                        {
                            return maxValue;
                        }
                    }
                }

                return null;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        internal static IReadOnlyList<string> Parse(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);

                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("data", out var data)
                    || data.ValueKind != JsonValueKind.Array)
                {
                    return [];
                }

                var models = new List<string>(data.GetArrayLength());

                foreach (var entry in data.EnumerateArray())
                {
                    if (entry.ValueKind == JsonValueKind.Object
                        && entry.TryGetProperty("id", out var id)
                        && id.ValueKind == JsonValueKind.String
                        && id.GetString() is { Length: > 0 } value)
                    {
                        var normalized = value.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
                            ? value["models/".Length..]
                            : value;
                        models.Add(normalized);
                    }
                }

                return models;
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static string? TryExtractErrorMessage(string body)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (document.RootElement.TryGetProperty("error", out var error))
                {
                    if (error.ValueKind == JsonValueKind.Object && error.TryGetProperty("message", out var msg))
                    {
                        return msg.GetString();
                    }
                    if (error.ValueKind == JsonValueKind.String)
                    {
                        return error.GetString();
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
        }
    }
}
