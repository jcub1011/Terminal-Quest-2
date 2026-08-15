namespace TerminalQuest.Agents.LmStudio
{
    /// <summary>One entry of the transcript, in the shape the chat-completions API expects.</summary>
    /// <remarks>
    /// This exists because the endpoint is stateless: every request carries the whole conversation,
    /// so the conversation has to be a thing the session owns. Claude Code needs no equivalent - its
    /// process remembers.
    /// </remarks>
    /// <param name="Role">One of <c>system</c>, <c>user</c>, <c>assistant</c> or <c>tool</c>.</param>
    /// <param name="Content">
    /// The text of the message. Empty for an assistant turn that did nothing but call tools.
    /// </param>
    /// <param name="ToolCalls">The tools this assistant turn asked for; null for every other role.</param>
    /// <param name="ToolCallId">
    /// Which call this <c>tool</c> message answers. Required on that role and meaningless on the
    /// rest: it is how the model pairs a result with the request it made.
    /// </param>
    internal sealed record ChatMessage(
        string Role,
        string Content,
        IReadOnlyList<ToolCall>? ToolCalls = null,
        string? ToolCallId = null,
        string? ThoughtSignature = null)
    {
        public static ChatMessage System(string content) => new("system", content);

        public static ChatMessage User(string content) => new("user", content);

        public static ChatMessage Assistant(
            string content,
            IReadOnlyList<ToolCall> toolCalls,
            string? thoughtSignature = null) =>
            new("assistant", content, toolCalls.Count == 0 ? null : toolCalls, ThoughtSignature: thoughtSignature);

        public static ChatMessage Tool(string toolCallId, string content) =>
            new("tool", content, ToolCallId: toolCallId);
    }

    /// <summary>One tool the model asked to run.</summary>
    /// <param name="Arguments">
    /// A JSON object, still as text. It is passed through unparsed because it arrives that way and
    /// has to go back that way in the next request - parsing it is only worth doing once, at the
    /// point of the call.
    /// </param>
    /// <param name="ThoughtSignature">
    /// Gemini thought signature token preserved for function calling.
    /// </param>
    internal sealed record ToolCall(string Id, string Name, string Arguments, string? ThoughtSignature = null);
}
