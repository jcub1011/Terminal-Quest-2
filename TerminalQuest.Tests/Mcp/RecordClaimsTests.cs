using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    /// <summary>
    /// The claims list: what was said to the player, by whom, and what it gave away.
    /// </summary>
    /// <remarks>
    /// Two orderings are asserted rather than assumed, because both were chosen for which failure they
    /// leave behind. A bad entry refuses the whole call, so a turn is never half recorded; and the ledger
    /// is written before any secret is spent, so a crash between the two leaves a secret that is gated
    /// too strictly rather than one shared with no record of having been shared.
    /// </remarks>
    public sealed class RecordClaimsTests
    {
        private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static ToolOutcome Call(SaveStore store, string arguments) =>
            QuestTools.Invoke(store, "record_claims", Args(arguments));

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
                });
            }

            save.Store.WriteCharacters(file);
            save.Store.Touch(5);

            return save;
        }

        private static void Give(SaveStore store, string who, string secret, SecretStage stage)
        {
            var file = store.ReadCharacters();

            SaveStore.FindCharacter(file, who)!.Secrets.Add(new Secret
            {
                Name = secret,
                Stage = stage,
                Text = "Bricked up the fever winter.",
                Turn = 1,
            });

            store.WriteCharacters(file);
        }

        private static Secret SecretOf(SaveStore store, string who, string name) =>
            Secrets.Find(SaveStore.FindCharacter(store.ReadCharacters(), who)!, name)!;

        // ---- Recording -----------------------------------------------------------------------

        [Fact]
        public void A_claim_is_recorded_with_its_turn_and_its_speaker()
        {
            using var save = Scene();

            var outcome = Call(save.Store, """
                {"claims":[{"claim":"The bridge north of the Ford has been out since the flood.","speaker":"Bess"}]}
                """);

            Assert.False(outcome.IsError);

            var entry = Assert.Single(save.Store.Ledger.Read().Entries);

            Assert.Equal("The bridge north of the Ford has been out since the flood.", entry.Claim);
            Assert.Equal("Bess", entry.Speaker);
            Assert.Equal(5, entry.Turn);
            Assert.Equal(1, entry.Seq);
        }

        [Fact]
        public void Several_claims_become_several_entries_in_order()
        {
            // One entry per assertion, not one per turn. A paragraph naming a price, a road and a rumour
            // is three things the world is now committed to.
            using var save = Scene();

            var outcome = Call(save.Store, """
                {"claims":[
                  {"claim":"The ferry costs two coin."},
                  {"claim":"The road east is watched."},
                  {"claim":"The miller's daughter has not been seen since spring."}]}
                """);

            Assert.Contains("Recorded 3 claims", outcome.Text, StringComparison.Ordinal);
            Assert.Equal(
                ["The ferry costs two coin.", "The road east is watched.", "The miller's daughter has not been seen since spring."],
                save.Store.Ledger.Read().Entries.Select(entry => entry.Claim));
        }

        [Fact]
        public void One_claim_is_reported_in_the_singular()
        {
            using var save = Scene();

            Assert.Contains(
                "Recorded 1 claim.",
                Call(save.Store, """{"claims":[{"claim":"It is raining."}]}""").Text,
                StringComparison.Ordinal);
        }

        [Fact]
        public void A_claim_with_no_speaker_is_the_narrator_s_own_and_is_accepted()
        {
            // The world describing a room is speaking too, and most contradictions are its rather than
            // anybody's dialogue.
            using var save = Scene();

            Assert.False(Call(save.Store, """{"claims":[{"claim":"The hall smells of wet ash."}]}""").IsError);

            var entry = Assert.Single(save.Store.Ledger.Read().Entries);

            Assert.Empty(entry.Speaker);
            Assert.Empty(entry.SpeakerId);
        }

        [Theory]
        [InlineData("Narrator")]
        [InlineData("Narration")]
        [InlineData("narrator")]
        [InlineData("  DM  ")]
        [InlineData("Game Master")]
        [InlineData("you")]
        public void The_narrator_naming_itself_is_the_same_as_naming_nobody(string speaker)
        {
            // Measured in a real session: four of eleven calls were refused outright because a small
            // model filled in the optional speaker field with "Narrator", and the whole turn's ledger
            // went with them. The refusal named no way forward, so the model sent it again unchanged.
            using var save = Scene();

            var outcome = Call(save.Store, $$"""
                {"claims":[{"claim":"The hall smells of wet ash.","speaker":"{{speaker}}"}]}
                """);

            Assert.False(outcome.IsError);

            var entry = Assert.Single(save.Store.Ledger.Read().Entries);

            Assert.Empty(entry.Speaker);
            Assert.Empty(entry.SpeakerId);
        }

        [Fact]
        public void A_character_actually_called_Narrator_still_answers_to_their_own_name()
        {
            // The lookup runs first, so the alias list can never take a name away from somebody who
            // has it.
            using var save = Scene();

            var file = save.Store.ReadCharacters();
            file.Characters.Add(new Character
            {
                Id = file.TakeId(),
                Name = "Narrator",
                Kind = CharacterKind.Npc,
                Health = 10,
                MaxHealth = 10,
            });
            save.Store.WriteCharacters(file);

            Call(save.Store, """{"claims":[{"claim":"I saw nothing.","speaker":"Narrator"}]}""");

            var entry = Assert.Single(save.Store.Ledger.Read().Entries);

            Assert.Equal("Narrator", entry.Speaker);
            Assert.NotEmpty(entry.SpeakerId);
        }

        [Fact]
        public void A_speaker_nobody_answers_to_is_still_refused()
        {
            // Widening this to the narrator's own names must not turn every misspelling into a
            // silently unattributed claim.
            using var save = Scene();

            var outcome = Call(save.Store, """{"claims":[{"claim":"I saw nothing.","speaker":"Narratorr"}]}""");

            Assert.True(outcome.IsError);
            Assert.Empty(save.Store.Ledger.Read().Entries);
        }

        [Fact]
        public void The_speaker_is_recorded_by_name_and_by_id()
        {
            using var save = Scene();

            Call(save.Store, """{"claims":[{"claim":"I saw nothing.","speaker":"Bess"}]}""");

            var entry = Assert.Single(save.Store.Ledger.Read().Entries);
            var bess = SaveStore.FindCharacter(save.Store.ReadCharacters(), "Bess")!;

            Assert.Equal("Bess", entry.Speaker);
            Assert.Equal(bess.Id, entry.SpeakerId);
        }

        [Fact]
        public void A_recorded_claim_still_finds_its_speaker_after_a_rename()
        {
            // Why both fields exist. The name is what the player heard and is never rewritten; the id is
            // what a later pass joins on.
            using var save = Scene();
            Call(save.Store, """{"claims":[{"claim":"I saw nothing.","speaker":"Bess"}]}""");

            QuestTools.Invoke(save.Store, "set_character", Args("""
                {"name":"Bess","new_name":"Bessa of the Ford"}
                """));

            var entry = Assert.Single(save.Store.Ledger.Read().Entries);
            var renamed = SaveStore.FindCharacterById(save.Store.ReadCharacters(), entry.SpeakerId);

            Assert.Equal("Bess", entry.Speaker);
            Assert.Equal("Bessa of the Ford", renamed?.Name);
        }

        [Fact]
        public void The_claim_text_is_trimmed()
        {
            using var save = Scene();

            Call(save.Store, """{"claims":[{"claim":"  It is raining.  "}]}""");

            Assert.Equal("It is raining.", Assert.Single(save.Store.Ledger.Read().Entries).Claim);
        }

        // ---- Truth status --------------------------------------------------------------------

        [Fact]
        public void A_missing_truth_status_is_recorded_as_true()
        {
            // The narrator's ordinary assertion is a true one, and demanding the flag every time would
            // cost tokens to say what is already the case.
            using var save = Scene();

            Call(save.Store, """{"claims":[{"claim":"It is raining."}]}""");

            Assert.Equal(ClaimTruth.True, Assert.Single(save.Store.Ledger.Read().Entries).Truth);
        }

        [Fact]
        public void A_lie_is_recorded_as_a_lie_rather_than_as_a_fault()
        {
            // The distinction the ledger exists to keep. A deceptive character is not a consistency bug,
            // and a lie recorded as one could never be paid off later.
            using var save = Scene();

            Call(save.Store, """{"claims":[{"claim":"I was home all evening.","speaker":"Tam","truth":"lie"}]}""");

            Assert.Equal(ClaimTruth.Lie, Assert.Single(save.Store.Ledger.Read().Entries).Truth);
        }

        [Fact]
        public void An_honest_mistake_is_told_apart_from_a_lie()
        {
            using var save = Scene();

            Call(save.Store, """{"claims":[{"claim":"The bridge is sound.","speaker":"Bess","truth":"mistaken"}]}""");

            Assert.Equal(ClaimTruth.Mistaken, Assert.Single(save.Store.Ledger.Read().Entries).Truth);
        }

        [Fact]
        public void A_boolean_truth_status_is_tolerated()
        {
            // Models send true where the schema asks for "true", and refusing that would cost a turn to
            // no purpose - the judgement the number and boolean readers already make.
            using var save = Scene();

            Assert.False(Call(save.Store, """{"claims":[{"claim":"It is raining.","truth":true}]}""").IsError);
            Assert.Equal(ClaimTruth.True, Assert.Single(save.Store.Ledger.Read().Entries).Truth);
        }

        [Fact]
        public void The_word_false_is_taken_to_mean_a_lie()
        {
            using var save = Scene();

            Assert.False(Call(save.Store, """{"claims":[{"claim":"I was home.","truth":"false"}]}""").IsError);
            Assert.Equal(ClaimTruth.Lie, Assert.Single(save.Store.Ledger.Read().Entries).Truth);
        }

        [Fact]
        public void A_truth_status_that_is_not_one_of_the_three_is_refused_and_lists_them()
        {
            using var save = Scene();

            var outcome = Call(save.Store, """{"claims":[{"claim":"It is raining.","truth":"probably"}]}""");

            Assert.True(outcome.IsError);
            Assert.Contains("probably", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("true, lie or mistaken", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_narrator_cannot_record_a_claim_as_unverified()
        {
            // Unverified is the game's to write, for the player's own line. A narrator that could reach it
            // would have a way to record an assertion as nobody's responsibility.
            using var save = Scene();

            Assert.True(Call(save.Store, """{"claims":[{"claim":"It is raining.","truth":"unverified"}]}""").IsError);
        }

        [Fact]
        public void The_narrator_cannot_record_a_claim_as_a_contradiction()
        {
            // A finding rather than a stance anybody takes, and it arrives as a new entry naming the old
            // one rather than as a label chosen at the time.
            using var save = Scene();

            Assert.True(Call(save.Store, """{"claims":[{"claim":"It is raining.","truth":"contradiction"}]}""").IsError);
        }

        // ---- Refusals ------------------------------------------------------------------------

        [Fact]
        public void record_claims_needs_at_least_one_claim()
        {
            using var save = Scene();

            Assert.True(Call(save.Store, """{"claims":[]}""").IsError);
            Assert.True(Call(save.Store, "{}").IsError);
            Assert.True(Call(save.Store, """{"claims":"a sentence"}""").IsError);
        }

        [Fact]
        public void A_claim_with_no_text_refuses_the_whole_call_and_says_which_entry()
        {
            using var save = Scene();

            var outcome = Call(save.Store, """
                {"claims":[{"claim":"The ferry costs two coin."},{"claim":"   "}]}
                """);

            Assert.True(outcome.IsError);
            Assert.Contains("Claim 2 has no text", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Nothing_is_written_when_one_entry_is_bad()
        {
            // The reason the entries are built in full before any is appended: a half-recorded turn is
            // worse than a refused call the narrator can retry.
            using var save = Scene();

            Call(save.Store, """
                {"claims":[{"claim":"The ferry costs two coin."},{"claim":""}]}
                """);

            Assert.Empty(save.Store.Ledger.Read().Entries);
        }

        [Fact]
        public void A_speaker_nobody_answers_to_is_refused_and_offers_the_way_out()
        {
            using var save = Scene();

            var outcome = Call(save.Store, """{"claims":[{"claim":"I saw nothing.","speaker":"Nobody"}]}""");

            Assert.True(outcome.IsError);
            Assert.Contains("leave the speaker out", outcome.Text, StringComparison.Ordinal);
            Assert.Empty(save.Store.Ledger.Read().Entries);
        }

        // ---- Spending a secret ---------------------------------------------------------------

        [Fact]
        public void A_claim_that_reveals_a_live_secret_spends_it()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            var outcome = Call(save.Store, """
                {"claims":[{"claim":"The cellar was bricked up the fever winter.","speaker":"Bess","reveals":"the sealed cellar"}]}
                """);

            Assert.False(outcome.IsError);
            Assert.Contains("common knowledge now", outcome.Text, StringComparison.Ordinal);
            Assert.Equal(SecretStage.Spent, SecretOf(save.Store, "Bess", "the sealed cellar").Stage);
        }

        [Fact]
        public void The_revealed_secret_is_named_on_the_ledger_entry()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            Call(save.Store, """
                {"claims":[{"claim":"It was bricked up.","speaker":"Bess","reveals":"the sealed cellar"}]}
                """);

            Assert.Equal("the sealed cellar", Assert.Single(save.Store.Ledger.Read().Entries).Reveals);
        }

        [Fact]
        public void Revealing_spends_it_for_every_live_holder()
        {
            // Spent means the player knows, which is a fact about the player and not about who happened to
            // say it. Leaving Tam's copy live would keep the gate refusing over something already out.
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);
            Give(save.Store, "Tam", "the sealed cellar", SecretStage.Live);

            Call(save.Store, """
                {"claims":[{"claim":"It was bricked up.","speaker":"Bess","reveals":"the sealed cellar"}]}
                """);

            Assert.Equal(SecretStage.Spent, SecretOf(save.Store, "Bess", "the sealed cellar").Stage);
            Assert.Equal(SecretStage.Spent, SecretOf(save.Store, "Tam", "the sealed cellar").Stage);
        }

        [Fact]
        public void A_claim_that_reveals_a_dormant_secret_leaves_it_dormant()
        {
            // The narrator was never handed it, so it cannot have revealed it. A name colliding with
            // something asleep is a mislabel.
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Dormant);

            var outcome = Call(save.Store, """
                {"claims":[{"claim":"It was bricked up.","speaker":"Bess","reveals":"the sealed cellar"}]}
                """);

            Assert.False(outcome.IsError);
            Assert.Equal(SecretStage.Dormant, SecretOf(save.Store, "Bess", "the sealed cellar").Stage);
        }

        [Fact]
        public void A_secret_already_spent_is_reported_without_error()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Spent);

            var outcome = Call(save.Store, """
                {"claims":[{"claim":"It was bricked up.","speaker":"Bess","reveals":"the sealed cellar"}]}
                """);

            Assert.False(outcome.IsError);
            Assert.Contains("already common knowledge", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_claim_that_reveals_nothing_on_record_is_still_recorded_and_says_nothing_changed()
        {
            // Nothing is refused over a mislabel: the claims were already said to the player and are
            // binding whether or not this tool understood the name attached to them.
            using var save = Scene();

            var outcome = Call(save.Store, """
                {"claims":[{"claim":"It was bricked up.","reveals":"a secret nobody has"}]}
                """);

            Assert.False(outcome.IsError);
            Assert.Contains("nothing changed hands", outcome.Text, StringComparison.Ordinal);
            Assert.Single(save.Store.Ledger.Read().Entries);
        }

        [Fact]
        public void One_secret_named_by_two_claims_is_reported_once()
        {
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            var outcome = Call(save.Store, """
                {"claims":[
                  {"claim":"It was bricked up.","speaker":"Bess","reveals":"the sealed cellar"},
                  {"claim":"Nobody has opened it since.","speaker":"Bess","reveals":"The Sealed Cellar"}]}
                """);

            var mentions = outcome.Text.Split("common knowledge now").Length - 1;

            Assert.Equal(1, mentions);
        }

        [Fact]
        public void Spending_a_secret_lifts_the_refusal_it_was_causing()
        {
            // The end-to-end loop, and the only test that exercises the gate, the rule, the tool and the
            // derivation together.
            using var save = Scene();
            Give(save.Store, "Bess", "the sealed cellar", SecretStage.Live);

            Assert.False(QuestTools.Invoke(save.Store, "get_character", Args("""{"name":"Bess"}""")).IsError);
            Assert.True(QuestTools.Invoke(save.Store, "get_character", Args("""{"name":"Tam"}""")).IsError);

            Call(save.Store, """
                {"claims":[{"claim":"It was bricked up.","speaker":"Bess","reveals":"the sealed cellar"}]}
                """);

            Assert.False(QuestTools.Invoke(save.Store, "get_character", Args("""{"name":"Tam"}""")).IsError);
        }
    }
}
