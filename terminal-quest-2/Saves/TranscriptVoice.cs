using System.Text.Json.Serialization;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// Who wrote a line of the transcript.
    /// </summary>
    /// <remarks>
    /// <see cref="Player"/> is zero for <see cref="SecretStage.Dormant"/>'s reason. The log's
    /// serializer omits a property sitting at its default, so a hand-edited line that loses its
    /// voice reads back as the player's. That is the harmless way round: a mangled line is drawn as
    /// an echoed command and can never be handed back to the narrator as prose it once wrote.
    /// </remarks>
    internal enum TranscriptVoice
    {
        /// <summary>A line the player typed, exactly as they typed it.</summary>
        [JsonStringEnumMemberName("player")]
        Player = 0,

        /// <summary>Prose the narrator wrote, with its markup intact.</summary>
        [JsonStringEnumMemberName("narrator")]
        Narrator,
    }
}
