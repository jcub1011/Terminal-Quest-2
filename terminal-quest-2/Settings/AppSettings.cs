namespace TerminalQuest.Settings
{
    /// <summary>
    /// Everything the player can change about how the game reaches a model.
    /// </summary>
    /// <remarks>
    /// Every provider's fields are kept, not just the selected one's, so switching back and forth
    /// does not cost the player the model name, address, and key they already typed. A plain class
    /// with settable properties for the same reason as the save documents: it is what the source
    /// generator serializes without reflection.
    /// </remarks>
    internal sealed class AppSettings
    {
        /// <summary>Which provider a new session is built against.</summary>
        public AgentProvider Provider { get; set; } = AgentProvider.ClaudeCode;

        /// <summary>
        /// The Claude model, by id or alias. Empty leaves the choice to whatever the CLI is
        /// configured to use.
        /// </summary>
        public string ClaudeModel { get; set; } = DefaultClaudeModel;

        /// <summary>The Claude model for the Director. Empty uses whatever is configured for the narrator.</summary>
        public string DirectorClaudeModel { get; set; } = string.Empty;

        /// <summary>Google (Gemini) endpoint, model, and credential.</summary>
        public OpenAiEndpointConfig Google { get; set; } = OpenAiEndpointConfig.ForPreset(OpenAiPresets.Google);

        /// <summary>OpenAI endpoint, model, and credential.</summary>
        public OpenAiEndpointConfig OpenAI { get; set; } = OpenAiEndpointConfig.ForPreset(OpenAiPresets.OpenAI);

        /// <summary>Anthropic (Claude via compatibility gateway) endpoint, model, and credential.</summary>
        public OpenAiEndpointConfig Anthropic { get; set; } = OpenAiEndpointConfig.ForPreset(OpenAiPresets.Anthropic);

        /// <summary>Manually configured endpoint, model, and credential (LM Studio, Ollama, vLLM, Jan, etc.).</summary>
        public OpenAiEndpointConfig Custom { get; set; } = OpenAiEndpointConfig.ForPreset(OpenAiPresets.Custom);

        /// <summary>
        /// The program Ctrl+G hands a text field's contents to. May carry fixed arguments, as in
        /// <c>code -w</c>.
        /// </summary>
        /// <remarks>
        /// A windowed editor is what this is for: it opens beside the game and cannot disturb the
        /// screen. A terminal editor - <c>vim</c>, <c>nano</c> - inherits this console and draws over
        /// the game while it runs; the screen is repainted when it exits, but that repair is the most
        /// that is promised.
        /// </remarks>
        public string EditorCommand { get; set; } = DefaultEditorCommand;

        /// <summary>
        /// How much of the last session, in characters of prose, a resumed save recalls word for word.
        /// </summary>
        /// <remarks>
        /// One number for two consumers on purpose: it sizes both the block drawn on screen when the
        /// save opens and what <c>get_transcript</c> hands the narrator. Splitting them would let the
        /// player read further back than the narrator can remember, or the reverse, and either way the
        /// two would be talking about scenes the other had not seen.
        /// <para>
        /// A preference rather than a constant because the trade is genuinely the player's: recall
        /// competes with the world state for the narrator's context, and what it buys - continuity of
        /// voice - is worth more in some campaigns than others.
        /// </para>
        /// </remarks>
        public int TranscriptRecallCharacters { get; set; } = Saves.TranscriptRecall.DefaultCharacters;

        /// <summary>Small and fast, which is what a turn of narration wants.</summary>
        /// <remarks>
        /// The undated alias rather than a pinned snapshot, so the settings screen can offer it as
        /// one of a short list of names and a file written today still matches a build shipped
        /// after the next snapshot lands.
        /// </remarks>
        public const string DefaultClaudeModel = "claude-haiku-4-5";

        /// <summary>Where a manual server listens unless configured otherwise (defaults to LM Studio).</summary>
        public const string DefaultCustomBaseUrl = "http://localhost:1234/v1";

        /// <summary>
        /// Present on every Windows install, so Ctrl+G works without anyone having to configure
        /// anything first, and windowed rather than terminal-based, which is the kind that leaves the
        /// game's own screen alone.
        /// </summary>
        public const string DefaultEditorCommand = "notepad.exe";

        /// <summary>Whether the provider is reached over an OpenAI-compatible HTTP API.</summary>
        public static bool IsOpenAiProvider(AgentProvider provider) => provider switch
        {
            AgentProvider.Google => true,
            AgentProvider.OpenAI => true,
            AgentProvider.Anthropic => true,
            AgentProvider.Custom => true,
            _ => false,
        };

        /// <summary>
        /// The provider with the legacy aggregate resolved: old files that said "OpenAI API" name a
        /// preset beside it, and the preset decides. Without any context it behaves as Custom.
        /// </summary>
        public static AgentProvider EffectiveProvider(AgentProvider provider) => provider switch
        {
#pragma warning disable CS0612, CS0618 // Intentional: legacy value support.
            AgentProvider.OpenAiApi => AgentProvider.Custom,
#pragma warning restore CS0612, CS0618
            AgentProvider.Google => provider,
            AgentProvider.OpenAI => provider,
            AgentProvider.Anthropic => provider,
            AgentProvider.Custom => provider,
            _ => AgentProvider.ClaudeCode,
        };

        /// <summary>The endpoint slot for a provider. Claude Code has no endpoint; it reads Custom.</summary>
        public OpenAiEndpointConfig EndpointFor(AgentProvider provider) => EffectiveProvider(provider) switch
        {
            AgentProvider.Google => Google,
            AgentProvider.OpenAI => OpenAI,
            AgentProvider.Anthropic => Anthropic,
            _ => Custom,
        };

        /// <summary>Root of the active OpenAI-compatible API, endpoint paths excluded.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string ActiveBaseUrl => AppSettings.NormalizeBaseUrl(EndpointFor(Provider).BaseUrl);

        /// <summary>The active model id, exactly as the server lists it.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string ActiveModel => EndpointFor(Provider).Model?.Trim() ?? string.Empty;

        /// <summary>The active Director model id, falling back to the narrator's.</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string ActiveDirectorModel
        {
            get
            {
                var endpoint = EndpointFor(Provider);
                return string.IsNullOrWhiteSpace(endpoint.DirectorModel) ? ActiveModel : endpoint.DirectorModel.Trim();
            }
        }

        /// <summary>
        /// The active credential in usable form. A <c>TQ2_*</c> environment variable wins when set
        /// and is never written anywhere; otherwise the stored key.
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string ActiveApiKey => GetEnvironmentApiKey(Provider) ?? EndpointFor(Provider).ResolveApiKey();

        /// <summary>A key from the environment when the player keeps it out of the file. Never persisted.</summary>
        public static string? GetEnvironmentApiKey(AgentProvider provider)
        {
            var name = EffectiveProvider(provider) switch
            {
                AgentProvider.Google => "TQ2_GOOGLE_API_KEY",
                AgentProvider.OpenAI => "TQ2_OPENAI_API_KEY",
                AgentProvider.Anthropic => "TQ2_ANTHROPIC_API_KEY",
                _ => "TQ2_CUSTOM_API_KEY",
            };

            return Environment.GetEnvironmentVariable(name) is { Length: > 0 } value ? value : null;
        }

        /// <summary>
        /// Whether a string is somewhere the game could actually send a request.
        /// </summary>
        /// <remarks>
        /// Here rather than on the settings screen for the same reason as
        /// <see cref="Saves.SavePaths.IsValidName"/>: what counts as a usable value is a property
        /// of the setting, not of the screen that happens to collect it. The screen checks it
        /// twice - as the player leaves the field, and again before writing - and both call this.
        /// </remarks>
        public static bool IsAddress(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        /// <summary>
        /// Normalizes an OpenAI-compatible API base URL by ensuring it trims trailing slashes
        /// and appends /v1 if no subpath was provided on a root host address.
        /// </summary>
        public static string NormalizeBaseUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return DefaultCustomBaseUrl;
            }

            var trimmed = url.Trim().TrimEnd('/');
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                if (string.IsNullOrEmpty(uri.AbsolutePath) || uri.AbsolutePath == "/")
                {
                    return $"{trimmed}/v1";
                }
            }

            return trimmed;
        }

        /// <summary>Takes every value from another instance.</summary>
        /// <remarks>
        /// The settings screen edits a copy and the host adopts it wholesale once the player
        /// commits. Doing that here rather than field-by-field at the call site means a setting
        /// added later cannot be silently left behind by a copy block nobody remembered to update.
        /// </remarks>
        public void CopyFrom(AppSettings other)
        {
            ArgumentNullException.ThrowIfNull(other);

            Provider = other.Provider;
            ClaudeModel = other.ClaudeModel;
            DirectorClaudeModel = other.DirectorClaudeModel;
            Google = other.Google with { };
            OpenAI = other.OpenAI with { };
            Anthropic = other.Anthropic with { };
            Custom = other.Custom with { };
            EditorCommand = other.EditorCommand;
            TranscriptRecallCharacters = other.TranscriptRecallCharacters;
        }

        /// <summary>Ensures string properties and bounds are valid and never null.</summary>
        public void Normalize()
        {
#pragma warning disable CS0612, CS0618 // Intentional: legacy value support.
            if (Provider == AgentProvider.OpenAiApi)
            {
                Provider = AgentProvider.Custom;
            }
#pragma warning restore CS0612, CS0618
            if (!Enum.IsDefined(Provider))
            {
                Provider = AgentProvider.ClaudeCode;
            }

            ClaudeModel ??= string.Empty;
            DirectorClaudeModel ??= string.Empty;
            Google ??= OpenAiEndpointConfig.ForPreset(OpenAiPresets.Google);
            OpenAI ??= OpenAiEndpointConfig.ForPreset(OpenAiPresets.OpenAI);
            Anthropic ??= OpenAiEndpointConfig.ForPreset(OpenAiPresets.Anthropic);
            Custom ??= OpenAiEndpointConfig.ForPreset(OpenAiPresets.Custom);
            Google.Normalize();
            OpenAI.Normalize();
            Anthropic.Normalize();
            Custom.Normalize();
            EditorCommand = string.IsNullOrWhiteSpace(EditorCommand) ? DefaultEditorCommand : EditorCommand.Trim();
            if (TranscriptRecallCharacters < Saves.TranscriptRecall.MinCharacters || TranscriptRecallCharacters > Saves.TranscriptRecall.MaxCharacters)
            {
                TranscriptRecallCharacters = Saves.TranscriptRecall.DefaultCharacters;
            }
        }

        /// <summary>
        /// Migrates a file written before providers were split: the old preset name decides the
        /// provider, and the old shared address, model, and key move into that provider's slot.
        /// New-shape values always win; this only fills what the new shape left default.
        /// </summary>
        internal void MigrateLegacy(LegacyAppSettings legacy)
        {
            if (legacy is null)
            {
                return;
            }

            var target = OpenAiPresets.ProviderForPreset(legacy.OpenAiPreset);
            var slot = EndpointFor(target);
            var pristine = OpenAiEndpointConfig.ForPreset(OpenAiPresets.ForProvider(target));

            if (legacy.LmStudioBaseUrl is { Length: > 0 } url && slot.BaseUrl == pristine.BaseUrl)
            {
                slot.BaseUrl = NormalizeBaseUrl(url);
            }

            if (legacy.LmStudioModel is not null && slot.Model == pristine.Model)
            {
                slot.Model = legacy.LmStudioModel.Trim();
            }

            if (legacy.DirectorLmStudioModel is not null && string.IsNullOrEmpty(slot.DirectorModel))
            {
                slot.DirectorModel = legacy.DirectorLmStudioModel.Trim();
            }

            if (legacy.LmStudioApiKey is { Length: > 0 } key && string.IsNullOrEmpty(slot.ApiKey))
            {
                slot.SetApiKey(key);
            }

#pragma warning disable CS0612, CS0618 // Intentional: legacy value support.
            if (Provider == AgentProvider.OpenAiApi)
            {
                Provider = target;
            }
#pragma warning restore CS0612, CS0618
        }
    }
}
