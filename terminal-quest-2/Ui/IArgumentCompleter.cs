using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Strategy for generating argument auto-complete suggestions for player slash commands.
    /// </summary>
    internal interface IArgumentCompleter
    {
        /// <summary>
        /// Generates suggestions matching the given argument prefix.
        /// </summary>
        /// <param name="commandName">The command being completed (e.g. "character", "delete").</param>
        /// <param name="argumentPrefix">The argument text typed so far after the command name.</param>
        /// <param name="store">The active save store, if available.</param>
        /// <returns>A list of matching suggestions.</returns>
        IReadOnlyList<SuggestionItem> GetSuggestions(string commandName, string argumentPrefix, SaveStore? store);
    }

    /// <summary>
    /// Default completer for commands that take no arguments or have no dynamic argument suggestions.
    /// </summary>
    internal sealed class NullArgumentCompleter : IArgumentCompleter
    {
        public static readonly NullArgumentCompleter Instance = new();

        public IReadOnlyList<SuggestionItem> GetSuggestions(string commandName, string argumentPrefix, SaveStore? store) => [];
    }

    /// <summary>
    /// Completes character names from the active save store.
    /// </summary>
    internal sealed class CharacterArgumentCompleter : IArgumentCompleter
    {
        public static readonly CharacterArgumentCompleter Instance = new();

        public IReadOnlyList<SuggestionItem> GetSuggestions(string commandName, string argumentPrefix, SaveStore? store)
        {
            if (store is null)
            {
                return [];
            }

            try
            {
                var chars = store.ReadCharacters().Characters;
                return chars
                    .Where(c => c.Name.StartsWith(argumentPrefix, StringComparison.OrdinalIgnoreCase))
                    .Select(c => new SuggestionItem(
                        InsertText: $"/{commandName} {c.Name}",
                        DisplayText: c.Name,
                        Summary: c.Kind == CharacterKind.Player
                            ? "(you)"
                            : (c.Description.Length > 0 ? c.Description : $"Health {c.Health}/{c.MaxHealth}"),
                        Role: TextRole.Character))
                    .ToArray();
            }
            catch (SaveException)
            {
                return [];
            }
        }
    }

    /// <summary>
    /// Completes location names from the active save store.
    /// </summary>
    internal sealed class LocationArgumentCompleter : IArgumentCompleter
    {
        public static readonly LocationArgumentCompleter Instance = new();

        public IReadOnlyList<SuggestionItem> GetSuggestions(string commandName, string argumentPrefix, SaveStore? store)
        {
            if (store is null)
            {
                return [];
            }

            try
            {
                var locs = store.ReadLocations().Locations;
                return locs
                    .Where(l => l.Name.StartsWith(argumentPrefix, StringComparison.OrdinalIgnoreCase))
                    .Select(l => new SuggestionItem(
                        InsertText: $"/{commandName} {l.Name}",
                        DisplayText: l.Name,
                        Summary: l.Description,
                        Role: TextRole.Place))
                    .ToArray();
            }
            catch (SaveException)
            {
                return [];
            }
        }
    }

    /// <summary>
    /// Completes save names for save management commands (excluding the currently active save).
    /// </summary>
    internal sealed class SaveNameArgumentCompleter : IArgumentCompleter
    {
        public static readonly SaveNameArgumentCompleter Instance = new();

        public IReadOnlyList<SuggestionItem> GetSuggestions(string commandName, string argumentPrefix, SaveStore? store)
        {
            try
            {
                var saves = SavePaths.List();
                return saves
                    .Where(s => !SaveStore.Matches(s.Name, store?.Name)
                        && s.Name.StartsWith(argumentPrefix, StringComparison.OrdinalIgnoreCase))
                    .Select(s => new SuggestionItem(
                        InsertText: $"/{commandName} {s.Name}",
                        DisplayText: s.Name,
                        Summary: $"turn {s.Turn}  {s.SizeText}",
                        Role: TextRole.Normal))
                    .ToArray();
            }
            catch
            {
                return [];
            }
        }
    }

    /// <summary>
    /// Convenient access to singleton argument completers.
    /// </summary>
    internal static class ArgumentCompleters
    {
        public static IArgumentCompleter Null => NullArgumentCompleter.Instance;
        public static IArgumentCompleter Character => CharacterArgumentCompleter.Instance;
        public static IArgumentCompleter Location => LocationArgumentCompleter.Instance;
        public static IArgumentCompleter SaveName => SaveNameArgumentCompleter.Instance;
    }
}
