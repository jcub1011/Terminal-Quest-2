namespace TerminalQuest.Mcp
{
    /// <summary>
    /// What a tool hands back: text for the model, and whether it went wrong.
    /// <para>
    /// A failure is still a successful JSON-RPC result with <see cref="IsError"/> set, because the
    /// point is for the narrator to read "no character named Bess" and correct itself. A
    /// transport-level error would never reach the model as text.
    /// </para>
    /// </summary>
    internal readonly record struct ToolOutcome(string Text, bool IsError)
    {
        public static ToolOutcome Ok(string text) => new(text, IsError: false);

        public static ToolOutcome Fail(string text) => new(text, IsError: true);
    }
}
