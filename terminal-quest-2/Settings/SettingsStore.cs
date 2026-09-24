using System.Text;
using System.Text.Json;

using TerminalQuest.Saves;

namespace TerminalQuest.Settings
{
    /// <summary>
    /// Reads and writes <c>settings.json</c>, next to the saves folder rather than inside it: a
    /// preference outlives any one playthrough and must not be deleted with one.
    /// </summary>
    internal static class SettingsStore
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        /// <summary>Where the file lives.</summary>
        public static string Path => System.IO.Path.Combine(PathProvider.Settings, "settings.json");

        /// <summary>
        /// The stored settings, or the defaults.
        /// </summary>
        /// <remarks>
        /// Never throws. A settings file that is missing, unreadable or malformed must not stop the
        /// game starting - unlike a save, nothing in here is the player's work, and the defaults are
        /// a working configuration. They can see what went wrong on the settings screen, which is
        /// where they would go to fix it anyway.
        /// </remarks>
        public static AppSettings Read()
        {
            PathProvider.EnsureMigrated();
            return Read(Path);
        }

        /// <summary>
        /// The same, from a named file.
        /// </summary>
        /// <remarks>
        /// This overload is the seam that lets the recovery behaviour above be checked without writing to the real profile.
        /// </remarks>
        internal static AppSettings Read(string path)
        {
            ArgumentNullException.ThrowIfNull(path);

            try
            {
                if (!File.Exists(path))
                {
                    return new AppSettings();
                }

                var text = File.ReadAllText(path, Utf8NoBom);

                if (text.AsSpan().IsWhiteSpace())
                {
                    return new AppSettings();
                }

                var settings = JsonSerializer.Deserialize(text, SettingsJsonContext.Default.AppSettings);
                if (settings is null)
                {
                    return new AppSettings();
                }

                // A file from before providers were split carries its address, model, and key
                // under the old shared names. Harvest them separately: the new shape ignores
                // unknown properties, so without this they would be dropped unread.
                try
                {
                    var legacy = JsonSerializer.Deserialize(text, SettingsJsonContext.Default.LegacyAppSettings);
                    if (legacy?.HasValues == true)
                    {
                        settings.MigrateLegacy(legacy);
                    }
                }
                catch (JsonException)
                {
                    // The main document already parsed; a legacy section that does not parse is
                    // simply not migrated.
                }

                settings.Normalize();
                return settings;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                return new AppSettings();
            }
        }

        /// <summary>Stores the settings, replacing whatever was there.</summary>
        /// <exception cref="SaveException">The file could not be written.</exception>
        public static void Write(AppSettings settings) => Write(settings, Path);

        /// <summary>The same, to a named file. The seam described on <see cref="Read(string)"/>.</summary>
        /// <exception cref="SaveException">The file could not be written.</exception>
        internal static void Write(AppSettings settings, string path)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(path);

            try
            {
                var folder = System.IO.Path.GetDirectoryName(path);

                if (folder is { Length: > 0 })
                {
                    Directory.CreateDirectory(folder);
                }

                var temporary = $"{path}.tmp";
                File.WriteAllText(
                    temporary,
                    JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings),
                    Utf8NoBom);
                File.Move(temporary, path, overwrite: true);
                RestrictToCurrentUser(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                throw new SaveException($"Could not write settings: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Locks the settings file down to the current user, since it holds sealed API keys.
        /// Best effort and Windows-only: it must never break a save, and elsewhere the file
        /// keeps the platform defaults.
        /// </summary>
        private static void RestrictToCurrentUser(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                var info = new FileInfo(path);
                var security = info.GetAccessControl();
                security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                security.SetOwner(System.Security.Principal.WindowsIdentity.GetCurrent().User!);
                security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    System.Security.Principal.WindowsIdentity.GetCurrent().User!,
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));
                info.SetAccessControl(security);
            }
            catch
            {
                // A file with default permissions still works; DPAPI keeps another user from
                // unsealing the keys inside it.
            }
        }
    }
}
