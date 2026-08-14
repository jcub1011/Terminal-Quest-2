using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The two columns the save menu formats for itself.
    /// </summary>
    public sealed class SaveEntryTests
    {
        private static SaveEntry OfSize(long bytes) => new("Save", DateTimeOffset.Now, 1, bytes);

        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(1, "1 B")]
        [InlineData(1023, "1023 B")]
        [InlineData(1024, "1.0 KB")]
        [InlineData(1536, "1.5 KB")]
        [InlineData(1048575, "1024.0 KB")]
        [InlineData(1048576, "1.0 MB")]
        [InlineData(1572864, "1.5 MB")]
        public void Size_is_shown_at_the_right_scale(long bytes, string expected)
        {
            Assert.Equal(expected, OfSize(bytes).SizeText);
        }

        [Fact]
        public void A_size_that_could_not_be_measured_reads_as_zero()
        {
            // SavePaths.Measure returns 0 rather than failing the whole menu over one unreadable
            // file, so this is the string a permission problem produces.
            Assert.Equal("0 B", OfSize(0).SizeText);
        }

        [Fact]
        public void A_nonsensical_negative_size_still_formats()
        {
            Assert.Equal("-1 B", OfSize(-1).SizeText);
        }

        [Fact]
        public void A_save_nobody_has_played_says_so()
        {
            var entry = new SaveEntry("Save", default, 0, 0);

            Assert.Equal("never", entry.LastPlayedText);
        }

        [Fact]
        public void A_played_save_is_stamped_in_the_players_own_time_zone()
        {
            // Compared against a locally computed expectation rather than a fixed string: the
            // machine's zone is not something the test gets to choose.
            var played = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.FromHours(2));
            var entry = new SaveEntry("Save", played, 12, 0);

            Assert.Equal(played.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), entry.LastPlayedText);
        }

        [Fact]
        public void The_stamp_is_absolute_rather_than_relative()
        {
            // The whole point of the column is telling two similar saves apart, so it must never
            // degrade into "yesterday".
            var entry = new SaveEntry("Save", DateTimeOffset.Now.AddMinutes(-5), 3, 0);

            Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}$", entry.LastPlayedText);
        }
    }
}
