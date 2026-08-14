using TerminalQuest.Saves;
using TerminalQuest.Settings;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// The one row that sets how much of a last session a resumed save recalls.
    /// </summary>
    public sealed class SettingsMemoryPageTests
    {
        private static SettingsMemoryPage Page(out AppSettings draft)
        {
            draft = new AppSettings();
            return new SettingsMemoryPage(draft);
        }

        [Fact]
        public void The_row_shows_the_size_in_force()
        {
            var draft = new AppSettings { TranscriptRecallCharacters = 2500 };

            Assert.Equal("2500 characters", Assert.Single(new SettingsMemoryPage(draft).Rows).Value);
        }

        [Fact]
        public void Editing_starts_from_the_current_number_alone()
        {
            // The units are on the row, not in the box: the player should be typing 2500, not
            // deleting the word "characters" first.
            var draft = new AppSettings { TranscriptRecallCharacters = 2500 };

            Assert.True(new SettingsMemoryPage(draft).TryBeginEdit(SettingsMemoryPage.RecallRow, out var text));
            Assert.Equal("2500", text);
        }

        [Fact]
        public void A_size_in_range_is_taken()
        {
            var page = Page(out var draft);

            Assert.Null(page.Commit(SettingsMemoryPage.RecallRow, " 2500 "));
            Assert.Equal(2500, draft.TranscriptRecallCharacters);
        }

        [Theory]
        [InlineData("")]
        [InlineData("lots")]
        [InlineData("2500 characters")]
        [InlineData("2.5")]
        [InlineData("-500")]
        public void Something_that_is_not_a_number_is_refused_with_a_reason(string typed)
        {
            var page = Page(out var draft);
            var before = draft.TranscriptRecallCharacters;

            Assert.NotNull(page.Commit(SettingsMemoryPage.RecallRow, typed));
            Assert.Equal(before, draft.TranscriptRecallCharacters);
        }

        [Theory]
        [InlineData(TranscriptRecall.MinCharacters - 1)]
        [InlineData(TranscriptRecall.MaxCharacters + 1)]
        public void A_size_outside_the_range_is_refused_rather_than_quietly_clamped(int typed)
        {
            // Clamping is for values arriving from a file or a model, where there is nobody to tell.
            // Here somebody is looking at the screen, and storing a different number than they typed
            // is how a setting comes to be mistrusted.
            var page = Page(out var draft);
            var before = draft.TranscriptRecallCharacters;

            var refusal = page.Commit(SettingsMemoryPage.RecallRow, typed.ToString());

            Assert.NotNull(refusal);
            Assert.Contains(TranscriptRecall.MinCharacters.ToString(), refusal, StringComparison.Ordinal);
            Assert.Contains(TranscriptRecall.MaxCharacters.ToString(), refusal, StringComparison.Ordinal);
            Assert.Equal(before, draft.TranscriptRecallCharacters);
        }

        [Theory]
        [InlineData(TranscriptRecall.MinCharacters)]
        [InlineData(TranscriptRecall.MaxCharacters)]
        public void The_bounds_themselves_are_allowed(int typed)
        {
            var page = Page(out var draft);

            Assert.Null(page.Commit(SettingsMemoryPage.RecallRow, typed.ToString()));
            Assert.Equal(typed, draft.TranscriptRecallCharacters);
        }

        [Fact]
        public void The_tabs_page_offers_it_and_opens_it()
        {
            // The row and the switch behind it are a pair; a name in one and not the other is either
            // unreachable or an error when taken.
            var draft = new AppSettings();
            var tabs = new SettingsTabsPage(draft);

            var index = tabs.Rows.ToList().FindIndex(row => row.Label == "Memory");

            Assert.True(index >= 0, "the settings screen should offer a Memory tab");
            Assert.IsType<SettingsMemoryPage>(tabs.Enter(index));
        }
    }
}
