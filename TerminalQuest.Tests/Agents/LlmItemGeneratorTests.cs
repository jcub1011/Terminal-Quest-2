using System.Net;
using System.Text;

using TerminalQuest.Agents;
using TerminalQuest.Saves;
using TerminalQuest.Settings;

using Xunit;

namespace TerminalQuest.Tests.Agents
{
    public sealed class LlmItemGeneratorTests
    {
        [Fact]
        public void Prompt_compose_substitutes_summary_and_aptitude()
        {
            var summary = "A stealthy rogue who strikes from the shadows.";
            var aptitude = "Skilled in lockpicking, poisons, and silent footfalls.";

            var composed = ItemGeneratorPromptFile.Compose(summary, aptitude);

            Assert.Contains(summary, composed);
            Assert.Contains(aptitude, composed);
            Assert.DoesNotContain("{{SUMMARY}}", composed);
            Assert.DoesNotContain("{{APTITUDE}}", composed);
        }

        [Fact]
        public void GetDefaultItems_returns_expected_categories()
        {
            var defaults = LlmItemGenerator.GetDefaultItems();

            Assert.NotEmpty(defaults.Weapons);
            Assert.NotEmpty(defaults.Offhands);
            Assert.NotEmpty(defaults.Specials);

            Assert.All(defaults.Weapons, w =>
            {
                Assert.False(string.IsNullOrWhiteSpace(w.Name));
                Assert.False(string.IsNullOrWhiteSpace(w.Description));
                Assert.True(w.Quantity > 0);
            });

            Assert.All(defaults.Offhands, o =>
            {
                Assert.False(string.IsNullOrWhiteSpace(o.Name));
                Assert.False(string.IsNullOrWhiteSpace(o.Description));
                Assert.True(o.Quantity > 0);
            });

            Assert.All(defaults.Specials, s =>
            {
                Assert.False(string.IsNullOrWhiteSpace(s.Name));
                Assert.False(string.IsNullOrWhiteSpace(s.Description));
                Assert.True(s.Quantity > 0);
            });
        }

        [Fact]
        public void ParseItemsJson_parses_valid_schema()
        {
            var json = """
            {
              "weapons": [
                { "name": "sunblade", "description": "Glows with dawnlight." }
              ],
              "offhands": [
                { "name": "mirror shield", "description": "Polished silver." }
              ],
              "specials": [
                { "name": "astrolabe", "description": "Brass instruments." }
              ]
            }
            """;

            var result = LlmItemGenerator.ParseItemsJson(json);

            Assert.Single(result.Weapons);
            Assert.Equal("sunblade", result.Weapons[0].Name);
            Assert.Equal("Glows with dawnlight.", result.Weapons[0].Description);

            Assert.Single(result.Offhands);
            Assert.Equal("mirror shield", result.Offhands[0].Name);

            Assert.Single(result.Specials);
            Assert.Equal("astrolabe", result.Specials[0].Name);
        }

        [Fact]
        public void ParseItemsJson_handles_markdown_fences()
        {
            var json = """
            ```json
            {
              "weapons": [
                { "name": "shadow dagger", "description": "Blackened steel." }
              ],
              "offhands": [
                { "name": "smoke bomb", "description": "Clay sphere." }
              ],
              "specials": [
                { "name": "lockpicks", "description": "Fine wires." }
              ]
            }
            ```
            """;

            var result = LlmItemGenerator.ParseItemsJson(json);

            Assert.Single(result.Weapons);
            Assert.Equal("shadow dagger", result.Weapons[0].Name);
        }

        [Fact]
        public void ParseItemsJson_falls_back_on_malformed_json()
        {
            var result = LlmItemGenerator.ParseItemsJson("This is not valid JSON at all.");

            Assert.NotEmpty(result.Weapons);
            Assert.NotEmpty(result.Offhands);
            Assert.NotEmpty(result.Specials);
        }

        [Fact]
        public async Task GenerateAsync_with_mock_openai_returns_generated_items()
        {
            var mockResponseJson = """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "{\"weapons\":[{\"name\":\"flame rapier\",\"description\":\"Flickers with heat.\"}],\"offhands\":[{\"name\":\"spellbook\",\"description\":\"Vellum bound in leather.\"}],\"specials\":[{\"name\":\"fire crystal\",\"description\":\"A pulsing ruby.\"}]}"
                  }
                }
              ]
            }
            """;

            var handler = new MockHttpMessageHandler(mockResponseJson);
            var settings = new AppSettings
            {
                Provider = AgentProvider.OpenAiApi,
                LmStudioBaseUrl = "http://localhost:1234/v1",
                LmStudioModel = "test-model",
            };

            var items = await LlmItemGenerator.GenerateAsync(
                settings,
                "A fiery battlemage.",
                "Schooled in pyromancy and fencing.",
                CancellationToken.None,
                handler);

            Assert.Single(items.Weapons);
            Assert.Equal("flame rapier", items.Weapons[0].Name);
            Assert.Single(items.Offhands);
            Assert.Equal("spellbook", items.Offhands[0].Name);
            Assert.Single(items.Specials);
            Assert.Equal("fire crystal", items.Specials[0].Name);
        }

        private sealed class MockHttpMessageHandler : HttpMessageHandler
        {
            private readonly string _response;

            public MockHttpMessageHandler(string response)
            {
                _response = response;
            }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(_response, Encoding.UTF8, "application/json"),
                };
                return Task.FromResult(response);
            }
        }
    }
}
