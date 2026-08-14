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
    /// against one provider and holds a process or a transcript for as long as it lives. Changing
    /// the answer partway through a game would mean tearing that down and rebuilding it, and the
    /// screen that decides it belongs before anything has been built.
    /// </para>
    /// <para>
    /// Owns no game logic beyond writing the file - it collects answers, saves them, and stops.
    /// </para>
    /// </summary>
    internal sealed class SettingsWindow : Window
    {
        private const int FieldHeight = 3;
        private const int FieldCount = 4;

        private const string Hint =
            "Up/Down picks a provider.  Enter moves down.  Ctrl+L lists LM Studio's models.  Ctrl+Enter saves.  Esc cancels.";

        /// <summary>How long the model list may take before the screen gives up on it.</summary>
        private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

        private readonly IApplication _app;
        private readonly ProviderListView _providers;
        private readonly Label _models;
        private readonly Label _hint;
        private readonly TextField _claudeModel;
        private readonly TextField _baseUrl;
        private readonly TextField _lmStudioModel;
        private readonly TextField _apiKey;

        private bool _probing;

        public SettingsWindow(IApplication app, AppSettings settings)
        {
            ArgumentNullException.ThrowIfNull(app);
            ArgumentNullException.ThrowIfNull(settings);

            _app = app;

            Title = "Settings";
            BorderStyle = LineStyle.Rounded;
            SetScheme(Theme.CreateScheme());

            _providers = new ProviderListView
            {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill() - ((FieldHeight * FieldCount) + 2),
            };

            _providers.Select(settings.Provider);

            _models = new Label
            {
                X = 0,
                Y = Pos.Bottom(_providers),
                Width = Dim.Fill(),
                Height = 1,
            };
            _models.SetScheme(Theme.CreateScheme());

            _hint = new Label
            {
                X = 0,
                Y = Pos.Bottom(_models),
                Width = Dim.Fill(),
                Height = 1,
                Text = Hint,
            };
            _hint.SetScheme(Theme.CreateScheme());

            // Every provider's fields are shown whichever one is highlighted. Hiding the others
            // would save four rows and cost the player the ability to see, in one glance, what
            // switching over would actually connect them to.
            _claudeModel = MakeField(settings.ClaudeModel);
            _baseUrl = MakeField(settings.LmStudioBaseUrl);
            _lmStudioModel = MakeField(settings.LmStudioModel);
            _apiKey = MakeField(settings.LmStudioApiKey);

            foreach (var field in new[] { _claudeModel, _baseUrl, _lmStudioModel, _apiKey })
            {
                field.Accepting += OnFieldAccepting;
            }

            var claudeFrame = Labelled(
                "Claude model (blank uses the CLI's own default)", _claudeModel, Pos.Bottom(_hint));
            var urlFrame = Labelled(
                "LM Studio address", _baseUrl, Pos.Bottom(claudeFrame));
            var modelFrame = Labelled(
                "LM Studio model (blank uses whichever is loaded)", _lmStudioModel, Pos.Bottom(urlFrame));
            var keyFrame = Labelled(
                "LM Studio API key (only checked if the server has authentication on)", _apiKey, Pos.Bottom(modelFrame));

            Add(_providers, _models, _hint, claudeFrame, urlFrame, modelFrame, keyFrame);

            Initialized += (_, _) => _claudeModel.SetFocus();
        }

        /// <summary>What the player settled on and this screen saved, or null when they cancelled.</summary>
        public AppSettings? Chosen { get; private set; }

        /// <summary>Raised once the settings are saved.</summary>
        public event Action? Done;

        /// <summary>Raised when the player leaves without changing anything.</summary>
        public event Action? Cancelled;

        protected override bool OnKeyDown(Key key)
        {
            if (key == Key.Esc || key == Key.Q.WithCtrl)
            {
                Cancelled?.Invoke();
                return true;
            }

            if (key == Key.Enter.WithCtrl)
            {
                Submit();
                return true;
            }

            if (key == Key.L.WithCtrl)
            {
                _ = ProbeModelsAsync();
                return true;
            }

            // The arrows drive the provider list wherever focus happens to be, matching the class
            // list on the character screen.
            if (key == Key.CursorUp)
            {
                _providers.MoveSelection(-1);
                return true;
            }

            if (key == Key.CursorDown)
            {
                _providers.MoveSelection(1);
                return true;
            }

            return base.OnKeyDown(key);
        }

        private static TextField MakeField(string text) => new()
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 1,
            Text = text,
        };

        private static FrameView Labelled(string title, View content, Pos y)
        {
            var frame = new FrameView
            {
                Title = title,
                X = 0,
                Y = y,
                Width = Dim.Fill(),
                Height = FieldHeight,
                BorderStyle = LineStyle.Rounded,
            };

            frame.Add(content);
            return frame;
        }

        /// <summary>Enter walks down the form; the last field submits, as on the character screen.</summary>
        private void OnFieldAccepting(object? sender, CommandEventArgs e)
        {
            e.Handled = true;

            var next = ReferenceEquals(sender, _claudeModel) ? _baseUrl
                : ReferenceEquals(sender, _baseUrl) ? _lmStudioModel
                : ReferenceEquals(sender, _lmStudioModel) ? _apiKey
                : null;

            if (next is null)
            {
                Submit();
                return;
            }

            ShowHint();
            next.SetFocus();
        }

        /// <summary>
        /// Asks the configured address what it is serving, and fills the model field in from the
        /// answer when it is still empty.
        /// </summary>
        /// <remarks>
        /// This is the screen's connection test as much as it is a convenience. A player who came
        /// here to switch to LM Studio finds out now whether the server is up, rather than on a
        /// first turn that fails into the transcript.
        /// </remarks>
        private async Task ProbeModelsAsync()
        {
            if (_probing)
            {
                return;
            }

            var address = _baseUrl.Text?.Trim() ?? string.Empty;

            if (address.Length == 0)
            {
                Fail("Fill in the LM Studio address first.");
                return;
            }

            _probing = true;
            Fail($"Asking {address}...");

            var key = _apiKey.Text?.Trim() ?? string.Empty;

            try
            {
                var models = await Task
                    .Run(() => LmStudioModels.ListAsync(address, key, ProbeTimeout))
                    .ConfigureAwait(false);

                _app.Invoke(() => ShowModels(models));
            }
            catch (AgentException ex)
            {
                // Only the headline: the detail is a response body, and this is one row.
                _app.Invoke(() => Fail(FirstLine(ex.Message)));
            }
            finally
            {
                _probing = false;
            }
        }

        private void ShowModels(IReadOnlyList<string> models)
        {
            if (models.Count == 0)
            {
                _models.Text = string.Empty;
                Fail("The server answered but listed no models. Load one in LM Studio.");
                _models.SetNeedsDraw();
                return;
            }

            // Filling a blank field is helpful; overwriting a name the player typed is not.
            if ((_lmStudioModel.Text?.Trim() ?? string.Empty).Length == 0)
            {
                _lmStudioModel.Text = models[0];
            }

            _models.Text = $"Serving: {string.Join(", ", models)}";
            _models.SetNeedsDraw();

            ShowHint();
        }

        private void Submit()
        {
            var provider = _providers.Selected;
            var address = _baseUrl.Text?.Trim() ?? string.Empty;

            // Only checked for the provider that will actually use it: a half-typed address left
            // behind on the Claude side is not a reason to refuse to save.
            if (provider == AgentProvider.LmStudio && !IsAddress(address))
            {
                Fail("The LM Studio address needs to be a full URL, such as http://localhost:1234/v1");
                _baseUrl.SetFocus();
                return;
            }

            var chosen = new AppSettings
            {
                Provider = provider,
                ClaudeModel = _claudeModel.Text?.Trim() ?? string.Empty,
                LmStudioBaseUrl = address,
                LmStudioModel = _lmStudioModel.Text?.Trim() ?? string.Empty,
                LmStudioApiKey = _apiKey.Text?.Trim() ?? string.Empty,
            };

            // Written here rather than by the host, so a disk that will not take it is reported on
            // the screen the player is standing on and can do something about.
            try
            {
                SettingsStore.Write(chosen);
            }
            catch (SaveException ex)
            {
                Fail(ex.Message);
                return;
            }

            Chosen = chosen;
            Done?.Invoke();
        }

        private static bool IsAddress(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        private static string FirstLine(string message)
        {
            var end = message.IndexOf('\n');
            return end < 0 ? message : message[..end].TrimEnd();
        }

        private void ShowHint()
        {
            if (_hint.Text == Hint)
            {
                return;
            }

            _hint.Text = Hint;
            _hint.SetNeedsDraw();
        }

        private void Fail(string message)
        {
            _hint.Text = message;
            _hint.SetNeedsDraw();
        }
    }
}
