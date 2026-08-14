using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Carries rolls from the file the narrator wrote them in onto the transcript.
    /// <para>
    /// The narrator's tools run in another process, so a roll arrives the way every other change
    /// does: as a file on disk, with nothing to subscribe to. Unlike the status pane, which can wait
    /// for the turn to end, a roll has to land beside the prose that describes it - so this is read
    /// repeatedly while a turn runs rather than once after it.
    /// </para>
    /// <para>
    /// The cursor lives here and not in the save. Whether a window has drawn a roll is a fact about
    /// one window in one process, and writing it down would make the game a second author of a
    /// document only the narrator is supposed to write - two processes racing on one rename, to
    /// record something that is not true of the world at all.
    /// </para>
    /// </summary>
    internal sealed class RollWatcher
    {
        private readonly SaveStore _store;

        /// <summary>The highest id drawn into the transcript. Everything at or below it has been shown.</summary>
        private int _shown;

        /// <summary>
        /// Rolls whose reveal has been drawn.
        /// <para>
        /// A second reckoning because a reveal re-shows a roll the cursor has long since passed:
        /// the id cannot say "shown once, and shown again since it was revealed", so the exception
        /// is tracked as one. Small by construction - only ever hidden rolls the narrator has
        /// deliberately come back to.
        /// </para>
        /// </summary>
        private readonly HashSet<int> _revealed = [];

        public RollWatcher(SaveStore store)
        {
            _store = store;
        }

        /// <summary>
        /// Marks everything already on record as seen, without showing any of it.
        /// </summary>
        /// <remarks>
        /// Called once before the first turn. A resumed save has a log going back to the start of the
        /// campaign, and replaying it into a fresh transcript would bury the scene the player came
        /// back for. The same split as <c>/story</c> and the narration: the log is the save's record,
        /// the transcript is this sitting's.
        /// </remarks>
        /// <exception cref="SaveException">The roll log exists but could not be parsed.</exception>
        public void CatchUp()
        {
            foreach (var roll in _store.ReadRolls().Rolls)
            {
                _shown = Math.Max(_shown, roll.Id);

                if (roll.Revealed)
                {
                    _revealed.Add(roll.Id);
                }
            }
        }

        /// <summary>
        /// Every roll not yet drawn, oldest first, and empty in the common case.
        /// </summary>
        /// <remarks>
        /// Reads the roll log and nothing else when there is nothing new, which is almost always.
        /// Names cost a read of <c>characters.json</c> - the document that carries every memory in
        /// the game - so that is paid for only when there is actually a line to draw.
        /// </remarks>
        /// <exception cref="SaveException">A document exists but could not be parsed.</exception>
        public IReadOnlyList<DiceRoll> Take()
        {
            var rolls = _store.ReadRolls().Rolls;

            var highest = 0;
            foreach (var roll in rolls)
            {
                highest = Math.Max(highest, roll.Id);
            }

            // A log that has shrunk was edited by hand. Following it down means the next roll is
            // drawn once rather than the whole tail being drawn again.
            if (highest < _shown)
            {
                _shown = highest;
            }

            List<DiceRoll>? fresh = null;

            foreach (var roll in rolls)
            {
                var isNew = roll.Id > _shown;
                var isRevealed = roll.Hidden && roll.Revealed && !_revealed.Contains(roll.Id);

                if (!isNew && !isRevealed)
                {
                    continue;
                }

                (fresh ??= []).Add(roll);
            }

            if (fresh is null)
            {
                return [];
            }

            // Advanced only once the caller has the lines: a read that throws leaves the cursor
            // where it was, so nothing is lost to a save that would not parse for a moment.
            foreach (var roll in fresh)
            {
                _shown = Math.Max(_shown, roll.Id);

                if (roll.Revealed)
                {
                    _revealed.Add(roll.Id);
                }
            }

            return fresh;
        }

        /// <summary>
        /// One roll, as both the transcript and <c>/rolls</c> draw it. One formatter and two callers,
        /// so what the player saw at the time and what they read back later cannot drift apart.
        /// </summary>
        /// <remarks>
        /// Built span by span rather than run through <see cref="MarkupParser"/>, and not only
        /// because it is cheaper: the parser exists for text the model wrote, and nothing on this
        /// line did.
        /// <para>
        /// A hidden roll returns before the total is ever appended. Not blanked and not masked - the
        /// number does not enter the line. That is where hiding is actually enforced; what the
        /// prompt asks of the narrator is only manners.
        /// </para>
        /// <para>
        /// No die glyph. U+2684 is in neither Consolas nor Cascadia Mono, so under conhost it draws
        /// as a hollow box; the only non-ASCII this UI otherwise uses is a box rule and a bullet,
        /// both of which every console font has. A word costs three columns and always renders.
        /// </para>
        /// </remarks>
        public static StyledLine Line(DiceRoll roll, string? rollerName)
        {
            ArgumentNullException.ThrowIfNull(roll);

            var line = new StyledLine();
            line.Append("roll  ", TextRole.Roll);

            line.Append(rollerName is { Length: > 0 } ? rollerName : "the world", TextRole.Normal);

            // The attribute when one applied, otherwise what the roll was for. Both are kept in the
            // save, and /rolls has room to show the reason either way.
            var label = roll.Attribute is { Length: > 0 } ? roll.Attribute : roll.Reason;

            if (label is { Length: > 0 })
            {
                line.Append(" — ", TextRole.System);
                line.Append(label, TextRole.Roll);
            }

            line.Append($"  {(roll.Notation is { Length: > 0 } ? roll.Notation : "?")}", TextRole.System);

            if (roll.Hidden && !roll.Revealed)
            {
                line.Append("  hidden", TextRole.System);
                return line;
            }

            line.Append("  = ", TextRole.System);
            line.Append(roll.Total.ToString(), TextRole.Item);

            if (roll.Faces.Count > 0)
            {
                line.Append($"  ({string.Join(", ", roll.Faces)})", TextRole.System);
            }

            if (roll.Hidden)
            {
                line.Append("  revealed", TextRole.Roll);
            }

            return line;
        }
    }
}
