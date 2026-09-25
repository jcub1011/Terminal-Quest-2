using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;

using TerminalQuest.Settings;

namespace TerminalQuest.Agents.Pricing
{
    /// <summary>
    /// What one model costs and how much it can hold, as published by models.dev.
    /// </summary>
    /// <param name="InputPerM">USD per million uncached prompt tokens. Null where the catalog does not say.</param>
    /// <param name="OutputPerM">USD per million generated tokens. Null where the catalog does not say.</param>
    /// <param name="CacheReadPerM">
    /// USD per million prompt tokens served from the provider's cache. Null means the provider has no
    /// discount, and cached tokens are charged at <paramref name="InputPerM"/>.
    /// </param>
    /// <param name="ContextLimit">The model's context window in tokens, or zero where it is not listed.</param>
    internal sealed record ModelRates(double? InputPerM, double? OutputPerM, double? CacheReadPerM, int ContextLimit)
    {
        /// <summary>True when both the prompt and the answer have a price.</summary>
        public bool IsPriced => InputPerM is not null && OutputPerM is not null;

        /// <summary>The USD cost of one request, or null when <see cref="IsPriced"/> is false.</summary>
        public double? Cost(int uncachedTokens, int cachedTokens, int outputTokens)
        {
            if (InputPerM is not { } input || OutputPerM is not { } output)
            {
                return null;
            }

            return (uncachedTokens * input + cachedTokens * (CacheReadPerM ?? input) + outputTokens * output) / 1_000_000d;
        }
    }

    /// <summary>
    /// Per-model prices and context windows for the OpenAI-compatible providers, read from
    /// <see href="https://models.dev">models.dev</see> - the open-source catalog OpenCode prices its
    /// sessions with.
    /// </summary>
    /// <remarks>
    /// The OpenAI-compatible API reports tokens but never money, and a table compiled into the game
    /// would be out of date within weeks: model ids churn faster than releases do, which is the same
    /// reason <see cref="ClaudeModels"/> ships no curated list. The catalog is fetched at most once a
    /// day and kept on disk, so a session started offline still prices from yesterday's copy.
    /// <para>
    /// Tiered prices (Gemini Pro above 200k tokens, for one) are ignored and the base rate used. A
    /// narrator's context sits well under any tier boundary for most of a campaign, and a figure that
    /// is slightly low late in a long one is a smaller fault than the complexity of tracking tiers.
    /// </para>
    /// </remarks>
    internal sealed partial class ModelCatalog
    {
        /// <summary>Where the catalog is published.</summary>
        public const string SourceUrl = "https://models.dev/api.json";

        /// <summary>How old the cached copy may be before a fresh one is fetched.</summary>
        public static readonly TimeSpan MaxAge = TimeSpan.FromHours(24);

        /// <summary>How long a fetch may take. Past this the stale copy, or nothing, is used.</summary>
        private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(5);

        private static readonly Lock SharedGate = new();
        private static Task<ModelCatalog>? _shared;

        /// <summary>The catalog with nothing in it: every lookup misses.</summary>
        public static readonly ModelCatalog Empty = new(new Dictionary<string, Dictionary<string, ModelRates>>(StringComparer.OrdinalIgnoreCase));

        private readonly Dictionary<string, Dictionary<string, ModelRates>> _providers;

        private ModelCatalog(Dictionary<string, Dictionary<string, ModelRates>> providers)
        {
            _providers = providers;
        }

        /// <summary>
        /// The process-wide catalog, loaded from <see cref="PathProvider.Cache"/> on first use and
        /// shared by every session after it.
        /// </summary>
        /// <remarks>
        /// The token cancels this caller's wait, not the load: the narrator and the Director start
        /// side by side, and one of them giving up should not cost the other its prices.
        /// </remarks>
        public static Task<ModelCatalog> GetSharedAsync(CancellationToken cancellationToken = default)
        {
            Task<ModelCatalog> shared;
            lock (SharedGate)
            {
                shared = _shared ??= LoadAsync(Path.Combine(PathProvider.Cache, "models.dev.json"));
            }

            return shared.WaitAsync(cancellationToken);
        }

        /// <summary>
        /// Reads the catalog from <paramref name="cachePath"/>, fetching a fresh copy first when the
        /// one there is missing or older than <see cref="MaxAge"/>.
        /// </summary>
        /// <remarks>
        /// Never throws. Prices are a nicety, and a narrator that could not start because a website
        /// was down would be a poor trade: a failed fetch falls back to the stale copy, and with no
        /// copy at all to <see cref="Empty"/>.
        /// </remarks>
        /// <param name="handler">Where the request goes. Null means a real socket; tests pass a script.</param>
        public static async Task<ModelCatalog> LoadAsync(
            string cachePath,
            HttpMessageHandler? handler = null,
            CancellationToken cancellationToken = default)
        {
            var cached = ReadCache(cachePath, out var age);

            if (cached is not null && age < MaxAge)
            {
                return cached;
            }

            try
            {
                using var client = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null)
                {
                    Timeout = FetchTimeout,
                };

                var text = await client.GetStringAsync(SourceUrl, cancellationToken).ConfigureAwait(false);

                // Parsed before it is written, so a captive portal's login page or a truncated body
                // cannot replace a copy that worked.
                var fresh = Parse(text);
                if (fresh._providers.Count == 0)
                {
                    return cached ?? Empty;
                }

                WriteCache(cachePath, text);
                return fresh;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or IOException or UnauthorizedAccessException)
            {
                return cached ?? Empty;
            }
        }

        /// <summary>Builds a catalog from the text of <c>api.json</c>.</summary>
        /// <exception cref="JsonException">The text is not JSON.</exception>
        public static ModelCatalog Parse(string json)
        {
            using var document = JsonDocument.Parse(json);

            var providers = new Dictionary<string, Dictionary<string, ModelRates>>(StringComparer.OrdinalIgnoreCase);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ModelCatalog(providers);
            }

            foreach (var provider in document.RootElement.EnumerateObject())
            {
                if (provider.Value.ValueKind != JsonValueKind.Object
                    || !provider.Value.TryGetProperty("models", out var models)
                    || models.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var entries = new Dictionary<string, ModelRates>(StringComparer.OrdinalIgnoreCase);

                foreach (var model in models.EnumerateObject())
                {
                    if (model.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    double? input = null, output = null, cacheRead = null;
                    if (model.Value.TryGetProperty("cost", out var cost) && cost.ValueKind == JsonValueKind.Object)
                    {
                        input = ReadDouble(cost, "input");
                        output = ReadDouble(cost, "output");
                        cacheRead = ReadDouble(cost, "cache_read");
                    }

                    var context = 0;
                    if (model.Value.TryGetProperty("limit", out var limit)
                        && limit.ValueKind == JsonValueKind.Object
                        && limit.TryGetProperty("context", out var contextValue)
                        && contextValue.ValueKind == JsonValueKind.Number
                        && contextValue.TryGetInt32(out var parsed)
                        && parsed > 0)
                    {
                        context = parsed;
                    }

                    entries[model.Name] = new ModelRates(input, output, cacheRead, context);
                }

                providers[provider.Name] = entries;
            }

            return new ModelCatalog(providers);
        }

        /// <summary>
        /// The entry for <paramref name="modelId"/> under <paramref name="providerId"/>, or null.
        /// </summary>
        /// <remarks>
        /// Tried in order: the id as given; without Gemini's <c>models/</c> prefix; without a trailing
        /// release date, so a pinned <c>gpt-4o-mini-2024-07-18</c> prices as <c>gpt-4o-mini</c>. Nothing
        /// looser than that. Aliases such as <c>gemini-flash-lite-latest</c> are listed in the catalog
        /// under their own names, and a prefix guess that picked the wrong sibling would show a
        /// confident, wrong figure where a miss shows an honest "?".
        /// </remarks>
        public ModelRates? Resolve(string providerId, string modelId)
        {
            if (!_providers.TryGetValue(providerId, out var models))
            {
                return null;
            }

            var id = modelId.Trim();

            if (models.TryGetValue(id, out var rates))
            {
                return rates;
            }

            if (id.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
            {
                id = id["models/".Length..];

                if (models.TryGetValue(id, out rates))
                {
                    return rates;
                }
            }

            var undated = DateSuffix().Replace(id, string.Empty);

            return undated.Length != id.Length && models.TryGetValue(undated, out rates) ? rates : null;
        }

        /// <summary>
        /// The catalog provider to price a session against, or null where none applies.
        /// </summary>
        /// <remarks>
        /// A custom endpoint that is really one of the presets typed out by hand is priced as that
        /// preset; OpenRouter is recognised by its host. Anything else custom is unpriced unless the
        /// server reports a cost of its own.
        /// </remarks>
        public static string? ProviderFor(AgentProvider provider, string baseUrl) => provider switch
        {
            AgentProvider.OpenAI => "openai",
            AgentProvider.Google => "google",
            AgentProvider.Anthropic => "anthropic",
            AgentProvider.Custom when OpenAiPresets.DetectPreset(baseUrl) is { IsCustom: false } preset
                => ProviderFor(preset.Provider, baseUrl),
            AgentProvider.Custom when HostOf(baseUrl) is { } host
                && (host.Equals("openrouter.ai", StringComparison.OrdinalIgnoreCase)
                    || host.EndsWith(".openrouter.ai", StringComparison.OrdinalIgnoreCase))
                => "openrouter",
            _ => null,
        };

        /// <summary>
        /// Whether <paramref name="baseUrl"/> points at this machine or its local network - a server
        /// the player runs themselves, which charges nothing.
        /// </summary>
        public static bool IsLocal(string baseUrl)
        {
            if (HostOf(baseUrl) is not { } host)
            {
                return false;
            }

            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
                || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!IPAddress.TryParse(host.Trim('[', ']'), out var address))
            {
                return false;
            }

            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                // Link-local and unique-local (fc00::/7).
                return address.IsIPv6LinkLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;
            }

            var bytes = address.GetAddressBytes();
            return bytes[0] == 10
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                || (bytes[0] == 192 && bytes[1] == 168)
                || (bytes[0] == 169 && bytes[1] == 254);
        }

        private static string? HostOf(string baseUrl) =>
            Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri) && uri.Host.Length > 0 ? uri.Host : null;

        private static ModelCatalog? ReadCache(string cachePath, out TimeSpan age)
        {
            age = TimeSpan.MaxValue;

            try
            {
                if (!File.Exists(cachePath))
                {
                    return null;
                }

                age = DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath);
                var catalog = Parse(File.ReadAllText(cachePath));
                return catalog._providers.Count > 0 ? catalog : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return null;
            }
        }

        private static void WriteCache(string cachePath, string text)
        {
            // Best effort: a copy that could not be written only means the next session fetches again.
            try
            {
                if (Path.GetDirectoryName(cachePath) is { Length: > 0 } folder)
                {
                    Directory.CreateDirectory(folder);
                }

                var temporary = cachePath + ".tmp";
                File.WriteAllText(temporary, text);
                File.Move(temporary, cachePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        private static double? ReadDouble(JsonElement owner, string propertyName) =>
            owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
                ? number
                : null;

        /// <summary>A trailing <c>-2024-07-18</c> or <c>-20241022</c>.</summary>
        [GeneratedRegex(@"-(\d{4}-\d{2}-\d{2}|\d{8})$")]
        private static partial Regex DateSuffix();
    }
}
