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

                var result = CliPrompt.AskString("Name: ", defaultValue: "Hero", onRepaint: () => { });

                Assert.Equal("Hero", result);
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }

        [Fact]
        public void AskInt_returns_parsed_integer_when_redirected()
        {
            var originalIn = Console.In;
            try
            {
                using var stringReader = new StringReader("42\n");
                Console.SetIn(stringReader);

                var result = CliPrompt.AskInt("Count: ", defaultValue: 10);

                Assert.Equal(42, result);
            }
            finally
            {
                Console.SetIn(originalIn);
            }
        }

        [Fact]
        public void Confirm_returns_default_when_redirected()
        {
            var result = CliPrompt.Confirm("Proceed?", defaultValue: true);
            Assert.True(result);

            var resultFalse = CliPrompt.Confirm("Proceed?", defaultValue: false);
            Assert.False(resultFalse);
        }

        [Fact]
        public void WaitKeyOrCancel_returns_true_when_redirected()
        {
            var result = CliPrompt.WaitKeyOrCancel("Press any key...");
            Assert.True(result);
        }
    }
}
