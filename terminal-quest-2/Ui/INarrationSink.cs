namespace TerminalQuest.Ui
{
    /// <summary>
    /// Where <see cref="NarrationPump"/> puts the text it has drained.
    /// </summary>
    /// <remarks>
    /// The pump's job is the threading — the <see cref="System.Threading.Interlocked"/> drain gate
    /// and the coalescing of a fast token stream into few updates. None of that needs a view, so
    /// the pump depends on this instead of on <see cref="NarrationView"/> directly and its
    /// behaviour can be checked without a terminal.
    /// </remarks>
    internal interface INarrationSink
    {
        /// <summary>Appends streamed text to the paragraph being written.</summary>
        void AppendDelta(string text);

        /// <summary>Closes the current paragraph.</summary>
        void CommitBlock();
    }
}
