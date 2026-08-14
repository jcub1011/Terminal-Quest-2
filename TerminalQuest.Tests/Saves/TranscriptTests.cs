using System.Text;

using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The transcript log itself: that prose survives a round trip unaltered, and that the bounded
    /// tail read hands back whole entries.
    /// </summary>
    /// <remarks>
    /// Byte-for-byte fidelity is the whole promise of this file, so most of what is asserted here is
    /// that nothing helpfully normalised anything on the way past - not the markup, not the newlines,
    /// not the quotes.
    /// </remarks>
    public sealed class TranscriptTests
    {
        private const string FileName = "transcript.jsonl";

        private static TranscriptEntry Entry(
            int turn = 1,
            TranscriptVoice voice = TranscriptVoice.Narrator,
            string text = "The door gives.") =>
            new() { Turn = turn, Voice = voice, Text = text };

        // ---- Round trip ----------------------------------------------------------------------

        [Fact]
        public void An_absent_log_reads_as_empty()
        {
            using var save = new TempSave();

            Assert.Empty(save.Store.Transcript.Read().Entries);
            Assert.False(save.Has(FileName));
        }

        [Fact]
        public void A_line_comes_back_as_it_went_in()
        {
            using var save = new TempSave();

            save.Store.Transcript.Append(Entry(turn: 7, text: "You are in a room."));

            var entry = Assert.Single(save.Store.Transcript.Read().Entries);

            Assert.Equal(7, entry.Turn);
            Assert.Equal(TranscriptVoice.Narrator, entry.Voice);
            Assert.Equal("You are in a room.", entry.Text);
            Assert.Equal(1, entry.Seq);
        }

        [Fact]
        public void Markup_survives_untouched()
        {
            // The point of storing prose rather than a summary. The narrator reads its own tagging
            // back, and the replay needs it to colour the recalled scene as it was coloured live.
            const string Prose =
                "The [item]iron key[/item] turns. [speech]\"Who goes there?\"[/speech] "
              + "calls a [danger]watchman[/danger] from [place]the Hollow Gate[/place].";

            using var save = new TempSave();

            save.Store.Transcript.Append(Entry(text: Prose));

            Assert.Equal(Prose, Assert.Single(save.Store.Transcript.Read().Entries).Text);
        }

        [Fact]
        public void Prose_with_paragraph_breaks_stays_one_line_of_log()
        {
            // A raw newline is not valid inside a JSON string, so the encoder escapes it. That is
            // what lets a multi-paragraph turn live on a single line of a line-oriented file.
            using var save = new TempSave();

            save.Store.Transcript.Append(Entry(text: "First.\nSecond.\r\nThird."));

            Assert.Single(save.ReadLines(FileName));
            Assert.Equal("First.\nSecond.\r\nThird.", Assert.Single(save.Store.Transcript.Read().Entries).Text);
        }

        [Fact]
        public void A_player_line_keeps_what_they_typed()
        {
            // Including the line breaks an external editor lets them write, and the slashes and
            // quotes that would be mangled by anything trying to be clever.
            const string Typed = "say \"I'll pay when the road is safe\"\nthen wait";

            using var save = new TempSave();

            save.Store.Transcript.Append(Entry(voice: TranscriptVoice.Player, text: Typed));

            var entry = Assert.Single(save.Store.Transcript.Read().Entries);

            Assert.Equal(TranscriptVoice.Player, entry.Voice);
            Assert.Equal(Typed, entry.Text);
        }

        [Fact]
        public void A_player_line_omits_the_voice_it_defaults_to()
        {
            // Player is zero so that a hand-edited line which loses its voice reads back as the
            // player's rather than as prose the narrator never wrote.
            using var save = new TempSave();

            save.Store.Transcript.Append(Entry(voice: TranscriptVoice.Player, text: "go north"));

            Assert.DoesNotContain("voice", save.ReadRaw(FileName), StringComparison.Ordinal);
        }

        [Fact]
        public void A_line_with_no_voice_reads_as_the_players()
        {
            using var save = new TempSave();
            save.WriteRaw(FileName, """{"seq":1,"turn":1,"text":"go north"}""" + "\n");

            Assert.Equal(TranscriptVoice.Player, Assert.Single(save.Store.Transcript.Read().Entries).Voice);
        }

        [Fact]
        public void A_narrator_line_names_its_voice_in_lowercase()
        {
            using var save = new TempSave();

            save.Store.Transcript.Append(Entry());

            Assert.Contains("\"voice\":\"narrator\"", save.ReadRaw(FileName), StringComparison.Ordinal);
        }

        [Fact]
        public void The_conversation_comes_back_in_the_order_it_happened()
        {
            using var save = new TempSave();

            save.Store.Transcript.Append(Entry(turn: 1, voice: TranscriptVoice.Player, text: "one"));
            save.Store.Transcript.Append(Entry(turn: 1, text: "two"));
            save.Store.Transcript.Append(Entry(turn: 2, voice: TranscriptVoice.Player, text: "three"));

            Assert.Equal(
                ["one", "two", "three"],
                save.Store.Transcript.Read().Entries.Select(entry => entry.Text));
        }

        // ---- The bounded tail ----------------------------------------------------------------

        [Fact]
        public void Tail_of_an_absent_log_is_empty()
        {
            using var save = new TempSave();

            Assert.Empty(save.Store.Transcript.Tail(8 * 1024));
        }

        [Fact]
        public void A_window_covering_the_file_keeps_its_first_line()
        {
            // Nothing precedes the first line, so it cannot be a fragment of one.
            using var save = new TempSave();

            save.Store.Transcript.Append(Entry(text: "only"));

            Assert.Equal("only", Assert.Single(save.Store.Transcript.Tail(8 * 1024)).Text);
        }

        [Fact]
        public void A_window_that_opens_mid_file_drops_the_line_it_cut()
        {
            using var save = new TempSave();

            for (var index = 1; index <= 6; index++)
            {
                save.Store.Transcript.Append(Entry(turn: index, text: $"line {index}"));
            }

            // Sized to open somewhere inside the file rather than on a boundary, which is the case
            // the first-line rule exists for.
            var length = new FileInfo(Path.Combine(save.Directory, FileName)).Length;
            var window = save.Store.Transcript.Tail((int)(length / 2));

            Assert.NotEmpty(window);

            // Whole entries only, and the last one is always among them.
            Assert.All(window, entry => Assert.StartsWith("line ", entry.Text, StringComparison.Ordinal));
            Assert.Equal("line 6", window[^1].Text);
            Assert.True(window.Count < 6, "a half-file window should not return the whole file");
        }

        [Fact]
        public void A_torn_last_line_does_not_stop_the_rest_being_read()
        {
            // What a process killed mid-append leaves behind. The next append heals it; a read
            // before that has to cope on its own.
            using var save = new TempSave();

            save.Store.Transcript.Append(Entry(text: "whole"));
            File.AppendAllText(Path.Combine(save.Directory, FileName), """{"seq":2,"turn":1,"te""");

            Assert.Equal("whole", Assert.Single(save.Store.Transcript.Tail(8 * 1024)).Text);
            Assert.Equal(1, save.Store.Transcript.Read().Malformed);
        }

        [Fact]
        public void A_window_is_bytes_and_a_multibyte_character_is_not_split_into_nonsense()
        {
            // The window may open inside a UTF-8 sequence. That fragment must be discarded with the
            // partial line it belongs to, not decoded into a replacement character and parsed.
            using var save = new TempSave();

            save.Store.Transcript.Append(Entry(turn: 1, text: "a road — long and unpaved — going north"));
            save.Store.Transcript.Append(Entry(turn: 2, text: "the gate — shut"));

            var length = new FileInfo(Path.Combine(save.Directory, FileName)).Length;

            foreach (var window in Enumerable.Range(1, (int)length))
            {
                var read = save.Store.Transcript.Tail(window);

                Assert.All(read, entry => Assert.DoesNotContain('�', entry.Text));
            }
        }

        [Fact]
        public void Speech_and_accents_stay_legible_on_disk()
        {
            // The relaxed encoder, and the reason it is used. Prose is the most quote-heavy thing
            // this format holds, and a transcript full of " would be unreadable by hand -
            // which is the one thing the save format promises about every file in it.
            using var save = new TempSave();

            save.Store.Transcript.Append(Entry(text: "[speech]\"Café is shut,\" she said[/speech] — plainly."));

            var raw = save.ReadRaw(FileName);

            Assert.Contains("\\\"Café is shut,\\\"", raw, StringComparison.Ordinal);
            Assert.Contains("—", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("\\u0022", raw, StringComparison.Ordinal);
        }

        [Fact]
        public void The_log_shares_the_folder_with_the_other_two()
        {
            using var save = new TempSave();

            save.Store.Transcript.Append(Entry());

            Assert.Equal(
                Path.Combine(save.Directory, FileName),
                save.Store.Transcript.Path);
            Assert.Empty(save.TempFiles);
        }

        [Fact]
        public void The_file_carries_no_encoding_preamble()
        {
            // A preamble in the middle of a jsonl file corrupts one line while being invisible in
            // every editor, so the log shares SaveStore's BOM-less encoding.
            using var save = new TempSave();

            save.Store.Transcript.Append(Entry());

            var bytes = File.ReadAllBytes(Path.Combine(save.Directory, FileName));

            Assert.NotEqual<byte[]>(Encoding.UTF8.GetPreamble(), bytes.Take(3).ToArray());
        }
    }
}
