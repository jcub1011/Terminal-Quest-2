namespace TerminalQuest.Saves
{
    /// <summary>
    /// Where saves live on disk, and the rules for naming one.
    /// <para>
    /// A save is nothing more than a named folder holding the documents that belong to it, so
    /// creating one is a <c>mkdir</c> and listing them is an enumeration. There is no index file
    /// to fall out of step with what is actually on disk.
    /// </para>
    /// </summary>
    internal static class SavePaths
    {
        /// <summary>Overrides <see cref="Root"/>, mirroring the <c>TQ_DRIVER</c> convention.</summary>
        private const string RootVariable = "TQ_SAVES";

        /// <summary>
        /// The saves directory. <c>%APPDATA%\TerminalQuest\Saves</c> unless <c>TQ_SAVES</c> says
        /// otherwise.
        /// </summary>
        public static string Root
        {
            get
            {
                if (Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } configured)
                {
                    return Path.GetFullPath(configured);
                }

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                // GetFolderPath returns empty rather than throwing when the profile is unavailable.
                // Falling back beside the executable keeps a portable copy of the game playable.
                if (appData.Length == 0)
                {
                    appData = AppContext.BaseDirectory;
                }

                return Path.Combine(appData, "TerminalQuest", "Saves");
            }
        }

        /// <summary>Existing saves, most recently played first.</summary>
        /// <remarks>
        /// A folder with unreadable or missing metadata is still listed - it is a save that needs
        /// looking at, not one to hide from the menu.
        /// </remarks>
        public static IReadOnlyList<SaveMetadata> List()
        {
            var root = Root;
            if (!Directory.Exists(root))
            {
                return [];
            }

            var saves = new List<SaveMetadata>();

            foreach (var directory in Directory.EnumerateDirectories(root))
            {
                var name = Path.GetFileName(directory);
                if (name.Length == 0)
                {
                    continue;
                }

                var store = new SaveStore(directory);
                var metadata = store.ReadMetadata();
                metadata.Name = name;
                saves.Add(metadata);
            }

            saves.Sort(static (left, right) => right.LastPlayed.CompareTo(left.LastPlayed));
            return saves;
        }

        /// <summary>Whether a save folder of this name already exists.</summary>
        public static bool Exists(string name) =>
            IsValidName(name) && Directory.Exists(Path.Combine(Root, name.Trim()));

        /// <summary>Opens an existing save, creating and stamping it when it is new.</summary>
        public static SaveStore Open(string name)
        {
            if (!IsValidName(name))
            {
                throw new ArgumentException($"'{name}' is not a usable save name.", nameof(name));
            }

            var directory = Path.Combine(Root, name.Trim());
            var isNew = !Directory.Exists(directory);

            Directory.CreateDirectory(directory);

            var store = new SaveStore(directory);

            if (isNew)
            {
                var now = DateTimeOffset.Now;
                store.WriteMetadata(new SaveMetadata
                {
                    Name = name.Trim(),
                    Created = now,
                    LastPlayed = now,
                    Turn = 0,
                });
            }

            return store;
        }

        /// <summary>
        /// Whether a name can be a folder. Rejects path separators and reserved characters rather
        /// than silently rewriting them, so the name in the menu is always the name on disk.
        /// </summary>
        public static bool IsValidName(string? name)
        {
            if (name is null)
            {
                return false;
            }

            var trimmed = name.Trim();

            return trimmed.Length is > 0 and <= 64
                && trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
                && trimmed is not ("." or "..");
        }
    }
}
