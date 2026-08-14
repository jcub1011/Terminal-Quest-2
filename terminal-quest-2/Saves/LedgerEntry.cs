namespace TerminalQuest.Saves
{
    /// <summary>
    /// One thing said to the player, and by whom. The unit of the revealed ledger.
    /// <para>
    /// Per assertion, not per scene and not per turn. Coarser entries were considered and rejected:
    /// a scene's worth of prose reduced to one line is too lossy for a consistency check to find
    /// anything in, and a per-turn summary still asks the narrator to extract its assertions - so
    /// coarsening throws precision away without saving the work it was meant to save.
    /// </para>
    /// <para>
    /// Canonical state and what the player believes are different things, and the contradiction that
    /// matters is with what they were <em>told</em>. That is what this records, which is why it holds
    /// claims rather than facts and why every entry carries who made it.
    /// </para>
    /// </summary>
    internal sealed class LedgerEntry : ILogEntry
    {
        public int Seq { get; set; }

        public int Turn { get; set; }

        /// <summary>
        /// Who said it, as they were called at the time. Empty when it was narration rather than
        /// anybody's voice - the world describing a room is speaking too.
        /// </summary>
        /// <remarks>
        /// Kept verbatim beside <see cref="SpeakerId"/> for the reason a <see cref="Memory"/> keeps
        /// its prose beside its subject index: prose written before a rename is never rewritten, and
        /// a claim recording the old name is the player's own recollection rather than a mistake to
        /// correct.
        /// </remarks>
        public string Speaker { get; set; } = string.Empty;

        /// <summary>
        /// The same speaker as a reference, resolved on the way in. Empty for narration, and empty
        /// when the name answered to nobody on record.
        /// </summary>
        /// <remarks>
        /// Both, deliberately. A batch job joins on the id and so survives a rename; a person reads
        /// the name and wants the one they would have heard.
        /// </remarks>
        public string SpeakerId { get; set; } = string.Empty;

        /// <summary>
        /// The assertion, in one sentence, as whoever recorded it reduced what was written to what it
        /// commits the world to. Prose, not a schema.
        /// </summary>
        public string Claim { get; set; } = string.Empty;

        public ClaimTruth Truth { get; set; }

        /// <summary>
        /// The short name of a secret this claim let out, or empty. What drives a secret's automatic
        /// move to spent.
        /// </summary>
        /// <remarks>
        /// A name and never a reference, because a secret has no id to refer to it by - see
        /// <see cref="Secret"/> on why that is a house rule rather than an oversight.
        /// </remarks>
        public string Reveals { get; set; } = string.Empty;

        /// <summary>
        /// The sequence of an earlier entry this one is a finding about; zero for an ordinary claim.
        /// </summary>
        /// <remarks>
        /// The append-only way to record that something said earlier turned out to be wrong, without
        /// rewriting the line that said it. A later judgement about a claim is a new claim - which is
        /// the same rule the world follows, and the reason this field exists before anything writes
        /// it: a reader who found no way to record a finding would reach for editing a line instead,
        /// and quietly destroy the only property this log has.
        /// </remarks>
        public int Adjudicates { get; set; }
    }
}
