using System.Diagnostics;
using System.Text;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Turns the one string the player typed into the settings into a program to run.
    /// <para>
    /// Enough of a shell to cover what an editor command actually looks like - a bare name, a quoted
    /// path with spaces in it, a flag or two - and deliberately no more. The setting names a program,
    /// not a line for a shell to interpret.
    /// </para>
    /// </summary>
    internal readonly record struct EditorCommandLine
    {
        /// <summary>What <c>PATHEXT</c> falls back to when the environment has not set it.</summary>
        private const string DefaultPathExtensions = ".COM;.EXE;.BAT;.CMD";

        private EditorCommandLine(string executable, string[] arguments, string display)
        {
            Executable = executable;
            Arguments = arguments;
            Display = display;
        }

        /// <summary>The program to run, as the player wrote it.</summary>
        public string Executable { get; }

        /// <summary>Fixed arguments that go before the file, such as <c>-w</c>.</summary>
        public string[] Arguments { get; }

        /// <summary>The name to put in a message about this editor.</summary>
        public string Display { get; }

        /// <summary>
        /// Reads the setting, and says why not when it cannot.
        /// </summary>
        /// <remarks>
        /// A missing program is caught here rather than left to the launch. The editor runs by way of
        /// <c>cmd</c>'s <c>start</c> so that a terminal editor gets a console of its own, and the
        /// price of that is that <c>cmd</c> reports a name it could not find onto a console nobody can
        /// see - indistinguishable, from here, from an editor that opened and was closed again. So the
        /// disk is asked first, while there is still someone to tell.
        /// </remarks>
        public static bool TryParse(string command, out EditorCommandLine launch, out string? reason)
        {
            launch = default;
            reason = null;

            var text = (command ?? string.Empty).Trim();

            if (text.Length == 0)
            {
                reason = "No editor is set.";
                return false;
            }

            string executable;
            string rest;

            if (text.StartsWith('"'))
            {
                var end = text.IndexOf('"', 1);

                // Guessing where an unclosed quote was meant to close would be worse than saying so:
                // the player is either still typing or has made a mistake, and both want telling.
                if (end < 0)
                {
                    reason = "The editor command has a quote that is never closed.";
                    return false;
                }

                executable = text[1..end];
                rest = text[(end + 1)..];
            }
            else if (Find(text) is not null)
            {
                // The one genuinely ambiguous case, settled by asking the disk: an unquoted path with
                // spaces in it and no flags after it. A whole path resolves and is taken whole;
                // "code -w" does not resolve and falls through to the split below.
                executable = text;
                rest = string.Empty;
            }
            else
            {
                var space = text.IndexOf(' ');
                executable = space < 0 ? text : text[..space];
                rest = space < 0 ? string.Empty : text[(space + 1)..];
            }

            if (executable.Length == 0)
            {
                reason = "The editor command does not name a program.";
                return false;
            }

            if (Find(executable) is null)
            {
                reason = $"Could not find \"{executable}\" - check the command under Settings, Editor.";
                return false;
            }

            launch = new EditorCommandLine(executable, Split(rest), Path.GetFileName(executable));
            return true;
        }

        /// <summary>
        /// How to start the editor on <paramref name="file"/>.
        /// </summary>
        /// <remarks>
        /// Everything goes through <c>cmd /c start "" /wait</c> rather than being launched directly,
        /// which is what makes a terminal editor usable at all. Launched directly, <c>vim</c> would
        /// inherit this console and fight the game for every keystroke - the game's loop keeps reading
        /// input while the editor is open, so neither would work. <c>start</c> gives a console program
        /// a console of its own and passes a windowed one straight through, <c>/wait</c> blocks for
        /// both, and <c>CreateNoWindow</c> on <c>cmd</c> itself means the shell doing this is never
        /// seen. One path, no guessing which kind of program the player named.
        /// </remarks>
        public ProcessStartInfo ToStartInfo(string file)
        {
            var info = new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("COMSPEC") is { Length: > 0 } shell
                    ? shell
                    : "cmd.exe",

                // Nothing is redirected, unlike the narrator's child process: an editor that cannot
                // reach a screen cannot be typed into.
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // Added one at a time rather than as one string, for the same reason the Claude session
            // does it: no hand-rolled quoting to get wrong.
            info.ArgumentList.Add("/c");
            info.ArgumentList.Add("start");

            // start reads a leading quoted argument as the title for the new window, so it is given
            // an empty one - otherwise it would take the program's path as the title and then have
            // nothing left to run.
            info.ArgumentList.Add(string.Empty);
            info.ArgumentList.Add("/wait");
            info.ArgumentList.Add(Executable);

            foreach (var argument in Arguments)
            {
                info.ArgumentList.Add(argument);
            }

            // Always last. There is no placeholder to put it anywhere else, so an editor that wants
            // the file somewhere other than the end of its command line is not supported.
            info.ArgumentList.Add(file);

            return info;
        }

        /// <summary>
        /// The program, if it is there: as given when it is a path, or found along <c>PATH</c> when it
        /// is a bare name, trying each <c>PATHEXT</c> suffix the way a shell would.
        /// </summary>
        private static string? Find(string executable)
        {
            if (executable.Length == 0)
            {
                return null;
            }

            try
            {
                if (executable.AsSpan().IndexOfAny('\\', '/', ':') >= 0)
                {
                    return File.Exists(executable) ? executable : null;
                }

                var extensions = (Environment.GetEnvironmentVariable("PATHEXT") ?? DefaultPathExtensions)
                    .Split(';', StringSplitOptions.RemoveEmptyEntries);

                var folders = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                    .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);

                foreach (var folder in folders)
                {
                    var candidate = Path.Combine(folder.Trim('"'), executable);

                    if (Path.HasExtension(executable) && File.Exists(candidate))
                    {
                        return candidate;
                    }

                    foreach (var extension in extensions)
                    {
                        if (File.Exists(candidate + extension))
                        {
                            return candidate + extension;
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // One malformed PATH entry is not a reason to stop looking at the rest of it, and a
                // path the process may not read is not a reason to refuse the edit.
            }

            return null;
        }

        /// <summary>Splits the arguments after the program, keeping a quoted run together.</summary>
        private static string[] Split(string arguments)
        {
            var tokens = new List<string>();
            var token = new StringBuilder();
            var quoted = false;
            var started = false;

            foreach (var character in arguments)
            {
                if (character == '"')
                {
                    quoted = !quoted;

                    // Remembered separately, so an argument written as "" survives as an empty one
                    // rather than disappearing along with its quotes.
                    started = true;
                    continue;
                }

                if (!quoted && char.IsWhiteSpace(character))
                {
                    if (started || token.Length > 0)
                    {
                        tokens.Add(token.ToString());
                        token.Clear();
                        started = false;
                    }

                    continue;
                }

                token.Append(character);
            }

            if (started || token.Length > 0)
            {
                tokens.Add(token.ToString());
            }

            return [.. tokens];
        }
    }
}
