using TerminalQuest.Agents;
using TerminalQuest.Agents.Claude;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Agents
{
    /// <summary>
    /// The Claude session driven end to end against a stand-in CLI.
    /// </summary>
    /// <remarks>
    /// A real child process, because that is the only way to reach the streaming state machine:
    /// <c>ClaudeSession</c> sets <c>FileName</c> from its options and builds the argument vector
    /// itself, so there is no seam short of an executable. Slower and more OS-dependent than the
    /// rest of the suite, hence its own category.
    /// <para>
    /// The stand-in is chosen through the environment, which is process-wide, so these run one at
    /// a time.
    /// </para>
    /// </remarks>
    [Collection(EnvironmentCollection.Name)]
    [Trait(Categories.Name, Categories.Integration)]
    public sealed class ClaudeSessionTests
    {
        /// <summary>Sets the stand-in's script for the life of one test.</summary>
        private sealed class Script : IDisposable
        {
            private readonly string? _previousScript;
            private readonly string? _previousArgv;

            public Script(string name)
            {
                _previousScript = Environment.GetEnvironmentVariable("TQ_FAKE_CLAUDE_SCRIPT");
                _previousArgv = Environment.GetEnvironmentVariable("TQ_FAKE_CLAUDE_ARGV");

                Folder = Path.Combine(Path.GetTempPath(), "TerminalQuest.Tests", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(Folder);
                ArgvPath = Path.Combine(Folder, "argv.txt");

                Environment.SetEnvironmentVariable("TQ_FAKE_CLAUDE_SCRIPT", name);
                Environment.SetEnvironmentVariable("TQ_FAKE_CLAUDE_ARGV", ArgvPath);
            }

            public string Folder { get; }

            public string ArgvPath { get; }

            public string[] Argv => File.Exists(ArgvPath) ? File.ReadAllLines(ArgvPath) : [];

            public void Dispose()
            {
                Environment.SetEnvironmentVariable("TQ_FAKE_CLAUDE_SCRIPT", _previousScript);
                Environment.SetEnvironmentVariable("TQ_FAKE_CLAUDE_ARGV", _previousArgv);

                try
                {
                    Directory.Delete(Folder, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        /// <summary>The stand-in CLI, built alongside this assembly and sitting beside it.</summary>
        private static string FakeClaude
        {
            get
            {
                var name = OperatingSystem.IsWindows() ? "tq-fake-claude.exe" : "tq-fake-claude";
                var path = Path.Combine(AppContext.BaseDirectory, name);

                Assert.True(
                    File.Exists(path),
                    $"The stand-in CLI was not built next to the tests. Expected it at {path}.");

                return path;
            }
        }

        private static ClaudeSessionOptions Options(TimeSpan? turnTimeout = null) => new()
        {
            Model = null,
            ExecutablePath = FakeClaude,
            AllowedTools = "mcp__quest__get_state",
            McpConfigJson = """{"mcpServers":{}}""",
            SystemPrompt = "You narrate.",
            StartupGracePeriod = TimeSpan.FromMilliseconds(200),
            TurnTimeout = turnTimeout ?? TimeSpan.FromSeconds(30),
        };

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        // ---- A turn end to end -----------------------------------------------------------------

        [Fact]
        public async Task A_turn_returns_the_text_the_model_produced()
        {
            using var script = new Script("reply");
            await using var session = new ClaudeSession(Options());

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal("Hello there", result.Text);
            Assert.False(result.IsError);
        }

        [Fact]
        public async Task The_deltas_assemble_into_the_same_text_the_result_reports()
        {
            // The documented contract: what the pane was shown is what the turn came to.
            using var script = new Script("reply");
            await using var session = new ClaudeSession(Options());

            var streamed = new List<string>();
            session.OnTextDelta += delta => { lock (streamed) { streamed.Add(delta); } };

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            lock (streamed)
            {
                Assert.Equal(result.Text, string.Concat(streamed));
            }
        }

        [Fact]
        public async Task Reasoning_never_reaches_the_caller()
        {
            // The stream carries thinking_delta blocks alongside the text; only text may escape.
            using var script = new Script("thinking");
            await using var session = new ClaudeSession(Options());

            var streamed = new List<string>();
            session.OnTextDelta += delta => { lock (streamed) { streamed.Add(delta); } };

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal("The road was empty.", result.Text);

            lock (streamed)
            {
                Assert.DoesNotContain("should describe", string.Concat(streamed), StringComparison.Ordinal);
            }
        }

        [Fact]
        public async Task A_failure_the_model_reports_is_a_result_rather_than_an_exception()
        {
            // Failures about the turn come back through IsError; only provider failures throw.
            using var script = new Script("error");
            await using var session = new ClaudeSession(Options());

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.True(result.IsError);
            Assert.Equal("something went wrong", result.Text);
        }

        [Fact]
        public async Task Usage_and_cost_are_carried_back_from_the_result()
        {
            using var script = new Script("usage");
            await using var session = new ClaudeSession(Options());

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal(0.0125, result.CostUsd);
            Assert.Equal(1234, result.DurationMs);
            Assert.Equal(11, result.InputTokens);
            Assert.Equal(22, result.OutputTokens);
            Assert.Equal(33, result.CacheReadTokens);
            Assert.Equal(44, result.CacheCreationTokens);
        }

        [Fact]
        public async Task A_result_with_no_usage_reports_zeroes_rather_than_failing()
        {
            using var script = new Script("reply");
            await using var session = new ClaudeSession(Options());

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal(0, result.InputTokens);
            Assert.Equal(0d, result.CostUsd);
        }

        [Fact]
        public async Task Unreadable_frames_do_not_derail_the_turn()
        {
            // The CLI's stdout is not something this process controls; a frame it cannot read is
            // skipped rather than being allowed to strand a turn that is still coming.
            using var script = new Script("noise");
            await using var session = new ClaudeSession(Options());

            await session.StartAsync(Token);
            var result = await session.SendAsync("Look around.", Token);

            Assert.Equal("survived", result.Text);
        }

        // ---- Session identity ---------------------------------------------------------------------

        [Fact]
        public async Task The_session_id_and_capabilities_are_taken_from_the_first_turn()
        {
            using var script = new Script("resession");
            await using var session = new ClaudeSession(Options());

            await session.StartAsync(Token);
            await session.SendAsync("First.", Token);

            Assert.Equal("session-1", session.SessionId);
            Assert.Contains("interrupt_receipt_v1", session.Capabilities);
            Assert.True(session.SupportsInterrupt);
        }

        [Fact]
        public async Task A_later_init_never_replaces_the_session_id()
        {
            // First writer wins: the id identifies the conversation, and a second one would mean
            // the rest of the session was attributed to the wrong thread of talk.
            using var script = new Script("resession");
            await using var session = new ClaudeSession(Options());

            await session.StartAsync(Token);
            await session.SendAsync("First.", Token);
            await session.SendAsync("Second.", Token);

            Assert.Equal("session-1", session.SessionId);
        }

        [Fact]
        public async Task Turns_are_answered_in_order()
        {
            using var script = new Script("resession");
            await using var session = new ClaudeSession(Options());

            await session.StartAsync(Token);

            Assert.Equal("turn 1", (await session.SendAsync("First.", Token)).Text);
            Assert.Equal("turn 2", (await session.SendAsync("Second.", Token)).Text);
        }

        // ---- Lifecycle ------------------------------------------------------------------------------

        [Fact]
        public async Task A_session_cannot_be_started_twice()
        {
            using var script = new Script("reply");
            await using var session = new ClaudeSession(Options());

            await session.StartAsync(Token);

            await Assert.ThrowsAsync<InvalidOperationException>(() => session.StartAsync(Token));
        }

        [Fact]
        public async Task A_provider_that_is_not_there_is_reported_as_an_agent_failure()
        {
            // This means no turn will ever succeed until something outside the game changes, which
            // is exactly the distinction AgentException carries.
            await using var session = new ClaudeSession(Options() with
            {
                ExecutablePath = Path.Combine(AppContext.BaseDirectory, "definitely-not-here.exe"),
            });

            await Assert.ThrowsAsync<AgentException>(() => session.StartAsync(Token));
        }

        [Fact]
        public async Task A_provider_that_dies_at_startup_is_reported_with_what_it_said()
        {
            using var script = new Script("die");
            await using var session = new ClaudeSession(Options());

            var exception = await Assert.ThrowsAsync<AgentException>(async () =>
            {
                await session.StartAsync(Token);
                await session.SendAsync("Look around.", Token);
            });

            Assert.Contains("fell over", exception.Message, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_turn_that_never_answers_gives_up_rather_than_hanging_forever()
        {
            using var script = new Script("hang");
            await using var session = new ClaudeSession(Options(turnTimeout: TimeSpan.FromMilliseconds(500)));

            await session.StartAsync(Token);

            await Assert.ThrowsAnyAsync<Exception>(() => session.SendAsync("Look around.", Token));
        }

        [Fact]
        public async Task A_disposed_session_refuses_further_turns()
        {
            using var script = new Script("reply");
            var session = new ClaudeSession(Options());

            await session.StartAsync(Token);
            await session.DisposeAsync();

            await Assert.ThrowsAsync<ObjectDisposedException>(() => session.SendAsync("Look around.", Token));
        }

        [Fact]
        public async Task Disposing_twice_is_harmless()
        {
            using var script = new Script("reply");
            var session = new ClaudeSession(Options());

            await session.StartAsync(Token);
            await session.DisposeAsync();
            await session.DisposeAsync();
        }

        [Fact]
        public async Task Interrupting_a_session_that_never_started_is_harmless()
        {
            await using var session = new ClaudeSession(Options());

            await session.InterruptAsync();
        }

        // ---- What the process was actually launched with -----------------------------------------------

        [Fact]
        public async Task The_child_is_launched_with_the_arguments_the_options_asked_for()
        {
            // The argument vector is where the session is stripped down to just the quest tools,
            // so it is worth confirming end to end rather than only where it is built.
            using var script = new Script("reply");
            await using var session = new ClaudeSession(Options());

            await session.StartAsync(Token);
            await session.SendAsync("Look around.", Token);

            var argv = script.Argv;

            Assert.Contains("--strict-mcp-config", argv);
            Assert.Contains("--disable-slash-commands", argv);
            Assert.Contains("mcp__quest__get_state", argv);
            Assert.Contains("dontAsk", argv);
        }

        [Fact]
        public async Task A_prompt_full_of_awkward_characters_survives_the_transport()
        {
            using var script = new Script("reply");
            await using var session = new ClaudeSession(Options());

            await session.StartAsync(Token);
            var result = await session.SendAsync("He said \"mind the {gap}\"\tand left.", Token);

            Assert.Equal("Hello there", result.Text);
        }
    }
}
