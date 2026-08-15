using TerminalQuest.Tests.Infrastructure;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// The small pure helpers behind the views: scroll maths, the theme's role mapping and the
    /// frame-rate clamp. Nothing here draws, so none of it needs a terminal.
    /// </summary>
    public sealed class ViewHelpersTests
    {
        // ---- The context gauge ----------------------------------------------------------------

        [Theory]
        [InlineData(0, "0")]
        [InlineData(842, "842")]
        [InlineData(999, "999")]
        [InlineData(1_000, "1k")]
        [InlineData(84_137, "84k")]
        [InlineData(999_999, "999k")]
        [InlineData(1_000_000, "1.0M")]
        [InlineData(1_048_576, "1.0M")]
        [InlineData(1_500_000, "1.5M")]
        [InlineData(9_900_000, "9.9M")]
        [InlineData(10_000_000, "10M")]
        [InlineData(int.MaxValue, "2147M")]
        public void A_token_count_abbreviates_to_the_columns_the_pane_can_spare(int tokens, string expected)
        {
            Assert.Equal(expected, StatusView.FormatTokens(tokens));
        }

        [Fact]
        public void An_abbreviated_count_never_outgrows_five_columns()
        {
            // The pane is twenty-seven wide and this shares a row with a label and a percentage, so
            // the width is a budget rather than an observation.
            foreach (var tokens in (int[])[0, 999, 1_000, 999_999, 1_000_000, 9_999_999, int.MaxValue])
            {
                Assert.True(
                    StatusView.FormatTokens(tokens).Length <= 5,
                    $"{tokens} formatted to '{StatusView.FormatTokens(tokens)}'");
            }
        }

        [Fact]
        public void An_empty_context_fills_nothing_and_a_full_one_fills_everything()
        {
            Assert.Equal(0, StatusView.BarFill(used: 0, window: 1000, width: 20));
            Assert.Equal(20, StatusView.BarFill(used: 1000, window: 1000, width: 20));
            Assert.Equal(20, StatusView.BarFill(used: 1200, window: 1000, width: 20));
        }

        [Fact]
        public void A_context_barely_started_still_shows_a_cell()
        {
            // Rounding would floor this to nothing, and a bar that reads empty while the narrator is
            // holding a conversation misleads about the one thing it is there to say.
            Assert.Equal(1, StatusView.BarFill(used: 1, window: 1_000_000, width: 27));
        }

        [Fact]
        public void A_context_nearly_full_still_shows_a_gap()
        {
            // The converse: rounding to full would claim there is no room left when there is.
            Assert.Equal(26, StatusView.BarFill(used: 999_999, window: 1_000_000, width: 27));
        }

        [Fact]
        public void The_fill_tracks_the_proportion_in_between()
        {
            Assert.Equal(5, StatusView.BarFill(used: 500, window: 1000, width: 10));
            Assert.Equal(3, StatusView.BarFill(used: 250, window: 1000, width: 10));
            Assert.Equal(8, StatusView.BarFill(used: 750, window: 1000, width: 10));
        }

        [Fact]
        public void A_gauge_with_nothing_to_divide_by_fills_nothing()
        {
            // A window of zero is a server that would not say how much it can hold. The caller draws
            // no bar at all in that case, and this must not throw on the way to finding out.
            Assert.Equal(0, StatusView.BarFill(used: 500, window: 0, width: 20));
            Assert.Equal(0, StatusView.BarFill(used: 500, window: -1, width: 20));
        }

        [Fact]
        public void A_pane_too_narrow_for_a_bar_does_not_throw_trying_to_draw_one()
        {
            // The clamp that keeps a gap at the full end has no room to work in a single column, and
            // Math.Clamp with a minimum above its maximum throws rather than picking one.
            Assert.Equal(0, StatusView.BarFill(used: 500, window: 1000, width: 1));
            Assert.Equal(1, StatusView.BarFill(used: 1000, window: 1000, width: 1));
            Assert.Equal(0, StatusView.BarFill(used: 500, window: 1000, width: 0));
        }

        // ---- Transcript follow -----------------------------------------------------------------

        [Fact]
        public void Following_keeps_the_last_row_at_the_foot()
        {
            Assert.Equal(90, NarrationView.NextOffset(0, totalRows: 100, viewportHeight: 10, following: true));
        }

        [Fact]
        public void A_transcript_shorter_than_the_pane_never_scrolls()
        {
            Assert.Equal(0, NarrationView.NextOffset(0, totalRows: 3, viewportHeight: 10, following: true));
            Assert.Equal(0, NarrationView.NextOffset(0, totalRows: 3, viewportHeight: 10, following: false));
        }

        [Fact]
        public void A_detached_reader_does_not_move_when_text_arrives()
        {
            // The whole point of the mechanism: the narrator writing must not cost the player their
            // place, however much of it arrives.
            Assert.Equal(20, NarrationView.NextOffset(20, totalRows: 100, viewportHeight: 10, following: false));
            Assert.Equal(20, NarrationView.NextOffset(20, totalRows: 500, viewportHeight: 10, following: false));
        }

        [Fact]
        public void Streaming_never_moves_a_detached_reader()
        {
            // The same, as the invariant rather than two examples of it.
            for (var y = 0; y <= 90; y++)
            {
                for (var growth = 1; growth <= 50; growth++)
                {
                    Assert.Equal(y, NarrationView.NextOffset(y, 100 + growth, viewportHeight: 10, following: false));
                }
            }
        }

        [Fact]
        public void A_detached_reader_is_pulled_back_by_a_shrink()
        {
            // A wider terminal re-wraps to fewer rows. Left alone the offset would strand the pane
            // past the end of the transcript, drawing blank space below the last line.
            Assert.Equal(40, NarrationView.NextOffset(95, totalRows: 50, viewportHeight: 10, following: false));
        }

        [Fact]
        public void The_offset_never_leaves_the_transcript()
        {
            Assert.Equal(0, NarrationView.NextOffset(-5, totalRows: 100, viewportHeight: 10, following: false));
        }

        [Fact]
        public void Following_is_exactly_the_last_row_being_on_screen()
        {
            Assert.True(NarrationView.AtBottom(90, totalRows: 100, viewportHeight: 10));
            Assert.False(NarrationView.AtBottom(89, totalRows: 100, viewportHeight: 10));
            Assert.True(NarrationView.AtBottom(0, totalRows: 3, viewportHeight: 10));
        }

        [Fact]
        public void A_reader_clamped_to_the_foot_rejoins()
        {
            // Growing the terminal can land a detached reader on the last row without their having
            // asked to return. Left detached there the pane would sit a screen short of the narrator
            // for the rest of the session.
            var settled = NarrationView.NextOffset(95, totalRows: 50, viewportHeight: 10, following: false);

            Assert.True(NarrationView.AtBottom(settled, totalRows: 50, viewportHeight: 10));
        }

        [Fact]
        public void A_pane_that_has_never_been_drawn_still_follows()
        {
            // Height is zero until the first layout. A transcript must not decide it has been left
            // behind before it has been shown once.
            Assert.Equal(0, NarrationView.BottomOffsetFor(totalRows: 10, viewportHeight: 0));
            Assert.True(NarrationView.AtBottom(0, totalRows: 10, viewportHeight: 0));
        }

        // ---- Theme -----------------------------------------------------------------------------

        [Fact]
        public void Every_role_has_an_ink()
        {
            // A role added without a colour would silently draw as whatever the default is.
            foreach (var role in Enum.GetValues<TextRole>())
            {
                var ink = Theme.For(role);

                Assert.True(ink.Foreground != default || role == TextRole.Normal);
            }
        }

        [Fact]
        public void The_games_own_voices_are_distinguishable_from_narration()
        {
            // [roll] and [command] are the game speaking, and the player should be able to tell.
            Assert.NotEqual(Theme.For(TextRole.Normal), Theme.For(TextRole.Roll));
            Assert.NotEqual(Theme.For(TextRole.Normal), Theme.For(TextRole.Command));
        }

        [Fact]
        public void Danger_does_not_look_like_ordinary_narration()
        {
            Assert.NotEqual(Theme.For(TextRole.Normal), Theme.For(TextRole.Danger));
        }

        [Fact]
        public void Every_role_maps_to_an_attribute_without_throwing()
        {
            foreach (var role in Enum.GetValues<TextRole>())
            {
                _ = Theme.Attr(role);
            }
        }

        [Fact]
        public void A_scheme_can_be_built_without_a_running_application()
        {
            Assert.NotNull(Theme.CreateScheme());
        }

        // ---- Frame rate clamp ---------------------------------------------------------------------

        [Collection(EnvironmentCollection.Name)]
        [Trait(Categories.Name, Categories.Environment)]
        public sealed class FrameRateTests
        {
            private static ushort CapWith(string? value)
            {
                var previous = Environment.GetEnvironmentVariable("TQ_FPS");

                try
                {
                    Environment.SetEnvironmentVariable("TQ_FPS", value);
                    return Responsiveness.Cap();
                }
                finally
                {
                    Environment.SetEnvironmentVariable("TQ_FPS", previous);
                }
            }

            [Theory]
            [InlineData(null)]
            [InlineData("")]
            [InlineData("not a number")]
            [InlineData("-1")]
            [InlineData("99999")]   // beyond ushort, so the parse fails rather than clamping
            [InlineData("1.5")]
            public void An_unusable_setting_falls_back_to_the_default(string? value)
            {
                // A typo must not stall the loop or spin it.
                Assert.Equal(100, CapWith(value));
            }

            [Theory]
            [InlineData("1", 20)]
            [InlineData("0", 20)]
            [InlineData("20", 20)]
            [InlineData("60", 60)]
            [InlineData("1000", 1000)]
            [InlineData("2000", 1000)]
            public void A_usable_setting_is_clamped_into_range(string value, int expected)
            {
                Assert.Equal(expected, CapWith(value));
            }
        }
    }
}
