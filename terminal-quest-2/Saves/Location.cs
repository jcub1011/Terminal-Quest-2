namespace TerminalQuest.Saves
{
    /// <summary>A place in the world, who is standing in it, and what it has been through.</summary>
    internal sealed class Location
    {
        public string Name { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Who is here right now - presence, not history.
        /// <para>
        /// A character belongs to at most one location, so this is only ever changed through
        /// <see cref="SaveStore.MoveCharacter"/>, which clears the old entry as it sets the new
        /// one. Left to add and remove calls the narrator would eventually leave someone standing
        /// in two places at once.
        /// </para>
        /// </summary>
        public List<string> Characters { get; set; } = [];

        /// <summary>What has happened here, oldest first.</summary>
        public List<LocationEvent> Events { get; set; } = [];
    }
}
