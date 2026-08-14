namespace TerminalQuest.Settings
{
    /// <summary>
    /// The Claude models the settings screen offers, and the mapping between the id stored in
    /// <see cref="AppSettings.ClaudeModel"/> and a name a player recognises.
    /// <para>
    /// Lives beside the settings rather than beside the screen that draws them because the save
    /// menu's one-line summary needs the same mapping, and two tables that have to agree are
    /// better off being one table.
    /// </para>
    /// </summary>
    internal static class ClaudeModels
    {
        /// <param name="Id">
        /// What goes on disk and from there to <c>--model</c>. Empty means the flag is left off
        /// altogether and the CLI narrates with whatever it is configured for.
        /// </param>
        /// <param name="Name">What the player picks from.</param>
        /// <param name="Detail">
        /// The trade being made. Worth showing: this model answers every turn, so the gap between
        /// the cheapest and the dearest is the difference between a game that costs pennies and
        /// one that does not.
        /// </param>
        internal readonly record struct Entry(string Id, string Name, string Detail);

        /// <summary>The offered models, in the order they are listed.</summary>
        public static readonly Entry[] All =
        [
            new(string.Empty, "Default", "whatever the CLI is set to"),
            new("claude-haiku-4-5", "Haiku", "fastest and cheapest"),
            new("claude-sonnet-5", "Sonnet", "balanced"),
            new("claude-opus-5", "Opus", "most capable"),
            new("claude-fable-5", "Fable", "deepest reasoning, slowest and dearest"),
        ];

        /// <summary>
        /// The index of the entry holding <paramref name="id"/>, or -1 for an id this build does
        /// not know.
        /// </summary>
        /// <remarks>
        /// A miss is not a fault. Settings written by an older build hold a dated id, and a player
        /// is free to hand-edit the file to something newer than this list. The screen shows those
        /// as they are rather than pretending nothing is selected.
        /// </remarks>
        public static int IndexOf(string id)
        {
            var wanted = id?.Trim() ?? string.Empty;

            for (var index = 0; index < All.Length; index++)
            {
                if (string.Equals(All[index].Id, wanted, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            return -1;
        }

        /// <summary>
        /// The name for an id, falling back to the id itself so an unknown model is still named
        /// something truthful.
        /// </summary>
        public static string Describe(string id)
        {
            var index = IndexOf(id);
            return index >= 0 ? All[index].Name : id.Trim();
        }
    }
}
