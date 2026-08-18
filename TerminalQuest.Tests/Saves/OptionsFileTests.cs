using System.Text.Json;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;
using Xunit;

namespace TerminalQuest.Tests.Saves
{
    public sealed class OptionsFileTests
    {
        [Fact]
        public void OptionsFile_serializes_and_deserializes_via_SaveJsonContext()
        {
            var options = new OptionsFile
            {
                Turn = 5,
                Options = ["Inspect the iron gate", "Climb the crumbling wall", "Call for help"],
            };

            var json = JsonSerializer.Serialize(options, SaveJsonContext.Readable.OptionsFile);
            var restored = JsonSerializer.Deserialize(json, SaveJsonContext.Readable.OptionsFile);

            Assert.NotNull(restored);
            Assert.Equal(5, restored.Turn);
            Assert.Equal(3, restored.Options.Count);
            Assert.Equal("Inspect the iron gate", restored.Options[0]);
            Assert.Equal("Climb the crumbling wall", restored.Options[1]);
            Assert.Equal("Call for help", restored.Options[2]);
        }

        [Fact]
        public void SaveStore_reads_and_writes_options_file()
        {
            using var save = new TempSave();

            var original = new OptionsFile
            {
                Turn = 2,
                Options = ["Option A", "Option B"],
            };

            save.Store.WriteOptions(original);
            Assert.True(save.Has("options.json"));

            var read = save.Store.ReadOptions();
            Assert.NotNull(read);
            Assert.Equal(2, read.Turn);
            Assert.Equal(2, read.Options.Count);
            Assert.Equal("Option A", read.Options[0]);
            Assert.Equal("Option B", read.Options[1]);
        }

        [Fact]
        public void SaveStore_ReadOptions_on_missing_file_returns_empty()
        {
            using var save = new TempSave();

            var read = save.Store.ReadOptions();
            Assert.NotNull(read);
            Assert.Equal(0, read.Turn);
            Assert.Empty(read.Options);
        }
    }
}
