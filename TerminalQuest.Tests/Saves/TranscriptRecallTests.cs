using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// Choosing how much of a conversation to hand back, and working out whose move it is.
    /// </summary>
    /// <remarks>
    /// A pure function over a list, so there is no save folder here - the same reason
    /// <see cref="SecretDivergenceTests"/> needs none. Two processes ask this the same question and
    /// have to get the same answer, which is why the rule lives somewhere neither of them owns.
    /// </remarks>
    public sealed class TranscriptRecallTests
    {
        private static TranscriptEntry Player(int turn, string text) =>
            new() { Turn = turn, Voice = TranscriptVoice.Player, Text = text };

        private static TranscriptEntry Narrator(int turn, string text) =>
            new() { Turn = turn, Voice = TranscriptVoice.Narrator, Text = text };

        /// <summary>A conversation of <paramref name="turns"/> exchanges, each line 100 characters.</summary>
        private static List<TranscriptEntry> Conversation(int turns)
        {
            var entries = new List<TranscriptEntry>();

            for (var turn = 1; turn <= turns; turn++)
            {
                entries.Add(Player(turn, new string('p', 100)));
                entries.Add(Narrator(turn, new string('n', 100)));
            }

            return entries;
        }

        // ---- The window ----------------------------------------------------------------------

        [Fact]
        public void Nothing_recalled_from_nothing()
        {
            Assert.Empty(TranscriptRecall.Tail([], TranscriptRecall.DefaultCharacters));
        }

        [Fact]
        public void A_short_conversation_is_recalled_whole()
        {
            var entries = Conversation(3);

            Assert.Equal(entries, TranscriptRecall.Tail(entries, TranscriptRecall.DefaultCharacters));
        }

        [Fact]
        public void The_window_is_taken_from_the_end()
        {
            var entries = Conversation(50);

            var recalled = TranscriptRecall.Tail(entries, 1000);

            Assert.Equal(entries[^1], recalled[^1]);
            Assert.True(recalled.Count < entries.Count);
        }

        [Fact]
        public void The_window_stays_inside_its_budget()
        {
            var entries = Conversation(50);

            var recalled = TranscriptRecall.Tail(entries, 1000);

            Assert.True(
                recalled.Sum(entry => entry.Text.Length) <= 1000,
                "the recalled prose should fit the budget it was given");
        }

        [Fact]
        public void Entries_are_never_cut_in_half()
        {
            var entries = Conversation(50);

            var recalled = TranscriptRecall.Tail(entries, 1050);

            Assert.All(recalled, entry => Assert.Equal(100, entry.Text.Length));
        }

        [Fact]
        public void The_last_entry_survives_a_budget_it_cannot_fit()
        {
            // Never empty. One long turn recalling itself is worth more than recalling nothing,
            // and the alternative would make a verbose narrator unresumable.
            var entries = new List<TranscriptEntry>
            {
                Narrator(1, new string('n', TranscriptRecall.MaxCharacters * 2)),
            };

            var recalled = TranscriptRecall.Tail(entries, TranscriptRecall.MinCharacters);

            Assert.Equal(entries[^1], Assert.Single(recalled));
        }

        [Fact]
        public void An_oversized_reply_still_brings_the_line_that_prompted_it()
        {
            // Both rules at once, and they compose the way round that keeps the exchange readable:
            // the reply is kept because it is last, and the prompt because the reply is a narrator's.
            var entries = new List<TranscriptEntry>
            {
                Player(1, "go north"),
                Narrator(1, new string('n', TranscriptRecall.MaxCharacters * 2)),
            };

            Assert.Equal(entries, TranscriptRecall.Tail(entries, TranscriptRecall.MinCharacters));
        }

        [Fact]
        public void A_recalled_reply_brings_the_line_that_prompted_it()
        {
            // Deliberately over budget. An answer shown without its question reads as a non-sequitur,
            // and a player line costs a few dozen characters.
            var entries = new List<TranscriptEntry>
            {
                Player(1, new string('p', 400)),
                Narrator(1, new string('n', 400)),
                Player(2, "ask about the toll"),
                Narrator(2, new string('n', 400)),
            };

            var recalled = TranscriptRecall.Tail(entries, 500);

            Assert.Equal(TranscriptVoice.Player, recalled[0].Voice);
            Assert.Equal("ask about the toll", recalled[0].Text);
        }

        [Fact]
        public void A_reply_is_not_given_a_prompt_from_another_turn()
        {
            // The opening turn of a resumed session has no player line at all - nobody typed
            // anything, the game asked for a scene. Reaching back for the previous turn's line would
            // pair the answer with a question it never answered.
            var entries = new List<TranscriptEntry>
            {
                Player(1, new string('p', 400)),
                Narrator(2, new string('n', 400)),
            };

            var recalled = TranscriptRecall.Tail(entries, 500);

            Assert.Equal(entries[^1], Assert.Single(recalled));
        }

        [Fact]
        public void A_window_starting_on_a_player_line_reaches_back_no_further()
        {
            // Six entries of a hundred characters fits 600 exactly and lands on a player line, so
            // the turn-boundary rule has nothing to do and must not pull in a seventh.
            var entries = Conversation(10);

            var recalled = TranscriptRecall.Tail(entries, 600);

            Assert.Equal(TranscriptVoice.Player, recalled[0].Voice);
            Assert.Equal(6, recalled.Count);
        }

        [Fact]
        public void The_recalled_order_is_the_order_it_happened()
        {
            var entries = Conversation(20);

            var recalled = TranscriptRecall.Tail(entries, 1000);

            Assert.Equal(
                recalled.OrderBy(entry => entries.IndexOf(entry)),
                recalled);
        }

        // ---- The budget ----------------------------------------------------------------------

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(TranscriptRecall.MinCharacters - 1)]
        public void A_budget_below_the_floor_is_lifted_to_it(int asked)
        {
            Assert.Equal(TranscriptRecall.MinCharacters, TranscriptRecall.Clamp(asked));
        }

        [Fact]
        public void A_budget_above_the_ceiling_is_brought_down_to_it()
        {
            Assert.Equal(TranscriptRecall.MaxCharacters, TranscriptRecall.Clamp(int.MaxValue));
        }

        [Fact]
        public void A_budget_in_range_is_left_alone()
        {
            Assert.Equal(4000, TranscriptRecall.Clamp(4000));
        }

        [Fact]
        public void An_unusable_budget_does_not_throw_it_is_clamped()
        {
            // Both a hand-edited settings file and a number the model invented arrive unchecked.
            var entries = Conversation(5);

            Assert.NotEmpty(TranscriptRecall.Tail(entries, -100));
        }

        [Fact]
        public void The_default_sits_inside_its_own_bounds()
        {
            Assert.InRange(
                TranscriptRecall.DefaultCharacters,
                TranscriptRecall.MinCharacters,
                TranscriptRecall.MaxCharacters);
        }

        // ---- Whose move it is ------------------------------------------------------------------

        [Fact]
        public void Nobody_is_awaited_on_an_empty_transcript()
        {
            Assert.False(TranscriptRecall.AwaitingNarrator([]));
        }

        [Fact]
        public void The_narrator_is_awaited_when_the_player_spoke_last()
        {
            // What a session killed mid-reply leaves behind: the player's line went down before the
            // turn ran, and the answer never did.
            Assert.True(TranscriptRecall.AwaitingNarrator([Player(1, "go north")]));
        }

        [Fact]
        public void Nobody_is_awaited_when_the_narrator_answered()
        {
            Assert.False(TranscriptRecall.AwaitingNarrator(
                [Player(1, "go north"), Narrator(1, "The road climbs.")]));
        }

        [Fact]
        public void The_verdict_is_about_the_window_it_is_asked_about()
        {
            // Both callers ask this of the window they are showing, so it has to answer for that
            // window - which, since the window always ends at the end of the log, is the same answer.
            var entries = Conversation(20);
            entries.Add(Player(21, "look behind me"));

            Assert.True(TranscriptRecall.AwaitingNarrator(TranscriptRecall.Tail(entries, 1000)));
        }
    }
}
