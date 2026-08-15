namespace TerminalQuest.Saves
{
    /// <summary>
    /// Translates between the ids records point at each other with and the names everybody else speaks.
    /// </summary>
    internal sealed class WorldIndex
    {
        private readonly Dictionary<string, string> _namesById = new(StringComparer.Ordinal);
        private readonly List<(string Name, string Id)> _byName = [];

        private WorldIndex()
        {
        }

        public static WorldIndex Build(
            CharacterFile? characters = null,
            LocationFile? locations = null,
            ItemFile? items = null)
        {
            var index = new WorldIndex();

            if (characters is not null)
            {
                foreach (var character in characters.Characters)
                {
                    index.Add(character.Id, character.Name);
                }
            }

            if (locations is not null)
            {
                foreach (var location in locations.Locations)
                {
                    index.Add(location.Id, location.Name);
                }
            }

            if (items is not null)
            {
                foreach (var item in items.Items)
                {
                    index.Add(item.Id, item.Name);
                }
            }

            return index;
        }

        public string? NameOf(string? id) =>
            id is { Length: > 0 } && _namesById.TryGetValue(id, out var name) ? name : null;

        public string? IdOf(string? name)
        {
            if (name is not { Length: > 0 })
            {
                return null;
            }

            foreach (var (candidate, id) in _byName)
            {
                if (SaveStore.Matches(candidate, name))
                {
                    return id;
                }
            }

            return null;
        }

        public IEnumerable<string> NamesOf(IEnumerable<string> ids)
        {
            ArgumentNullException.ThrowIfNull(ids);

            foreach (var id in ids)
            {
                if (NameOf(id) is { } name)
                {
                    yield return name;
                }
            }
        }

        private void Add(string? id, string name)
        {
            if (id is not { Length: > 0 })
            {
                return;
            }

            if (_namesById.TryAdd(id, name))
            {
                _byName.Add((name, id));
            }
        }
    }
}
