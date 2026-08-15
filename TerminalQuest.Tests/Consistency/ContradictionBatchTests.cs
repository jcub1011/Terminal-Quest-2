using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Consistency
{
    /// <summary>
    /// The consistency check, run as a batch job over a played session's logs rather than in live play.
    /// </summary>
    /// <remarks>
    /// Contradiction is a bug to prevent structurally, not a mechanic to support: the world does not
    /// retcon, and a narrated self-correction is worse than the original error because it tells the
    /// player the world is not stable. So these assertions exist to catch one out of band, after the
    /// fact, rather than to give the game something to apologise for mid-scene.
    /// <para>
    /// <b>What this cannot check, and why, because a green suite should not imply a guarantee it does not
    /// give.</b>
    /// </para>
    /// <para>
    /// <em>That no fetch returned a dormant secret.</em> Not expressible over the log at all: the journal
    /// records a call's inputs, never its output, and recording outputs would write every secret and
    /// every hidden roll into a plain text file the player can open - a worse leak than the one under
    /// test. It lives instead as a sweep over every advertised tool in <c>SecretGateTests</c>, which is
    /// stronger anyway: it holds for every log that could ever be written rather than one that happened
    /// to be.
    /// </para>
    /// <para>
    /// <em>That no turn produced prose without a claims list.</em> Also not expressible: the journal holds
    /// no prose, so a turn that read the world and narrated nothing is indistinguishable here from a turn
    /// that narrated and forgot. The live check in the game is the mechanism, because that is the only
    /// place both facts are in hand at once. What is checked below is the weaker, real property - that a
    /// turn which <em>did</em> record claims recorded them coherently.
    /// </para>
    /// <para>
    /// <em>That no ratified fact was ever negated.</em> There is no ratification yet, so the assertion
    /// would pass over an empty set and say nothing. Deliberately not written rather than written
    /// vacuously.
    /// </para>
    /// <para>
    /// <em>That every claim recorded true was consistent with canon as of the turn it was said.</em> Two
    /// things are missing and both belong to the Director. There is no canon: ratification is what
    /// promotes a claim out of the binding-but-inert tier into something else can be built on, so with
    /// nothing ratifying there is no second thing for a claim to be checked against - the assertion has no
    /// subject. And even given canon, comparing two pieces of free prose for agreement is a judgement
    /// rather than an assertion, which is to say a model call; the Director is the thing that can make
    /// one, which is why the design places this after it exists.
    /// </para>
    /// <para>
    /// This was briefly written as an unconditional failure under the <c>KnownBug</c> trait, to keep the
    /// debt visible. That was a mistake worth recording so it is not repeated: the trait documents itself
    /// as marking a test that asserts what the code <em>should</em> do and therefore fails today, and a
    /// test that can never go green by fixing code - only by being rewritten once a feature exists - is
    /// not that. It also made a genuinely broken suite indistinguishable from a healthy one, since the
    /// filter that hides known bugs had exactly one entry to hide. The debt is recorded here instead,
    /// beside the other two, which is what was done for those in the first place.
    /// </para>
    /// </remarks>
    public sealed class ContradictionBatchTests
    {
        private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

        /// <summary>
        /// A session played through the tools, so that the audit below runs over a real log rather than a
        /// hand-written one.
        /// </summary>
        private static TempSave Played()
        {
            var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", "A quiet sort.", ClassTemplates.All[0], "The Ford");

            Turn(save, 1,
                ("set_location", """{"name":"The Mill","description":"A wheel turns in the race."}"""),
                ("set_character", """{"name":"Bess","description":"Keeps the inn.","health":10,"max_health":10}"""),
                ("grant_secret", """{"character":"Bess","name":"the sealed cellar","detail":"Bricked up the fever winter."}"""),
                ("get_character", """{"name":"Bess"}"""),
                ("record_claims", """{"claims":[{"claim":"The mill wheel still turns.","speaker":"Bess"}]}"""));

            Turn(save, 2,
                ("set_location", """{"name":"The Mill","description":"The roof has fallen in since."}"""),
                ("set_character", """{"name":"Bess","description":"Lost a brother to the fever."}"""),
                ("recall", """{"character":"Bess"}"""),
                ("record_claims", """
                    {"claims":[
                      {"claim":"The cellar was bricked up the fever winter.","speaker":"Bess","reveals":"the sealed cellar"},
                      {"claim":"I was nowhere near the mill.","speaker":"Bess","truth":"lie"}]}
                    """));

            Turn(save, 3,
                ("get_character", """{"name":"Rowan"}"""),
                ("record_claims", """{"claims":[{"claim":"The ford runs high after rain."}]}"""));

            return save;
        }

        private static void Turn(TempSave save, int turn, params (string Tool, string Arguments)[] calls)
        {
            save.Store.Touch(turn);

            // The player's own line, which the game records without asking anybody whether it was true.
            save.Store.Ledger.Append(new LedgerEntry
            {
                Turn = turn,
                Speaker = "Rowan",
                SpeakerId = SaveStore.Player(save.Store.ReadCharacters())?.Id ?? string.Empty,
                Claim = $"a line the player typed on turn {turn}",
                Truth = ClaimTruth.Unverified,
            });

            foreach (var (tool, arguments) in calls)
            {
                QuestTools.Invoke(save.Store, tool, Args(arguments));
            }
        }

        // ---- The log itself ------------------------------------------------------------------

        [Fact]
        public void The_journal_sequence_climbs_with_no_gaps_and_no_duplicates()
        {
            // Also the backstop for the one hole the append path admits: the sequence is allocated from a
            // window at the end of the file, so a hand-edit burying a higher number further back could
            // reissue one. Nothing prevents that; this detects it.
            using var save = Played();

            var read = save.Store.Journal.Read();

            Assert.Equal(0, read.Malformed);
            Assert.Equal(
                Enumerable.Range(1, read.Entries.Count),
                read.Entries.Select(entry => entry.Seq));
        }

        [Fact]
        public void The_ledger_sequence_climbs_with_no_gaps_and_no_duplicates()
        {
            using var save = Played();

            var read = save.Store.Ledger.Read();

            Assert.Equal(0, read.Malformed);
            Assert.Equal(
                Enumerable.Range(1, read.Entries.Count),
                read.Entries.Select(entry => entry.Seq));
        }

        [Fact]
        public void Neither_log_ever_goes_back_in_time()
        {
            using var save = Played();

            foreach (var turns in new[]
            {
                save.Store.Journal.Read().Entries.Select(entry => entry.Turn),
                save.Store.Ledger.Read().Entries.Select(entry => entry.Turn),
            })
            {
                var previous = 0;

                foreach (var turn in turns)
                {
                    Assert.True(turn >= previous, $"turn {turn} followed turn {previous}.");
                    previous = turn;
                }
            }
        }

        // ---- Canon is extended, never negated ------------------------------------------------

        [Fact]
        public void Every_description_ever_asserted_is_still_on_record()
        {
            // The assertion the journal was bought for, and the Phase 1 subset of "consistent with canon":
            // every description the narrator ever asserted must still be findable in the document. It is
            // not a test of the extend function against itself - the reference is what is on disk, so this
            // also catches a hand-edit, a lost update between the two processes, and any tool added later
            // that assigns where it should append.
            using var save = Played();

            var characters = save.Store.ReadCharacters();
            var locations = save.Store.ReadLocations();

            foreach (var asserted in AssertedDescriptions(save.Store))
            {
                var current = asserted.IsPlace
                    ? SaveStore.FindLocation(locations, asserted.Subject)?.Description
                    : SaveStore.FindCharacter(characters, asserted.Subject)?.Description;

                Assert.NotNull(current);
                Assert.Contains(asserted.Text, current, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void The_audit_actually_has_descriptions_to_check()
        {
            // Guards the test above from passing because it found nothing. An audit over an empty set is
            // the failure mode of every batch check, and the one that looks most like success.
            using var save = Played();

            Assert.Equal(4, AssertedDescriptions(save.Store).Count);
        }

        [Fact]
        public void An_overwritten_description_is_caught()
        {
            // Proves the audit has teeth by committing the contradiction it exists to find. Writing the
            // document directly is the only way to do it: no tool will negate a description any more,
            // which is the point - so this stands in for a hand-edit or a lost update.
            using var save = Played();

            var characters = save.Store.ReadCharacters();
            SaveStore.FindCharacter(characters, "Bess")!.Description = "Actually a stranger here.";
            save.Store.WriteCharacters(characters);

            var locations = save.Store.ReadLocations();

            var negated = AssertedDescriptions(save.Store).Where(asserted =>
            {
                var current = asserted.IsPlace
                    ? SaveStore.FindLocation(locations, asserted.Subject)?.Description
                    : SaveStore.FindCharacter(characters, asserted.Subject)?.Description;

                return current is null || !current.Contains(asserted.Text, StringComparison.Ordinal);
            });

            Assert.Equal(2, negated.Count());
        }

        /// <summary>
        /// Every description the narrator asserted, replayed out of the journal.
        /// </summary>
        /// <remarks>
        /// Refused calls are skipped: a call the handler turned back asserted nothing, which is why the
        /// journal records an outcome. Renames are ignored - a description is checked against whoever
        /// answers to that name now, and a renamed subject simply is not found by its old name, so the
        /// replay reads the subject from the entry rather than trying to track identity through the log.
        /// </remarks>
        private static List<(bool IsPlace, string Subject, string Text)> AssertedDescriptions(SaveStore store)
        {
            var asserted = new List<(bool IsPlace, string Subject, string Text)>();

            foreach (var entry in store.Journal.Read().Entries)
            {
                if (entry.Failed)
                {
                    continue;
                }

                var isPlace = entry.Tool is "set_location";
                var isPerson = entry.Tool is "set_character";

                if (!isPlace && !isPerson)
                {
                    continue;
                }

                if (QuestTools.Text(entry.Arguments, "name") is not { Length: > 0 } subject)
                {
                    continue;
                }

                var text = QuestTools.Text(entry.Arguments, "description");

                if (text is { Length: > 0 })
                {
                    asserted.Add((isPlace, subject, text));
                }
            }

            return asserted;
        }

        // ---- The ledger ----------------------------------------------------------------------

        [Fact]
        public void Every_ledger_entry_is_well_formed()
        {
            using var save = Played();

            var characters = save.Store.ReadCharacters();
            var read = save.Store.Ledger.Read();

            Assert.Equal(0, read.Malformed);
            Assert.NotEmpty(read.Entries);

            foreach (var entry in read.Entries)
            {
                Assert.False(string.IsNullOrWhiteSpace(entry.Claim));
                Assert.True(entry.Turn > 0);

                // A speaker id is either absent - the narration's own voice - or answers to somebody.
                if (entry.SpeakerId.Length > 0)
                {
                    Assert.NotNull(SaveStore.FindCharacterById(characters, entry.SpeakerId));
                }

                // A revealed secret is either absent or names something somebody holds at some stage.
                if (entry.Reveals.Length > 0)
                {
                    Assert.Contains(
                        characters.Characters,
                        character => Secrets.Find(character, entry.Reveals) is not null);
                }
            }
        }

        [Fact]
        public void Every_secret_the_ledger_says_was_revealed_is_spent()
        {
            // The derivation, checked from the other end: the ledger says the player was told, so no
            // holder should still be sitting on it as though they had a card to play.
            using var save = Played();

            var characters = save.Store.ReadCharacters();

            foreach (var revealed in save.Store.Ledger.Read().Entries
                .Select(entry => entry.Reveals)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var character in characters.Characters)
                {
                    if (Secrets.Find(character, revealed) is { } secret)
                    {
                        Assert.NotEqual(SecretStage.Live, secret.Stage);
                    }
                }
            }
        }

        [Fact]
        public void A_lie_is_recorded_as_a_lie_and_is_not_a_contradiction()
        {
            // The distinction the whole truth column exists for. A deceptive character must not read as a
            // consistency fault, or every one of them would look like a bug and no lie could be paid off.
            using var save = Played();

            var entries = save.Store.Ledger.Read().Entries;

            Assert.Contains(entries, entry => entry.Truth == ClaimTruth.Lie);
            Assert.DoesNotContain(entries, entry => entry.Truth == ClaimTruth.Contradiction);
        }

        [Fact]
        public void The_player_s_own_claims_are_unverified_and_are_never_facts_to_check()
        {
            // Half of what a player types asserts nothing at all - "go north" - and the game has no way to
            // settle the other half without a model call. So a player entry is a record of speech, and any
            // future check of claims against the world has to filter these out.
            using var save = Played();

            var mine = save.Store.Ledger.Read().Entries.Where(entry => entry.Speaker == "Rowan").ToList();

            Assert.Equal(3, mine.Count);
            Assert.All(mine, entry => Assert.Equal(ClaimTruth.Unverified, entry.Truth));
        }

        [Fact]
        public void The_player_speaks_before_the_narrator_answers_on_every_turn()
        {
            // Ledger order matches the fiction's, which is why the player's line is recorded before the
            // turn is dispatched rather than after it comes back.
            using var save = Played();

            foreach (var turn in save.Store.Ledger.Read().Entries.GroupBy(entry => entry.Turn))
            {
                Assert.Equal("Rowan", turn.OrderBy(entry => entry.Seq).First().Speaker);
            }
        }

        // ---- Claims per turn -----------------------------------------------------------------

        [Fact]
        public void Every_turn_that_recorded_claims_recorded_them_once()
        {
            // Twice in a turn would mean the second call's claims were extracted from prose the first had
            // already accounted for, which double-counts what the player was told.
            using var save = Played();

            foreach (var turn in save.Store.Journal.Read().Entries
                .Where(entry => !entry.Failed && entry.Tool == "record_claims")
                .GroupBy(entry => entry.Turn))
            {
                Assert.Single(turn);
            }
        }

        [Fact]
        public void Every_turn_that_used_a_tool_at_all_recorded_its_claims()
        {
            // The closest a batch job can get to the live check, and worth saying what it is not: a turn
            // that narrated nothing would fail this while being perfectly correct. It holds here because
            // the session above narrates on every turn, so a regression that stopped recording claims
            // shows up - but it is a property of this fixture, not of the format.
            using var save = Played();

            var byTurn = save.Store.Journal.Read().Entries.Where(entry => !entry.Failed).GroupBy(entry => entry.Turn);

            foreach (var turn in byTurn)
            {
                Assert.Contains(turn, entry => entry.Tool == "record_claims");
            }
        }

        // ---- What Phase 2 owes ---------------------------------------------------------------

        [Fact]
        public void Claims_are_recorded_true_and_nothing_yet_can_check_them()
        {
            // Not the consistency check itself - see the class remarks on why that one cannot be written
            // until ratification exists. What this pins is the input that check will need: claims arrive
            // labelled true, in quantity, and are sitting in the ledger waiting for something able to
            // judge them. If that ever stops being so, the Director would be built against a ledger that
            // no longer carries what it was designed to read.
            using var save = Played();

            var claims = save.Store.Ledger.Read().Entries
                .Where(entry => entry.Truth == ClaimTruth.True)
                .ToList();

            Assert.NotEmpty(claims);
            Assert.All(claims, claim => Assert.False(string.IsNullOrWhiteSpace(claim.Claim)));

            // Every one of them is still in the first tier: said to the player, binding, and not yet
            // anything another claim could be measured against.
            Assert.DoesNotContain(
                save.Store.Ledger.Read().Entries,
                entry => entry.Adjudicates != 0);
        }
    }
}
