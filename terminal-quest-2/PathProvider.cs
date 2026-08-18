namespace TerminalQuest
{
    /// <summary>
    /// Central authority for resolving persisted paths on disk (saves, settings, scratch).
    /// </summary>
    internal static class PathProvider
    {
        private const string RootVariable = "TQ_SAVES";

        /// <summary>
        /// The persisted root folder. Defaults to <see cref="AppDirectory.Root"/> unless
        /// <c>TQ_SAVES</c> overrides it.
        /// </summary>
        public static string Root
        {
            get
            {
                if (Environment.GetEnvironmentVariable(RootVariable) is { Length: > 0 } configured)
                {
                    return Path.GetFullPath(configured);
                }

                return AppDirectory.Root;
            }
        }

        /// <summary>Subfolder where save games are stored.</summary>
        public static string Saves => Path.Combine(Root, "Saves");

        /// <summary>Subfolder where settings are stored. Fixed under <see cref="AppDirectory.Root"/>.</summary>
        public static string Settings => Path.Combine(AppDirectory.Root, "Settings");

        /// <summary>
        /// Migrates legacy saves and settings to the new directory layout if needed.
        /// Safe to call multiple times (idempotent).
        /// </summary>
        public static void EnsureMigrated()
        {
            Migrate(AppDirectory.Root);
            if (!string.Equals(Root, AppDirectory.Root, StringComparison.OrdinalIgnoreCase))
            {
                Migrate(Root);
            }
        }

        /// <summary>
        /// Migrates legacy saves and settings within a specific root directory.
        /// </summary>
        internal static void Migrate(string root)
        {
            ArgumentNullException.ThrowIfNull(root);

            try
            {
                if (!Directory.Exists(root))
                {
                    return;
                }

                var savesDir = Path.Combine(root, "Saves");
                var settingsDir = Path.Combine(root, "Settings");

                // 1. Migrate settings.json from root to Settings/settings.json
                var oldSettingsFile = Path.Combine(root, "settings.json");
                var newSettingsFile = Path.Combine(settingsDir, "settings.json");

                if (File.Exists(oldSettingsFile))
                {
                    Directory.CreateDirectory(settingsDir);
                    if (!File.Exists(newSettingsFile))
                    {
                        File.Move(oldSettingsFile, newSettingsFile);
                    }
                    else if (!string.Equals(oldSettingsFile, newSettingsFile, StringComparison.OrdinalIgnoreCase))
                    {
                        try { File.Delete(oldSettingsFile); } catch { }
                    }
                }

                // 2. Migrate unnested save directories in root to Saves/
                foreach (var dir in Directory.EnumerateDirectories(root))
                {
                    var dirName = Path.GetFileName(dir);
                    if (string.Equals(dirName, "Saves", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "Settings", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(dirName, "edit", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var hasSaveJson = File.Exists(Path.Combine(dir, "save.json"));
                    var isValidName = TerminalQuest.Saves.SavePaths.IsValidName(dirName);

                    if (hasSaveJson || isValidName)
                    {
                        Directory.CreateDirectory(savesDir);
                        var targetDir = Path.Combine(savesDir, dirName);
                        if (!Directory.Exists(targetDir))
                        {
                            Directory.Move(dir, targetDir);
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort migration: failures should not prevent the app from attempting to run
            }
        }
    }
}
