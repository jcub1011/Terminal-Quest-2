using System.Diagnostics;
using System.Text;
using Spectre.Console;
using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Launches an external editor on temporary scratch files or save prompt files.
    /// </summary>
    internal sealed class ExternalEditor
    {
        private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        private readonly Func<string> _command;

        public ExternalEditor(Func<string> command)
        {
            _command = command ?? throw new ArgumentNullException(nameof(command));
        }

        private static string ScratchDirectory => Path.Combine(PathProvider.Root, "edit");

        public async Task<string?> EditStringAsync(string initialText, string? promptTitle = null)
        {
            var command = _command().Trim();
            if (string.IsNullOrEmpty(command))
            {
                AnsiConsole.MarkupLine("[bold red]No editor configured in Settings.[/]");
                return null;
            }

            if (!EditorCommandLine.TryParse(command, out var launch, out var reason))
            {
                AnsiConsole.MarkupLine($"[bold red]{Markup.Escape(reason ?? "Invalid editor command.")}[/]");
                return null;
            }

            var path = Path.Combine(ScratchDirectory, $"tq-edit-{Guid.NewGuid():N}.txt");

            try
            {
                Directory.CreateDirectory(ScratchDirectory);
                Sweep();
                await File.WriteAllTextAsync(path, (initialText ?? string.Empty).ReplaceLineEndings("\r\n"), Utf8NoBom);

                if (!string.IsNullOrEmpty(promptTitle))
                {
                    AnsiConsole.MarkupLine($"[dim]Opening external editor ({Markup.Escape(launch.Display)})...[/]");
                }

                var success = await RunProcessAsync(launch, path);
                if (!success)
                {
                    return null;
                }

                if (File.Exists(path))
                {
                    var result = await File.ReadAllTextAsync(path, Utf8NoBom);
                    return result;
                }

                return initialText;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Failed to run editor: {Markup.Escape(ex.Message)}[/]");
                return null;
            }
            finally
            {
                TryDelete(path);
            }
        }

        public async Task<bool> EditFileAsync(string path)
        {
            var command = _command().Trim();
            if (string.IsNullOrEmpty(command))
            {
                AnsiConsole.MarkupLine("[bold red]No editor configured in Settings.[/]");
                return false;
            }

            if (!EditorCommandLine.TryParse(command, out var launch, out var reason))
            {
                AnsiConsole.MarkupLine($"[bold red]{Markup.Escape(reason ?? "Invalid editor command.")}[/]");
                return false;
            }

            try
            {
                var original = File.Exists(path) ? await File.ReadAllTextAsync(path, Utf8NoBom) : string.Empty;
                AnsiConsole.MarkupLine($"[dim]Editing {Markup.Escape(Path.GetFileName(path))} with {Markup.Escape(launch.Display)}...[/]");
                var success = await RunProcessAsync(launch, path);
                if (!success)
                {
                    return false;
                }

                var updated = File.Exists(path) ? await File.ReadAllTextAsync(path, Utf8NoBom) : string.Empty;
                return !string.Equals(original, updated, StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Failed to edit file: {Markup.Escape(ex.Message)}[/]");
                return false;
            }
        }

        private static async Task<bool> RunProcessAsync(EditorCommandLine launch, string filePath)
        {
            try
            {
                var psi = launch.ToStartInfo(filePath);
                using var process = Process.Start(psi);
                if (process is null)
                {
                    return false;
                }

                await process.WaitForExitAsync();
                return true;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Editor process error: {Markup.Escape(ex.Message)}[/]");
                return false;
            }
        }

        private static void Sweep()
        {
            try
            {
                if (!Directory.Exists(ScratchDirectory))
                {
                    return;
                }

                var threshold = DateTime.UtcNow - StaleAfter;
                foreach (var file in Directory.EnumerateFiles(ScratchDirectory, "tq-edit-*.txt"))
                {
                    if (File.GetLastWriteTimeUtc(file) < threshold)
                    {
                        TryDelete(file);
                    }
                }
            }
            catch
            {
                // Sweep is best-effort.
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}
