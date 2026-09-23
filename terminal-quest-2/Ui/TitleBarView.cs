using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The top title bar showing the game/save title on the left and the current location on the right.
    /// Built using Terminal.Gui built-in <see cref="Label"/> controls.
    /// </summary>
    internal sealed class TitleBarView : View
    {
        private const string GameTitle = "Terminal Quest";

        private readonly GameState _state;
        private readonly Label _titlePrefixLabel;
        private readonly Label _saveNameLabel;
        private readonly Label _locationLabel;

        public TitleBarView(GameState state)
        {
            _state = state;
            CanFocus = false;
            Height = 1;
            SetScheme(Theme.CreateScheme());

            _titlePrefixLabel = new Label
            {
                X = 0,
                Y = 0,
                Width = GameTitle.Length,
                Height = 1,
                Text = GameTitle,
            };
            _titlePrefixLabel.SetScheme(Theme.LabelScheme(TextRole.Hint));

            _saveNameLabel = new Label
            {
                X = GameTitle.Length,
                Y = 0,
                Width = Dim.Percent(50) - GameTitle.Length,
                Height = 1,
                Text = string.Empty,
            };
            _saveNameLabel.SetScheme(Theme.LabelScheme(TextRole.Command));

            _locationLabel = new Label
            {
                X = Pos.Percent(50),
                Y = 0,
                Width = Dim.Percent(50),
                Height = 1,
                TextAlignment = Alignment.End,
                Text = "nowhere",
            };
            var locationScheme = new Scheme
            {
                Normal = Theme.Attr(TextRole.Place),
                Focus = Theme.Attr(TextRole.Place),
                HotNormal = Theme.Attr(TextRole.Place),
                HotFocus = Theme.Attr(TextRole.Place),
            };
            _locationLabel.SetScheme(locationScheme);

            Add(_titlePrefixLabel, _saveNameLabel, _locationLabel);
            Refresh();
        }

        public void Refresh()
        {
            _saveNameLabel.Text = _state.SaveName.Length > 0
                ? $" - {_state.SaveName}"
                : string.Empty;

            _locationLabel.Text = _state.Location.Length > 0
                ? _state.Location
                : "nowhere";

            SetNeedsDraw();
        }
    }
}
