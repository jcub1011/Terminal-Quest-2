using Xunit;

namespace TerminalQuest.Tests
{
    public sealed class AppVersionTests
    {
        [Fact]
        public void Current_matches_project_version()
        {
            Assert.Equal("1.1.0", AppVersion.Current);
        }

        [Fact]
        public void Display_uses_v_prefixed_tag_form()
        {
            Assert.Equal("v1.1.0", AppVersion.Display);
        }

        [Fact]
        public void Current_has_no_build_metadata_suffix()
        {
            Assert.DoesNotContain("+", AppVersion.Current);
            Assert.False(string.IsNullOrWhiteSpace(AppVersion.Current));
        }
    }
}
