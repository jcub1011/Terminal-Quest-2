using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The commands a half-typed slash could still turn into, floating over the foot of the
    /// transcript while the player types.
    /// <para>
    /// A third hand-drawn list rather than a fourth caller of <see cref="MenuListView"/>. That one
    /// deliberately has no scroll window - the settings screen relies on the drawn row and its
    /// index being the same number so it can drop an editor onto a row - and a bare <c>/</c>
    /// matches every command there is, which is more rows than the strip above the box can show.
    /// The scrolling belongs here, where it costs nothing, rather than in a shared control where
    /// it would cost the settings screen its one invariant.
    /// </para>
    /// <para>
    /// It offers and never decides: what is highlighted goes nowhere until the player presses a
    /// key for it, so a suggestion cannot quietly become the command that runs.
    /// </para>
    /// </summary>
    internal sealed class CommandSuggestionView : ThemedView
    {
        /// <summary>Two columns for the cursor, matching the menu and save lists.</summary>
        private const int MarkerWidth = 2;

        /// <summary>Where the summaries line up, measured from the left edge of the strip.</summary>
        private const int SummaryColumn = 24;

        /// <summary>What a row needs before the summary is worth drawing at all.</summary>
        private const int MinimumForSummary = SummaryColumn + 12;

        private IReadOnlyList<PlayerCommandInfo> _suggestions = [];
        private int _selectedIndex;

        /// <summary>What to offer. Setting it puts the cursor back on the first row.</summary>
        public IReadOnlyList<PlayerCommandInfo> Suggestions
        {
            get => _suggestions;

            set
            {
                _suggestions = value ?? [];
                _selectedIndex = 0;
                SetNeedsDraw();
            }
        }

        /// <summary>
        /// Whether the rows are still a question or have become an answer.
        /// <para>
        /// While the player is typing the name they are choosing between commands, and the cursor
        /// and the completing keys mean something. Once the name is settled the strip stays up -
        /// it is where <c>/delete &lt;name&gt;</c> goes on saying it wants a name - but it is a
        /// reminder rather than a menu, so it draws no cursor and offers nothing to complete.
        /// </para>
        /// </summary>
        public bool IsChoosing { get; set; } = true;

        /// <summary>
        /// The command Tab, Right or Enter would complete to, or null when there is nothing to
        /// take - including when the strip has stopped being a choice.
        /// <para>
        /// Null in that second case is what stops Right, at the end of a line, from replacing a
        /// half-typed argument with the command it belongs to.
        /// </para>
        /// </summary>
        public PlayerCommandInfo? Selected =>
            IsChoosing && _selectedIndex >= 0 && _selectedIndex < _suggestions.Count
                ? _suggestions[_selectedIndex]
                : null;

        /// <summary>Moves the cursor, clamping at both ends rather than wrapping.</summary>
        public void MoveSelection(int delta)
        {
            _selectedIndex = Math.Clamp(_selectedIndex + delta, 0, Math.Max(0, _suggestions.Count - 1));
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

            // Keep the highlight on screen when there are more commands than rows to show them in.
            var first = Math.Max(0, Math.Min(_selectedIndex - (height / 2), _suggestions.Count - height));

            for (var row = 0; row < height && first + row < _suggestions.Count; row++)
            {
                DrawRow(_suggestions[first + row], row, width, IsChoosing && first + row == _selectedIndex);
            }

            return true;
        }

        /// <param name="isCursor">
        /// Whether this is the row a completing key would take. A settled command is drawn bright
        /// too - it is the one in play - but without the arrow, which would promise a choice that
        /// is no longer on offer.
        /// </param>
        private void DrawRow(PlayerCommandInfo command, int row, int width, bool isCursor)
        {
            var isSelected = isCursor || !IsChoosing;

            Move(0, row);
            SetRole(TextRole.System);
            AddStr(isCursor ? "> " : "  ");

            // The summary is dropped rather than crushed against the name it belongs to: which
            // commands are on offer is the one thing this strip cannot do without.
            var hasSummary = width >= MinimumForSummary && command.Summary.Length > 0;

            // Truncated short of the summary column when there is one, so a long name cannot run
            // into a description that belongs to the same row and read as one word.
            var nameWidth = hasSummary ? SummaryColumn - MarkerWidth - 1 : width - MarkerWidth;

            SetRole(isSelected ? TextRole.Command : TextRole.Normal);
            AddStr(Fit(command.Usage, Math.Max(0, nameWidth)));

            if (!hasSummary)
            {
                return;
            }

            Move(SummaryColumn, row);
            SetRole(TextRole.System);
            AddStr(Fit(command.Summary, width - SummaryColumn));
        }

        private static string Fit(string text, int width) =>
            text.Length <= width ? text : text[..Math.Max(0, width)];
    }
}
