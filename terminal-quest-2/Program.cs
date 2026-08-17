using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Settings;
using TerminalQuest.Ui;

namespace TerminalQuest
{
    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // Re-entry as the narrator's state server. This branch must come before anything that
            // touches the console: stdout is the MCP transport from here on.
            if (args is ["--mcp-server", var saveDirectory, ..])
            {
                return await RunStateServerAsync(saveDirectory);
            }

            return await RunGameAsync();
        }

        /// <summary>
        /// Serves one save folder over stdio until the parent closes the pipe.
        /// </summary>
        private static async Task<int> RunStateServerAsync(string saveDirectory)
        {
            try
            {
                var store = new SaveStore(saveDirectory);
                store.RequireSupportedSchema();
                QuestJournal.OnFailure = message => Console.Error.WriteLine($"quest server: {message}");
                return await McpServer.RunAsync(store);
            }
            catch (Exception ex)
            {
                await Console.Error.WriteLineAsync($"quest server failed: {ex.Message}");
                return 1;
            }
        }

        private static async Task<int> RunGameAsync()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            PathProvider.EnsureMigrated();

            var settings = SettingsStore.Read();
            var editor = new ExternalEditor(() => settings.EditorCommand);

            while (true)
            {
                var store = await MainMenuController.RunAsync(settings, editor);
                if (store is null)
                {
                    return 0;
                }

                await GameLoop.RunAsync(settings, store, editor);
            }
        }
    }
}
