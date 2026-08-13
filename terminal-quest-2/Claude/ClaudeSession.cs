using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TerminalQuest.Claude
{
    /// <summary>
    /// A long-lived <c>claude</c> process driven over newline-delimited JSON.
    /// </summary>
    /// <remarks>
    /// One process is held open for the whole conversation, so context and the prompt cache both
    /// survive across turns — the second and later turns read their prefix from cache instead of
    /// rebuilding it. The session runs with no tools, no MCP servers and no skills; it is purely
    /// text in, text out.
    /// <para>
    /// All JSON is handled with <see cref="JsonDocument"/> and <see cref="Utf8JsonWriter"/> rather
    /// than <c>JsonSerializer</c>, so the type stays free of reflection and is safe under
    /// <c>PublishAot</c>.
    /// </para>
    /// </remarks>
    public sealed class ClaudeSession : IAsyncDisposable
    {
        private const int MaxBufferedStandardErrorChars = 16 * 1024;

        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly ClaudeSessionOptions _options;
        private readonly SemaphoreSlim _turnGate = new(1, 1);
        private readonly StringBuilder _standardError = new();
        private readonly Lock _standardErrorLock = new();

        private Process? _process;
        private Task? _stdoutReader;
        private Task? _stderrReader;
        private TaskCompletionSource<ClaudeTurnResult>? _pendingTurn;
        private bool _disposed;

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

            // ArgumentList quotes each element itself, so the embedded MCP JSON and the empty
            // --tools value survive without hand-rolled escaping.
            foreach (var argument in BuildArguments(_options))
            {
                startInfo.ArgumentList.Add(argument);
            }

            Process process;
            try
            {
                process = Process.Start(startInfo)
                    ?? throw new ClaudeException($"Could not start '{_options.ExecutablePath}'.");
            }
            catch (Exception ex) when (ex is not ClaudeException)
            {
                throw new ClaudeException($"Could not start '{_options.ExecutablePath}'. Is it on PATH?", ex.Message);
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

                throw new ClaudeException(
                    $"'{_options.ExecutablePath}' exited immediately after starting.",
                    SnapshotStandardError(),
                    process.ExitCode);
            }
        }

        /// <summary>
        /// Sends one message and waits for the complete response. Turns are serialized: a second
        /// call waits for the first to finish.
        /// </summary>
        public async Task<ClaudeTurnResult> SendAsync(string prompt, CancellationToken cancellationToken = default)
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
                    throw new ClaudeException(
                        "The Claude process has exited.", SnapshotStandardError(), process.ExitCode);
                }

                var turn = new TaskCompletionSource<ClaudeTurnResult>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingTurn = turn;

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

                    throw new ClaudeException(
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

        private static IEnumerable<string> BuildArguments(ClaudeSessionOptions options)
        {
            yield return "-p";

            yield return "--input-format";
            yield return "stream-json";
            yield return "--output-format";
            yield return "stream-json";
            yield return "--verbose";
            yield return "--include-partial-messages";

            // Strip the session down to nothing. --tools "" alone is not sufficient: without the
            // rest of these the process still loads user MCP servers, skills and plugins, which
            // costs tens of thousands of prompt tokens per session.
            yield return "--tools";
            yield return string.Empty;
            yield return "--strict-mcp-config";
            yield return "--mcp-config";
            yield return "{\"mcpServers\":{}}";
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
                FaultOutstanding(new ClaudeException("Lost the connection to the Claude process.", ex.Message));
                return;
            }

            FaultOutstanding(new ClaudeException(
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
            var handler = OnTextDelta;
            if (handler is null)
            {
                return;
            }

            if (!root.TryGetProperty("event", out var payload) || payload.ValueKind != JsonValueKind.Object)
            {
                return;
            }

            if (ReadString(payload, "type") != "content_block_delta")
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
                handler.Invoke(chunk);
            }
        }

        private void HandleResult(JsonElement root)
        {
            var turn = Interlocked.Exchange(ref _pendingTurn, null);
            if (turn is null)
            {
                return;
            }

            var usage = root.TryGetProperty("usage", out var candidate) && candidate.ValueKind == JsonValueKind.Object
                ? candidate
                : default;

            turn.TrySetResult(new ClaudeTurnResult
            {
                Text = ReadString(root, "result") ?? string.Empty,
                IsError = root.TryGetProperty("is_error", out var isError) && isError.ValueKind == JsonValueKind.True,
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

        private static string BuildUserMessage(string prompt)
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
