using System.Text.Json;
using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;
using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    public sealed class PresentOptionsTests
    {
        private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static ToolOutcome Call(SaveStore store, string arguments, string toolName = "present_options") =>
            QuestTools.Invoke(store, toolName, Args(arguments));

        [Fact]
        public void Tool_definition_is_registered_with_narrator_role()
        {
            var def = QuestTools.Definitions.FirstOrDefault(d => d.Name == "present_options");
            Assert.NotNull(def);
            Assert.Equal(ToolRole.Narrator, def.Role);
            Assert.Contains("options", def.InputSchema, StringComparison.Ordinal);
        }

        [Fact]
        public void Valid_options_are_saved_to_store_with_current_turn()
        {
            using var save = new TempSave();
            save.Store.Touch(3);

            var outcome = Call(save.Store, """{"options":["Approach the gate","Circle around","Call out"]}""");

            Assert.False(outcome.IsError);
            Assert.Contains("Presented 3 options", outcome.Text);

            var file = save.Store.ReadOptions();
            Assert.Equal(3, file.Turn);
            Assert.Equal(3, file.Options.Count);
            Assert.Equal("Approach the gate", file.Options[0]);
            Assert.Equal("Circle around", file.Options[1]);
            Assert.Equal("Call out", file.Options[2]);
        }

        [Theory]
        [InlineData("present_options")]
        [InlineData("options")]
        [InlineData("present")]
        public void Tool_aliases_dispatch_to_present_options(string toolName)
        {
            using var save = new TempSave();
            save.Store.Touch(1);

            var outcome = Call(save.Store, """{"options":["Go left","Go right"]}""", toolName);

            Assert.False(outcome.IsError);
            var file = save.Store.ReadOptions();
            Assert.Equal(2, file.Options.Count);
        }

        [Fact]
        public void Empty_options_array_fails()
        {
            using var save = new TempSave();
            save.Store.Touch(1);

            var outcome = Call(save.Store, """{"options":[]}""");

            Assert.True(outcome.IsError);
            Assert.Contains("needs a non-empty array", outcome.Text);
        }

        [Fact]
        public void Blank_only_options_fail()
        {
            using var save = new TempSave();
            save.Store.Touch(1);

            var outcome = Call(save.Store, """{"options":["  ", ""]}""");

            Assert.True(outcome.IsError);
            Assert.Contains("only blank options", outcome.Text);
        }

        [Fact]
        public void ClearOptions_resets_options_list()
        {
            using var save = new TempSave();
            save.Store.Touch(5);

            Call(save.Store, """{"options":["Option 1","Option 2"]}""");
            Assert.Equal(2, save.Store.ReadOptions().Options.Count);

            save.Store.ClearOptions();
            var cleared = save.Store.ReadOptions();
            Assert.Empty(cleared.Options);
            Assert.Equal(5, cleared.Turn);
        }
    }
}
