namespace TerminalQuest.Settings
{
    /// <summary>
    /// Everything the player can change about how the game reaches a model.
    /// </summary>
    /// <remarks>
    /// Every provider's fields are kept, not just the selected one's, so switching back and forth
    /// does not cost the player the model name and address they already typed. A plain class with
    /// settable properties for the same reason as the save documents: it is what the source
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

        /// <summary>The selected preset for OpenAI API (Google, OpenAI, Anthropic, Custom).</summary>
        public string OpenAiPreset { get; set; } = OpenAiPresets.Custom.Name;

        /// <summary>Root of the OpenAI-compatible API, endpoint paths excluded.</summary>
        public string LmStudioBaseUrl { get; set; } = DefaultLmStudioBaseUrl;

        /// <summary>The model id, exactly as the server lists it. Empty means whatever is loaded.</summary>
        public string LmStudioModel { get; set; } = string.Empty;

        /// <summary>The model id for the Director. Empty uses whatever is configured for the narrator.</summary>
        public string DirectorLmStudioModel { get; set; } = string.Empty;

        /// <summary>
        /// Bearer token. Only needed once the endpoint requires authentication (e.g. Google, OpenAI, Anthropic, or configured LM Studio).
        /// </summary>
        public string LmStudioApiKey { get; set; } = DefaultLmStudioApiKey;

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

        /// <summary>Where the OpenAI-compatible server listens unless configured otherwise (defaults to LM Studio).</summary>
        public const string DefaultOpenAiBaseUrl = DefaultLmStudioBaseUrl;

        /// <summary>The default API key placeholder.</summary>
        public const string DefaultOpenAiApiKey = DefaultLmStudioApiKey;

        /// <summary>Where LM Studio's server listens unless it has been told otherwise.</summary>
        public const string DefaultLmStudioBaseUrl = "http://localhost:1234/v1";

        /// <summary>
        /// The placeholder LM Studio's own examples use, which is right for a server that has not
        /// been told to check. One that has needs the real token pasted in.
        /// </summary>
        public const string DefaultLmStudioApiKey = "lm-studio";

        /// <summary>
        /// Present on every Windows install, so Ctrl+G works without anyone having to configure
        /// anything first, and windowed rather than terminal-based, which is the kind that leaves the
        /// game's own screen alone.
        /// </summary>
        public const string DefaultEditorCommand = "notepad.exe";

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
            OpenAiPreset = other.OpenAiPreset;
            LmStudioBaseUrl = other.LmStudioBaseUrl;
            LmStudioModel = other.LmStudioModel;
            DirectorLmStudioModel = other.DirectorLmStudioModel;
            LmStudioApiKey = other.LmStudioApiKey;
            EditorCommand = other.EditorCommand;
            TranscriptRecallCharacters = other.TranscriptRecallCharacters;
        }

        /// <summary>Ensures string properties and bounds are valid and never null.</summary>
        public void Normalize()
        {
            ClaudeModel ??= string.Empty;
            DirectorClaudeModel ??= string.Empty;
            OpenAiPreset = string.IsNullOrWhiteSpace(OpenAiPreset) ? OpenAiPresets.Custom.Name : OpenAiPreset.Trim();
            LmStudioBaseUrl = string.IsNullOrWhiteSpace(LmStudioBaseUrl) ? DefaultLmStudioBaseUrl : LmStudioBaseUrl.Trim();
            LmStudioModel ??= string.Empty;
            DirectorLmStudioModel ??= string.Empty;
            LmStudioApiKey = string.IsNullOrWhiteSpace(LmStudioApiKey) ? DefaultLmStudioApiKey : LmStudioApiKey.Trim();
            EditorCommand = string.IsNullOrWhiteSpace(EditorCommand) ? DefaultEditorCommand : EditorCommand.Trim();
            if (TranscriptRecallCharacters < Saves.TranscriptRecall.MinCharacters || TranscriptRecallCharacters > Saves.TranscriptRecall.MaxCharacters)
            {
                TranscriptRecallCharacters = Saves.TranscriptRecall.DefaultCharacters;
            }
        }
    }
}
