using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    public sealed class DirectorPromptFileTests
    {
        private const string FileName = "director-prompt.txt";

        [Fact]
        public void A_save_without_the_file_reads_as_the_default()
        {
            using var save = new TempSave();

            Assert.Equal(DirectorPromptFile.Default, DirectorPromptFile.Read(save.Store));
            Assert.False(save.Has(FileName));
        }

        [Fact]
        public void An_absent_file_reads_as_null_from_store()
        {
            using var save = new TempSave();

            Assert.Null(save.Store.ReadDirectorPrompt());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\r\n")]
        public void An_empty_file_falls_back_to_the_default(string contents)
        {
            using var save = new TempSave();

            save.WriteRaw(FileName, contents);

            Assert.Equal(DirectorPromptFile.Default, DirectorPromptFile.Read(save.Store));
        }

        [Fact]
        public void Ensure_seeds_the_default_when_missing()
        {
            using var save = new TempSave();

            var seeded = DirectorPromptFile.Ensure(save.Store);

            Assert.NotEmpty(seeded);
            Assert.True(save.Has(FileName));
            Assert.Equal(seeded, save.Store.ReadDirectorPrompt());
        }

        [Fact]
        public void Ensure_does_not_overwrite_existing_file()
        {
            using var save = new TempSave();
            const string custom = "Custom director instructions.";
            save.Store.WriteDirectorPrompt(custom);

            var read = DirectorPromptFile.Ensure(save.Store);

            Assert.Equal(custom, read);
            Assert.Equal(custom, save.Store.ReadDirectorPrompt());
        }
    }
}
