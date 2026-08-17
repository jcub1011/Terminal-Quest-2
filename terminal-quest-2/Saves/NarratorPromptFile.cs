namespace TerminalQuest.Saves
{
    /// <summary>
    /// The narrator's brief, and where each save keeps its own copy of it.
    /// </summary>
    internal static class NarratorPromptFile
    {
        private const string AssetRelativePath = "assets/narrator-prompt.md";

        public const string FileName = "system-prompt.txt";

        public static string Default => field ??= LoadDefault();

        private static string LoadDefault()
        {
            var path = Path.Combine(AppContext.BaseDirectory, AssetRelativePath);
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Required narrator prompt file '{AssetRelativePath}' was not found at '{path}'. " +
                    "Please obtain a replacement from the repository.",
                    path);
            }

            var content = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(content))
            {
                throw new InvalidOperationException(
                    $"Required narrator prompt file at '{path}' is empty. " +
                    "Please obtain a replacement from the repository.");
            }

            return content;
        }

        /// <summary>
        /// Past which a prompt is worth warning about.
        /// </summary>
        public const int WarnAboveCharacters = 24_000;

        /// <summary>
        /// This save's prompt: what is in the file, or <see cref="Default"/> when there is nothing
        /// usable there.
        /// </summary>
        public static string Read(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            return store.ReadSystemPrompt() is { } text && !text.AsSpan().IsWhiteSpace()
                ? text
                : Default;
        }

        /// <summary>
        /// Makes sure the save has a prompt file, and returns the prompt it now holds.
        /// </summary>
        public static string Ensure(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            if (store.ReadSystemPrompt() is { } existing && !existing.AsSpan().IsWhiteSpace())
            {
                return existing;
            }

            var seeded = Default.ReplaceLineEndings();
            store.WriteSystemPrompt(seeded);
            return seeded;
        }
    }
}
