using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Draws a recalled conversation back into the transcript pane.
    /// <para>
    /// A static formatter with no view, on <see cref="RollWatcher.Line"/>'s precedent and for its
    /// reason: one place decides what a recalled scene looks like, so what the player sees on
    /// resuming cannot drift from what they saw when it happened.
    /// </para>
    /// </summary>
    /// <remarks>
    /// This is the one deliberate exception to the rule <see cref="RollWatcher.CatchUp"/> states -
    /// that the log is the save's memory and the transcript is this sitting's. That rule is about not
    /// burying the scene the player came back for under a campaign of replayed history, and it still
    /// holds: what is drawn here is a bounded window chosen by <see cref="TranscriptRecall.Tail"/>,
    /// and it <em>is</em> the scene they came back for.
    /// </remarks>
    internal static class TranscriptReplay
    {
        /// <summary>
        /// The recalled window as finished paragraphs, ready to be added in order.
        /// </summary>
        /// <remarks>
        /// Rolls are placed by turn rather than by timestamp, which is as exact as the record allows
        /// and exact enough: a roll belongs to the turn it was thrown in, and within a turn the live
        /// screen already shows every roll above the prose that describes it, because
        /// <c>ShowRolls</c> closes the paragraph in flight before it draws one. Reproducing that
        /// ordering is reproducing what happened.
        /// <para>
        /// Rolls from turns outside the window are left out rather than bunched at the top. A die
        /// thrown in a scene the player is not being shown is not context, it is noise, and
        /// <c>/rolls</c> still has all of them.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<StyledLine> Lines(
            IReadOnlyList<TranscriptEntry> entries,
            IReadOnlyList<DiceRoll> rolls,
            CharacterFile characters)
        {
            ArgumentNullException.ThrowIfNull(entries);
            ArgumentNullException.ThrowIfNull(rolls);
            ArgumentNullException.ThrowIfNull(characters);

            if (entries.Count == 0)
            {
                return [];
            }

            var lines = new List<StyledLine>
            {
                StyledLine.FromText("--- from your last session ---", TextRole.System),
            };

            // Which turns have had their rolls drawn. A turn can hold more than one narrator entry -
            // nothing forbids it - and its dice must not be dealt out again beside the second.
            var rolled = new HashSet<int>();

            foreach (var entry in entries)
            {
                lines.Add(new StyledLine());

                if (entry.Voice == TranscriptVoice.Player)
                {
                    // The same shape GameWindow echoes a live command in, so a recalled line and one
                    // typed a moment ago are indistinguishable on screen.
                    lines.Add(StyledLine.FromText($"> {entry.Text}", TextRole.Command));
                    continue;
                }

                if (rolled.Add(entry.Turn))
                {
                    foreach (var roll in rolls)
                    {
                        if (roll.Turn != entry.Turn)
                        {
                            continue;
                        }

                        // Through the same formatter the live pane uses, which is also where a hidden
                        // roll's total is withheld - so hiding survives the replay without this having
                        // to know that hiding exists.
                        lines.Add(RollWatcher.Line(roll, SaveStore.FindCharacterById(characters, roll.CharacterId)?.Name));
                    }
                }

                // Parsed rather than stored as spans: the markup is what was written, and re-reading
                // it here is what colours the recalled prose exactly as it was coloured live.
                var entryLines = entry.Text.Replace("\r\n", "\n").Split('\n');
                foreach (var el in entryLines)
                {
                    lines.Add(MarkupParser.Parse(el));
                }
            }

            lines.Add(new StyledLine());
            lines.Add(StyledLine.FromText(
                TranscriptRecall.AwaitingNarrator(entries)
                    ? "--- the narrator was still speaking when this save was closed; its unfinished reply was discarded ---"
                    : "--- you were here ---",
                TextRole.System));

            return lines;
        }
    }
}
