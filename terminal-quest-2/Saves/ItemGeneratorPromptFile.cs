namespace TerminalQuest.Saves
{
    /// <summary>
    /// Prompts used to generate custom inventory items via LLM.
    /// </summary>
    internal static class ItemGeneratorPromptFile
    {
        private const string AssetRelativePath = "assets/item-generator-prompt.txt";

        public static string DefaultPrompt => field ??= LoadAsset(AssetRelativePath);

        private static string LoadAsset(string relativePath)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (File.Exists(path))
            {
                var content = File.ReadAllText(path);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    return content;
                }
            }

            return FallbackPrompt;
        }

        public static string Compose(string summary, string aptitude)
        {
            var prompt = DefaultPrompt;
            var cleanSummary = string.IsNullOrWhiteSpace(summary) ? "A versatile custom adventurer." : summary.Trim();
            var cleanAptitude = string.IsNullOrWhiteSpace(aptitude) ? "A skilled combatant and problem solver." : aptitude.Trim();

            return prompt
                .Replace("{{SUMMARY}}", cleanSummary, StringComparison.Ordinal)
                .Replace("{{APTITUDE}}", cleanAptitude, StringComparison.Ordinal);
        }

        public const string FallbackPrompt =
            "You are an equipment master for a grounded, atmospheric fantasy text RPG.\n" +
            "Your task is to generate a diverse, evocative set of starting inventory items tailored specifically to the character archetype described below.\n\n" +
            "### CHARACTER CONTEXT\n" +
            "Summary: {{SUMMARY}}\n" +
            "Aptitude: {{APTITUDE}}\n\n" +
            "### INSTRUCTIONS\n" +
            "Generate starting gear options tailored to this archetype's skills, theme, and background.\n" +
            "1. Create exactly 4 Weapons suited to their combat style or background.\n" +
            "2. Create exactly 4 Offhand Items.\n" +
            "3. Create exactly 4 Special Items.\n\n" +
            "### RESPONSE FORMAT\n" +
            "Return ONLY valid JSON matching this schema with no additional commentary:\n" +
            "{\n" +
            "  \"weapons\": [\n" +
            "    { \"name\": \"Item Name\", \"description\": \"One concise sentence describing its appearance and condition.\" }\n" +
            "  ],\n" +
            "  \"offhands\": [\n" +
            "    { \"name\": \"Item Name\", \"description\": \"One concise sentence describing its appearance and condition.\" }\n" +
            "  ],\n" +
            "  \"specials\": [\n" +
            "    { \"name\": \"Item Name\", \"description\": \"One concise sentence describing its appearance and condition.\" }\n" +
            "  ]\n" +
            "}";
    }
}
