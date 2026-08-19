using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using TerminalQuest.Agents.LmStudio;

namespace TerminalQuest.Agents.Claude
{
    /// <summary>
    /// A long-lived <c>claude</c> process driven over newline-delimited JSON.
    /// </summary>
    /// <remarks>
    /// One process is held open for the whole conversation, so context and the prompt cache both
    /// survive across turns — the second and later turns read their prefix from cache instead of
    /// rebuilding it. The session sees only what <see cref="ClaudeSessionOptions"/> declares: no
    /// skills, no plugins, and no MCP servers beyond the one the caller supplies.
    /// <para>
    /// All JSON is handled with <see cref="JsonDocument"/> and <see cref="Utf8JsonWriter"/> rather
    /// than <c>JsonSerializer</c>, so the type stays free of reflection and is safe under
    /// <c>PublishAot</c>.
    /// </para>
    /// </remarks>
    internal sealed class ClaudeSession : IAgentSession
    {
        private const int MaxBufferedStandardErrorChars = 16 * 1024;

        /// <summary>
        /// The window every model this game offers is assumed to hold, reported as the denominator of
        /// the status pane's context gauge.
        /// </summary>
        /// <remarks>
        /// An assumption, and worth naming as one. The CLI is free to serve a session less than the
        /// model's ceiling, and it does not say which it gave — nothing in the protocol carries the
        /// number, so there is nothing to read instead of guessing. Where the real window is smaller
        /// the gauge reads roomier than the session is.
        /// </remarks>
        private const int ClaudeContextTokens = 1_000_000;

        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly ClaudeSessionOptions _options;
        private readonly SemaphoreSlim _turnGate = new(1, 1);
        private readonly StringBuilder _standardError = new();
        private readonly Lock _standardErrorLock = new();
        private readonly ThinkTagFilter _thinkTagFilter = new();
        private readonly Lock _filterLock = new();

        private Process? _process;
        private Task? _stdoutReader;
        private Task? _stderrReader;
        private TaskCompletionSource<AgentTurnResult>? _pendingTurn;
        private bool _disposed;

        // Written by the stdout reader, read when a result is assembled - hence Volatile at both
        // ends. Neither is ever reset: see TrackPromptSize for why the last value seen is the one
        // that is true.
        private int _contextPromptTokens;
        private int _contextOutputTokens;

        public ClaudeSession(ClaudeSessionOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            _options = options;
        }

        /// <summary>
        /// Raised for each chunk of response text as it is generated. Reasoning output is filtered
        /// out, so only text the player should see is forwarded. Invoked on a background thread.
        /// </summary>
        public event Action<string>? OnTextDelta;

        /// <summary>
        /// Session id reported by Claude Code. Null until the first turn completes: the process
        /// stays silent until it receives its first message.
        /// </summary>
        public string? SessionId { get; private set; }

        /// <summary>
        /// Protocol capabilities advertised by this Claude Code build. Empty until the first turn
        /// completes, for the same reason as <see cref="SessionId"/>.
        /// </summary>
        public IReadOnlyList<string> Capabilities { get; private set; } = [];

        /// <summary>Whether this build understands the interrupt control request.</summary>
        public bool SupportsInterrupt => Capabilities.Contains("interrupt_receipt_v1");

        /// <summary>
        /// Launches the process and confirms it does not fail immediately.
        /// </summary>
        /// <remarks>
        /// Claude Code emits nothing — not even <c>system/init</c> — until the first user message
        /// reaches its stdin, so there is no readiness handshake to wait for here. This only guards
        /// against a process that dies on startup, for example because a flag was rejected.
        /// </remarks>
        public async Task StartAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_process is not null)
            {
                throw new InvalidOperationException("This session has already been started.");
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = _options.ExecutablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = Utf8NoBom,
                StandardOutputEncoding = Utf8NoBom,
                StandardErrorEncoding = Utf8NoBom,
            };

            if (_options.WorkingDirectory is { Length: > 0 } workingDirectory)
            {
                startInfo.WorkingDirectory = workingDirectory;
            }

            // ArgumentList quotes each element itself, so the embedded MCP JSON — braces, quotes,
            // an executable path with spaces in it — survives without hand-rolled escaping.
            foreach (var argument in BuildArguments(_options))
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process process;
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new AgentException($"Could not start '{_options.ExecutablePath}'.");
            }
            catch (Exception ex) when (ex is not AgentException)
            {
                throw new AgentException($"Could not start '{_options.ExecutablePath}'. Is it on PATH?", ex.Message);
            }

            _process = process;
            process.StandardInput.AutoFlush = false;

            _stdoutReader = Task.Run(() => ReadStandardOutputAsync(process), CancellationToken.None);
            _stderrReader = Task.Run(() => ReadStandardErrorAsync(process), CancellationToken.None);

            // Nothing to wait for on the happy path, so watch briefly for an early exit instead.
            using var graceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var exited = process.WaitForExitAsync(graceCts.Token);
            var grace = Task.Delay(_options.StartupGracePeriod, graceCts.Token);

            await Task.WhenAny(exited, grace).ConfigureAwait(false);
            await graceCts.CancelAsync().ConfigureAwait(false);
            await SwallowAsync(exited).ConfigureAwait(false);
            await SwallowAsync(grace).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();

            if (process.HasExited)
            {
                // Let stderr drain so the exception carries the real reason.
                await SwallowAsync(_stderrReader).ConfigureAwait(false);

                throw new AgentException(
                    $"'{_options.ExecutablePath}' exited immediately after starting.",
                    SnapshotStandardError(),
                    process.ExitCode);
            }
        }

        /// <summary>
        /// Sends one message and waits for the complete response. Turns are serialized: a second
        /// call waits for the first to finish.
        /// </summary>
        public async Task<AgentTurnResult> SendAsync(string prompt, CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrEmpty(prompt);
            ObjectDisposedException.ThrowIf(_disposed, this);

            var process = _process
                ?? throw new InvalidOperationException($"Call {nameof(StartAsync)} before {nameof(SendAsync)}.");

            await _turnGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (process.HasExited)
                {
                    throw new AgentException(
                        "The Claude process has exited.", SnapshotStandardError(), process.ExitCode);
                }

                var turn = new TaskCompletionSource<AgentTurnResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingTurn = turn;

                lock (_filterLock)
                {
                    _thinkTagFilter.Flush();
                }

                await WriteLineAsync(process, BuildUserMessage(prompt), cancellationToken).ConfigureAwait(false);

                using var turnCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                turnCts.CancelAfter(_options.TurnTimeout);

                try
                {
                    return await turn.Task.WaitAsync(turnCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    Interlocked.CompareExchange(ref _pendingTurn, null, turn);
                    await TryInterruptAsync().ConfigureAwait(false);

                    if (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }

                    throw new AgentException(
                        $"Claude did not return a result within {_options.TurnTimeout}.",
                        SnapshotStandardError());
                }
            }
            finally
            {
                _turnGate.Release();
            }
        }

        /// <summary>
        /// Asks Claude to abandon the turn in progress. A no-op when the running build does not
        /// advertise interrupt support.
        /// </summary>
        public Task InterruptAsync() => TryInterruptAsync();

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            var process = _process;
            if (process is not null)
            {
                // Closing stdin is how Claude Code is told the conversation is over.
                try
                {
                    process.StandardInput.Close();
                }
                catch (Exception)
                {
                    // Already gone; the kill below is the backstop.
                }

                try
                {
                    using var exitCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await process.WaitForExitAsync(exitCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    try
                    {
                        process.Kill(entireProcessTree: true);
                    }
                    catch (Exception)
                    {
                        // Nothing left to do.
                    }
                }
                catch (Exception)
                {
                    // Nothing left to do.
                }
            }

            FaultOutstanding(new ObjectDisposedException(nameof(ClaudeSession)));

            await SwallowAsync(_stdoutReader).ConfigureAwait(false);
            await SwallowAsync(_stderrReader).ConfigureAwait(false);

            process?.Dispose();
            _turnGate.Dispose();
        }

        internal static IEnumerable<string> BuildArguments(ClaudeSessionOptions options)
        {
            yield return "-p";

            yield return "--input-format";
            yield return "stream-json";
            yield return "--output-format";
            yield return "stream-json";
            yield return "--verbose";
            yield return "--include-partial-messages";

            // Strip the session down to exactly what the caller asked for. Naming the tools is not
            // sufficient on its own: without the rest of these the process still loads the user's
            // own MCP servers, skills and plugins, which costs tens of thousands of prompt tokens
            // per session. --strict-mcp-config in particular is what keeps --mcp-config the whole
            // truth rather than an addition to whatever the user has configured.
            yield return "--tools";
            yield return options.AllowedTools;

            // --tools decides which tools exist; --allowed-tools decides which may run without
            // being asked about. Both are needed: under any permission mode a tool that is only
            // named in the first is offered to the model and then refused when it calls, which it
            // reports to the player as the game being broken.
            if (options.AllowedTools is { Length: > 0 })
            {
                yield return "--allowed-tools";
                yield return options.AllowedTools;
            }

            yield return "--strict-mcp-config";
            yield return "--mcp-config";
            yield return options.McpConfigJson;
            yield return "--disable-slash-commands";
            yield return "--setting-sources";
            yield return string.Empty;
            yield return "--permission-mode";
            yield return "dontAsk";

            yield return "--system-prompt";
            yield return options.SystemPrompt;

            if (options.Model is { Length: > 0 } model)
            {
                yield return "--model";
                yield return model;
            }

            if (!options.PersistSession)
            {
                yield return "--no-session-persistence";
            }
        }

        private async Task ReadStandardOutputAsync(Process process)
        {
            try
            {
                while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    if (line.Length == 0)
                    {
                        continue;
                    }

                    try
                    {
                        HandleLine(line);
                    }
                    catch (JsonException)
                    {
                        // Not a protocol line; ignore rather than tearing down the session.
                    }
                }
            }
            catch (Exception ex)
            {
                FaultOutstanding(new AgentException("Lost the connection to the Claude process.", ex.Message));
                return;
            }

            FaultOutstanding(new AgentException(
                "The Claude process ended before returning a result.",
                SnapshotStandardError(),
                process.HasExited ? process.ExitCode : null));
        }

        private async Task ReadStandardErrorAsync(Process process)
        {
            try
            {
                while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    lock (_standardErrorLock)
                    {
                        if (_standardError.Length < MaxBufferedStandardErrorChars)
                        {
                            _standardError.AppendLine(line);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // stderr is diagnostic only; never let it fail the session.
            }
        }

        private void HandleLine(string line)
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            switch (ReadString(root, "type"))
            {
                case "system":
                    HandleSystem(root);
                    break;
                case "stream_event":
                    HandleStreamEvent(root);
                    break;
                case "result":
                    HandleResult(root);
                    break;
            }
        }

        private void HandleSystem(JsonElement root)
        {
            if (ReadString(root, "subtype") != "init")
            {
                return;
            }

            // init can be emitted more than once in a session; the first one wins.
            SessionId ??= ReadString(root, "session_id");

            if (Capabilities.Count == 0
                && root.TryGetProperty("capabilities", out var capabilities)
                && capabilities.ValueKind == JsonValueKind.Array)
            {
                var parsed = new List<string>(capabilities.GetArrayLength());
                foreach (var capability in capabilities.EnumerateArray())
                {
                    if (capability.ValueKind == JsonValueKind.String && capability.GetString() is { } value)
                    {
                        parsed.Add(value);
                    }
                }

                Capabilities = parsed;
            }
        }

        private void HandleStreamEvent(JsonElement root)
        {
            if (!root.TryGetProperty("event", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // Dispatched before any check on OnTextDelta, deliberately: the usage frames are read for
            // the status pane, not for the caller, and a session nobody is listening to for prose
            // still fills its context exactly as fast.
            switch (ReadString(payload, "type"))
            {
                case "message_start":
                    TrackPromptSize(payload);
                    break;
                case "message_delta":
                    TrackOutputSize(payload);
                    break;
                case "content_block_delta":
                    ForwardTextDelta(payload);
                    break;
            }
        }

        /// <summary>
        /// Records how large one request's prompt was, from the usage on the frame that opens it.
        /// </summary>
        /// <remarks>
        /// Overwritten rather than added to, and never cleared. Within a turn the conversation only
        /// grows, so the last request's prompt is both the largest and the one still occupying the
        /// window; across turns the figure carries, which is what lets the pane keep reading true in
        /// the gaps between them.
        /// <para>
        /// This is the whole reason the turn's <c>result</c> usage is not used for the gauge. That
        /// block totals every request the turn made, and the tool loop sends the same conversation
        /// again on each one, so it holds one context several times over.
        /// </para>
        /// </remarks>
        private void TrackPromptSize(JsonElement payload)
        {
            if (!payload.TryGetProperty("message", out var message)
                || message.ValueKind != JsonValueKind.Object
                || !message.TryGetProperty("usage", out var usage)
                || usage.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // All three, because input_tokens counts only what was not served from cache. On every
            // turn after the first it is the small remainder and cache_read holds the bulk.
            Volatile.Write(
                ref _contextPromptTokens,
                ReadInt32(usage, "input_tokens")
              + ReadInt32(usage, "cache_read_input_tokens")
              + ReadInt32(usage, "cache_creation_input_tokens"));

            // The answer has not been written yet, so this starts at whatever the opening frame
            // claims and is corrected by the deltas below as it arrives.
            Volatile.Write(ref _contextOutputTokens, ReadInt32(usage, "output_tokens"));
        }

        /// <summary>
        /// Updates the length of the answer being generated. The count is cumulative for the message
        /// in flight, so the last frame seen carries its finished size.
        /// </summary>
        private void TrackOutputSize(JsonElement payload)
        {
            if (!payload.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            var output = ReadInt32(usage, "output_tokens");
            if (output > 0)
            {
                Volatile.Write(ref _contextOutputTokens, output);
            }
        }

        private void ForwardTextDelta(JsonElement payload)
        {
            var handler = OnTextDelta;
            if (handler is null)
            {
                return;
            }

            if (!payload.TryGetProperty("delta", out var delta) || delta.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            // Only text_delta. The stream also carries thinking_delta blocks, which must never
            // reach the caller.
            if (ReadString(delta, "type") != "text_delta")
            {
                return;
            }

            if (ReadString(delta, "text") is { } chunk)
            {
                string visible;
                lock (_filterLock)
                {
                    visible = _thinkTagFilter.Feed(chunk);
                }

                if (visible.Length > 0)
                {
                    handler.Invoke(visible);
                }
            }
        }

        private void HandleResult(JsonElement root)
        {
            var turn = Interlocked.Exchange(ref _pendingTurn, null);
            if (turn is null)
            {
                return;
            }

            string flushed;
            lock (_filterLock)
            {
                flushed = _thinkTagFilter.Flush();
            }

            if (flushed.Length > 0)
            {
                OnTextDelta?.Invoke(flushed);
            }

            var usage = root.TryGetProperty("usage", out var candidate) && candidate.ValueKind == JsonValueKind.Object
                ? candidate
                : default;

            // Zero means no message_start frame was ever seen - an older build, or one that does not
            // honour --include-partial-messages. The whole-turn total is then all there is: it
            // overstates a turn that used tools, which is worse than the streamed figure and better
            // than showing the player nothing.
            var prompt = Volatile.Read(ref _contextPromptTokens);
            var context = prompt > 0
                ? prompt + Volatile.Read(ref _contextOutputTokens)
                : ReadInt32(usage, "input_tokens")
                  + ReadInt32(usage, "cache_read_input_tokens")
                  + ReadInt32(usage, "cache_creation_input_tokens");

            var rawResult = ReadString(root, "result") ?? string.Empty;
            var isError = root.TryGetProperty("is_error", out var errorProp) && errorProp.ValueKind == JsonValueKind.True;
            var resultText = isError ? rawResult : ThinkTagFilter.Filter(rawResult);

            turn.TrySetResult(new AgentTurnResult
            {
                ContextTokens = context,
                ContextWindowTokens = ClaudeContextTokens,
                Text = resultText,
                IsError = isError,
                CostUsd = ReadDouble(root, "total_cost_usd"),
                DurationMs = ReadInt32(root, "duration_ms"),
                InputTokens = ReadInt32(usage, "input_tokens"),
                OutputTokens = ReadInt32(usage, "output_tokens"),
                CacheReadTokens = ReadInt32(usage, "cache_read_input_tokens"),
                CacheCreationTokens = ReadInt32(usage, "cache_creation_input_tokens"),
            });
        }

        private async Task TryInterruptAsync()
        {
            // Capabilities stay empty until the first turn reports them, so an unknown build gets
            // the benefit of the doubt rather than silently skipping the interrupt.
            if (Capabilities.Count > 0 && !SupportsInterrupt)
            {
                return;
            }

            var process = _process;
            if (process is null || process.HasExited)
            {
                return;
            }

            try
            {
                await WriteLineAsync(process, BuildInterrupt(), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Best effort: the turn is already being abandoned.
            }
        }

        private static async Task WriteLineAsync(Process process, string json, CancellationToken cancellationToken)
        {
            var input = process.StandardInput;

            // Explicit "\n" rather than WriteLineAsync, which would emit CRLF on Windows.
            await input.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await input.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
            await input.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        internal static string BuildUserMessage(string prompt)
        {
            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("type", "user");

                writer.WriteStartObject("message");
                writer.WriteString("role", "user");

                writer.WriteStartArray("content");
                writer.WriteStartObject();
                writer.WriteString("type", "text");
                writer.WriteString("text", prompt);
                writer.WriteEndObject();
                writer.WriteEndArray();

                writer.WriteEndObject();
                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private static string BuildInterrupt()
        {
            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("type", "control_request");
                writer.WriteString("request_id", Guid.NewGuid().ToString("N"));

                writer.WriteStartObject("request");
                writer.WriteString("subtype", "interrupt");
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private void FaultOutstanding(Exception exception) =>
            Interlocked.Exchange(ref _pendingTurn, null)?.TrySetException(exception);

        private string? SnapshotStandardError()
        {
            lock (_standardErrorLock)
            {
                return _standardError.Length == 0 ? null : _standardError.ToString();
            }
        }

        private static async Task SwallowAsync(Task? task)
        {
            if (task is null)
            {
                return;
            }

            try
            {
                await task.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Shutdown path: reader faults are expected and already reported elsewhere.
            }
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

        private static double ReadDouble(JsonElement owner, string propertyName) =>
            owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var number)
                ? number
                : 0d;
    }
}
