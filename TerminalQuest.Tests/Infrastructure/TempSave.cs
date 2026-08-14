using TerminalQuest.Saves;

namespace TerminalQuest.Tests.Infrastructure
{
    /// <summary>
    /// A throwaway save folder and a <see cref="SaveStore"/> over it.
    /// </summary>
    /// <remarks>
    /// <see cref="SaveStore"/> takes its directory as a constructor argument, so these tests need
    /// no environment variable and run in parallel. Only <see cref="SavePaths"/> needs
    /// <c>TQ_SAVES</c>, which is why that lives in <see cref="SavesRoot"/> instead.
    /// </remarks>
    internal sealed class TempSave : IDisposable
    {
        public TempSave(string? name = null)
        {
            Directory = Path.Combine(
                Path.GetTempPath(),
                "TerminalQuest.Tests",
                Guid.NewGuid().ToString("N"),
                name ?? "Save");

            System.IO.Directory.CreateDirectory(Directory);
            Store = new SaveStore(Directory);
        }

        public string Directory { get; }

        public SaveStore Store { get; }

        /// <summary>The folder this save's temp root sits in, for tests that need a sibling.</summary>
        public string Parent => Path.GetDirectoryName(Directory)!;

        /// <summary>Writes a document verbatim, including text that is not valid JSON.</summary>
        public void WriteRaw(string fileName, string contents) =>
            File.WriteAllText(Path.Combine(Directory, fileName), contents);

        public string ReadRaw(string fileName) =>
            File.ReadAllText(Path.Combine(Directory, fileName));

        /// <summary>
        /// A line-oriented document as its lines, with the trailing blank a well-formed one ends on
        /// removed. What an append-only log has to be inspected as.
        /// </summary>
        public string[] ReadLines(string fileName) =>
            ReadRaw(fileName).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        public bool Has(string fileName) => File.Exists(Path.Combine(Directory, fileName));

        /// <summary>The <c>.tmp</c> files left behind by a write, which should always be none.</summary>
        public string[] TempFiles =>
            System.IO.Directory.GetFiles(Directory, "*.tmp", SearchOption.AllDirectories);

        public void Dispose()
        {
            try
            {
                System.IO.Directory.Delete(Parent, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A leaked temp folder is not worth failing a test over.
            }
        }
    }
}
