using System.Text;
using System.Text.RegularExpressions;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The narrator's instructions: tools loaded fresh from assets, and story prompt saved per-save.
    /// </summary>
    public sealed class NarratorPromptFileTests
    {
        private const string FileName = "narrator-story.txt";
        private const string LegacyFileName = "system-prompt.txt";

        // ---- Falling back -------------------------------------------------------------------------

        [Fact]
        public void A_save_without_the_file_reads_as_the_default()
        {
            using var save = new TempSave();

            Assert.Equal(NarratorPromptFile.StoryDefault, NarratorPromptFile.Read(save.Store));

            // Reading must not create it. Only Ensure does that, and only where it is safe to.
            Assert.False(save.Has(FileName));
        }

        [Fact]
        public void An_absent_file_reads_as_null_rather_than_empty()
        {
            using var save = new TempSave();

            Assert.Null(save.Store.ReadNarratorStory());
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\r\n\r\n")]
        public void An_empty_file_falls_back_to_the_default(string contents)
        {
            using var save = new TempSave();

            save.WriteRaw(FileName, contents);

            Assert.Equal(NarratorPromptFile.StoryDefault, NarratorPromptFile.Read(save.Store));
        }

        // ---- Seeding ------------------------------------------------------------------------------

        [Fact]
        public void Ensure_writes_the_default_and_returns_it()
        {
            using var save = new TempSave();

            var seeded = NarratorPromptFile.Ensure(save.Store);

            Assert.True(save.Has(FileName));
            Assert.Equal(NarratorPromptFile.StoryDefault.ReplaceLineEndings(), seeded);
            Assert.Equal(seeded, save.ReadRaw(FileName));
        }

        [Fact]
        public void The_seeded_file_uses_this_platforms_line_endings()
        {
            using var save = new TempSave();

            var seeded = NarratorPromptFile.Ensure(save.Store);

            Assert.Contains(Environment.NewLine, seeded, StringComparison.Ordinal);
            Assert.Equal(NarratorPromptFile.StoryDefault.ReplaceLineEndings(), seeded);
        }

        [Fact]
        public void Ensure_leaves_an_edited_file_alone()
        {
            const string Written = "You are a laconic narrator. Two sentences, never three.";

            using var save = new TempSave();

            NarratorPromptFile.Ensure(save.Store);
            save.Store.WriteNarratorStory(Written);

            Assert.Equal(Written, NarratorPromptFile.Ensure(save.Store));
            Assert.Equal(Written, save.ReadRaw(FileName));
        }

        [Fact]
        public void Ensure_migrates_legacy_system_prompt_file()
        {
            const string LegacyPrompt = "Legacy custom narrator instructions.";

            using var save = new TempSave();
            save.WriteRaw(LegacyFileName, LegacyPrompt);

            var result = NarratorPromptFile.Ensure(save.Store);

            Assert.Equal(LegacyPrompt, result);
            Assert.True(save.Has(FileName));
            Assert.Equal(LegacyPrompt, save.ReadRaw(FileName));
        }

        [Fact]
        public void Ensure_reseeds_a_file_that_has_been_emptied()
        {
            using var save = new TempSave();

            save.WriteRaw(FileName, "   ");

            Assert.Equal(NarratorPromptFile.StoryDefault.ReplaceLineEndings(), NarratorPromptFile.Ensure(save.Store));
        }

        [Fact]
        public void Ensure_is_idempotent()
        {
            using var save = new TempSave();

            Assert.Equal(NarratorPromptFile.Ensure(save.Store), NarratorPromptFile.Ensure(save.Store));
        }

        [Fact]
        public void UpdateStory_overwrites_custom_file_with_latest_default()
        {
            using var save = new TempSave();

            save.Store.WriteNarratorStory("Old custom prompt");
            var updated = NarratorPromptFile.UpdateStory(save.Store);

            Assert.Equal(NarratorPromptFile.StoryDefault.ReplaceLineEndings(), updated);
            Assert.Equal(updated, save.Store.ReadNarratorStory());
        }

        // ---- Composition --------------------------------------------------------------------------

        [Fact]
        public void Compose_combines_tools_and_story_prompts()
        {
            using var save = new TempSave();
            const string CustomStory = "Custom dark fantasy setting.";
            save.Store.WriteNarratorStory(CustomStory);

            var composed = NarratorPromptFile.Compose(save.Store);

            Assert.Contains(CustomStory, composed, StringComparison.Ordinal);
            Assert.Contains(NarratorPromptFile.ToolsDefault.Trim(), composed, StringComparison.Ordinal);
            Assert.Contains("---", composed, StringComparison.Ordinal);
        }

        // ---- Fidelity -----------------------------------------------------------------------------

        [Fact]
        public void What_the_player_writes_comes_back_byte_for_byte()
        {
            const string Written = "First line.\nSecond line.\r\nThird\tline, indented\n  and trailing.  ";

            using var save = new TempSave();

            save.Store.WriteNarratorStory(Written);

            Assert.Equal(Written, save.Store.ReadNarratorStory());
            Assert.Equal(Written, NarratorPromptFile.Read(save.Store));
        }

        [Fact]
        public void Markup_rules_and_braces_survive_the_round_trip()
        {
            const string Written =
                "Mark items as [item](itm_1) and use {This} and {Player} in memories.";

            using var save = new TempSave();

            save.Store.WriteNarratorStory(Written);

            Assert.Equal(Written, save.Store.ReadNarratorStory());
        }

        [Fact]
        public void A_prompt_written_in_another_script_survives()
        {
            const string Written = "Narrate in Norwegian. Fjorden er kald. Пиши кратко. 竜が来る。";

            using var save = new TempSave();

            save.Store.WriteNarratorStory(Written);

            Assert.Equal(Written, save.Store.ReadNarratorStory());
        }

        [Fact]
        public void The_file_is_written_without_a_byte_order_mark()
        {
            using var save = new TempSave();

            NarratorPromptFile.Ensure(save.Store);

            var bytes = File.ReadAllBytes(Path.Combine(save.Directory, FileName));

            Assert.False(bytes.AsSpan(0, 3).SequenceEqual(Encoding.UTF8.GetPreamble()));
        }

        [Fact]
        public void A_file_saved_with_a_byte_order_mark_reads_without_it()
        {
            const string Written = "Be brief.";

            using var save = new TempSave();

            File.WriteAllText(
                Path.Combine(save.Directory, FileName),
                Written,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            Assert.Equal(Written, save.Store.ReadNarratorStory());
        }

        [Fact]
        public void Writing_leaves_no_temporary_file_behind()
        {
            using var save = new TempSave();

            NarratorPromptFile.Ensure(save.Store);
            save.Store.WriteNarratorStory("Rewritten.");

            Assert.Empty(save.TempFiles);
        }

        [Fact]
        public void The_path_names_the_file_inside_this_save()
        {
            using var save = new TempSave("Riverbend");

            Assert.Equal(Path.Combine(save.Directory, FileName), save.Store.NarratorStoryPath);
        }

        // ---- The tools & story defaults -----------------------------------------------------------

        [Fact]
        public void The_tools_default_teaches_the_markup_the_parser_reads()
        {
            Assert.Contains("[Entity Name](id)", NarratorPromptFile.ToolsDefault, StringComparison.Ordinal);
            Assert.Contains("[\"Spoken words go here.\"]", NarratorPromptFile.ToolsDefault, StringComparison.Ordinal);
        }

        [Fact]
        public void The_tools_default_teaches_the_numbered_choices()
        {
            Assert.Contains("present_options", NarratorPromptFile.ToolsDefault, StringComparison.Ordinal);
            Assert.Contains("NUMBERED CHOICES", NarratorPromptFile.ToolsDefault, StringComparison.Ordinal);
        }

        [Fact]
        public void The_composed_default_is_well_inside_the_length_worth_warning_about()
        {
            var composed = NarratorPromptFile.Compose(NarratorPromptFile.ToolsDefault, NarratorPromptFile.StoryDefault);
            Assert.True(
                composed.Length < NarratorPromptFile.WarnAboveCharacters,
                $"the composed default prompt is {composed.Length} characters");
        }

        [Fact]
        public void Every_tool_the_tools_default_names_is_a_tool_that_exists()
        {
            var known = QuestTools.Definitions.Select(tool => tool.Name).ToHashSet(StringComparer.Ordinal);

            var named = Regex
                .Matches(NarratorPromptFile.ToolsDefault, @"\b[a-z]+(?:_[a-z]+)+\b")
                .Select(match => match.Value)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            Assert.NotEmpty(named);
            Assert.DoesNotContain(named, name => !known.Contains(name));
        }

        [Fact]
        public void The_tools_default_names_call_roll()
        {
            Assert.Contains("call roll", NarratorPromptFile.ToolsDefault, StringComparison.Ordinal);
            Assert.Contains("Set hidden true", NarratorPromptFile.ToolsDefault, StringComparison.Ordinal);
        }

        [Fact]
        public void The_defaults_are_plain_ascii()
        {
            Assert.DoesNotContain(NarratorPromptFile.ToolsDefault, character => character > '\x7f');
            Assert.DoesNotContain(NarratorPromptFile.StoryDefault, character => character > '\x7f');
        }
    }
}
