namespace TerminalQuest.Saves
{
    /// <summary>
    /// Structured directives issued by the Director for the Narrator to act upon.
    /// </summary>
    internal sealed class DirectiveFile
    {
        public int TargetJournalSequence { get; set; }

        public string Trigger { get; set; } = string.Empty;

        public int ExpiryTurn { get; set; }

        public string Tone { get; set; } = string.Empty;

        public string PacingNote { get; set; } = string.Empty;

        public List<string> SecretPromotions { get; set; } = [];

        public List<int> RatifiedClaimSequences { get; set; } = [];

        public bool Consumed { get; set; }

        /// <summary>
        /// Whether this directive contains active guidance that has not yet expired or been consumed.
        /// </summary>
        public bool IsActive(int currentTurn)
        {
            if (Consumed)
            {
                return false;
            }

            if (ExpiryTurn > 0 && currentTurn > ExpiryTurn)
            {
                return false;
            }

            return !string.IsNullOrWhiteSpace(Tone)
                || !string.IsNullOrWhiteSpace(PacingNote)
                || SecretPromotions.Count > 0
                || RatifiedClaimSequences.Count > 0;
        }
    }
}
