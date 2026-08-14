using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// Keeping a copy of the prose a turn streamed, so the transcript records what the screen showed.
    /// </summary>
    public sealed class NarrationRecorderTests
    {
        [Fact]
        public void Nothing_streamed_is_nothing_recorded()
        {
            Assert.Empty(new NarrationRecorder().TakeAndClear());
        }

        [Fact]
        public void Deltas_are_joined_in_the_order_they_arrived()
        {
            var recorder = new NarrationRecorder();

            recorder.Append("The [item]iron ");
            recorder.Append("key[/item] ");
            recorder.Append("turns.");

            Assert.Equal("The [item]iron key[/item] turns.", recorder.TakeAndClear());
        }

        [Fact]
        public void Taking_empties_it_so_a_turn_cannot_be_recorded_twice()
        {
            var recorder = new NarrationRecorder();
            recorder.Append("The door gives.");

            recorder.TakeAndClear();

            Assert.Empty(recorder.TakeAndClear());
        }

        [Fact]
        public void Clearing_discards_an_unfinished_turn()
        {
            // The discard rule, at this level: a turn abandoned mid-sentence must not bequeath its
            // half-sentence to the next one.
            var recorder = new NarrationRecorder();
            recorder.Append("The door gi");

            recorder.Clear();
            recorder.Append("You are in a room.");

            Assert.Equal("You are in a room.", recorder.TakeAndClear());
        }

        [Fact]
        public void An_empty_delta_adds_nothing()
        {
            var recorder = new NarrationRecorder();

            recorder.Append(string.Empty);
            recorder.Append("one");

            Assert.Equal("one", recorder.TakeAndClear());
        }

        [Fact]
        public void Concurrent_deltas_all_survive()
        {
            // The event is raised on the provider's reader thread while the UI thread may be reading
            // the buffer at the end of a turn, so the lock is doing real work.
            var recorder = new NarrationRecorder();

            Parallel.For(0, 500, _ => recorder.Append("x"));

            Assert.Equal(500, recorder.TakeAndClear().Length);
        }
    }
}
