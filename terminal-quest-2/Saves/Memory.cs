namespace TerminalQuest.Saves
{
    /// <summary>
    /// Something a character knows.
    /// <para>
    /// Not merely what happened to them: what they witnessed, overheard, or concluded. A memory
    /// is the mechanism by which the world holds a grudge - a character who watched the player
    /// kill someone carries that, and the narrator reads it back before voicing them.
    /// </para>
    /// <para>
    /// <see cref="Text"/> is stored exactly as the narrator wrote it, placeholders and all. See
    /// <see cref="Placeholders"/> for why they survive to disk rather than being resolved on the
    /// way in.
    /// </para>
    /// </summary>
    internal sealed class Memory
    {
        /// <summary>Stable identifier within the owning character, assigned on write.</summary>
        public int Id { get; set; }

        /// <summary>The turn this was recorded on, so the narrator can weigh recent against old.</summary>
        public int Turn { get; set; }

        /// <summary>Unconstrained prose, with <c>{This}</c> and <c>{Player}</c> left unresolved.</summary>
        public string Text { get; set; } = string.Empty;

        /// <summary>
        /// The entities this memory involves, for retrieval only.
        /// <para>
        /// A retrieval index, not a schema constraint: <see cref="Text"/> is authoritative and a
        /// memory with no subjects is perfectly valid. This exists so that "what does Bess know
        /// about the player" can be answered without handing the narrator everything Bess has
        /// ever known.
        /// </para>
        /// </summary>
        public List<string> Subjects { get; set; } = [];
    }
}
