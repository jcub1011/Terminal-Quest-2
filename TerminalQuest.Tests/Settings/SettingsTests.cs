using System.Reflection;
using System.Text;
using System.Text.Json;

using TerminalQuest.Saves;
using TerminalQuest.Settings;

using Xunit;

namespace TerminalQuest.Tests.Settings
{
    /// <summary>
    /// The settings document, its defaults, and the recovery behaviour that keeps a bad one from
    /// stopping the game starting.
    /// </summary>
    public sealed class SettingsTests
    {
        private sealed class TempSettings : IDisposable
        {
            public TempSettings()
            {
                Folder = Path.Combine(
                    Path.GetTempPath(),
                    "TerminalQuest.Tests",
                    Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(Folder);
                Path_ = System.IO.Path.Combine(Folder, "settings.json");
            }

            public string Folder { get; }

            public string Path_ { get; }

            public void Write(string contents) => File.WriteAllText(Path_, contents);

            public void Dispose()
            {
                try
                {
                    Directory.Delete(Folder, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        // ---- Defaults --------------------------------------------------------------------

        [Fact]
        public void A_fresh_install_has_a_working_configuration()
        {
            var settings = new AppSettings();

            Assert.Equal(AgentProvider.ClaudeCode, settings.Provider);
            Assert.Equal(AppSettings.DefaultClaudeModel, settings.ClaudeModel);
            Assert.Equal(OpenAiPresets.Google.BaseUrl, settings.Google.BaseUrl);
            Assert.Equal(OpenAiPresets.Google.DefaultModel, settings.Google.Model);
            Assert.Equal(OpenAiPresets.OpenAI.BaseUrl, settings.OpenAI.BaseUrl);
            Assert.Equal(OpenAiPresets.Anthropic.BaseUrl, settings.Anthropic.BaseUrl);
            Assert.Equal(AppSettings.DefaultCustomBaseUrl, settings.Custom.BaseUrl);
            Assert.Equal(string.Empty, settings.Custom.Model);
            Assert.Equal(string.Empty, settings.Custom.ResolveApiKey());
            Assert.Equal(AppSettings.DefaultEditorCommand, settings.EditorCommand);
        }

        [Fact]
        public void The_default_provider_is_the_zero_value_so_an_unstated_one_still_works()
        {
            Assert.Equal(default, AgentProvider.ClaudeCode);
        }

        [Fact]
        public void The_default_address_is_one_the_game_would_accept()
        {
            Assert.True(AppSettings.IsAddress(AppSettings.DefaultCustomBaseUrl));
        }

        // ---- Addresses -------------------------------------------------------------------------

        [Theory]
        [InlineData("http://localhost:1234/v1")]
        [InlineData("https://example.test/v1")]
        [InlineData("HTTP://EXAMPLE.TEST")]
        public void Somewhere_a_request_could_go_is_an_address(string value)
        {
            Assert.True(AppSettings.IsAddress(value));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("localhost:1234")]
        [InlineData("file://c:/x")]
        [InlineData("ftp://example.test")]
        [InlineData("not a url")]
        public void Anything_else_is_not(string value)
        {
            Assert.False(AppSettings.IsAddress(value));
        }

        [Fact]
        public void A_null_address_is_refused_rather_than_throwing()
        {
            Assert.False(AppSettings.IsAddress(null!));
        }

        // ---- Presets ---------------------------------------------------------------------------

        [Fact]
        public void Presets_define_known_providers()
        {
            Assert.Equal(4, OpenAiPresets.All.Length);
            Assert.Equal("Google", OpenAiPresets.Google.Name);
            Assert.Equal("OpenAI", OpenAiPresets.OpenAI.Name);
            Assert.Equal("Anthropic", OpenAiPresets.Anthropic.Name);
            Assert.Equal("Custom", OpenAiPresets.Custom.Name);
            Assert.True(OpenAiPresets.Custom.IsCustom);
            Assert.False(OpenAiPresets.Google.IsCustom);
            Assert.Equal(AgentProvider.Google, OpenAiPresets.Google.Provider);
            Assert.Equal(AgentProvider.OpenAI, OpenAiPresets.OpenAI.Provider);
            Assert.Equal(AgentProvider.Anthropic, OpenAiPresets.Anthropic.Provider);
            Assert.Equal(AgentProvider.Custom, OpenAiPresets.Custom.Provider);
            Assert.Same(OpenAiPresets.Google, OpenAiPresets.ForProvider(AgentProvider.Google));
            Assert.Same(OpenAiPresets.OpenAI, OpenAiPresets.ForProvider(AgentProvider.OpenAI));
            Assert.Same(OpenAiPresets.Anthropic, OpenAiPresets.ForProvider(AgentProvider.Anthropic));
            Assert.Same(OpenAiPresets.Custom, OpenAiPresets.ForProvider(AgentProvider.Custom));
            Assert.Equal(AgentProvider.Google, OpenAiPresets.ProviderForPreset("Google"));
            Assert.Equal(AgentProvider.Custom, OpenAiPresets.ProviderForPreset("NoSuchPreset"));
        }

        [Theory]
        [InlineData("https://generativelanguage.googleapis.com/v1beta/openai", "Google")]
        [InlineData("https://generativelanguage.googleapis.com/v1beta/openai/", "Google")]
        [InlineData("https://api.openai.com/v1", "OpenAI")]
        [InlineData("https://api.openai.com", "OpenAI")]
        [InlineData("https://api.openai.com/", "OpenAI")]
        [InlineData("https://api.anthropic.com/v1", "Anthropic")]
        [InlineData("https://api.anthropic.com", "Anthropic")]
        [InlineData("https://api.anthropic.com/", "Anthropic")]
        [InlineData("http://localhost:1234/v1", "Custom")]
        [InlineData("http://localhost:1234", "Custom")]
        [InlineData("http://127.0.0.1:11434/v1", "Custom")]
        [InlineData("http://127.0.0.1:11434", "Custom")]
        public void DetectPreset_matches_url_to_preset(string url, string expectedPreset)
        {
            var preset = OpenAiPresets.DetectPreset(url);
            Assert.Equal(expectedPreset, preset.Name);
        }

        [Theory]
        [InlineData(null, AppSettings.DefaultCustomBaseUrl)]
        [InlineData("", AppSettings.DefaultCustomBaseUrl)]
        [InlineData("   ", AppSettings.DefaultCustomBaseUrl)]
        [InlineData("http://localhost:1234", "http://localhost:1234/v1")]
        [InlineData("http://localhost:1234/", "http://localhost:1234/v1")]
        [InlineData("http://127.0.0.1:1234", "http://127.0.0.1:1234/v1")]
        [InlineData("http://localhost:11434", "http://localhost:11434/v1")]
        [InlineData("https://api.openai.com", "https://api.openai.com/v1")]
        [InlineData("https://api.openai.com/", "https://api.openai.com/v1")]
        [InlineData("http://localhost:1234/v1", "http://localhost:1234/v1")]
        [InlineData("http://localhost:1234/v1/", "http://localhost:1234/v1")]
        [InlineData("https://generativelanguage.googleapis.com/v1beta/openai", "https://generativelanguage.googleapis.com/v1beta/openai")]
        [InlineData("https://generativelanguage.googleapis.com/v1beta/openai/", "https://generativelanguage.googleapis.com/v1beta/openai")]
        public void NormalizeBaseUrl_normalizes_root_and_preserves_path_endpoints(string? input, string expected)
        {
            Assert.Equal(expected, AppSettings.NormalizeBaseUrl(input));
        }

        // ---- Copying ---------------------------------------------------------------------------

        [Fact]
        public void Copying_takes_every_settable_property()
        {
            // Written reflectively so that a setting added later cannot be silently left behind by
            // a copy block nobody remembered to update — which is the stated reason CopyFrom exists.
            var source = new AppSettings
            {
                Provider = AgentProvider.Google,
                ClaudeModel = "claude-opus-5",
                DirectorClaudeModel = "claude-director-3",
                Google = new OpenAiEndpointConfig
                {
                    BaseUrl = "https://google.test/v1",
                    Model = "google-model",
                    DirectorModel = "google-director",
                    ApiKey = "google-key",
                },
                OpenAI = new OpenAiEndpointConfig
                {
                    BaseUrl = "https://openai.test/v1",
                    Model = "openai-model",
                    DirectorModel = "openai-director",
                    ApiKey = "openai-key",
                },
                Anthropic = new OpenAiEndpointConfig
                {
                    BaseUrl = "https://anthropic.test/v1",
                    Model = "anthropic-model",
                    DirectorModel = "anthropic-director",
                    ApiKey = "anthropic-key",
                },
                Custom = new OpenAiEndpointConfig
                {
                    BaseUrl = "https://example.test/v1",
                    Model = "some-model",
                    DirectorModel = "director-model",
                    ApiKey = "secret",
                },
                EditorCommand = "code -w",
                TranscriptRecallCharacters = 1234,
            };

            var destination = new AppSettings();
            destination.CopyFrom(source);

            var fresh = new AppSettings();

            foreach (var property in typeof(AppSettings)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.CanRead && property.CanWrite))
            {
                // Every property has to differ from a fresh instance, or the assertion below would
                // pass on a setting CopyFrom never touched. That is the exact failure this test is
                // for, and without this it only catches it by luck.
                Assert.NotEqual(property.GetValue(fresh), property.GetValue(source));
                Assert.Equal(property.GetValue(source), property.GetValue(destination));
            }
        }

        [Fact]
        public void Copying_from_nothing_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(() => new AppSettings().CopyFrom(null!));
        }

        // ---- The model table ----------------------------------------------------------------------

        [Fact]
        public void The_model_table_offers_a_default_that_defers_to_the_cli()
        {
            Assert.Equal(string.Empty, ClaudeModels.All[0].Id);
        }

        [Fact]
        public void Model_ids_are_unique()
        {
            var ids = ClaudeModels.All.Select(entry => entry.Id).ToList();

            Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        }

        [Fact]
        public void Every_offered_model_names_itself_and_its_trade()
        {
            // The gap between the cheapest and the dearest is the difference between a game that
            // costs pennies and one that does not, so the detail is not decoration.
            Assert.All(ClaudeModels.All, entry =>
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Name));
                Assert.False(string.IsNullOrWhiteSpace(entry.Detail));
            });
        }

        [Fact]
        public void The_shipped_default_model_is_one_the_table_offers()
        {
            Assert.True(ClaudeModels.IndexOf(AppSettings.DefaultClaudeModel) >= 0);
        }

        [Fact]
        public void The_default_entry_is_found()
        {
            Assert.Equal(0, ClaudeModels.IndexOf(""));
        }

        [Theory]
        [InlineData("claude-haiku-4-5")]
        [InlineData("claude-opus-5")]
        [InlineData("CLAUDE-HAIKU-4-5")]
        [InlineData("claude-something-else")]
        public void An_id_this_build_does_not_know_is_a_miss_rather_than_a_fault(string id)
        {
            // Only the Default row ships: named ids are typed freehand or merged live from
            // the API, never known to the build. A miss is still not a fault.
            Assert.Equal(-1, ClaudeModels.IndexOf(id));
        }

        [Fact]
        public void A_null_id_reads_as_the_default_entry()
        {
            Assert.Equal(0, ClaudeModels.IndexOf(null!));
            Assert.Equal("Default", ClaudeModels.Describe(null!));
        }

        [Fact]
        public void The_default_is_described_by_name()
        {
            Assert.Equal("Default", ClaudeModels.Describe(""));
        }

        [Theory]
        [InlineData("claude-haiku-4-5")]
        [InlineData("claude-from-the-future")]
        public void An_unknown_id_is_still_named_something_truthful(string id)
        {
            Assert.Equal(id, ClaudeModels.Describe($"  {id}  "));
        }

        // ---- The document on disk -------------------------------------------------------------------

        [Fact]
        public void Settings_round_trip_through_a_file()
        {
            using var temp = new TempSettings();
            var written = new AppSettings
            {
                Provider = AgentProvider.Custom,
                ClaudeModel = "claude-opus-5",
                Custom = new OpenAiEndpointConfig
                {
                    BaseUrl = "https://example.test/v1",
                    Model = "some-model",
                    DirectorModel = "director-model",
                },
                EditorCommand = "code -w",
                TranscriptRecallCharacters = 1234,
            };
            written.Custom.SetApiKey("secret");

            SettingsStore.Write(written, temp.Path_);
            var read = SettingsStore.Read(temp.Path_);

            Assert.Equal(AgentProvider.Custom, read.Provider);
            Assert.Equal("claude-opus-5", read.ClaudeModel);
            Assert.Equal("https://example.test/v1", read.Custom.BaseUrl);
            Assert.Equal("some-model", read.Custom.Model);
            Assert.Equal("director-model", read.Custom.DirectorModel);
            Assert.Equal("secret", read.Custom.ResolveApiKey());
            Assert.Equal("code -w", read.EditorCommand);
            Assert.Equal(1234, read.TranscriptRecallCharacters);
        }

        [Fact]
        public void Each_provider_keeps_its_own_values_through_a_file()
        {
            using var temp = new TempSettings();
            var written = new AppSettings { Provider = AgentProvider.Google };
            written.Google.Model = "google-model";
            written.Google.SetApiKey("google-key");
            written.Custom.Model = "custom-model";
            written.Custom.SetApiKey("custom-key");

            SettingsStore.Write(written, temp.Path_);
            var read = SettingsStore.Read(temp.Path_);

            Assert.Equal(AgentProvider.Google, read.Provider);
            Assert.Equal("google-model", read.Google.Model);
            Assert.Equal("google-key", read.Google.ResolveApiKey());
            Assert.Equal("custom-model", read.Custom.Model);
            Assert.Equal("custom-key", read.Custom.ResolveApiKey());
            Assert.Equal(OpenAiPresets.OpenAI.DefaultModel, read.OpenAI.Model);
        }

        [Fact]
        public void The_stored_key_is_never_plaintext()
        {
            using var temp = new TempSettings();
            var written = new AppSettings();
            written.Google.SetApiKey("super-secret-key");

            SettingsStore.Write(written, temp.Path_);

            Assert.DoesNotContain("\"super-secret-key\"", File.ReadAllText(temp.Path_), StringComparison.Ordinal);
            Assert.Equal("super-secret-key", SettingsStore.Read(temp.Path_).Google.ResolveApiKey());
        }

        [Fact]
        public void Sealing_round_trips_a_key()
        {
            const string key = "sk-test-123";
            var stored = ApiKeyProtection.Protect(key);

            Assert.NotEqual(key, stored);
            Assert.Equal(key, ApiKeyProtection.Unprotect(stored));
        }

        [Fact]
        public void A_legacy_plaintext_key_reads_and_is_sealed_on_save()
        {
            // Hand-edited or pre-protection: raw text keeps working, then seals itself.
            Assert.Equal("raw-key", ApiKeyProtection.Unprotect("raw-key"));

            using var temp = new TempSettings();
            temp.Write("""{"provider":"Custom","custom":{"baseUrl":"http://localhost:1234/v1","apiKey":"raw-key"}}""");

            var read = SettingsStore.Read(temp.Path_);
            Assert.Equal("raw-key", read.Custom.ResolveApiKey());

            SettingsStore.Write(read, temp.Path_);
            Assert.DoesNotContain("\"raw-key\"", File.ReadAllText(temp.Path_), StringComparison.Ordinal);
        }

        [Fact]
        public void An_environment_key_wins_without_being_stored()
        {
            const string variable = "TQ2_GOOGLE_API_KEY";
            var previous = Environment.GetEnvironmentVariable(variable);
            try
            {
                Environment.SetEnvironmentVariable(variable, "env-key");
                var settings = new AppSettings { Provider = AgentProvider.Google };
                settings.Google.SetApiKey("stored-key");

                Assert.Equal("env-key", settings.ActiveApiKey);

                using var temp = new TempSettings();
                SettingsStore.Write(settings, temp.Path_);
                Assert.DoesNotContain("env-key", File.ReadAllText(temp.Path_), StringComparison.Ordinal);
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, previous);
            }
        }

        [Fact]
        public void EffectiveProvider_resolves_legacy_and_unknown_values()
        {
            Assert.Equal(AgentProvider.Custom, AppSettings.EffectiveProvider((AgentProvider)1));
            Assert.Equal(AgentProvider.ClaudeCode, AppSettings.EffectiveProvider((AgentProvider)99));
            Assert.Equal(AgentProvider.Google, AppSettings.EffectiveProvider(AgentProvider.Google));
            Assert.True(AppSettings.IsOpenAiProvider(AgentProvider.Anthropic));
            Assert.False(AppSettings.IsOpenAiProvider(AgentProvider.ClaudeCode));
        }

        [Fact]
        public void An_absent_recall_size_reads_as_the_default_rather_than_zero()
        {
            // A settings file written by an older build has no such property, and a save resuming
            // against a zero would recall nothing at all.
            using var temp = new TempSettings();
            temp.Write("""{"provider":"claudeCode"}""");

            Assert.Equal(
                TranscriptRecall.DefaultCharacters,
                SettingsStore.Read(temp.Path_).TranscriptRecallCharacters);
        }

        [Fact]
        public void The_provider_is_stored_as_a_name_rather_than_a_number()
        {
            // So a hand-edited file reads as something a person can understand, and so reordering
            // the enum cannot silently change what a stored file means.
            using var temp = new TempSettings();
            SettingsStore.Write(new AppSettings { Provider = AgentProvider.Anthropic }, temp.Path_);

            Assert.Contains("\"Anthropic\"", File.ReadAllText(temp.Path_), StringComparison.Ordinal);
        }

        [Fact]
        public void Legacy_provider_names_migrate_to_their_provider()
        {
            using var temp = new TempSettings();
            temp.Write("""{"provider":"LmStudio"}""");

            Assert.Equal(AgentProvider.Custom, SettingsStore.Read(temp.Path_).Provider);
        }

        [Fact]
        public void Property_names_are_camel_case()
        {
            using var temp = new TempSettings();
            SettingsStore.Write(new AppSettings(), temp.Path_);

            var json = File.ReadAllText(temp.Path_);

            Assert.Contains("\"claudeModel\"", json, StringComparison.Ordinal);
            Assert.Contains("\"google\"", json, StringComparison.Ordinal);
            Assert.Contains("\"custom\"", json, StringComparison.Ordinal);
            Assert.DoesNotContain("lmStudioBaseUrl", json, StringComparison.Ordinal);
            Assert.DoesNotContain("openAiPreset", json, StringComparison.Ordinal);
        }

        [Fact]
        public void Writing_creates_the_folder_when_it_is_missing()
        {
            using var temp = new TempSettings();
            var nested = Path.Combine(temp.Folder, "nested", "settings.json");

            SettingsStore.Write(new AppSettings(), nested);

            Assert.True(File.Exists(nested));
        }

        [Fact]
        public void Writing_nothing_is_a_programming_error()
        {
            using var temp = new TempSettings();

            Assert.Throws<ArgumentNullException>(() => SettingsStore.Write(null!, temp.Path_));
        }

        // ---- Recovery ---------------------------------------------------------------------------------

        [Fact]
        public void A_file_that_is_not_there_reads_as_the_defaults()
        {
            using var temp = new TempSettings();

            Assert.Equal(AppSettings.DefaultClaudeModel, SettingsStore.Read(temp.Path_).ClaudeModel);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n")]
        [InlineData("{ not json")]
        [InlineData("null")]
        [InlineData("[]")]
        [InlineData("\"a string\"")]
        [InlineData("""{"provider":"SomeProviderThisBuildLacks"}""")]
        public void A_file_the_game_cannot_use_reads_as_the_defaults(string contents)
        {
            // Nothing in here is the player's work and the defaults are a working configuration, so
            // a bad settings file must never stop the game starting.
            using var temp = new TempSettings();
            temp.Write(contents);

            var settings = SettingsStore.Read(temp.Path_);

            Assert.Equal(AppSettings.DefaultClaudeModel, settings.ClaudeModel);
            Assert.Equal(AgentProvider.ClaudeCode, settings.Provider);
        }

        [Fact]
        public void An_unknown_provider_falls_back_to_default_without_costing_the_rest_of_the_file()
        {
            using var temp = new TempSettings();
            temp.Write("""{"provider":"Nonsense","editorCommand":"code -w"}""");

            var settings = SettingsStore.Read(temp.Path_);
            Assert.Equal("code -w", settings.EditorCommand);
            Assert.Equal(AgentProvider.ClaudeCode, settings.Provider);
        }

        [Theory]
        [InlineData("openai", AgentProvider.OpenAI)]
        [InlineData("OpenAI", AgentProvider.OpenAI)]
        [InlineData("google", AgentProvider.Google)]
        [InlineData("gemini", AgentProvider.Google)]
        [InlineData("Google", AgentProvider.Google)]
        [InlineData("anthropic", AgentProvider.Anthropic)]
        [InlineData("Anthropic", AgentProvider.Anthropic)]
        [InlineData("custom", AgentProvider.Custom)]
        [InlineData("Custom", AgentProvider.Custom)]
        [InlineData("claude", AgentProvider.ClaudeCode)]
        [InlineData("claude-code", AgentProvider.ClaudeCode)]
        [InlineData("ClaudeCode", AgentProvider.ClaudeCode)]
        [InlineData("claudeCode", AgentProvider.ClaudeCode)]
        [InlineData("0", AgentProvider.ClaudeCode)]
        [InlineData("2", AgentProvider.Google)]
        [InlineData("3", AgentProvider.OpenAI)]
        [InlineData("4", AgentProvider.Anthropic)]
        [InlineData("5", AgentProvider.Custom)]
        // Legacy aggregates resolve to Custom once normalized: the old preset beside them
        // decides the real provider during migration (covered below).
        [InlineData("OpenAiApi", AgentProvider.Custom)]
        [InlineData("openAiApi", AgentProvider.Custom)]
        [InlineData("lm-studio", AgentProvider.Custom)]
        [InlineData("LmStudio", AgentProvider.Custom)]
        [InlineData("api", AgentProvider.Custom)]
        [InlineData("1", AgentProvider.Custom)]
        internal void Provider_deserializes_various_formats_resiliently(string providerJson, AgentProvider expected)
        {
            using var temp = new TempSettings();
            var json = int.TryParse(providerJson, out _)
                ? $$"""{"provider": {{providerJson}}, "editorCommand": "code -w"}"""
                : $$"""{"provider": "{{providerJson}}", "editorCommand": "code -w"}""";
            temp.Write(json);

            var settings = SettingsStore.Read(temp.Path_);
            Assert.Equal(expected, settings.Provider);
            Assert.Equal("code -w", settings.EditorCommand);
        }

        [Fact]
        public void An_unknown_property_does_not_cost_the_rest_of_the_file()
        {
            using var temp = new TempSettings();
            temp.Write("""{"editorCommand":"code -w","settingFromALaterBuild":true}""");

            Assert.Equal("code -w", SettingsStore.Read(temp.Path_).EditorCommand);
        }

        [Fact]
        public void A_partial_file_keeps_the_defaults_for_what_it_leaves_out()
        {
            using var temp = new TempSettings();
            temp.Write("""{"editorCommand":"code -w"}""");

            var settings = SettingsStore.Read(temp.Path_);

            Assert.Equal("code -w", settings.EditorCommand);
            Assert.Equal(AppSettings.DefaultClaudeModel, settings.ClaudeModel);
        }

        [Fact]
        public void A_file_carrying_a_byte_order_mark_still_reads()
        {
            using var temp = new TempSettings();
            File.WriteAllText(temp.Path_, """{"editorCommand":"code -w"}""", new UTF8Encoding(true));

            Assert.Equal("code -w", SettingsStore.Read(temp.Path_).EditorCommand);
        }

        [Fact]
        public void Reading_never_throws_whatever_is_in_the_file()
        {
            using var temp = new TempSettings();

            foreach (var contents in new[]
            {
                "{}", "[]", "0", "true", "\u0000", new string('x', 100_000),
                """{"provider":123}""", """{"claudeModel":null}""",
            })
            {
                temp.Write(contents);

                var settings = SettingsStore.Read(temp.Path_);

                Assert.NotNull(settings);
            }
        }

        [Fact]
        public void A_null_string_in_the_file_is_normalized_to_safe_value()
        {
            using var temp = new TempSettings();
            temp.Write("""{"claudeModel":null,"editorCommand":null}""");

            var settings = SettingsStore.Read(temp.Path_);

            Assert.Equal(string.Empty, settings.ClaudeModel);
            Assert.Equal(AppSettings.DefaultEditorCommand, settings.EditorCommand);
        }

        [Fact]
        public void A_null_path_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(() => SettingsStore.Read(null!));
            Assert.Throws<ArgumentNullException>(() => SettingsStore.Write(new AppSettings(), null!));
        }

        [Fact]
        public void The_real_settings_path_sits_beside_the_saves_rather_than_inside_one()
        {
            // A preference outlives any one playthrough and must not be deleted with one.
            Assert.EndsWith("settings.json", SettingsStore.Path, StringComparison.Ordinal);
            Assert.DoesNotContain(
                Path.Combine("TerminalQuest", "Saves"),
                SettingsStore.Path,
                StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void The_settings_context_writes_what_it_reads()
        {
            var json = JsonSerializer.Serialize(
                new AppSettings { EditorCommand = "code -w" },
                SettingsJsonContext.Default.AppSettings);

            var read = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings)!;

            Assert.Equal("code -w", read.EditorCommand);
        }

        [Fact]
        public void A_legacy_settings_file_migrates_to_its_preset_provider()
        {
            using var temp = new TempSettings();
            temp.Write("""
            {
              "provider": "OpenAiApi",
              "claudeModel": "",
              "openAiPreset": "Google",
              "lmStudioBaseUrl": "https://generativelanguage.googleapis.com/v1beta/openai",
              "lmStudioModel": "gemini-flash-lite-latest",
              "lmStudioApiKey": "placeholder-api-key",
              "editorCommand": "notepad.exe",
              "transcriptRecallCharacters": 4000
            }
            """);
            var read = SettingsStore.Read(temp.Path_);
            Assert.Equal(AgentProvider.Google, read.Provider);
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai", read.Google.BaseUrl);
            Assert.Equal("gemini-flash-lite-latest", read.Google.Model);
            Assert.Equal("placeholder-api-key", read.Google.ResolveApiKey());
            Assert.Equal("notepad.exe", read.EditorCommand);
            Assert.Equal(4000, read.TranscriptRecallCharacters);
        }

        [Fact]
        public void A_legacy_custom_file_migrates_its_address_and_key()
        {
            using var temp = new TempSettings();
            temp.Write("""
            {
              "provider": "LmStudio",
              "openAiPreset": "Custom",
              "lmStudioBaseUrl": "http://my-host:8080/v1",
              "lmStudioModel": "my-model",
              "directorLmStudioModel": "my-director",
              "lmStudioApiKey": "my-key"
            }
            """);
            var read = SettingsStore.Read(temp.Path_);
            Assert.Equal(AgentProvider.Custom, read.Provider);
            Assert.Equal("http://my-host:8080/v1", read.Custom.BaseUrl);
            Assert.Equal("my-model", read.Custom.Model);
            Assert.Equal("my-director", read.Custom.DirectorModel);
            Assert.Equal("my-key", read.Custom.ResolveApiKey());
        }

        [Fact]
        public void A_legacy_standby_config_survives_beside_claude()
        {
            using var temp = new TempSettings();
            temp.Write("""
            {
              "provider": "ClaudeCode",
              "openAiPreset": "OpenAI",
              "lmStudioBaseUrl": "https://api.openai.com/v1",
              "lmStudioModel": "gpt-4o",
              "lmStudioApiKey": "standby-key"
            }
            """);
            var read = SettingsStore.Read(temp.Path_);
            Assert.Equal(AgentProvider.ClaudeCode, read.Provider);
            Assert.Equal("gpt-4o", read.OpenAI.Model);
            Assert.Equal("standby-key", read.OpenAI.ResolveApiKey());
        }

        [Fact]
        public void A_blank_api_key_is_preserved()
        {
            using var temp = new TempSettings();
            var settings = new AppSettings
            {
                Provider = AgentProvider.Google,
            };
            settings.Google.SetApiKey(string.Empty);

            SettingsStore.Write(settings, temp.Path_);
            var read = SettingsStore.Read(temp.Path_);

            Assert.Equal(string.Empty, read.Google.ResolveApiKey());
        }

        [Fact]
        public void Atomic_settings_write_replaces_target_and_leaves_no_tmp_behind()
        {
            using var temp = new TempSettings();
            var settings = new AppSettings();
            settings.Custom.Model = "test-model";

            SettingsStore.Write(settings, temp.Path_);

            Assert.True(File.Exists(temp.Path_));
            Assert.False(File.Exists($"{temp.Path_}.tmp"));
            Assert.Equal("test-model", SettingsStore.Read(temp.Path_).Custom.Model);
        }

        [Fact]
        public void Settings_path_is_isolated_under_test_root()
        {
            using var root = new TerminalQuest.Tests.Infrastructure.SavesRoot();
            Assert.Equal(Path.Combine(root.Root, "Settings", "settings.json"), SettingsStore.Path);
            Assert.DoesNotContain(AppDirectory.Root, SettingsStore.Path, StringComparison.OrdinalIgnoreCase);
        }
    }
}
