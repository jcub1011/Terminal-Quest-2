using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    public sealed class DirectorPromptFileTests
    {
        private const string FileName = "director-story.txt";
        private const string LegacyFileName = "director-prompt.txt";

        [Fact]
        public void A_save_without_the_file_reads_as_the_default()
        {
            using var save = new TempSave();

            Assert.Equal(DirectorPromptFile.StoryDefault, DirectorPromptFile.Read(save.Store));
            Assert.False(save.Has(FileName));
        }

        [Fact]
        public void An_absent_file_reads_as_null_from_store()
        {
            using var save = new TempSave();

            Assert.Null(save.Store.ReadDirectorStory());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\r\n")]
        public void An_empty_file_falls_back_to_the_default(string contents)
        {
            using var save = new TempSave();

            save.WriteRaw(FileName, contents);

            Assert.Equal(DirectorPromptFile.StoryDefault, DirectorPromptFile.Read(save.Store));
        }

        [Fact]
        public void Ensure_seeds_the_default_when_missing()
        {
            using var save = new TempSave();

            var seeded = DirectorPromptFile.Ensure(save.Store);

            Assert.NotEmpty(seeded);
            Assert.True(save.Has(FileName));
            Assert.Equal(seeded, save.Store.ReadDirectorStory());
        }

        [Fact]
        public void Ensure_does_not_overwrite_existing_file()
        {
            using var save = new TempSave();
            const string custom = "Custom director instructions.";
            save.Store.WriteDirectorStory(custom);

            var read = DirectorPromptFile.Ensure(save.Store);

            Assert.Equal(custom, read);
            Assert.Equal(custom, save.Store.ReadDirectorStory());
        }

        [Fact]
        public void Ensure_migrates_legacy_director_prompt_file()
        {
            const string LegacyPrompt = "Legacy director instructions.";

            using var save = new TempSave();
            save.WriteRaw(LegacyFileName, LegacyPrompt);

            var result = DirectorPromptFile.Ensure(save.Store);

            Assert.Equal(LegacyPrompt, result);
            Assert.True(save.Has(FileName));
            Assert.Equal(LegacyPrompt, save.ReadRaw(FileName));
        }

        [Fact]
        public void UpdateStory_overwrites_with_latest_default()
        {
            using var save = new TempSave();
            save.Store.WriteDirectorStory("Old custom instructions");

            var updated = DirectorPromptFile.UpdateStory(save.Store);

            Assert.Equal(DirectorPromptFile.StoryDefault.ReplaceLineEndings(), updated);
            Assert.Equal(updated, save.Store.ReadDirectorStory());
        }

        [Fact]
        public void Compose_combines_tools_and_story_prompts()
        {
            using var save = new TempSave();
            const string CustomStory = "Custom grimdark campaign.";
            save.Store.WriteDirectorStory(CustomStory);

            var composed = DirectorPromptFile.Compose(save.Store);

            Assert.Contains(CustomStory, composed, StringComparison.Ordinal);
            Assert.Contains(DirectorPromptFile.ToolsDefault.Trim(), composed, StringComparison.Ordinal);
            Assert.Contains("---", composed, StringComparison.Ordinal);
        }

        [Fact]
        public void The_defaults_are_plain_ascii()
        {
            Assert.DoesNotContain(DirectorPromptFile.ToolsDefault, character => character > '\x7f');
            Assert.DoesNotContain(DirectorPromptFile.StoryDefault, character => character > '\x7f');
        }
    }
}
