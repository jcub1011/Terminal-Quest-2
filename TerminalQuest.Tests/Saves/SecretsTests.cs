using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The secret model, its stages, and how a save that predates it behaves.
    /// </summary>
    /// <remarks>
    /// The stage rules are the security boundary, so the assertions worth having are the negative
    /// ones: that dormant does not count as known, that a stage nobody wrote reads as dormant rather
    /// than as live, and that an old save is not quietly given secrets it never had.
    /// </remarks>
    public sealed class SecretsTests
    {
        private static Character Bess(params Secret[] secrets) =>
            new() { Id = "chr_1", Name = "Bess", Secrets = [.. secrets] };

        private static Secret Cellar(SecretStage stage = SecretStage.Live) =>
            new() { Name = "the sealed cellar", Stage = stage, Text = "Bricked up the fever winter.", Turn = 3 };

        // ---- Stages on disk ------------------------------------------------------------------

        [Fact]
        public void A_secret_with_no_stage_reads_as_dormant()
        {
            // Fail closed. A hand-written secret that forgot its stage - or misspelled it - must be
            // returned by nothing rather than treated as being in play.
            using var save = new TempSave();
            save.WriteRaw("characters.json", """
                {"characters":[{"id":"chr_1","name":"Bess","kind":"npc",
                  "secrets":[{"name":"the sealed cellar","text":"Bricked up."}]}],"nextId":1}
                """);

            var bess = save.Store.ReadCharacters().Characters[0];

            Assert.Equal(SecretStage.Dormant, Assert.Single(bess.Secrets).Stage);
        }

        [Fact]
        public void A_stage_round_trips_through_its_wire_spelling()
        {
            // The on-disk string and the string a tool schema offers the model are the same string.
            using var save = new TempSave();
            save.Store.WriteCharacters(new CharacterFile { Characters = [Bess(Cellar())] });

            Assert.Contains("\"stage\": \"live\"", save.ReadRaw("characters.json"), StringComparison.Ordinal);
        }

        [Theory]
        [InlineData("dormant")]
        [InlineData("live")]
        [InlineData("spent")]
        public void Every_stage_is_written_in_the_spelling_the_tool_schema_uses(string spelling)
        {
            // The stage arrives as its wire spelling rather than as the enum, because the enum is
            // internal and a public theory cannot take one. That turns out to be the better test: it
            // asserts the spelling parses and is written back unchanged, which is the actual contract.
            using var save = new TempSave();
            var stage = Enum.Parse<SecretStage>(spelling, ignoreCase: true);

            save.Store.WriteCharacters(new CharacterFile { Characters = [Bess(Cellar(stage))] });

            Assert.Contains($"\"stage\": \"{spelling}\"", save.ReadRaw("characters.json"), StringComparison.Ordinal);
        }

        [Fact]
        public void A_save_with_no_secrets_property_reads_as_holding_none()
        {
            // The whole basis for leaving the schema version alone: an old document is not broken by
            // this, it simply says nobody was keeping anything.
            using var save = new TempSave();
            save.WriteRaw("characters.json", """
                {"characters":[{"id":"chr_1","name":"Bess","kind":"npc","health":10,"maxHealth":10}],"nextId":1}
                """);

            var bess = save.Store.ReadCharacters().Characters[0];

            Assert.Empty(bess.Secrets);
            Assert.Equal(2, SaveStore.CurrentSchemaVersion);
        }

        [Fact]
        public void A_secret_survives_a_full_trip_through_disk()
        {
            using var save = new TempSave();
            save.Store.WriteCharacters(new CharacterFile { Characters = [Bess(Cellar(SecretStage.Spent))] });

            var secret = Assert.Single(save.Store.ReadCharacters().Characters[0].Secrets);

            Assert.Equal("the sealed cellar", secret.Name);
            Assert.Equal(SecretStage.Spent, secret.Stage);
            Assert.Equal("Bricked up the fever winter.", secret.Text);
            Assert.Equal(3, secret.Turn);
        }

        // ---- Finding and holding -------------------------------------------------------------

        [Fact]
        public void Finding_a_secret_ignores_case_and_surrounding_space()
        {
            var bess = Bess(Cellar());

            Assert.NotNull(Secrets.Find(bess, "  The Sealed Cellar "));
        }

        [Fact]
        public void Finding_something_nobody_holds_is_null_rather_than_a_fault()
        {
            var bess = Bess(Cellar());

            Assert.Null(Secrets.Find(bess, "the other thing"));
            Assert.Null(Secrets.Find(bess, null));
            Assert.Null(Secrets.Find(bess, "   "));
        }

        [Theory]
        [InlineData("live", true)]
        [InlineData("spent", true)]
        [InlineData("dormant", false)]
        public void Holding_counts_live_and_spent_and_not_dormant(string spelling, bool holds)
        {
            // A holder of a dormant secret behaves as though unaware, so for every question that asks
            // this they are unaware.
            var bess = Bess(Cellar(Enum.Parse<SecretStage>(spelling, ignoreCase: true)));

            Assert.Equal(holds, Secrets.Holds(bess, "the sealed cellar"));
        }

        [Fact]
        public void Secrets_at_a_stage_come_back_in_the_order_the_file_holds_them()
        {
            var bess = Bess(
                new Secret { Name = "first", Stage = SecretStage.Live },
                new Secret { Name = "asleep", Stage = SecretStage.Dormant },
                new Secret { Name = "second", Stage = SecretStage.Live });

            Assert.Equal(
                ["first", "second"],
                Secrets.AtStage(bess, SecretStage.Live).Select(secret => secret.Name));
        }

        // ---- Granting and spending -----------------------------------------------------------

        [Fact]
        public void Granting_creates_it_live_and_stamps_the_turn()
        {
            // Live rather than dormant, which is the opposite of the stage's own default: nothing yet
            // exists to wake a sleeping secret, and one nothing can rouse is worse than none.
            var bess = Bess();

            var granted = Secrets.Grant(bess, "  the sealed cellar  ", "  Bricked up.  ", turn: 9);

            Assert.Equal("the sealed cellar", granted.Name);
            Assert.Equal("Bricked up.", granted.Text);
            Assert.Equal(SecretStage.Live, granted.Stage);
            Assert.Equal(9, granted.Turn);
            Assert.Same(granted, Assert.Single(bess.Secrets));
        }

        [Fact]
        public void Spending_a_live_secret_turns_it_spent()
        {
            var bess = Bess(Cellar());

            Assert.True(Secrets.Spend(bess, "The Sealed Cellar"));
            Assert.Equal(SecretStage.Spent, Assert.Single(bess.Secrets).Stage);
        }

        [Fact]
        public void Spending_a_dormant_secret_changes_nothing_and_says_so()
        {
            // The narrator was never handed it, so it cannot have revealed it. A name that collides
            // with something asleep is a mislabel, not a reveal.
            var bess = Bess(Cellar(SecretStage.Dormant));

            Assert.False(Secrets.Spend(bess, "the sealed cellar"));
            Assert.Equal(SecretStage.Dormant, Assert.Single(bess.Secrets).Stage);
        }

        [Fact]
        public void Spending_something_already_spent_reports_no_change()
        {
            var bess = Bess(Cellar(SecretStage.Spent));

            Assert.False(Secrets.Spend(bess, "the sealed cellar"));
            Assert.Equal(SecretStage.Spent, Assert.Single(bess.Secrets).Stage);
        }

        [Fact]
        public void Spending_something_nobody_holds_reports_no_change()
        {
            var bess = Bess();

            Assert.False(Secrets.Spend(bess, "the sealed cellar"));
        }

        // ---- Names ---------------------------------------------------------------------------

        [Fact]
        public void A_canonical_name_is_trimmed_and_a_blank_one_is_nothing()
        {
            Assert.Equal("the sealed cellar", Secrets.CanonicalName("  the sealed cellar "));
            Assert.Null(Secrets.CanonicalName(null));
            Assert.Null(Secrets.CanonicalName(""));
            Assert.Null(Secrets.CanonicalName("   "));
        }
    }
}
