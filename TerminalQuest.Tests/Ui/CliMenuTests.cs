using TerminalQuest.Ui;
using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class CliMenuTests
    {
        [Fact]
        public void MatchItem_matches_by_exact_text()
        {
            string[] items = ["Continue", "New Game", "Settings", "Quit"];
            var match = CliMenu.MatchItem(items, "New Game", s => s, null);
            Assert.Equal("New Game", match);
        }

        [Fact]
        public void MatchItem_matches_case_insensitively()
        {
            string[] items = ["Continue", "New Game", "Settings", "Quit"];
            var match = CliMenu.MatchItem(items, "settings", s => s, null);
            Assert.Equal("Settings", match);
        }

        [Fact]
        public void MatchItem_matches_by_prefix()
        {
            string[] items = ["Continue", "New Game", "Settings", "Quit"];
            var match = CliMenu.MatchItem(items, "qui", s => s, null);
            Assert.Equal("Quit", match);
        }

        [Fact]
        public void MatchItem_matches_by_match_key()
        {
            var items = new (string Name, string Code)[]
            {
                ("Continue", "c"),
                ("New Game", "n"),
                ("Quit", "q")
            };

            var match = CliMenu.MatchItem(items, "q", i => i.Name, i => i.Code);
            Assert.Equal("Quit", match.Name);
        }

        [Fact]
        public void MatchItem_returns_default_when_no_match()
        {
            string[] items = ["Continue", "New Game", "Quit"];
            var match = CliMenu.MatchItem(items, "xyz", s => s, null);
            Assert.Null(match);
        }
    }
}
