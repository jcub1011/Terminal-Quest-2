using System.Text;
using System.Text.RegularExpressions;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The one document in a save folder the player writes for the narrator: that it is seeded once, never seeded
    /// over, and read back exactly as they left it.
    /// </summary>
    public sealed class NarratorPromptFileTests
    {
        private const string FileName = "system-prompt.txt";

        // ---- Falling back -------------------------------------------------------------------------

        [Fact]
        public void A_save_without_the_file_reads_as_the_default()
        {
            using var save = new TempSave();

            Assert.Equal(NarratorPromptFile.Default, NarratorPromptFile.Read(save.Store));

            // Reading must not create it. Only Ensure does that, and only where it is safe to.
            Assert.False(save.Has(FileName));
        }

        [Fact]
        public void An_absent_file_reads_as_null_rather_than_empty()
        {
            // The store's own answer, below the fallback: "nobody wrote one" has to stay separable
            // from "somebody emptied it", even though the policy above treats them alike.
            using var save = new TempSave();

            Assert.Null(save.Store.ReadSystemPrompt());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\r\n")]
        public void An_empty_file_falls_back_to_the_default(string contents)
        {
            // What a crash mid-write leaves behind, and an accident far more often than a request to
            // narrate with no instructions at all.
            using var save = new TempSave();

            save.WriteRaw(FileName, contents);

            Assert.Equal(NarratorPromptFile.Default, NarratorPromptFile.Read(save.Store));
        }

        // ---- Seeding ------------------------------------------------------------------------------

        [Fact]
        public void Ensure_writes_the_default_and_returns_it()
        {
            using var save = new TempSave();

            var seeded = NarratorPromptFile.Ensure(save.Store);

            Assert.True(save.Has(FileName));
            Assert.Equal(NarratorPromptFile.Default.ReplaceLineEndings(), seeded);
            Assert.Equal(seeded, save.ReadRaw(FileName));
        }

        [Fact]
        public void The_seeded_file_uses_this_platforms_line_endings()
        {
            // The first thing that happens to this file is that somebody opens it in Notepad.
            using var save = new TempSave();

            var seeded = NarratorPromptFile.Ensure(save.Store);

            Assert.Contains(Environment.NewLine, seeded, StringComparison.Ordinal);
            Assert.Equal(NarratorPromptFile.Default.ReplaceLineEndings(), seeded);
        }

        [Fact]
        public void Ensure_leaves_an_edited_file_alone()
        {
            // The promise the whole feature rests on: a prompt the player has written is never
            // improved, replaced or topped up by the game.
            const string Written = "You are a laconic narrator. Two sentences, never three.";

            using var save = new TempSave();

            NarratorPromptFile.Ensure(save.Store);
            save.Store.WriteSystemPrompt(Written);

            Assert.Equal(Written, NarratorPromptFile.Ensure(save.Store));
            Assert.Equal(Written, save.ReadRaw(FileName));
        }

        [Fact]
        public void Ensure_reseeds_a_file_that_has_been_emptied()
        {
            using var save = new TempSave();

            save.WriteRaw(FileName, "   ");

            Assert.Equal(NarratorPromptFile.Default.ReplaceLineEndings(), NarratorPromptFile.Ensure(save.Store));
        }

        [Fact]
        public void Ensure_is_idempotent()
        {
            using var save = new TempSave();

            Assert.Equal(NarratorPromptFile.Ensure(save.Store), NarratorPromptFile.Ensure(save.Store));
        }

        // ---- Fidelity -----------------------------------------------------------------------------

        [Fact]
        public void What_the_player_writes_comes_back_byte_for_byte()
        {
            // Line endings included, and unmixed. A document store that tidied what it was handed
            // would make the file on disk stop matching what the editor saved.
            const string Written = "First line.\nSecond line.\r\nThird\tline, indented\n  and trailing.  ";

            using var save = new TempSave();

            save.Store.WriteSystemPrompt(Written);

            Assert.Equal(Written, save.Store.ReadSystemPrompt());
            Assert.Equal(Written, NarratorPromptFile.Read(save.Store));
        }

        [Fact]
        public void Markup_rules_and_braces_survive_the_round_trip()
        {
            // The prompt is largely about square brackets and placeholder braces. Nothing may treat
            // either as syntax of its own.
            const string Written =
                "Mark items as [item](itm_1) and use {This} and {Player} in memories.";

            using var save = new TempSave();

            save.Store.WriteSystemPrompt(Written);

            Assert.Equal(Written, save.Store.ReadSystemPrompt());
        }

        [Fact]
        public void A_prompt_written_in_another_script_survives()
        {
            const string Written = "Narrate in Norwegian. Fjorden er kald. Пиши кратко. 竜が来る。";

            using var save = new TempSave();

            save.Store.WriteSystemPrompt(Written);

            Assert.Equal(Written, save.Store.ReadSystemPrompt());
        }

        [Fact]
        public void The_file_is_written_without_a_byte_order_mark()
        {
            // Shared with every other document here: a preamble is invisible in an editor and would
            // reach the model as a stray character at the head of its instructions.
            using var save = new TempSave();

            NarratorPromptFile.Ensure(save.Store);

            var bytes = File.ReadAllBytes(Path.Combine(save.Directory, FileName));

            Assert.False(bytes.AsSpan(0, 3).SequenceEqual(Encoding.UTF8.GetPreamble()));
        }

        [Fact]
        public void A_file_saved_with_a_byte_order_mark_reads_without_it()
        {
            // Notepad's encoding dropdown is right there, and a prompt that began with an invisible
            // character would be handed to the narrator that way.
            const string Written = "Be brief.";

            using var save = new TempSave();

            File.WriteAllText(
                Path.Combine(save.Directory, FileName),
                Written,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            Assert.Equal(Written, save.Store.ReadSystemPrompt());
        }

        [Fact]
        public void Writing_leaves_no_temporary_file_behind()
        {
            using var save = new TempSave();

            NarratorPromptFile.Ensure(save.Store);
            save.Store.WriteSystemPrompt("Rewritten.");

            Assert.Empty(save.TempFiles);
        }

        [Fact]
        public void The_path_names_the_file_inside_this_save()
        {
            using var save = new TempSave("Riverbend");

            Assert.Equal(Path.Combine(save.Directory, FileName), save.Store.SystemPromptPath);
        }

        // ---- The default itself -------------------------------------------------------------------

        [Fact]
        public void The_default_still_teaches_the_markup_the_parser_reads()
        {
            // The prompt and MarkupParser are a pair. This does not stop a player breaking that in
            // their own save - they are allowed to - but it stops the shipped default drifting from
            // the parser without anybody noticing.
            Assert.Contains("[Entity Name](id)", NarratorPromptFile.Default, StringComparison.Ordinal);
            Assert.Contains("[\"Spoken words go here.\"]", NarratorPromptFile.Default, StringComparison.Ordinal);
        }

        [Fact]
        public void The_default_still_teaches_the_numbered_choices()
        {
            // The second pairing between this text and code it cannot see. NarrationView.Wrap keeps
            // newlines and drops leading spaces, so the list only renders as a list while the brief
            // asks for one short line per choice - and nothing in the game parses the player's "2",
            // so this section is the only thing that makes a bare number mean anything at all.
            Assert.Contains("What do you do?", NarratorPromptFile.Default, StringComparison.Ordinal);
            Assert.Contains("\n1. ", NarratorPromptFile.Default, StringComparison.Ordinal);
        }

        [Fact]
        public void The_default_is_well_inside_the_length_worth_warning_about()
        {
            // If the shipped text ever crossed this line, every new save would open on a warning.
            Assert.True(
                NarratorPromptFile.Default.Length < NarratorPromptFile.WarnAboveCharacters,
                $"the default prompt is {NarratorPromptFile.Default.Length} characters");
        }

        [Fact]
        public void Every_tool_the_default_names_is_a_tool_that_exists()
        {
            var known = QuestTools.Definitions.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

            var named = Regex
                .Matches(NarratorPromptFile.Default, @"\b[a-z]+(?:_[a-z]+)+\b")
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // Guards the guard: a prompt that stopped naming tools would pass vacuously.
            Assert.NotEmpty(named);
            Assert.DoesNotContain(named, name => !known.Contains(name));
        }

        [Fact]
        public void The_default_still_names_the_one_tool_this_cannot_spell_check()
        {
            Assert.Contains("call roll", NarratorPromptFile.Default, StringComparison.Ordinal);
            Assert.Contains("Set hidden true", NarratorPromptFile.Default, StringComparison.Ordinal);
        }

        [Fact]
        public void The_default_is_plain_ascii()
        {
            Assert.DoesNotContain(NarratorPromptFile.Default, character => character > '\x7f');
        }
    }
}
