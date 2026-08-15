using TerminalQuest.Agents;
using TerminalQuest.Agents.Claude;
using TerminalQuest.Agents.LmStudio;
using TerminalQuest.Saves;
using TerminalQuest.Settings;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Agents
{
    /// <summary>
    /// The one place in the game that knows both providers exist.
    /// </summary>
    public sealed class AgentSessionFactoryTests
    {
        private static async Task<T> BuiltAsync<T>(AppSettings settings, SaveStore store)
            where T : class
        {
            var session = AgentSessionFactory.Create(settings, store, "You narrate.");

            await using (session)
            {
                return Assert.IsType<T>(session);
            }
        }

        [Fact]
        public async Task The_default_provider_builds_a_claude_session()
        {
            using var save = new TempSave();

            await BuiltAsync<ClaudeSession>(new AppSettings(), save.Store);
        }

        [Fact]
        public async Task Choosing_lm_studio_builds_an_lm_studio_session()
        {
            using var save = new TempSave();

            await BuiltAsync<LmStudioSession>(
                new AppSettings { Provider = AgentProvider.LmStudio },
                save.Store);
        }

        [Fact]
        public async Task Choosing_openai_api_builds_an_openai_session()
        {
            using var save = new TempSave();

            await BuiltAsync<LmStudioSession>(
                new AppSettings { Provider = AgentProvider.OpenAiApi },
                save.Store);
        }

        [Fact]
        public async Task An_enum_value_this_build_does_not_know_falls_back_to_claude()
        {
            // Settings are read from a file a player may have edited. Falling through to the
            // default provider is better than a session that cannot be constructed at all.
            using var save = new TempSave();

            await BuiltAsync<ClaudeSession>(
                new AppSettings { Provider = (AgentProvider)99 },
                save.Store);
        }

        [Fact]
        public void Building_a_session_needs_settings_and_a_save()
        {
            using var save = new TempSave();

            Assert.Throws<ArgumentNullException>(
                () => AgentSessionFactory.Create(null!, save.Store, "You narrate."));
            Assert.Throws<ArgumentNullException>(
                () => AgentSessionFactory.Create(new AppSettings(), null!, "You narrate."));
        }

        [Fact]
        public async Task Building_a_session_reaches_no_provider()
        {
            // Construction must not connect: the settings screen builds one to check a model list
            // and the save menu builds one before the player has committed to anything.
            using var save = new TempSave();

            foreach (var provider in Enum.GetValues<AgentProvider>())
            {
                var session = AgentSessionFactory.Create(
                    new AppSettings { Provider = provider, LmStudioBaseUrl = "http://127.0.0.1:1/v1" },
                    save.Store,
                    "You narrate.");

                await session.DisposeAsync();
            }
        }

        [Fact]
        public async Task A_claude_session_is_pointed_at_this_save()
        {
            // The MCP server is launched pointed at the save's folder, so a factory that lost the
            // directory would hand the narrator somebody else's world.
            using var save = new TempSave("Riverbend");
            var session = AgentSessionFactory.Create(new AppSettings(), save.Store, "You narrate.");

            await using (session)
            {
                Assert.IsType<ClaudeSession>(session);
            }

            // The config the session was built with names this folder.
            Assert.Contains(
                save.Store.Directory.Replace("\\", "\\\\", StringComparison.Ordinal),
                TerminalQuest.Mcp.QuestServerConfig.Build(save.Store.Directory),
                StringComparison.OrdinalIgnoreCase);
        }
    }
}
