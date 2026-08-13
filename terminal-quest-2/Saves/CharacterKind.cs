using System.Text.Json.Serialization;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// Whether a character is the one the player is steering, or someone the narrator voices.
    /// <para>
    /// The wire spellings are pinned lowercase so the JSON reads as <c>"kind": "player"</c> and
    /// matches, exactly, the values the tool schema offers the model. Left to the member names,
    /// the file would say <c>"Player"</c> while the schema said <c>"player"</c> - a discrepancy
    /// nothing would catch until someone edited a save by hand.
    /// </para>
    /// </summary>
    internal enum CharacterKind
    {
        /// <summary>Voiced by the narrator.</summary>
        [JsonStringEnumMemberName("npc")]
        Npc = 0,

        /// <summary>The player. Exactly one character should carry this.</summary>
        [JsonStringEnumMemberName("player")]
        Player,
    }
}
