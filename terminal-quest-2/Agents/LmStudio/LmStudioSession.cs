using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;

namespace TerminalQuest.Agents.LmStudio
{
    /// <summary>
    /// A narrator backed by an OpenAI-compatible <c>/chat/completions</c> endpoint - LM Studio by
    /// default, but the shape is common enough that Ollama, llama.cpp, vLLM and Jan all answer it.
    /// </summary>
    /// <remarks>
    /// The endpoint is a completion call, not an agent, and the difference is most of this file.
    /// It is stateless, so the transcript is kept here and resent in full every request. It does
    /// not run anything, so when it asks for a tool the answer has to be fetched and handed back
    /// before it will carry on - which is why one turn is a loop rather than a request.
    /// <para>
    /// Tools are served straight out of <see cref="QuestTools"/> in this process. The MCP server is
    /// not involved at all: it exists because Claude Code runs the tool loop in its own process and
    /// needs a way to reach back into the save, and here there is nothing to reach across.
    /// </para>
    /// <para>
    /// All JSON is handled with <see cref="JsonDocument"/> and <see cref="Utf8JsonWriter"/> rather
    /// than <c>JsonSerializer</c>, so the type stays free of reflection and is safe under
    /// <c>PublishAot</c>.
    /// </para>
    /// </remarks>
    internal sealed class LmStudioSession : IAgentSession
    {
        /// <summary>Cap on an error body quoted back into an exception message.</summary>
        private const int MaxQuotedErrorChars = 2 * 1024;

        private static readonly MediaTypeHeaderValue Json = new("application/json");

        /// <summary>How Claude Code names these same tools, and the one wrong name worth forgiving.</summary>
        private static readonly string McpPrefix = $"mcp__{QuestTools.ServerName}__";

        private readonly LmStudioSessionOptions _options;
        private readonly SaveStore _store;
        private readonly HttpClient _client;
        private readonly SemaphoreSlim _turnGate = new(1, 1);
        private readonly List<ChatMessage> _history = [];

        private CancellationTokenSource? _turn;
        private bool _started;
        private bool _disposed;

        public LmStudioSession(LmStudioSessionOptions options, SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(store);

            _options = options;
            _store = store;

            _client = new HttpClient
            {
                // The turn owns the deadline, not the request: one turn is several requests plus
                // the tool calls between them, and a per-request timeout would cut a slow local
                // model off partway through a scene it was going to finish.
                Timeout = Timeout.InfiniteTimeSpan,
            };

            if (_options.ApiKey is { Length: > 0 } key)
            {
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }

            _history.Add(ChatMessage.System(_options.SystemPrompt));
        }

        /// <inheritdoc />
        public event Action<string>? OnTextDelta;

        /// <summary>
        /// Confirms the server is up and has the configured model.
        /// </summary>
        /// <remarks>
        /// A local model server is off far more often than it is misconfigured, and the failure a
        /// player sees otherwise is a stalled first turn. Asking for the model list costs one round
        /// trip and turns both of the likely mistakes - server not started, model name wrong - into
        /// a sentence they can act on.
        /// </remarks>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_started)
            {
                throw new InvalidOperationException("This session has already been started.");
            }

            var models = await LmStudioModels
                .ListAsync(_options.BaseUrl, _options.ApiKey, _options.StartupTimeout, cancellationToken)
                .ConfigureAwait(false);

            // A model name that is merely absent from the list is still worth refusing here. Sent
            // anyway it comes back as a 404 on the first turn, by which point the player is looking
            // at a blank transcript rather than at the settings screen. An empty list means the
            // server answered in a shape this does not read, which is not evidence of anything.
            if (_options.Model is { Length: > 0 } model
                && models.Count > 0
                && !models.Contains(model, StringComparer.OrdinalIgnoreCase))
            {
                throw new AgentException($"'{model}' is not one of the models {_options.BaseUrl} is offering.");
            }

            _started = true;
        }

        /// <summary>
        /// Runs one turn: send the prompt, then keep answering tool calls until the model narrates.
        /// </summary>
        public async Task<AgentTurnResult> SendAsync(string prompt, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(prompt);
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (!_started)
            {
                throw new InvalidOperationException($"Call {nameof(StartAsync)} before {nameof(SendAsync)}.");
            }

            await _turnGate.WaitAsync(cancellationToken).ConfigureAwait(false);

            var start = Stopwatch.GetTimestamp();

            // Everything this turn appends, so a failure can put the transcript back as it was. A
            // half-written turn is not merely untidy: an assistant message whose tool calls were
            // never answered is rejected outright by the next request.
            var mark = _history.Count;

            using var turn = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            turn.CancelAfter(_options.TurnTimeout);
            _turn = turn;

            try
            {
                _history.Add(ChatMessage.User(prompt));

                var inputTokens = 0;
                var outputTokens = 0;

                for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
                {
                    var reply = await StreamReplyAsync(turn.Token).ConfigureAwait(false);

                    // Input tokens are the whole prompt, so the last request's count is the turn's;
                    // output accumulates across every request the turn made.
                    inputTokens = reply.InputTokens > 0 ? reply.InputTokens : inputTokens;
                    outputTokens += reply.OutputTokens;

                    _history.Add(ChatMessage.Assistant(reply.Text, reply.ToolCalls));

                    if (reply.ToolCalls.Count == 0)
                    {
                        return Finish(reply.Text, isError: false, inputTokens, outputTokens, start);
                    }

                    foreach (var call in reply.ToolCalls)
                    {
                        _history.Add(ChatMessage.Tool(call.Id, Run(call)));
                    }
                }

                return Finish(
                    $"The narrator used tools {_options.MaxToolIterations} times without telling any of it. "
                  + "Try again, or try a larger model.",
                    isError: true,
                    inputTokens,
                    outputTokens,
                    start);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _history.RemoveRange(mark, _history.Count - mark);
                throw new AgentException($"The narrator did not answer within {_options.TurnTimeout}.");
            }
            catch (Exception)
            {
                _history.RemoveRange(mark, _history.Count - mark);
                throw;
            }
            finally
            {
                _turn = null;
                _turnGate.Release();
            }
        }

        /// <summary>Abandons the turn in progress by cancelling the request behind it.</summary>
        public Task InterruptAsync()
        {
            try
            {
                _turn?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The turn finished on its own between the read and the call.
            }

            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;

            _client.Dispose();
            _turnGate.Dispose();

            return ValueTask.CompletedTask;
        }

        private static AgentTurnResult Finish(string text, bool isError, int inputTokens, int outputTokens, long start) =>
            new()
            {
                Text = text,
                IsError = isError,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,

                // CostUsd and the cache counts stay zero: a model running on this machine has
                // neither a price nor a prompt cache to report.
                DurationMs = (int)Stopwatch.GetElapsedTime(start).TotalMilliseconds,
            };

        /// <summary>Runs one tool call and renders the answer as the text the model gets back.</summary>
        /// <remarks>
        /// Nothing here throws. Every way a call can go wrong - unparseable arguments, an unknown
        /// tool, a save that will not write - is worth more to the model as a sentence it can read
        /// and correct than as a failed turn, which is the same bargain <see cref="ToolOutcome"/>
        /// already strikes for ordinary refusals.
        /// </remarks>
        private string Run(ToolCall call)
        {
            // Tools are advertised under their bare names, but a model that has seen the MCP form
            // somewhere will occasionally reach for it. Answering is cheaper than a wasted turn.
            var name = call.Name.StartsWith(McpPrefix, StringComparison.Ordinal)
                ? call.Name[McpPrefix.Length..]
                : call.Name;

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(call.Arguments is { Length: > 0 } ? call.Arguments : "{}");
            }
            catch (JsonException ex)
            {
                return $"Those arguments were not valid JSON ({ex.Message}). Call {name} again with a proper object.";
            }

            using (document)
            {
                var arguments = document.RootElement.ValueKind == JsonValueKind.Object
                    ? document.RootElement
                    : default;

                try
                {
                    return QuestTools.Invoke(_store, name, arguments).Text;
                }
                catch (SaveException ex)
                {
                    return $"That could not be written to the save: {ex.Message}";
                }
            }
        }

        /// <summary>
        /// Sends the transcript and reads the reply off the wire, raising <see cref="OnTextDelta"/>
        /// as prose arrives.
        /// </summary>
        private async Task<Reply> StreamReplyAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint("chat/completions"))
            {
                Content = new ByteArrayContent(BuildRequest()),
            };

            request.Content.Headers.ContentType = Json;

            HttpResponseMessage response;
            try
            {
                response = await _client
                    .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                throw new AgentException($"Lost the connection to {_options.BaseUrl}.", ex.Message);
            }

            using (response)
            {
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

                    throw new AgentException(
                        $"{_options.BaseUrl} rejected the request.", Quote(body), (int)response.StatusCode);
                }

                await using var stream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);

                return await ReadEventsAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>Consumes the server-sent event stream and assembles one reply out of it.</summary>
        private async Task<Reply> ReadEventsAsync(Stream stream, CancellationToken cancellationToken)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var filter = _options.StripThinkTags ? new ThinkTagFilter() : null;
            var text = new StringBuilder();
            var calls = new List<PartialToolCall>();
            var inputTokens = 0;
            var outputTokens = 0;

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                // Blank lines separate events and a leading colon is a keep-alive comment; neither
                // carries a payload.
                if (line.Length == 0 || line[0] == ':' || !line.StartsWith("data:", StringComparison.Ordinal))
                {
                    continue;
                }

                var payload = line.AsMemory(5).Trim();

                if (payload.Length == 0)
                {
                    continue;
                }

                if (payload.Span.SequenceEqual("[DONE]"))
                {
                    break;
                }

                using var document = JsonDocument.Parse(payload);
                var root = document.RootElement;

                if (root.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                // Some servers report a mid-stream failure as an event rather than a status code,
                // by which point the headers have long since said 200.
                if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                {
                    throw new AgentException(
                        $"{_options.BaseUrl} failed partway through the reply.",
                        ReadString(error, "message") ?? Quote(error.ToString()));
                }

                if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
                {
                    inputTokens = ReadInt32(usage, "prompt_tokens");
                    outputTokens = ReadInt32(usage, "completion_tokens");
                }

                if (!root.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var choice in choices.EnumerateArray())
                {
                    if (!choice.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    // Only "content" is ever read. Reasoning arrives as "reasoning_content" or
                    // "reasoning" depending on the server, and neither is story - the same rule
                    // ClaudeSession applies to thinking_delta.
                    if (ReadString(delta, "content") is { Length: > 0 } chunk)
                    {
                        Emit(filter is null ? chunk : filter.Feed(chunk), text);
                    }

                    if (delta.TryGetProperty("tool_calls", out var toolCalls)
                        && toolCalls.ValueKind == JsonValueKind.Array)
                    {
                        Accumulate(calls, toolCalls);
                    }
                }
            }

            if (filter is not null)
            {
                Emit(filter.Flush(), text);
            }

            return new Reply(
                text.ToString(),
                [.. calls.Select(static (call, index) => call.Build(index)).Where(static call => call.Name.Length > 0)],
                inputTokens,
                outputTokens);
        }

        private void Emit(string visible, StringBuilder text)
        {
            if (visible.Length == 0)
            {
                return;
            }

            text.Append(visible);
            OnTextDelta?.Invoke(visible);
        }

        /// <summary>
        /// Folds one <c>tool_calls</c> delta into the calls being assembled.
        /// </summary>
        /// <remarks>
        /// Arguments are streamed as fragments of their JSON text and have to be concatenated in
        /// arrival order, which is what <c>index</c> is for: it identifies which call a fragment
        /// belongs to when a model asks for several at once, and it is the only thing tying the
        /// pieces together.
        /// </remarks>
        private static void Accumulate(List<PartialToolCall> calls, JsonElement deltas)
        {
            foreach (var delta in deltas.EnumerateArray())
            {
                if (delta.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var index = ReadInt32(delta, "index");

                while (calls.Count <= index)
                {
                    calls.Add(new PartialToolCall());
                }

                var call = calls[index];

                if (ReadString(delta, "id") is { Length: > 0 } id)
                {
                    call.Id = id;
                }

                if (!delta.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (ReadString(function, "name") is { Length: > 0 } name)
                {
                    call.Name = name;
                }

                if (ReadString(function, "arguments") is { Length: > 0 } arguments)
                {
                    call.Arguments.Append(arguments);
                }
            }
        }

        private byte[] BuildRequest()
        {
            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();

                if (_options.Model is { Length: > 0 } model)
                {
                    writer.WriteString("model", model);
                }

                writer.WriteBoolean("stream", true);

                // Without this the usage block never arrives on a streamed response, and the status
                // pane has nothing to show.
                writer.WriteStartObject("stream_options");
                writer.WriteBoolean("include_usage", true);
                writer.WriteEndObject();

                if (_options.Temperature is { } temperature)
                {
                    writer.WriteNumber("temperature", temperature);
                }

                if (_options.MaxOutputTokens is { } maxTokens)
                {
                    writer.WriteNumber("max_tokens", maxTokens);
                }

                writer.WriteStartArray("messages");
                foreach (var message in _history)
                {
                    WriteMessage(writer, message);
                }

                writer.WriteEndArray();

                writer.WriteStartArray("tools");
                foreach (var tool in QuestTools.Definitions)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function");

                    writer.WriteStartObject("function");
                    writer.WriteString("name", tool.Name);
                    writer.WriteString("description", tool.Description);

                    // Already compact, already valid - QuestTool checked both when it was built.
                    writer.WritePropertyName("parameters");
                    writer.WriteRawValue(tool.InputSchema);
                    writer.WriteEndObject();

                    writer.WriteEndObject();
                }

                writer.WriteEndArray();

                writer.WriteEndObject();
            }

            return buffer.WrittenSpan.ToArray();
        }

        private static void WriteMessage(Utf8JsonWriter writer, ChatMessage message)
        {
            writer.WriteStartObject();
            writer.WriteString("role", message.Role);
            writer.WriteString("content", message.Content);

            if (message.ToolCallId is { Length: > 0 } toolCallId)
            {
                writer.WriteString("tool_call_id", toolCallId);
            }

            if (message.ToolCalls is not { Count: > 0 } toolCalls)
            {
                writer.WriteEndObject();
                return;
            }

            writer.WriteStartArray("tool_calls");
            foreach (var call in toolCalls)
            {
                writer.WriteStartObject();
                writer.WriteString("id", call.Id);
                writer.WriteString("type", "function");

                writer.WriteStartObject("function");
                writer.WriteString("name", call.Name);
                writer.WriteString("arguments", call.Arguments);
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private string Endpoint(string path) => $"{_options.BaseUrl.TrimEnd('/')}/{path}";

        private static string? Quote(string? body) =>
            body is null || body.Length <= MaxQuotedErrorChars ? body : body[..MaxQuotedErrorChars];

        private static string? ReadString(JsonElement owner, string propertyName) =>
            owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static int ReadInt32(JsonElement owner, string propertyName) =>
            owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetInt32(out var number)
                ? number
                : 0;

        /// <summary>What one request to the endpoint produced.</summary>
        private readonly record struct Reply(
            string Text,
            IReadOnlyList<ToolCall> ToolCalls,
            int InputTokens,
            int OutputTokens);

        /// <summary>A tool call still being assembled out of stream fragments.</summary>
        private sealed class PartialToolCall
        {
            public string Id { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;

            public StringBuilder Arguments { get; } = new();

            /// <summary>
            /// Freezes the call. The position stands in for an id the server never sent: the id is
            /// only ever used to pair the result back to the request, and a server that omits it
            /// has nothing else to pair on either.
            /// </summary>
            public ToolCall Build(int index) =>
                new(Id.Length > 0 ? Id : $"call_{index}", Name, Arguments.ToString());
        }
    }
}
