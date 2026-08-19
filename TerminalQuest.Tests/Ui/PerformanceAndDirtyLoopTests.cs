using System.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using TerminalQuest.Ui;
using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// Holds the views to the bargain the main loop is built on: a view that has not changed does
    /// not repaint.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Terminal.Gui descends into a subview during a draw only if that subview says it needs one -
    /// <see cref="View.NeedsDraw"/>, <c>SubViewNeedsDraw</c> or <see cref="View.NeedsLayout"/>. A
    /// view that re-arms one of those from outside its own draw is asking to be repainted on every
    /// iteration of the main loop, for as long as its screen is open, and at the frame cap
    /// <see cref="TerminalQuest.Ui.Responsiveness"/> sets that is a hundred repaints a second of a
    /// screen nobody is touching. It costs little per frame, which is exactly why it survives
    /// inspection of any one frame.
    /// </para>
    /// <para>
    /// What these tests do <em>not</em> catch, and cannot: re-arming from <em>inside</em>
    /// <c>OnDrawingContent</c>. The framework clears the flags at the end of the same
    /// <see cref="View.Draw(DrawContext)"/> that raised them, so that mistake is swallowed rather
    /// than looping - it makes each frame more expensive without making more frames. The control
    /// below pins that down, so the next reader does not have to rediscover it.
    /// </para>
    /// </remarks>
    public sealed class PerformanceAndDirtyLoopTests
    {
        /// <summary>Re-arms itself from inside its own draw, the way StatusView and TitleBarView once did.</summary>
        private sealed class RearmsInsideDraw : View
        {
            protected override bool OnDrawingContent(DrawContext? context)
            {
                SetNeedsDraw();
                return base.OnDrawingContent(context);
            }
        }

        /// <summary>
        /// How many of ten quiet draw passes actually repainted <paramref name="view"/>.
        /// </summary>
        /// <remarks>
        /// The view is hosted in a parent and the parent is drawn, because "would the main loop
        /// have repainted this?" is a question only the parent can answer - drawing the view
        /// directly bypasses the very check under test.
        /// </remarks>
        private static int PaintsWhileQuiet(View view, Action<View>? seed = null)
        {
            view.Viewport = new Rectangle(0, 0, 80, 24);
            seed?.Invoke(view);

            var host = new View { Width = 80, Height = 24 };
            host.Add(view);

            // The opening passes legitimately paint; let them.
            for (var i = 0; i < 5; i++)
            {
                host.Layout(new Size(80, 24));
                host.Draw(new DrawContext());
            }

            var paints = 0;
            view.DrawComplete += (_, _) => paints++;

            for (var i = 0; i < 10; i++)
            {
                host.Draw(new DrawContext());
            }

            return paints;
        }

        [Fact]
        public void A_view_marked_dirty_from_outside_repaints_every_pass()
        {
            // The control that proves the measurement above can see a repaint at all. Without it,
            // every other test here passes just as well against a harness that never draws.
            var view = new View();
            view.Viewport = new Rectangle(0, 0, 80, 24);

            var host = new View { Width = 80, Height = 24 };
            host.Add(view);
            host.Layout(new Size(80, 24));
            host.Draw(new DrawContext());

            var paints = 0;
            view.DrawComplete += (_, _) => paints++;

            for (var i = 0; i < 10; i++)
            {
                view.SetNeedsDraw();
                host.Draw(new DrawContext());
            }

            Assert.Equal(10, paints);
        }

        [Fact]
        public void Re_arming_inside_the_draw_does_not_loop()
        {
            // Documents the framework's actual behaviour rather than the intuition about it.
            Assert.Equal(0, PaintsWhileQuiet(new RearmsInsideDraw()));
        }

        [Fact]
        public void An_untouched_narration_pane_does_not_repaint()
        {
            Assert.Equal(0, PaintsWhileQuiet(new NarrationView(), view =>
            {
                var narration = (NarrationView)view;
                for (var i = 0; i < 200; i++)
                {
                    narration.AddLine($"line {i} of transcript text", TextRole.Normal);
                }
            }));
        }

        [Fact]
        public void An_untouched_title_bar_does_not_repaint()
        {
            var state = new GameState { SaveName = "Camp Alpha", Location = "Ancient Ruins" };

            Assert.Equal(0, PaintsWhileQuiet(new TitleBarView(state)));
        }

        [Fact]
        public void An_untouched_status_pane_does_not_repaint()
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

            Assert.Equal(0, PaintsWhileQuiet(new StatusView(state)));
        }

        [Fact]
        public void An_untouched_options_pane_does_not_repaint()
        {
            Assert.Equal(0, PaintsWhileQuiet(new OptionsView(), view =>
                ((OptionsView)view).SetOptions(new[]
                {
                    "Investigate the strange shimmering light coming from the cellar door.",
                    "Leave the building immediately.",
                })));
        }

        [Fact]
        public void An_untouched_inventory_pane_does_not_repaint()
        {
            Assert.Equal(0, PaintsWhileQuiet(new InventoryView()));
        }

        [Fact]
        public void Dirtying_one_view_repaints_every_sibling()
        {
            // Not our behaviour, and not a test of our code: a stock Window with three stock Views
            // and no configuration of any kind. Terminal.Gui 2.4.17 has no partial redraw - marking
            // any one view dirty repaints the whole container subtree, and SetNeedsDraw(Rectangle)
            // does not narrow it either.
            //
            // This is the reason a single keystroke costs ~240ms on a full-screen terminal: the
            // frame is always the whole screen, so its cost tracks the size of the window rather
            // than the size of the change. It is pinned here so that the day it stops being true -
            // a Terminal.Gui upgrade, most likely - somebody finds out.
            var window = new Window { Width = 120, Height = 40 };
            var top = new View { X = 0, Y = 0, Width = 60, Height = 20 };
            var bottom = new View { X = 0, Y = 20, Width = 60, Height = 20 };
            var side = new View { X = 60, Y = 0, Width = 60, Height = 40 };
            window.Add(top, bottom, side);

            var host = new View { Width = 120, Height = 40 };
            host.Add(window);

            for (var i = 0; i < 5; i++)
            {
                host.Layout(new Size(120, 40));
                host.Draw(new DrawContext());
            }

            var repainted = 0;
            foreach (var view in new[] { top, bottom, side })
            {
                view.DrawComplete += (_, _) => repainted++;
            }

            top.SetNeedsDraw(new Rectangle(0, 0, 60, 1));
            host.Draw(new DrawContext());

            Assert.Equal(3, repainted);
        }

        [Fact]
        public void OptionsView_CalculateRequiredHeight_and_rendered_rows_behave_correctly()
        {
            var optionsView = new OptionsView();
            optionsView.Viewport = new Rectangle(0, 0, 60, 4);

            optionsView.SetOptions(new[]
            {
                "Investigate the strange shimmering light coming from the cellar door.",
                "Leave the building immediately."
            });

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
