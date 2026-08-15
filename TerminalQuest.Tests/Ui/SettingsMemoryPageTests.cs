using TerminalQuest.Saves;
using TerminalQuest.Settings;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// Validation and boundary tests for the transcript recall setting.
    /// </summary>
    public sealed class SettingsMemoryPageTests
    {
        [Fact]
        public void Default_transcript_recall_is_within_valid_range()
        {
            var draft = new AppSettings();
            Assert.Equal(TranscriptRecall.DefaultCharacters, draft.TranscriptRecallCharacters);
            Assert.True(draft.TranscriptRecallCharacters >= TranscriptRecall.MinCharacters);
            Assert.True(draft.TranscriptRecallCharacters <= TranscriptRecall.MaxCharacters);
        }

        [Theory]
        [InlineData(TranscriptRecall.MinCharacters)]
        [InlineData(TranscriptRecall.MaxCharacters)]
        [InlineData(2500)]
        public void Valid_recall_character_counts_are_accepted(int characters)
        {
            var draft = new AppSettings { TranscriptRecallCharacters = characters };
            Assert.Equal(characters, draft.TranscriptRecallCharacters);
            Assert.True(characters >= TranscriptRecall.MinCharacters && characters <= TranscriptRecall.MaxCharacters);
        }

        [Theory]
        [InlineData(TranscriptRecall.MinCharacters - 1)]
        [InlineData(TranscriptRecall.MaxCharacters + 1)]
        [InlineData(-100)]
        [InlineData(0)]
        public void Out_of_range_values_are_detected(int characters)
        {
            var isValid = characters >= TranscriptRecall.MinCharacters && characters <= TranscriptRecall.MaxCharacters;
            Assert.False(isValid);
        }
    }
}
