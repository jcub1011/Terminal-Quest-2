using System.Net;
using System.Text;

using TerminalQuest.Agents.Pricing;
using TerminalQuest.Settings;

using Xunit;

namespace TerminalQuest.Tests.Agents
{
    /// <summary>
    /// The models.dev price catalog: what it reads, how it finds a model, and how it behaves when the
    /// network will not cooperate.
    /// </summary>
    public sealed class ModelCatalogTests
    {
        /// <summary>A trimmed-down <c>api.json</c> in the published shape.</summary>
        internal const string Sample = """
            {
              "openai": {
                "id": "openai",
                "models": {
                  "gpt-4o-mini": {
                    "id": "gpt-4o-mini",
                    "limit": { "context": 128000, "output": 16384 },
                    "cost": { "input": 0.15, "output": 0.6, "cache_read": 0.075 }
                  },
                  "text-embedding-3-small": {
                    "id": "text-embedding-3-small",
                    "limit": { "context": 8191 },
                    "cost": { "input": 0.02 }
                  }
                }
              },
              "google": {
                "id": "google",
                "models": {
                  "gemini-flash-lite-latest": {
                    "id": "gemini-flash-lite-latest",
                    "limit": { "context": 1048576 },
                    "cost": { "input": 0.3, "output": 2.5, "cache_read": 0.03 }
                  }
                }
              }
            }
            """;

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public void Prices_and_the_context_window_are_read_per_model()
        {
            var rates = ModelCatalog.Parse(Sample).Resolve("openai", "gpt-4o-mini");

            Assert.NotNull(rates);
            Assert.Equal(0.15, rates.InputPerM);
            Assert.Equal(0.6, rates.OutputPerM);
            Assert.Equal(0.075, rates.CacheReadPerM);
            Assert.Equal(128000, rates.ContextLimit);
            Assert.True(rates.IsPriced);
        }

        [Fact]
        public void A_model_with_no_output_price_is_not_priced()
        {
            var rates = ModelCatalog.Parse(Sample).Resolve("openai", "text-embedding-3-small");

            Assert.NotNull(rates);
            Assert.False(rates.IsPriced);
            Assert.Null(rates.Cost(100, 0, 100));
        }

        [Fact]
        public void Cost_is_per_million_with_cached_tokens_at_the_cache_rate()
        {
            var rates = new ModelRates(InputPerM: 1.0, OutputPerM: 4.0, CacheReadPerM: 0.1, ContextLimit: 0);

            // 1M uncached at $1 + 1M cached at $0.10 + 1M out at $4.
            Assert.Equal(5.1, rates.Cost(1_000_000, 1_000_000, 1_000_000)!.Value, precision: 10);
        }

        [Fact]
        public void Without_a_cache_rate_cached_tokens_are_charged_as_input()
        {
            var rates = new ModelRates(InputPerM: 2.0, OutputPerM: 0.0, CacheReadPerM: null, ContextLimit: 0);

            Assert.Equal(2.0, rates.Cost(500_000, 500_000, 0)!.Value, precision: 10);
        }

        [Theory]
        [InlineData("openai", "gpt-4o-mini")]
        [InlineData("OpenAI", "GPT-4o-Mini")]
        [InlineData("openai", "gpt-4o-mini-2024-07-18")]
        [InlineData("openai", "gpt-4o-mini-20240718")]
        [InlineData("google", "gemini-flash-lite-latest")]
        [InlineData("google", "models/gemini-flash-lite-latest")]
        public void A_model_is_found_under_the_names_providers_actually_use(string provider, string model)
        {
            Assert.NotNull(ModelCatalog.Parse(Sample).Resolve(provider, model));
        }

        [Theory]
        [InlineData("openai", "gpt-4o")]
        [InlineData("anthropic", "gpt-4o-mini")]
        [InlineData("google", "gemini-flash-lite")]
        public void Nothing_looser_than_that_is_guessed(string provider, string model)
        {
            // A near miss is a different model with a different price. Better unpriced than wrong.
            Assert.Null(ModelCatalog.Parse(Sample).Resolve(provider, model));
        }

        [Fact]
        public async Task A_fresh_cache_is_used_without_asking_the_network()
        {
            using var folder = new Folder();
            File.WriteAllText(folder.CachePath, Sample);
            var handler = new CountingHandler(HttpStatusCode.OK, "{}");

            var catalog = await ModelCatalog.LoadAsync(folder.CachePath, handler, Token);

            Assert.Equal(0, handler.Requests);
            Assert.NotNull(catalog.Resolve("openai", "gpt-4o-mini"));
        }

        [Fact]
        public async Task A_stale_cache_is_refreshed_and_rewritten()
        {
            using var folder = new Folder();
            File.WriteAllText(folder.CachePath, """{"openai":{"models":{"old":{"cost":{"input":1,"output":1}}}}}""");
            File.SetLastWriteTimeUtc(folder.CachePath, DateTime.UtcNow - TimeSpan.FromDays(2));
            var handler = new CountingHandler(HttpStatusCode.OK, Sample);

            var catalog = await ModelCatalog.LoadAsync(folder.CachePath, handler, Token);

            Assert.Equal(1, handler.Requests);
            Assert.NotNull(catalog.Resolve("openai", "gpt-4o-mini"));
            Assert.Contains("gpt-4o-mini", File.ReadAllText(folder.CachePath), StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_stale_cache_still_serves_when_the_fetch_fails()
        {
            using var folder = new Folder();
            File.WriteAllText(folder.CachePath, Sample);
            File.SetLastWriteTimeUtc(folder.CachePath, DateTime.UtcNow - TimeSpan.FromDays(2));

            var catalog = await ModelCatalog.LoadAsync(
                folder.CachePath, new CountingHandler(HttpStatusCode.ServiceUnavailable, string.Empty), Token);

            Assert.NotNull(catalog.Resolve("openai", "gpt-4o-mini"));
        }

        [Fact]
        public async Task A_page_that_is_not_the_catalog_does_not_replace_a_copy_that_worked()
        {
            // A captive portal answers 200 with its own HTML.
            using var folder = new Folder();
            File.WriteAllText(folder.CachePath, Sample);
            File.SetLastWriteTimeUtc(folder.CachePath, DateTime.UtcNow - TimeSpan.FromDays(2));

            var catalog = await ModelCatalog.LoadAsync(
                folder.CachePath, new CountingHandler(HttpStatusCode.OK, "<html>Sign in to Wi-Fi</html>"), Token);

            Assert.NotNull(catalog.Resolve("openai", "gpt-4o-mini"));
            Assert.Equal(Sample, File.ReadAllText(folder.CachePath));
        }

        [Fact]
        public async Task No_cache_and_no_network_is_an_empty_catalog_rather_than_a_failure()
        {
            using var folder = new Folder();

            var catalog = await ModelCatalog.LoadAsync(
                folder.CachePath, new CountingHandler(HttpStatusCode.ServiceUnavailable, string.Empty), Token);

            Assert.Null(catalog.Resolve("openai", "gpt-4o-mini"));
            Assert.False(File.Exists(folder.CachePath));
        }

        [Theory]
        [InlineData("http://localhost:1234/v1", true)]
        [InlineData("http://127.0.0.1:11434/v1", true)]
        [InlineData("http://[::1]:1234/v1", true)]
        [InlineData("http://192.168.1.20:1234/v1", true)]
        [InlineData("http://10.0.0.5/v1", true)]
        [InlineData("http://172.20.0.5/v1", true)]
        [InlineData("http://gaming-pc.local:1234/v1", true)]
        [InlineData("https://api.openai.com/v1", false)]
        [InlineData("http://172.32.0.5/v1", false)]
        [InlineData("not a url", false)]
        public void Only_a_server_on_this_machine_or_network_counts_as_free(string baseUrl, bool expected)
        {
            Assert.Equal(expected, ModelCatalog.IsLocal(baseUrl));
        }

        [Theory]
        [InlineData(AgentProvider.OpenAI, "https://api.openai.com/v1", "openai")]
        [InlineData(AgentProvider.Google, "https://generativelanguage.googleapis.com/v1beta/openai", "google")]
        [InlineData(AgentProvider.Anthropic, "https://api.anthropic.com/v1", "anthropic")]
        [InlineData(AgentProvider.Custom, "https://api.openai.com/v1", "openai")]
        [InlineData(AgentProvider.Custom, "https://openrouter.ai/api/v1", "openrouter")]
        [InlineData(AgentProvider.Custom, "http://localhost:1234/v1", null)]
        [InlineData(AgentProvider.Custom, "https://llm.example.com/v1", null)]
        internal void Each_provider_is_priced_against_its_own_listing(AgentProvider provider, string baseUrl, string? expected)
        {
            Assert.Equal(expected, ModelCatalog.ProviderFor(provider, baseUrl));
        }

        private sealed class Folder : IDisposable
        {
            private readonly string _root = Path.Combine(Path.GetTempPath(), "TerminalQuest.Tests", Guid.NewGuid().ToString("N"));

            public Folder()
            {
                Directory.CreateDirectory(Path.Combine(_root, "Cache"));
            }

            public string CachePath => Path.Combine(_root, "Cache", "models.dev.json");

            public void Dispose()
            {
                try
                {
                    Directory.Delete(_root, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }

        private sealed class CountingHandler(HttpStatusCode status, string body) : HttpMessageHandler
        {
            public int Requests { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Requests++;

                return Task.FromResult(new HttpResponseMessage(status)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json"),
                });
            }
        }
    }
}
