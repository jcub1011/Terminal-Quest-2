using Xunit;

namespace TerminalQuest.Tests.Infrastructure
{
    /// <summary>
    /// Points <c>SavePaths.Root</c> at a throwaway folder for the life of one test.
    /// </summary>
    /// <remarks>
    /// <c>TQ_SAVES</c> is the only seam <see cref="TerminalQuest.Saves.SavePaths"/> has — everything
    /// else about it is static — and an environment variable is process-wide. Every test that uses
    /// this must therefore belong to <see cref="EnvironmentCollection"/> so the runner does not
    /// interleave two of them and let one test's root answer another's question.
    /// <para>
    /// The previous value is restored on dispose rather than cleared, so a developer running the
    /// suite with their own <c>TQ_SAVES</c> set does not lose it.
    /// </para>
    /// </remarks>
    internal sealed class SavesRoot : IDisposable
    {
        private const string Variable = "TQ_SAVES";

        private readonly string? _previous;

        public SavesRoot()
        {
            _previous = Environment.GetEnvironmentVariable(Variable);

            Root = Path.Combine(
                Path.GetTempPath(),
                "TerminalQuest.Tests",
                Guid.NewGuid().ToString("N"),
                "Saves");

            Directory.CreateDirectory(Root);
            Environment.SetEnvironmentVariable(Variable, Root);
        }

        /// <summary>The folder <c>SavePaths.Root</c> now resolves to.</summary>
        public string Root { get; }

        /// <summary>Creates a save folder directly, bypassing <c>SavePaths.Open</c>.</summary>
        public string Folder(string name)
        {
            var directory = Path.Combine(Root, name);
            Directory.CreateDirectory(directory);
            return directory;
        }

        /// <summary>The names of the folders actually on disk, sorted for comparison.</summary>
        public string[] Folders =>
            [.. Directory.GetDirectories(Root).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal)];

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(Variable, _previous);

            try
            {
                Directory.Delete(Path.GetDirectoryName(Root)!, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leaked temp folder is not worth failing a test over.
            }
        }
    }

    /// <summary>
    /// Groups every test that writes a process-wide environment variable so they run one at a time.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class EnvironmentCollection
    {
        public const string Name = "Environment";
    }
}
