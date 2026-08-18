using TerminalQuest.Saves;
using Xunit;

namespace TerminalQuest.Tests.Infrastructure
{
    [Collection(EnvironmentCollection.Name)]
    [Trait(Categories.Name, Categories.Environment)]
    public sealed class PathProviderTests
    {
        [Fact]
        public void Saves_are_composed_from_root_and_settings_from_app_directory()
        {
            using var root = new SavesRoot();

            Assert.Equal(Path.Combine(root.Root, "Saves"), PathProvider.Saves);
            Assert.Equal(Path.Combine(AppDirectory.Root, "Settings"), PathProvider.Settings);
        }

        [Fact]
        public void Legacy_settings_file_is_migrated_to_settings_subfolder()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "TerminalQuest.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var legacySettings = Path.Combine(tempRoot, "settings.json");
                File.WriteAllText(legacySettings, "{\"provider\": 0}");

                PathProvider.Migrate(tempRoot);

                var newSettings = Path.Combine(tempRoot, "Settings", "settings.json");
                Assert.True(File.Exists(newSettings));
                Assert.False(File.Exists(legacySettings));
                Assert.Equal("{\"provider\": 0}", File.ReadAllText(newSettings));
            }
            finally
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }

        [Fact]
        public void Legacy_save_folders_in_root_are_migrated_to_saves_subfolder()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "TerminalQuest.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var legacySaveDir = Path.Combine(tempRoot, "Riverbend");
                Directory.CreateDirectory(legacySaveDir);
                File.WriteAllText(Path.Combine(legacySaveDir, "save.json"), "{\"turn\": 5}");

                PathProvider.Migrate(tempRoot);

                var newSaveDir = Path.Combine(tempRoot, "Saves", "Riverbend");
                Assert.True(Directory.Exists(newSaveDir));
                Assert.True(File.Exists(Path.Combine(newSaveDir, "save.json")));
                Assert.False(Directory.Exists(legacySaveDir));
            }
            finally
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }

        [Fact]
        public void Migration_leaves_reserved_folders_alone()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "TerminalQuest.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var savesDir = Path.Combine(tempRoot, "Saves");
                var settingsDir = Path.Combine(tempRoot, "Settings");
                var editDir = Path.Combine(tempRoot, "edit");

                Directory.CreateDirectory(savesDir);
                Directory.CreateDirectory(settingsDir);
                Directory.CreateDirectory(editDir);

                File.WriteAllText(Path.Combine(editDir, "scratch.txt"), "some draft");

                PathProvider.Migrate(tempRoot);

                Assert.True(Directory.Exists(savesDir));
                Assert.True(Directory.Exists(settingsDir));
                Assert.True(Directory.Exists(editDir));
                Assert.True(File.Exists(Path.Combine(editDir, "scratch.txt")));
            }
            finally
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }

        [Fact]
        public void Migration_is_idempotent()
        {
            var tempRoot = Path.Combine(Path.GetTempPath(), "TerminalQuest.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempRoot);

            try
            {
                var legacySettings = Path.Combine(tempRoot, "settings.json");
                File.WriteAllText(legacySettings, "{\"provider\": 0}");

                var legacySave = Path.Combine(tempRoot, "Castle");
                Directory.CreateDirectory(legacySave);
                File.WriteAllText(Path.Combine(legacySave, "save.json"), "{}");

                // Run migration multiple times
                PathProvider.Migrate(tempRoot);
                PathProvider.Migrate(tempRoot);

                Assert.True(File.Exists(Path.Combine(tempRoot, "Settings", "settings.json")));
                Assert.True(Directory.Exists(Path.Combine(tempRoot, "Saves", "Castle")));
            }
            finally
            {
                try { Directory.Delete(tempRoot, recursive: true); } catch { }
            }
        }
    }
}
