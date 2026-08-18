namespace TerminalQuest.Saves
{
    /// <summary>
    /// The narrator's instructions: tool/engine contract loaded dynamically from assets,
    /// and story/voice persona kept per-save.
    /// </summary>
    internal static class NarratorPromptFile
    {
        private const string ToolsAssetRelativePath = "assets/narrator-tools.md";
        private const string StoryAssetRelativePath = "assets/narrator-story.md";

        public const string FileName = "narrator-story.txt";
        public const string LegacyFileName = "system-prompt.txt";

        public static string ToolsDefault => field ??= LoadAsset(ToolsAssetRelativePath);

        public static string StoryDefault => field ??= LoadAsset(StoryAssetRelativePath);

        public static string Default => StoryDefault;

        private static string LoadAsset(string relativePath)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Required narrator prompt asset file '{relativePath}' was not found at '{path}'. " +
                    "Please obtain a replacement from the repository.",
                    path);
            }

            var content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException(
                    $"Required narrator prompt asset file at '{path}' is empty. " +
                    "Please obtain a replacement from the repository.");
            }

            return content;
        }

        /// <summary>
        /// Past which a prompt is worth warning about.
        /// </summary>
        public const int WarnAboveCharacters = 24_000;

        /// <summary>
        /// This save's story prompt: what is in the save file, or <see cref="StoryDefault"/> when empty.
        /// </summary>
        public static string Read(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            if (store.ReadNarratorStory() is { } story && !story.AsSpan().IsWhiteSpace())
            {
                return story;
            }

            if (store.ReadSystemPrompt() is { } legacy && !legacy.AsSpan().IsWhiteSpace())
            {
                return legacy;
            }

            return StoryDefault;
        }

        /// <summary>
        /// Makes sure the save has a narrator story file, migrating legacy file if present, and returns what it holds.
        /// </summary>
        public static string Ensure(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            if (store.ReadNarratorStory() is { } existing && !existing.AsSpan().IsWhiteSpace())
            {
                return existing;
            }

            if (store.ReadSystemPrompt() is { } legacy && !legacy.AsSpan().IsWhiteSpace())
            {
                store.WriteNarratorStory(legacy);
                return legacy;
            }

            var seeded = StoryDefault.ReplaceLineEndings();
            store.WriteNarratorStory(seeded);
            return seeded;
        }

        /// <summary>
        /// Overwrites the save's story prompt with the current asset default.
        /// </summary>
        public static string UpdateStory(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            var seeded = StoryDefault.ReplaceLineEndings();
            store.WriteNarratorStory(seeded);
            return seeded;
        }

        /// <summary>
        /// Composes the complete prompt delivered to the narrator agent session (fresh tools asset + save's story prompt).
        /// </summary>
        public static string Compose(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);
            return Compose(ToolsDefault, Read(store));
        }

        /// <summary>
        /// Combines tool instructions and story instructions into a complete prompt.
        /// </summary>
        public static string Compose(string tools, string story)
        {
            var trimmedTools = tools?.Trim() ?? string.Empty;
            var trimmedStory = story?.Trim() ?? string.Empty;

            if (trimmedStory.Length == 0)
            {
                return trimmedTools;
            }

            if (trimmedTools.Length == 0)
            {
                return trimmedStory;
            }

            return $"{trimmedStory}{Environment.NewLine}{Environment.NewLine}---{Environment.NewLine}{Environment.NewLine}{trimmedTools}";
        }
    }
}
