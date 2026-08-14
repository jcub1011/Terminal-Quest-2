using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The findings log: what the game noticed going wrong and did not trouble the player with.
    /// </summary>
    /// <remarks>
    /// The load-bearing assertion here is the one about not throwing. One caller is reporting that a
    /// log could not be written, so this call is expected to fail sometimes — and a diagnostic that
    /// could take a turn down would be worse than the trouble it describes.
    /// </remarks>
    public sealed class FindingsTests
    {
        private const string FileName = "diagnostics.jsonl";

        // ---- Recording -------------------------------------------------------------------------

        [Fact]
        public void A_save_where_nothing_went_wrong_has_no_findings_file()
        {
            // The presence of the file is itself the signal, so it must not be created by asking.
            using var save = new TempSave();

            Assert.Empty(save.Store.Diagnostics.Read().Entries);
            Assert.False(save.Has(FileName));
        }

        [Fact]
        public void A_finding_comes_back_as_it_went_in()
        {
            using var save = new TempSave();

            Findings.Record(save.Store, 14, Finding.ClaimsMissing);

            var entry = Assert.Single(save.Store.Diagnostics.Read().Entries);

            Assert.Equal(14, entry.Turn);
            Assert.Equal(Finding.ClaimsMissing, entry.Finding);
            Assert.Equal(1, entry.Seq);
        }

        [Fact]
        public void A_detail_is_kept_where_there_is_one()
        {
            using var save = new TempSave();

            Findings.Record(save.Store, 3, Finding.RecordUnwritable, "Could not append to journal.jsonl: denied");

            Assert.Equal(
                "Could not append to journal.jsonl: denied",
                Assert.Single(save.Store.Diagnostics.Read().Entries).Detail);
        }

        [Fact]
        public void Every_occurrence_is_kept_rather_than_only_the_first()
        {
            // The count is the diagnostic. A narrator that forgets its claims once is noise; one that
            // forgets fourteen times in a row is the prompt being wrong.
            using var save = new TempSave();

            foreach (var turn in Enumerable.Range(1, 14))
            {
                Findings.Record(save.Store, turn, Finding.ClaimsMissing);
            }

            Assert.Equal(14, save.Store.Diagnostics.Read().Entries.Count);
        }

        [Fact]
        public void Findings_are_turn_stamped_so_they_line_up_with_the_other_logs()
        {
            // The whole reason this lives in the save folder rather than in one file for the machine.
            using var save = new TempSave();

            Findings.Record(save.Store, 7, Finding.ClaimsMissing);

            Assert.Equal(7, Assert.Single(save.Store.Diagnostics.Read().Entries).Turn);
        }

        [Fact]
        public void The_finding_is_named_on_the_wire_rather_than_numbered()
        {
            using var save = new TempSave();

            Findings.Record(save.Store, 1, Finding.ClaimsMissing);

            Assert.Contains("\"finding\":\"claimsMissing\"", save.ReadRaw(FileName), StringComparison.Ordinal);
        }

        [Fact]
        public void A_line_that_names_no_finding_reads_as_unknown()
        {
            // Zero is the value the game never writes, so reading one back means a hand-edit or a
            // later build.
            using var save = new TempSave();
            save.WriteRaw(FileName, """{"seq":1,"turn":1,"detail":"something"}""" + "\n");

            Assert.Equal(Finding.Unknown, Assert.Single(save.Store.Diagnostics.Read().Entries).Finding);
        }

        // ---- Never at the caller's expense -------------------------------------------------------

        [Fact]
        public void A_folder_that_will_not_take_the_line_does_not_throw()
        {
            // The case that matters: this is called to report that a log could not be written, and
            // the folder it would write to is the one that just refused. Staged by putting a file
            // where the save folder should be, so creating the directory fails the way a full disk
            // or a denied permission would.
            using var save = new TempSave();

            var blocked = Path.Combine(save.Parent, "blocked");
            File.WriteAllText(blocked, "not a folder");

            Findings.Record(new SaveStore(blocked), 1, Finding.RecordUnwritable, "Could not append to journal.jsonl");
        }

        [Fact]
        public void A_findings_log_that_will_not_parse_does_not_stop_the_next_one_landing()
        {
            // A hand-edit, or a process killed mid-append. The log is diagnostic, so the worst it may
            // do is lose a line of itself.
            using var save = new TempSave();
            save.WriteRaw(FileName, "not json at all\n");

            Findings.Record(save.Store, 2, Finding.ClaimsMissing);

            var read = save.Store.Diagnostics.Read();

            Assert.Equal(1, read.Malformed);
            Assert.Equal(Finding.ClaimsMissing, Assert.Single(read.Entries).Finding);
        }

        [Fact]
        public void Recording_against_nothing_is_a_programming_error()
        {
            // Distinct from the above on purpose. A folder that refuses is the world being awkward;
            // a null store is the caller being wrong, and swallowing that would hide a bug.
            Assert.Throws<ArgumentNullException>(
                () => Findings.Record(null!, 1, Finding.ClaimsMissing));
        }

        [Fact]
        public void The_findings_log_is_separate_from_the_journal()
        {
            // Not tidiness: ClaimsMissing works by asking the journal whether a turn holds a
            // record_claims entry, and the batch consistency test asserts over what it finds there.
            using var save = new TempSave();

            Findings.Record(save.Store, 1, Finding.ClaimsMissing);

            Assert.Empty(save.Store.Journal.Read().Entries);
            Assert.NotEqual(save.Store.Journal.Path, save.Store.Diagnostics.Path);
        }
    }
}
