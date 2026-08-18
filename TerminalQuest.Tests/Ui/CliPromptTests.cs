using TerminalQuest.Ui;
using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class CliPromptTests
    {
        [Fact]
        public async Task ReadLineAsync_returns_trimmed_line_when_redirected()
        {
            var originalIn = Console.In;
            try
            {
                using var stringReader = new StringReader("  look around  \n");
                Console.SetIn(stringReader);

                var prompt = new CliPrompt(new ExternalEditor(() => "notepad"));
                var input = await prompt.ReadLineAsync();

                Assert.Equal("look around", input);
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }

        [Fact]
        public async Task ReadLineAsync_returns_empty_when_empty_line_input()
        {
            var originalIn = Console.In;
            try
            {
                using var stringReader = new StringReader("\n");
                Console.SetIn(stringReader);

                var prompt = new CliPrompt(new ExternalEditor(() => "notepad"));
                var input = await prompt.ReadLineAsync();

                Assert.Equal(string.Empty, input);
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }

        [Fact]
        public void AskString_returns_default_when_redirected_and_empty()
        {
            var originalIn = Console.In;
            try
            {
                using var stringReader = new StringReader("\n");
                Console.SetIn(stringReader);

                var result = CliPrompt.AskString("Name: ", defaultValue: "Hero");

                Assert.Equal("Hero", result);
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }
    }
}
