using TerminalQuest.Saves;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// Drawing a recalled conversation back into the transcript pane.
    /// </summary>
    /// <remarks>
    /// The load-bearing assertions are the ones about what a replay must not change: a hidden roll's
    /// total stays out of the line, and a recalled player command is shaped exactly like a live one.
    /// The first is a guard on a rule enforced elsewhere, and it is worth restating here because a
    /// replay is a second path to the same screen and the obvious way to leak a total is to build a
    /// roll line by hand rather than going through the formatter that withholds it.
    /// </remarks>
    public sealed class TranscriptReplayTests
    {
        private static TranscriptEntry Player(int turn, string text) =>
            new() { Turn = turn, Voice = TranscriptVoice.Player, Text = text };

        private static TranscriptEntry Narrator(int turn, string text) =>
            new() { Turn = turn, Voice = TranscriptVoice.Narrator, Text = text };

        private static DiceRoll Roll(
            int id,
            int turn,
            bool hidden = false,
            int total = 14,
            string characterId = "") =>
            new()
            {
                Id = id,
                Turn = turn,
                Notation = "1d20",
                Reason = "Forcing the door",
                Total = total,
                Hidden = hidden,
                CharacterId = characterId,
            };

        private static string TextOf(StyledLine line) =>
            string.Concat(line.Spans.Select(span => span.Text));

        private static IReadOnlyList<string> TextOf(IReadOnlyList<StyledLine> lines) =>
            lines.Select(TextOf).ToArray();

        private static IReadOnlyList<StyledLine> Replay(
            IReadOnlyList<TranscriptEntry> entries,
            IReadOnlyList<DiceRoll>? rolls = null,
            CharacterFile? characters = null) =>
            TranscriptReplay.Lines(entries, rolls ?? [], characters ?? new CharacterFile());

        // ---- Nothing to recall -----------------------------------------------------------------

        [Fact]
        public void An_empty_recall_draws_nothing_at_all()
        {
            // Not even a divider. A save with no transcript should open exactly as it did before
            // this feature existed.
            Assert.Empty(Replay([]));
        }

        // ---- The two voices --------------------------------------------------------------------

        [Fact]
        public void A_player_line_is_drawn_the_way_it_was_echoed()
        {
            // The same shape GameWindow gives a live command, so a recalled line and one typed a
            // moment ago are indistinguishable.
            var lines = Replay([Player(1, "push open the door")]);

            var echoed = Assert.Single(lines, line => TextOf(line).StartsWith("> ", StringComparison.Ordinal));

            Assert.Equal("> push open the door", TextOf(echoed));
            Assert.All(echoed.Spans, span => Assert.Equal(TextRole.Command, span.Role));
        }

        [Fact]
        public void Narrator_markup_is_parsed_back_into_roles()
        {
            // Why the prose is stored with its tags. Recalled text has to be coloured as it was, and
            // the tags are the only record of how that was.
            var lines = Replay([Narrator(1, "The [item]iron key[/item] turns.")]);

            var prose = Assert.Single(lines, line => TextOf(line).Contains("iron key", StringComparison.Ordinal));

            Assert.Equal("The iron key turns.", TextOf(prose));
            Assert.Contains(prose.Spans, span => span is { Text: "iron key", Role: TextRole.Item });
        }

        [Fact]
        public void The_conversation_keeps_its_order()
        {
            var lines = TextOf(Replay(
            [
                Player(1, "one"),
                Narrator(1, "two"),
                Player(2, "three"),
            ]));

            var oneAt = lines.ToList().FindIndex(text => text == "> one");
            var twoAt = lines.ToList().FindIndex(text => text == "two");
            var threeAt = lines.ToList().FindIndex(text => text == "> three");

            Assert.True(oneAt < twoAt && twoAt < threeAt, "the replay should read in the order it happened");
        }

        // ---- Rolls -----------------------------------------------------------------------------

        [Fact]
        public void A_roll_is_drawn_above_the_prose_of_its_own_turn()
        {
            // Which is where ShowRolls puts it live: a tool call ends a block of text, so the die
            // lands above the paragraph that describes it.
            var lines = TextOf(Replay(
                [Player(1, "force it"), Narrator(1, "The door gives.")],
                [Roll(1, turn: 1)]));

            var rollAt = lines.ToList().FindIndex(text => text.StartsWith("roll", StringComparison.Ordinal));
            var proseAt = lines.ToList().FindIndex(text => text == "The door gives.");

            Assert.True(rollAt >= 0, "the roll should be drawn");
            Assert.True(rollAt < proseAt, "the roll should sit above the prose of its turn");
        }

        [Fact]
        public void A_roll_from_a_turn_outside_the_window_is_left_out()
        {
            // A die thrown in a scene the player is not being shown is noise, not context, and
            // /rolls still has every one of them.
            var lines = TextOf(Replay(
                [Narrator(5, "The door gives.")],
                [Roll(1, turn: 2)]));

            Assert.DoesNotContain(lines, text => text.StartsWith("roll", StringComparison.Ordinal));
        }

        [Fact]
        public void A_hidden_roll_still_keeps_its_total_off_the_screen()
        {
            // The guard. Replay is a second path to the same pane, and it must not become the one
            // that spells out a number the player was never told.
            var lines = TextOf(Replay(
                [Narrator(1, "You cannot tell whether she believed you.")],
                [Roll(1, turn: 1, hidden: true, total: 18)]));

            Assert.DoesNotContain(lines, text => text.Contains("18", StringComparison.Ordinal));
            Assert.Contains(lines, text => text.Contains("hidden", StringComparison.Ordinal));
        }

        [Fact]
        public void A_rolls_character_is_named_from_the_roster()
        {
            var characters = new CharacterFile();
            characters.Characters.Add(new Character { Id = "chr_1", Name = "Rowan" });

            var lines = TextOf(Replay(
                [Narrator(1, "The door gives.")],
                [Roll(1, turn: 1, characterId: "chr_1")],
                characters));

            Assert.Contains(lines, text => text.Contains("Rowan", StringComparison.Ordinal));
        }

        [Fact]
        public void A_turns_dice_are_not_dealt_out_twice()
        {
            // Nothing forbids two narrator entries sharing a turn, and its dice belong to the turn
            // rather than to either of them.
            var lines = TextOf(Replay(
                [Narrator(1, "First."), Narrator(1, "Second.")],
                [Roll(1, turn: 1)]));

            Assert.Single(lines, text => text.StartsWith("roll", StringComparison.Ordinal));
        }

        // ---- Whose move it was -----------------------------------------------------------------

        [Fact]
        public void An_interrupted_session_says_the_reply_was_discarded()
        {
            var lines = TextOf(Replay([Player(1, "look behind me")]));

            Assert.Contains(lines, text => text.Contains("discarded", StringComparison.Ordinal));
        }

        [Fact]
        public void A_session_that_ended_cleanly_says_no_such_thing()
        {
            var lines = TextOf(Replay(
                [Player(1, "look behind me"), Narrator(1, "Nothing follows you.")]));

            Assert.DoesNotContain(lines, text => text.Contains("discarded", StringComparison.Ordinal));
        }

        [Fact]
        public void The_recalled_block_is_fenced_so_it_is_not_mistaken_for_this_sitting()
        {
            var lines = Replay([Narrator(1, "The door gives.")]);

            Assert.Equal(TextRole.System, Assert.Single(lines[0].Spans).Role);
            Assert.Contains("last session", TextOf(lines[0]), StringComparison.Ordinal);
            Assert.Equal(TextRole.System, Assert.Single(lines[^1].Spans).Role);
        }
    }
}
