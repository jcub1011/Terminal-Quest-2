namespace TerminalQuest.Saves
{
    /// <summary>
    /// The director's instructions: tool/engine contract loaded dynamically from assets,
    /// and story/campaign persona kept per-save.
    /// </summary>
    internal static class DirectorPromptFile
    {
        private const string ToolsAssetRelativePath = "assets/director-tools.md";
        private const string StoryAssetRelativePath = "assets/director-story.md";

        public const string FileName = "director-story.txt";
        public const string LegacyFileName = "director-prompt.txt";

        public static string ToolsDefault => field ??= LoadAsset(ToolsAssetRelativePath);

        public static string StoryDefault => field ??= LoadAsset(StoryAssetRelativePath);

        public static string Default => StoryDefault;

        private static string LoadAsset(string relativePath)
        {
            var path = Path.Combine(AppContext.BaseDirectory, relativePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Required director prompt asset file '{relativePath}' was not found at '{path}'. " +
                    "Please obtain a replacement from the repository.",
                    path);
            }

            var content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException(
                    $"Required director prompt asset file at '{path}' is empty. " +
                    "Please obtain a replacement from the repository.");
            }

            return content;
        }

        /// <summary>
        /// This save's director story prompt: what is in the save file, or <see cref="StoryDefault"/> when empty.
        /// </summary>
        public static string Read(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            if (store.ReadDirectorStory() is { } story && !story.AsSpan().IsWhiteSpace())
            {
                return story;
            }

            if (store.ReadDirectorPrompt() is { } legacy && !legacy.AsSpan().IsWhiteSpace())
            {
                return legacy;
            }

            return StoryDefault;
        }

        /// <summary>
        /// Makes sure the save has a director story file, migrating legacy file if present, and returns what it holds.
        /// </summary>
        public static string Ensure(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            if (store.ReadDirectorStory() is { } existing && !existing.AsSpan().IsWhiteSpace())
            {
                return existing;
            }

            if (store.ReadDirectorPrompt() is { } legacy && !legacy.AsSpan().IsWhiteSpace())
            {
                store.WriteDirectorStory(legacy);
                return legacy;
            }

            var seeded = StoryDefault.ReplaceLineEndings();
            store.WriteDirectorStory(seeded);
            return seeded;
        }

        /// <summary>
        /// Overwrites the save's director story prompt with the current asset default.
        /// </summary>
        public static string UpdateStory(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            var seeded = StoryDefault.ReplaceLineEndings();
            store.WriteDirectorStory(seeded);
            return seeded;
        }

        /// <summary>
        /// Composes the complete prompt delivered to the director agent session (fresh tools asset + save's story prompt).
        /// </summary>
        public static string Compose(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);
            return Compose(ToolsDefault, Read(store));
        }

        /// <summary>
        /// Combines tool instructions and story instructions into a complete director prompt.
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
