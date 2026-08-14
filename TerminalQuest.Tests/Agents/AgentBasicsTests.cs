using System.Text.Json;

using TerminalQuest.Agents;
using TerminalQuest.Agents.Claude;
using TerminalQuest.Agents.LmStudio;
using TerminalQuest.Mcp;

using Xunit;

namespace TerminalQuest.Tests.Agents
{
    /// <summary>
    /// The pure pieces of the agent layer: how a failure is worded, how a message is shaped, and
    /// the argument vector the narrator's CLI is started with.
    /// </summary>
    public sealed class AgentBasicsTests
    {
        // ---- AgentException -----------------------------------------------------------------

        [Fact]
        public void A_bare_failure_is_just_its_message()
        {
            Assert.Equal("Could not reach the server.", new AgentException("Could not reach the server.").Message);
        }

        [Fact]
        public void A_code_is_appended()
        {
            Assert.Equal(
                "Refused. (code 401)",
                new AgentException("Refused.", code: 401).Message);
        }

        [Fact]
        public void Detail_goes_on_its_own_line()
        {
            var exception = new AgentException("Refused.", "the body", 401);

            Assert.Contains("(code 401)", exception.Message, StringComparison.Ordinal);
            Assert.Contains(Environment.NewLine + "detail: the body", exception.Message, StringComparison.Ordinal);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Empty_detail_adds_nothing(string? detail)
        {
            Assert.Equal("Refused.", new AgentException("Refused.", detail).Message);
        }

        [Fact]
        public void Detail_is_trimmed_at_the_end()
        {
            var exception = new AgentException("Refused.", "the body\r\n\r\n");

            Assert.EndsWith("detail: the body", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void The_parts_are_kept_as_well_as_composed()
        {
            var exception = new AgentException("Refused.", "the body", 401);

            Assert.Equal("the body", exception.Detail);
            Assert.Equal(401, exception.Code);
        }

        // ---- ChatMessage ---------------------------------------------------------------------------

        [Fact]
        public void Messages_carry_the_role_their_factory_names()
        {
            Assert.Equal("system", ChatMessage.System("rules").Role);
            Assert.Equal("user", ChatMessage.User("hello").Role);
            Assert.Equal("tool", ChatMessage.Tool("call_1", "result").Role);
            Assert.Equal("assistant", ChatMessage.Assistant("text", []).Role);
        }

        [Fact]
        public void An_assistant_message_with_no_tool_calls_carries_none()
        {
            // Normalised to null because the serializer keys off "is there a non-empty list", and
            // an empty array on the wire is not the same thing to every server.
            Assert.Null(ChatMessage.Assistant("text", []).ToolCalls);
        }

        [Fact]
        public void An_assistant_message_keeps_the_tool_calls_it_was_given()
        {
            var calls = new[] { new ToolCall("call_1", "get_state", "{}") };

            Assert.Equal(calls, ChatMessage.Assistant("text", calls).ToolCalls);
        }

        [Fact]
        public void A_tool_result_is_paired_back_to_its_request()
        {
            var message = ChatMessage.Tool("call_1", "the result");

            Assert.Equal("call_1", message.ToolCallId);
            Assert.Equal("the result", message.Content);
        }

        // ---- The narrator's argument vector ------------------------------------------------------------

        private static ClaudeSessionOptions Options(
            string? model = null,
            string allowedTools = "mcp__quest__get_state",
            bool persistSession = false) =>
            new()
            {
                Model = model,
                AllowedTools = allowedTools,
                McpConfigJson = """{"mcpServers":{}}""",
                SystemPrompt = "You narrate.",
                PersistSession = persistSession,
            };

        private static List<string> Arguments(ClaudeSessionOptions options) =>
            ClaudeSession.BuildArguments(options).ToList();

        [Fact]
        public void The_session_is_stripped_to_exactly_what_the_caller_asked_for()
        {
            // Without these the process still loads the user's own MCP servers, skills and plugins,
            // which costs tens of thousands of prompt tokens per session.
            var args = Arguments(Options());

            Assert.Contains("--strict-mcp-config", args);
            Assert.Contains("--disable-slash-commands", args);
            Assert.Contains("--setting-sources", args);
            Assert.Equal(string.Empty, args[args.IndexOf("--setting-sources") + 1]);
        }

        [Fact]
        public void The_tools_are_named_twice_because_the_two_flags_mean_different_things()
        {
            // --tools decides which tools exist; --allowed-tools decides which may run without
            // being asked about. A tool named only in the first is offered and then refused.
            var args = Arguments(Options());

            Assert.Equal("mcp__quest__get_state", args[args.IndexOf("--tools") + 1]);
            Assert.Equal("mcp__quest__get_state", args[args.IndexOf("--allowed-tools") + 1]);
        }

        [Fact]
        public void No_tools_means_the_allow_flag_is_left_off()
        {
            var args = Arguments(Options(allowedTools: string.Empty));

            Assert.Contains("--tools", args);
            Assert.DoesNotContain("--allowed-tools", args);
        }

        [Fact]
        public void The_permission_mode_never_stops_to_ask()
        {
            // There is nobody at the console to answer: the player is looking at the transcript.
            var args = Arguments(Options());

            Assert.Equal("dontAsk", args[args.IndexOf("--permission-mode") + 1]);
        }

        [Fact]
        public void The_config_is_passed_as_one_argument_however_much_json_it_is()
        {
            var json = QuestServerConfig.Build(Path.GetTempPath());
            var args = Arguments(Options() with { McpConfigJson = json });

            Assert.Equal(json, args[args.IndexOf("--mcp-config") + 1]);
        }

        [Fact]
        public void The_system_prompt_is_passed_whole()
        {
            var args = Arguments(Options() with { SystemPrompt = "Line one.\nLine two." });

            Assert.Equal("Line one.\nLine two.", args[args.IndexOf("--system-prompt") + 1]);
        }

        [Fact]
        public void A_model_is_named_only_when_one_was_chosen()
        {
            // An empty setting means "whatever the CLI is configured for", which is expressed by
            // leaving the flag off rather than by passing an empty one.
            var args = Arguments(Options("claude-opus-5"));
            Assert.Equal("claude-opus-5", args[args.IndexOf("--model") + 1]);

            Assert.DoesNotContain("--model", Arguments(Options(model: null)));
            Assert.DoesNotContain("--model", Arguments(Options(model: string.Empty)));
        }

        [Fact]
        public void A_session_is_not_persisted_unless_it_was_asked_for()
        {
            Assert.Contains("--no-session-persistence", Arguments(Options()));
            Assert.DoesNotContain("--no-session-persistence", Arguments(Options(persistSession: true)));
        }

        [Fact]
        public void The_stream_is_read_and_written_as_json()
        {
            var args = Arguments(Options());

            Assert.Equal("stream-json", args[args.IndexOf("--input-format") + 1]);
            Assert.Equal("stream-json", args[args.IndexOf("--output-format") + 1]);
            Assert.Contains("--include-partial-messages", args);
        }

        [Fact]
        public void No_argument_is_null()
        {
            Assert.All(Arguments(Options("claude-opus-5")), argument => Assert.NotNull(argument));
        }

        // ---- The user message ----------------------------------------------------------------------------

        [Fact]
        public void A_user_message_is_one_line_of_json()
        {
            // The transport is newline-delimited; a literal newline would split the frame.
            var message = ClaudeSession.BuildUserMessage("Look around.\nThen wait.");

            Assert.DoesNotContain('\n', message);
        }

        [Fact]
        public void A_user_message_carries_the_prompt_as_text_content()
        {
            var root = JsonDocument.Parse(ClaudeSession.BuildUserMessage("Look around.")).RootElement;

            Assert.Equal("user", root.GetProperty("type").GetString());
            var content = Assert.Single(root.GetProperty("message").GetProperty("content").EnumerateArray().ToList());
            Assert.Equal("text", content.GetProperty("type").GetString());
            Assert.Equal("Look around.", content.GetProperty("text").GetString());
        }

        [Fact]
        public void A_prompt_full_of_quotes_and_braces_survives()
        {
            const string prompt = """He said "mind the {gap}" and left.""";

            var root = JsonDocument.Parse(ClaudeSession.BuildUserMessage(prompt)).RootElement;

            Assert.Equal(
                prompt,
                root.GetProperty("message").GetProperty("content")[0].GetProperty("text").GetString());
        }

        // ---- Guards ------------------------------------------------------------------------------------------

        [Fact]
        public void A_session_needs_options()
        {
            Assert.Throws<ArgumentNullException>(() => new ClaudeSession(null!));
        }

        [Fact]
        public async Task A_session_that_was_never_started_refuses_a_turn()
        {
            await using var session = new ClaudeSession(Options());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => session.SendAsync("Look around.", TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task A_turn_needs_a_prompt()
        {
            await using var session = new ClaudeSession(Options());

            await Assert.ThrowsAsync<ArgumentNullException>(
                () => session.SendAsync(null!, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ArgumentException>(
                () => session.SendAsync(string.Empty, TestContext.Current.CancellationToken));
        }

        [Fact]
        public async Task A_fresh_session_advertises_nothing_until_it_has_run()
        {
            // The process stays silent until it receives its first message, so there is nothing to
            // report yet — and nothing that should look like a capability the session does not have.
            await using var session = new ClaudeSession(Options());

            Assert.Null(session.SessionId);
            Assert.Empty(session.Capabilities);
            Assert.False(session.SupportsInterrupt);
        }
    }
}
