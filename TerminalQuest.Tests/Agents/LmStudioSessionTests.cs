using System.Net;

using TerminalQuest.Agents;
using TerminalQuest.Agents.LmStudio;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Agents
{
    /// <summary>
    /// The LM Studio session driven against a scripted transport.
    /// </summary>
    /// <remarks>
    /// The streaming reply and the tool loop are where this type earns its keep, and neither can be
    /// reached without answering an HTTP request. These run against an injected handler rather than
    /// a real socket, so they are deterministic and need no port.
    /// </remarks>
    [Trait(Categories.Name, Categories.Integration)]
    public sealed class LmStudioSessionTests
    {
        private static CancellationToken Token => TestContext.Current.CancellationToken;

        private static LmStudioSessionOptions Options(
            string? model = null,
            int maxToolIterations = 12,
            TimeSpan? turnTimeout = null) =>
            new()
            {
                BaseUrl = "http://localhost:1234/v1",
                Model = model,
                SystemPrompt = "You narrate.",
                MaxToolIterations = maxToolIterations,
                TurnTimeout = turnTimeout ?? TimeSpan.FromSeconds(30),
            };

        private static TempSave Seeded()
        {
            var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", "A quiet sort.", ClassTemplates.All[0], "The Ford");
            return save;
        }

        // ---- Starting -----------------------------------------------------------------------

        [Fact]
        public async Task Starting_asks_the_server_what_it_is_serving()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);

            // Two questions, both answered before a turn is spent: what is being served, and how much
            // of it the model can hold.
            Assert.Equal(["/v1/models", "/api/v0/models"], handler.Paths);
        }

        [Fact]
        public async Task Starting_reads_the_context_length_from_the_native_endpoint()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model").ContextLength(8192).Says("Hello.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal(8192, result.ContextWindowTokens);
        }

        [Fact]
        public async Task A_server_without_the_native_endpoint_still_starts_and_still_narrates()
        {
            // Ollama, llama.cpp, vLLM and Jan all speak /v1 and answer 404 here. The context gauge
            // does without its denominator; the session must not notice at all.
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model").Says("Hello.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal(0, result.ContextWindowTokens);
            Assert.Equal("Hello.", result.Text);
            Assert.False(result.IsError);
        }

        [Fact]
        public async Task Context_is_the_last_prompt_plus_the_answer_to_it()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .ContextLength(8192)
                .Says("Hello.", promptTokens: 900, completionTokens: 40);

            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal(940, result.ContextTokens);
        }

        [Fact]
        public async Task A_turn_that_used_tools_counts_the_conversation_once()
        {
            // This provider resends the whole history on every round trip, so the last request's
            // prompt already contains the earlier ones. Only the last answer is outside it.
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .ContextLength(8192)
                .Calls("get_characters", "{}", promptTokens: 800, completionTokens: 5)
                .Says("Hello.", promptTokens: 1500, completionTokens: 40);

            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            // 1540 - the last prompt and the last answer. Not 800 as well: that request's prompt is
            // a prefix of this one, and adding them would count the conversation twice.
            Assert.Equal(1540, result.ContextTokens);

            // Billing still totals the turn, both answers included. That the two figures disagree is
            // the whole reason ContextTokens is not derived from them.
            Assert.Equal(45, result.OutputTokens);
        }

        [Fact]
        public async Task A_server_that_reports_no_usage_reports_no_context_either()
        {
            // Rather than an answer's length with no prompt to sit in, which would read as a context
            // that shrank.
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .ContextLength(8192)
                .Says("Hello.", promptTokens: 0, completionTokens: 0);

            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal(0, result.ContextTokens);
        }

        [Fact]
        public async Task A_model_the_server_does_not_offer_is_refused_before_a_turn_is_spent()
        {
            // Sent anyway it comes back as a 404 on the first turn, by which point the player is
            // looking at a blank transcript rather than at the settings screen.
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model", "another-model");
            await using var session = new LmStudioSession(Options("not-loaded"), save.Store, handler);

            var exception = await Assert.ThrowsAsync<AgentException>(() => session.StartAsync(Token));

            Assert.Contains("not-loaded", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_model_the_server_does_offer_is_accepted()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model");
            await using var session = new LmStudioSession(Options("A-MODEL"), save.Store, handler);

            await session.StartAsync(Token);
        }

        [Fact]
        public async Task An_empty_model_list_is_not_evidence_of_anything()
        {
            // It means the server answered in a shape this does not read, which is not the same as
            // "your model is missing".
            using var save = Seeded();
            var handler = new ScriptedHandler().Json("{}");
            await using var session = new LmStudioSession(Options("some-model"), save.Store, handler);

            await session.StartAsync(Token);
        }

        [Fact]
        public async Task A_server_wanting_a_key_says_so_in_its_own_words()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Status(HttpStatusCode.Unauthorized, "{}");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            var exception = await Assert.ThrowsAsync<AgentException>(() => session.StartAsync(Token));

            Assert.Contains("API key", exception.Message, StringComparison.Ordinal);
            Assert.Equal(401, exception.Code);
        }

        [Fact]
        public async Task A_server_that_refuses_the_list_is_reported()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Status(HttpStatusCode.InternalServerError, "{}");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await Assert.ThrowsAsync<AgentException>(() => session.StartAsync(Token));
        }

        [Fact]
        public async Task A_session_cannot_be_started_twice()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model").Models("a-model");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(Token));
        }

        // ---- A turn -------------------------------------------------------------------------------

        [Fact]
        public async Task A_turn_returns_what_the_model_streamed()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model").Says("The road was empty.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal("The road was empty.", result.Text);
            Assert.False(result.IsError);
        }

        [Fact]
        public async Task The_deltas_assemble_into_the_text_the_turn_reports()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Stream(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"The road \"}}]}",
                    "data: {\"choices\":[{\"delta\":{\"content\":\"was empty.\"}}]}",
                    "data: [DONE]");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            var streamed = new List<string>();
            session.OnTextDelta += delta => { lock (streamed) { streamed.Add(delta); } };

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal("The road was empty.", result.Text);

            lock (streamed)
            {
                Assert.Equal(result.Text, string.Concat(streamed));
            }
        }

        [Fact]
        public async Task Reasoning_is_stripped_out_of_the_narration()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Stream(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"<think>weather?</think>\"}}]}",
                    "data: {\"choices\":[{\"delta\":{\"content\":\"The road was empty.\"}}]}",
                    "data: [DONE]");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal("The road was empty.", result.Text);
        }

        [Fact]
        public async Task A_reasoning_field_beside_the_content_is_ignored()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Stream(
                    "data: {\"choices\":[{\"delta\":{\"reasoning_content\":\"hidden\",\"content\":\"shown\"}}]}",
                    "data: [DONE]");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);

            Assert.Equal("shown", (await session.SendAsync("Look around.", Token)).Text);
        }

        [Fact]
        public async Task Keep_alive_comments_and_blank_lines_are_skipped()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Stream(
                    ": keep-alive",
                    string.Empty,
                    "data: {\"choices\":[{\"delta\":{\"content\":\"shown\"}}]}",
                    ": another",
                    "data: [DONE]");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);

            Assert.Equal("shown", (await session.SendAsync("Look around.", Token)).Text);
        }

        [Fact]
        public async Task Token_counts_come_back_from_the_usage_frame()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model").Says("text", promptTokens: 30, completionTokens: 7);
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal(30, result.InputTokens);
            Assert.Equal(7, result.OutputTokens);
        }

        [Fact]
        public async Task An_error_arriving_after_a_two_hundred_still_fails_the_turn()
        {
            // A server can accept the request and fall over partway through the reply.
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Stream(
                    "data: {\"choices\":[{\"delta\":{\"content\":\"half a \"}}]}",
                    "data: {\"error\":{\"message\":\"out of memory\"}}",
                    "data: [DONE]");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);

            await Assert.ThrowsAsync<AgentException>(() => session.SendAsync("Look around.", Token));
        }

        [Fact]
        public async Task A_turn_that_never_answers_gives_up()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model").Hangs();
            await using var session = new LmStudioSession(
                Options(turnTimeout: TimeSpan.FromMilliseconds(300)),
                save.Store,
                handler);

            await session.StartAsync(Token);

            await Assert.ThrowsAsync<AgentException>(() => session.SendAsync("Look around.", Token));
        }

        // ---- The tool loop ---------------------------------------------------------------------------

        [Fact]
        public async Task A_tool_call_is_run_and_its_answer_sent_back()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Calls("mcp__quest__get_state", "{}")
                .Says("Rowan stands at the ford.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal("Rowan stands at the ford.", result.Text);

            // The second request carries the tool's answer, which is how the model sees the world.
            Assert.Contains("Rowan", handler.Bodies[^1], StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_tool_that_fails_answers_with_a_sentence_rather_than_ending_the_turn()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Calls("mcp__quest__get_character", """{"name":"Bess"}""")
                .Says("Nobody by that name.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            var result = await session.SendAsync("Who is Bess?", Token);

            Assert.Equal("Nobody by that name.", result.Text);
            Assert.False(result.IsError);
        }

        [Fact]
        public async Task A_tool_call_with_unreadable_arguments_answers_rather_than_throwing()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Calls("mcp__quest__get_character", "{ not json")
                .Says("Let me try again.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);

            Assert.Equal("Let me try again.", (await session.SendAsync("Who?", Token)).Text);
        }

        [Fact]
        public async Task The_same_call_twice_in_a_turn_is_only_run_once()
        {
            // Measured in a real session: twenty byte-identical update_character calls in a single
            // turn, because the reply did not read like anything had happened. Running the write again
            // is the part that has to stop - the model repeating itself is a model to be answered, not
            // an instruction to write the memory twice.
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Calls("mcp__quest__add_memory", """{"character":"Rowan","text":"{This} crossed the ford."}""")
                .Calls("mcp__quest__record_event", """{"title":"The ford","detail":"Rowan crossed the ford.","characters":["Rowan"]}""")
                .Says("Rowan crosses.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            await session.SendAsync("Cross the ford.", Token);

            var story = save.Store.Story.Read().Entries;
            Assert.Single(story);
        }

        [Fact]
        public async Task A_suppressed_repeat_is_answered_with_what_the_first_call_said()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Calls("mcp__quest__get_state", "{}")
                .Calls("mcp__quest__get_state", "{}")
                .Says("Rowan stands at the ford.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            await session.SendAsync("Look around.", Token);

            // The third request is the one carrying the answer to the repeated call.
            Assert.Contains("already called get_state", handler.Bodies[^1], StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_suppressed_repeat_is_still_journalled()
        {
            // The journal answers "what did the narrator do", and doing this twice is a thing it did.
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Calls("mcp__quest__get_state", "{}")
                .Calls("mcp__quest__get_state", "{}")
                .Says("Rowan stands at the ford.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            await session.SendAsync("Look around.", Token);

            var calls = save.Store.Journal.Read().Entries
                .Where(entry => entry.Tool == "get_state")
                .ToList();

            Assert.Equal(2, calls.Count);
            Assert.False(calls[0].Failed);
            Assert.True(calls[1].Failed);
        }

        [Fact]
        public async Task Arguments_written_two_ways_are_still_the_same_call()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Calls("mcp__quest__get_character", """{"name":"Rowan"}""")
                .Calls("mcp__quest__get_character", "{ \"name\" : \"Rowan\" }")
                .Says("Rowan.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            await session.SendAsync("Who is Rowan?", Token);

            Assert.Contains("already called get_character", handler.Bodies[^1], StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("random_noun")]
        [InlineData("random_adjective")]
        [InlineData("roll")]
        public async Task A_tool_that_is_meant_to_answer_differently_is_never_suppressed(string tool)
        {
            // Two rolls for two blows are not one roll, and a narrator drawing seeds again wants seeds
            // it has not already had. Suppressing these would be a regression dressed as a fix.
            using var save = Seeded();
            var arguments = tool == "roll"
                ? """{"notation":"1d20","reason":"the leap"}"""
                : """{"count":3}""";

            var handler = new ScriptedHandler()
                .Models("a-model")
                .Calls($"mcp__quest__{tool}", arguments)
                .Calls($"mcp__quest__{tool}", arguments)
                .Says("Done.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            await session.SendAsync("Go.", Token);

            var calls = save.Store.Journal.Read().Entries.Where(entry => entry.Tool == tool).ToList();

            Assert.Equal(2, calls.Count);
            Assert.DoesNotContain(calls, entry => entry.Failed);
        }

        [Fact]
        public async Task A_repeat_on_the_next_turn_is_run_again()
        {
            // A new turn is a new situation; the world has moved since the last answer.
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Calls("mcp__quest__get_state", "{}")
                .Says("First.")
                .Calls("mcp__quest__get_state", "{}")
                .Says("Second.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            await session.SendAsync("Look around.", Token);
            await session.SendAsync("Look again.", Token);

            var calls = save.Store.Journal.Read().Entries.Where(entry => entry.Tool == "get_state").ToList();

            Assert.Equal(2, calls.Count);
            Assert.DoesNotContain(calls, entry => entry.Failed);
        }

        [Fact]
        public async Task A_model_that_only_ever_calls_tools_is_stopped()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model");

            for (var i = 0; i < 3; i++)
            {
                handler.Calls("mcp__quest__get_state", "{}");
            }

            await using var session = new LmStudioSession(Options(maxToolIterations: 3), save.Store, handler);

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.True(result.IsError);
            Assert.Contains("used tools 3 times", result.Text, StringComparison.Ordinal);
        }

        // ---- History ------------------------------------------------------------------------------------

        [Fact]
        public async Task A_second_turn_carries_the_first_one_with_it()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model").Says("First.").Says("Second.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            await session.SendAsync("One.", Token);
            await session.SendAsync("Two.", Token);

            Assert.Contains("One.", handler.Bodies[^1], StringComparison.Ordinal);
            Assert.Contains("First.", handler.Bodies[^1], StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_failed_turn_leaves_no_half_written_history_behind()
        {
            // An assistant message whose tool calls were never answered is rejected outright by
            // the next request, so a failed turn has to roll all the way back.
            using var save = Seeded();
            var handler = new ScriptedHandler()
                .Models("a-model")
                .Status(HttpStatusCode.InternalServerError, "{}")
                .Says("Recovered.");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            await Assert.ThrowsAnyAsync<Exception>(() => session.SendAsync("One.", Token));

            var result = await session.SendAsync("Two.", Token);

            Assert.Equal("Recovered.", result.Text);
            Assert.DoesNotContain("One.", handler.Bodies[^1], StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_system_prompt_leads_every_request()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model").Says("text");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            await session.SendAsync("Look around.", Token);

            Assert.Contains("You narrate.", handler.Bodies[^1], StringComparison.Ordinal);
        }

        [Fact]
        public async Task The_request_goes_to_the_chat_completions_endpoint()
        {
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model").Says("text");
            await using var session = new LmStudioSession(Options(), save.Store, handler);

            await session.StartAsync(Token);
            await session.SendAsync("Look around.", Token);

            Assert.Equal("/v1/chat/completions", handler.Paths[^1]);
        }

        // ---- Guards -------------------------------------------------------------------------------------

        [Fact]
        public void A_session_needs_options_and_a_store()
        {
            using var save = Seeded();

            Assert.Throws<ArgumentNullException>(() => new LmStudioSession(null!, save.Store));
            Assert.Throws<ArgumentNullException>(() => new LmStudioSession(Options(), null!));
        }

        [Fact]
        public async Task A_turn_needs_a_prompt()
        {
            using var save = Seeded();
            await using var session = new LmStudioSession(Options(), save.Store, new ScriptedHandler());

            await Assert.ThrowsAnyAsync<ArgumentException>(() => session.SendAsync(string.Empty, Token));
        }

        [Fact]
        public async Task Interrupting_a_session_with_nothing_in_flight_is_harmless()
        {
            using var save = Seeded();
            await using var session = new LmStudioSession(Options(), save.Store, new ScriptedHandler());

            await session.InterruptAsync();
        }

        [Fact]
        public async Task An_injected_handler_survives_the_session_that_used_it()
        {
            // The caller owns it: this session hands the same handler to the model-list call, so
            // disposing it with the client would break the next use of it.
            using var save = Seeded();
            var handler = new ScriptedHandler().Models("a-model");

            var session = new LmStudioSession(Options(), save.Store, handler);
            await session.StartAsync(Token);
            await session.DisposeAsync();

            handler.Models("still-usable");
            using var client = new HttpClient(handler, disposeHandler: false);
            using var response = await client.GetAsync("http://localhost:1234/v1/models", Token);

            Assert.True(response.IsSuccessStatusCode);
        }
    }
}
