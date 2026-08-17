using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    public sealed class PromptAssetTests
    {
        [Fact]
        public void Narrator_prompt_asset_exists_and_loads()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "narrator-prompt.md");
            Assert.True(File.Exists(path), $"Expected {path} to exist in test output directory.");

            var content = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(content));
            Assert.Contains("ROLE", content);
            Assert.Equal(content, NarratorPromptFile.Default);
        }

        [Fact]
        public void Director_prompt_asset_exists_and_loads()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "director-prompt.md");
            Assert.True(File.Exists(path), $"Expected {path} to exist in test output directory.");

            var content = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(content));
            Assert.Contains("director", content, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(content, DirectorPromptFile.Default);
        }
    }
}
