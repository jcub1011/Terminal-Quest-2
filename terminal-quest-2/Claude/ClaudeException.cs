using System.Text;

namespace TerminalQuest.Claude
{
    /// <summary>
    /// Raised when the underlying <c>claude</c> process fails to start, dies unexpectedly,
    /// or stops responding. Model-level failures are reported through
    /// <see cref="ClaudeTurnResult.IsError"/> instead of this exception.
    /// </summary>
    public sealed class ClaudeException : Exception
    {
        public ClaudeException(string message, string? standardError = null, int? exitCode = null)
            : base(Compose(message, standardError, exitCode))
        {
            StandardError = standardError;
            ExitCode = exitCode;
        }

        /// <summary>Buffered stderr from the process, when any was produced.</summary>
        public string? StandardError { get; }

        /// <summary>Process exit code, when the process had already exited.</summary>
        public int? ExitCode { get; }

        private static string Compose(string message, string? standardError, int? exitCode)
        {
            var builder = new StringBuilder(message);

            if (exitCode is { } code)
            {
                builder.Append(" (exit code ").Append(code).Append(')');
            }

            if (!string.IsNullOrWhiteSpace(standardError))
            {
                builder.AppendLine().Append("stderr: ").Append(standardError.TrimEnd());
            }

            return builder.ToString();
        }
    }
}
