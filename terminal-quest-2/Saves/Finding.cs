using System.Text.Json.Serialization;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// Something the game noticed going wrong that the player is not asked to care about.
    /// <para>
    /// An enumeration rather than a message, so the log can be counted as well as read. "How often
    /// does this narrator forget its claims" is the question the missing-claims check exists to
    /// answer, and it is a question about a hundred turns rather than about one - which a file of
    /// prose sentences could not be asked.
    /// </para>
    /// </summary>
    /// <remarks>
    /// The wire spellings are pinned for <see cref="SecretStage"/>'s reason: a name written into a
    /// file outlives the member it came from, and renaming the member should not silently change
    /// what an old line means.
    /// </remarks>
    internal enum Finding
    {
        /// <summary>
        /// A line that named no finding, or named one this build does not know.
        /// <para>
        /// Zero so that every entry the game writes names its finding explicitly - the log's
        /// serializer omits a property sitting at its default, so this is the one value that never
        /// appears in a file the game wrote. Reading one back means somebody edited it, or that it
        /// came from a later build.
        /// </para>
        /// </summary>
        [JsonStringEnumMemberName("unknown")]
        Unknown = 0,

        /// <summary>
        /// The narrator wrote prose and recorded no claims, so nothing it said that turn reached the
        /// ledger.
        /// </summary>
        /// <remarks>
        /// The fix is the prompt, never this: a turn that forgets is a turn the ledger has a hole in,
        /// and the hole cannot be filled after the fact because only the model knew what it meant to
        /// assert. Recorded in every build rather than only a debug one, for the reason it was
        /// reported in every build before - a shipped game quietly losing its record is worse than a
        /// line in a file nobody reads until they need it.
        /// </remarks>
        [JsonStringEnumMemberName("claimsMissing")]
        ClaimsMissing,

        /// <summary>
        /// A log line could not be written. The story is unaffected; its record is not.
        /// </summary>
        /// <remarks>
        /// Recorded on a best-effort basis and expected to fail in the very case it describes: a
        /// folder that will not take a journal line will not take this one either. It is worth
        /// attempting anyway, because the common causes are specific to one file - an editor holding
        /// <c>journal.jsonl</c> open, a half-finished hand-edit - and in those the note lands.
        /// </remarks>
        [JsonStringEnumMemberName("recordUnwritable")]
        RecordUnwritable,
    }
}
