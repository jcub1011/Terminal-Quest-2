namespace TerminalQuest.Settings
{
    /// <summary>
    /// Everything the game needs to reach one OpenAI-compatible provider: its endpoint, the model
    /// to ask for, and the credential. Each provider keeps its own, so switching back and forth
    /// never costs the player what they already typed.
    /// </summary>
    /// <remarks>
    /// <see cref="ApiKey"/> is stored sealed (see <see cref="ApiKeyProtection"/>) and only ever
    /// plaintext in memory while the settings screen holds it. A plain class with settable
    /// properties for the same reason as the save documents: it is what the source generator
    /// serializes without reflection.
    /// </remarks>
    internal sealed record OpenAiEndpointConfig
    {
        /// <summary>Root of the OpenAI-compatible API, endpoint paths excluded.</summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>The model id, exactly as the server lists it. Empty means whatever is loaded.</summary>
        public string Model { get; set; } = string.Empty;

        /// <summary>The model id for the Director. Empty uses whatever is configured for the narrator.</summary>
        public string DirectorModel { get; set; } = string.Empty;

        /// <summary>Bearer token in storage form. Only needed once the endpoint requires authentication.</summary>
        public string ApiKey { get; set; } = string.Empty;

        /// <summary>The config a provider starts from: its built-in endpoint and default model.</summary>
        public static OpenAiEndpointConfig ForPreset(OpenAiPreset preset)
        {
            ArgumentNullException.ThrowIfNull(preset);

            return new OpenAiEndpointConfig
            {
                BaseUrl = preset.BaseUrl,
                Model = preset.DefaultModel,
            };
        }

        /// <summary>The credential in usable form. Empty when none is stored.</summary>
        public string ResolveApiKey() => ApiKeyProtection.Unprotect(ApiKey);

        /// <summary>Stores a freshly typed credential, sealing it on the way in.</summary>
        public void SetApiKey(string? plaintext) => ApiKey = ApiKeyProtection.Protect(plaintext?.Trim());

        /// <summary>Ensures string properties are valid and the key is in storage form, never null.</summary>
        public void Normalize()
        {
            BaseUrl = AppSettings.NormalizeBaseUrl(BaseUrl);
            Model = Model?.Trim() ?? string.Empty;
            DirectorModel = DirectorModel?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(ApiKey))
            {
                ApiKey = string.Empty;
            }
            else if (!ApiKeyProtection.LooksProtected(ApiKey))
            {
                // Legacy plaintext or a hand edit: seal it now so the next write is protected.
                ApiKey = ApiKeyProtection.Protect(ApiKey.Trim());
            }
        }
    }
}
