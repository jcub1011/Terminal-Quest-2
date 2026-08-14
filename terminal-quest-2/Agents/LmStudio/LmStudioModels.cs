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
        /// Asks how many tokens the served model can hold, or null where that cannot be established.
        /// </summary>
        /// <remarks>
        /// This is LM Studio's own endpoint, not the OpenAI-compatible one: <c>/v1/models</c> lists
        /// ids and nothing else, and the context length is only on <c>/api/v0/models</c>. Everything
        /// else that speaks this API - Ollama, llama.cpp, vLLM, Jan - answers 404 there, which is why
        /// every failure here is null rather than an exception. The context gauge is decoration; a
        /// server that will not say is not a server that is broken.
        /// </remarks>
        /// <param name="model">
        /// The id to ask about. Null means the caller did not name one, so whichever model the server
        /// has loaded is the one that will answer the turns.
        /// </param>
        public static async Task<int?> ContextLengthAsync(
            string baseUrl,
            string? apiKey,
            string? model,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            HttpMessageHandler? handler = null)
        {
            // The configured base url points at the OpenAI-compatible surface; the native one is a
            // sibling of it, so the /v1 has to come off rather than be appended to.
            var root = baseUrl.TrimEnd('/');
            if (root.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
            {
                root = root[..^"/v1".Length];
            }

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

            try
            {
                using var response = await client
                    .GetAsync($"{root}/api/v0/models", deadline.Token)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                var body = await response.Content.ReadAsStringAsync(deadline.Token).ConfigureAwait(false);

                return ParseContextLength(body, model);
            }
            catch (Exception ex)
                when (ex is HttpRequestException
                   || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // A server that is not there, refuses, or is too slow simply does not say. The player
                // leaving is the one exception: that cancellation is theirs and has to go back to the
                // caller, or a session abandoned mid-startup reports that it started.
                return null;
            }
        }

        /// <summary>
        /// Reads the context length for <paramref name="model"/> out of an <c>/api/v0/models</c> body.
        /// </summary>
        /// <remarks>
        /// Prefers <c>loaded_context_length</c> - what the model is actually serving, and present only
        /// while it is loaded - over <c>max_context_length</c>, which is the ceiling it could have been
        /// loaded at. Quoting the ceiling for a model loaded at a quarter of it would flatter the gauge
        /// by exactly the factor that matters.
        /// </remarks>
        internal static int? ParseContextLength(string body, string? model)
        {
            var wanted = model?.Trim();

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
                    if (entry.ValueKind != JsonValueKind.Object || !Matches(entry, wanted))
                    {
                        continue;
                    }

                    if (Length(entry, "loaded_context_length") is { } loaded)
                    {
                        return loaded;
                    }

                    if (Length(entry, "max_context_length") is { } max)
                    {
                        return max;
                    }
                }

                return null;
            }
            catch (JsonException)
            {
                return null;
            }

            static bool Matches(JsonElement entry, string? wanted) =>
                wanted is { Length: > 0 }
                    ? entry.TryGetProperty("id", out var id)
                      && id.ValueKind == JsonValueKind.String
                      && string.Equals(id.GetString(), wanted, StringComparison.Ordinal)

                    // No id was asked for, so the loaded model is the one that will answer. An
                    // embedding model sits in this list too and is never it, but it is never loaded
                    // for narration either, so "loaded" is enough to pick by.
                    : entry.TryGetProperty("state", out var state)
                      && state.ValueKind == JsonValueKind.String
                      && string.Equals(state.GetString(), "loaded", StringComparison.Ordinal);

            static int? Length(JsonElement entry, string name) =>
                entry.TryGetProperty(name, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var length)
                && length > 0
                    ? length
                    : null;
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
