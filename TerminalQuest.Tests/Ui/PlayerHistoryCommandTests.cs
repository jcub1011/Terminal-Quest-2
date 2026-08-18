using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class PlayerHistoryCommandTests
    {
        private static void Say(TempSave save, int turn, TranscriptVoice voice, string text) =>
            save.Store.Transcript.Append(new TranscriptEntry { Turn = turn, Voice = voice, Text = text });

        [Fact]
        public void History_command_exists_in_all_commands()
        {
            Assert.Contains(PlayerCommands.All, c => c.Name == "history");
            Assert.Contains(PlayerCommands.All, c => c.Name == "transcript" && c.IsAlias);
        }

        [Fact]
        public void History_on_empty_save_reports_nothing_spoken()
        {
            using var save = new TempSave();

            var result = PlayerCommands.Execute("/history", save.Store);

            Assert.Contains(result.Lines, l => l.ToPlainText().Contains("Nothing has been spoken"));
        }

        [Fact]
        public void History_shows_paged_chat_entries()
        {
            using var save = new TempSave();
            Say(save, 1, TranscriptVoice.Player, "go north");
            Say(save, 1, TranscriptVoice.Narrator, "You see a forest path.");
            Say(save, 2, TranscriptVoice.Player, "enter the forest");
            Say(save, 2, TranscriptVoice.Narrator, "The trees loom overhead.");

            var result = PlayerCommands.Execute("/history", save.Store);

            var text = string.Join("\n", result.Lines.Select(l => l.ToPlainText()));
            Assert.Contains("Chat History", text);
            Assert.Contains("> go north", text);
            Assert.Contains("You see a forest path.", text);
            Assert.Contains("> enter the forest", text);
        }

        [Fact]
        public void History_with_query_filters_matching_entries()
        {
            using var save = new TempSave();
            Say(save, 1, TranscriptVoice.Player, "talk to the goblin");
            Say(save, 1, TranscriptVoice.Narrator, "The goblin grins wickedly.");
            Say(save, 2, TranscriptVoice.Player, "drink potion");
            Say(save, 2, TranscriptVoice.Narrator, "Health restored.");

            var result = PlayerCommands.Execute("/history goblin", save.Store);

            var text = string.Join("\n", result.Lines.Select(l => l.ToPlainText()));
            Assert.Contains("goblin", text);
            Assert.DoesNotContain("potion", text);
        }

        [Fact]
        public void History_with_turn_filters_specific_turn()
        {
            using var save = new TempSave();
            Say(save, 1, TranscriptVoice.Player, "turn 1 action");
            Say(save, 1, TranscriptVoice.Narrator, "turn 1 reply");
            Say(save, 2, TranscriptVoice.Player, "turn 2 action");
            Say(save, 2, TranscriptVoice.Narrator, "turn 2 reply");

            var result = PlayerCommands.Execute("/history turn 2", save.Store);

            var text = string.Join("\n", result.Lines.Select(l => l.ToPlainText()));
            Assert.Contains("turn 2 action", text);
            Assert.DoesNotContain("turn 1 action", text);
        }

        [Fact]
        public void Transcript_alias_functions_identically()
        {
            using var save = new TempSave();
            Say(save, 1, TranscriptVoice.Player, "look around");
            Say(save, 1, TranscriptVoice.Narrator, "A dark cave.");

            var result = PlayerCommands.Execute("/transcript", save.Store);

            var text = string.Join("\n", result.Lines.Select(l => l.ToPlainText()));
            Assert.Contains("look around", text);
            Assert.Contains("A dark cave.", text);
        }
    }
}
