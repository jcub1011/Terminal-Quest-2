using System.Text;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Keeps a copy of the prose a turn streamed, so it can be written to the transcript once the
    /// turn comes back whole.
    /// <para>
    /// <see cref="NarrationPump"/>'s sibling, subscribed to the same <c>OnTextDelta</c> event and
    /// existing for the same reason: that event is raised on a background thread, and what it carries
    /// has to reach somewhere that is not one. The pump's answer is to marshal onto the UI thread;
    /// this one's is a lock, because nothing here touches a view.
    /// </para>
    /// </summary>
    /// <remarks>
    /// Taken from the stream rather than from <c>AgentTurnResult.Text</c>, and the difference is not
    /// academic. On the Claude path that property is the CLI's <c>result</c> field, which carries the
    /// final assistant message; a turn that wrote a line, called a tool and then wrote another put
    /// both on the player's screen and may report only the second. Recording that would make the save
    /// disagree with what was read, which is the one thing a verbatim transcript exists to prevent.
    /// <para>
    /// So this records exactly what was drawn, because it is fed by exactly what drew it.
    /// </para>
    /// </remarks>
    internal sealed class NarrationRecorder
    {
        private readonly Lock _gate = new();
        private readonly StringBuilder _spoken = new();

        /// <summary>Called from the provider's reader thread, beside the pump.</summary>
        public void Append(string delta)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            lock (_gate)
            {
                _spoken.Append(delta);
            }
        }

        /// <summary>Forgets the turn in progress. Called before a turn, not after one.</summary>
        /// <remarks>
        /// Before rather than after, so that a turn abandoned mid-sentence - the player leaving, the
        /// provider failing - leaves nothing behind to be mistaken for the next turn's opening words.
        /// Whoever reads this only ever does so on the path where the turn finished.
        /// </remarks>
        public void Clear()
        {
            lock (_gate)
            {
                _spoken.Clear();
            }
        }

        /// <summary>Everything streamed since the last <see cref="Clear"/>, and empties the buffer.</summary>
        public string TakeAndClear()
        {
            lock (_gate)
            {
                var spoken = _spoken.ToString();
                _spoken.Clear();
                return spoken;
            }
        }
    }
}
