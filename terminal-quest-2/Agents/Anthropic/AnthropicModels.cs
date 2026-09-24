using System.Net;
using System.Text.Json;

namespace TerminalQuest.Agents.Anthropic
{
    /// <summary>
    /// Asks Anthropic's Models API what exists, so the Claude dropdown can offer ids newer
    /// than this build's curated list.
    /// </summary>
    /// <remarks>
    /// The Claude Code provider itself needs no key (it drives the local CLI), so this is
    /// strictly opportunistic: it runs only when a key is available (the TQ2_ANTHROPIC_API_KEY
    /// environment variable or the Anthropic slot's stored key) and every failure - missing
    /// key, unreachable API, refusal - falls back to the curated list. A failed refresh must
    /// never blank the dropdown.
    /// </remarks>
    internal static class AnthropicModels
    {
        /// <summary>The model ids the API lists, in the order it lists them.</summary>
        /// <exception cref="AgentException">The API could not be reached or refused the request.</exception>
        /// <param name="handler">
        /// Where the request goes. Null means a real socket, which is what the game always passes.
        /// A supplied handler stays the caller's to dispose, because it may outlive this call.
        /// </param>
        public static async Task<IReadOnlyList<string>> ListAsync(
            string apiKey,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            HttpMessageHandler? handler = null)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(apiKey);

            using var client = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null)
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            client.DefaultRequestHeaders.Add("x-api-key", apiKey);
            client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);

            string body;
            try
            {
                using var response = await client
                    .GetAsync("https://api.anthropic.com/v1/models", deadline.Token)
                    .ConfigureAwait(false);

                body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    throw new AgentException(
                        "Anthropic refused the request: the API key is missing or incorrect.",
                        body,
                        (int)response.StatusCode);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new AgentException(
                        "Anthropic refused the model list.", body, (int)response.StatusCode);
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new AgentException($"Anthropic did not answer within {timeout.TotalSeconds:0} seconds.");
            }
            catch (HttpRequestException ex)
            {
                throw new AgentException("Could not reach Anthropic. Is the network up?", ex.Message);
            }

            return Parse(body);
        }

        /// <summary>
        /// Pulls the ids out of the <c>data</c> array. An answer in an unexpected shape yields
        /// an empty list rather than an error: the caller reads "no list" as "keep the curated
        /// list" rather than as "no models".
        /// </summary>
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
                        models.Add(value);
                    }
                }

                return models;
            }
            catch (JsonException)
            {
                return [];
            }
        }
    }
}
