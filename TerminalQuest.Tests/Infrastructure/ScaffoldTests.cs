using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Infrastructure
{
    /// <summary>
    /// Proves the harness itself works: the runner starts, and <c>InternalsVisibleTo</c> actually
    /// exposes the game's internal types. Everything else in the suite depends on both.
    /// </summary>
    public sealed class ScaffoldTests
    {
        [Fact]
        public void Internal_types_are_visible_to_the_test_assembly()
        {
            Assert.Equal(2, SaveStore.CurrentSchemaVersion);
        }

        [Fact]
        public void Temp_save_creates_a_usable_store()
        {
            using var save = new TempSave();

            Assert.True(Directory.Exists(save.Directory));
            Assert.Empty(save.Store.ReadCharacters().Characters);
        }
    }
}
