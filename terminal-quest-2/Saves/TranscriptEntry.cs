namespace TerminalQuest.Saves
{
    /// <summary>
    /// One line of the conversation, word for word. The unit of what a resumed session remembers.
    /// <para>
    /// The other two logs record what happened <em>around</em> the prose: the journal holds the tool
    /// calls a turn made, the ledger holds the assertions it committed the world to. Neither holds a
    /// sentence the player actually read, and reducing a scene to either loses the voice it was
    /// written in - which is what a cold narrator most needs back and least able to reconstruct.
    /// </para>
    /// <para>
    /// This does not reopen the rule that <see cref="JournalEntry"/> states, that a tool's reply is
    /// never stored. That rule guards secrets and hidden roll totals, and nothing here can carry one:
    /// every line was either typed by the player or drawn on their screen. What is written down is
    /// exactly what they have already seen, which is the standing <see cref="LedgerEntry"/> has too.
    /// </para>
    /// </summary>
    /// <remarks>
    /// There is no flag for an unfinished reply, because there is no unfinished entry. A narrator
    /// line is appended once the turn has come back whole, so a session killed mid-sentence writes
    /// nothing and the log simply ends on the player. Whose move it is falls out of that - see
    /// <see cref="TranscriptRecall.AwaitingNarrator"/> - rather than being recorded twice and left to
    /// disagree with itself.
    /// </remarks>
    internal sealed class TranscriptEntry : ILogEntry
    {
        public int Seq { get; set; }

        /// <summary>
        /// The turn this line belongs to. A player line and the narrator's answer to it share one,
        /// which is what lets a roll made in between be drawn back in the place it appeared.
        /// </summary>
        public int Turn { get; set; }

        public TranscriptVoice Voice { get; set; }

        /// <summary>
        /// The text itself, unaltered.
        /// </summary>
        /// <remarks>
        /// Narrator prose keeps its markup, so a recalled scene is drawn in the colours it was first
        /// drawn in and the narrator reads back tagging it can copy. A player line keeps whatever
        /// they typed, newlines included - <c>Ui.ExternalEditor</c> lets a command be written in
        /// another program and arrive with its line breaks intact.
        /// </remarks>
        public string Text { get; set; } = string.Empty;
    }
}
