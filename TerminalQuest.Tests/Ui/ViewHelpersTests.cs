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
        // ---- Scroll window --------------------------------------------------------------------

        [Fact]
        public void A_list_that_fits_never_scrolls()
        {
            Assert.Equal(0, ThemedView.ScrollWindowStart(0, count: 3, height: 10));
            Assert.Equal(0, ThemedView.ScrollWindowStart(2, count: 3, height: 10));
        }

        [Fact]
        public void The_selection_sits_mid_pane_once_the_list_is_taller_than_it()
        {
            Assert.Equal(45, ThemedView.ScrollWindowStart(50, count: 100, height: 10));
        }

        [Fact]
        public void The_window_never_runs_off_the_top()
        {
            Assert.Equal(0, ThemedView.ScrollWindowStart(0, count: 100, height: 10));
            Assert.Equal(0, ThemedView.ScrollWindowStart(3, count: 100, height: 10));
        }

        [Fact]
        public void The_window_never_runs_off_the_bottom()
        {
            Assert.Equal(90, ThemedView.ScrollWindowStart(99, count: 100, height: 10));
        }

        [Fact]
        public void The_selected_row_is_always_inside_the_window()
        {
            // The property the whole calculation exists for.
            for (var count = 1; count <= 40; count++)
            {
                for (var height = 1; height <= 20; height++)
                {
                    for (var selected = 0; selected < count; selected++)
                    {
                        var start = ThemedView.ScrollWindowStart(selected, count, height);

                        Assert.True(start >= 0);

                        if (count > height)
                        {
                            Assert.InRange(selected, start, start + height - 1);
                        }
                    }
                }
            }
        }

        [Fact]
        public void An_empty_list_starts_at_the_top()
        {
            Assert.Equal(0, ThemedView.ScrollWindowStart(0, count: 0, height: 10));
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
                Assert.Equal(200, CapWith(value));
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
