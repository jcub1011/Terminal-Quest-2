using System.ComponentModel;
using System.Diagnostics;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Shows a folder to the player in the desktop's file browser.
    /// <para>
    /// Nothing like <see cref="ExternalEditor"/>'s machinery is needed here: Explorer is a windowed
    /// program that never asks for a console, so it cannot fight the game for keystrokes and cannot
    /// leave the terminal needing repair. One call, and the game carries on drawing.
    /// </para>
    /// </summary>
    internal static class FileExplorer
    {
        /// <summary>Opens a window on <paramref name="folder"/>, and says why not when it cannot.</summary>
        /// <remarks>
        /// The folder is expected to exist already; a caller that has not checked will get a window
        /// on whatever Explorer decides that path means.
        /// </remarks>
        public static bool TryOpen(string folder, out string? reason)
        {
            reason = null;

            var info = new ProcessStartInfo
            {
                FileName = Executable(),

                // The house style for every process this game starts: no shell, and the argument
                // added on its own rather than pasted into a command line, so a save folder with a
                // space or a quote in its name needs no escaping thought about here.
                UseShellExecute = false,
            };

            info.ArgumentList.Add(folder);

            try
            {
                // Not waited for, and its exit code is not read. Explorer hands the request to the
                // desktop shell that is already running and the process we started exits at once,
                // with a code that says nothing about whether a window appeared - checking it would
                // report a failure over a folder that opened perfectly well.
                Process.Start(info)?.Dispose();
                return true;
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException
                                          or InvalidOperationException or NotSupportedException)
            {
                reason = $"Could not open the folder: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Explorer, found under the Windows directory rather than left to <c>PATH</c> - the same
        /// caution <c>COMSPEC</c> gets in <see cref="EditorCommandLine.ToStartInfo"/>, so that
        /// something else answering to the name cannot be what opens.
        /// </summary>
        private static string Executable()
        {
            const string Name = "explorer.exe";

            try
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.Windows) is { Length: > 0 } windows
                    ? Path.Combine(windows, Name)
                    : Name;
            }
            catch (ArgumentException)
            {
                return Name;
            }
        }
    }
}
