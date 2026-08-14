namespace TerminalQuest.Saves
{
    /// <summary>
    /// How much of a transcript is worth handing back, and whose move it is.
    /// <para>
    /// Pure, like <see cref="SecretDivergence"/> and for the same reason: no store, no files, no
    /// clock. Two callers need the same answer from different processes - the game, drawing the
    /// scene the player is coming back to, and the <c>get_transcript</c> tool answering the narrator
    /// - and the only way those two cannot drift is for neither to own the rule.
    /// </para>
    /// </summary>
    internal static class TranscriptRecall
    {
        /// <summary>
        /// How much verbatim conversation is recalled when nobody has said otherwise.
        /// <para>
        /// The narrator answers in at most two sentences, so a turn costs a couple of hundred
        /// characters and this is twenty-odd exchanges - far enough back to carry the thread of a
        /// scene, and about a thousand tokens, which is small beside the system prompt it rides
        /// behind.
        /// </para>
        /// </summary>
        public const int DefaultCharacters = 4000;

        /// <summary>
        /// Below this there is no point recalling anything: a single exchange would not survive the
        /// budget, and the last entry is kept regardless, so the setting would stop meaning anything.
        /// </summary>
        public const int MinCharacters = 500;

        /// <summary>
        /// The ceiling, which is a courtesy to the model rather than to the disk. Recall competes with
        /// the world state for the same context, and a player who sets this to the whole campaign
        /// would be trading the narrator's grasp of what is true for its memory of how it was phrased.
        /// </summary>
        public const int MaxCharacters = 20000;

        /// <summary>The window, brought inside <see cref="MinCharacters"/>..<see cref="MaxCharacters"/>.</summary>
        /// <remarks>
        /// Here rather than at each call site, because both a hand-edited settings file and a number
        /// the model made up arrive the same way - as an integer nobody has checked - and the answer
        /// to both is the same.
        /// </remarks>
        public static int Clamp(int characters) =>
            Math.Clamp(characters, MinCharacters, MaxCharacters);

        /// <summary>
        /// The end of the conversation, oldest first, within a budget of characters of prose.
        /// </summary>
        /// <remarks>
        /// Whole entries only. Half a paragraph is worse than no paragraph: the player would be shown
        /// a sentence that starts nowhere, and the narrator would read its own writing as though it
        /// had trailed off.
        /// <para>
        /// The last entry is kept even when it alone breaks the budget, so a single long turn recalls
        /// itself rather than nothing at all. And when the oldest kept entry is a narrator's, the
        /// player line of the same turn is pulled in behind it even though the budget is spent - an
        /// answer shown without the line that prompted it reads as a non-sequitur, and a player line
        /// is a few dozen characters.
        /// </para>
        /// <para>
        /// Measured in characters of <see cref="TranscriptEntry.Text"/> and not in bytes of log,
        /// because what the budget is protecting is the reader at the other end - a context window or
        /// a terminal - and neither cares what the line cost on disk.
        /// </para>
        /// </remarks>
        public static IReadOnlyList<TranscriptEntry> Tail(
            IReadOnlyList<TranscriptEntry> entries,
            int characters)
        {
            ArgumentNullException.ThrowIfNull(entries);

            if (entries.Count == 0)
            {
                return [];
            }

            var budget = Clamp(characters);

            // From the last entry backwards, and the last entry is taken before the budget is
            // consulted at all - that is what makes "never empty" true rather than nearly true.
            var first = entries.Count - 1;
            var spent = entries[first].Text.Length;

            for (var index = entries.Count - 2; index >= 0; index--)
            {
                var cost = entries[index].Text.Length;

                if (spent + cost > budget)
                {
                    break;
                }

                spent += cost;
                first = index;
            }

            // Over budget on purpose. The alternative is dropping the narrator's reply instead, which
            // costs a whole exchange to save one short line.
            if (entries[first].Voice == TranscriptVoice.Narrator
                && first > 0
                && entries[first - 1] is { Voice: TranscriptVoice.Player } prompt
                && prompt.Turn == entries[first].Turn)
            {
                first--;
            }

            var window = new TranscriptEntry[entries.Count - first];
            for (var index = 0; index < window.Length; index++)
            {
                window[index] = entries[first + index];
            }

            return window;
        }

        /// <summary>
        /// Whether the narrator owes the player an answer.
        /// </summary>
        /// <remarks>
        /// Derived, and deliberately not a field on the log. A narrator line is only appended once its
        /// turn has come back whole, so a session that ended mid-sentence left the player's line as
        /// the last thing on record - which is the same shape as a session that ended with the player
        /// typing and then walking away, and wants the same answer. A stored flag would be a second
        /// account of this, free to disagree with the log it sits in and impossible to write at all on
        /// the one path that matters, where the process is gone before it could.
        /// </remarks>
        public static bool AwaitingNarrator(IReadOnlyList<TranscriptEntry> entries)
        {
            ArgumentNullException.ThrowIfNull(entries);

            return entries.Count > 0 && entries[^1].Voice == TranscriptVoice.Player;
        }
    }
}
