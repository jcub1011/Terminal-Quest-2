namespace TerminalQuest.Saves
{
    /// <summary>
    /// A durable change to a place, deliberately the same shape as a <see cref="Memory"/>: a
    /// location accumulates history the way a person does.
    /// <para>
    /// This is what makes somewhere worth revisiting. A gate a dragon broke is still broken three
    /// scenes later because the fact lives here rather than in the model's context window.
    /// Distinct from a <see cref="StoryEvent"/>, which is a beat in the player's narrative -
    /// "entered the Hollow Gate" is a story event, "the left span is rubble" is a location event.
    /// </para>
    /// </summary>
    internal sealed class LocationEvent
    {
        public int Id { get; set; }

        public int Turn { get; set; }

        /// <summary>Prose with <c>{This}</c> (this location) and <c>{Player}</c> unresolved.</summary>
        public string Text { get; set; } = string.Empty;
    }
}
