using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The dice resolver: the only place in the game where chance lives, and a parser fed by a
    /// language model. Both halves matter — a wrong total is a silently unfair game, and an
    /// unbounded expression is a hang.
    /// </summary>
    public sealed class DiceTests
    {
        private static SequenceRandom Faces(params int[] values) => new(values);

        // ---- Shape of a successful roll -------------------------------------------------

        [Fact]
        public void Rolls_each_die_and_sums_them()
        {
            var outcome = Dice.TryRoll("3d6", Faces(2, 5, 4), out var error);

            Assert.NotNull(outcome);
            Assert.Equal(string.Empty, error);
            Assert.Equal([2, 5, 4], outcome.Faces);
            Assert.Equal(11, outcome.Total);
        }

        [Fact]
        public void Reports_the_tidied_notation_rather_than_what_was_typed()
        {
            // The parsed form is the honest one: the two differ exactly when the narrator was
            // sloppy, and the player is shown this string.
            var outcome = Dice.TryRoll("2 D 6 + 3", Faces(1, 1), out _);

            Assert.NotNull(outcome);
            Assert.Equal("2d6+3", outcome.Notation);
        }

        [Fact]
        public void A_die_without_a_count_rolls_one()
        {
            var random = Faces(17);

            var outcome = Dice.TryRoll("d20", random, out _);

            Assert.NotNull(outcome);
            Assert.Equal(17, outcome.Total);
            Assert.Equal(1, random.Draws);
        }

        [Fact]
        public void Adds_a_flat_bonus()
        {
            var outcome = Dice.TryRoll("1d8+2", Faces(5), out _);

            Assert.NotNull(outcome);
            Assert.Equal(7, outcome.Total);
            Assert.Equal([5], outcome.Faces);
        }

        [Fact]
        public void Subtracts_a_negative_term()
        {
            var outcome = Dice.TryRoll("1d20-1d4", Faces(20, 3), out _);

            Assert.NotNull(outcome);
            Assert.Equal(17, outcome.Total);
        }

        [Fact]
        public void The_first_term_may_carry_a_sign()
        {
            var outcome = Dice.TryRoll("-1d4", Faces(3), out var error);

            Assert.NotNull(outcome);
            Assert.Equal(string.Empty, error);
            Assert.Equal(-3, outcome.Total);
        }

        [Fact]
        public void A_bare_number_is_a_whole_expression()
        {
            var outcome = Dice.TryRoll("+3", Faces(), out _);

            Assert.NotNull(outcome);
            Assert.Equal(3, outcome.Total);
            Assert.Empty(outcome.Faces);
        }

        // ---- Keeps ----------------------------------------------------------------------

        [Fact]
        public void Keeping_the_highest_still_reports_every_die_thrown()
        {
            // Seeing the kept 6, 5, 4 beside the dropped 1 is what makes advantage legible as
            // something that happened rather than a number that arrived.
            var outcome = Dice.TryRoll("4d6kh3", Faces(6, 1, 5, 4), out _);

            Assert.NotNull(outcome);
            Assert.Equal([6, 1, 5, 4], outcome.Faces);
            Assert.Equal(15, outcome.Total);
        }

        [Fact]
        public void Keeping_the_lowest_takes_the_other_end()
        {
            var outcome = Dice.TryRoll("2d20kl1", Faces(18, 4), out _);

            Assert.NotNull(outcome);
            Assert.Equal([18, 4], outcome.Faces);
            Assert.Equal(4, outcome.Total);
        }

        [Fact]
        public void A_bare_k_keeps_one_highest()
        {
            var outcome = Dice.TryRoll("2d20k", Faces(9, 15), out var error);

            Assert.NotNull(outcome);
            Assert.Equal(string.Empty, error);
            Assert.Equal(15, outcome.Total);
        }

        [Fact]
        public void Kh_without_a_number_keeps_one()
        {
            var outcome = Dice.TryRoll("2d20kh", Faces(9, 15), out _);

            Assert.NotNull(outcome);
            Assert.Equal(15, outcome.Total);
        }

        [Fact]
        public void Kl_without_a_number_keeps_one()
        {
            var outcome = Dice.TryRoll("2d20kl", Faces(9, 15), out _);

            Assert.NotNull(outcome);
            Assert.Equal(9, outcome.Total);
        }

        [Fact]
        public void Keeping_every_die_is_the_same_as_not_keeping()
        {
            var outcome = Dice.TryRoll("3d6kh3", Faces(2, 5, 4), out _);

            Assert.NotNull(outcome);
            Assert.Equal(11, outcome.Total);
        }

        [Fact]
        public void A_keep_applies_only_to_its_own_term()
        {
            var outcome = Dice.TryRoll("2d20kh1+1d4", Faces(3, 19, 2), out _);

            Assert.NotNull(outcome);
            Assert.Equal([3, 19, 2], outcome.Faces);
            Assert.Equal(21, outcome.Total);
        }

        // ---- Drop notation is deliberately not accepted ----------------------------------

        [Theory]
        [InlineData("4d6dl1")]
        [InlineData("4d6dh1")]
        public void Drop_notation_is_refused_so_there_is_one_way_to_say_it(string notation)
        {
            var outcome = Dice.TryRoll(notation, Faces(1, 2, 3, 4), out var error);

            Assert.Null(outcome);
            Assert.NotEqual(string.Empty, error);
            Assert.Contains("kh", error, StringComparison.Ordinal);
        }

        // ---- Refusals --------------------------------------------------------------------

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\r\n")]
        public void An_empty_notation_is_refused(string? notation)
        {
            var outcome = Dice.TryRoll(notation!, Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains("A roll needs a notation", error, StringComparison.Ordinal);
        }

        [Fact]
        public void Rolling_no_dice_is_refused()
        {
            var outcome = Dice.TryRoll("0d6", Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains("rolls no dice", error, StringComparison.Ordinal);
        }

        [Fact]
        public void Too_many_dice_are_refused()
        {
            var outcome = Dice.TryRoll($"{Dice.MaxDice + 1}d6", Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains("too many dice", error, StringComparison.Ordinal);
        }

        [Fact]
        public void Exactly_the_dice_limit_is_allowed()
        {
            var values = new int[Dice.MaxDice];
            Array.Fill(values, 1);

            var outcome = Dice.TryRoll($"{Dice.MaxDice}d6", Faces(values), out _);

            Assert.NotNull(outcome);
            Assert.Equal(Dice.MaxDice, outcome.Faces.Count);
        }

        [Fact]
        public void A_die_with_too_few_sides_is_refused()
        {
            var outcome = Dice.TryRoll("1d1", Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains($"at least {Dice.MinSides} sides", error, StringComparison.Ordinal);
        }

        [Fact]
        public void A_die_with_too_many_sides_is_refused()
        {
            var outcome = Dice.TryRoll($"1d{Dice.MaxSides + 1}", Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains("too many sides", error, StringComparison.Ordinal);
        }

        [Fact]
        public void A_die_with_no_sides_named_is_refused()
        {
            var outcome = Dice.TryRoll("2d", Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains("how many sides", error, StringComparison.Ordinal);
        }

        [Fact]
        public void Keeping_no_dice_is_refused()
        {
            var outcome = Dice.TryRoll("4d6k0", Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains("keeps no dice", error, StringComparison.Ordinal);
        }

        [Fact]
        public void Keeping_more_dice_than_are_rolled_is_refused()
        {
            var outcome = Dice.TryRoll("2d6kh3", Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains("keeps more dice than it rolls", error, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_terms_run_together_are_refused()
        {
            // Compacting turns this into "1d41d4", which is unambiguous only because the parser
            // insists every term after the first is joined by a sign.
            var outcome = Dice.TryRoll("1d4 1d4", Faces(1, 1), out var error);

            Assert.Null(outcome);
            Assert.Contains("runs two terms together", error, StringComparison.Ordinal);
        }

        [Fact]
        public void An_expression_ending_on_a_sign_is_refused()
        {
            var outcome = Dice.TryRoll("1d6+", Faces(1), out var error);

            Assert.Null(outcome);
            Assert.Contains("ends on a sign", error, StringComparison.Ordinal);
        }

        [Fact]
        public void Text_that_is_not_notation_at_all_is_refused()
        {
            var outcome = Dice.TryRoll("roll for initiative", Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains("is not dice notation", error, StringComparison.Ordinal);
        }

        // ---- Bounds that exist because a model writes the notation ------------------------

        [Fact]
        public void Exactly_the_term_limit_is_allowed()
        {
            var notation = string.Join('+', Enumerable.Repeat("1", Dice.MaxTerms));

            var outcome = Dice.TryRoll(notation, Faces(), out var error);

            Assert.NotNull(outcome);
            Assert.Equal(string.Empty, error);
            Assert.Equal(Dice.MaxTerms, outcome.Total);
        }

        [Fact]
        public void One_term_past_the_limit_is_refused()
        {
            var notation = string.Join('+', Enumerable.Repeat("1", Dice.MaxTerms + 1));

            var outcome = Dice.TryRoll(notation, Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains("too many terms", error, StringComparison.Ordinal);
        }

        [Fact]
        public void A_flat_bonus_past_the_limit_is_refused()
        {
            var outcome = Dice.TryRoll($"1d6+{Dice.MaxFlat + 1}", Faces(1), out var error);

            Assert.Null(outcome);
            Assert.Contains("too large", error, StringComparison.Ordinal);
        }

        [Fact]
        public void The_flat_limit_is_checked_before_the_sign_is_applied()
        {
            // The magnitude is what is bounded, so a huge penalty is refused exactly like a huge
            // bonus rather than sneaking past a signed comparison.
            var outcome = Dice.TryRoll($"1d6-{Dice.MaxFlat + 1}", Faces(1), out var error);

            Assert.Null(outcome);
            Assert.Contains("too large", error, StringComparison.Ordinal);
        }

        [Fact]
        public void Exactly_the_flat_limit_is_allowed()
        {
            var outcome = Dice.TryRoll($"{Dice.MaxFlat}", Faces(), out _);

            Assert.NotNull(outcome);
            Assert.Equal(Dice.MaxFlat, outcome.Total);
        }

        [Fact]
        public void A_runaway_digit_run_saturates_instead_of_overflowing()
        {
            // The guard that matters: a plausible typo must come back as a sentence the narrator
            // can act on, never as an OverflowException or a wrapped negative that slips under a
            // limit check.
            var outcome = Dice.TryRoll("999999999999999999999999999999d6", Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains("too many dice", error, StringComparison.Ordinal);
        }

        [Fact]
        public void A_runaway_side_count_saturates_too()
        {
            var outcome = Dice.TryRoll("1d999999999999999999999999999999", Faces(), out var error);

            Assert.Null(outcome);
            Assert.Contains("too many sides", error, StringComparison.Ordinal);
        }

        // ---- Contract held across every input --------------------------------------------

        [Theory]
        [InlineData("2d6+3")]
        [InlineData("d20")]
        [InlineData("4d6kh3")]
        [InlineData("0d6")]
        [InlineData("1d1")]
        [InlineData("2d6kh3")]
        [InlineData("nonsense")]
        [InlineData("1d6+")]
        [InlineData("")]
        public void Error_is_set_when_and_only_when_the_roll_failed(string notation)
        {
            var values = new int[Dice.MaxDice];
            Array.Fill(values, 1);

            var outcome = Dice.TryRoll(notation, Faces(values), out var error);

            if (outcome is null)
            {
                Assert.NotEqual(string.Empty, error);
            }
            else
            {
                Assert.Equal(string.Empty, error);
            }
        }

        [Fact]
        public void A_null_generator_is_a_programming_error_not_a_bad_roll()
        {
            Assert.Throws<ArgumentNullException>(() => Dice.TryRoll("1d6", null!, out _));
        }

        [Fact]
        public void Case_is_ignored()
        {
            var outcome = Dice.TryRoll("2D6KH1", Faces(3, 6), out var error);

            Assert.NotNull(outcome);
            Assert.Equal(string.Empty, error);
            Assert.Equal("2d6kh1", outcome.Notation);
            Assert.Equal(6, outcome.Total);
        }
    }
}
