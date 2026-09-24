namespace TerminalQuest.Settings
{
    /// <summary>
    /// Configuration details for an OpenAI-compatible provider preset.
    /// </summary>
    internal sealed record OpenAiPreset(
        string Name,
        string BaseUrl,
        string DefaultModel,
        string Description,
        AgentProvider Provider,
        bool IsCustom = false);

    /// <summary>
    /// The built-in OpenAI-compatible providers (Google, OpenAI, Anthropic, Custom), and the
    /// defaults each one starts from. Claude Code is not here: it drives the local CLI and has
    /// no endpoint.
    /// </summary>
    internal static class OpenAiPresets
    {
        public static readonly OpenAiPreset Google = new(
            "Google",
            "https://generativelanguage.googleapis.com/v1beta/openai",
            "gemini-flash-lite-latest",
            "Google AI Studio (Gemini Flash Lite / Flash / Pro). Get a free API key at aistudio.google.com",
            AgentProvider.Google);

        public static readonly OpenAiPreset OpenAI = new(
            "OpenAI",
            "https://api.openai.com/v1",
            "gpt-4o-mini",
            "OpenAI API (GPT-4o, GPT-4o-mini). Get an API key at platform.openai.com",
            AgentProvider.OpenAI);

        public static readonly OpenAiPreset Anthropic = new(
            "Anthropic",
            "https://api.anthropic.com/v1",
            "claude-3-5-sonnet-20241022",
            "Anthropic API (Claude via compatibility gateway). Get an API key at console.anthropic.com",
            AgentProvider.Anthropic);

        public static readonly OpenAiPreset Custom = new(
            "Custom",
            "http://localhost:1234/v1",
            string.Empty,
            "Local or custom server (LM Studio, Ollama, vLLM, Jan, etc.).",
            AgentProvider.Custom,
            IsCustom: true);

        public static readonly OpenAiPreset[] All = [Google, OpenAI, Anthropic, Custom];

        public static OpenAiPreset FindByName(string? name) =>
            All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Custom;

        /// <summary>The preset (and its defaults) for an OpenAI-compatible provider.</summary>
        public static OpenAiPreset ForProvider(AgentProvider provider) =>
            All.FirstOrDefault(p => p.Provider == provider) ?? Custom;

        /// <summary>The provider an old preset name migrates to.</summary>
        public static AgentProvider ProviderForPreset(string? name) => FindByName(name).Provider;

        public static OpenAiPreset DetectPreset(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return Custom;
            }

            var trimmed = baseUrl.TrimEnd('/');
            var normalized = AppSettings.NormalizeBaseUrl(baseUrl);
            return All.FirstOrDefault(p => !p.IsCustom && (string.Equals(p.BaseUrl.TrimEnd('/'), trimmed, StringComparison.OrdinalIgnoreCase) || string.Equals(p.BaseUrl.TrimEnd('/'), normalized, StringComparison.OrdinalIgnoreCase)))
                ?? Custom;
        }
    }
}
