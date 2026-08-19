using System.Buffers;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Settings;

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

        /// <summary>
        /// The most tool calls one message may ask for.
        /// </summary>
        /// <remarks>
        /// A bound rather than a rule: <c>index</c> arrives from the server and is used to size a
        /// list, so a single delta claiming index 20,000,000 would allocate twenty million entries
        /// before anything else got a say. A model asking for more than this many at once is
        /// malfunctioning, and the deltas past the cap are dropped rather than trusted.
        /// </remarks>
        private const int MaxToolCalls = 64;

        private static readonly MediaTypeHeaderValue Json = new("application/json");

        /// <summary>
        /// What the journal says about a call that was answered from earlier in the same turn rather
        /// than run a second time.
        /// </summary>
        private const string DuplicateSuppressed = "Duplicate call within the turn; answered from the first.";

        /// <summary>How Claude Code names these same tools, and the one wrong name worth forgiving.</summary>
        private static readonly string McpPrefix = $"mcp__{QuestTools.ServerName}__";

        private readonly LmStudioSessionOptions _options;
        private readonly SaveStore _store;
        private readonly HttpClient _client;
        private readonly HttpMessageHandler? _handler;
        private readonly SemaphoreSlim _turnGate = new(1, 1);
        private readonly List<ChatMessage> _history = [];

        private CancellationTokenSource? _turn;
        private bool _started;
        private bool _disposed;

        /// <summary>
        /// Context length of the served model, or zero where the server would not say. Read once at
        /// startup: the model cannot change under a running session, because the session is the
        /// history it has been building against that model.
        /// </summary>
        private int _contextWindowTokens;

        /// <param name="handler">
        /// Where the requests go. Null means a real socket, which is what the game always passes.
        /// It exists so the streaming reply can be driven from canned bytes. A supplied handler
        /// stays the caller's to dispose - this session also hands it to
        /// <see cref="LmStudioModels.ListAsync"/>, so it must survive the client that used it.
        /// </param>
        public LmStudioSession(
            LmStudioSessionOptions options,
            SaveStore store,
            HttpMessageHandler? handler = null)
        {
            ArgumentNullException.ThrowIfNull(options);
            ArgumentNullException.ThrowIfNull(store);

            _options = options with { BaseUrl = AppSettings.NormalizeBaseUrl(options.BaseUrl) };
            _store = store;

            _handler = handler;

            _client = new HttpClient(handler ?? new HttpClientHandler(), disposeHandler: handler is null)
            {
                // The turn owns the deadline, not the request: one turn is several requests plus
                // the tool calls between them, and a per-request timeout would cut a slow local
                // model off partway through a scene it was going to finish.
                Timeout = Timeout.InfiniteTimeSpan,
            };

            if (_options.ApiKey is { Length: > 0 } key)
            {
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", key);
                _client.DefaultRequestHeaders.TryAddWithoutValidation("x-goog-api-key", key);
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
                .ListAsync(
                    _options.BaseUrl,
                    _options.ApiKey,
                    _options.StartupTimeout,
                    cancellationToken,
                    _handler)
                .ConfigureAwait(false);

            // A model name that is merely absent from the list is still worth refusing here. Sent
            // anyway it comes back as a 404 on the first turn, by which point the player is looking
            // at a blank transcript rather than at the settings screen. An empty list means the
            // server answered in a shape this does not read, which is not evidence of anything.
            if (_options.Model is { Length: > 0 } model
                && models.Count > 0
                && !models.Contains(model, StringComparer.OrdinalIgnoreCase)
                && !models.Contains(model.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? model["models/".Length..] : $"models/{model}", StringComparer.OrdinalIgnoreCase))
            {
                throw new AgentException($"'{model}' is not one of the models {_options.BaseUrl} is offering.");
            }

            // After the check above rather than beside it, and unable to fail the start: this only
            // feeds the status pane's context gauge, and a server that does not offer LM Studio's
            // native endpoint is a server the game is otherwise perfectly happy to narrate with.
            _contextWindowTokens = await LmStudioModels
                .ContextLengthAsync(
                    _options.BaseUrl,
                    _options.ApiKey,
                    _options.Model,
                    _options.StartupTimeout,
                    cancellationToken,
                    _handler)
                .ConfigureAwait(false) ?? 0;

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

                // Per turn, not per session: the same question asked next turn is a fair question,
                // because the world has moved since.
                var answered = new Dictionary<string, string>(StringComparer.Ordinal);

                var inputTokens = 0;
                var outputTokens = 0;

                // Kept apart from outputTokens because the two answer different questions. That one
                // is billing and totals the turn; this one is occupancy, and only the reply still in
                // the context counts - the earlier ones are already inside inputTokens.
                var lastOutputTokens = 0;

                string? lastAssistantTextWithTools = null;

                for (var iteration = 0; iteration < _options.MaxToolIterations; iteration++)
                {
                    var reply = await StreamReplyAsync(
                        turn.Token,
                        allowEmpty: !string.IsNullOrWhiteSpace(lastAssistantTextWithTools)).ConfigureAwait(false);

                    // Input tokens are the whole prompt, so the last request's count is the turn's;
                    // output accumulates across every request the turn made.
                    inputTokens = reply.InputTokens > 0 ? reply.InputTokens : inputTokens;
                    outputTokens += reply.OutputTokens;
                    lastOutputTokens = reply.OutputTokens;

                    _history.Add(ChatMessage.Assistant(reply.Text, reply.ToolCalls, reply.ThoughtSignature));

                    if (reply.ToolCalls.Count == 0)
                    {
                        var finalText = reply.Text;
                        if (string.IsNullOrWhiteSpace(finalText) && !string.IsNullOrWhiteSpace(lastAssistantTextWithTools))
                        {
                            finalText = lastAssistantTextWithTools;
                        }

                        if (!string.IsNullOrEmpty(finalText))
                        {
                            await StreamPacedAsync(finalText, turn.Token).ConfigureAwait(false);
                        }

                        return Finish(finalText, isError: false, inputTokens, outputTokens, lastOutputTokens, start);
                    }

                    if (!string.IsNullOrWhiteSpace(reply.Text))
                    {
                        lastAssistantTextWithTools = reply.Text;
                    }

                    foreach (var call in reply.ToolCalls)
                    {
                        _history.Add(ChatMessage.Tool(call.Id, Run(call, answered)));
                    }
                }

                return Finish(
                    $"The narrator used tools {_options.MaxToolIterations} times without telling any of it. "
                  + "Try again, or try a larger model.",
                    isError: true,
                    inputTokens,
                    outputTokens,
                    lastOutputTokens,
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

        private AgentTurnResult Finish(
            string text,
            bool isError,
            int inputTokens,
            int outputTokens,
            int lastOutputTokens,
            long start) =>
            new()
            {
                Text = text,
                IsError = isError,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,

                // The prompt of the last request plus the answer to it. Every earlier round trip of
                // the turn is already counted inside that prompt, because this provider resends the
                // whole history on each one.
                ContextTokens = inputTokens > 0 ? inputTokens + lastOutputTokens : 0,
                ContextWindowTokens = _contextWindowTokens,

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
        /// <param name="call">What the model asked for.</param>
        /// <param name="answered">
        /// What this turn has already been asked and told, so a call made twice is answered from here
        /// rather than run again. See <see cref="Repeatable"/>.
        /// </param>
        private string Run(ToolCall call, Dictionary<string, string> answered)
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

                // Keyed on the re-emitted arguments rather than the raw string, so the same call
                // formatted two ways is still the same call.
                var callKey = $"{name} {Canonical(document.RootElement)}";

                if (answered.TryGetValue(callKey, out var previous))
                {
                    // Journalled anyway, and as a failure, so the file still shows the loop happening -
                    // the journal answers "what did the narrator do", and doing this twice is a thing
                    // it did. Failed rather than not, so a suppressed record_claims cannot stand in for
                    // the real one in Program.ClaimsMissing, which counts only calls that succeeded.
                    QuestJournal.Record(_store, name, arguments, failed: true, error: DuplicateSuppressed);

                    return $"You already called {name} with exactly these arguments this turn, and it "
                         + $"answered:{Environment.NewLine}{previous}{Environment.NewLine}That is still "
                         + "true and calling again will not change it. Act on it, or move on.";
                }

                ToolOutcome outcome;
                try
                {
                    outcome = QuestTools.Invoke(_store, name, arguments);
                }
                catch (SaveException ex)
                {
                    outcome = ToolOutcome.Fail($"That could not be written to the save: {ex.Message}");
                }

                // Refusals are remembered for all tools (including roll): a call the world turned down
                // will fail identically with unchanged arguments. Successful calls are remembered for
                // non-repeatable tools to prevent re-reading or re-writing identical state.
                if (outcome.IsError || !Repeatable(name))
                {
                    answered[callKey] = outcome.Text;
                }

                return outcome.Text;
            }
        }

        /// <summary>
        /// Whether asking this tool the same thing twice is supposed to give a different answer.
        /// </summary>
        /// <remarks>
        /// The word draws and the dice are the whole of it. Everything else is a read of the save or a
        /// write to it, and repeating one inside a single turn is a model that has stopped reading its
        /// replies rather than a model that wants a second opinion. Suppressing these three instead
        /// would break them: two rolls for two blows are not one roll.
        /// </remarks>
        private static bool Repeatable(string tool) =>
            tool is "roll" or "random_noun" or "random_adjective";

        /// <summary>Re-emits arguments without insignificant whitespace, so they compare as text.</summary>
        private static string Canonical(JsonElement arguments)
        {
            var buffer = new ArrayBufferWriter<byte>();
            using (var writer = new Utf8JsonWriter(buffer))
            {
                arguments.WriteTo(writer);
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        /// <summary>
        /// Sends the transcript and reads the reply off the wire.
        /// </summary>
        private async Task<Reply> StreamReplyAsync(
            CancellationToken cancellationToken,
            bool allowEmpty = false)
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

                return await ReadEventsAsync(stream, cancellationToken, allowEmpty).ConfigureAwait(false);
            }
        }

        /// <summary>Consumes the server-sent event stream and assembles one reply out of it.</summary>
        private async Task<Reply> ReadEventsAsync(
            Stream stream,
            CancellationToken cancellationToken,
            bool allowEmpty = false)
        {
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var filter = _options.StripThinkTags ? new ThinkTagFilter() : null;
            var text = new StringBuilder();
            var nonSseContent = new StringBuilder();
            var calls = new List<PartialToolCall>();
            var inputTokens = 0;
            var outputTokens = 0;
            string? thoughtSignature = null;

            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                // Blank lines separate events and a leading colon is a keep-alive comment; neither
                // carries a payload.
                if (line.Length == 0 || line[0] == ':')
                {
                    continue;
                }

                if (!line.StartsWith("data:", StringComparison.Ordinal))
                {
                    if (nonSseContent.Length < MaxQuotedErrorChars)
                    {
                        nonSseContent.AppendLine(line);
                    }
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
                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("error", out var error)
                    && error.ValueKind != JsonValueKind.Null)
                {
                    throw new AgentException(
                        $"{_options.BaseUrl} failed partway through the reply.",
                        ReadString(error, "message") ?? Quote(error.ToString()));
                }

                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("usage", out var usage)
                    && usage.ValueKind == JsonValueKind.Object)
                {
                    inputTokens = ReadInt32(usage, "prompt_tokens");
                    outputTokens = ReadInt32(usage, "completion_tokens");
                }

                if (root.ValueKind != JsonValueKind.Object
                    || !root.TryGetProperty("choices", out var choices)
                    || choices.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var choice in choices.EnumerateArray())
                {
                    if (choice.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

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

                    if (ReadString(delta, "thought_signature") is { Length: > 0 } deltaSig)
                    {
                        thoughtSignature = deltaSig;
                    }
                    else if (delta.TryGetProperty("extra_content", out var ec)
                        && ec.ValueKind == JsonValueKind.Object
                        && ec.TryGetProperty("google", out var google)
                        && google.ValueKind == JsonValueKind.Object
                        && ReadString(google, "thought_signature") is { Length: > 0 } gsig)
                    {
                        thoughtSignature = gsig;
                    }
                }
            }

            if (filter is not null)
            {
                Emit(filter.Flush(), text);
            }

            if (thoughtSignature is { Length: > 0 })
            {
                foreach (var call in calls)
                {
                    call.ThoughtSignature ??= thoughtSignature;
                }
            }

            var builtCalls = calls
                .Select(static (call, index) => call.Build(index))
                .Where(static call => call.Name.Length > 0)
                .ToList();

            if (text.Length == 0 && builtCalls.Count == 0)
            {
                var nonSse = nonSseContent.ToString().Trim();
                if (nonSse.Length > 0)
                {
                    throw new AgentException(
                        $"{_options.BaseUrl} returned an unexpected response.",
                        Quote(nonSse));
                }

                if (!allowEmpty)
                {
                    throw new AgentException(
                        $"{_options.BaseUrl} returned an empty response with no narration or tool calls.");
                }
            }

            return new Reply(
                text.ToString(),
                builtCalls,
                inputTokens,
                outputTokens,
                thoughtSignature);
        }

        private static void Emit(string visible, StringBuilder text)
        {
            if (visible.Length == 0)
            {
                return;
            }

            text.Append(visible);
        }

        private async Task StreamPacedAsync(string text, CancellationToken cancellationToken)
        {
            var handler = OnTextDelta;
            if (handler is null || text.Length == 0)
            {
                return;
            }

            if (_options.StreamPacing <= TimeSpan.Zero)
            {
                handler.Invoke(text);
                return;
            }

            var chunks = SliceWords(text);
            if (chunks.Count <= 1)
            {
                handler.Invoke(text);
                return;
            }

            var delayMs = (int)_options.StreamPacing.TotalMilliseconds;
            if (chunks.Count * delayMs > 2500)
            {
                delayMs = Math.Max(3, 2500 / chunks.Count);
            }

            foreach (var chunk in chunks)
            {
                handler.Invoke(chunk);
                try
                {
                    await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private static List<string> SliceWords(string text)
        {
            var list = new List<string>();
            var span = text.AsSpan();
            var i = 0;

            while (i < span.Length)
            {
                var start = i;

                while (i < span.Length && char.IsWhiteSpace(span[i]))
                {
                    i++;
                }

                while (i < span.Length && !char.IsWhiteSpace(span[i]))
                {
                    i++;
                }

                if (i > start)
                {
                    list.Add(text.Substring(start, i - start));
                }
            }

            return list;
        }

        /// <summary>
        /// Folds one <c>tool_calls</c> delta into the calls being assembled.
        /// </summary>
        /// <remarks>
        /// Arguments are streamed as fragments of their JSON text and have to be concatenated in
        /// arrival order, which is what <c>index</c> is for: it identifies which call a fragment
        /// belongs to when a model asks for several at once, and it is the only thing tying the
        /// pieces together.
        /// <para>
        /// It is also a number off the wire used to index a list, so it is checked against
        /// <see cref="MaxToolCalls"/> before it gets to. A negative one would throw past the loop
        /// below, which cannot grow a list to a negative size, and an absurd one would grow it to
        /// whatever was asked for. Either takes the turn down with something the caller is not
        /// prepared for, so a delta naming an index outside the range is skipped the same way one
        /// that is not an object is.
        /// </para>
        /// </remarks>
        internal static void Accumulate(List<PartialToolCall> calls, JsonElement deltas)
        {
            foreach (var delta in deltas.EnumerateArray())
            {
                if (delta.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var index = ReadInt32(delta, "index");

                if (index is < 0 or >= MaxToolCalls)
                {
                    continue;
                }

                while (calls.Count <= index)
                {
                    calls.Add(new PartialToolCall());
                }

                var call = calls[index];

                if (ReadString(delta, "id") is { Length: > 0 } id)
                {
                    call.Id = id;
                }

                if (ReadString(delta, "thought_signature") is { Length: > 0 } sig)
                {
                    call.ThoughtSignature = sig;
                }
                else if (delta.TryGetProperty("extra_content", out var ec)
                    && ec.ValueKind == JsonValueKind.Object
                    && ec.TryGetProperty("google", out var google)
                    && google.ValueKind == JsonValueKind.Object
                    && ReadString(google, "thought_signature") is { Length: > 0 } gsig)
                {
                    call.ThoughtSignature = gsig;
                }

                if (!delta.TryGetProperty("function", out var function) || function.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (ReadString(function, "name") is { Length: > 0 } name)
                {
                    call.Name = name;
                }

                if (ReadString(function, "thought_signature") is { Length: > 0 } fnSig)
                {
                    call.ThoughtSignature = fnSig;
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

                // Google's OpenAI-compatible endpoint does not support stream_options and rejects
                // requests containing unrecognized fields with a 400 Bad Request.
                if (!_options.BaseUrl.Contains("googleapis.com", StringComparison.OrdinalIgnoreCase))
                {
                    writer.WriteStartObject("stream_options");
                    writer.WriteBoolean("include_usage", true);
                    writer.WriteEndObject();
                }

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
                foreach (var tool in QuestTools.Definitions.Where(t => (t.Role & _options.Role) != 0))
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

            if (!string.IsNullOrEmpty(message.Content))
            {
                writer.WriteString("content", message.Content);
            }
            else if (message.ToolCalls is not { Count: > 0 })
            {
                writer.WriteString("content", string.Empty);
            }
            else
            {
                writer.WriteNull("content");
            }

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

                var sig = call.ThoughtSignature ?? message.ThoughtSignature;
                if (sig is { Length: > 0 })
                {
                    writer.WriteStartObject("extra_content");
                    writer.WriteStartObject("google");
                    writer.WriteString("thought_signature", sig);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        private string Endpoint(string path) => $"{_options.BaseUrl.TrimEnd('/')}/{path}";

        private static string? Quote(string? body)
        {
            if (body is null)
            {
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.ValueKind == JsonValueKind.Object)
                {
                    if (doc.RootElement.TryGetProperty("error", out var errorObj))
                    {
                        if (errorObj.ValueKind == JsonValueKind.String)
                        {
                            var str = errorObj.GetString();
                            if (!string.IsNullOrEmpty(str))
                            {
                                return str.Length <= MaxQuotedErrorChars ? str : str[..MaxQuotedErrorChars];
                            }
                        }
                        else if (errorObj.ValueKind == JsonValueKind.Object)
                        {
                            var msg = ReadString(errorObj, "message");
                            if (errorObj.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
                            {
                                var parts = new List<string>();
                                if (!string.IsNullOrEmpty(msg))
                                {
                                    parts.Add(msg);
                                }

                                foreach (var item in details.EnumerateArray())
                                {
                                    if (item.ValueKind != JsonValueKind.Object)
                                    {
                                        continue;
                                    }

                                    if (ReadString(item, "description") is { Length: > 0 } desc)
                                    {
                                        parts.Add(desc);
                                    }
                                    else if (ReadString(item, "reason") is { Length: > 0 } reason)
                                    {
                                        parts.Add(reason);
                                    }
                                    else if (item.TryGetProperty("fieldViolations", out var fvs) && fvs.ValueKind == JsonValueKind.Array)
                                    {
                                        foreach (var fv in fvs.EnumerateArray())
                                        {
                                            if (fv.ValueKind != JsonValueKind.Object)
                                            {
                                                continue;
                                            }

                                            var field = ReadString(fv, "field");
                                            var fvDesc = ReadString(fv, "description");
                                            parts.Add($"{field}: {fvDesc}");
                                        }
                                    }
                                }

                                if (parts.Count > 0)
                                {
                                    var full = string.Join(" - ", parts);
                                    return full.Length <= MaxQuotedErrorChars ? full : full[..MaxQuotedErrorChars];
                                }
                            }

                            if (!string.IsNullOrEmpty(msg))
                            {
                                return msg;
                            }
                        }
                    }

                    if (ReadString(doc.RootElement, "message") is { Length: > 0 } directMsg)
                    {
                        return directMsg;
                    }
                }
            }
            catch (Exception)
            {
            }

            return body.Length <= MaxQuotedErrorChars ? body : body[..MaxQuotedErrorChars];
        }

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
            int OutputTokens,
            string? ThoughtSignature = null);

        /// <summary>A tool call still being assembled out of stream fragments.</summary>
        internal sealed class PartialToolCall
        {
            public string Id { get; set; } = string.Empty;

            public string Name { get; set; } = string.Empty;

            public string? ThoughtSignature { get; set; }

            public StringBuilder Arguments { get; } = new();

            /// <summary>
            /// Freezes the call. The position stands in for an id the server never sent: the id is
            /// only ever used to pair the result back to the request, and a server that omits it
            /// has nothing else to pair on either.
            /// </summary>
            public ToolCall Build(int index) =>
                new(Id.Length > 0 ? Id : $"call_{index}", Name, Arguments.ToString(), ThoughtSignature);
        }
    }
}
