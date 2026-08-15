using Terminal.Gui.Views;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The one rule the game's lists share about where the highlight goes when their rows are
    /// replaced.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="ListView.SelectedItem"/> is nullable because an empty list has no highlight, and
    /// the library means it: assigning any index - zero included - while there are no rows throws
    /// <see cref="ArgumentException"/>. Every list here is refilled wholesale and can be refilled
    /// with nothing, so each one had a crash in its setter waiting for the day its collection came
    /// back empty.
    /// </para>
    /// <para>
    /// Clamping is done here too, since a list that has just shrunk is the same moment: the
    /// highlight the caller wants to keep may be past the end of what is now there.
    /// </para>
    /// </remarks>
    internal static class ListSelection
    {
        /// <summary>Puts the highlight on a row, or takes it off when there are no rows to take.</summary>
        /// <param name="list">The list being highlighted.</param>
        /// <param name="count">How many rows it now has.</param>
        /// <param name="index">Where the highlight is wanted; clamped to what is there.</param>
        public static void Highlight(this ListView list, int count, int index)
        {
            ArgumentNullException.ThrowIfNull(list);

            list.SelectedItem = count > 0 ? Math.Clamp(index, 0, count - 1) : null;
        }
    }
}
