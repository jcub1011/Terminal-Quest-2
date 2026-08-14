namespace TerminalQuest.Saves
{
    /// <summary>
    /// Whether a knowledge fetch may be answered, given who has already been read this turn.
    /// <para>
    /// The problem this solves is that context here is <em>pulled</em> by the model through tools
    /// rather than assembled and handed to it, so there is no filtering step to insert. Once the
    /// narrator's session has asked about one character and then another, it holds the first one's
    /// secrets while voicing the second, and it cannot be un-told. Splitting the scene into a call per
    /// character would fix it and cost far more than it saves: one narrator process is held open for a
    /// whole conversation so that the prompt prefix stays cached, and a process per character means a
    /// cold cache each.
    /// </para>
    /// <para>
    /// So the turn becomes the unit of speaking order instead. A scene holding two differently
    /// informed characters can only have one of them speak knowingly per turn, which reads as the
    /// scene taking two turns. That is a pacing cost rather than a correctness one, and it is cheaper
    /// than either leaking or splitting.
    /// </para>
    /// <para>
    /// Pure, and kept that way deliberately: no store, no files, no clock, and no strings the model
    /// reads - the refusal is worded by the caller. What is left is a predicate over three plain
    /// values, which is what makes the rule testable without a save folder at all.
    /// </para>
    /// </summary>
    internal static class SecretDivergence
    {
        /// <summary>
        /// The name of a character already read this turn whose live secrets
        /// <paramref name="fetched"/> does not share, or null when the fetch may be answered.
        /// </summary>
        /// <param name="fetched">The character this fetch names.</param>
        /// <param name="roster">
        /// Everybody on record, so that a name recovered from the log can be turned back into the
        /// secrets behind it. Worth noting that the rule needs this at all: it is often described as a
        /// function of the log and the turn, and it is really a function of the log, the turn and who
        /// holds what.
        /// </param>
        /// <param name="namesReadThisTurn">
        /// Characters a knowledge fetch has already been answered for this turn, in order. Names,
        /// because a name is what a tool call carries - an id never leaves the save layer.
        /// </param>
        /// <returns>
        /// The first blocker in log order, so that the refusal is stable when the narrator tries
        /// again. A message naming a different character each time would read as the world changing
        /// its mind rather than as a rule.
        /// </returns>
        public static string? BlockingHolder(
            Character fetched,
            IReadOnlyList<Character> roster,
            IReadOnlyList<string> namesReadThisTurn)
        {
            ArgumentNullException.ThrowIfNull(fetched);
            ArgumentNullException.ThrowIfNull(roster);
            ArgumentNullException.ThrowIfNull(namesReadThisTurn);

            foreach (var name in namesReadThisTurn)
            {
                // The same character read twice is not a divergence, it is the narrator checking its
                // notes - and refusing that would make voicing anybody a one-shot affair.
                if (SaveStore.Matches(name, fetched.Name))
                {
                    continue;
                }

                // A name nothing answers to is a fetch that failed on its own terms, or somebody since
                // removed by hand. Either way it handed nothing over.
                var earlier = roster.FirstOrDefault(candidate => SaveStore.Matches(candidate.Name, name));
                if (earlier is null)
                {
                    continue;
                }

                // Live only. Dormant secrets were never handed over, and spent ones are shared - which
                // is the whole reason those stages exist, and what keeps this proportional to what is
                // in play rather than to the size of the campaign.
                //
                // The stage is re-read from the roster as it stands now rather than as it stood at the
                // earlier fetch. That is sound because a stage only ever moves towards being
                // shareable: one that has gone spent since is one this fetch no longer needs
                // protecting from.
                foreach (var secret in Secrets.AtStage(earlier, SecretStage.Live))
                {
                    if (!Secrets.Holds(fetched, secret.Name))
                    {
                        return earlier.Name;
                    }
                }
            }

            return null;
        }
    }
}
