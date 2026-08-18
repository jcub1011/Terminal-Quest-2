using System.Text;
using Spectre.Console;
using TerminalQuest.Saves;
using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    internal enum MainMenuAction
    {
        Continue,
        NewGame,
        LoadSave,
        ManageSaves,
        Settings,
        Quit
    }

    internal sealed record class MainMenuChoice(MainMenuAction Action, string Label, string? Extra = null);

    /// <summary>
    /// Interactive CLI controller for the main menu: continuing, loading, managing saves, and settings.
    /// </summary>
    internal static class MainMenuController
    {
        public static async Task<SaveStore?> RunAsync(
            AppSettings settings,
            ExternalEditor editor)
        {
            while (true)
            {
                var saves = SavePaths.List().ToList();
                var menuChoices = new List<MainMenuChoice>();

                if (saves.Count > 0)
                {
                    var latest = saves[0];
                    menuChoices.Add(new(MainMenuAction.Continue, $"Continue [bold green]'{Markup.Escape(latest.Name)}'[/]", $"Turn {latest.Turn}, {latest.SizeText}"));
                }

                menuChoices.Add(new(MainMenuAction.NewGame, "New Game"));

                if (saves.Count > 0)
                {
                    menuChoices.Add(new(MainMenuAction.LoadSave, "Load Save..."));
                    menuChoices.Add(new(MainMenuAction.ManageSaves, "Manage Saves (Rename, Duplicate, Reset, Delete)..."));
                }

                menuChoices.Add(new(MainMenuAction.Settings, "Settings"));
                menuChoices.Add(new(MainMenuAction.Quit, "Quit"));

                var choice = CliMenu.Prompt(
                    "[bold #8fb26a]Main Menu:[/]",
                    menuChoices,
                    c => string.IsNullOrEmpty(c.Extra) ? c.Label : $"{c.Label} [dim]({c.Extra})[/]",
                    c => c.Action.ToString(),
                    defaultIndex: 0,
                    renderHeader: () => RenderHeader(settings),
                    allowCancel: true,
                    cancelHint: "quit");

                if (choice is null || choice.Action == MainMenuAction.Quit)
                {
                    return null;
                }

                switch (choice.Action)
                {
                    case MainMenuAction.Continue:
                        if (saves.Count > 0)
                        {
                            return new SaveStore(SavePaths.Folder(saves[0].Name));
                        }
                        break;

                    case MainMenuAction.NewGame:
                        var newStore = await CreateNewSaveAsync(saves, editor);
                        if (newStore is not null)
                        {
                            return newStore;
                        }
                        break;

                    case MainMenuAction.LoadSave:
                        var pickedStore = PickSave(saves, settings);
                        if (pickedStore is not null)
                        {
                            return pickedStore;
                        }
                        break;

                    case MainMenuAction.ManageSaves:
                        await ManageSavesMenuAsync();
                        break;

                    case MainMenuAction.Settings:
                        await SettingsController.RunAsync(settings, editor);
                        break;

                    case MainMenuAction.Quit:
                        return null;
                }
            }
        }

        private static void RenderHeader(AppSettings settings)
        {
            var narratorDesc = settings.Provider switch
            {
                AgentProvider.ClaudeCode => $"Claude ({settings.ClaudeModel})",
                AgentProvider.LmStudio => $"{settings.OpenAiPreset} ({settings.LmStudioModel})",
                _ => settings.Provider.ToString()
            };

            AnsiConsole.Write(new FigletText("Terminal Quest").Color(new Color(0x8f, 0xb2, 0x6a)));
            AnsiConsole.MarkupLine($"[dim]Narrator:[/] [bold #e0b050]{Markup.Escape(narratorDesc)}[/] [dim]• v2.0[/]");
            AnsiConsole.WriteLine();
        }

        private static void RenderNewGameHeader()
        {
            AnsiConsole.Write(new Rule("[bold cyan]Create New Game[/]")
            {
                Border = BoxBorder.Rounded,
                Style = new Style(new Color(0x8f, 0xb2, 0x6a))
            });
            AnsiConsole.WriteLine();
        }

        private static Task<SaveStore?> CreateNewSaveAsync(List<SaveEntry> saves, ExternalEditor editor)
        {
            void RepaintNewGame()
            {
                AnsiConsole.Clear();
                RenderNewGameHeader();
            }

            RepaintNewGame();

            var name = CliPrompt.AskString(
                "[bold #e0b050]Enter name for new save:[/] ",
                allowEmpty: false,
                validator: input =>
                {
                    var trimmed = input.Trim();
                    if (string.IsNullOrEmpty(trimmed))
                    {
                        return ValidationResult.Error("Save name cannot be empty.");
                    }
                    if (!SavePaths.IsValidName(trimmed))
                    {
                        return ValidationResult.Error("Invalid save name. Must not contain invalid characters or reserved names.");
                    }
                    if (saves.Any(s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        return ValidationResult.Error($"A save named '{trimmed}' already exists.");
                    }
                    return ValidationResult.Success();
                },
                onRepaint: RepaintNewGame);

            if (string.IsNullOrEmpty(name))
            {
                return Task.FromResult<SaveStore?>(null);
            }

            var dir = SavePaths.Folder(name);
            Directory.CreateDirectory(dir);
            return Task.FromResult<SaveStore?>(new SaveStore(dir));
        }

        private static SaveStore? PickSave(List<SaveEntry> saves, AppSettings settings)
        {
            if (saves.Count == 0)
            {
                return null;
            }

            var picked = CliMenu.Prompt(
                "[bold #8fb26a]Select a save to load:[/]",
                saves,
                s => $"{s.Name,-16} | Turn {s.Turn,3} | {s.SizeText,7} | [dim]{s.LastPlayedText}[/]",
                s => s.Name,
                defaultIndex: 0,
                renderHeader: () =>
                {
                    AnsiConsole.Write(new Rule("[bold cyan]Load Save[/]")
                    {
                        Border = BoxBorder.Rounded,
                        Style = new Style(new Color(0x8f, 0xb2, 0x6a))
                    });
                    AnsiConsole.WriteLine();
                },
                allowCancel: true);

            if (string.IsNullOrEmpty(picked.Name))
            {
                return null;
            }

            return new SaveStore(SavePaths.Folder(picked.Name));
        }

        private static async Task ManageSavesMenuAsync()
        {
            while (true)
            {
                var saves = SavePaths.List().ToList();
                if (saves.Count == 0)
                {
                    return;
                }

                void RenderManageHeader()
                {
                    AnsiConsole.Write(new Rule("[bold cyan]Manage Saves[/]")
                    {
                        Border = BoxBorder.Rounded,
                        Style = new Style(new Color(0x8f, 0xb2, 0x6a))
                    });
                    AnsiConsole.WriteLine();
                }

                var picked = CliMenu.Prompt(
                    "[bold #8fb26a]Select a save to manage:[/]",
                    saves,
                    s => $"{s.Name,-16} | Turn {s.Turn,3} | {s.SizeText,7} | [dim]{s.LastPlayedText}[/]",
                    s => s.Name,
                    defaultIndex: 0,
                    renderHeader: RenderManageHeader,
                    allowCancel: true);

                if (string.IsNullOrEmpty(picked.Name))
                {
                    return;
                }

                var actions = new[]
                {
                    "Rename Save",
                    "Duplicate Save",
                    "Reset Save (Clear progress to start of character)",
                    "Delete Save",
                    "Reveal in File Explorer",
                    "Back"
                };

                var action = CliMenu.Prompt(
                    $"[bold #8fb26a]Action for save '{Markup.Escape(picked.Name)}':[/]",
                    actions,
                    a => a,
                    defaultIndex: 0,
                    renderHeader: RenderManageHeader,
                    allowCancel: true,
                    cancelHint: "go back");

                if (action is null || action.StartsWith("Back"))
                {
                    continue;
                }

                var folder = SavePaths.Folder(picked.Name);

                if (action.StartsWith("Rename"))
                {
                    var newName = CliPrompt.AskString(
                        "[bold #e0b050]New save name:[/] ",
                        allowEmpty: true);

                    if (!string.IsNullOrEmpty(newName) && SavePaths.IsValidName(newName))
                    {
                        try
                        {
                            SavePaths.Rename(picked.Name, newName);
                            AnsiConsole.MarkupLine("[green]Save renamed successfully.[/]");
                            Thread.Sleep(800);
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[bold red]Rename failed: {Markup.Escape(ex.Message)}[/]");
                            Thread.Sleep(1200);
                        }
                    }
                }
                else if (action.StartsWith("Duplicate"))
                {
                    try
                    {
                        var copyName = SavePaths.Duplicate(picked.Name);
                        AnsiConsole.MarkupLine($"[green]Save duplicated successfully as '{Markup.Escape(copyName)}'.[/]");
                        Thread.Sleep(800);
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[bold red]Duplicate failed: {Markup.Escape(ex.Message)}[/]");
                        Thread.Sleep(1200);
                    }
                }
                else if (action.StartsWith("Reset"))
                {
                    if (CliPrompt.Confirm($"Reset '{picked.Name}' back to turn 0?", defaultValue: false) == true)
                    {
                        try
                        {
                            SavePaths.Reset(picked.Name);
                            AnsiConsole.MarkupLine("[green]Save reset to turn 0.[/]");
                            Thread.Sleep(800);
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[bold red]Reset failed: {Markup.Escape(ex.Message)}[/]");
                            Thread.Sleep(1200);
                        }
                    }
                }
                else if (action.StartsWith("Delete"))
                {
                    if (CliPrompt.Confirm($"[bold red]Permanently delete save '{picked.Name}'?[/]", defaultValue: false) == true)
                    {
                        try
                        {
                            SavePaths.Delete(picked.Name);
                            AnsiConsole.MarkupLine("[green]Save deleted.[/]");
                            Thread.Sleep(800);
                        }
                        catch (Exception ex)
                        {
                            AnsiConsole.MarkupLine($"[bold red]Delete failed: {Markup.Escape(ex.Message)}[/]");
                            Thread.Sleep(1200);
                        }
                    }
                }
                else if (action.StartsWith("Reveal"))
                {
                    FileExplorer.TryOpen(folder, out _);
                }
            }
        }
    }
}
