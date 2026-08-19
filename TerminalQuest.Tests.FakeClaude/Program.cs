using System.Text;
using System.Text.Json;

namespace TerminalQuest.Tests.FakeClaude
{
    /// <summary>
    /// A stand-in for the <c>claude</c> CLI, speaking the same newline-delimited JSON over stdio.
    /// </summary>
    /// <remarks>
    /// Reads one JSON line per turn and replies with whatever the test asked for. The script is
    /// supplied through <c>TQ_FAKE_CLAUDE_SCRIPT</c> rather than through the argument vector,
    /// because <c>ClaudeSession</c> builds that vector itself and leaves no room for anything of
    /// ours. The whole argument vector is echoed to a file when <c>TQ_FAKE_CLAUDE_ARGV</c> names
    /// one, so a test can check what the session actually launched.
    /// </remarks>
    internal static class Program
    {
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private static async Task<int> Main(string[] args)
        {
            if (Environment.GetEnvironmentVariable("TQ_FAKE_CLAUDE_ARGV") is { Length: > 0 } argvPath)
            {
                await File.WriteAllLinesAsync(argvPath, args, Utf8NoBom).ConfigureAwait(false);
            }

            var script = Environment.GetEnvironmentVariable("TQ_FAKE_CLAUDE_SCRIPT") ?? "reply";

            if (script == "die")
            {
                await Console.Error.WriteAsync("the model host fell over").ConfigureAwait(false);
                return 3;
            }

            using var input = new StreamReader(Console.OpenStandardInput(), Utf8NoBom);
            await using var output = new StreamWriter(Console.OpenStandardOutput(), Utf8NoBom) { AutoFlush = false };

            var turn = 0;

            while (await input.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (line.Length == 0)
                {
                    continue;
                }

                // An interrupt carries no prompt and gets no result frame; the session is expected
                // to stop waiting on its own.
                if (IsInterrupt(line))
                {
                    continue;
                }

                turn++;

                foreach (var frame in Frames(script, turn))
                {
                    await output.WriteAsync(frame).ConfigureAwait(false);
                    await output.WriteAsync('\n').ConfigureAwait(false);
                }

                await output.FlushAsync().ConfigureAwait(false);

                if (script == "hang")
                {
                    // Deliberately never answers, so the turn timeout is what ends the wait.
                    await Task.Delay(Timeout.Infinite).ConfigureAwait(false);
                }
            }

            return 0;
        }

        private static bool IsInterrupt(string line)
        {
            try
            {
                using var document = JsonDocument.Parse(line);

                return document.RootElement.ValueKind == JsonValueKind.Object
                    && document.RootElement.TryGetProperty("type", out var type)
                    && type.ValueKind == JsonValueKind.String
                    && type.GetString() == "control_request";
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static IEnumerable<string> Frames(string script, int turn) => script switch
        {
            "reply" => [Init(), Delta("Hello"), Delta(" there"), Result("Hello there")],

            // Reasoning must never reach the caller.
            "thinking" =>
            [
                Init(),
                Thinking("I should describe the weather"),
                Delta("The road was empty."),
                Result("The road was empty."),
            ],

            "story_tags" =>
            [
                Init(),
                Delta("<story>The road "),
                Delta("was empty.</story>"),
                Result("<story>The road was empty.</story>"),
            ],

            // Two turns, to prove the session id is only taken from the first.
            "resession" => [Init($"session-{turn}"), Delta($"turn {turn}"), Result($"turn {turn}")],

            "error" => [Init(), Result("something went wrong", isError: true)],

            "usage" => [Init(), Delta("counted"), Result("counted", usage: true)],

            // Two requests in one turn, as a tool loop produces. The second message_start is the one
            // that describes the context: its prompt has grown to include the first exchange, and the
            // result's totals hold both prompts added together.
            "context" =>
            [
                Init(),
                MessageStart(input: 100, cacheRead: 200, cacheCreation: 300),
                MessageStart(input: 10, cacheRead: 1000, cacheCreation: 500),
                Delta("counted"),
                MessageDelta(output: 77),
                Result("counted", usage: true),
            ],

            "noise" => [Init(), "{ not json", "[]", "{\"type\":\"unknown\"}", Delta("survived"), Result("survived")],

            "hang" => [Init()],

            _ => [Init(), Delta("Hello"), Result("Hello")],
        };

        // Built by concatenation rather than as interpolated raw strings: these frames end in runs
        // of closing braces, which fight the raw-string brace counting for no benefit here.
        private static string Init(string sessionId = "session-1") =>
            "{\"type\":\"system\",\"subtype\":\"init\",\"session_id\":" + Json(sessionId)
            + ",\"capabilities\":[\"interrupt_receipt_v1\"]}";

        private static string Delta(string text) =>
            "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\","
            + "\"delta\":{\"type\":\"text_delta\",\"text\":" + Json(text) + "}}}";

        /// <summary>
        /// The frame that opens one request, carrying the usage that describes its prompt. The output
        /// count starts at one because that is what the real stream does - the answer has not been
        /// written yet.
        /// </summary>
        private static string MessageStart(int input, int cacheRead, int cacheCreation) =>
            "{\"type\":\"stream_event\",\"event\":{\"type\":\"message_start\",\"message\":{\"usage\":{"
            + "\"input_tokens\":" + input
            + ",\"output_tokens\":1"
            + ",\"cache_read_input_tokens\":" + cacheRead
            + ",\"cache_creation_input_tokens\":" + cacheCreation + "}}}}";

        /// <summary>The answer's running length, cumulative for the message in flight.</summary>
        private static string MessageDelta(int output) =>
            "{\"type\":\"stream_event\",\"event\":{\"type\":\"message_delta\",\"usage\":{"
            + "\"output_tokens\":" + output + "}}}";

        private static string Thinking(string text) =>
            "{\"type\":\"stream_event\",\"event\":{\"type\":\"content_block_delta\","
            + "\"delta\":{\"type\":\"thinking_delta\",\"thinking\":" + Json(text) + "}}}";

        private static string Result(string text, bool isError = false, bool usage = false) =>
            usage
                ? "{\"type\":\"result\",\"result\":" + Json(text)
                  + ",\"is_error\":false,\"total_cost_usd\":0.0125,\"duration_ms\":1234,"
                  + "\"usage\":{\"input_tokens\":11,\"output_tokens\":22,"
                  + "\"cache_read_input_tokens\":33,\"cache_creation_input_tokens\":44}}"
                : "{\"type\":\"result\",\"result\":" + Json(text)
                  + ",\"is_error\":" + (isError ? "true" : "false") + "}";

        private static string Json(string value) => JsonSerializer.Serialize(value);
    }
}
