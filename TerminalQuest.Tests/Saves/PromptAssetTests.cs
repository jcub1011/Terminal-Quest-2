using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    public sealed class PromptAssetTests
    {
        [Fact]
        public void Narrator_tools_prompt_asset_exists_and_loads()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "narrator-tools.md");
            Assert.True(File.Exists(path), $"Expected {path} to exist in test output directory.");

            var content = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(content));
            Assert.Contains("HOW TO CALL TOOLS", content);
            Assert.Equal(content, NarratorPromptFile.ToolsDefault);
        }

        [Fact]
        public void Narrator_story_prompt_asset_exists_and_loads()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "narrator-story.md");
            Assert.True(File.Exists(path), $"Expected {path} to exist in test output directory.");

            var content = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(content));
            Assert.Contains("ROLE", content);
            Assert.Equal(content, NarratorPromptFile.StoryDefault);
        }

        [Fact]
        public void Director_tools_prompt_asset_exists_and_loads()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "director-tools.md");
            Assert.True(File.Exists(path), $"Expected {path} to exist in test output directory.");

            var content = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(content));
            Assert.Contains("CORE DISCIPLINE", content);
            Assert.Equal(content, DirectorPromptFile.ToolsDefault);
        }

        [Fact]
        public void Director_story_prompt_asset_exists_and_loads()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "director-story.md");
            Assert.True(File.Exists(path), $"Expected {path} to exist in test output directory.");

            var content = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(content));
            Assert.Contains("director", content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("YES, AND", content);
            Assert.Equal(content, DirectorPromptFile.StoryDefault);
        }

        [Fact]
        public void Item_generator_prompt_asset_exists_and_loads()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "assets", "item-generator-prompt.txt");
            Assert.True(File.Exists(path), $"Expected {path} to exist in test output directory.");

            var content = File.ReadAllText(path);
            Assert.False(string.IsNullOrWhiteSpace(content));
            Assert.Contains("INSTRUCTIONS", content);
            Assert.Equal(content, ItemGeneratorPromptFile.DefaultPrompt);
        }
    }
}
