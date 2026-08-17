using Spectre.Console;
using TerminalQuest.Ui;
using Xunit;

namespace TerminalQuest.Tests.Ui
{
    public sealed class ViewHelpersTests
    {
        [Fact]
        public void Every_role_has_an_ink()
        {
            foreach (var role in Enum.GetValues<TextRole>())
            {
                var ink = Theme.For(role);
                Assert.True(ink.Foreground != default || role == TextRole.Normal);
                Assert.False(string.IsNullOrEmpty(ink.MarkupTag));
            }
        }

        [Fact]
        public void The_games_own_voices_are_distinguishable_from_narration()
        {
            Assert.NotEqual(Theme.For(TextRole.Normal), Theme.For(TextRole.Roll));
            Assert.NotEqual(Theme.For(TextRole.Normal), Theme.For(TextRole.Command));
        }

        [Fact]
        public void Danger_does_not_look_like_ordinary_narration()
        {
            Assert.NotEqual(Theme.For(TextRole.Normal), Theme.For(TextRole.Danger));
        }

        [Fact]
        public void Every_role_maps_to_a_style_without_throwing()
        {
            foreach (var role in Enum.GetValues<TextRole>())
            {
                var style = Theme.StyleFor(role);
                Assert.NotNull(style);
            }
        }

        [Fact]
        public void Format_escapes_and_wraps_with_markup_tags()
        {
            var formatted = Theme.Format("Hello [World]", TextRole.Item);
            Assert.Contains("[[World]]", formatted);
            Assert.StartsWith("[bold #e0b050]", formatted);
            Assert.EndsWith("[/]", formatted);
        }

        [Fact]
        public void Format_empty_or_null_returns_empty()
        {
            Assert.Equal(string.Empty, Theme.Format("", TextRole.Normal));
            Assert.Equal(string.Empty, Theme.Format(null!, TextRole.Normal));
        }
    }
}
