using Terminal.Gui.ViewBase;

using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The provider picker on the settings screen.
    /// <para>
    /// A third near-sibling of <see cref="SaveListView"/> and <see cref="ClassListView"/>, drawn
    /// the same way and factored no further, for the reason given on <see cref="ClassListView"/>:
    /// the columns differ, and a shared control would take a formatter callback for the sake of
    /// three call sites.
    /// </para>
    /// </summary>
    internal sealed class ProviderListView : ThemedView
    {
        private static readonly (AgentProvider Provider, string Name, string Detail)[] Providers =
        [
            (AgentProvider.ClaudeCode, "Claude Code", "the claude CLI, run as a child process"),
            (AgentProvider.LmStudio, "LM Studio", "a model on this machine, over HTTP"),
        ];

        private int _selectedIndex;

        /// <summary>The highlighted provider.</summary>
        public AgentProvider Selected => Providers[_selectedIndex].Provider;

        /// <summary>Puts the highlight on a provider, for opening the screen on the current one.</summary>
        public void Select(AgentProvider provider)
        {
            var index = Array.FindIndex(Providers, entry => entry.Provider == provider);

            if (index < 0)
            {
                return;
            }

            _selectedIndex = index;
            SetNeedsDraw();
        }

        /// <summary>Moves the highlight, clamping at both ends rather than wrapping.</summary>
        public void MoveSelection(int delta)
        {
            _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, Providers.Length - 1);
            SetNeedsDraw();
        }

        protected override bool OnDrawingContent(DrawContext? context)
        {
            var width = Viewport.Width;
            var height = Viewport.Height;

            if (width <= 0 || height <= 0)
            {
                return true;
            }

            BeginPaint(width, height);

            for (var row = 0; row < height && row < Providers.Length; row++)
            {
                var (_, name, detail) = Providers[row];
                var isSelected = row == _selectedIndex;

                Move(0, row);
                SetRole(TextRole.System);
                AddStr(isSelected ? "> " : "  ");

                SetRole(isSelected ? TextRole.Command : TextRole.Normal);
                AddStr(Fit(name, Math.Max(0, width - 2)));

                var column = width - detail.Length;
                if (column > name.Length + 3)
                {
                    Move(column, row);
                    SetRole(TextRole.System);
                    AddStr(detail);
                }
            }

            return true;
        }

        private static string Fit(string text, int width) =>
            text.Length <= width ? text : text[..Math.Max(0, width)];
    }
}
