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
        bool IsCustom = false);

    /// <summary>
    /// Predefined provider presets (Google, OpenAI, Anthropic, Custom).
    /// </summary>
    internal static class OpenAiPresets
    {
        public static readonly OpenAiPreset Google = new(
            "Google",
            "https://generativelanguage.googleapis.com/v1beta/openai",
            "gemini-2.0-flash",
            "Google AI Studio (Gemini 2.0 Flash / Pro). Get a free API key at aistudio.google.com");

        public static readonly OpenAiPreset OpenAI = new(
            "OpenAI",
            "https://api.openai.com/v1",
            "gpt-4o-mini",
            "OpenAI API (GPT-4o, GPT-4o-mini). Get an API key at platform.openai.com");

        public static readonly OpenAiPreset Anthropic = new(
            "Anthropic",
            "https://api.anthropic.com/v1",
            "claude-3-5-sonnet-20241022",
            "Anthropic API (Claude via compatibility gateway). Get an API key at console.anthropic.com");

        public static readonly OpenAiPreset Custom = new(
            "Custom",
            "http://localhost:1234/v1",
            string.Empty,
            "Local or custom server (LM Studio, Ollama, vLLM, Jan, etc.).",
            IsCustom: true);

        public static readonly OpenAiPreset[] All = [Google, OpenAI, Anthropic, Custom];

        public static OpenAiPreset FindByName(string? name) =>
            All.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)) ?? Custom;

        public static OpenAiPreset DetectPreset(string? baseUrl)
        {
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return Custom;
            }

            var trimmed = baseUrl.TrimEnd('/');
            return All.FirstOrDefault(p => !p.IsCustom && string.Equals(p.BaseUrl.TrimEnd('/'), trimmed, StringComparison.OrdinalIgnoreCase))
                ?? Custom;
        }
    }
}
