namespace TerminalQuest.Tests.Infrastructure
{
    /// <summary>
    /// Trait values used across the suite.
    /// </summary>
    /// <remarks>
    /// <see cref="KnownBug"/> marks a test that asserts what the code <em>should</em> do and
    /// therefore fails today. They are deliberate: the suite doubles as an executable bug list,
    /// and a red test is harder to ignore than a comment. Filter them out with
    /// <c>--filter-not-trait Category=KnownBug</c> to see whether anything else is broken.
    /// <para>
    /// Nothing carries it at the moment - the bugs the first round of them described have been
    /// fixed and the tests kept as ordinary regression tests. The convention stands for the next
    /// assertion written ahead of its mechanism.
    /// </para>
    /// </remarks>
    internal static class Categories
    {
        public const string Name = "Category";

        public const string KnownBug = "KnownBug";

        /// <summary>Touches the network or spawns a process; slower and timing-sensitive.</summary>
        public const string Integration = "Integration";

        /// <summary>Mutates process-wide environment variables and cannot run in parallel.</summary>
        public const string Environment = "Environment";
    }
}
