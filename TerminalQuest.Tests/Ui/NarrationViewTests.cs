using TerminalQuest.Ui;
using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class NarrationViewTests
    {
        [Fact]
        public void AddLine_increases_total_rows()
        {
            using var view = new NarrationView();
            Assert.Equal(0, view.TotalRows);

            view.AddLine("First line of story", TextRole.Normal);
            Assert.Equal(1, view.TotalRows);
            Assert.Single(view.CommittedLines);
        }

        [Fact]
        public void AppendDelta_and_CommitBlock_stream_and_commit_text()
        {
            using var view = new NarrationView();
            Assert.Equal(0, view.TotalRows);

            view.AppendDelta("The ancient serpent speaks...");
            Assert.Equal(1, view.TotalRows);

            view.CommitBlock();
            Assert.Equal(1, view.TotalRows);
            Assert.Single(view.CommittedLines);
        }
    }
}
