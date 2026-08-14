using System.Text;

namespace TerminalQuest.Agents
{
    /// <summary>
    /// Raised when the provider itself fails: a process that will not start or has died, a server
    /// that cannot be reached, a model that does not exist. Failures the model reports about its
    /// own turn come back through <see cref="AgentTurnResult.IsError"/> instead.
    /// </summary>
    /// <remarks>
    /// The distinction is what the host does about it. This exception means no turn will ever
    /// succeed until something outside the game changes, and <c>Program</c> reports it once and
    /// leaves the player with their slash commands.
    /// </remarks>
    internal sealed class AgentException : Exception
    {
        /// <param name="detail">
        /// Whatever the transport had to say for itself: buffered stderr from a child process, or
        /// a response body from an HTTP endpoint.
        /// </param>
        /// <param name="code">A process exit code, or an HTTP status code.</param>
        public AgentException(string message, string? detail = null, int? code = null)
            : base(Compose(message, detail, code))
        {
            Detail = detail;
            Code = code;
        }

        /// <summary>Transport diagnostics, when there were any.</summary>
        public string? Detail { get; }

        /// <summary>Process exit code or HTTP status code, when one applies.</summary>
        public int? Code { get; }

        private static string Compose(string message, string? detail, int? code)
        {
            var builder = new StringBuilder(message);

            if (code is { } value)
            {
                builder.Append(" (code ").Append(value).Append(')');
            }

            if (!string.IsNullOrWhiteSpace(detail))
            {
                builder.AppendLine().Append("detail: ").Append(detail.TrimEnd());
            }

            return builder.ToString();
        }
    }
}
