using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The startup screen: pick a save, or name a new one.
    /// <para>
    /// This runs before the narrator process exists, because the save folder has to be known
    /// before the MCP server can be pointed at it - the choice made here becomes a command-line
    /// argument to a child process, so it cannot be deferred.
    /// </para>
    /// <para>
    /// Owns no game logic. It sets <see cref="Chosen"/> and stops; the host decides what opening
    /// a save means.
    /// </para>
    /// </summary>
    internal sealed class SaveMenuWindow : Window
    {
        private const int InputHeight = 3;
        private const int HintHeight = 1;

        private const string Hint =
            "Up/Down and Enter to continue a save.  Del deletes one.  Esc quits.";

        private readonly SaveListView _list;
        private readonly TextField _name;
        private readonly Label _error;

        /// <summary>
        /// The save the next Del keypress will destroy, or null when nothing is half-confirmed.
        /// <para>
        /// Held by name rather than by index: the list is rebuilt after every delete, and an index
        /// would quietly come to mean a different save.
        /// </para>
        /// </summary>
        private string? _pendingDelete;

        public SaveMenuWindow()
        {
            Title = "Terminal Quest";
            BorderStyle = LineStyle.Rounded;
            SetScheme(Theme.CreateScheme());

            _list = new SaveListView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill() - (InputHeight + HintHeight),
                Saves = ReadSaves(out var failure),
            };

            _error = new Label
            {
                X = 0,
                Y = Pos.Bottom(_list),
                Width = Dim.Fill(),
                Height = HintHeight,
                Text = failure ?? Hint,
            };
            _error.SetScheme(Theme.CreateScheme());

            _name = new TextField
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
            };
            _name.Accepting += OnNameAccepting;

            var frame = new FrameView
            {
                Title = "new save name",
                X = 0,
                Y = Pos.Bottom(_error),
                Width = Dim.Fill(),
                Height = InputHeight,
                BorderStyle = LineStyle.Rounded,
            };
            frame.Add(_name);

            Add(_list, _error, frame);
        }

        /// <summary>The save the player settled on, or null when they quit instead.</summary>
        public SaveStore? Chosen { get; private set; }

        /// <summary>Raised once a save is open and the game should start.</summary>
        public event Action? Done;

        /// <summary>Raised when the player leaves without starting anything.</summary>
        public event Action? Cancelled;

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Esc || key == Key.Q.WithCtrl)
            {
                Cancelled?.Invoke();
                return true;
            }

            // Del means "delete the highlighted save" only while the name box is empty. With text
            // in it the player is editing, and Del has to keep its ordinary meaning there - the
            // same empty-box convention that decides what Enter does.
            if (key == Key.Delete && (_name.Text?.Length ?? 0) == 0)
            {
                Delete();
                return true;
            }

            // Any key that is not Del abandons a half-confirmed delete, so a pending confirmation
            // cannot survive the player moving on and be triggered by an unrelated Del later.
            CancelPendingDelete();

            // The arrows drive the list even though focus lives in the name field, so continuing
            // an existing save never needs a Tab first.
            if (key == Key.CursorUp)
            {
                _list.MoveSelection(-1);
                return true;
            }

            if (key == Key.CursorDown)
            {
                _list.MoveSelection(1);
                return true;
            }

            return base.OnKeyDown(key);
        }

        /// <summary>
        /// Del once asks, Del again destroys. A save is a playthrough, and there is no undo - but
        /// a modal here would be the only one in the game, so the confirmation is the second press.
        /// </summary>
        private void Delete()
        {
            if (_list.Selected is not { } selected)
            {
                Fail("There is no save to delete.");
                return;
            }

            if (!SaveStore.Matches(_pendingDelete, selected.Name))
            {
                _pendingDelete = selected.Name;
                Fail($"Delete '{selected.Name}' and everything in it? Del again to confirm.");
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

            _list.Saves = ReadSaves(out var failure);
            Fail(failure ?? $"Deleted '{selected.Name}'.");
        }

        private void CancelPendingDelete()
        {
            if (_pendingDelete is null)
            {
                return;
            }

            _pendingDelete = null;
            Fail(Hint);
        }

        public void FocusInput() => _name.SetFocus();

        private void OnNameAccepting(object? sender, CommandEventArgs e)
        {
            // Handled either way: Enter must never propagate up and trigger a default accept on
            // the window itself.
            e.Handled = true;

            var typed = _name.Text?.Trim() ?? string.Empty;

            // An empty box means "continue what is highlighted"; text in it means "make this one".
            if (typed.Length == 0)
            {
                if (_list.Selected is not { } selected)
                {
                    Fail("Type a name to start a new save.");
                    return;
                }

                Open(selected.Name);
                return;
            }

            if (!SavePaths.IsValidName(typed))
            {
                Fail("That name will not work as a folder. Avoid \\ / : * ? \" < > |");
                return;
            }

            if (SavePaths.Exists(typed))
            {
                // Loading it is almost certainly what was meant, and is not destructive either way.
                Open(typed);
                return;
            }

            Open(typed);
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

        private void Fail(string message)
        {
            _error.Text = message;
            _error.SetNeedsDraw();
        }

        private static IReadOnlyList<SaveMetadata> ReadSaves(out string? failure)
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
