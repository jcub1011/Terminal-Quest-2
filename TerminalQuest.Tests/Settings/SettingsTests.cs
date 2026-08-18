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
            Assert.Equal(OpenAiPresets.Custom.Name, settings.OpenAiPreset);
            Assert.Equal(AppSettings.DefaultLmStudioBaseUrl, settings.LmStudioBaseUrl);
            Assert.Equal(AppSettings.DefaultLmStudioApiKey, settings.LmStudioApiKey);
            Assert.Equal(AppSettings.DefaultEditorCommand, settings.EditorCommand);
            Assert.Equal(string.Empty, settings.LmStudioModel);
        }

        [Fact]
        public void The_default_provider_is_the_zero_value_so_an_unstated_one_still_works()
        {
            Assert.Equal(default, AgentProvider.ClaudeCode);
        }

        [Fact]
        public void The_default_address_is_one_the_game_would_accept()
        {
            Assert.True(AppSettings.IsAddress(AppSettings.DefaultLmStudioBaseUrl));
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
        [InlineData(null, AppSettings.DefaultLmStudioBaseUrl)]
        [InlineData("", AppSettings.DefaultLmStudioBaseUrl)]
        [InlineData("   ", AppSettings.DefaultLmStudioBaseUrl)]
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
                Provider = AgentProvider.OpenAiApi,
                ClaudeModel = "claude-opus-5",
                DirectorClaudeModel = "claude-director-3",
                OpenAiPreset = "Google",
                LmStudioBaseUrl = "https://example.test/v1",
                LmStudioModel = "some-model",
                DirectorLmStudioModel = "director-model",
                LmStudioApiKey = "secret",
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

        [Theory]
        [InlineData("claude-haiku-4-5", 1)]
        [InlineData("  claude-opus-5  ", 3)]
        [InlineData("", 0)]
        public void A_known_id_is_found(string id, int expected)
        {
            Assert.Equal(expected, ClaudeModels.IndexOf(id));
        }

        [Theory]
        [InlineData("CLAUDE-HAIKU-4-5")]
        [InlineData("claude-something-else")]
        public void An_id_this_build_does_not_know_is_a_miss_rather_than_a_fault(string id)
        {
            // Settings written by an older build hold a dated id, and a player may hand-edit the
            // file to something newer than this list.
            Assert.Equal(-1, ClaudeModels.IndexOf(id));
        }

        [Fact]
        public void A_null_id_reads_as_the_default_entry()
        {
            Assert.Equal(0, ClaudeModels.IndexOf(null!));
            Assert.Equal("Default", ClaudeModels.Describe(null!));
        }

        [Theory]
        [InlineData("claude-haiku-4-5", "Haiku")]
        [InlineData("", "Default")]
        public void A_known_id_is_described_by_name(string id, string expected)
        {
            Assert.Equal(expected, ClaudeModels.Describe(id));
        }

        [Fact]
        public void An_unknown_id_is_still_named_something_truthful()
        {
            Assert.Equal("claude-from-the-future", ClaudeModels.Describe("  claude-from-the-future  "));
        }

        // ---- The document on disk -------------------------------------------------------------------

        [Fact]
        public void Settings_round_trip_through_a_file()
        {
            using var temp = new TempSettings();
            var written = new AppSettings
            {
                Provider = AgentProvider.LmStudio,
                ClaudeModel = "claude-opus-5",
                LmStudioBaseUrl = "https://example.test/v1",
                LmStudioModel = "some-model",
                LmStudioApiKey = "secret",
                EditorCommand = "code -w",
                TranscriptRecallCharacters = 1234,
            };

            SettingsStore.Write(written, temp.Path_);
            var read = SettingsStore.Read(temp.Path_);

            Assert.Equal(AgentProvider.LmStudio, read.Provider);
            Assert.Equal("claude-opus-5", read.ClaudeModel);
            Assert.Equal("https://example.test/v1", read.LmStudioBaseUrl);
            Assert.Equal("some-model", read.LmStudioModel);
            Assert.Equal("secret", read.LmStudioApiKey);
            Assert.Equal("code -w", read.EditorCommand);
            Assert.Equal(1234, read.TranscriptRecallCharacters);
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
            SettingsStore.Write(new AppSettings { Provider = AgentProvider.OpenAiApi }, temp.Path_);

            Assert.Contains("\"OpenAiApi\"", File.ReadAllText(temp.Path_), StringComparison.Ordinal);
        }

        [Fact]
        public void Legacy_provider_name_lm_studio_reads_as_openai_api()
        {
            using var temp = new TempSettings();
            temp.Write("""{"provider":"LmStudio"}""");

            Assert.Equal(AgentProvider.OpenAiApi, SettingsStore.Read(temp.Path_).Provider);
        }

        [Fact]
        public void Property_names_are_camel_case()
        {
            using var temp = new TempSettings();
            SettingsStore.Write(new AppSettings(), temp.Path_);

            var json = File.ReadAllText(temp.Path_);

            Assert.Contains("\"claudeModel\"", json, StringComparison.Ordinal);
            Assert.Contains("\"lmStudioBaseUrl\"", json, StringComparison.Ordinal);
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
        [InlineData("openai", AgentProvider.OpenAiApi)]
        [InlineData("OpenAI", AgentProvider.OpenAiApi)]
        [InlineData("OpenAiApi", AgentProvider.OpenAiApi)]
        [InlineData("openAiApi", AgentProvider.OpenAiApi)]
        [InlineData("lm-studio", AgentProvider.OpenAiApi)]
        [InlineData("LmStudio", AgentProvider.OpenAiApi)]
        [InlineData("google", AgentProvider.OpenAiApi)]
        [InlineData("gemini", AgentProvider.OpenAiApi)]
        [InlineData("claude", AgentProvider.ClaudeCode)]
        [InlineData("claude-code", AgentProvider.ClaudeCode)]
        [InlineData("ClaudeCode", AgentProvider.ClaudeCode)]
        [InlineData("claudeCode", AgentProvider.ClaudeCode)]
        [InlineData("0", AgentProvider.ClaudeCode)]
        [InlineData("1", AgentProvider.OpenAiApi)]
        internal void Provider_deserializes_various_formats_resiliently(string providerJson, AgentProvider expected)
        {
            using var temp = new TempSettings();
            var json = providerJson is "0" or "1"
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
        public void User_configured_settings_file_loads_accurately()
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
            Assert.Equal(AgentProvider.OpenAiApi, read.Provider);
            Assert.Equal("Google", read.OpenAiPreset);
            Assert.Equal("https://generativelanguage.googleapis.com/v1beta/openai", read.LmStudioBaseUrl);
            Assert.Equal("gemini-flash-lite-latest", read.LmStudioModel);
            Assert.Equal("placeholder-api-key", read.LmStudioApiKey);
            Assert.Equal("notepad.exe", read.EditorCommand);
            Assert.Equal(4000, read.TranscriptRecallCharacters);
        }
    }
}
