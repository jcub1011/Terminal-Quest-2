using TerminalQuest.Tests.Infrastructure;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// Turning the one string the player typed into the settings into a program to run.
    /// </summary>
    /// <remarks>
    /// Serialized: the success paths need a program that really exists, which means pointing
    /// <c>PATH</c> and <c>PATHEXT</c> at a folder of this test's own — process-wide state.
    /// </remarks>
    [Collection(EnvironmentCollection.Name)]
    [Trait(Categories.Name, Categories.Environment)]
    public sealed class EditorCommandLineTests
    {
        /// <summary>A folder holding a stand-in editor, put on <c>PATH</c> for the life of a test.</summary>
        private sealed class FakeEditor : IDisposable
        {
            private readonly string? _path;
            private readonly string? _pathExt;
            private readonly string? _comSpec;

            public FakeEditor(string fileName = "tq-editor.exe")
            {
                _path = Environment.GetEnvironmentVariable("PATH");
                _pathExt = Environment.GetEnvironmentVariable("PATHEXT");
                _comSpec = Environment.GetEnvironmentVariable("COMSPEC");

                Folder = Path.Combine(
                    Path.GetTempPath(),
                    "TerminalQuest.Tests",
                    Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(Folder);
                Executable = Path.Combine(Folder, fileName);
                File.WriteAllText(Executable, "not really a program");

                Environment.SetEnvironmentVariable("PATH", Folder);
                Environment.SetEnvironmentVariable("PATHEXT", ".COM;.EXE;.BAT;.CMD");
            }

            public string Folder { get; }

            public string Executable { get; }

            public void Dispose()
            {
                Environment.SetEnvironmentVariable("PATH", _path);
                Environment.SetEnvironmentVariable("PATHEXT", _pathExt);
                Environment.SetEnvironmentVariable("COMSPEC", _comSpec);

                try
                {
                    Directory.Delete(Folder, recursive: true);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        // ---- Refusals ----------------------------------------------------------------------

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void No_editor_set_is_reported_plainly(string? command)
        {
            Assert.False(EditorCommandLine.TryParse(command!, out _, out var reason));
            Assert.Equal("No editor is set.", reason);
        }

        [Fact]
        public void An_unclosed_quote_is_reported_rather_than_guessed_at()
        {
            // The player is either still typing or has made a mistake, and both want telling.
            Assert.False(EditorCommandLine.TryParse("\"C:\\Program Files\\thing", out _, out var reason));
            Assert.Contains("never closed", reason!, StringComparison.Ordinal);
        }

        [Fact]
        public void A_program_that_is_not_there_is_caught_before_the_launch()
        {
            // cmd's start would report a missing name onto a console nobody can see, which is
            // indistinguishable from an editor that opened and was closed again.
            using var editor = new FakeEditor();

            Assert.False(EditorCommandLine.TryParse("definitely-not-installed", out _, out var reason));
            Assert.Contains("Could not find", reason!, StringComparison.Ordinal);
        }

        [Fact]
        public void An_empty_quoted_name_is_refused()
        {
            Assert.False(EditorCommandLine.TryParse("\"\" -w", out _, out var reason));
            Assert.NotNull(reason);
        }

        // ---- Parsing -------------------------------------------------------------------------

        [Fact]
        public void A_bare_name_is_found_along_the_path()
        {
            using var editor = new FakeEditor();

            Assert.True(EditorCommandLine.TryParse("tq-editor", out var launch, out var reason));
            Assert.Null(reason);
            Assert.Equal("tq-editor", launch.Executable);
            Assert.Empty(launch.Arguments);
        }

        [Fact]
        public void A_name_with_its_extension_is_found_too()
        {
            using var editor = new FakeEditor();

            Assert.True(EditorCommandLine.TryParse("tq-editor.exe", out var launch, out _));
            Assert.Equal("tq-editor.exe", launch.Executable);
        }

        [Fact]
        public void A_bare_name_with_flags_splits_at_the_first_space()
        {
            using var editor = new FakeEditor();

            Assert.True(EditorCommandLine.TryParse("tq-editor -w --new", out var launch, out _));
            Assert.Equal("tq-editor", launch.Executable);
            Assert.Equal(["-w", "--new"], launch.Arguments);
        }

        [Fact]
        public void A_quoted_path_survives_the_spaces_in_it()
        {
            using var editor = new FakeEditor();

            Assert.True(EditorCommandLine.TryParse($"\"{editor.Executable}\" -w", out var launch, out _));
            Assert.Equal(editor.Executable, launch.Executable);
            Assert.Equal(["-w"], launch.Arguments);
        }

        [Fact]
        public void An_unquoted_path_with_spaces_is_settled_by_asking_the_disk()
        {
            // The one genuinely ambiguous case: a whole path that resolves is taken whole.
            using var editor = new FakeEditor();
            var spaced = Path.Combine(editor.Folder, "my editor.exe");
            File.WriteAllText(spaced, "not really a program");

            Assert.True(EditorCommandLine.TryParse(spaced, out var launch, out _));
            Assert.Equal(spaced, launch.Executable);
            Assert.Empty(launch.Arguments);
        }

        [Fact]
        public void The_display_name_is_just_the_file_name()
        {
            using var editor = new FakeEditor();

            Assert.True(EditorCommandLine.TryParse($"\"{editor.Executable}\"", out var launch, out _));
            Assert.Equal("tq-editor.exe", launch.Display);
        }

        [Fact]
        public void A_quoted_argument_stays_one_argument()
        {
            using var editor = new FakeEditor();

            Assert.True(EditorCommandLine.TryParse("tq-editor \"two words\" -w", out var launch, out _));
            Assert.Equal(["two words", "-w"], launch.Arguments);
        }

        [Fact]
        public void An_argument_written_as_empty_quotes_survives_as_an_empty_one()
        {
            using var editor = new FakeEditor();

            Assert.True(EditorCommandLine.TryParse("tq-editor \"\" -w", out var launch, out _));
            Assert.Equal([string.Empty, "-w"], launch.Arguments);
        }

        [Fact]
        public void Surrounding_whitespace_is_ignored()
        {
            using var editor = new FakeEditor();

            Assert.True(EditorCommandLine.TryParse("   tq-editor   -w   ", out var launch, out _));
            Assert.Equal("tq-editor", launch.Executable);
            Assert.Equal(["-w"], launch.Arguments);
        }

        // ---- Launching ----------------------------------------------------------------------------

        [Fact]
        public void The_editor_is_started_through_a_shell_that_gives_it_its_own_console()
        {
            // Launched directly, a terminal editor would inherit this console and fight the game
            // for every keystroke.
            using var editor = new FakeEditor();
            Assert.True(EditorCommandLine.TryParse("tq-editor -w", out var launch, out _));

            var info = launch.ToStartInfo(@"C:\temp\scratch.txt");

            Assert.Equal(
                ["/c", "start", string.Empty, "/wait", "tq-editor", "-w", @"C:\temp\scratch.txt"],
                info.ArgumentList);
        }

        [Fact]
        public void The_window_title_argument_is_empty_so_start_still_has_a_program_to_run()
        {
            // start reads a leading quoted argument as the title; without the empty one it would
            // take the program's path as the title and then have nothing left to run.
            using var editor = new FakeEditor();
            Assert.True(EditorCommandLine.TryParse("tq-editor", out var launch, out _));

            var info = launch.ToStartInfo("file.txt");

            Assert.Equal("start", info.ArgumentList[1]);
            Assert.Equal(string.Empty, info.ArgumentList[2]);
        }

        [Fact]
        public void The_file_is_always_the_last_argument()
        {
            using var editor = new FakeEditor();
            Assert.True(EditorCommandLine.TryParse("tq-editor -w --new", out var launch, out _));

            Assert.Equal("file.txt", launch.ToStartInfo("file.txt").ArgumentList[^1]);
        }

        [Fact]
        public void Nothing_is_redirected_because_an_editor_needs_a_screen()
        {
            using var editor = new FakeEditor();
            Assert.True(EditorCommandLine.TryParse("tq-editor", out var launch, out _));

            var info = launch.ToStartInfo("file.txt");

            Assert.False(info.RedirectStandardInput);
            Assert.False(info.RedirectStandardOutput);
            Assert.False(info.UseShellExecute);
            Assert.True(info.CreateNoWindow);
        }

        [Fact]
        public void The_shell_comes_from_the_environment_when_it_names_one()
        {
            using var editor = new FakeEditor();
            Environment.SetEnvironmentVariable("COMSPEC", @"C:\custom\cmd.exe");
            Assert.True(EditorCommandLine.TryParse("tq-editor", out var launch, out _));

            Assert.Equal(@"C:\custom\cmd.exe", launch.ToStartInfo("file.txt").FileName);
        }

        [Fact]
        public void The_shell_falls_back_when_the_environment_names_none()
        {
            using var editor = new FakeEditor();
            Environment.SetEnvironmentVariable("COMSPEC", null);
            Assert.True(EditorCommandLine.TryParse("tq-editor", out var launch, out _));

            Assert.Equal("cmd.exe", launch.ToStartInfo("file.txt").FileName);
        }

        [Fact]
        public void A_file_path_with_spaces_stays_one_argument()
        {
            // The whole reason arguments are added one at a time rather than as one string.
            using var editor = new FakeEditor();
            Assert.True(EditorCommandLine.TryParse("tq-editor", out var launch, out _));

            var info = launch.ToStartInfo(@"C:\my folder\scratch file.txt");

            Assert.Equal(@"C:\my folder\scratch file.txt", info.ArgumentList[^1]);
        }
    }
}
