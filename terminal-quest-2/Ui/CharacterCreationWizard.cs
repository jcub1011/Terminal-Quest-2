using Spectre.Console;
using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Interactive CLI wizard for creating a new player character.
    /// </summary>
    internal static class CharacterCreationWizard
    {
        private enum WizardStep
        {
            Archetype = 1,
            Name = 2,
            Description = 3,
            Location = 4,
            Confirmation = 5
        }

        public static async Task<CharacterCreationResult?> RunAsync(
            SaveStore store,
            ExternalEditor editor)
        {
            void RenderWizardHeader()
            {
                AnsiConsole.Write(new Rule("[bold cyan]New Character Creation[/]")
                {
                    Border = BoxBorder.Rounded,
                    Style = new Style(new Color(0x8f, 0xb2, 0x6a))
                });
                AnsiConsole.WriteLine();
            }

            void RenderArchetypeDetails(ClassTemplate c)
            {
                var panelContent = new System.Text.StringBuilder();

                panelContent.AppendLine($"[bold #8fb26a]Health:[/] [bold #ffffff]{c.MaxHealth} HP[/]");
                panelContent.AppendLine();

                panelContent.AppendLine("[bold #8fb26a]Attributes:[/]");
                var attrList = string.Join(", ", c.Attributes.Select(a => $"[bold #ffffff]{a.Name}[/] {a.Score}"));
                panelContent.AppendLine($"  {attrList}");
                panelContent.AppendLine();

                panelContent.AppendLine("[bold #8fb26a]Starting Kit:[/]");
                foreach (var item in c.StartingItems)
                {
                    var count = item.Quantity > 1 ? $" (x{item.Quantity})" : string.Empty;
                    panelContent.AppendLine($"  • [bold #ffffff]{Markup.Escape(item.Name)}[/]{count} [dim]— {Markup.Escape(item.Description)}[/]");
                }
                panelContent.AppendLine();

                panelContent.AppendLine($"[dim italic]\"{Markup.Escape(c.Aptitude)}\"[/]");

                var card = new Panel(panelContent.ToString().TrimEnd())
                {
                    Header = new PanelHeader($" [bold cyan]{Markup.Escape(c.Name)} Summary[/] "),
                    Border = BoxBorder.Rounded,
                    BorderStyle = new Style(new Color(0x8f, 0xb2, 0x6a)),
                    Padding = new Padding(1, 0, 1, 0)
                };

                AnsiConsole.Write(card);
            }

            ClassTemplate? archetypeChoice = null;
            string? name = null;
            string? description = null;
            string? rawDescInput = null;
            string? startLocation = null;
            string? rawPlaceInput = null;

            var step = WizardStep.Archetype;

            while (true)
            {
                switch (step)
                {
                    case WizardStep.Archetype:
                        var classes = ClassTemplates.All;
                        var defaultIdx = archetypeChoice is not null
                            ? Math.Max(0, classes.ToList().FindIndex(c => c.Name == archetypeChoice.Name))
                            : 0;

                        archetypeChoice = CliMenu.Prompt(
                            "[bold #8fb26a]Choose your character archetype:[/]",
                            classes,
                            c => $"{c.Name} [dim]— HP {c.MaxHealth}, {Markup.Escape(c.Aptitude)}[/]",
                            c => c.Name,
                            defaultIndex: defaultIdx,
                            renderHeader: RenderWizardHeader,
                            allowCancel: true,
                            cancelHint: "cancel");

                        if (archetypeChoice is null)
                        {
                            return null;
                        }

                        step = WizardStep.Name;
                        break;

                    case WizardStep.Name:
                        AnsiConsole.Clear();
                        RenderWizardHeader();
                        RenderArchetypeDetails(archetypeChoice!);
                        AnsiConsole.WriteLine();

                        name = CliPrompt.AskString(
                            "[bold #e0b050]Character Name:[/] ",
                            defaultValue: name ?? archetypeChoice!.Name,
                            allowEmpty: false,
                            validator: n =>
                            {
                                var trimmed = n.Trim();
                                return trimmed.Length is > 0 and <= 40
                                    ? ValidationResult.Success()
                                    : ValidationResult.Error("Name must be between 1 and 40 characters.");
                            },
                            cancelHint: "go back");

                        if (name is null)
                        {
                            step = WizardStep.Archetype;
                            break;
                        }

                        step = WizardStep.Description;
                        break;

                    case WizardStep.Description:
                        AnsiConsole.Clear();
                        RenderWizardHeader();
                        RenderArchetypeDetails(archetypeChoice!);
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine($"[bold #8fb26a]Character Name:[/] [bold]{Markup.Escape(name!)}[/]");
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine("[dim]Press Enter to accept default, or edit who your character is (type /edit to open external editor):[/]");

                        rawDescInput = CliPrompt.AskString(
                            "[bold #e0b050]Who you are:[/] ",
                            defaultValue: rawDescInput ?? archetypeChoice!.Aptitude,
                            allowEmpty: true,
                            cancelHint: "go back");

                        if (rawDescInput is null)
                        {
                            step = WizardStep.Name;
                            break;
                        }

                        if (string.Equals(rawDescInput, "/edit", StringComparison.OrdinalIgnoreCase))
                        {
                            var edited = await editor.EditStringAsync(archetypeChoice!.Aptitude, "Character Backstory");
                            description = string.IsNullOrWhiteSpace(edited) ? archetypeChoice!.Aptitude : edited.Trim();
                        }
                        else
                        {
                            description = string.IsNullOrWhiteSpace(rawDescInput) ? archetypeChoice!.Aptitude : rawDescInput;
                        }

                        step = WizardStep.Location;
                        break;

                    case WizardStep.Location:
                        AnsiConsole.Clear();
                        RenderWizardHeader();
                        RenderArchetypeDetails(archetypeChoice!);
                        AnsiConsole.WriteLine();
                        AnsiConsole.MarkupLine($"[bold #8fb26a]Character Name:[/] [bold]{Markup.Escape(name!)}[/]");
                        AnsiConsole.MarkupLine($"[bold #8fb26a]Who you are:[/] {Markup.Escape(description!)}");
                        AnsiConsole.WriteLine();

                        rawPlaceInput = CliPrompt.AskString(
                            "[bold #e0b050]Where you begin (leave empty for narrator choice):[/] ",
                            defaultValue: rawPlaceInput,
                            allowEmpty: true,
                            cancelHint: "go back");

                        if (rawPlaceInput is null)
                        {
                            step = WizardStep.Description;
                            break;
                        }

                        startLocation = string.IsNullOrWhiteSpace(rawPlaceInput) ? null : rawPlaceInput;
                        step = WizardStep.Confirmation;
                        break;

                    case WizardStep.Confirmation:
                        AnsiConsole.Clear();
                        RenderWizardHeader();

                        var summaryPanel = new Panel(
                            $"[bold #8fb26a]Name:[/] [bold]{Markup.Escape(name!)}[/]\n" +
                            $"[bold #8fb26a]Archetype:[/] {Markup.Escape(archetypeChoice!.Name)} (HP {archetypeChoice!.MaxHealth})\n" +
                            $"[bold #8fb26a]Who you are:[/] {Markup.Escape(description!)}\n" +
                            $"[bold #8fb26a]Where you begin:[/] {(startLocation is not null ? Markup.Escape(startLocation) : "[dim]Narrator choice[/]")}")
                        {
                            Header = new PanelHeader(" [bold cyan]Character Summary[/] "),
                            Border = BoxBorder.Rounded,
                            BorderStyle = new Style(new Color(0x8f, 0xb2, 0x6a)),
                            Padding = new Padding(1, 0, 1, 0)
                        };

                        AnsiConsole.Write(summaryPanel);
                        AnsiConsole.WriteLine();

                        var confirmed = CliPrompt.Confirm("[bold green]Begin adventure with this character?[/]", defaultValue: true, cancelHint: "go back");

                        if (confirmed != true)
                        {
                            step = WizardStep.Location;
                            break;
                        }

                        try
                        {
                            NewGame.Create(store, name!, description!, archetypeChoice!, startLocation);
                            return new CharacterCreationResult(HasStartLocation: startLocation is not null, Error: null);
                        }
                        catch (Exception ex)
                        {
                            return new CharacterCreationResult(HasStartLocation: false, Error: ex.Message);
                        }
                }
            }
        }
    }

    internal readonly record struct CharacterCreationResult(bool HasStartLocation, string? Error);
}
