namespace TerminalQuest.Settings
{
    /// <summary>
    /// The fields a settings file carried before providers were split into per-provider slots.
    /// </summary>
    /// <remarks>
    /// Read-only migration source: the new shape never writes these. Every property is nullable
    /// so a new-shape file (which lacks them all) reads as "nothing to migrate" rather than as
    /// empty values that would overwrite real configuration.
    /// </remarks>
    internal sealed class LegacyAppSettings
    {
        /// <summary>The selected preset for the old shared OpenAI API section (Google, OpenAI, Anthropic, Custom).</summary>
        public string? OpenAiPreset { get; set; }

        /// <summary>Root of the old shared OpenAI-compatible API.</summary>
        public string? LmStudioBaseUrl { get; set; }

        /// <summary>The old shared model id.</summary>
        public string? LmStudioModel { get; set; }

        /// <summary>The old shared Director model id.</summary>
        public string? DirectorLmStudioModel { get; set; }

        /// <summary>The old shared bearer token, in plaintext.</summary>
        public string? LmStudioApiKey { get; set; }

        /// <summary>Whether the file carries anything worth migrating.</summary>
        public bool HasValues =>
            OpenAiPreset is { Length: > 0 } ||
            LmStudioBaseUrl is { Length: > 0 } ||
            LmStudioModel is not null ||
            DirectorLmStudioModel is not null ||
            LmStudioApiKey is { Length: > 0 };
    }
}
