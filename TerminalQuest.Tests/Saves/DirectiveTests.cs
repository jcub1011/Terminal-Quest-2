using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    public sealed class DirectiveTests
    {
        [Fact]
        public void Directive_serializes_and_deserializes_via_SaveJsonContext()
        {
            var directive = new DirectiveFile
            {
                TargetJournalSequence = 42,
                Trigger = "LocationChanged",
                ExpiryTurn = 10,
                Tone = "Tense, cautious",
                PacingNote = "Introduce Rowan's rival",
                SecretPromotions = ["the sealed cellar"],
                RatifiedClaimSequences = [1, 2],
                Consumed = false,
            };

            var json = JsonSerializer.Serialize(directive, SaveJsonContext.Readable.DirectiveFile);
            var restored = JsonSerializer.Deserialize(json, SaveJsonContext.Readable.DirectiveFile);

            Assert.NotNull(restored);
            Assert.Equal(42, restored.TargetJournalSequence);
            Assert.Equal("LocationChanged", restored.Trigger);
            Assert.Equal(10, restored.ExpiryTurn);
            Assert.Equal("Tense, cautious", restored.Tone);
            Assert.Equal("Introduce Rowan's rival", restored.PacingNote);
            Assert.Equal(["the sealed cellar"], restored.SecretPromotions);
            Assert.Equal([1, 2], restored.RatifiedClaimSequences);
            Assert.False(restored.Consumed);
        }

        [Fact]
        public void Directive_IsActive_evaluates_expiry_and_consumed_state()
        {
            var directive = new DirectiveFile
            {
                ExpiryTurn = 5,
                Tone = "Foreboding",
                Consumed = false,
            };

            Assert.True(directive.IsActive(currentTurn: 3));
            Assert.True(directive.IsActive(currentTurn: 5));
            Assert.False(directive.IsActive(currentTurn: 6));

            directive.Consumed = true;
            Assert.False(directive.IsActive(currentTurn: 3));
        }

        [Fact]
        public void SaveStore_reads_and_writes_directive_file()
        {
            using var save = new TempSave();

            var original = new DirectiveFile
            {
                TargetJournalSequence = 7,
                Trigger = "StoryEvent",
                ExpiryTurn = 12,
                Tone = "Urgent",
                PacingNote = "Escalate the guard search",
                Consumed = false,
            };

            save.Store.WriteDirective(original);
            Assert.True(save.Has("directive.json"));

            var read = save.Store.ReadDirective();
            Assert.NotNull(read);
            Assert.Equal(7, read.TargetJournalSequence);
            Assert.Equal("StoryEvent", read.Trigger);
            Assert.Equal("Urgent", read.Tone);
            Assert.Equal("Escalate the guard search", read.PacingNote);
        }

        [Fact]
        public void QuestRender_renders_directive_formatting()
        {
            var directive = new DirectiveFile
            {
                Tone = "Suspenseful",
                PacingNote = "A noise in the darkness draws near.",
                SecretPromotions = ["the crypt key"],
            };

            var rendered = QuestRender.Directive(directive);

            Assert.Contains("[DIRECTIVE from Director]", rendered);
            Assert.Contains("Tone/Tension: Suspenseful", rendered);
            Assert.Contains("Pacing Guidance: A noise in the darkness draws near.", rendered);
            Assert.Contains("Activated Secrets in Play: the crypt key", rendered);
        }
    }
}
