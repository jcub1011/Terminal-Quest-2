using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The rule that decides whether a knowledge fetch may be answered.
    /// </summary>
    /// <remarks>
    /// No <c>TempSave</c> and no <c>SaveStore</c> anywhere in this file, which is the point of the rule
    /// being pure: it is a predicate over who holds what and who has already been read, so it can be
    /// checked exhaustively without a save folder or a turn ever happening.
    /// </remarks>
    public sealed class SecretDivergenceTests
    {
        private static Character Person(string name, params (string Name, SecretStage Stage)[] secrets) =>
            new()
            {
                Id = "chr_" + name.ToLowerInvariant(),
                Name = name,
                Secrets = [.. secrets.Select(secret => new Secret
                {
                    Name = secret.Name,
                    Stage = secret.Stage,
                    Text = $"What {name} knows about {secret.Name}.",
                })],
            };

        // ---- Nothing to diverge from ---------------------------------------------------------

        [Fact]
        public void Nothing_read_yet_blocks_nothing()
        {
            var bess = Person("Bess", ("the sealed cellar", SecretStage.Live));

            Assert.Null(SecretDivergence.BlockingHolder(bess, [bess], []));
        }

        [Fact]
        public void Reading_the_same_character_twice_blocks_nothing()
        {
            // The narrator checking its notes, not a divergence. Refusing this would make voicing
            // anybody a one-shot affair.
            var bess = Person("Bess", ("the sealed cellar", SecretStage.Live));

            Assert.Null(SecretDivergence.BlockingHolder(bess, [bess], ["Bess"]));
        }

        [Fact]
        public void The_same_character_under_a_different_casing_is_still_the_same_character()
        {
            var bess = Person("Bess", ("the sealed cellar", SecretStage.Live));

            Assert.Null(SecretDivergence.BlockingHolder(bess, [bess], ["  bess "]));
        }

        [Fact]
        public void A_character_holding_no_secrets_blocks_nothing()
        {
            var bess = Person("Bess");
            var tam = Person("Tam");

            Assert.Null(SecretDivergence.BlockingHolder(tam, [bess, tam], ["Bess"]));
        }

        [Fact]
        public void A_second_character_who_shares_every_live_secret_blocks_nothing()
        {
            // Two people in on one thing is the ordinary case, and it must not cost a turn.
            var bess = Person("Bess", ("the sealed cellar", SecretStage.Live));
            var tam = Person("Tam", ("the sealed cellar", SecretStage.Live));

            Assert.Null(SecretDivergence.BlockingHolder(tam, [bess, tam], ["Bess"]));
        }

        [Fact]
        public void A_name_nobody_answers_to_is_ignored()
        {
            // A fetch that failed on its own terms, or somebody since removed by hand.
            var tam = Person("Tam");

            Assert.Null(SecretDivergence.BlockingHolder(tam, [tam], ["Nobody At All"]));
        }

        // ---- Diverging -----------------------------------------------------------------------

        [Fact]
        public void A_second_character_who_lacks_a_live_secret_is_blocked_and_names_the_first()
        {
            var bess = Person("Bess", ("the sealed cellar", SecretStage.Live));
            var tam = Person("Tam");

            Assert.Equal("Bess", SecretDivergence.BlockingHolder(tam, [bess, tam], ["Bess"]));
        }

        [Fact]
        public void The_first_blocker_in_log_order_is_the_one_named()
        {
            // Stability, so that trying again produces the same refusal. A message naming a different
            // character each time would read as the world changing its mind rather than as a rule.
            var bess = Person("Bess", ("the sealed cellar", SecretStage.Live));
            var mott = Person("Mott", ("the innkeeper's brother", SecretStage.Live));
            var tam = Person("Tam");

            Assert.Equal(
                "Bess",
                SecretDivergence.BlockingHolder(tam, [bess, mott, tam], ["Bess", "Mott"]));
            Assert.Equal(
                "Mott",
                SecretDivergence.BlockingHolder(tam, [bess, mott, tam], ["Mott", "Bess"]));
        }

        [Fact]
        public void Sharing_one_live_secret_but_not_another_is_still_blocked()
        {
            var bess = Person(
                "Bess",
                ("the sealed cellar", SecretStage.Live),
                ("the innkeeper's brother", SecretStage.Live));
            var tam = Person("Tam", ("the sealed cellar", SecretStage.Live));

            Assert.Equal("Bess", SecretDivergence.BlockingHolder(tam, [bess, tam], ["Bess"]));
        }

        // ---- Which stages matter -------------------------------------------------------------

        [Fact]
        public void A_dormant_secret_never_blocks()
        {
            // It was never handed over, so there is nothing the narrator has to be kept from.
            var bess = Person("Bess", ("the sealed cellar", SecretStage.Dormant));
            var tam = Person("Tam");

            Assert.Null(SecretDivergence.BlockingHolder(tam, [bess, tam], ["Bess"]));
        }

        [Fact]
        public void A_spent_secret_never_blocks()
        {
            // The player has already been told. Going on protecting it would cost turns for nothing.
            var bess = Person("Bess", ("the sealed cellar", SecretStage.Spent));
            var tam = Person("Tam");

            Assert.Null(SecretDivergence.BlockingHolder(tam, [bess, tam], ["Bess"]));
        }

        [Fact]
        public void A_dormant_copy_of_the_same_secret_does_not_excuse_the_divergence()
        {
            // The load-bearing case. Tam is named in the same secret but his copy is asleep, so he
            // behaves as though unaware - and a fetch of him must still be refused. Counting a dormant
            // copy as sharing would let a hand-edit meant to keep him ignorant open the gate instead.
            var bess = Person("Bess", ("the sealed cellar", SecretStage.Live));
            var tam = Person("Tam", ("the sealed cellar", SecretStage.Dormant));

            Assert.Equal("Bess", SecretDivergence.BlockingHolder(tam, [bess, tam], ["Bess"]));
        }

        [Fact]
        public void A_spent_copy_of_the_same_secret_does_excuse_it()
        {
            // Unlike dormant: spent means this character may speak of it, so they are not being kept
            // from anything.
            var bess = Person("Bess", ("the sealed cellar", SecretStage.Live));
            var tam = Person("Tam", ("the sealed cellar", SecretStage.Spent));

            Assert.Null(SecretDivergence.BlockingHolder(tam, [bess, tam], ["Bess"]));
        }

        [Fact]
        public void A_secret_shared_under_a_different_casing_still_counts_as_shared()
        {
            var bess = Person("Bess", ("The Sealed Cellar", SecretStage.Live));
            var tam = Person("Tam", ("the sealed cellar", SecretStage.Live));

            Assert.Null(SecretDivergence.BlockingHolder(tam, [bess, tam], ["Bess"]));
        }

        // ---- Guards --------------------------------------------------------------------------

        [Fact]
        public void The_rule_needs_a_character_a_roster_and_a_history()
        {
            var bess = Person("Bess");

            Assert.Throws<ArgumentNullException>(() => SecretDivergence.BlockingHolder(null!, [bess], []));
            Assert.Throws<ArgumentNullException>(() => SecretDivergence.BlockingHolder(bess, null!, []));
            Assert.Throws<ArgumentNullException>(() => SecretDivergence.BlockingHolder(bess, [bess], null!));
        }
    }
}
