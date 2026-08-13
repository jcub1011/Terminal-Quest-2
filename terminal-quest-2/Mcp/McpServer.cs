using System.Buffers;
using System.Text;
using System.Text.Json;

using TerminalQuest.Saves;

namespace TerminalQuest.Mcp
{
    /// <summary>
    /// A Model Context Protocol server speaking JSON-RPC 2.0 over stdio, exposing one save folder
    /// to the narrator.
    /// <para>
    /// This runs as a second copy of the game binary, launched by the <c>claude</c> CLI as a child
    /// process (see <c>Program.Main</c>'s <c>--mcp-server</c> branch). That is why it takes a
    /// directory rather than a live object: it shares nothing with the TUI except the files on
    /// disk.
    /// </para>
    /// <para>
    /// <b>Standard output carries protocol frames and nothing else.</b> A stray write there - a
    /// leftover <c>Console.WriteLine</c>, an unhandled exception's stack trace - corrupts the
    /// transport and the client drops the connection. Diagnostics go to stderr, which the CLI
    /// treats as log output.
    /// </para>
    /// <para>
    /// All JSON is handled with <see cref="JsonDocument"/> and <see cref="Utf8JsonWriter"/> rather
    /// than <c>JsonSerializer</c>, matching <c>ClaudeSession</c>: these are ad-hoc protocol shapes
    /// rather than stable documents, and the type stays free of reflection under <c>PublishAot</c>.
    /// </para>
    /// </summary>
    internal static class McpServer
    {
        /// <summary>Fallback when the client does not name a protocol version.</summary>
        private const string DefaultProtocolVersion = "2024-11-05";

        private const int MethodNotFound = -32601;
        private const int InvalidParams = -32602;
        private const int InternalError = -32603;

        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Serves requests until standard input closes, which is how the client signals shutdown.
        /// </summary>
        /// <returns>A process exit code.</returns>
        public static async Task<int> RunAsync(SaveStore store, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(store);

            using var input = new StreamReader(Console.OpenStandardInput(), Utf8NoBom);
            await using var output = new StreamWriter(Console.OpenStandardOutput(), Utf8NoBom) { AutoFlush = false };

            while (await input.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                var response = Handle(store, line);

                // Notifications have no id and take no reply; sending one would be a protocol error.
                if (response is null)
                {
                    continue;
                }

                // Explicit "\n" rather than WriteLineAsync, which would emit CRLF on Windows.
                await output.WriteAsync(response).ConfigureAwait(false);
                await output.WriteAsync('\n').ConfigureAwait(false);
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            return 0;
        }

        /// <summary>Turns one request line into one response line, or null for a notification.</summary>
        private static string? Handle(SaveStore store, string line)
        {
            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                // Unparseable input carries no id to answer against, so there is nobody to tell.
                return null;
            }

            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                var id = root.TryGetProperty("id", out var candidate) && candidate.ValueKind is not JsonValueKind.Null
                    ? candidate
                    : default;
                var isNotification = id.ValueKind == JsonValueKind.Undefined;

                var method = ReadString(root, "method");
                var parameters = root.TryGetProperty("params", out var value) && value.ValueKind == JsonValueKind.Object
                    ? value
                    : default;

                if (isNotification)
                {
                    // "notifications/initialized" and friends are acknowledged by staying silent.
                    return null;
                }

                try
                {
                    return method switch
                    {
                        "initialize" => Initialize(id, parameters),
                        "tools/list" => ToolsList(id),
                        "tools/call" => ToolsCall(store, id, parameters),
                        "ping" => Result(id, static _ => { }),
                        _ => Error(id, MethodNotFound, $"Unknown method '{method}'."),
                    };
                }
                catch (SaveException ex)
                {
                    // A broken save is the caller's problem to report, not grounds for tearing the
                    // server down: the next call may well target a document that still parses.
                    return Error(id, InternalError, ex.Message);
                }
                catch (Exception ex)
                {
                    return Error(id, InternalError, ex.Message);
                }
            }
        }

        private static string Initialize(JsonElement id, JsonElement parameters)
        {
            // Echo the client's version when it names one: the CLI and this server ship together,
            // so pinning a version here would only create a mismatch to debug later.
            var protocolVersion = ReadString(parameters, "protocolVersion") ?? DefaultProtocolVersion;

            return Result(id, writer =>
            {
                writer.WriteString("protocolVersion", protocolVersion);

                writer.WriteStartObject("capabilities");
                writer.WriteStartObject("tools");
                writer.WriteEndObject();
                writer.WriteEndObject();

                writer.WriteStartObject("serverInfo");
                writer.WriteString("name", "quest");
                writer.WriteString("version", "1.0.0");
                writer.WriteEndObject();
            });
        }

        private static string ToolsList(JsonElement id) => Result(id, static writer =>
        {
            writer.WriteStartArray("tools");

            foreach (var tool in QuestTools.Definitions)
            {
                writer.WriteStartObject();
                writer.WriteString("name", tool.Name);
                writer.WriteString("description", tool.Description);
                writer.WritePropertyName("inputSchema");
                writer.WriteRawValue(tool.InputSchema);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        });

        private static string ToolsCall(SaveStore store, JsonElement id, JsonElement parameters)
        {
            if (ReadString(parameters, "name") is not { Length: > 0 } name)
            {
                return Error(id, InvalidParams, "tools/call requires a tool name.");
            }

            var arguments = parameters.ValueKind == JsonValueKind.Object
                && parameters.TryGetProperty("arguments", out var value)
                && value.ValueKind == JsonValueKind.Object
                    ? value
                    : default;

            var outcome = QuestTools.Invoke(store, name, arguments);

            // A tool that fails reports it inside the result, not as a JSON-RPC error: the model
            // is meant to read "no character named Bess" and act on it, and a transport-level
            // error would never reach it as text.
            return Result(id, writer =>
            {
                writer.WriteStartArray("content");
                writer.WriteStartObject();
                writer.WriteString("type", "text");
                writer.WriteString("text", outcome.Text);
                writer.WriteEndObject();
                writer.WriteEndArray();

                writer.WriteBoolean("isError", outcome.IsError);
            });
        }

        private static string Result(JsonElement id, Action<Utf8JsonWriter> writeResult)
        {
            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("jsonrpc", "2.0");
                WriteId(writer, id);

                writer.WriteStartObject("result");
                writeResult(writer);
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        private static string Error(JsonElement id, int code, string message)
        {
            var buffer = new ArrayBufferWriter<byte>();

            using (var writer = new Utf8JsonWriter(buffer))
            {
                writer.WriteStartObject();
                writer.WriteString("jsonrpc", "2.0");
                WriteId(writer, id);

                writer.WriteStartObject("error");
                writer.WriteNumber("code", code);
                writer.WriteString("message", message);
                writer.WriteEndObject();

                writer.WriteEndObject();
            }

            return Encoding.UTF8.GetString(buffer.WrittenSpan);
        }

        /// <summary>Echoes the request id back with its original type - it may be a string or a number.</summary>
        private static void WriteId(Utf8JsonWriter writer, JsonElement id)
        {
            writer.WritePropertyName("id");

            switch (id.ValueKind)
            {
                case JsonValueKind.String:
                    writer.WriteStringValue(id.GetString());
                    break;
                case JsonValueKind.Number:
                    writer.WriteRawValue(id.GetRawText());
                    break;
                default:
                    writer.WriteNullValue();
                    break;
            }
        }

        private static string? ReadString(JsonElement owner, string propertyName) =>
            owner.ValueKind == JsonValueKind.Object
            && owner.TryGetProperty(propertyName, out var value)
            && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
    }
}
