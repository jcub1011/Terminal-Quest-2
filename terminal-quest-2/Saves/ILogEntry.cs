namespace TerminalQuest.Saves
{
    /// <summary>
    /// What every line of an append-only log carries, whatever else it carries.
    /// <para>
    /// Small on purpose. <see cref="AppendLog{TEntry}"/> is generic over this rather than over
    /// nothing, because the two things it does that a serializer cannot - allocate a sequence
    /// number, and answer "what happened this turn" - are the only two fields it needs to know
    /// about. Everything else on an entry is the log's business and none of the appender's.
    /// </para>
    /// </summary>
    internal interface ILogEntry
    {
        /// <summary>
        /// Position in the log, from one. Allocated by <see cref="AppendLog{TEntry}.Append"/>,
        /// which is the only writer of this property and the only property it writes.
        /// </summary>
        /// <remarks>
        /// Deliberately not derived from the line's position in the file. The save format is meant
        /// to be hand-edited - that is the whole justification for short readable ids in
        /// <see cref="EntityIds"/> - and deleting one line would silently renumber every later
        /// entry, repointing anything that had already recorded a sequence at a different write. An
        /// explicit number also lets a reader notice a gap or a duplicate, which nothing derived
        /// from an ordinal ever could.
        /// </remarks>
        int Seq { get; set; }

        /// <summary>
        /// The turn the line belongs to, so that "what happened this turn" is answerable without
        /// reading the whole log into meaning.
        /// </summary>
        int Turn { get; }
    }
}
