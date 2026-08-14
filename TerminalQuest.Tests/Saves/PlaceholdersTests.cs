using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The two tokens stored prose may carry, and the substitution applied on the way out.
    /// </summary>
    public sealed class PlaceholdersTests
    {
        [Fact]
        public void This_becomes_the_owner()
        {
            Assert.Equal(
                "Rowan drew the blade.",
                Placeholders.Resolve("{This} drew the blade.", "Rowan", null));
        }

        [Fact]
        public void Player_becomes_the_players_name()
        {
            Assert.Equal(
                "Rowan owes Tam a debt.",
                Placeholders.Resolve("{This} owes {Player} a debt.", "Rowan", "Tam"));
        }

        [Theory]
        [InlineData("{this}")]
        [InlineData("{THIS}")]
        [InlineData("{ThIs}")]
        public void Token_case_is_ignored_so_a_sloppy_narrator_is_not_silently_dropped(string token)
        {
            Assert.Equal("Rowan", Placeholders.Resolve(token, "Rowan", null));
        }

        [Theory]
        [InlineData("{player}")]
        [InlineData("{PLAYER}")]
        public void Player_token_case_is_ignored_too(string token)
        {
            Assert.Equal("Tam", Placeholders.Resolve(token, "Rowan", "Tam"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void Blank_text_resolves_to_nothing_rather_than_throwing(string? text)
        {
            Assert.Equal(string.Empty, Placeholders.Resolve(text!, "Rowan", "Tam"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void An_unknown_player_leaves_the_token_standing(string? playerName)
        {
            // Worth pinning because the literal reaches the screen: there is no fallback name.
            Assert.Equal(
                "Rowan owes {Player} a debt.",
                Placeholders.Resolve("{This} owes {Player} a debt.", "Rowan", playerName));
        }

        [Fact]
        public void Text_without_tokens_is_returned_unchanged()
        {
            Assert.Equal("Nothing to see.", Placeholders.Resolve("Nothing to see.", "Rowan", "Tam"));
        }

        [Fact]
        public void Every_occurrence_is_substituted()
        {
            Assert.Equal(
                "Rowan and Rowan",
                Placeholders.Resolve("{This} and {This}", "Rowan", null));
        }

        [Fact]
        public void An_owner_name_containing_the_player_token_is_substituted_again()
        {
            // This is ordering, not a bug per se: {This} is replaced first, so anything the owner
            // name itself contains is exposed to the second pass. Pinned so the ordering cannot
            // change unnoticed — a narrator-supplied owner name reaches this.
            Assert.Equal(
                "Tam",
                Placeholders.Resolve("{This}", "{Player}", "Tam"));
        }

        // ---- Mentions --------------------------------------------------------------------

        [Fact]
        public void Mentions_finds_a_name_written_out()
        {
            Assert.True(Placeholders.Mentions("Tam paid up.", "Tam", "Rowan", "Tam"));
        }

        [Fact]
        public void Mentions_finds_a_name_written_as_a_token()
        {
            // The narrator may write the player as {Player} in one record and by name in the next;
            // a filter that missed one of those would be worse than no filter.
            Assert.True(Placeholders.Mentions("{Player} paid up.", "Tam", "Rowan", "Tam"));
        }

        [Fact]
        public void Mentions_finds_the_owner_behind_this()
        {
            Assert.True(Placeholders.Mentions("{This} paid up.", "Rowan", "Rowan", "Tam"));
        }

        [Fact]
        public void Mentions_is_case_insensitive()
        {
            Assert.True(Placeholders.Mentions("TAM paid up.", "tam", "Rowan", "Tam"));
        }

        [Fact]
        public void Mentions_is_false_when_nobody_is_named()
        {
            Assert.False(Placeholders.Mentions("The road was empty.", "Tam", "Rowan", "Tam"));
        }

        // ---- Blanks ----------------------------------------------------------------------

        [Fact]
        public void An_empty_search_term_matches_nothing()
        {
            // string.Contains("") is true for every string, so without a guard an empty entity
            // makes Mentions return true unconditionally. QuestTools reaches this on the
            // memory-filter path, where that would hand back every memory the character has
            // rather than none.
            Assert.False(Placeholders.Mentions("The road was empty.", string.Empty, "Rowan", "Tam"));
        }

        [Fact]
        public void Mentions_tolerates_blank_text_the_way_resolve_does()
        {
            // The two must agree: Resolve guards null text and returns empty, so Mentions answers
            // false rather than throwing on text.Contains. A memory with no text is a
            // hand-editable state, not a crash.
            Assert.False(Placeholders.Mentions(null!, "Tam", "Rowan", "Tam"));
        }
    }
}
