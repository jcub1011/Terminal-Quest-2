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

        private readonly SaveListView _list;
        private readonly TextField _name;
        private readonly Label _error;

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
                Text = failure ?? "Up/Down and Enter to continue a save.  Esc quits.",
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
