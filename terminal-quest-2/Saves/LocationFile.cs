namespace TerminalQuest.Saves
{
    /// <summary>Root document of <c>locations.json</c>.</summary>
    internal sealed class LocationFile
    {
        public List<Location> Locations { get; set; } = [];

        /// <summary>The counter behind <c>loc_N</c>. Monotonic - see <see cref="CharacterFile.NextId"/>.</summary>
        public int NextId { get; set; }

        /// <summary>Allocates the next free id and advances the counter. The caller writes the file.</summary>
        public string TakeId()
        {
            NextId = EntityIds.Ceiling(EntityIds.Location, Locations.Select(location => location.Id), NextId) + 1;
            return EntityIds.Location + NextId;
        }
    }
}
