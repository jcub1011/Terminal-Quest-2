using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    /// <summary>
    /// The lifecycle gate: which secrets can leave the save layer, and when a fetch is refused outright.
    /// </summary>
    /// <remarks>
    /// These run through <c>QuestTools.Invoke</c> rather than against the gate directly, because the
    /// guarantee being checked is about what a tool call can return - not about what a function returns
    /// when called correctly. A gate that worked but was bypassed by one handler would pass the second
    /// kind of test and fail the first.
    /// </remarks>
    public sealed class SecretGateTests
    {
        /// <summary>
        /// A sentinel that could not plausibly be generated: if it appears in a tool result, it came out
        /// of a secret.
        /// </summary>
        private const string Sentinel = "ZZQX-secret-detail-must-not-escape-ZZQX";

        private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static ToolOutcome Call(SaveStore store, string name, string arguments = "{}") =>
            QuestTools.Invoke(store, name, Args(arguments));

        /// <summary>A save with a player, a place, and two NPCs standing in it.</summary>
        private static TempSave Scene()
        {
            var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", "A quiet sort.", ClassTemplates.All[0], "The Ford");

            var file = save.Store.ReadCharacters();

            foreach (var name in new[] { "Bess", "Tam" })
            {
                file.Characters.Add(new Character
                {
                    Id = file.TakeId(),
                    Name = name,
                    Kind = CharacterKind.Npc,
                    Health = 10,
                    MaxHealth = 10,
                    Description = $"{name} of the Ford.",
                });
            }

            save.Store.WriteCharacters(file);
            save.Store.Touch(1);

            return save;
        }

        private static void Give(SaveStore store, string who, string secret, SecretStage stage)
        {
            var file = store.ReadCharacters();
            var character = SaveStore.FindCharacter(file, who)!;

            character.Secrets.Add(new Secret
            {
                Name = secret,
                Stage = stage,
                Text = Sentinel,
                Turn = 1,
            });

            store.WriteCharacters(file);
        }

        // ---- Dormant is returned by nothing ---------------------------------------------------

        [Theory]
        [MemberData(nameof(EveryToolName))]
        public void A_dormant_secret_is_returned_by_nothing(string tool)
        {
            // The strongest assertion in the suite, and the only one that covers a tool nobody has
            // written yet. A dormant secret is not filtered out of an assembled context - nothing
            // yields one - so this sweeps every advertised tool rather than the two that could.
            //
            // The arguments are a superset satisfying most schemas at once, so that as many tools as
            // possible actually run rather than refusing for want of a parameter. The ones that still
            // refuse are covered anyway: a refusal message must not carry a secret either.
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Dormant);

            var outcome = Call(save.Store, tool, """
                {"name":"Bess","character":"Bess","about":"the sealed cellar","notation":"d20",
                 "reason":"listening at the door","title":"A night at the Ford","detail":"Nothing happened.",
                 "text":"{This} heard nothing.","destination":"The Ford","item":"rope","amount":1,
                 "attribute":"Wisdom","property":"name","value":"Bess","score":12,"count":3,
                 "claims":[{"claim":"Bess said nothing.","speaker":"Bess"}]}
                """);

            Assert.DoesNotContain(Sentinel, outcome.Text, StringComparison.Ordinal);
        }

        public static TheoryData<string> EveryToolName()
        {
            var data = new TheoryData<string>();

            foreach (var tool in QuestTools.Definitions)
            {
                data.Add(tool.Name);
            }

            return data;
        }

        [Fact]
        public void A_dormant_secret_does_not_reach_the_fetch_that_names_its_holder()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Dormant);

            Assert.DoesNotContain(
                "the sealed cellar",
                Call(save.Store, "get_character", """{"name":"Bess"}""").Text,
                StringComparison.Ordinal);
        }

        // ---- Live reaches its holder and nobody else -----------------------------------------

        [Fact]
        public void A_live_secret_reaches_a_fetch_naming_its_holder()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            var outcome = Call(save.Store, "get_character", """{"name":"Bess"}""");

            Assert.False(outcome.IsError);
            Assert.Contains(Sentinel, outcome.Text, StringComparison.Ordinal);
            Assert.Contains("the sealed cellar", outcome.Text, StringComparison.Ordinal);
        }



        [Fact]
        public void A_live_secret_does_not_reach_a_fetch_naming_anybody_else()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            Assert.DoesNotContain(
                Sentinel,
                Call(save.Store, "get_character", """{"name":"Tam"}""").Text,
                StringComparison.Ordinal);
        }

        [Fact]
        public void The_player_s_own_record_carries_no_other_character_s_secret()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            Assert.DoesNotContain(
                Sentinel,
                Call(save.Store, "get_state").Text,
                StringComparison.Ordinal);
        }

        // ---- Spent is shared -----------------------------------------------------------------

        [Fact]
        public void A_spent_secret_reaches_a_fetch_naming_anybody()
        {
            // Spent means the player was told, which is a fact about the player rather than about who
            // happened to say it. A character still protecting it would be protecting nothing.
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Spent);

            var outcome = Call(save.Store, "get_character", """{"name":"Tam"}""");

            Assert.Contains(Sentinel, outcome.Text, StringComparison.Ordinal);
            Assert.Contains("Common knowledge now", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_character_with_nothing_reads_exactly_as_they_did_before_secrets_existed()
        {
            using var save = Scene();

            var outcome = Call(save.Store, "get_character", """{"name":"Tam"}""");

            Assert.DoesNotContain("secret", outcome.Text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Common knowledge", outcome.Text, StringComparison.Ordinal);
        }

        // ---- Divergence ----------------------------------------------------------------------

        [Fact]
        public void A_divergent_fetch_is_refused_and_says_who_was_read()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            Call(save.Store, "get_character", """{"name":"Bess"}""");
            var outcome = Call(save.Store, "get_character", """{"name":"Tam"}""");

            Assert.True(outcome.IsError);
            Assert.Contains("Bess", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("Tam", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("next one", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_divergent_fetch_returns_nothing_of_the_character_it_refused()
        {
            // The whole fetch is refused rather than answered without the secrets. Handing over a
            // complete-looking character with the secrets quietly removed would have the narrator voice
            // them as fully informed - the exact failure the gate exists to prevent, arriving through
            // the gate.
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            Call(save.Store, "get_character", """{"name":"Bess"}""");
            var outcome = Call(save.Store, "get_character", """{"name":"Tam"}""");

            Assert.DoesNotContain("Tam of the Ford", outcome.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("HP", outcome.Text, StringComparison.Ordinal);
        }


        [Fact]
        public void Reading_the_same_character_repeatedly_is_never_refused()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            Assert.False(Call(save.Store, "get_character", """{"name":"Bess"}""").IsError);
            Assert.False(Call(save.Store, "get_character", """{"name":"Bess"}""").IsError);
        }

        [Fact]
        public void A_refused_fetch_does_not_count_as_having_been_read()
        {
            // If a refusal counted, the first one of a turn would become permanent: the narrator would
            // be told to try Tam next turn, and then refused for having tried.
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);
            Give(save.Store, "Tam", "the innkeeper's brother", SecretStage.Live);

            Call(save.Store, "get_character", """{"name":"Bess"}""");
            Assert.True(Call(save.Store, "get_character", """{"name":"Tam"}""").IsError);

            // Bess is still readable. Had the refused Tam fetch been counted, she would now be blocked
            // by a fetch that never happened.
            Assert.False(Call(save.Store, "get_character", """{"name":"Bess"}""").IsError);
        }

        [Fact]
        public void Only_this_turn_s_fetches_count()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            Call(save.Store, "get_character", """{"name":"Bess"}""");
            save.Store.Touch(2);

            Assert.False(Call(save.Store, "get_character", """{"name":"Tam"}""").IsError);
        }

        [Fact]
        public void Nothing_is_refused_when_no_secret_is_in_play()
        {
            using var save = Scene();

            Call(save.Store, "get_character", """{"name":"Bess"}""");

            Assert.False(Call(save.Store, "get_character", """{"name":"Tam"}""").IsError);
        }

        [Fact]
        public void A_spent_secret_stops_blocking_the_fetch_it_blocked()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            Call(save.Store, "get_character", """{"name":"Bess"}""");
            Assert.True(Call(save.Store, "get_character", """{"name":"Tam"}""").IsError);

            // Told to the player, so there is nothing left to keep Tam away from.
            var file = save.Store.ReadCharacters();
            Secrets.Spend(SaveStore.FindCharacter(file, "Bess")!, "the sealed cellar");
            save.Store.WriteCharacters(file);

            Assert.False(Call(save.Store, "get_character", """{"name":"Tam"}""").IsError);
        }

        // ---- What the gate costs -------------------------------------------------------------

        [Fact]
        public void A_fetch_naming_nobody_on_record_fails_with_its_own_message()
        {
            // A call about to fail on its own terms should say so, rather than being refused for a
            // reason that would only confuse the narrator further.
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);
            Call(save.Store, "get_character", """{"name":"Bess"}""");

            var outcome = Call(save.Store, "get_character", """{"name":"Nobody"}""");

            Assert.True(outcome.IsError);
            Assert.Contains("There is no character named 'Nobody'", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_tool_that_is_not_a_knowledge_fetch_reads_neither_the_roster_nor_the_journal()
        {
            // Asserts the early return, and so that the gate costs nothing on the great majority of
            // calls: with an unreadable roster and an unreadable journal, a word draw still works.
            using var save = new TempSave();
            save.WriteRaw("characters.json", "{ not json");
            save.WriteRaw("journal.jsonl", "{ not json\n");

            Assert.False(Call(save.Store, "random_noun").IsError);
        }

        [Fact]
        public void Every_knowledge_fetch_is_a_tool_that_exists_and_declares_the_argument_it_is_read_by()
        {
            // The closed list is data so that it can be checked. A fetch named here that no longer
            // exists, or one whose subject argument was renamed, would otherwise silently stop being
            // gated at all.
            foreach (var (tool, argument) in SecretGate.KnowledgeFetches)
            {
                var definition = Assert.Single(QuestTools.Definitions, candidate => candidate.Name == tool);

                using var schema = JsonDocument.Parse(definition.InputSchema);

                Assert.True(
                    schema.RootElement.GetProperty("properties").TryGetProperty(argument, out _),
                    $"{tool} does not declare '{argument}'.");

                Assert.Contains(
                    argument,
                    schema.RootElement.GetProperty("required").EnumerateArray().Select(entry => entry.GetString()));
            }
        }

        [Fact]
        public void get_state_is_not_a_knowledge_fetch()
        {
            // It renders the player through a renderer that carries no secrets, so it hands nothing
            // over - and listing it would have every session's opening call poison the turn's history.
            Assert.DoesNotContain("get_state", SecretGate.KnowledgeFetches.Keys);
        }
    }
}
