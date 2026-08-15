using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// Carrying rolls from the file the narrator wrote them in onto the transcript.
    /// </summary>
    /// <remarks>
    /// <see cref="RollWatcher.Line"/> is where hiding is actually enforced — what the system prompt
    /// asks of the narrator is only manners. That makes the hidden-total assertions here the most
    /// load-bearing in the UI layer.
    /// </remarks>
    public sealed class RollWatcherTests
    {
        private static DiceRoll Roll(
            int id,
            bool hidden = false,
            bool revealed = false,
            int total = 14,
            string notation = "1d20") =>
            new()
            {
                Id = id,
                Turn = 1,
                Notation = notation,
                Reason = "Forcing the door",
                Total = total,
                Hidden = hidden,
                Revealed = revealed,
            };

        private static void Write(TempSave save, params DiceRoll[] rolls)
        {
            var path = Path.Combine(save.Directory, "rolls.jsonl");
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, SaveStore.Utf8NoBom);
            foreach (var roll in rolls)
            {
                writer.WriteLine(System.Text.Json.JsonSerializer.Serialize(roll, LogJsonContext.Readable.DiceRoll));
            }
        }

        private static string TextOf(StyledLine line) =>
            string.Concat(line.Spans.Select(span => span.Text));

        // ---- Hiding is enforced here ------------------------------------------------------------

        [Fact]
        public void A_hidden_roll_never_puts_its_total_on_the_line()
        {
            // Not blanked and not masked — the number does not enter the line at all.
            var line = RollWatcher.Line(Roll(1, hidden: true, total: 18), "Rowan");

            Assert.DoesNotContain("18", TextOf(line), StringComparison.Ordinal);
            Assert.Contains("hidden", TextOf(line), StringComparison.Ordinal);
        }

        [Fact]
        public void A_hidden_roll_never_shows_its_faces_either()
        {
            var roll = Roll(1, hidden: true, total: 18);
            roll.Faces.Add(18);

            var line = RollWatcher.Line(roll, "Rowan");

            Assert.DoesNotContain("18", TextOf(line), StringComparison.Ordinal);
        }

        [Fact]
        public void A_revealed_roll_finally_shows_its_total()
        {
            var line = RollWatcher.Line(Roll(1, hidden: true, revealed: true, total: 18), "Rowan");

            Assert.Contains("18", TextOf(line), StringComparison.Ordinal);
            Assert.Contains("revealed", TextOf(line), StringComparison.Ordinal);
        }

        [Fact]
        public void An_open_roll_shows_its_total_and_faces()
        {
            var roll = Roll(1, total: 17);
            roll.Faces.Add(14);

            var line = RollWatcher.Line(roll, "Rowan");

            Assert.Contains("= 17", TextOf(line), StringComparison.Ordinal);
            Assert.Contains("(14)", TextOf(line), StringComparison.Ordinal);
        }

        [Fact]
        public void Every_hidden_roll_hides_whatever_its_total_happens_to_be()
        {
            // Swept rather than sampled: a formatting change that appended the total before the
            // early return would pass a single-value test by luck.
            for (var total = -20; total <= 60; total++)
            {
                var line = RollWatcher.Line(Roll(1, hidden: true, total: total), "Rowan");

                Assert.Contains("hidden", TextOf(line), StringComparison.Ordinal);
                Assert.DoesNotContain($"= {total}", TextOf(line), StringComparison.Ordinal);
            }
        }

        // ---- The rest of the line ------------------------------------------------------------------

        [Fact]
        public void A_roll_with_nobody_behind_it_is_the_worlds()
        {
            Assert.Contains("the world", TextOf(RollWatcher.Line(Roll(1), null)), StringComparison.Ordinal);
        }

        [Fact]
        public void The_attribute_is_shown_when_one_applied()
        {
            var roll = Roll(1);
            roll.Attribute = "Strength";

            Assert.Contains("Strength", TextOf(RollWatcher.Line(roll, "Rowan")), StringComparison.Ordinal);
        }

        [Fact]
        public void The_reason_is_shown_when_no_attribute_applied()
        {
            Assert.Contains("Forcing the door", TextOf(RollWatcher.Line(Roll(1), "Rowan")), StringComparison.Ordinal);
        }

        [Fact]
        public void A_roll_with_no_notation_still_draws()
        {
            var line = RollWatcher.Line(Roll(1, notation: string.Empty), "Rowan");

            Assert.Contains("?", TextOf(line), StringComparison.Ordinal);
        }

        [Fact]
        public void The_line_is_marked_as_the_games_own_voice()
        {
            Assert.Contains(RollWatcher.Line(Roll(1), "Rowan").Spans, span => span.Role == TextRole.Roll);
        }

        [Fact]
        public void A_null_roll_is_a_programming_error()
        {
            Assert.Throws<ArgumentNullException>(() => RollWatcher.Line(null!, "Rowan"));
        }

        // ---- The cursor -------------------------------------------------------------------------------

        [Fact]
        public void Nothing_is_taken_from_an_empty_log()
        {
            using var save = new TempSave();

            Assert.Empty(new RollWatcher(save.Store).Take());
        }

        [Fact]
        public void A_new_roll_is_taken_once()
        {
            using var save = new TempSave();
            var watcher = new RollWatcher(save.Store);
            Write(save, Roll(1));

            Assert.Single(watcher.Take());
            Assert.Empty(watcher.Take());
        }

        [Fact]
        public void Catching_up_marks_the_backlog_seen_without_showing_it()
        {
            // Replaying a resumed campaign's whole log would bury the scene the player came back for.
            using var save = new TempSave();
            Write(save, Roll(1), Roll(2), Roll(3));

            var watcher = new RollWatcher(save.Store);
            watcher.CatchUp();

            Assert.Empty(watcher.Take());
        }

        [Fact]
        public void Rolls_arriving_after_catching_up_are_still_taken()
        {
            using var save = new TempSave();
            Write(save, Roll(1));
            var watcher = new RollWatcher(save.Store);
            watcher.CatchUp();

            Write(save, Roll(1), Roll(2));

            Assert.Equal(2, Assert.Single(watcher.Take()).Id);
        }

        [Fact]
        public void Rolls_are_taken_oldest_first()
        {
            using var save = new TempSave();
            Write(save, Roll(1), Roll(2), Roll(3));

            Assert.Equal([1, 2, 3], new RollWatcher(save.Store).Take().Select(roll => roll.Id).ToList());
        }

        [Fact]
        public void A_reveal_re_shows_a_roll_the_cursor_has_passed()
        {
            using var save = new TempSave();
            var watcher = new RollWatcher(save.Store);
            Write(save, Roll(1, hidden: true));
            Assert.Single(watcher.Take());

            Write(save, Roll(1, hidden: true, revealed: true));

            Assert.Equal(1, Assert.Single(watcher.Take()).Id);
        }

        [Fact]
        public void A_reveal_is_re_shown_only_once()
        {
            using var save = new TempSave();
            var watcher = new RollWatcher(save.Store);
            Write(save, Roll(1, hidden: true));
            watcher.Take();
            Write(save, Roll(1, hidden: true, revealed: true));
            watcher.Take();

            Assert.Empty(watcher.Take());
        }

        [Fact]
        public void An_already_revealed_roll_seen_at_catch_up_is_not_re_shown()
        {
            using var save = new TempSave();
            Write(save, Roll(1, hidden: true, revealed: true));

            var watcher = new RollWatcher(save.Store);
            watcher.CatchUp();

            Assert.Empty(watcher.Take());
        }

        [Fact]
        public void An_open_roll_is_never_re_shown_as_a_reveal()
        {
            using var save = new TempSave();
            var watcher = new RollWatcher(save.Store);
            Write(save, Roll(1));
            watcher.Take();

            Write(save, Roll(1, revealed: true));

            Assert.Empty(watcher.Take());
        }

        [Fact]
        public void A_log_that_has_shrunk_draws_the_next_roll_once_rather_than_the_whole_tail()
        {
            // A hand-edited log is the only way this happens, and following it down is what stops
            // the transcript replaying everything after it.
            using var save = new TempSave();
            var watcher = new RollWatcher(save.Store);
            Write(save, Roll(1), Roll(2), Roll(3));
            watcher.Take();

            Write(save, Roll(1));
            Assert.Empty(watcher.Take());

            Write(save, Roll(1), Roll(2));
            Assert.Equal(2, Assert.Single(watcher.Take()).Id);
        }

        [Fact]
        public void A_read_that_throws_leaves_the_cursor_where_it_was()
        {
            // Nothing is lost to a save that would not parse for a moment.
            using var save = new TempSave();
            var watcher = new RollWatcher(save.Store);

            var path = Path.Combine(save.Directory, "rolls.jsonl");
            using (var lockStream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
            {
                Assert.Throws<SaveException>(() => watcher.Take());
            }

            Write(save, Roll(1));
            Assert.Single(watcher.Take());
        }

        [Fact]
        public void Catching_up_on_a_broken_log_reports_it()
        {
            using var save = new TempSave();
            var path = Path.Combine(save.Directory, "rolls.jsonl");
            using var lockStream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

            Assert.Throws<SaveException>(() => new RollWatcher(save.Store).CatchUp());
        }
    }
}
