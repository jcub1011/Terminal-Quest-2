using System.Collections.Concurrent;
using System.Text;
using Terminal.Gui.App;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Carries streamed narration from the Claude reader thread onto the UI thread.
    /// <para>
    /// <c>ClaudeSession.OnTextDelta</c> is raised on a background thread. Views are not
    /// thread-safe, so nothing here touches <see cref="NarrationView"/> directly - deltas are
    /// queued and applied inside <see cref="IApplication.Invoke(Action)"/>.
    /// </para>
    /// <para>
    /// A single drain is scheduled at a time and it empties the whole queue, so a fast stream
    /// coalesces into few UI updates instead of posting one work item per token.
    /// </para>
    /// </summary>
    internal sealed class NarrationPump
    {
        private readonly IApplication _app;
        private readonly NarrationView _view;
        private readonly ConcurrentQueue<string> _queue = new();

        private int _drainScheduled;

        public NarrationPump(IApplication app, NarrationView view)
        {
            _app = app;
            _view = view;
        }

        /// <summary>Called from the Claude reader thread. Must not touch any view.</summary>
        public void Enqueue(string delta)
        {
            if (string.IsNullOrEmpty(delta))
            {
                return;
            }

            _queue.Enqueue(delta);

            if (Interlocked.CompareExchange(ref _drainScheduled, 1, 0) == 0)
            {
                _app.Invoke(Drain);
            }
        }

        private void Drain()
        {
            // Clear the gate before draining. If a delta arrives mid-drain it will schedule a
            // fresh drain rather than being stranded in the queue; the cost is at worst one
            // extra no-op pass.
            Volatile.Write(ref _drainScheduled, 0);

            var batch = new StringBuilder();
            while (_queue.TryDequeue(out var delta))
            {
                batch.Append(delta);
            }

            if (batch.Length > 0)
            {
                _view.AppendDelta(batch.ToString());
            }
        }

        /// <summary>
        /// Flushes anything still queued and closes the paragraph. Safe to call from any thread;
        /// the work is marshalled like everything else.
        /// </summary>
        public void CompleteBlock() => _app.Invoke(CompleteBlockNow);

        /// <summary>
        /// The same, on the calling thread and without marshalling. Only safe from the UI thread.
        /// </summary>
        /// <remarks>
        /// Exists for the roll drain, which is already on the UI thread and has to close the
        /// paragraph <em>before</em> it adds its lines. Going through
        /// <see cref="IApplication.Invoke(Action)"/> would queue that work behind the caller instead,
        /// so the roll would be appended first and the paragraph closed after - which is the exact
        /// ordering the drain exists to avoid.
        /// </remarks>
        public void CompleteBlockNow()
        {
            Drain();
            _view.CommitBlock();
        }
    }
}
