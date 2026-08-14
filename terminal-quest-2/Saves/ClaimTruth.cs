using System.Text.Json.Serialization;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// What a speaker was doing with a claim, judged against the world rather than against their
    /// intent.
    /// <para>
    /// A plain true-or-false flag would collapse the distinction the ledger exists to keep. A
    /// character who lies has said something false, and that is a fact about what was said, not a
    /// fault in the world. Collapsing the two would make every deceptive character look like a
    /// consistency bug, and would make it impossible to pay a lie off later - which is most of what
    /// having a record of lies is for.
    /// </para>
    /// <para>
    /// The game already draws this line on the mechanical side, where a hidden roll's total is kept
    /// from the player and never from the narrator. This is the same idea applied to prose.
    /// </para>
    /// <para>
    /// The first four are a <em>speaker's stance</em>, written when the claim is recorded.
    /// <see cref="Contradiction"/> is never one: it is a finding about an earlier claim, and because
    /// the ledger is append-only it arrives as a new entry naming that claim rather than as an edit
    /// to it. Canon is extended and never negated; the log that records canon obeys its own rule.
    /// </para>
    /// <para>
    /// Wire spellings are pinned lowercase for the reason <see cref="CharacterKind"/> pins its own:
    /// these values appear verbatim in a tool schema, and left to the member names the file would
    /// say <c>"Lie"</c> where the schema said <c>"lie"</c>.
    /// </para>
    /// </summary>
    internal enum ClaimTruth
    {
        /// <summary>
        /// Nobody has settled it. Zero, so a hand-written line that leaves the field out is not read
        /// as an assertion of fact - and the honest reading of anything the player says, which the
        /// game records without asking anyone whether it was true.
        /// </summary>
        [JsonStringEnumMemberName("unverified")]
        Unverified = 0,

        /// <summary>Consistent with the world as it stood on the turn it was said.</summary>
        [JsonStringEnumMemberName("true")]
        True,

        /// <summary>
        /// The speaker knew better. Binding as a record of what was said, and not as a fact about
        /// the world.
        /// </summary>
        [JsonStringEnumMemberName("lie")]
        Lie,

        /// <summary>
        /// Said in good faith and false anyway - a rumour repeated, a thing misremembered. Unlike a
        /// lie this can be corrected in the fiction without anybody having been dishonest, which is
        /// why it is worth telling apart.
        /// </summary>
        [JsonStringEnumMemberName("mistaken")]
        Mistaken,

        /// <summary>
        /// The world itself said something that cannot be reconciled with what it had already said.
        /// A bug to be fixed out of band, never a mechanic, and never narrated as a correction.
        /// </summary>
        [JsonStringEnumMemberName("contradiction")]
        Contradiction,
    }
}
