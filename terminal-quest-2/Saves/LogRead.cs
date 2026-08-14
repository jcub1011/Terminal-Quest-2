namespace TerminalQuest.Saves
{
    /// <summary>
    /// Everything a log had to say, plus a count of what could not be read.
    /// <para>
    /// Deliberately unlike <see cref="SaveStore"/>'s document read, which throws
    /// <see cref="SaveException"/> on anything it cannot parse. A document is one thing, and losing
    /// part of it loses all of it; a log is ten thousand things, and a process killed mid-append
    /// leaves a torn last line as a matter of routine. Throwing would make the log unreadable after
    /// any crash, which is the opposite of what a log is for.
    /// </para>
    /// <para>
    /// <see cref="Malformed"/> is reported rather than hidden because the count is the diagnostic.
    /// One malformed line at the end of a log being written right now is normal; anything else means
    /// something is wrong, and a caller that silently skipped both could not tell them apart.
    /// </para>
    /// </summary>
    /// <param name="Entries">Every readable entry, oldest first.</param>
    /// <param name="Malformed">Lines that were present and could not be read.</param>
    internal readonly record struct LogRead<TEntry>(IReadOnlyList<TEntry> Entries, int Malformed)
        where TEntry : class, ILogEntry;
}
