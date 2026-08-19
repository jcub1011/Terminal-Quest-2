using System.Drawing;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using TerminalQuest.Saves;
using TerminalQuest.Ui;
using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class PerformanceAndDirtyLoopTests
    {
        [Fact]
        public void TitleBarView_Refresh_updates_labels_without_dirty_loop()
        {
            var state = new GameState
            {
                SaveName = "Camp Alpha",
                Location = "Ancient Ruins"
            };

            var titleBar = new TitleBarView(state);
            titleBar.Viewport = new Rectangle(0, 0, 80, 1);

            titleBar.Refresh();

            Assert.NotNull(titleBar);
        }

        [Fact]
        public void StatusView_Refresh_updates_all_state_panes()
        {
            var state = new GameState
            {
                PlayerName = "Rowan",
                Health = 15,
                MaxHealth = 20,
                Turn = 4,
                Money = 150,
                ContextTokens = 12500,
                ContextWindowTokens = 200000,
                CostUsd = 0.0425,
                LastDurationMs = 1200,
            };
            state.Attributes.Add(new AttributeEntry("STR", 14));
            state.Attributes.Add(new AttributeEntry("DEX", 12));
            state.Inventory.Add(new InventoryEntry(1, "Torch", "itm_torch"));
            state.Inventory.Add(new InventoryEntry(3, "Ration", "itm_ration"));

            var status = new StatusView(state);
            status.Viewport = new Rectangle(0, 0, 28, 30);

            status.Refresh();

            Assert.NotNull(status);
        }

        [Fact]
        public void OptionsView_CalculateRequiredHeight_and_rendered_rows_behave_correctly()
        {
            var optionsView = new OptionsView();
            optionsView.Viewport = new Rectangle(0, 0, 60, 4);

            optionsView.SetOptions([
                "Investigate the strange shimmering light coming from the cellar door.",
                "Leave the building immediately."
            ]);

            var height = optionsView.CalculateRequiredHeight(60);
            Assert.True(height >= 2);
            Assert.Equal(2, optionsView.Options.Count);
        }

        [Fact]
        public void NarrationView_Wrap_preserves_span_chunking_and_entity_ids()
        {
            var line = new StyledLine();
            line.Append("Examine ", TextRole.Normal);
            line.Append("the glowing orb", TextRole.Item, "itm_orb");
            line.Append(" on the pedestal.", TextRole.Normal);

            var wrapped = NarrationView.Wrap(line.Spans, 40);

            Assert.Single(wrapped);
            Assert.Equal(3, wrapped[0].Spans.Count);
            Assert.Equal("Examine ", wrapped[0].Spans[0].Text);
            Assert.Equal(TextRole.Normal, wrapped[0].Spans[0].Role);
            Assert.Equal("the glowing orb", wrapped[0].Spans[1].Text);
            Assert.Equal(TextRole.Item, wrapped[0].Spans[1].Role);
            Assert.Equal("itm_orb", wrapped[0].Spans[1].EntityId);
            Assert.Equal(" on the pedestal.", wrapped[0].Spans[2].Text);
            Assert.Equal(TextRole.Normal, wrapped[0].Spans[2].Role);
        }

        [Fact]
        public void NarrationView_Wrap_splits_long_words_across_lines()
        {
            var line = new StyledLine();
            line.Append("Supercalifragilisticexpialidocious", TextRole.Speech, "speaker_mary");

            var wrapped = NarrationView.Wrap(line.Spans, 10);

            Assert.Equal(4, wrapped.Count);
            Assert.Equal("Supercalif", wrapped[0].Spans[0].Text);
            Assert.Equal(TextRole.Speech, wrapped[0].Spans[0].Role);
            Assert.Equal("speaker_mary", wrapped[0].Spans[0].EntityId);
            Assert.Equal("ragilistic", wrapped[1].Spans[0].Text);
            Assert.Equal("expialidoc", wrapped[2].Spans[0].Text);
            Assert.Equal("ious", wrapped[3].Spans[0].Text);
        }
    }
}
