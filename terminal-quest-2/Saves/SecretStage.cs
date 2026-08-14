using System.Text.Json.Serialization;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// How far along a secret is, and therefore whether it can leave the save layer at all.
    /// <para>
    /// The wire spellings are pinned lowercase for the reason <see cref="CharacterKind"/> pins its
    /// own: the string in the file and the string a tool schema offers the model are the same string,
    /// and left to the member names the file would say <c>"Live"</c> where the schema said
    /// <c>"live"</c> - a discrepancy nothing would catch until somebody hand-edited a save.
    /// </para>
    /// <para>
    /// <see cref="Dormant"/> is zero deliberately, and it is the one choice here worth arguing about.
    /// A hand-written secret that forgot its stage - or misspelled it - reads as dormant, which is
    /// returned by nothing and so can leak by no mechanism at all. The reciprocal cost is that
    /// somebody granting a secret by hand must remember to write <c>"stage": "live"</c> or it will
    /// never fire. That failure wastes an evening; the other spoils a campaign.
    /// </para>
    /// </summary>
    internal enum SecretStage
    {
        /// <summary>
        /// Not in play. Returned by nothing, to anyone, ever.
        /// <para>
        /// A character holding only dormant secrets behaves as though unaware, which is the wanted
        /// behaviour rather than a limitation: a secret nothing has activated is not plot-relevant
        /// yet. This is stronger than filtering a secret out of some assembled context - there is no
        /// context to filter, because no tool will yield one.
        /// </para>
        /// </summary>
        [JsonStringEnumMemberName("dormant")]
        Dormant = 0,

        /// <summary>
        /// In play, for its holder. Returned only to a fetch that names somebody holding it, and the
        /// only stage that can make one fetch refuse another.
        /// </summary>
        /// <remarks>
        /// That last part is what keeps the cost of the whole mechanism proportional to how many
        /// secrets are in play rather than to how many a campaign has accumulated.
        /// </remarks>
        [JsonStringEnumMemberName("live")]
        Live,

        /// <summary>
        /// Said to the player. Returned to any fetch, and never makes anything refuse.
        /// </summary>
        /// <remarks>
        /// Derived rather than declared: a claim naming a secret moves it here, so nothing has to be
        /// asked whether the player has been told. Once the player knows, keeping it from the
        /// narrator would only mean a character going on protecting something that is already out.
        /// </remarks>
        [JsonStringEnumMemberName("spent")]
        Spent,
    }
}
