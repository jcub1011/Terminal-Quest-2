namespace TerminalQuest.Saves
{
    /// <summary>
    /// Translates between the ids records point at each other with and the names everybody else
    /// speaks.
    /// <para>
    /// The whole identity scheme rests on one rule: an id never leaves the save layer. The player
    /// types names, the narrator is given and asked for names, and the screen shows names. This is
    /// the seam where that happens, in both directions.
    /// </para>
    /// <para>
    /// Built from documents the caller has already read, so it costs no extra reading in the
    /// handlers that need it. Nothing is cached beyond the lifetime of the call that built it -
    /// another process may be writing these files right now, which is the same reason
    /// <see cref="SaveStore"/> caches nothing either.
    /// </para>
    /// </summary>
    internal sealed class WorldIndex
    {
        private readonly Dictionary<string, string> _namesById = new(StringComparer.Ordinal);
        private readonly List<(string Name, string Id)> _byName = [];

        private WorldIndex()
        {
        }

        /// <summary>
        /// Indexes whichever documents the caller has to hand. Any of them may be null: a handler
        /// that only ever renders a roster has no reason to read the inventory just to build this.
        /// </summary>
        public static WorldIndex Build(
            CharacterFile? characters = null,
            LocationFile? locations = null,
            InventoryFile? inventory = null)
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

            if (inventory is not null)
            {
                foreach (var item in inventory.Items)
                {
                    index.Add(item.Id, item.Name);
                }
            }

            return index;
        }

        /// <summary>What an id is called now, or null when nothing on record answers to it.</summary>
        public string? NameOf(string? id) =>
            id is { Length: > 0 } && _namesById.TryGetValue(id, out var name) ? name : null;

        /// <summary>
        /// The id of the thing called <paramref name="name"/>, or null when nothing is.
        /// <para>
        /// Characters first, then locations, then items - the order the narrator most often means.
        /// Exact match only, on the same trimmed case-insensitive rule as everywhere else: a
        /// substring that resolved to two entities would be worse than not resolving at all, and
        /// partial names are already covered by the prose search that backs every lookup.
        /// </para>
        /// </summary>
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

        /// <summary>
        /// The names behind a list of ids, in order.
        /// <para>
        /// An id nothing answers to is skipped rather than passed through. A dangling reference
        /// means the save was hand-edited, and showing <c>chr_9</c> to the narrator or the player
        /// would break the one rule this class exists to keep - neither could do anything with it
        /// anyway. An absent character reads correctly as absent.
        /// </para>
        /// </summary>
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

            // First writer wins. Duplicate ids are only reachable through hand-editing, and
            // rebinding the id halfway through a document would be harder to explain than
            // consistently meaning the first record that claimed it.
            if (_namesById.TryAdd(id, name))
            {
                _byName.Add((name, id));
            }
        }
    }
}
