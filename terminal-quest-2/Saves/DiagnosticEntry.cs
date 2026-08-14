namespace TerminalQuest.Saves
{
    /// <summary>
    /// One thing the game noticed going wrong, kept for whoever comes looking later.
    /// <para>
    /// Its own log rather than a line in the journal, because the journal is one tool call per line
    /// and a finding is not a tool call. That is not tidiness: <c>ClaimsMissing</c> works by asking
    /// the journal whether a turn holds a <c>record_claims</c> entry, and the batch consistency test
    /// asserts over what it finds there. A log that answers "what did the narrator do" must not also
    /// carry entries about what it failed to do, or the two questions stop having separate answers.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Turn-stamped like the other three, and that is the whole reason it lives in the save folder
    /// rather than in one file for the whole machine. A finding is only worth much beside the tool
    /// calls, the claims and the prose of the same turn, and putting it here is what lets those four
    /// be read together.
    /// </remarks>
    internal sealed class DiagnosticEntry : ILogEntry
    {
        public int Seq { get; set; }

        public int Turn { get; set; }

        public Finding Finding { get; set; }

        /// <summary>
        /// What varies, where anything does - the underlying failure for
        /// <see cref="Finding.RecordUnwritable"/>, and empty where the finding says it all.
        /// </summary>
        public string Detail { get; set; } = string.Empty;
    }

    /// <summary>
    /// Writing findings down, on the understanding that failing to is not worth another failure.
    /// </summary>
    internal static class Findings
    {
        /// <summary>
        /// Notes a finding against a save, and never throws.
        /// </summary>
        /// <remarks>
        /// Swallowing every exception is the same bargain <c>Mcp.QuestJournal.Record</c> makes, and
        /// here it is the stricter of the two: one caller is reporting that a log could not be
        /// written, so this call is expected to fail sometimes and there is nowhere left to say so.
        /// A diagnostic that could take a turn down would be worse than the trouble it describes.
        /// <para>
        /// Nothing is reported to the player, which is the point. A finding is about the game's own
        /// record-keeping, and the player can neither act on it nor be helped by reading it in the
        /// middle of a scene.
        /// </para>
        /// </remarks>
        public static void Record(SaveStore store, int turn, Finding finding, string detail = "")
        {
            ArgumentNullException.ThrowIfNull(store);

            try
            {
                store.Diagnostics.Append(new DiagnosticEntry
                {
                    Turn = turn,
                    Finding = finding,
                    Detail = detail,
                });
            }
            catch (Exception)
            {
                // Deliberately everything, and deliberately silent. See the remarks above: the one
                // place left to report this is the thing that just refused to be written to.
            }
        }
    }
}
