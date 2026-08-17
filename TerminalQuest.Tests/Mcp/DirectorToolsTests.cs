using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    public sealed class DirectorToolsTests
    {
        private static TempSave SeededSave()
        {
            var save = new TempSave();
            var characters = new CharacterFile();
            var bess = new Character
            {
                Id = "c1",
                Name = "Bess",
                Kind = CharacterKind.Npc,
                Health = 10,
                MaxHealth = 10,
            };
            Secrets.Grant(bess, "the sealed cellar", "Bricked up after the fever.", 1, SecretStage.Dormant);
            characters.Characters.Add(bess);
            save.Store.WriteCharacters(characters);
            return save;
        }

        private static JsonElement Arguments(string json) =>
            JsonDocument.Parse(json).RootElement.Clone();

        [Fact]
        public void Promote_secret_promotes_dormant_secret_to_live()
        {
            using var save = SeededSave();

            var outcome = QuestTools.Invoke(save.Store, "promote_secret", Arguments("""
                {"character":"Bess","name":"the sealed cellar"}
                """));

            Assert.False(outcome.IsError);
            Assert.Contains("Promoted secret", outcome.Text);

            var characters = save.Store.ReadCharacters();
            var bess = SaveStore.FindCharacter(characters, "Bess");
            var secret = Secrets.Find(bess!, "the sealed cellar");

            Assert.NotNull(secret);
            Assert.Equal(SecretStage.Live, secret.Stage);
        }

        [Fact]
        public void Promote_secret_fails_if_already_live_or_missing()
        {
            using var save = SeededSave();

            // First promotion succeeds
            QuestTools.Invoke(save.Store, "promote_secret", Arguments("""
                {"character":"Bess","name":"the sealed cellar"}
                """));

            // Second promotion fails because it is no longer dormant
            var outcome = QuestTools.Invoke(save.Store, "promote_secret", Arguments("""
                {"character":"Bess","name":"the sealed cellar"}
                """));

            Assert.True(outcome.IsError);
        }

        [Fact]
        public void Ratify_claim_appends_ratified_ledger_entry()
        {
            using var save = new TempSave();
            save.Store.Ledger.Append(new LedgerEntry
            {
                Turn = 1,
                Speaker = "Bess",
                Claim = "The northern gate is locked.",
                Truth = ClaimTruth.True,
            });

            var outcome = QuestTools.Invoke(save.Store, "ratify_claim", Arguments("""
                {"sequence":1}
                """));

            Assert.False(outcome.IsError);
            Assert.Contains("Ratified claim #1", outcome.Text);

            var entries = save.Store.Ledger.Read().Entries;
            Assert.Equal(2, entries.Count);

            var ratification = entries[1];
            Assert.Equal(ClaimTruth.Ratified, ratification.Truth);
            Assert.Equal(1, ratification.Adjudicates);
            Assert.Equal("The northern gate is locked.", ratification.Claim);
        }

        [Fact]
        public void Emit_directive_writes_to_directive_file()
        {
            using var save = new TempSave();

            var outcome = QuestTools.Invoke(save.Store, "emit_directive", Arguments("""
                {"tone":"Mysterious and tense","pacing_note":"Have a stranger approach the inn."}
                """));

            Assert.False(outcome.IsError);
            Assert.True(save.Has("directive.json"));

            var directive = save.Store.ReadDirective();
            Assert.NotNull(directive);
            Assert.Equal("Mysterious and tense", directive.Tone);
            Assert.Equal("Have a stranger approach the inn.", directive.PacingNote);
            Assert.False(directive.Consumed);
        }

        [Fact]
        public void Get_unratified_claims_filters_out_ratified_claims()
        {
            using var save = new TempSave();
            save.Store.Ledger.Append(new LedgerEntry
            {
                Turn = 1,
                Speaker = "Rowan",
                Claim = "The river is frozen.",
                Truth = ClaimTruth.True,
            });
            save.Store.Ledger.Append(new LedgerEntry
            {
                Turn = 2,
                Speaker = "Bess",
                Claim = "Wolves prowl the forest.",
                Truth = ClaimTruth.True,
            });

            // Ratify claim #1
            QuestTools.Invoke(save.Store, "ratify_claim", Arguments("""{"sequence":1}"""));

            var outcome = QuestTools.Invoke(save.Store, "get_unratified_claims", Arguments("""{}"""));

            Assert.False(outcome.IsError);
            Assert.DoesNotContain("The river is frozen.", outcome.Text);
            Assert.Contains("Wolves prowl the forest.", outcome.Text);
        }

        [Fact]
        public void Tool_role_isolation_scopes_narrator_and_director()
        {
            var narratorAllowed = QuestTools.AllowedTools(ToolRole.Narrator);
            var directorAllowed = QuestTools.AllowedTools(ToolRole.Director);

            // Director tools only visible to Director
            Assert.DoesNotContain("mcp__quest__emit_directive", narratorAllowed);
            Assert.DoesNotContain("mcp__quest__ratify_claim", narratorAllowed);
            Assert.DoesNotContain("mcp__quest__promote_secret", narratorAllowed);
            Assert.DoesNotContain("mcp__quest__get_unratified_claims", narratorAllowed);

            Assert.Contains("mcp__quest__emit_directive", directorAllowed);
            Assert.Contains("mcp__quest__ratify_claim", directorAllowed);
            Assert.Contains("mcp__quest__promote_secret", directorAllowed);
            Assert.Contains("mcp__quest__get_unratified_claims", directorAllowed);

            // Both tools visible to Both
            Assert.Contains("mcp__quest__get_state", narratorAllowed);
            Assert.Contains("mcp__quest__get_state", directorAllowed);

            // Narrator-only tools not in Director
            Assert.DoesNotContain("mcp__quest__roll", directorAllowed);
            Assert.DoesNotContain("mcp__quest__record_claims", directorAllowed);
            Assert.Contains("mcp__quest__roll", narratorAllowed);
        }
    }
}
