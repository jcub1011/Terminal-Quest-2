using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The rule for changing a description: add to it, never replace it.
    /// </summary>
    /// <remarks>
    /// The two mutable description fields are the only place in the save format where the world can
    /// contradict something the player was already told - everything else the narrator writes is
    /// appended, and an append-only structure cannot contradict itself. So this small function carries
    /// the whole of that guarantee.
    /// </remarks>
    public sealed class DescriptionsTests
    {
        [Fact]
        public void A_first_description_is_taken_whole()
        {
            Assert.Equal("A ford across the river.", Descriptions.Extend(string.Empty, "A ford across the river."));
        }

        [Fact]
        public void An_addition_is_appended_to_what_is_already_there()
        {
            Assert.Equal(
                "A ford across the river. A mill stands on the far bank.",
                Descriptions.Extend("A ford across the river.", "A mill stands on the far bank."));
        }

        [Fact]
        public void The_same_words_twice_do_not_double_it()
        {
            // The case that earns this function its keep. A narrator calling an upsert twice in a scene
            // sends the same sentence twice, and without this the field doubles inside one session.
            const string existing = "A ford across the river.";

            Assert.Equal(existing, Descriptions.Extend(existing, "A ford across the river."));
        }

        [Fact]
        public void The_same_words_in_a_different_case_do_not_double_it_either()
        {
            const string existing = "A ford across the river.";

            Assert.Equal(existing, Descriptions.Extend(existing, "a FORD across the river."));
        }

        [Fact]
        public void Something_already_said_in_passing_is_not_repeated()
        {
            const string existing = "A ford across the river. A mill stands on the far bank.";

            Assert.Equal(existing, Descriptions.Extend(existing, "A mill stands on the far bank."));
        }

        [Fact]
        public void An_empty_addition_leaves_it_alone()
        {
            // What the blanking bug used to do instead: assign, and lose the description entirely.
            const string existing = "A ford across the river.";

            Assert.Equal(existing, Descriptions.Extend(existing, string.Empty));
            Assert.Equal(existing, Descriptions.Extend(existing, "   "));
            Assert.Equal(existing, Descriptions.Extend(existing, null));
        }

        [Fact]
        public void An_addition_is_trimmed_and_joined_with_a_single_space()
        {
            // One paragraph of prose, because it is rendered as a line among lines and a newline inside
            // it would read as two facts where there is one description.
            Assert.Equal(
                "A ford. A mill.",
                Descriptions.Extend("A ford.   ", "   A mill.  "));
        }

        [Fact]
        public void An_addition_that_would_outgrow_the_ceiling_is_refused()
        {
            // Null rather than a silent truncation, so the caller can refuse and name somewhere better to
            // put it.
            var existing = new string('x', Descriptions.MaxLength - 2);

            Assert.Null(Descriptions.Extend(existing, "yyy"));
        }

        [Fact]
        public void An_addition_that_exactly_fills_the_ceiling_is_accepted()
        {
            var existing = new string('x', Descriptions.MaxLength - 4);

            Assert.NotNull(Descriptions.Extend(existing, "yyy"));
        }

        [Fact]
        public void A_first_description_longer_than_the_ceiling_is_refused()
        {
            Assert.Null(Descriptions.Extend(string.Empty, new string('x', Descriptions.MaxLength + 1)));
        }

        [Fact]
        public void A_repeat_is_accepted_even_when_the_field_is_already_full()
        {
            // Nothing is being added, so there is nothing to refuse - and refusing here would turn a
            // harmless duplicate call into a failed turn.
            var existing = new string('x', Descriptions.MaxLength);

            Assert.Equal(existing, Descriptions.Extend(existing, "xxx"));
        }
    }
}
