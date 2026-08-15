using Terminal.Gui.Views;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The commands a half-typed slash could still turn into, floating over the foot of the
    /// transcript while the player types.
    /// <para>
    /// A <see cref="ListView"/> with a <see cref="StyledListSource{T}"/>, like the game's other
    /// lists. A bare <c>/</c> matches every command there is, which is more rows than the strip above
    /// the box can show, so the scrolling matters here - and it is the library's.
    /// </para>
    /// <para>
    /// It offers and never decides: what is highlighted goes nowhere until the player presses a
    /// key for it, so a suggestion cannot quietly become the command that runs.
    /// </para>
    /// </summary>
    internal sealed class CommandSuggestionView : ListView
    {
        /// <summary>Two columns for the cursor, matching the menu and save lists.</summary>
        private const int MarkerWidth = 2;

        /// <summary>Where the summaries line up, measured from the left edge of the strip.</summary>
        private const int SummaryColumn = 24;

        /// <summary>What a row needs before the summary is worth drawing at all.</summary>
        private const int MinimumForSummary = SummaryColumn + 12;

        private readonly StyledListSource<PlayerCommandInfo> _source;

        public CommandSuggestionView()
        {
            // Focus stays in the command box while this is up - that is the whole point of it - so
            // the strip must not be in the focus chain.
            CanFocus = false;

            _source = new StyledListSource<PlayerCommandInfo>(Row);
            Source = _source;
        }

        /// <summary>What to offer. Setting it puts the cursor back on the first row.</summary>
        public IReadOnlyList<PlayerCommandInfo> Suggestions
        {
            get => _source.Items;

            set
            {
                _source.Items = value;
                SelectedItem = 0;
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
            IsChoosing && Suggestions.Count > 0 ? Suggestions[Index] : null;

        /// <summary>
        /// The highlight as a plain index. <see cref="ListView.SelectedItem"/> is null for an empty
        /// list, and every use here wants a number in range of what is currently listed.
        /// </summary>
        private int Index => Math.Clamp(SelectedItem ?? 0, 0, Math.Max(0, Suggestions.Count - 1));

        /// <summary>Moves the cursor, clamping at both ends rather than wrapping.</summary>
        public void MoveSelection(int delta)
        {
            if (Suggestions.Count == 0)
            {
                return;
            }

            SelectedItem = Math.Clamp(Index + delta, 0, Suggestions.Count - 1);
            EnsureSelectedItemVisible();
        }

        /// <param name="isCursor">
        /// Whether this is the row a completing key would take. A settled command is drawn bright
        /// too - it is the one in play - but without the arrow, which would promise a choice that
        /// is no longer on offer.
        /// </param>
        private StyledLine Row(PlayerCommandInfo command, int width, bool isCursor)
        {
            var cursor = isCursor && IsChoosing;
            var isSelected = cursor || !IsChoosing;

            var line = new StyledLine();
            line.Append(cursor ? "> " : "  ", TextRole.System);

            // The summary is dropped rather than crushed against the name it belongs to: which
            // commands are on offer is the one thing this strip cannot do without.
            var hasSummary = width >= MinimumForSummary && command.Summary.Length > 0;

            // Truncated short of the summary column when there is one, so a long name cannot run
            // into a description that belongs to the same row and read as one word.
            var nameWidth = hasSummary ? SummaryColumn - MarkerWidth - 1 : width - MarkerWidth;

            line.Append(
                Fit(command.Usage, Math.Max(0, nameWidth)),
                isSelected ? TextRole.Command : TextRole.Normal);

            if (!hasSummary)
            {
                return line;
            }

            if (SummaryColumn > line.Length)
            {
                line.Append(new string(' ', SummaryColumn - line.Length), TextRole.Normal);
            }

            line.Append(Fit(command.Summary, width - SummaryColumn), TextRole.System);

            return line;
        }

        private static string Fit(string text, int width) =>
            text.Length <= width ? text : text[..Math.Max(0, width)];
    }
}
