using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

using TerminalQuest.Agents;
using TerminalQuest.Agents.LmStudio;
using TerminalQuest.Saves;
using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The settings screen: who narrates, and how to reach them.
    /// <para>
    /// Reached from <see cref="SaveMenuWindow"/> and only from there, because a session is built
    /// against one adapter and holds a process or a transcript for as long as it lives. Changing
    /// the answer partway through a game would mean tearing that down and rebuilding it, and the
    /// screen that decides it belongs before anything has been built.
    /// </para>
    /// <para>
    /// A navigator rather than a form. Levels of <see cref="SettingsPage"/> stack up - categories,
    /// then adapters, then one adapter's settings - and only the level the player is standing on
    /// is on screen. The view tree is built once and never changes; descending swaps the rows and
    /// nothing else.
    /// </para>
    /// <para>
    /// Owns no game logic beyond writing the file - it collects answers, saves them, and stops.
    /// </para>
    /// </summary>
    internal sealed class SettingsWindow : Window
    {
        /// <summary>The first row of the list, in the window's own coordinates.</summary>
        /// <remarks>
        /// The editor is dropped onto a row by number, so this has to agree with where the list
        /// actually starts. It is one because the breadcrumb takes the top line.
        /// </remarks>
        private const int ListTop = 1;

        private const string EditHint = "Enter commits.  Esc discards the edit.";

        /// <summary>How long the model list may take before the screen gives up on it.</summary>
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

        private readonly IApplication _app;
        private readonly AppSettings _original;
        private readonly AppSettings _draft;
        private readonly List<(SettingsPage Page, int Cursor)> _trail = [];

        private readonly BreadcrumbView _breadcrumb;
        private readonly MenuListView _list;
        private readonly Label _hint;
        private readonly TextField _editor;

        private int _editIndex = -1;
        private bool _probing;
        private bool _discardArmed;
        private CancellationTokenSource? _probe;

        public SettingsWindow(IApplication app, AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(settings);

            _app = app;
            _original = settings;

            // Edited on a copy, so backing out leaves the caller's settings as they were and
            // "discard" needs nothing more than dropping this.
            _draft = new AppSettings();
            _draft.CopyFrom(settings);

            Title = "Settings";
            BorderStyle = LineStyle.Rounded;
            SetScheme(Theme.CreateScheme());

            _breadcrumb = new BreadcrumbView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
            };

            _list = new MenuListView
            {
                X = 0,
                Y = Pos.Bottom(_breadcrumb),
                Width = Dim.Fill(),
                Height = Dim.Fill() - 2,
            };

            _hint = new Label
            {
                X = 0,
                Y = Pos.Bottom(_list),
                Width = Dim.Fill(),
                Height = 1,
            };
            _hint.SetScheme(Theme.CreateScheme());

            // One editor, moved to whichever row is being typed into, rather than a field per row:
            // the rows are drawn, not built, so there is nothing to give a permanent field to.
            _editor = new TextField
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 1,
                Visible = false,
                CanFocus = false,
            };
            _editor.SetScheme(Theme.CreateScheme());
            _editor.Accepting += OnEditorAccepting;

            // Added last so it draws over the row it is sitting on.
            Add(_breadcrumb, _list, _hint, _editor);

            _trail.Add((new SettingsTabsPage(_draft), 0));
            Refresh();

            // The window has no visible focusable child, so it has to hold focus itself for the
            // keys to arrive at all. Asked for here rather than in the constructor for the same
            // reason as the character screen: that is too early to stick.
            Initialized += (_, _) => SetFocus();
        }

        /// <summary>What the player settled on and this screen saved, or null when they left.</summary>
        public AppSettings? Chosen { get; private set; }

        /// <summary>Raised once the settings are saved.</summary>
        public event Action? Done;

        /// <summary>Raised when the player leaves without keeping anything.</summary>
        public event Action? Cancelled;

        private SettingsPage Page => _trail[^1].Page;

        protected override bool OnKeyDown(Key key)
        {
            // Two presses of a leaving key in a row is the only thing that discards; anything else
            // in between means the player was not answering that question.
            if (key != Key.Esc && key != Key.CursorLeft)
            {
                _discardArmed = false;
            }

            return _editIndex >= 0 ? OnKeyDownEditing(key) : OnKeyDownBrowsing(key);
        }

        /// <summary>
        /// Keys while a row is being typed into.
        /// <para>
        /// The focused editor has already had its turn by the time this runs - Terminal.Gui offers
        /// a key to the focused subview first - so everything printable, and Enter, is gone before
        /// we see it. What reaches here is what a text field has no use for, and the important one
        /// is Esc: it has to close the editor and stop, or a single press would close the editor
        /// and back out of the page underneath it in one go.
        /// </para>
        /// </summary>
        private bool OnKeyDownEditing(Key key)
        {
            if (key == Key.Esc)
            {
                CancelEdit();
                return true;
            }

            // Swallowed: up and down would leave the editor sitting over a row it is no longer
            // editing. Left and right belong to the text and the editor has already had them -
            // but a field ignores them at either end of what is typed, and one arriving here
            // would walk out of the page underneath, so they stop at this line either way.
            if (key == Key.CursorUp || key == Key.CursorDown
                || key == Key.CursorLeft || key == Key.CursorRight)
            {
                return true;
            }

            if (key == Key.Q.WithCtrl)
            {
                CancelEdit();
                Cancelled?.Invoke();
                return true;
            }

            if (key == Key.Enter.WithCtrl)
            {
                if (CommitEdit())
                {
                    Save();
                }

                return true;
            }

            return base.OnKeyDown(key);
        }

        private bool OnKeyDownBrowsing(Key key)
        {
            if (key == Key.Esc)
            {
                GoBack();
                return true;
            }

            if (key == Key.Q.WithCtrl)
            {
                Cancelled?.Invoke();
                return true;
            }

            if (key == Key.Enter.WithCtrl)
            {
                Save();
                return true;
            }

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

            // Left and Esc both walk up a level, and the settings screen is itself one level below
            // the start page - so from the top of the trail they leave, guarded by the same
            // unsaved-changes prompt either way. Nothing here can walk a player out of settings
            // they have not saved without asking.
            if (key == Key.CursorLeft)
            {
                GoBack();
                return true;
            }

            if (key == Key.CursorRight)
            {
                Open(_list.SelectedIndex);
                return true;
            }

            if (key == Key.Enter)
            {
                Activate(_list.SelectedIndex);
                return true;
            }

            if (key == Key.L.WithCtrl)
            {
                Reprobe();
                return true;
            }

            return base.OnKeyDown(key);
        }

        /// <summary>
        /// What Enter means, which depends on what the row is: a row that can be chosen is chosen,
        /// and a row that cannot is opened.
        /// </summary>
        private void Activate(int index)
        {
            if (Page.CanSelect(index))
            {
                SelectRow(index);
                return;
            }

            Open(index);
        }

        /// <summary>
        /// What Right means: go into this row, whatever going in amounts to here - a deeper page,
        /// a list the server has to be asked for, or the editor for a row that is typed into.
        /// </summary>
        private void Open(int index)
        {
            var page = Page;

            // Asked first: whether there is a list to open is the server's answer to give.
            if (page.NeedsProbe(index))
            {
                _ = ProbeAsync(index);
                return;
            }

            if (page.TryBeginEdit(index, out var text))
            {
                BeginEdit(index, text);
                return;
            }

            if (page.Enter(index) is { } child)
            {
                Push(child);
            }
        }

        private void SelectRow(int index)
        {
            if (Page.Select(index))
            {
                RefreshRows();
            }
        }

        private void Push(SettingsPage child)
        {
            _trail[^1] = (_trail[^1].Page, _list.SelectedIndex);
            _trail.Add((child, 0));
            Refresh();
        }

        /// <summary>
        /// Esc: off one layer, never two. The editor is a layer, then each page, then the screen.
        /// </summary>
        private void GoBack()
        {
            if (_trail.Count > 1)
            {
                CancelProbe();
                _trail.RemoveAt(_trail.Count - 1);
                Refresh();
                return;
            }

            if (!IsDirty())
            {
                Cancelled?.Invoke();
                return;
            }

            if (!_discardArmed)
            {
                _discardArmed = true;
                Fail("Unsaved changes.  Ctrl+Enter saves, Esc or Left again discards.");
                return;
            }

            Cancelled?.Invoke();
        }

        private bool IsDirty() =>
            _draft.Provider != _original.Provider
            || !string.Equals(_draft.ClaudeModel, _original.ClaudeModel, StringComparison.Ordinal)
            || !string.Equals(_draft.LmStudioBaseUrl, _original.LmStudioBaseUrl, StringComparison.Ordinal)
            || !string.Equals(_draft.LmStudioModel, _original.LmStudioModel, StringComparison.Ordinal)
            || !string.Equals(_draft.LmStudioApiKey, _original.LmStudioApiKey, StringComparison.Ordinal);

        private void BeginEdit(int index, string text)
        {
            _editIndex = index;

            _editor.Secret = Page.IsSecret(index);
            _editor.Text = text;
            _editor.X = Math.Max(0, Page.ValueColumn);
            _editor.Y = ListTop + index;
            _editor.CanFocus = true;
            _editor.Visible = true;
            _editor.SetFocus();

            SetHint(EditHint);
        }

        /// <summary>Puts the editor away, taking the focus off it before it goes.</summary>
        private void EndEdit()
        {
            SetFocus();
            _editor.Visible = false;
            _editor.CanFocus = false;
            _editor.Secret = false;
            _editIndex = -1;
            _list.SetNeedsDraw();
        }

        private void CancelEdit()
        {
            EndEdit();
            ShowHint();
        }

        /// <summary>Takes what was typed. False when the page refused it and the editor stays open.</summary>
        private bool CommitEdit()
        {
            if (_editIndex < 0)
            {
                return true;
            }

            var index = _editIndex;

            if (Page.Commit(index, _editor.Text?.Trim() ?? string.Empty) is { } error)
            {
                Fail(error);
                return false;
            }

            EndEdit();
            RefreshRows();
            ShowHint();
            return true;
        }

        /// <summary>Enter inside the editor. Handled either way, so it never reaches the window.</summary>
        private void OnEditorAccepting(object? sender, CommandEventArgs e)
        {
            e.Handled = true;
            CommitEdit();
        }

        private void Save()
        {
            var address = _draft.LmStudioBaseUrl?.Trim() ?? string.Empty;

            // Only checked for the adapter that will actually use it: a half-typed address left
            // behind on the Claude side is not a reason to refuse to save.
            if (_draft.Provider == AgentProvider.LmStudio && !AppSettings.IsAddress(address))
            {
                GoToAddress();
                Fail("The LM Studio address needs to be a full URL, such as http://localhost:1234/v1");
                return;
            }

            // Written here rather than by the host, so a disk that will not take it is reported on
            // the screen the player is standing on and can do something about.
            try
            {
                SettingsStore.Write(_draft);
            }
            catch (SaveException ex)
            {
                Fail(ex.Message);
                return;
            }

            Chosen = _draft;
            Done?.Invoke();
        }

        /// <summary>
        /// Puts the player in front of the address, wherever they were when they tried to save.
        /// </summary>
        private void GoToAddress()
        {
            CancelProbe();
            EndEdit();

            _trail.Clear();
            _trail.Add((new SettingsTabsPage(_draft), 0));
            _trail.Add((new SettingsAdaptersPage(_draft), 1));
            _trail.Add((new SettingsLmStudioPage(_draft), SettingsLmStudioPage.AddressRow));
            Refresh();
        }

        /// <summary>Ctrl+L: ask the server again, replacing any list already on screen.</summary>
        private void Reprobe()
        {
            if (Page is SettingsLmModelsPage)
            {
                _trail.RemoveAt(_trail.Count - 1);
                Refresh();
            }

            if (Page is SettingsLmStudioPage)
            {
                _ = ProbeAsync(SettingsLmStudioPage.ModelRow);
            }
        }

        /// <summary>
        /// Asks the configured address what it is serving, and opens the answer as a list.
        /// </summary>
        /// <remarks>
        /// This is the screen's connection test as much as it is a convenience. A player who came
        /// here to switch to LM Studio finds out now whether the server is up, rather than on a
        /// first turn that fails into the transcript - and when it is not up, they are handed the
        /// editor so the model can still be typed in by hand.
        /// </remarks>
        private async Task ProbeAsync(int index)
        {
            if (_probing)
            {
                return;
            }

            var address = _draft.LmStudioBaseUrl?.Trim() ?? string.Empty;

            if (!AppSettings.IsAddress(address))
            {
                BeginEdit(SettingsLmStudioPage.AddressRow, address);
                Fail("Fill in the LM Studio address first.");
                return;
            }

            var origin = Page;
            var key = _draft.LmStudioApiKey?.Trim() ?? string.Empty;

            CancelProbe();

            // Owned by this call and disposed by it. Backing out of the page cancels the token but
            // deliberately does not dispose the source, because the request is still holding it -
            // tearing it down underneath a live HttpClient is how that turns into an
            // ObjectDisposedException on a background thread nobody is catching.
            var probe = new CancellationTokenSource();
            _probe = probe;

            _probing = true;
            Fail($"Asking {address}...");

            try
            {
                var models = await Task
                    .Run(() => LmStudioModels.ListAsync(address, key, ProbeTimeout, probe.Token), probe.Token)
                    .ConfigureAwait(false);

                _app.Invoke(() => ShowModels(origin, index, models));
            }
            catch (AgentException ex)
            {
                // Only the headline: the detail is a response body, and this is one row.
                _app.Invoke(() => ProbeFailed(origin, index, FirstLine(ex.Message)));
            }
            catch (OperationCanceledException)
            {
                // The player walked away from the page. Nothing left to tell them.
            }
            finally
            {
                _probing = false;

                if (ReferenceEquals(_probe, probe))
                {
                    _probe = null;
                }

                probe.Dispose();
            }
        }

        private void ShowModels(SettingsPage origin, int index, IReadOnlyList<string> models)
        {
            // The answer can arrive after the player has gone somewhere else, and dropping a list
            // of models onto whatever page they are on now would be worse than saying nothing.
            if (!ReferenceEquals(Page, origin))
            {
                return;
            }

            if (models.Count == 0)
            {
                ProbeFailed(origin, index, "The server answered but listed no models. Load one in LM Studio.");
                return;
            }

            Push(new SettingsLmModelsPage(_draft, models));
        }

        private void ProbeFailed(SettingsPage origin, int index, string message)
        {
            if (!ReferenceEquals(Page, origin))
            {
                return;
            }

            // No list to offer, so fall back to letting them type it - the same thing this screen
            // asked for before it learned to go and look.
            if (origin.TryBeginEdit(index, out var text))
            {
                BeginEdit(index, text);
            }

            Fail(message);
        }

        /// <summary>
        /// Tells an in-flight probe to stop. Disposing it is the job of whoever started it.
        /// </summary>
        private void CancelProbe()
        {
            _probe?.Cancel();
            _probe = null;
        }

        /// <summary>Redraws everything after the trail has changed.</summary>
        private void Refresh()
        {
            var page = Page;
            var crumbs = new string[_trail.Count];

            for (var index = 0; index < _trail.Count; index++)
            {
                crumbs[index] = _trail[index].Page.Title;
            }

            _breadcrumb.Crumbs = crumbs;
            _list.ValueColumn = page.ValueColumn;
            _list.Rows = page.Rows;
            _list.SelectedIndex = _trail[^1].Cursor;
            ShowHint();
        }

        /// <summary>Redraws the rows after a change on this page, leaving the cursor where it is.</summary>
        private void RefreshRows()
        {
            var cursor = _list.SelectedIndex;
            _list.Rows = Page.Rows;
            _list.SelectedIndex = cursor;
        }

        private void ShowHint() => SetHint(Page.Hint);

        private void Fail(string message) => SetHint(message);

        private void SetHint(string text)
        {
            if (_hint.Text == text)
            {
                return;
            }

            _hint.Text = text;
            _hint.SetNeedsDraw();
        }

        private static string FirstLine(string message)
        {
            var end = message.IndexOf('\n');
            return end < 0 ? message : message[..end].TrimEnd();
        }
    }
}
