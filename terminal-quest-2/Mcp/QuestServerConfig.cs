using System.Buffers;
using System.Text;
using System.Text.Json;

namespace TerminalQuest.Mcp
{
    /// <summary>
    /// Builds the <c>--mcp-config</c> payload that points the narrator's CLI at a save.
    /// <para>
    /// The server is this same binary, re-entered with <c>--mcp-server</c>. Shipping the state
    /// server inside the game rather than beside it means there is no second executable to deploy,
    /// version or find on PATH - and no way for the two to drift apart.
    /// </para>
    /// </summary>
    internal static class QuestServerConfig
    {
        /// <summary>The assembly name, needed only for the <c>dotnet TerminalQuest.dll</c> launch style.</summary>
        private const string AssemblyFileName = "TerminalQuest.dll";

        /// <summary>Config JSON declaring the quest server for one save folder.</summary>
        /// <param name="saveDirectory">Absolute path to the save. Passed to the child verbatim.</param>
        public static string Build(string saveDirectory)
        {
            ArgumentException.ThrowIfNullOrEmpty(saveDirectory);

            var (command, leadingArguments) = ResolveLaunch();

            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteStartObject("mcpServers");
                writer.WriteStartObject(QuestTools.ServerName);

                writer.WriteString("type", "stdio");
                writer.WriteString("command", command);

                writer.WriteStartArray("args");
                foreach (var argument in leadingArguments)
                {
                    writer.WriteStringValue(argument);
                }

                writer.WriteStringValue("--mcp-server");
                writer.WriteStringValue(Path.GetFullPath(saveDirectory));
                writer.WriteEndArray();

                writer.WriteEndObject();
                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        /// <summary>
        /// How to start a second copy of this program.
        /// <para>
        /// Published ahead-of-time, the process is its own executable and this is just its path.
        /// Run as <c>dotnet TerminalQuest.dll</c> during development, the process is the shared
        /// host instead, so the assembly has to be named back to it - otherwise the child would be
        /// a bare <c>dotnet</c> with nothing to run.
        /// </para>
        /// </summary>
        private static (string Command, IReadOnlyList<string> Arguments) ResolveLaunch()
        {
            var processPath = Environment.ProcessPath;

            if (processPath is not { Length: > 0 })
            {
                // Nothing better to offer than the name and a hope that it is on PATH.
                return ("TerminalQuest", []);
            }

            var isSharedHost = Path.GetFileNameWithoutExtension(processPath)
                .Equals("dotnet", StringComparison.OrdinalIgnoreCase);

            if (!isSharedHost)
            {
                return (processPath, []);
            }

            // AppContext.BaseDirectory rather than Assembly.Location: the latter is an IL3000
            // trim warning under PublishAot and returns nothing useful there anyway.
            return (processPath, [Path.Combine(AppContext.BaseDirectory, AssemblyFileName)]);
        }
    }
}
