using System.Text.Json;

using TerminalQuest.Saves;

namespace TerminalQuest.Mcp
{
    /// <summary>
    /// Writes one <c>journal.jsonl</c> line per tool call, and never lets that fail a tool call.
    /// <para>
    /// <b>A broken log does not break play.</b> The journal is a record of the game rather than part
    /// of it: no handler reads it, the model cannot see it, and nothing the player does depends on
    /// it. Letting an append failure propagate would turn a full disk or a stray read-only bit into a
    /// refused tool call - which the narrator reads as the world declining, and then narrates around.
    /// The trade is worth stating rather than hiding: a log with a hole in it makes the consistency
    /// check incomplete, and an incomplete audit is a better failure than a corrupted story.
    /// </para>
    /// <para>
    /// It is reported, though, not swallowed. <see cref="OnFailure"/> is set once at startup by
    /// whoever owns the console - the state server writes to stderr, the game writes a line into the
    /// transcript where a failed turn already goes. Left unset, as in a test, it is silent.
    /// </para>
    /// </summary>
    internal static class QuestJournal
    {
        /// <summary>An empty object, standing in for the arguments of a tool that takes none.</summary>
        /// <remarks>
        /// The server hands over a default <see cref="JsonElement"/> for those, and writing an
        /// undefined element throws rather than producing null - so this substitution is required and
        /// not defensive. Deliberately never disposed: it is two bytes, and it is wanted for as long
        /// as the process runs.
        /// </remarks>
        private static readonly JsonElement NoArguments = JsonDocument.Parse("{}").RootElement;

        /// <summary>Where an append failure is reported. Null means nowhere.</summary>
        /// <remarks>
        /// A delegate rather than a flag or a logger because the two hosts have nothing in common to
        /// report through: one owns a terminal UI it must marshal onto, the other owns a stdio
        /// transport where stdout is reserved for the protocol.
        /// <para>
        /// Reporting the same trouble once rather than every time is the host's business too, and
        /// belongs on its side of this seam. A latch here would be static mutable state that test
        /// classes running in parallel would trip over each other on.
        /// </para>
        /// </remarks>
        public static Action<string>? OnFailure { get; set; }

        /// <summary>Records that a tool ran, and what it was asked.</summary>
        /// <param name="store">The save being played.</param>
        /// <param name="tool">The tool name as dispatch was given it, existing or not.</param>
        /// <param name="arguments">The model's arguments, possibly undefined.</param>
        /// <param name="failed">Whether the call was refused or threw.</param>
        /// <param name="error">Why it threw, when it threw. Empty otherwise.</param>
        public static void Record(
            SaveStore store,
            string tool,
            JsonElement arguments,
            bool failed,
            string error)
        {
            try
            {
                store.Journal.Append(new JournalEntry
                {
                    // Read here rather than passed in. The state server is a separate process and
                    // save.json is the only place it can learn what turn it is, which is why the TUI
                    // stamps the turn before the turn runs rather than after it.
                    Turn = store.CurrentTurn(),
                    Tool = tool,
                    Arguments = arguments.ValueKind == JsonValueKind.Undefined ? NoArguments : arguments,
                    Failed = failed,
                    Error = error,
                });
            }
            catch (Exception ex)
            {
                // Everything, not only SaveException: an unreadable save.json on the way to the turn
                // number, and anything the serializer has to say about an argument shape nobody
                // anticipated, are both no more worth a refused tool call than a failed append is.
                OnFailure?.Invoke($"Could not journal {tool}: {ex.Message}");
            }
        }
    }
}
