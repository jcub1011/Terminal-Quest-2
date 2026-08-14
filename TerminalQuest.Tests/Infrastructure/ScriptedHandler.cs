using System.Net;
using System.Text;

namespace TerminalQuest.Tests.Infrastructure
{
    /// <summary>
    /// Answers the LM Studio session's requests from a script instead of a socket.
    /// </summary>
    /// <remarks>
    /// Preferred over a loopback listener: no port to bind, nothing to leak if a test fails part
    /// way through, and the request bodies are recorded so a test can check what the session
    /// actually sent - which is how the tool loop's history handling is observed at all.
    /// </remarks>
    internal sealed class ScriptedHandler : HttpMessageHandler
    {
        private readonly Queue<Func<HttpRequestMessage, HttpResponseMessage>> _replies = new();
        private readonly Lock _gate = new();
        private readonly List<string> _bodies = [];
        private readonly List<string> _paths = [];

        /// <summary>Every request body sent, in order.</summary>
        public IReadOnlyList<string> Bodies
        {
            get
            {
                lock (_gate)
                {
                    return [.. _bodies];
                }
            }
        }

        /// <summary>Every path requested, in order.</summary>
        public IReadOnlyList<string> Paths
        {
            get
            {
                lock (_gate)
                {
                    return [.. _paths];
                }
            }
        }

        /// <summary>Answers the model-list request with the given ids.</summary>
        public ScriptedHandler Models(params string[] ids)
        {
            var data = string.Join(',', ids.Select(id => $"{{\"id\":\"{id}\"}}"));

            return Json($"{{\"data\":[{data}]}}");
        }

        /// <summary>Answers with a server-sent-event stream built from raw frame lines.</summary>
        public ScriptedHandler Stream(params string[] lines)
        {
            var body = string.Concat(lines.Select(line => line + "\n\n"));

            return Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "text/event-stream"),
            });
        }

        /// <summary>Answers with a chat completion that streams <paramref name="text"/> and stops.</summary>
        public ScriptedHandler Says(string text, int promptTokens = 10, int completionTokens = 5)
        {
            return Stream(
                "data: " + Chunk($"\"content\":{Quote(text)}"),
                "data: " + Usage(promptTokens, completionTokens),
                "data: [DONE]");
        }

        /// <summary>Answers with a chat completion that asks for one tool and stops.</summary>
        public ScriptedHandler Calls(string tool, string arguments, string id = "call_1")
        {
            var call = $"\"tool_calls\":[{{\"index\":0,\"id\":\"{id}\","
                + $"\"function\":{{\"name\":\"{tool}\",\"arguments\":{Quote(arguments)}}}}}]";

            return Stream("data: " + Chunk(call), "data: [DONE]");
        }

        /// <summary>Answers with a raw JSON body and a 200.</summary>
        public ScriptedHandler Json(string body) =>
            Enqueue(_ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });

        /// <summary>Answers with a status code and a body.</summary>
        public ScriptedHandler Status(HttpStatusCode code, string body = "") =>
            Enqueue(_ => new HttpResponseMessage(code)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });

        /// <summary>Never answers, so the turn's own deadline is what ends the wait.</summary>
        public ScriptedHandler Hangs() =>
            Enqueue(_ => throw new TaskCanceledException("the server never answered"));

        public ScriptedHandler Enqueue(Func<HttpRequestMessage, HttpResponseMessage> reply)
        {
            lock (_gate)
            {
                _replies.Enqueue(reply);
            }

            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            Func<HttpRequestMessage, HttpResponseMessage> reply;

            lock (_gate)
            {
                _bodies.Add(body);
                _paths.Add(request.RequestUri?.AbsolutePath ?? string.Empty);

                if (!_replies.TryDequeue(out var next))
                {
                    throw new InvalidOperationException(
                        $"The session made {_bodies.Count} requests but the script only answers {_bodies.Count - 1}.");
                }

                reply = next;
            }

            cancellationToken.ThrowIfCancellationRequested();

            return reply(request);
        }

        private static string Chunk(string delta) =>
            $"{{\"choices\":[{{\"index\":0,\"delta\":{{{delta}}}}}]}}";

        private static string Usage(int promptTokens, int completionTokens) =>
            $"{{\"choices\":[],\"usage\":{{\"prompt_tokens\":{promptTokens},"
            + $"\"completion_tokens\":{completionTokens}}}}}";

        private static string Quote(string value) => System.Text.Json.JsonSerializer.Serialize(value);
    }
}
