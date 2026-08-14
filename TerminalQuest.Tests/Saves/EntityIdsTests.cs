using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// Id numbering. The rule that matters: a freshly allocated id must never collide with one
    /// something already points at, because that merges two entities rather than failing.
    /// </summary>
    public sealed class EntityIdsTests
    {
        [Fact]
        public void The_ceiling_is_the_stored_counter_when_it_leads()
        {
            Assert.Equal(100, EntityIds.Ceiling(EntityIds.Character, [], 100));
        }

        [Fact]
        public void The_ceiling_rises_to_the_highest_id_in_use()
        {
            // A hand-edited save may carry a counter that lags its ids.
            var ids = new string?[] { "chr_1", "chr_42", "chr_7" };

            Assert.Equal(42, EntityIds.Ceiling(EntityIds.Character, ids, 0));
        }

        [Fact]
        public void A_negative_counter_never_drags_the_ceiling_below_zero()
        {
            Assert.Equal(0, EntityIds.Ceiling(EntityIds.Character, [], -5));
        }

        [Fact]
        public void Ids_of_another_type_do_not_move_the_ceiling()
        {
            var ids = new string?[] { "loc_99", "itm_50" };

            Assert.Equal(3, EntityIds.Ceiling(EntityIds.Character, ids, 3));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("chr_")]
        [InlineData("chr_abc")]
        [InlineData("chr_0")]
        [InlineData("chr_-5")]
        [InlineData("nonsense")]
        public void Malformed_ids_leave_numbering_alone(string? id)
        {
            // Tolerance is deliberate: a bad id in a hand-edited file should not throw in the
            // middle of a turn.
            Assert.Equal(7, EntityIds.Ceiling(EntityIds.Character, [id], 7));
        }

        [Theory]
        [InlineData("chr_1")]
        [InlineData("chr_9999")]
        public void A_well_formed_id_is_recognised(string id)
        {
            Assert.True(EntityIds.IsWellFormed(id, EntityIds.Character));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("chr_0")]
        [InlineData("chr_")]
        [InlineData("chr_x")]
        [InlineData("loc_1")]
        [InlineData("CHR_5")]   // the prefix comparison is ordinal
        public void Anything_else_is_not_well_formed(string? id)
        {
            Assert.False(EntityIds.IsWellFormed(id, EntityIds.Character));
        }

        [Fact]
        public void Prefixes_are_distinct()
        {
            Assert.Equal(3, new[] { EntityIds.Character, EntityIds.Location, EntityIds.Item }.Distinct().Count());
        }

        [Fact]
        public void A_null_id_list_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(
                () => EntityIds.Ceiling(EntityIds.Character, null!, 0));
        }

        [Fact]
        public void A_null_prefix_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(() => EntityIds.Ceiling(null!, [], 0));
        }

        [Fact]
        public void An_empty_prefix_is_a_programming_error()
        {
            Assert.Throws<ArgumentException>(() => EntityIds.Ceiling(string.Empty, [], 0));
        }

        // ---- What the parse must not accept ----------------------------------------------

        [Theory]
        [InlineData("chr_+5")]
        [InlineData("chr_ 5")]
        [InlineData("chr_5 ")]
        public void An_id_with_a_sign_or_spaces_is_not_one_this_scheme_could_have_issued(string id)
        {
            // int.TryParse defaults to NumberStyles.Integer, which permits a leading '+' and
            // surrounding whitespace. Under that default these pass IsWellFormed and count toward
            // the ceiling while never matching an ordinal id lookup such as
            // SaveStore.FindCharacterById — a record reachable by numbering but not by reference.
            Assert.False(EntityIds.IsWellFormed(id, EntityIds.Character));
        }
    }
}
