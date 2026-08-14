using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    /// <summary>
    /// The JSON-RPC layer the narrator's CLI talks to.
    /// </summary>
    /// <remarks>
    /// Standard output carries protocol frames and nothing else, and every frame is one line — so
    /// the framing invariants here are not stylistic. A response containing a newline splits into
    /// two frames and the client drops the connection.
    /// </remarks>
    public sealed class McpServerTests
    {
        private static JsonElement Respond(SaveStore store, string request)
        {
            var response = McpServer.Handle(store, request);

            Assert.NotNull(response);
            Assert.DoesNotContain('\n', response);
            Assert.DoesNotContain('\r', response);

            return JsonDocument.Parse(response).RootElement.Clone();
        }

        private static string Request(string method, string id = "1", string? parameters = null) =>
            parameters is null
                ? $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}"}"""
                : $$"""{"jsonrpc":"2.0","id":{{id}},"method":"{{method}}","params":{{parameters}}}""";

        // ---- Framing and malformed input ----------------------------------------------------

        [Theory]
        [InlineData("{ not json")]
        [InlineData("")]
        [InlineData("garbage")]
        public void Unparseable_input_gets_no_reply(string line)
        {
            // There is no id to answer against, so there is nobody to tell.
            using var save = new TempSave();

            Assert.Null(McpServer.Handle(save.Store, line));
        }

        [Theory]
        [InlineData("[1,2,3]")]
        [InlineData("\"a string\"")]
        [InlineData("42")]
        [InlineData("null")]
        public void A_request_that_is_not_an_object_gets_no_reply(string line)
        {
            using var save = new TempSave();

            Assert.Null(McpServer.Handle(save.Store, line));
        }

        [Theory]
        [InlineData("""{"jsonrpc":"2.0","method":"notifications/initialized"}""")]
        [InlineData("""{"jsonrpc":"2.0","method":"ping","id":null}""")]
        public void A_notification_is_acknowledged_by_staying_silent(string line)
        {
            // Sending a reply to a notification would itself be a protocol error.
            using var save = new TempSave();

            Assert.Null(McpServer.Handle(save.Store, line));
        }

        [Fact]
        public void Every_response_is_a_single_line()
        {
            using var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", "A smith's apprentice.\nTwo lines.", ClassTemplates.All[0], "The Ford");

            foreach (var request in new[]
            {
                Request("initialize"),
                Request("tools/list"),
                Request("ping"),
                Request("tools/call", parameters: """{"name":"get_state","arguments":{}}"""),
            })
            {
                var response = McpServer.Handle(save.Store, request);

                Assert.NotNull(response);
                Assert.DoesNotContain('\n', response);
            }
        }

        // ---- Ids -----------------------------------------------------------------------------

        [Fact]
        public void A_numeric_id_is_echoed_as_a_number()
        {
            using var save = new TempSave();

            var response = Respond(save.Store, Request("ping", "7"));

            Assert.Equal(JsonValueKind.Number, response.GetProperty("id").ValueKind);
            Assert.Equal(7, response.GetProperty("id").GetInt32());
        }

        [Fact]
        public void A_string_id_is_echoed_as_a_string()
        {
            using var save = new TempSave();

            var response = Respond(save.Store, Request("ping", "\"abc\""));

            Assert.Equal(JsonValueKind.String, response.GetProperty("id").ValueKind);
            Assert.Equal("abc", response.GetProperty("id").GetString());
        }

        [Fact]
        public void Every_response_names_the_protocol_version()
        {
            using var save = new TempSave();

            Assert.Equal("2.0", Respond(save.Store, Request("ping")).GetProperty("jsonrpc").GetString());
        }

        // ---- initialize -------------------------------------------------------------------------

        [Fact]
        public void Initialize_echoes_the_version_the_client_named()
        {
            // The CLI and this server ship together, so pinning a version here would only create
            // a mismatch to debug later.
            using var save = new TempSave();

            var response = Respond(
                save.Store,
                Request("initialize", parameters: """{"protocolVersion":"2025-06-18"}"""));

            Assert.Equal("2025-06-18", response.GetProperty("result").GetProperty("protocolVersion").GetString());
        }

        [Fact]
        public void Initialize_falls_back_when_the_client_names_no_version()
        {
            using var save = new TempSave();

            var response = Respond(save.Store, Request("initialize"));

            Assert.Equal("2024-11-05", response.GetProperty("result").GetProperty("protocolVersion").GetString());
        }

        [Fact]
        public void Initialize_advertises_tool_support()
        {
            using var save = new TempSave();

            var result = Respond(save.Store, Request("initialize")).GetProperty("result");

            Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
            Assert.Equal("quest", result.GetProperty("serverInfo").GetProperty("name").GetString());
        }

        // ---- tools/list ---------------------------------------------------------------------------

        [Fact]
        public void Tools_list_returns_every_definition_with_a_schema()
        {
            using var save = new TempSave();

            var tools = Respond(save.Store, Request("tools/list")).GetProperty("result").GetProperty("tools");

            Assert.Equal(QuestTools.Definitions.Count, tools.GetArrayLength());
            Assert.All(tools.EnumerateArray().ToList(), tool =>
            {
                Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("name").GetString()));
                Assert.False(string.IsNullOrWhiteSpace(tool.GetProperty("description").GetString()));
                Assert.Equal(JsonValueKind.Object, tool.GetProperty("inputSchema").ValueKind);
            });
        }

        // ---- tools/call ----------------------------------------------------------------------------

        [Fact]
        public void A_tool_call_returns_its_text_as_content()
        {
            using var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", string.Empty, ClassTemplates.All[0], "The Ford");

            var result = Respond(
                    save.Store,
                    Request("tools/call", parameters: """{"name":"get_state","arguments":{}}"""))
                .GetProperty("result");

            var content = Assert.Single(result.GetProperty("content").EnumerateArray().ToList());
            Assert.Equal("text", content.GetProperty("type").GetString());
            Assert.Contains("Rowan", content.GetProperty("text").GetString()!, StringComparison.Ordinal);
            Assert.False(result.GetProperty("isError").GetBoolean());
        }

        [Fact]
        public void A_tool_that_fails_reports_it_inside_the_result()
        {
            // The model is meant to read "no character named Bess" and act on it; a transport-level
            // error would never reach it as text.
            using var save = new TempSave();

            var result = Respond(
                    save.Store,
                    Request("tools/call", parameters: """{"name":"get_character","arguments":{"name":"Bess"}}"""))
                .GetProperty("result");

            Assert.True(result.GetProperty("isError").GetBoolean());
            Assert.False(Respond(save.Store, Request("ping")).TryGetProperty("error", out _));
        }

        [Fact]
        public void An_unknown_tool_is_reported_inside_the_result_too()
        {
            using var save = new TempSave();

            var result = Respond(
                    save.Store,
                    Request("tools/call", parameters: """{"name":"no_such_tool","arguments":{}}"""))
                .GetProperty("result");

            Assert.True(result.GetProperty("isError").GetBoolean());
        }

        [Theory]
        [InlineData("""{"arguments":{}}""")]
        [InlineData("""{"name":""}""")]
        [InlineData("""{}""")]
        public void A_tool_call_without_a_name_is_an_invalid_params_error(string parameters)
        {
            using var save = new TempSave();

            var response = Respond(save.Store, Request("tools/call", parameters: parameters));

            Assert.Equal(-32602, response.GetProperty("error").GetProperty("code").GetInt32());
        }

        [Fact]
        public void A_tool_call_with_no_arguments_at_all_still_reaches_the_tool()
        {
            using var save = new TempSave();

            var response = Respond(save.Store, Request("tools/call", parameters: """{"name":"get_state"}"""));

            Assert.True(response.TryGetProperty("result", out _));
        }

        // ---- Errors ------------------------------------------------------------------------------------

        [Fact]
        public void An_unknown_method_is_a_method_not_found_error()
        {
            using var save = new TempSave();

            var error = Respond(save.Store, Request("does/not/exist")).GetProperty("error");

            Assert.Equal(-32601, error.GetProperty("code").GetInt32());
            Assert.Contains("does/not/exist", error.GetProperty("message").GetString()!, StringComparison.Ordinal);
        }

        [Fact]
        public void A_request_with_no_method_is_a_method_not_found_error()
        {
            using var save = new TempSave();

            var response = Respond(save.Store, """{"jsonrpc":"2.0","id":1}""");

            Assert.Equal(-32601, response.GetProperty("error").GetProperty("code").GetInt32());
        }

        [Fact]
        public void A_broken_save_is_reported_without_tearing_the_server_down()
        {
            // The next call may well target a document that still parses, so a corrupt file is
            // the caller's problem to report rather than grounds for shutting down.
            using var save = new TempSave();
            save.WriteRaw("characters.json", "{ not json");

            var response = Respond(
                save.Store,
                Request("tools/call", parameters: """{"name":"get_state","arguments":{}}"""));

            Assert.Equal(-32603, response.GetProperty("error").GetProperty("code").GetInt32());

            // Still serving.
            Assert.True(Respond(save.Store, Request("ping", "2")).TryGetProperty("result", out _));
        }

        [Fact]
        public void Ping_answers_with_an_empty_result()
        {
            using var save = new TempSave();

            var response = Respond(save.Store, Request("ping"));

            Assert.Equal(JsonValueKind.Object, response.GetProperty("result").ValueKind);
            Assert.False(response.TryGetProperty("error", out _));
        }
    }
}
