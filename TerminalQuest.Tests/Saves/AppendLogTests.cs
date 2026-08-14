using System.Text;
using System.Text.Json;

using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The append-only log: sequence allocation, file sharing, and tolerance of a torn tail.
    /// </summary>
    /// <remarks>
    /// The interesting assertions here are all about failure. A log is written a line at a time by two
    /// processes that never speak to each other, and is expected to survive being hand-edited and
    /// being killed halfway through a write - so what happens to a malformed line matters more than
    /// what happens to a good one.
    /// </remarks>
    public sealed class AppendLogTests
    {
        private const string FileName = "journal.jsonl";

        private static AppendLog<JournalEntry> Log(TempSave save) =>
            new(Path.Combine(save.Directory, FileName), LogJsonContext.Readable.JournalEntry);

        private static JournalEntry Entry(int turn = 1, string tool = "get_state") =>
            new() { Turn = turn, Tool = tool, Arguments = JsonDocument.Parse("{}").RootElement };

        // ---- Sequence allocation -------------------------------------------------------------

        [Fact]
        public void The_first_entry_is_sequence_one()
        {
            using var save = new TempSave();

            Assert.Equal(1, Log(save).Append(Entry()));
        }

        [Fact]
        public void Sequences_climb_by_one_and_never_repeat()
        {
            using var save = new TempSave();
            var log = Log(save);

            var allocated = Enumerable.Range(0, 5).Select(_ => log.Append(Entry())).ToArray();

            Assert.Equal([1, 2, 3, 4, 5], allocated);
            Assert.Equal([1, 2, 3, 4, 5], log.Read().Entries.Select(entry => entry.Seq));
        }

        [Fact]
        public void A_second_log_over_the_same_file_continues_the_sequence()
        {
            // Two instances is what two processes see: nothing is cached, so the number comes off
            // the file rather than out of the object that last wrote it.
            using var save = new TempSave();

            Log(save).Append(Entry());
            Log(save).Append(Entry());

            Assert.Equal(3, Log(save).Append(Entry()));
        }

        [Fact]
        public void A_gap_in_the_sequence_does_not_reissue_a_number()
        {
            // A hand-edited log may have had a line taken out of it. Numbering resumes past the
            // highest that was ever used, never filling the hole - the same rule entity ids follow.
            using var save = new TempSave();
            save.WriteRaw(FileName, """
                {"seq":1,"turn":1,"tool":"get_state","arguments":{}}
                {"seq":9,"turn":1,"tool":"get_state","arguments":{}}

                """);

            Assert.Equal(10, Log(save).Append(Entry()));
        }

        [Fact]
        public void The_head_is_found_past_a_line_longer_than_the_tail_window()
        {
            // One entry bigger than the tail window means nothing in the window parses, and the
            // scan has to fall back to the whole file rather than start numbering again at one.
            using var save = new TempSave();
            var log = Log(save);

            log.Append(new JournalEntry
            {
                Turn = 1,
                Tool = "upsert_location",
                Arguments = JsonDocument.Parse($$"""{"description":"{{new string('x', 20_000)}}"}""").RootElement,
            });

            Assert.Equal(1, log.Head());
            Assert.Equal(2, log.Append(Entry()));
        }

        [Fact]
        public void The_head_of_an_absent_log_is_zero_and_asking_does_not_create_it()
        {
            using var save = new TempSave();

            Assert.Equal(0, Log(save).Head());
            Assert.False(save.Has(FileName));
        }

        // ---- Reading -------------------------------------------------------------------------

        [Fact]
        public void An_absent_log_reads_as_empty_and_is_not_created_by_asking()
        {
            using var save = new TempSave();

            var read = Log(save).Read();

            Assert.Empty(read.Entries);
            Assert.Equal(0, read.Malformed);
            Assert.False(save.Has(FileName));
        }

        [Fact]
        public void Entries_come_back_oldest_first()
        {
            using var save = new TempSave();
            var log = Log(save);

            log.Append(Entry(tool: "first"));
            log.Append(Entry(tool: "second"));

            Assert.Equal(["first", "second"], log.Read().Entries.Select(entry => entry.Tool));
        }

        [Fact]
        public void For_turn_returns_only_that_turn()
        {
            using var save = new TempSave();
            var log = Log(save);

            log.Append(Entry(turn: 1, tool: "before"));
            log.Append(Entry(turn: 2, tool: "during"));
            log.Append(Entry(turn: 3, tool: "after"));

            Assert.Equal(["during"], log.ForTurn(2).Select(entry => entry.Tool));
        }

        [Fact]
        public void For_turn_returns_them_oldest_first()
        {
            // It scans backwards to avoid parsing the whole campaign, so the order has to be put back.
            using var save = new TempSave();
            var log = Log(save);

            log.Append(Entry(turn: 4, tool: "first"));
            log.Append(Entry(turn: 4, tool: "second"));
            log.Append(Entry(turn: 4, tool: "third"));

            Assert.Equal(["first", "second", "third"], log.ForTurn(4).Select(entry => entry.Tool));
        }

        [Fact]
        public void For_turn_finds_a_turn_it_is_not_at_the_end_of()
        {
            // The backward scan stops when the turn drops below the one asked for, which is only correct
            // because entries are appended in order. This is the case that would catch it stopping early.
            using var save = new TempSave();
            var log = Log(save);

            log.Append(Entry(turn: 1, tool: "before"));
            log.Append(Entry(turn: 2, tool: "during"));
            log.Append(Entry(turn: 2, tool: "also during"));
            log.Append(Entry(turn: 3, tool: "after"));
            log.Append(Entry(turn: 3, tool: "also after"));

            Assert.Equal(["during", "also during"], log.ForTurn(2).Select(entry => entry.Tool));
        }

        [Fact]
        public void For_turn_on_a_turn_with_nothing_in_it_is_empty()
        {
            using var save = new TempSave();
            var log = Log(save);

            log.Append(Entry(turn: 1));
            log.Append(Entry(turn: 3));

            Assert.Empty(log.ForTurn(2));
            Assert.Empty(log.ForTurn(9));
            Assert.Empty(Log(new TempSave()).ForTurn(1));
        }

        [Fact]
        public void For_turn_steps_over_a_malformed_line_rather_than_stopping_at_it()
        {
            // A torn last line is routine and cannot be dated, so it must not hide the entries behind it.
            using var save = new TempSave();
            save.WriteRaw(FileName, """
                {"seq":1,"turn":2,"tool":"during","arguments":{}}
                { not json
                {"seq":3,"turn":2,"tool":"also during","arguments":{}}

                """);

            Assert.Equal(["during", "also during"], Log(save).ForTurn(2).Select(entry => entry.Tool));
        }

        [Fact]
        public void A_malformed_line_is_skipped_rather_than_losing_the_file()
        {
            // The whole reason a log does not throw the way a document does: one unreadable line out
            // of thousands must not cost the other thousands.
            using var save = new TempSave();
            save.WriteRaw(FileName, """
                {"seq":1,"turn":1,"tool":"good","arguments":{}}
                { not json at all
                {"seq":3,"turn":1,"tool":"also good","arguments":{}}

                """);

            var read = Log(save).Read();

            Assert.Equal(["good", "also good"], read.Entries.Select(entry => entry.Tool));
            Assert.Equal(1, read.Malformed);
        }

        [Fact]
        public void A_blank_line_is_ignored_rather_than_counted_as_malformed()
        {
            // A hand-edited file has them, and a well-formed log ends on one. Neither is a fault.
            using var save = new TempSave();
            save.WriteRaw(FileName, """
                {"seq":1,"turn":1,"tool":"get_state","arguments":{}}


                {"seq":2,"turn":1,"tool":"get_state","arguments":{}}

                """);

            var read = Log(save).Read();

            Assert.Equal(2, read.Entries.Count);
            Assert.Equal(0, read.Malformed);
        }

        [Fact]
        public void A_line_from_a_later_build_still_reads()
        {
            using var save = new TempSave();
            save.WriteRaw(
                FileName,
                """{"seq":1,"turn":4,"tool":"get_state","arguments":{},"somethingNewer":true}""" + "\n");

            var read = Log(save).Read();

            Assert.Equal(0, read.Malformed);
            Assert.Equal(4, Assert.Single(read.Entries).Turn);
        }

        // ---- Surviving a crash ---------------------------------------------------------------

        [Fact]
        public void A_torn_last_line_from_a_crash_is_closed_rather_than_joined()
        {
            // A process killed mid-append leaves a line with no newline on it. Appending onto the end
            // of it would produce one line that is neither entry, losing the new one as well as the
            // old.
            using var save = new TempSave();
            save.WriteRaw(FileName, """
                {"seq":1,"turn":1,"tool":"whole","arguments":{}}
                {"seq":2,"turn":1,"tool":"tor
                """.ReplaceLineEndings("\n"));

            var log = Log(save);
            log.Append(Entry(tool: "after the crash"));

            var read = log.Read();

            Assert.Equal(1, read.Malformed);
            Assert.Equal(["whole", "after the crash"], read.Entries.Select(entry => entry.Tool));
        }

        [Fact]
        public void A_torn_tail_does_not_stop_the_sequence_climbing()
        {
            using var save = new TempSave();
            save.WriteRaw(FileName, """
                {"seq":7,"turn":1,"tool":"whole","arguments":{}}
                {"seq":8,"turn":1,"tool":"tor
                """.ReplaceLineEndings("\n"));

            // Eight is unreadable, so seven is the highest that can be established. Nine would be
            // nicer and is not available: the point is that it does not go backwards.
            Assert.Equal(8, Log(save).Append(Entry()));
        }

        [Fact]
        public void Appending_leaves_no_temporary_file_behind()
        {
            // There is no rename here to leave one, which is the difference from a document write -
            // asserted so that anybody who adds one has to think about why.
            using var save = new TempSave();

            Log(save).Append(Entry());

            Assert.Empty(save.TempFiles);
        }

        // ---- Sharing, and the format -------------------------------------------------------

        [Fact]
        public void The_share_modes_let_a_reader_and_an_appender_coexist()
        {
            // The assertion this file exists for. A reader that asks for more than it needs does not
            // protect itself - it makes the other process's append fail, and the model is told its
            // tool refused. Both directions are checked because both happen: the transcript polls
            // this save several times a second for the whole of a turn.
            using var save = new TempSave();
            var log = Log(save);
            log.Append(Entry());

            using (new FileStream(
                log.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                Assert.Equal(2, log.Append(Entry()));
            }

            using (new FileStream(
                log.Path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read | FileShare.Delete))
            {
                using var reader = new FileStream(
                    log.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

                Assert.True(reader.Length > 0);
            }
        }

        [Fact]
        public void Two_appenders_racing_never_share_a_sequence_number()
        {
            // Threads rather than processes on purpose. A share mode is a property of a handle, not
            // of a process, so two handles opened on one thread pool exercise the same check the
            // operating system applies between the game and the tool server - which is why this is
            // not an integration test and does not spawn anything.
            using var save = new TempSave();
            const int writers = 4;
            const int each = 15;

            var logs = Enumerable.Range(0, writers).Select(_ => Log(save)).ToArray();

            Parallel.ForEach(logs, log =>
            {
                for (var index = 0; index < each; index++)
                {
                    log.Append(Entry());
                }
            });

            var read = Log(save).Read();

            Assert.Equal(0, read.Malformed);
            Assert.Equal(writers * each, read.Entries.Count);
            Assert.Equal(
                Enumerable.Range(1, writers * each),
                read.Entries.Select(entry => entry.Seq).Order());
        }

        [Fact]
        public void An_entry_is_written_as_one_line()
        {
            // The deliberate opposite of a save document, which is indented so a person can edit it.
            // Indentation here would not be untidy, it would be a log nothing can read back.
            using var save = new TempSave();
            var log = Log(save);

            log.Append(Entry());
            log.Append(Entry());

            Assert.Equal(2, save.ReadLines(FileName).Length);
        }

        [Fact]
        public void A_line_never_holds_a_raw_newline()
        {
            // Narrator prose has paragraph breaks in it and goes through here verbatim. The encoder
            // is relaxed about HTML and about non-ASCII; it is not relaxed about control characters,
            // because a raw newline inside a JSON string is not JSON.
            using var save = new TempSave();
            var log = Log(save);
            var prose = "first line\nsecond line\r\nthird";

            log.Append(new JournalEntry
            {
                Turn = 1,
                Tool = "add_memory",
                Arguments = JsonSerializer.SerializeToElement(
                    new Dictionary<string, string> { ["text"] = prose }),
            });

            Assert.Single(save.ReadLines(FileName));
            Assert.Equal(
                prose,
                Assert.Single(log.Read().Entries).Arguments.GetProperty("text").GetString());
        }

        [Fact]
        public void The_file_carries_no_byte_order_mark()
        {
            // A preamble in the middle of a jsonl file corrupts one line and is invisible in an
            // editor, so the encoding is shared with the document writer rather than restated.
            using var save = new TempSave();

            Log(save).Append(Entry());

            var bytes = File.ReadAllBytes(Path.Combine(save.Directory, FileName));

            Assert.NotEqual(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray());
        }

        // ---- Opaque arguments ---------------------------------------------------------------

        [Fact]
        public void Opaque_arguments_round_trip_verbatim()
        {
            // Stored rather than parsed because the divergence rule reads a name back out of here,
            // and because a tool's schema will change while old lines must still mean what they meant.
            using var save = new TempSave();
            var log = Log(save);
            const string arguments = """{"character":"Bess","about":"the sealed cellar","depth":3,"tags":["a","b"]}""";

            log.Append(new JournalEntry
            {
                Turn = 2,
                Tool = "get_memories",
                Arguments = JsonDocument.Parse(arguments).RootElement,
            });

            var stored = Assert.Single(log.Read().Entries).Arguments;

            Assert.Equal("Bess", stored.GetProperty("character").GetString());
            Assert.Equal(3, stored.GetProperty("depth").GetInt32());
            Assert.Equal(arguments, stored.GetRawText());
        }

        [Fact]
        public void An_undefined_element_is_what_the_journal_has_to_normalise()
        {
            // Pins the reason the recorder substitutes an empty object. The server hands over a
            // default JsonElement for a tool that takes no arguments, and writing one throws rather
            // than producing null - so this is a mandatory substitution and not a defensive one.
            using var save = new TempSave();

            Assert.Throws<InvalidOperationException>(() =>
                Log(save).Append(new JournalEntry { Turn = 1, Tool = "get_state" }));
        }

        [Fact]
        public void A_default_value_type_is_left_off_the_line_and_an_empty_string_is_not()
        {
            // Pins how far the ignore condition actually gets, because it is less far than it looks:
            // neither available condition suppresses an empty string, since both compare against null.
            // So the outcome flag vanishes when a call succeeded - which is most lines - and an unused
            // string is spelled out. Asserted so the reasoning in LogJsonContext stays true, and so that
            // making these properties nullable later is a visible decision rather than a silent one.
            using var save = new TempSave();

            Log(save).Append(Entry());

            var line = Assert.Single(save.ReadLines(FileName));

            Assert.DoesNotContain("\"failed\"", line, StringComparison.Ordinal);
            Assert.Contains("\"error\":\"\"", line, StringComparison.Ordinal);
        }

        [Fact]
        public void A_failed_call_does_say_so_on_the_line()
        {
            using var save = new TempSave();

            Log(save).Append(new JournalEntry
            {
                Turn = 1,
                Tool = "get_character",
                Arguments = JsonDocument.Parse("{}").RootElement,
                Failed = true,
            });

            Assert.Contains("\"failed\":true", Assert.Single(save.ReadLines(FileName)), StringComparison.Ordinal);
        }

        [Fact]
        public void An_apostrophe_survives_unescaped()
        {
            using var save = new TempSave();

            Log(save).Append(Entry(tool: "it's"));

            Assert.Contains("it's", save.ReadRaw(FileName), StringComparison.Ordinal);
        }
    }
}
