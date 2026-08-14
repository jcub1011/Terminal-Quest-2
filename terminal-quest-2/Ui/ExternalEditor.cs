using System.ComponentModel;
using System.Diagnostics;
using System.Text;

using Terminal.Gui.App;
using Terminal.Gui.Input;
using Terminal.Gui.Views;

using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Ctrl+G: hands a text field's contents to a real editor and takes back what comes out.
    /// <para>
    /// A single-line <see cref="TextField"/> is a poor place to write a paragraph, and the game asks
    /// for paragraphs - a character's description above all. Rather than grow a multi-line editor of
    /// our own, the text goes to whichever editor the player has named in the settings and comes back
    /// when they close it.
    /// </para>
    /// <para>
    /// There are two ways in. <see cref="TryBegin"/> edits a field, by way of a scratch copy that is
    /// read back and thrown away. <see cref="TryBeginFile"/> edits a file that already means
    /// something - the save's system prompt - in place, with nothing to read back into and nothing to
    /// delete afterwards. They share everything between the keystroke and the editor closing, which
    /// is most of what is here: finding the program, giving it a console of its own, waiting for it
    /// off the UI thread, and putting the terminal back together.
    /// </para>
    /// <para>
    /// One instance for the whole application, built in <c>Program</c> and handed to each window. The
    /// windows keep their own editor protocols; all this owns is the file, the child process, and the
    /// text that came back.
    /// </para>
    /// </summary>
    internal sealed class ExternalEditor
    {
        /// <summary>
        /// A run shorter than this cannot have been typed in, so an editor that returns inside it
        /// having changed nothing handed the file to a window that was already open and gave up its
        /// own process. Generous, because the wait goes through <c>cmd</c> and that is not instant.
        /// </summary>
        private static readonly TimeSpan ForkThreshold = TimeSpan.FromSeconds(1.5);

        /// <summary>How long a scratch file has to be untouched before it counts as litter.</summary>
        private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);

        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly IApplication _app;
        private readonly Func<string> _command;

        /// <summary>
        /// What the editor last returned, against the field it was returned for, so
        /// <see cref="Resolve"/> can tell whole text from the one line standing in for it.
        /// </summary>
        private readonly Dictionary<TextField, Shadow> _shadows = [];

        private Pending? _pending;

        public ExternalEditor(IApplication app, Func<string> command)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(command);

            _app = app;
            _command = command;
        }

        /// <summary>
        /// The chord, in one place so every window agrees on it.
        /// </summary>
        /// <remarks>
        /// Ctrl+G rather than anything else because that is what Claude Code uses for this, and it is
        /// spoken for by neither this game nor Terminal.Gui - so it reaches a window by ordinary
        /// bubbling even while a text field holds the focus.
        /// </remarks>
        public static Key RequestKey => Key.G.WithCtrl;

        /// <summary>
        /// Whether an editor is open on some field right now.
        /// <para>
        /// While it is, the window that started it swallows every key. The player's text is in another
        /// program, and a stray Esc that walked out of the save - or a Ctrl+Q that ended the game -
        /// would take it with them.
        /// </para>
        /// </summary>
        public bool IsBusy => _pending is not null;

        /// <summary>
        /// Where the scratch files go: the game's own folder, so a file left behind by a kill is
        /// somewhere the player can find rather than invisible in <c>%TEMP%</c>.
        /// </summary>
        private static string ScratchDirectory => Path.Combine(AppDirectory.Root, "edit");

        /// <summary>
        /// Opens the editor on <paramref name="field"/>'s current text.
        /// </summary>
        /// <param name="field">The field whose text is being edited, and where it will come back to.</param>
        /// <param name="notice">
        /// Shown while the editor is open, and called with null once it has closed. Each window says
        /// this its own way - a frame title on the game screen, the hint line everywhere else.
        /// </param>
        /// <returns>
        /// Whether the key was dealt with. Always true once this has been asked, because every way
        /// this can fail is one the player is told about rather than one that leaves the key unused.
        /// </returns>
        public bool TryBegin(TextField field, Action<string?> notice)
        {
            ArgumentNullException.ThrowIfNull(field);
            ArgumentNullException.ThrowIfNull(notice);

            if (IsBusy)
            {
                return true;
            }

            if (!TryResolveEditor(notice, out var launch))
            {
                return true;
            }

            var text = Resolve(field);
            var path = Path.Combine(ScratchDirectory, $"tq-edit-{Guid.NewGuid():N}.txt");

            try
            {
                Directory.CreateDirectory(ScratchDirectory);

                // Swept as an edit begins rather than on a timer: this is the only moment the game is
                // certainly about to touch the folder anyway, and a file orphaned by a kill last week
                // is not worth a background task.
                Sweep();

                // CRLF because Notepad is the default and older builds of it draw a lone \n as no
                // break at all - the whole text on one line, which is the opposite of the point.
                File.WriteAllText(path, text.ReplaceLineEndings("\r\n"), Utf8NoBom);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or NotSupportedException or ArgumentException)
            {
                TryDelete(path);
                notice($"Could not start {launch.Display}: {ex.Message}");
                return true;
            }

            if (!Start(launch, path, text, notice, field, saved: null, scratch: true))
            {
                TryDelete(path);
                return true;
            }

            // The field keeps showing the text but stops taking any, so the player cannot type into
            // one copy while editing another.
            field.ReadOnly = true;

            return true;
        }

        /// <summary>
        /// Opens the editor on a file that is already somebody's, and leaves it there.
        /// </summary>
        /// <param name="path">
        /// The file itself, not a copy of it. Whatever the editor saves is what the file now says -
        /// nothing here writes it, reads it back into a control, or deletes it afterwards.
        /// </param>
        /// <param name="notice">
        /// As <see cref="TryBegin"/>'s: shown while the editor is open, called with null once it has
        /// closed and there is nothing to report.
        /// </param>
        /// <param name="saved">
        /// Called once the editor has closed and the file's text has actually changed, after
        /// <paramref name="notice"/> and after this has let go of the edit. Not called when the editor
        /// saved nothing, so a caller can act on a real change - ending the session, in the one case
        /// this exists for - without acting on a look.
        /// </param>
        /// <returns>Whether the key was dealt with. Always true, on the same reasoning as the other.</returns>
        public bool TryBeginFile(string path, Action<string?> notice, Action? saved = null)
        {
            ArgumentException.ThrowIfNullOrEmpty(path);
            ArgumentNullException.ThrowIfNull(notice);

            if (IsBusy)
            {
                return true;
            }

            if (!TryResolveEditor(notice, out var launch))
            {
                return true;
            }

            string original;

            try
            {
                // Read before the editor runs, so a change can be told from a look afterwards. Read
                // with the same BOM detection the way back uses, or a file that already had one would
                // read differently before and after and every edit would look like a change.
                original = File.Exists(path) ? File.ReadAllText(path, Utf8NoBom) : string.Empty;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or NotSupportedException or ArgumentException)
            {
                notice($"Could not read {Path.GetFileName(path)}: {ex.Message}");
                return true;
            }

            // No scratch file to write and none to clean up: the file the editor is given is the file
            // that matters, so the delete every other path ends with must not happen here.
            Start(launch, path, original, notice, field: null, saved, scratch: false);
            return true;
        }

        /// <summary>
        /// The text <paramref name="field"/> really holds.
        /// <para>
        /// The same as its <see cref="TextField.Text"/> except after an edit that came back with more
        /// than one line in it: a single-line field cannot show those, so it shows them joined, and
        /// the whole thing is kept here. Every commit path reads a field through this, or a character
        /// described in three paragraphs reaches the save as one.
        /// </para>
        /// <para>
        /// The joined form is remembered alongside the whole one and checked here, so the moment the
        /// player types over what the editor returned, what they typed is what this reports.
        /// </para>
        /// </summary>
        public string Resolve(TextField field)
        {
            ArgumentNullException.ThrowIfNull(field);

            var shown = field.Text ?? string.Empty;

            return _shadows.TryGetValue(field, out var shadow)
                ? EditorText.Resolve(shown, shadow.Raw, shadow.Flattened)
                : shown;
        }

        /// <summary>
        /// Forgets what an editor returned for a field, for a caller that has put something else in
        /// it. Without this a field reset behind <see cref="Resolve"/>'s back could still report the
        /// old multi-line value, because the line it is showing would match again.
        /// </summary>
        public void Forget(TextField field)
        {
            ArgumentNullException.ThrowIfNull(field);
            _shadows.Remove(field);
        }

        /// <summary>
        /// Gives up on an edit still in flight, for a window that is going away.
        /// <para>
        /// The editor is deliberately left running. Closing the player's window out from under them
        /// to save a field they can no longer see is the worse of the two trades; what this prevents
        /// is the answer being written into a field that has been disposed.
        /// </para>
        /// </summary>
        public void Abandon()
        {
            if (_pending is not { } pending)
            {
                return;
            }

            pending.Abandoned = true;
            _pending = null;
        }

        /// <summary>
        /// Which program to run, or false having already said why not.
        /// </summary>
        private bool TryResolveEditor(Action<string?> notice, out EditorCommandLine launch)
        {
            launch = default;

            var command = _command().Trim();

            if (command.Length == 0)
            {
                notice($"No editor is set - see Settings, Editor. The default is {AppSettings.DefaultEditorCommand}.");
                return false;
            }

            if (!EditorCommandLine.TryParse(command, out launch, out var reason))
            {
                notice(reason);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Starts the editor on a file and arranges for <see cref="Finish"/> to run when it closes.
        /// </summary>
        /// <returns>
        /// False having already reported a program that would not start, in which case nothing is
        /// pending and the caller still owns whatever it wrote out.
        /// </returns>
        private bool Start(
            EditorCommandLine launch,
            string path,
            string original,
            Action<string?> notice,
            TextField? field,
            Action? saved,
            bool scratch)
        {
            Process process;

            try
            {
                process = Process.Start(launch.ToStartInfo(path))
                    ?? throw new InvalidOperationException("no process was started");
            }
            catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException
                                          or InvalidOperationException or NotSupportedException)
            {
                notice($"Could not start {launch.Display}: {ex.Message}");
                return false;
            }

            var pending = new Pending(field, notice, path, original, launch.Display, saved, scratch);
            _pending = pending;

            notice($"Editing in {launch.Display}... close it to continue.");

            // Waited for off the UI thread. Doing it here would hold the loop that has to paint the
            // notice, and the game would look hung rather than busy.
            _ = Task.Run(async () =>
            {
                try
                {
                    await process.WaitForExitAsync().ConfigureAwait(false);
                }
                catch (SystemException)
                {
                    // Nothing to learn from a process we can no longer ask about, and the notice has
                    // to come down either way. What is in the file is the only thing that matters.
                }
                finally
                {
                    _app.Invoke(() => Finish(pending));
                    process.Dispose();
                }
            });

            return true;
        }

        /// <summary>Takes what the editor left, on the UI thread.</summary>
        private void Finish(Pending pending)
        {
            Repair();

            if (pending.Abandoned)
            {
                TryDeleteScratch(pending);
                return;
            }

            _pending = null;

            if (pending.Field is { } editedField)
            {
                editedField.ReadOnly = false;
            }

            // Set only on the way out of a real change, and acted on in the finally: the callback may
            // close the window this was started from, so it must run after everything here is done
            // with the field and with _pending.
            var changed = false;

            try
            {
                if (!File.Exists(pending.Path))
                {
                    pending.Notice($"{pending.Display} did not save anything.");
                    return;
                }

                // This overload detects a byte-order mark, so an editor that saved UTF-16 or added a
                // UTF-8 BOM is read correctly and the mark does not survive as a stray character.
                // Notepad's encoding dropdown is right there, so this is not hypothetical.
                var edited = File.ReadAllText(pending.Path, Utf8NoBom);

                // The exit code is deliberately not consulted. The wait goes through cmd's start,
                // which reports its own rather than the editor's, and plenty of editors exit non-zero
                // for harmless reasons. What is in the file is the only thing that says anything.
                if (string.Equals(edited, pending.Original, StringComparison.Ordinal))
                {
                    // An editor that really waited and really changed nothing lands here too, and
                    // changing nothing is the same outcome either way - so the timing only decides
                    // whether there is something worth saying about it.
                    pending.Notice(pending.Started.Elapsed < ForkThreshold
                        ? $"{pending.Display} returned straight away - it needs its wait flag, as in \"code -w\"."
                        : null);
                    return;
                }

                changed = true;

                // Nothing to bring back for a file that was edited where it lives. It says what it
                // says, and the caller is about to be told that it changed.
                if (pending.Field is not { } field)
                {
                    pending.Notice(null);
                    return;
                }

                // Trailing newlines are the editor's, not the player's - Notepad adds one on its own.
                var raw = edited.TrimEnd('\r', '\n');
                var flattened = EditorText.Flatten(raw).Trim();

                field.Text = flattened;
                field.InsertionPoint = flattened.Length;

                // Only worth remembering when the two differ. Anything else would have Resolve
                // reporting a value the field is already showing.
                if (string.Equals(raw, flattened, StringComparison.Ordinal))
                {
                    _shadows.Remove(field);
                }
                else
                {
                    _shadows[field] = new Shadow(raw, flattened);
                }

                pending.Notice(null);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or NotSupportedException or ArgumentException)
            {
                pending.Notice($"Could not read back what {pending.Display} saved: {ex.Message}");
            }
            finally
            {
                TryDeleteScratch(pending);

                if (pending.Field is { CanFocus: true } focusable)
                {
                    focusable.SetFocus();
                }

                if (changed && pending.Saved is { } saved)
                {
                    saved();
                }
            }
        }

        /// <summary>
        /// Puts the screen and the terminal back the way the game had them.
        /// </summary>
        /// <remarks>
        /// A windowed editor cannot disturb either, but a terminal editor - run in a console of its
        /// own, so it never had ours - can still have asked this terminal for things on its way past.
        /// Clearing first means every cell is written again rather than patched, which is what undoes
        /// anything left behind; and mouse reporting is re-asserted because
        /// <see cref="MouseReporting"/> only does that as a session begins, and no session began here -
        /// which would leave the wheel dead in the transcript for the rest of the save.
        /// </remarks>
        private void Repair()
        {
            MouseReporting.Reapply(_app);

            _app.ClearScreenNextIteration = true;
            _app.LayoutAndDraw(true);
        }

        /// <summary>Throws away scratch files old enough that nothing can still be editing them.</summary>
        private static void Sweep()
        {
            try
            {
                foreach (var stale in Directory.EnumerateFiles(ScratchDirectory, "tq-edit-*.txt"))
                {
                    if (DateTime.UtcNow - File.GetLastWriteTimeUtc(stale) > StaleAfter)
                    {
                        File.Delete(stale);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or NotSupportedException or ArgumentException)
            {
                // Housekeeping. A folder that will not be swept is not a reason to refuse an edit.
            }
        }

        /// <summary>
        /// Throws away the file an edit was given, but only when the game is the one that made it.
        /// </summary>
        /// <remarks>
        /// The guard is the whole method. A field's text is edited through a copy that exists for no
        /// other reason and must not be left behind; a file edited in place is the save's, and deleting
        /// it would destroy the thing the player just wrote.
        /// </remarks>
        private static void TryDeleteScratch(Pending pending)
        {
            if (pending.IsScratch)
            {
                TryDelete(pending.Path);
            }
        }

        /// <summary>
        /// Deletes the scratch file, and does not care if it cannot.
        /// </summary>
        /// <remarks>
        /// The same bargain as <see cref="Saves.SaveStore"/>'s: a file left behind is untidy, and
        /// throwing over it here would lose the text the player just wrote, which is worse.
        /// </remarks>
        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                          or NotSupportedException or ArgumentException)
            {
                // Nothing to be done and nothing worth saying.
            }
        }

        /// <summary>What the editor returned for a field, and the one line standing in for it.</summary>
        private readonly record struct Shadow(string Raw, string Flattened);

        /// <summary>An edit in flight.</summary>
        private sealed class Pending
        {
            public Pending(
                TextField? field,
                Action<string?> notice,
                string path,
                string original,
                string display,
                Action? saved,
                bool scratch)
            {
                Field = field;
                Notice = notice;
                Path = path;
                Original = original;
                Display = display;
                Saved = saved;
                IsScratch = scratch;
                Started = Stopwatch.StartNew();
            }

            /// <summary>Where the text came from and goes back to, or null for a file edited in place.</summary>
            public TextField? Field { get; }

            public Action<string?> Notice { get; }

            public string Path { get; }

            /// <summary>What was written out, to tell a real edit from an editor that forked.</summary>
            public string Original { get; }

            /// <summary>The editor's name, as the player would recognise it in a message.</summary>
            public string Display { get; }

            /// <summary>Told once the file has really changed, if anybody asked to be.</summary>
            public Action? Saved { get; }

            /// <summary>
            /// Whether the file is the game's own copy, and so whether it may be deleted afterwards.
            /// </summary>
            public bool IsScratch { get; }

            public Stopwatch Started { get; }

            /// <summary>Whether the window that asked for this has since gone.</summary>
            public bool Abandoned { get; set; }
        }
    }
}
