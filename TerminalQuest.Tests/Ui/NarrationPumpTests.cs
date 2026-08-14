using TerminalQuest.Tests.Infrastructure;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// The queue that carries streamed narration from the reader thread onto the UI thread.
    /// </summary>
    /// <remarks>
    /// The interesting behaviour is the drain gate: one drain is scheduled at a time and it empties
    /// the whole queue, so a fast stream coalesces into few updates. The failure that gate can have
    /// is a delta arriving mid-drain and being stranded in the queue with no drain scheduled to
    /// collect it — narration that simply stops appearing until the next token happens to arrive.
    /// </remarks>
    public sealed class NarrationPumpTests
    {
        /// <summary>Runs the work immediately, as a UI thread that was already free would.</summary>
        private static NarrationPump Immediate(RecordingSink sink) =>
            new(action => action(), sink);

        /// <summary>Collects the work instead of running it, so a test can choose when it happens.</summary>
        private static NarrationPump Deferred(RecordingSink sink, List<Action> pending) =>
            new(pending.Add, sink);

        [Fact]
        public void A_delta_reaches_the_view()
        {
            var sink = new RecordingSink();
            var pump = Immediate(sink);

            pump.Enqueue("hello");

            Assert.Equal("hello", sink.Text);
        }

        [Fact]
        public void An_empty_delta_is_ignored()
        {
            var sink = new RecordingSink();
            var pump = Immediate(sink);

            pump.Enqueue(string.Empty);

            Assert.Empty(sink.Deltas);
        }

        [Fact]
        public void A_burst_of_deltas_coalesces_into_one_update()
        {
            // The whole point of the queue: a fast stream must not post one work item per token.
            var sink = new RecordingSink();
            var pending = new List<Action>();
            var pump = Deferred(sink, pending);

            pump.Enqueue("a");
            pump.Enqueue("b");
            pump.Enqueue("c");

            Assert.Single(pending);

            pending[0]();

            Assert.Equal("abc", sink.Text);
            Assert.Single(sink.Deltas);
        }

        [Fact]
        public void Only_one_drain_is_scheduled_at_a_time()
        {
            var sink = new RecordingSink();
            var pending = new List<Action>();
            var pump = Deferred(sink, pending);

            for (var i = 0; i < 50; i++)
            {
                pump.Enqueue("x");
            }

            Assert.Single(pending);
        }

        [Fact]
        public void A_delta_arriving_mid_drain_is_not_stranded()
        {
            // The gate is cleared before draining precisely so this schedules a fresh drain rather
            // than leaving the delta sitting in the queue with nothing coming to collect it.
            var sink = new RecordingSink();
            var pending = new List<Action>();
            var pump = Deferred(sink, pending);

            pump.Enqueue("first");
            sink.OnAppend = () => pump.Enqueue("second");

            pending[0]();

            Assert.Equal(2, pending.Count);
            pending[1]();

            Assert.Equal("firstsecond", sink.Text);
        }

        [Fact]
        public void A_drain_that_finds_nothing_appends_nothing()
        {
            // The cost of clearing the gate first is at worst one extra no-op pass.
            var sink = new RecordingSink();
            var pending = new List<Action>();
            var pump = Deferred(sink, pending);

            pump.Enqueue("a");
            pending[0]();
            pending[0]();

            Assert.Single(sink.Deltas);
        }

        [Fact]
        public void A_new_drain_is_scheduled_after_the_previous_one_ran()
        {
            var sink = new RecordingSink();
            var pending = new List<Action>();
            var pump = Deferred(sink, pending);

            pump.Enqueue("a");
            pending[0]();

            pump.Enqueue("b");

            Assert.Equal(2, pending.Count);
        }

        // ---- Closing a paragraph ---------------------------------------------------------------

        [Fact]
        public void Completing_a_block_flushes_what_is_queued_first()
        {
            var sink = new RecordingSink();
            var pump = Immediate(sink);

            pump.Enqueue("tail");
            pump.CompleteBlock();

            Assert.Equal("tail", sink.Text);
            Assert.Equal(1, sink.Commits);
        }

        [Fact]
        public void Completing_a_block_now_drains_on_the_calling_thread()
        {
            // The roll drain is already on the UI thread and has to close the paragraph *before*
            // it adds its lines; going through Invoke would queue that behind the caller and the
            // roll would land in the wrong order.
            var sink = new RecordingSink();
            var pending = new List<Action>();
            var pump = Deferred(sink, pending);

            pump.Enqueue("tail");
            pump.CompleteBlockNow();

            Assert.Equal("tail", sink.Text);
            Assert.Equal(1, sink.Commits);
        }

        [Fact]
        public void Completing_an_empty_block_still_closes_the_paragraph()
        {
            var sink = new RecordingSink();
            var pump = Immediate(sink);

            pump.CompleteBlockNow();

            Assert.Empty(sink.Deltas);
            Assert.Equal(1, sink.Commits);
        }

        // ---- Under real threads ------------------------------------------------------------------

        [Fact]
        public async Task Nothing_is_lost_when_deltas_arrive_from_another_thread()
        {
            // Enqueue is called from the agent's reader thread; the drain runs on the UI thread.
            var sink = new RecordingSink();
            var pending = new System.Collections.Concurrent.ConcurrentQueue<Action>();
            var pump = new NarrationPump(pending.Enqueue, sink);

            const int count = 500;

            var producer = Task.Run(
                () =>
                {
                    for (var i = 0; i < count; i++)
                    {
                        pump.Enqueue("x");
                    }
                },
                TestContext.Current.CancellationToken);

            var drained = 0;
            while (!producer.IsCompleted || !pending.IsEmpty)
            {
                if (pending.TryDequeue(out var work))
                {
                    work();
                    drained++;
                }
            }

            await producer;

            // Drain once more: the producer may have enqueued after the last dequeue.
            while (pending.TryDequeue(out var work))
            {
                work();
                drained++;
            }

            Assert.Equal(count, sink.Text.Length);
            Assert.True(drained <= count, "the gate should have coalesced at least some deltas");
        }

        // ---- Guards -------------------------------------------------------------------------------

        [Fact]
        public void A_null_sink_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(() => new NarrationPump(action => action(), null!));
        }

        [Fact]
        public void A_null_invoker_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(() => new NarrationPump((Action<Action>)null!, new RecordingSink()));
        }
    }
}
