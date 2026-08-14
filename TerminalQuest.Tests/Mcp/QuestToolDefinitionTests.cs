using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    /// <summary>
    /// Structural invariants over the tool table itself, rather than over what any tool does.
    /// </summary>
    /// <remarks>
    /// These are the tests that catch a tool being added and then silently left unreachable, or a
    /// schema that breaks the transport. Both are the kind of mistake that looks fine in review
    /// and only shows up as the narrator quietly not having a capability.
    /// </remarks>
    public sealed class QuestToolDefinitionTests
    {
        public static TheoryData<string> ToolNames()
        {
            var data = new TheoryData<string>();

            foreach (var tool in QuestTools.Definitions)
            {
                data.Add(tool.Name);
            }

            return data;
        }

        [Fact]
        public void There_are_tools_to_offer()
        {
            Assert.NotEmpty(QuestTools.Definitions);
        }

        [Fact]
        public void Tool_names_are_unique()
        {
            var names = QuestTools.Definitions.Select(tool => tool.Name).ToList();

            Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        }

        [Theory]
        [MemberData(nameof(ToolNames))]
        public void Every_advertised_tool_can_actually_be_called(string name)
        {
            // The failure this catches: a definition added to the table without a matching arm in
            // Invoke. The narrator would see the tool, call it, and be told it does not exist.
            using var save = new TempSave();

            var outcome = QuestTools.Invoke(save.Store, name, Arguments("{}"));

            Assert.DoesNotContain("There is no tool called", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void An_unknown_tool_is_refused_by_name()
        {
            using var save = new TempSave();

            var outcome = QuestTools.Invoke(save.Store, "no_such_tool", Arguments("{}"));

            Assert.True(outcome.IsError);
            Assert.Contains("no_such_tool", outcome.Text, StringComparison.Ordinal);
        }

        [Theory]
        [MemberData(nameof(ToolNames))]
        public void Every_schema_is_valid_json(string name)
        {
            var tool = QuestTools.Definitions.Single(t => t.Name == name);

            using var document = JsonDocument.Parse(tool.InputSchema);

            Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            Assert.Equal("object", document.RootElement.GetProperty("type").GetString());
        }

        [Theory]
        [MemberData(nameof(ToolNames))]
        public void Every_schema_is_a_single_line(string name)
        {
            // The transport is newline-delimited JSON. A literal newline inside a schema splits
            // one frame into two and the client drops the connection.
            var tool = QuestTools.Definitions.Single(t => t.Name == name);

            Assert.DoesNotContain('\n', tool.InputSchema);
            Assert.DoesNotContain('\r', tool.InputSchema);
        }

        [Theory]
        [MemberData(nameof(ToolNames))]
        public void Every_tool_explains_itself_to_the_narrator(string name)
        {
            var tool = QuestTools.Definitions.Single(t => t.Name == name);

            Assert.False(string.IsNullOrWhiteSpace(tool.Description));
        }

        [Fact]
        public void A_schema_that_is_not_json_is_refused_at_construction()
        {
            Assert.ThrowsAny<JsonException>(() => new QuestTool("x", "y", "{ not json"));
        }

        [Fact]
        public void The_allowed_tools_flag_names_every_tool_and_nothing_else()
        {
            // Built from Definitions so a tool cannot be added and then silently left unavailable
            // to the CLI.
            var allowed = QuestTools.AllowedTools().Split(',');

            Assert.Equal(QuestTools.Definitions.Count, allowed.Length);
            Assert.Equal(
                QuestTools.Definitions.Select(tool => $"mcp__{QuestTools.ServerName}__{tool.Name}").Order().ToList(),
                allowed.Order().ToList());
        }

        [Fact]
        public void The_server_name_is_the_one_the_tool_prefix_is_built_from()
        {
            Assert.Equal("quest", QuestTools.ServerName);
            Assert.All(
                QuestTools.AllowedTools().Split(','),
                entry => Assert.StartsWith("mcp__quest__", entry, StringComparison.Ordinal));
        }

        private static JsonElement Arguments(string json) =>
            JsonDocument.Parse(json).RootElement.Clone();
    }
}
