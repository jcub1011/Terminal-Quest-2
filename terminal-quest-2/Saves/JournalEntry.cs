using System.Text.Json;
using System.Text.Json.Serialization;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// One tool call, as it happened. The line-oriented version counter the rest of the design
    /// stamps itself against.
    /// <para>
    /// <b>Every</b> call, not only the ones that write. The rule that decides whether a knowledge
    /// fetch may be answered is a function of which secrets have already been handed over this turn,
    /// and handing over happens in <c>get_character</c> and <c>get_memories</c>, which change
    /// nothing. Recording only the mutating calls would leave that rule with no way to be computed.
    /// It also means <see cref="Mcp.QuestTool"/> needs no read-or-write flag, which would otherwise
    /// have to be set correctly on two dozen definitions and stay correct.
    /// </para>
    /// <para>
    /// What the tool <em>answered</em> is deliberately absent, and the omission is a guarantee rather
    /// than a saving. The journal records that a question was asked and what was asked; the ledger
    /// records what reached the player. Storing replies would duplicate every memory and description
    /// the narrator writes, and would put every secret and every hidden roll into a plain text file
    /// the player can open - a worse leak than any it would help detect.
    /// </para>
    /// </summary>
    internal sealed class JournalEntry : ILogEntry
    {
        public int Seq { get; set; }

        /// <summary>
        /// The turn this call belongs to, read from <c>save.json</c> at the moment it was made.
        /// </summary>
        public int Turn { get; set; }

        /// <summary>The bare tool name, exactly as dispatch was given it.</summary>
        /// <remarks>
        /// Including a name no tool answers to. A narrator reaching for something that does not
        /// exist is the single most useful line in this file when working out why a turn went wrong,
        /// and it is invisible everywhere else.
        /// </remarks>
        public string Tool { get; set; } = string.Empty;

        /// <summary>
        /// The arguments exactly as the model sent them, opaque to the save layer.
        /// <para>
        /// Verbatim rather than parsed into a shape, for two reasons that pull the same way. The
        /// divergence rule reads the character's name back out of here, so the content has to
        /// survive; and a tool's schema will change while old lines must go on meaning what they
        /// meant, which a shape pinned to today's schema could not manage.
        /// </para>
        /// </summary>
        /// <remarks>
        /// Pinned to always-write. The context-wide ignore condition would have to compare two
        /// <see cref="JsonElement"/>s to decide whether this one is default, and JsonElement defines
        /// no such comparison. A tool that takes no arguments records an empty object instead:
        /// <c>Mcp.QuestJournal</c> substitutes one, because the undefined element that the server
        /// hands over for those tools throws when written rather than serializing as null.
        /// </remarks>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public JsonElement Arguments { get; set; }

        /// <summary>
        /// Whether the call went wrong - refused by its handler, or thrown out of it.
        /// <para>
        /// Load-bearing rather than diagnostic. A refused <c>get_character</c> handed nothing over,
        /// so the divergence rule has to be able to leave it out; without this it would count a
        /// fetch it had just refused, and the first refusal of a turn would become permanent.
        /// </para>
        /// </summary>
        public bool Failed { get; set; }

        /// <summary>Why, when a handler threw rather than refused. Empty otherwise.</summary>
        public string Error { get; set; } = string.Empty;
    }
}
