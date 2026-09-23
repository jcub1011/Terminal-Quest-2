using System.Collections.ObjectModel;

using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The game status pane.
    /// Modernized to use Terminal.Gui built-in <see cref="ProgressBar"/>, <see cref="ListView"/>,
    /// <see cref="FrameView"/>, and <see cref="Label"/> controls.
    /// </summary>
    internal sealed class StatusView : View
    {
        private readonly GameState _state;

        private readonly FrameView _vitalsFrame;
        private readonly Label _hpCaption;
        private readonly Label _hpValue;
        private readonly ProgressBar _hpBar;
        private readonly Label _turnCaption;
        private readonly Label _turnValue;

        private readonly FrameView _attributesFrame;
        private readonly Label _attributesLabel;

        private readonly FrameView _inventoryFrame;
        private readonly Label _moneyCaption;
        private readonly Label _moneyValue;
        private readonly InventoryView _inventoryView;

        private readonly FrameView _sessionFrame;
        private readonly Label _contextCaption;
        private readonly Label _contextValue;
        private readonly ProgressBar _contextBar;
        private readonly Label _metricsLabel;

        /// <summary>
        /// Raised when the player clicks on an inventory entity in the Pack & Purse panel.
        /// </summary>
        public event Action<string>? EntityClicked;

        /// <summary>
        /// Raised when the player presses Esc while the pack has focus. Forwarded from the
        /// inventory view so the host can return focus to the input line.
        /// </summary>
        public event Action? InventoryExitRequested
        {
            add => _inventoryView.ExitRequested += value;
            remove => _inventoryView.ExitRequested -= value;
        }

        public StatusView(GameState state)
        {
            _state = state;
            CanFocus = false;
            SetScheme(Theme.CreateScheme());

            // 1. Vitals Frame
            _vitalsFrame = new FrameView
            {
                Title = "Vitals",
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = 5,
                BorderStyle = LineStyle.Rounded,
            };
            _vitalsFrame.SetScheme(Theme.CreateScheme());

            _hpCaption = new Label { X = 1, Y = 0, Width = 3, Text = "HP:" };
            _hpCaption.SetScheme(Theme.LabelScheme(TextRole.Hint));

            _hpValue = new Label { X = 5, Y = 0, Width = Dim.Fill() - 6, Text = "-" };
            _hpValue.SetScheme(Theme.LabelScheme(TextRole.Normal));

            _hpBar = new ProgressBar
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = 1,
                Fraction = 0f,
            };
            _hpBar.SetScheme(Theme.CreateScheme());

            _turnCaption = new Label { X = 1, Y = 2, Width = 5, Text = "Turn:" };
            _turnCaption.SetScheme(Theme.LabelScheme(TextRole.Hint));

            _turnValue = new Label { X = 7, Y = 2, Width = Dim.Fill() - 8, Text = "0" };
            _turnValue.SetScheme(Theme.LabelScheme(TextRole.Normal));

            _vitalsFrame.Add(_hpCaption, _hpValue, _hpBar, _turnCaption, _turnValue);

            // 2. Attributes Frame
            _attributesFrame = new FrameView
            {
                Title = "Attributes",
                X = 0,
                Y = Pos.Bottom(_vitalsFrame),
                Width = Dim.Fill(),
                Height = 5,
                BorderStyle = LineStyle.Rounded,
            };
            _attributesFrame.SetScheme(Theme.CreateScheme());

            _attributesLabel = new Label
            {
                X = 1,
                Y = 0,
                Width = Dim.Fill() - 2,
                Height = 3,
                Text = string.Empty,
            };
            _attributesLabel.SetScheme(Theme.LabelScheme(TextRole.Normal));
            _attributesFrame.Add(_attributesLabel);

            // 3. Inventory Frame
            _inventoryFrame = new FrameView
            {
                Title = "Pack & Purse",
                X = 0,
                Y = Pos.Bottom(_attributesFrame),
                Width = Dim.Fill(),
                Height = Dim.Fill() - 7,
                BorderStyle = LineStyle.Rounded,
            };
            _inventoryFrame.SetScheme(Theme.CreateScheme());

            _moneyCaption = new Label { X = 1, Y = 0, Width = 5, Text = "Gold:" };
            _moneyCaption.SetScheme(Theme.LabelScheme(TextRole.Hint));

            _moneyValue = new Label { X = 7, Y = 0, Width = Dim.Fill() - 8, Text = "0 gp" };
            _moneyValue.SetScheme(Theme.LabelScheme(TextRole.Item));

            _inventoryView = new InventoryView
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = Dim.Fill(),
            };
            _inventoryView.SetScheme(Theme.CreateScheme());
            _inventoryView.EntityClicked += entityId => EntityClicked?.Invoke(entityId);

            _inventoryFrame.Add(_moneyCaption, _moneyValue, _inventoryView);

            // 4. Session & Context Frame
            _sessionFrame = new FrameView
            {
                Title = "Session",
                X = 0,
                Y = Pos.Bottom(_inventoryFrame),
                Width = Dim.Fill(),
                Height = 7,
                BorderStyle = LineStyle.Rounded,
            };
            _sessionFrame.SetScheme(Theme.CreateScheme());

            _contextCaption = new Label { X = 1, Y = 0, Width = 8, Text = "Context:" };
            _contextCaption.SetScheme(Theme.LabelScheme(TextRole.Hint));

            _contextValue = new Label { X = 10, Y = 0, Width = Dim.Fill() - 11, Text = "-" };
            _contextValue.SetScheme(Theme.LabelScheme(TextRole.Normal));

            _contextBar = new ProgressBar
            {
                X = 1,
                Y = 1,
                Width = Dim.Fill() - 2,
                Height = 1,
                Fraction = 0f,
            };
            _contextBar.SetScheme(Theme.CreateScheme());

            _metricsLabel = new Label { X = 1, Y = 3, Width = Dim.Fill() - 2, Text = "$0.0000 | 0ms" };
            _metricsLabel.SetScheme(Theme.LabelScheme(TextRole.Hint));

            _sessionFrame.Add(_contextCaption, _contextValue, _contextBar, _metricsLabel);

            Add(_vitalsFrame, _attributesFrame, _inventoryFrame, _sessionFrame);

            Refresh();
        }

        public void Refresh()
        {
            // Update Vitals
            _vitalsFrame.Title = string.IsNullOrWhiteSpace(_state.PlayerName)
                ? "Vitals"
                : $"Vitals - {_state.PlayerName}";

            var hasPlayer = _state.MaxHealth > 0;
            var isHurt = hasPlayer && _state.Health * 4 <= _state.MaxHealth;
            _hpValue.Text = hasPlayer ? $"{_state.Health} / {_state.MaxHealth}" : "-";
            _hpValue.SetScheme(Theme.LabelScheme(isHurt ? TextRole.Danger : TextRole.Normal));
            if (hasPlayer)
            {
                _hpBar.Fraction = Math.Clamp((float)_state.Health / _state.MaxHealth, 0f, 1f);
                _hpBar.SetScheme(isHurt ? Theme.LabelScheme(TextRole.Danger) : Theme.CreateScheme());
            }
            _turnValue.Text = $"{_state.Turn}";

            // Update Attributes (2 columns)
            if (_state.Attributes.Count > 0)
            {
                var lines = new List<string>();
                for (var i = 0; i < _state.Attributes.Count; i += 2)
                {
                    var a1 = _state.Attributes[i];
                    var col1 = $"{a1.Label}: {a1.Score,2}";
                    if (i + 1 < _state.Attributes.Count)
                    {
                        var a2 = _state.Attributes[i + 1];
                        var col2 = $"{a2.Label}: {a2.Score,2}";
                        lines.Add($"{col1.PadRight(10)} {col2}");
                    }
                    else
                    {
                        lines.Add(col1);
                    }
                }
                _attributesLabel.Text = string.Join("\n", lines);
                _attributesLabel.SetScheme(Theme.LabelScheme(TextRole.Normal));
            }
            else
            {
                _attributesLabel.Text = "(No attributes)";
                _attributesLabel.SetScheme(Theme.LabelScheme(TextRole.Hint));
            }

            // Update Inventory
            _moneyValue.Text = $"{_state.Money} gp";
            _inventoryView.SetItems(_state.Inventory);

            // Update Session & Context
            if (_state.ContextTokens > 0)
            {
                if (_state.ContextWindowTokens > 0)
                {
                    var pct = (int)Math.Clamp(_state.ContextTokens * 100L / _state.ContextWindowTokens, 0, 100);
                    _contextValue.Text = $"{FormatTokens(_state.ContextTokens)} ({pct}%)";
                    _contextBar.Fraction = Math.Clamp((float)_state.ContextTokens / _state.ContextWindowTokens, 0f, 1f);
                    var alert = pct >= 95 ? TextRole.Danger : pct >= 80 ? TextRole.Important : TextRole.Normal;
                    _contextValue.SetScheme(Theme.LabelScheme(alert));
                    _contextBar.SetScheme(alert == TextRole.Normal ? Theme.CreateScheme() : Theme.LabelScheme(alert));
                }
                else
                {
                    _contextValue.Text = FormatTokens(_state.ContextTokens);
                    _contextValue.SetScheme(Theme.LabelScheme(TextRole.Normal));
                    _contextBar.Fraction = 0f;
                    _contextBar.SetScheme(Theme.CreateScheme());
                }
            }
            else
            {
                _contextValue.Text = "-";
                _contextValue.SetScheme(Theme.LabelScheme(TextRole.Normal));
                _contextBar.Fraction = 0f;
                _contextBar.SetScheme(Theme.CreateScheme());
            }

            var costStr = $"${_state.CostUsd:F4}";
            var durationStr = _state.LastDurationMs > 0 ? $"{_state.LastDurationMs}ms" : "-";
            _metricsLabel.Text = $"Cost: {costStr} | Latency: {durationStr}";

            SetNeedsDraw();
        }

        /// <summary>
        /// Abbreviates a token count to five columns at most.
        /// </summary>
        internal static string FormatTokens(int tokens)
        {
            if (tokens < 1_000)
            {
                return tokens.ToString();
            }

            if (tokens < 1_000_000)
            {
                return $"{tokens / 1_000}k";
            }

            return tokens < 10_000_000
                ? $"{tokens / 1_000_000.0:F1}M"
                : $"{tokens / 1_000_000}M";
        }

        /// <summary>
        /// How many of <paramref name="width"/> cells to fill for <paramref name="used"/> tokens of <paramref name="window"/>.
        /// </summary>
        internal static int BarFill(int used, int window, int width)
        {
            if (used <= 0 || window <= 0 || width <= 0)
            {
                return 0;
            }

            if (used >= window)
            {
                return width;
            }

            if (width == 1)
            {
                return 0;
            }

            var fill = (int)Math.Round((double)used / window * width, MidpointRounding.AwayFromZero);

            return Math.Clamp(fill, 1, width - 1);
        }
    }
}
