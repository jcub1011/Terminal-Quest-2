using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The startup screen: continue, load, start something new, or leave.
    /// <para>
    /// This runs before the narrator process exists, because the save folder has to be known
    /// before the MCP server can be pointed at it - the choice made here becomes a command-line
    /// argument to a child process, so it cannot be deferred.
    /// </para>
    /// <para>
    /// Two levels, in the shape <see cref="SettingsWindow"/> established: the options, and the
    /// list of saves behind Load. The view tree is built once and never changes - switching levels
    /// swaps which list is visible and nothing else. Two levels rather than a page trail because
    /// there are only ever two and their rows are different shapes.
    /// </para>
    /// <para>
    /// Owns no game logic. It sets <see cref="Chosen"/> and stops; the host decides what opening
    /// a save means.
    /// </para>
    /// </summary>
    internal sealed class SaveMenuWindow : Window
    {
        /// <summary>
        /// The narrator line, two hint lines, and the row the editor sits on. Two hint lines because
        /// the saves level has more keys than fit across a narrow terminal in one.
        /// </summary>
        private const int FooterHeight = 4;

        private const string OptionsHint =
            "Press the letter in brackets, or Up/Down and Enter.  Right opens a submenu.";

        /// <summary>
        /// The second options row. Not a key of this menu's at all - it is the terminal's - but this
        /// is the first screen a player sees, which makes it where the note is worth the row.
        /// </summary>
        private const string OptionsHintMore =
            "Ctrl+= and Ctrl+- resize your terminal's text.";

        private const string SavesHint =
            "Enter loads.  R renames.  D duplicates.  Ctrl+R resets.";

        private const string SavesHintMore =
            "F opens the save folder.  X deletes.  Left goes back.";

        /// <summary>Where the options live, so the keys and the rows cannot drift apart.</summary>
        private const int ContinueRow = 0;
        private const int LoadRow = 1;
        private const int NewSaveRow = 2;
        private const int SettingsRow = 3;
        private const int QuitRow = 4;

        private readonly MenuListView _options;
        private readonly SaveListView _saves;
        private readonly BreadcrumbView _breadcrumb;
        private readonly Label _narrator;
        private readonly Label _hint;

        /// <summary>The hint line's second row, blank whenever the first row is carrying a notice.</summary>
        private readonly Label _hintMore;

        private readonly Label _prompt;
        private readonly TextField _editor;

        private Level _level = Level.Options;
        private Editing _editing = Editing.None;

        /// <summary>What the keys do while the open edit lasts, so a notice can be taken back off.</summary>
        private string _editHint = string.Empty;

        /// <summary>
        /// The save the next X keypress will destroy, or null when nothing is half-confirmed.
        /// <para>
        /// Held by name rather than by index: the list is rebuilt and re-sorted after every
        /// operation, and an index would quietly come to mean a different save.
        /// </para>
        /// </summary>
        private string? _pendingDelete;

        /// <summary>The save the next Ctrl+R keypress will reset back to start, or null when nothing is half-confirmed.</summary>
        private string? _pendingReset;

        /// <summary>The save being renamed, held by name for the same reason.</summary>
        private string? _renaming;

        /// <param name="narrator">
        /// A one-line summary of the provider a game started here would use. Shown because the
        /// choice is made on another screen and would otherwise be invisible until the first turn
        /// either narrated or failed.
        /// </param>
        public SaveMenuWindow(string narrator)
        {
            Title = "Terminal Quest";
            BorderStyle = LineStyle.Rounded;
            SetScheme(Theme.CreateScheme());

            _breadcrumb = new BreadcrumbView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
            };

            // Both lists own the same rectangle; only the one for the current level is visible.
            _options = new MenuListView
            {
                X = 0,
                Y = Pos.Bottom(_breadcrumb),
                Width = Dim.Fill(),
                Height = Dim.Fill() - FooterHeight,
            };

            _saves = new SaveListView
            {
                X = 0,
                Y = Pos.Bottom(_breadcrumb),
                Width = Dim.Fill(),
                Height = Dim.Fill() - FooterHeight,
                Visible = false,
            };

            _narrator = Line($"Narrator: {narrator}", Pos.Bottom(_options));
            _hint = Line(string.Empty, Pos.Bottom(_narrator));
            _hintMore = Line(string.Empty, Pos.Bottom(_hint));

            _prompt = Line(string.Empty, Pos.Bottom(_hintMore));
            _prompt.Width = Dim.Auto();
            _prompt.Visible = false;

            // One editor for both jobs - naming a new save and renaming an old one - on its own
            // row rather than dropped onto a list row. The save list scrolls, so a row's index and
            // its position on screen are not the same number, and there is no arithmetic here to
            // get wrong.
            _editor = new TextField
            {
                X = Pos.Right(_prompt),
                Y = Pos.Bottom(_hintMore),
                Width = Dim.Fill(),
                Height = 1,
                Visible = false,
                CanFocus = false,
            };
            _editor.SetScheme(Theme.CreateScheme());
            _editor.Accepting += OnEditorAccepting;

            Add(_breadcrumb, _options, _saves, _narrator, _hint, _hintMore, _prompt, _editor);

            Reload(out var failure);
            ShowLevel();

            if (failure is not null)
            {
                Fail(failure);
            }

            // The window has no visible focusable child while browsing, so it has to hold focus
            // itself for the keys to arrive at all. Asked for here rather than in the constructor
            // because that is too early to stick.
            Initialized += (_, _) => SetFocus();
        }

        /// <summary>Which of the two levels is on screen.</summary>
        private enum Level
        {
            Options,
            Saves,
        }

        /// <summary>What the editor on the bottom row is currently collecting, if anything.</summary>
        private enum Editing
        {
            None,
            NewSave,
            Rename,
        }

        /// <summary>The save the player settled on, or null when they quit instead.</summary>
        public SaveStore? Chosen { get; private set; }

        /// <summary>
        /// What Ctrl+G hands a name being typed to, or null where there is nothing to hand it to.
        /// </summary>
        public ExternalEditor? Editor { get; init; }

        /// <summary>Raised once a save is open and the game should start.</summary>
        public event Action? Done;

        /// <summary>Raised when the player leaves without starting anything.</summary>
        public event Action? Cancelled;

        /// <summary>
        /// Lets go of an edit still open, so its answer is not written into a field that has gone.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Editor?.Abandon();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Raised when the player wants the settings screen. The host runs it and comes back here,
        /// so this window is closed rather than kept behind it.
        /// </summary>
        public event Action? SettingsRequested;

        protected override bool OnKeyDown(Key key) =>
            _editing != Editing.None ? OnKeyDownEditing(key)
            : _level == Level.Saves ? OnKeyDownSaves(key)
            : OnKeyDownOptions(key);

        /// <summary>
        /// Moves the highlight on the wheel, on whichever list the level is showing.
        /// <para>
        /// Handled here rather than on the lists themselves because moving the highlight on the
        /// saves level is never only that: it abandons a half-confirmed delete, exactly as the
        /// arrows do. A wheel wired straight into the list would slide the highlight off the save
        /// the confirmation was asked about and leave it armed.
        /// </para>
        /// </summary>
        protected override bool OnMouseEvent(Mouse mouse)
        {
            ArgumentNullException.ThrowIfNull(mouse);

            var delta = mouse.Flags.HasFlag(MouseFlags.WheeledUp) ? -1
                : mouse.Flags.HasFlag(MouseFlags.WheeledDown) ? 1
                : 0;

            // Nothing moves under a name being typed: the cursor the player is watching is in the
            // field, and the list it would move is the one that field is sitting on.
            if (delta == 0 || _editing != Editing.None)
            {
                return false;
            }

            if (_level != Level.Saves)
            {
                _options.MoveSelection(delta);
                return true;
            }

            CancelPendingDelete();
            CancelPendingReset();
            _saves.MoveSelection(delta);
            return true;
        }

        /// <summary>
        /// Keys while a name is being typed.
        /// <para>
        /// The focused editor has already had its turn by the time this runs - Terminal.Gui offers
        /// a key to the focused subview first - so everything printable, and Enter, is gone before
        /// we see it. That is what keeps the bare letters on the saves level from firing at a
        /// player who is only spelling a name. The important key here is Esc: it has to close the
        /// editor and stop, or a single press would close the editor and back out of the level
        /// underneath it in one go.
        /// </para>
        /// </summary>
        private bool OnKeyDownEditing(Key key)
        {
            // Nothing else happens while the name is in another program - least of all Esc, which
            // would put the editor away and drop the edit still being made.
            if (Editor is { IsBusy: true })
            {
                return true;
            }

            if (key == ExternalEditor.RequestKey && Editor is { } external)
            {
                return external.TryBegin(_editor, SetEditingNotice);
            }

            if (key == Key.Esc)
            {
                EndEdit();
                ShowHint();
                return true;
            }

            // Swallowed: the arrows belong to the text, and a field ignores them at either end of
            // what is typed. One arriving here would move a list the player cannot see the cursor
            // on, or walk out of the level underneath.
            if (key == Key.CursorUp || key == Key.CursorDown
                || key == Key.CursorLeft || key == Key.CursorRight)
            {
                return true;
            }

            if (key == Key.Q.WithCtrl)
            {
                Cancelled?.Invoke();
                return true;
            }

            return base.OnKeyDown(key);
        }

        private bool OnKeyDownOptions(Key key)
        {
            // Esc is Quit's second key rather than a separate act: on the options level there is
            // nothing left to back out of, so leaving is all it can mean.
            if (Letter(key, Key.Q) || key == Key.Esc || key == Key.Q.WithCtrl)
            {
                Cancelled?.Invoke();
                return true;
            }

            if (Letter(key, Key.C))
            {
                Continue();
                return true;
            }

            if (Letter(key, Key.L))
            {
                GoToSaves();
                return true;
            }

            if (Letter(key, Key.N))
            {
                BeginNewSave();
                return true;
            }

            if (Letter(key, Key.S))
            {
                SettingsRequested?.Invoke();
                return true;
            }

            if (key == Key.CursorUp)
            {
                _options.MoveSelection(-1);
                return true;
            }

            if (key == Key.CursorDown)
            {
                _options.MoveSelection(1);
                return true;
            }

            // Enter and Right are the same act here: every option either does something or leads
            // somewhere, and none of them is a value to choose between.
            if (key == Key.Enter || key == Key.CursorRight)
            {
                Activate(_options.SelectedIndex);
                return true;
            }

            return base.OnKeyDown(key);
        }

        private bool OnKeyDownSaves(Key key)
        {
            // Esc backs out one level rather than quitting: leaving the game is what Esc means on
            // the options level, one press further out.
            if (key == Key.Esc || key == Key.CursorLeft)
            {
                CancelPendingDelete();
                CancelPendingReset();
                GoToOptions();
                return true;
            }

            if (key == Key.Q.WithCtrl)
            {
                Cancelled?.Invoke();
                return true;
            }

            // Del is kept alongside X, because it is what this screen has always answered to.
            if (Letter(key, Key.X) || key == Key.Delete)
            {
                CancelPendingReset();
                Delete();
                return true;
            }

            if (key == Key.R.WithCtrl)
            {
                CancelPendingDelete();
                Reset();
                return true;
            }

            // Any key that is not the delete or reset key abandons a half-confirmed action, so a pending
            // confirmation cannot survive the player moving on and be triggered by an unrelated
            // press later.
            CancelPendingDelete();
            CancelPendingReset();

            if (key == Key.CursorUp)
            {
                _saves.MoveSelection(-1);
                return true;
            }

            if (key == Key.CursorDown)
            {
                _saves.MoveSelection(1);
                return true;
            }

            if (key == Key.Enter)
            {
                if (_saves.Selected is { } selected)
                {
                    Open(selected.Name);
                }

                return true;
            }

            if (Letter(key, Key.R))
            {
                BeginRename();
                return true;
            }

            if (Letter(key, Key.D))
            {
                Duplicate();
                return true;
            }

            if (Letter(key, Key.F))
            {
                Reveal();
                return true;
            }

            return base.OnKeyDown(key);
        }

        /// <summary>
        /// Whether a keypress is a letter, in either case. The unshifted key is what the hint line
        /// names, but a player with caps lock on sends the shifted one and means the same thing.
        /// </summary>
        private static bool Letter(Key key, Key letter) => key == letter || key == letter.WithShift;

        private void Activate(int index)
        {
            switch (index)
            {
                case ContinueRow:
                    Continue();
                    break;

                case LoadRow:
                    GoToSaves();
                    break;

                case NewSaveRow:
                    BeginNewSave();
                    break;

                case SettingsRow:
                    SettingsRequested?.Invoke();
                    break;

                case QuitRow:
                    Cancelled?.Invoke();
                    break;
            }
        }

        /// <summary>
        /// Opens the save saved most recently, which is the first one the list hands back. Nothing
        /// tracks a "last played" pointer separately: <see cref="SaveMetadata.LastPlayed"/> is
        /// stamped after every turn and <see cref="SavePaths.List"/> already sorts on it.
        /// </summary>
        private void Continue()
        {
            if (Latest() is not { } latest)
            {
                Fail("There is nothing to continue yet.  New Save starts one.");
                return;
            }

            Open(latest.Name);
        }

        private void GoToSaves()
        {
            if (_saves.Saves.Count == 0)
            {
                Fail("There are no saves to load yet.  New Save starts one.");
                return;
            }

            _level = Level.Saves;
            ShowLevel();
        }

        private void GoToOptions()
        {
            _level = Level.Options;
            ShowLevel();
        }

        /// <summary>
        /// Asks for a name, then makes a save nobody has played. Unlike the box this screen used
        /// to have, a name that is already taken is refused rather than quietly loaded: New Save
        /// says what it means, and Load is right above it for the other case.
        /// </summary>
        private void BeginNewSave()
        {
            _editing = Editing.NewSave;
            BeginEdit("new save name: ", string.Empty, "Enter creates it.  Ctrl+G opens an editor.  Esc goes back.");
        }

        private void BeginRename()
        {
            if (_saves.Selected is not { } selected)
            {
                return;
            }

            _renaming = selected.Name;
            _editing = Editing.Rename;
            BeginEdit(
                $"rename '{selected.Name}' to: ",
                selected.Name,
                "Enter renames it.  Ctrl+G opens an editor.  Esc leaves it alone.");
        }

        /// <summary>Takes what was typed. The editor stays open when the name will not do.</summary>
        private void Commit()
        {
            // Through the editor rather than off the field, for consistency with every other commit
            // path. A name written in Notepad across two lines arrives with the break in it and is
            // refused by SavePaths.IsValidName below, which is the honest answer.
            var typed = (Editor?.Resolve(_editor) ?? _editor.Text ?? string.Empty).Trim();
            var editing = _editing;

            if (typed.Length == 0)
            {
                Fail("Type a name first.");
                return;
            }

            if (!SavePaths.IsValidName(typed))
            {
                Fail("That name will not work as a folder. Avoid \\ / : * ? \" < > |");
                return;
            }

            if (editing == Editing.NewSave)
            {
                if (SavePaths.Exists(typed))
                {
                    Fail($"There is already a save called '{typed}'.  Load it instead.");
                    return;
                }

                EndEdit();
                Open(typed);
                return;
            }

            if (_renaming is not { } from)
            {
                EndEdit();
                return;
            }

            // Nothing typed but the name it already had. Closing quietly is the honest answer;
            // announcing a rename that did not happen is not.
            if (string.Equals(from, typed, StringComparison.Ordinal))
            {
                EndEdit();
                ShowHint();
                return;
            }

            try
            {
                SavePaths.Rename(from, typed);
            }
            catch (Exception ex) when (ex is SaveException or ArgumentException)
            {
                Fail(ex.Message);
                return;
            }

            EndEdit();
            Reload(out var failure);

            // By name, because the list has been re-sorted around the new one.
            _saves.Select(typed);
            Fail(failure ?? $"Renamed '{from}' to '{typed}'.");
        }

        /// <summary>
        /// X once asks, X again destroys. A save is a playthrough, and there is no undo - but a
        /// modal here would be the only one in the game, so the confirmation is the second press.
        /// </summary>
        private void Delete()
        {
            if (_saves.Selected is not { } selected)
            {
                Fail("There is no save to delete.");
                return;
            }

            if (!SaveStore.Matches(_pendingDelete, selected.Name))
            {
                _pendingDelete = selected.Name;
                Fail($"Delete '{selected.Name}' and everything in it?  X again to confirm.");
                return;
            }

            _pendingDelete = null;

            try
            {
                SavePaths.Delete(selected.Name);
            }
            catch (Exception ex) when (ex is SaveException or ArgumentException)
            {
                Fail(ex.Message);
                return;
            }

            Reload(out var failure);

            // The last save can be deleted from in here, and an empty list has nothing to act on.
            if (_saves.Saves.Count == 0)
            {
                GoToOptions();
            }

            Fail(failure ?? $"Deleted '{selected.Name}'.");
        }

        /// <summary>
        /// Ctrl+R once asks, Ctrl+R again resets.
        /// </summary>
        private void Reset()
        {
            if (_saves.Selected is not { } selected)
            {
                Fail("There is no save to reset.");
                return;
            }

            if (!SaveStore.Matches(_pendingReset, selected.Name))
            {
                _pendingReset = selected.Name;
                Fail($"Reset '{selected.Name}' back to start?  Ctrl+R again to confirm.");
                return;
            }

            _pendingReset = null;

            try
            {
                SavePaths.Reset(selected.Name);
            }
            catch (Exception ex) when (ex is SaveException or ArgumentException)
            {
                Fail(ex.Message);
                return;
            }

            Reload(out var failure);
            _saves.Select(selected.Name);
            Fail(failure ?? $"Reset '{selected.Name}' to its starting state.");
        }

        private void Duplicate()
        {
            if (_saves.Selected is not { } selected)
            {
                Fail("There is no save to duplicate.");
                return;
            }

            string copy;

            try
            {
                copy = SavePaths.Duplicate(selected.Name);
            }
            catch (Exception ex) when (ex is SaveException or ArgumentException)
            {
                Fail(ex.Message);
                return;
            }

            Reload(out var failure);
            _saves.Select(copy);
            Fail(failure ?? $"Copied '{selected.Name}' to '{copy}'.");
        }

        /// <summary>
        /// Shows the selected save's files. A save is a folder and nothing else, so there is nothing
        /// to export or unpack here - the player just wants to be standing in it.
        /// </summary>
        private void Reveal()
        {
            if (_saves.Selected is not { } selected)
            {
                Fail("There is no save to show.");
                return;
            }

            string folder;

            try
            {
                folder = SavePaths.Folder(selected.Name);
            }
            catch (ArgumentException ex)
            {
                Fail(ex.Message);
                return;
            }

            // The list was read off the disk, so a folder that has gone since means something
            // outside the game moved it. Read it again rather than leave a row that does nothing.
            if (!Directory.Exists(folder))
            {
                Reload(out _);

                if (_saves.Saves.Count == 0)
                {
                    GoToOptions();
                }

                Fail($"'{selected.Name}' is no longer on disk.");
                return;
            }

            if (!FileExplorer.TryOpen(folder, out var reason))
            {
                Fail(reason ?? $"Could not open the folder for '{selected.Name}'.");
                return;
            }

            Fail($"Opened the folder for '{selected.Name}'.");
        }

        private void Open(string name)
        {
            try
            {
                Chosen = SavePaths.Open(name);
            }
            catch (Exception ex) when (ex is SaveException or ArgumentException or IOException or UnauthorizedAccessException)
            {
                Fail(ex.Message);
                return;
            }

            Done?.Invoke();
        }

        private void CancelPendingDelete()
        {
            if (_pendingDelete is null)
            {
                return;
            }

            _pendingDelete = null;
            ShowHint();
        }

        private void CancelPendingReset()
        {
            if (_pendingReset is null)
            {
                return;
            }

            _pendingReset = null;
            ShowHint();
        }

        /// <param name="hint">
        /// What the keys do while this edit is open. Kept, so that a notice shown over it - an
        /// external editor's, above all - has something to put back afterwards.
        /// </param>
        private void BeginEdit(string prompt, string text, string hint)
        {
            _prompt.Text = prompt;
            _prompt.Visible = true;

            // One field serves both kinds of edit, so anything a previous external edit left against
            // it has to go, or a name that happens to match that one line would be committed as the
            // other edit's text.
            Editor?.Forget(_editor);

            _editor.Text = text;
            _editor.CanFocus = true;
            _editor.Visible = true;
            _editor.SetFocus();

            _editHint = hint;
            SetHint(hint);
        }

        /// <summary>
        /// Says where the name has gone while an external editor holds it, and puts the hint for the
        /// edit still in progress back once it is done.
        /// </summary>
        private void SetEditingNotice(string? notice) => SetHint(notice ?? _editHint);

        /// <summary>Puts the editor away, taking the focus off it before it goes.</summary>
        private void EndEdit()
        {
            SetFocus();

            _editor.Visible = false;
            _editor.CanFocus = false;
            _editor.Text = string.Empty;
            _prompt.Visible = false;

            _editing = Editing.None;
            _renaming = null;
        }

        /// <summary>Enter inside the editor. Handled either way, so it never reaches the window.</summary>
        private void OnEditorAccepting(object? sender, CommandEventArgs e)
        {
            e.Handled = true;
            Commit();
        }

        /// <summary>Rebuilds the rows and the breadcrumb after the level has changed.</summary>
        private void ShowLevel()
        {
            var onSaves = _level == Level.Saves;

            _breadcrumb.Crumbs = onSaves ? ["Terminal Quest", "Load"] : ["Terminal Quest"];
            _options.Visible = !onSaves;
            _saves.Visible = onSaves;

            RefreshOptions();
            ShowHint();
        }

        /// <summary>
        /// Rebuilt rather than kept, because Continue names a save that a delete, a rename or a
        /// duplicate may have just changed.
        /// </summary>
        private void RefreshOptions()
        {
            var cursor = _options.SelectedIndex;

            _options.Rows =
            [
                Latest() is { } save
                    ? new MenuRow($"[C]ontinue [{save.Name}]", string.Empty)
                    : new MenuRow("[C]ontinue", "no saves yet"),
                new MenuRow("[L]oad", string.Empty, HasSubmenu: true),
                new MenuRow("[N]ew Save", string.Empty),
                new MenuRow("[S]ettings", string.Empty, HasSubmenu: true),
                new MenuRow("[Q]uit", string.Empty),
            ];

            _options.SelectedIndex = cursor;
        }

        /// <summary>Reloads the save list from disk, and with it the Continue row.</summary>
        private void Reload(out string? failure)
        {
            _saves.Saves = ReadSaves(out failure);
            RefreshOptions();
        }

        private SaveEntry? Latest() => _saves.Saves.Count > 0 ? _saves.Saves[0] : null;

        private void ShowHint()
        {
            if (_level == Level.Saves)
            {
                SetHint(SavesHint, SavesHintMore);
                return;
            }

            SetHint(OptionsHint, OptionsHintMore);
        }

        private void Fail(string message) => SetHint(message);

        /// <param name="more">
        /// The second row. Empty by default, so a notice or an edit's own hint - both of which are
        /// one line - takes the row underneath it back off rather than leaving half of the keys for
        /// the level beneath sitting under an unrelated message.
        /// </param>
        private void SetHint(string text, string more = "")
        {
            Set(_hint, text);
            Set(_hintMore, more);
        }

        private static void Set(Label label, string text)
        {
            if (label.Text == text)
            {
                return;
            }

            label.Text = text;
            label.SetNeedsDraw();
        }

        private static Label Line(string text, Pos y)
        {
            var label = new Label
            {
                X = 0,
                Y = y,
                Width = Dim.Fill(),
                Height = 1,
                Text = text,
            };

            label.SetScheme(Theme.CreateScheme());
            return label;
        }

        private static IReadOnlyList<SaveEntry> ReadSaves(out string? failure)
        {
            try
            {
                failure = null;
                return SavePaths.List();
            }
            catch (Exception ex) when (ex is SaveException or IOException or UnauthorizedAccessException)
            {
                // A save folder that cannot be listed must not stop a new game being started.
                failure = $"Could not read saves: {ex.Message}";
                return [];
            }
        }
    }
}
