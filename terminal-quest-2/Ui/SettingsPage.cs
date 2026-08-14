using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// One level of the settings screen: the rows to show, and what the keys do to them.
    /// <para>
    /// Not a <see cref="Terminal.Gui.ViewBase.View"/>. <see cref="SettingsWindow"/> builds its view
    /// tree once and only ever swaps the data feeding it, so descending a level costs no layout
    /// and no reshuffling of focus. A page is a description of a level, not a thing on screen.
    /// </para>
    /// <para>
    /// Every page edits the same draft settings object directly. There is no per-level staging to
    /// merge back: the draft is already a copy, and nothing reaches disk until the player says so.
    /// </para>
    /// </summary>
    internal abstract class SettingsPage
    {
        protected SettingsPage(AppSettings draft)
        {
            ArgumentNullException.ThrowIfNull(draft);
            Draft = draft;
        }

        /// <summary>The settings being edited. Shared with every other page in the trail.</summary>
        protected AppSettings Draft { get; }

        /// <summary>This level's crumb in the breadcrumb.</summary>
        public abstract string Title { get; }

        /// <summary>
        /// The rows, rebuilt from <see cref="Draft"/> on every read so a change made on this page
        /// or a deeper one is on screen without anything having to invalidate anything.
        /// </summary>
        public abstract IReadOnlyList<MenuRow> Rows { get; }

        /// <summary>What the keys do here, for the hint line.</summary>
        public abstract string Hint { get; }

        /// <summary>
        /// Where the value column starts, or 0 to right-align. See
        /// <see cref="MenuListView.ValueColumn"/>.
        /// </summary>
        public virtual int ValueColumn => 0;

        /// <summary>
        /// Whether opening this row means asking LM Studio what it is serving first. Checked
        /// before everything else, because the answer decides whether there is a page to open.
        /// </summary>
        public virtual bool NeedsProbe(int index) => false;

        /// <summary>The page Right opens, or null when this row does not lead anywhere.</summary>
        public virtual SettingsPage? Enter(int index) => null;

        /// <summary>
        /// Whether this row is something to choose rather than something to open.
        /// <para>
        /// What Enter means depends on the answer: a row that can be chosen is chosen, and a row
        /// that cannot is opened. Right always opens regardless, which is what lets the adapter
        /// list offer both on the same row - Enter takes it, Right goes in and configures it.
        /// </para>
        /// <para>
        /// Asked separately from <see cref="Select"/> because that reports whether anything
        /// changed, and a row that was already the chosen one still has to count as choosable.
        /// </para>
        /// </summary>
        public virtual bool CanSelect(int index) => false;

        /// <summary>
        /// Puts this row in force. Returns whether anything actually changed, so the window only
        /// redraws when it must.
        /// </summary>
        public virtual bool Select(int index) => false;

        /// <summary>Whether this row is typed into, and what it currently holds.</summary>
        public virtual bool TryBeginEdit(int index, out string text)
        {
            text = string.Empty;
            return false;
        }

        /// <summary>Whether this row's value should be masked on screen and while being typed.</summary>
        public virtual bool IsSecret(int index) => false;

        /// <summary>
        /// Takes a typed value. Returns null when it was accepted, or the reason it was not - in
        /// which case the editor stays open on the offending row.
        /// </summary>
        public virtual string? Commit(int index, string text) => null;
    }
}
