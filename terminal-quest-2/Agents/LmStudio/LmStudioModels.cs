using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace TerminalQuest.Agents.LmStudio
{
    /// <summary>
    /// Asks an OpenAI-compatible server what it is serving.
    /// </summary>
    /// <remarks>
    /// Used twice, for the same reason both times: the two things most likely to be wrong about a
    /// local model are that the server is not running and that the model name is a guess, and one
    /// round trip settles both. The settings screen calls it so the player can pick from a list
    /// instead of typing an id; <see cref="LmStudioSession.StartAsync"/> calls it so a mistake is
    /// reported before a turn is spent on it.
    /// </remarks>
    internal static class LmStudioModels
    {
        /// <summary>The model ids the server lists, in the order it lists them.</summary>
        /// <exception cref="AgentException">The server could not be reached or refused the request.</exception>
        /// <param name="handler">
        /// Where the request goes. Null means a real socket, which is what the game always passes.
        /// A supplied handler stays the caller's to dispose, because it may outlive this call.
        /// </param>
        public static async Task<IReadOnlyList<string>> ListAsync(
            string baseUrl,
            string? apiKey,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            HttpMessageHandler? handler = null)
        {
            var address = $"{baseUrl.TrimEnd('/')}/models";

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

                body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.Unauthorized)
                {
                    // Worth its own sentence: the server is up and correctly addressed, so every
                    // other reading of a refusal sends the player looking in the wrong place.
                    throw new AgentException(
                        $"{baseUrl} wants an API key. Copy the token from LM Studio's developer "
                      + "settings into the API key field.",
                        body,
                        (int)response.StatusCode);
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw new AgentException(
                        $"{baseUrl} refused the model list.", body, (int)response.StatusCode);
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
        /// Pulls the ids out of the <c>data</c> array.
        /// </summary>
        /// <remarks>
        /// An answer in an unexpected shape yields an empty list rather than an error. The server
        /// answered, which is the harder half of what the caller wanted to know, and both callers
        /// treat "no list" as "cannot say" rather than as "no models".
        /// <para>
        /// That includes an answer whose root is not an object at all - a bare array, a number, a
        /// literal <c>null</c>. Those parse cleanly, so the <c>catch</c> below never sees them, and
        /// <c>TryGetProperty</c> throws <see cref="InvalidOperationException"/> on anything but an
        /// object. Left unchecked it escapes <see cref="ListAsync"/> and
        /// <see cref="LmStudioSession.StartAsync"/> as something no caller is prepared for, unlike
        /// the <see cref="AgentException"/> every other failure here arrives as.
        /// </para>
        /// </remarks>
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
