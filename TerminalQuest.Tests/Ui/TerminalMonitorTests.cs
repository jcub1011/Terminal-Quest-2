using TerminalQuest.Ui;
using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class TerminalMonitorTests
    {
        [Fact]
        public void TerminalSize_equality_works_as_expected()
        {
            var size1 = new TerminalSize(80, 24);
            var size2 = new TerminalSize(80, 24);
            var size3 = new TerminalSize(120, 30);

            Assert.Equal(size1, size2);
            Assert.NotEqual(size1, size3);
            Assert.Equal(80, size1.Width);
            Assert.Equal(24, size1.Height);
        }

        [Fact]
        public void GetSize_returns_positive_dimensions()
        {
            var size = TerminalMonitor.GetSize();
            Assert.True(size.Width >= 1);
            Assert.True(size.Height >= 1);
        }

        [Fact]
        public void HasResized_detects_difference_and_updates_lastKnownSize()
        {
            var lastSize = new TerminalSize(-1, -1);
            var resized = TerminalMonitor.HasResized(ref lastSize);

            Assert.True(resized);
            Assert.True(lastSize.Width >= 1);
            Assert.True(lastSize.Height >= 1);

            // Calling again with the same lastSize should return false
            var resizedAgain = TerminalMonitor.HasResized(ref lastSize);
            Assert.False(resizedAgain);
        }

        [Fact]
        public void ReadKeyOrResize_returns_default_when_cancelled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var key = TerminalMonitor.ReadKeyOrResize(
                onResize: null,
                pollIntervalMs: 5,
                cancellationToken: cts.Token);

            Assert.Equal(default(ConsoleKeyInfo), key);
        }

        [Fact]
        public async Task ReadKeyOrResizeAsync_returns_default_when_cancelled()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var key = await TerminalMonitor.ReadKeyOrResizeAsync(
                onResize: null,
                pollIntervalMs: 5,
                cancellationToken: cts.Token);

            Assert.Equal(default(ConsoleKeyInfo), key);
        }
    }
}
