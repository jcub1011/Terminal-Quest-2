namespace TerminalQuest
{
    /// <summary>
    /// The one folder everything the game keeps between runs lives under.
    /// </summary>
    /// <remarks>
    /// Saves and settings answer to different rules - a save is a playthrough and can be moved
    /// somewhere else with <c>TQ_SAVES</c>; settings are a preference and stay where the game put
    /// them - but they agree on where the game's corner of the disk is, and that agreement is the
    /// only thing here.
    /// </remarks>
    internal static class AppDirectory
    {
        /// <summary><c>%APPDATA%\TerminalQuest</c>, or beside the executable when there is no profile.</summary>
        public static string Root
        {
            get
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

                // GetFolderPath returns empty rather than throwing when the profile is unavailable.
                // Falling back beside the executable keeps a portable copy of the game playable.
                if (appData.Length == 0)
                {
                    appData = AppContext.BaseDirectory;
                }

                return Path.Combine(appData, "TerminalQuest");
            }
        }
    }
}
