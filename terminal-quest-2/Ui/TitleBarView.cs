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
        private readonly GameState _state;
        private readonly Label _titleLabel;
        private readonly Label _locationLabel;

        public TitleBarView(GameState state)
        {
            _state = state;
            CanFocus = false;
            Height = 1;
            SetScheme(Theme.CreateScheme());

            _titleLabel = new Label
            {
                X = 0,
                Y = 0,
                Width = Dim.Percent(50),
                Height = 1,
                Text = "Terminal Quest",
            };
            _titleLabel.SetScheme(Theme.CreateScheme());

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

            Add(_titleLabel, _locationLabel);
            Refresh();
        }

        public void Refresh()
        {
            _titleLabel.Text = _state.SaveName.Length > 0
                ? $"Terminal Quest - {_state.SaveName}"
                : "Terminal Quest";

            _locationLabel.Text = _state.Location.Length > 0
                ? _state.Location
                : "nowhere";

            SetNeedsDraw();
        }
    }
}
