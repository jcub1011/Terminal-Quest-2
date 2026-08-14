using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    /// <summary>
    /// The <c>--mcp-config</c> payload that points the narrator's CLI at one save.
    /// </summary>
    /// <remarks>
    /// Asserted on structure rather than on the exact command: how a second copy of the program is
    /// launched depends on whether the process is a published executable or the shared
    /// <c>dotnet</c> host, and under a test runner it is neither of the two the game ships as.
    /// </remarks>
    public sealed class QuestServerConfigTests
    {
        private static JsonElement Server(string saveDirectory)
        {
            var json = QuestServerConfig.Build(saveDirectory);

            return JsonDocument.Parse(json).RootElement
                .GetProperty("mcpServers")
                .GetProperty(QuestTools.ServerName)
                .Clone();
        }

        [Fact]
        public void The_server_speaks_over_stdio()
        {
            using var save = new TempSave();

            Assert.Equal("stdio", Server(save.Directory).GetProperty("type").GetString());
        }

        [Fact]
        public void The_config_declares_a_command_to_run()
        {
            using var save = new TempSave();

            Assert.False(string.IsNullOrWhiteSpace(Server(save.Directory).GetProperty("command").GetString()));
        }

        [Fact]
        public void The_arguments_end_by_pointing_the_child_at_the_save()
        {
            // The child is this same binary re-entered with --mcp-server, so these two arguments
            // are the whole contract between the parent and the state server.
            using var save = new TempSave();

            var args = Server(save.Directory)
                .GetProperty("args")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToList();

            Assert.Equal("--mcp-server", args[^2]);
            Assert.Equal(Path.GetFullPath(save.Directory), args[^1]);
        }

        [Fact]
        public void The_save_path_is_made_absolute()
        {
            var args = Server("relative-save")
                .GetProperty("args")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToList();

            Assert.True(Path.IsPathFullyQualified(args[^1]));
        }

        [Fact]
        public void The_config_is_valid_json_the_cli_can_read()
        {
            using var save = new TempSave();

            var document = JsonDocument.Parse(QuestServerConfig.Build(save.Directory));

            Assert.Equal(JsonValueKind.Object, document.RootElement.GetProperty("mcpServers").ValueKind);
        }

        [Fact]
        public void Only_the_quest_server_is_declared()
        {
            // The session sees no MCP servers beyond the one supplied here.
            using var save = new TempSave();

            var servers = JsonDocument.Parse(QuestServerConfig.Build(save.Directory))
                .RootElement.GetProperty("mcpServers");

            Assert.Single(servers.EnumerateObject().ToList());
        }

        [Theory]
        [InlineData("")]
        public void An_empty_save_directory_is_a_programming_error(string directory)
        {
            Assert.Throws<ArgumentException>(() => QuestServerConfig.Build(directory));
        }

        [Fact]
        public void A_null_save_directory_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(() => QuestServerConfig.Build(null!));
        }

        [Fact]
        public void A_path_with_spaces_survives_as_one_argument()
        {
            // The whole reason the payload is JSON rather than a command line: quoting is somebody
            // else's problem exactly once.
            using var save = new TempSave("A Save With Spaces");

            var args = Server(save.Directory)
                .GetProperty("args")
                .EnumerateArray()
                .Select(value => value.GetString()!)
                .ToList();

            Assert.Equal(Path.GetFullPath(save.Directory), args[^1]);
            Assert.Contains(' ', args[^1]);
        }
    }
}
