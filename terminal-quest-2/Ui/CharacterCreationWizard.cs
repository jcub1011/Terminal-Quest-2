using Spectre.Console;
using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Interactive CLI wizard for creating a new player character.
    /// </summary>
    internal static class CharacterCreationWizard
    {
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

            // 1. Archetype Selection
            var classes = ClassTemplates.All;
            var archetypeChoice = CliMenu.Prompt(
                "[bold #8fb26a]Choose your character archetype:[/]",
                classes,
                c => $"{c.Name} [dim]— HP {c.MaxHealth}, {Markup.Escape(c.Aptitude)}[/]",
                c => c.Name,
                defaultIndex: 0,
                renderHeader: RenderWizardHeader,
                allowCancel: true);

            if (archetypeChoice is null)
            {
                return null;
            }

            // 2. Character Name
            AnsiConsole.Clear();
            RenderWizardHeader();
            RenderArchetypeDetails(archetypeChoice);
            AnsiConsole.WriteLine();

            var name = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold #e0b050]Character Name:[/] ")
                    .DefaultValue(archetypeChoice.Name)
                    .Validate(n =>
                    {
                        var trimmed = n.Trim();
                        return trimmed.Length is > 0 and <= 40
                            ? ValidationResult.Success()
                            : ValidationResult.Error("[red]Name must be between 1 and 40 characters.[/]");
                    })).Trim();

            // 3. Who you are / Description
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine("[dim]Press Enter to accept default, or edit who your character is (type /edit to open external editor):[/]");
            var descInput = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold #e0b050]Who you are:[/] ")
                    .DefaultValue(archetypeChoice.Aptitude)
                    .AllowEmpty()).Trim();

            string description;
            if (string.Equals(descInput, "/edit", StringComparison.OrdinalIgnoreCase))
            {
                var edited = await editor.EditStringAsync(archetypeChoice.Aptitude, "Character Backstory");
                description = string.IsNullOrWhiteSpace(edited) ? archetypeChoice.Aptitude : edited.Trim();
            }
            else
            {
                description = string.IsNullOrWhiteSpace(descInput) ? archetypeChoice.Aptitude : descInput;
            }

            // 4. Starting Location
            AnsiConsole.WriteLine();
            var placeInput = AnsiConsole.Prompt(
                new TextPrompt<string>("[bold #e0b050]Where you begin (leave empty for narrator choice):[/] ")
                    .AllowEmpty()).Trim();

            var startLocation = string.IsNullOrWhiteSpace(placeInput) ? null : placeInput;

            // 5. Summary Card & Confirmation
            AnsiConsole.Clear();
            RenderWizardHeader();

            var summaryPanel = new Panel(
                $"[bold #8fb26a]Name:[/] [bold]{Markup.Escape(name)}[/]\n" +
                $"[bold #8fb26a]Archetype:[/] {Markup.Escape(archetypeChoice.Name)} (HP {archetypeChoice.MaxHealth})\n" +
                $"[bold #8fb26a]Who you are:[/] {Markup.Escape(description)}\n" +
                $"[bold #8fb26a]Where you begin:[/] {(startLocation is not null ? Markup.Escape(startLocation) : "[dim]Narrator choice[/]")}")
            {
                Header = new PanelHeader(" [bold cyan]Character Summary[/] "),
                Border = BoxBorder.Rounded,
                BorderStyle = new Style(new Color(0x8f, 0xb2, 0x6a)),
                Padding = new Padding(1, 0, 1, 0)
            };

            AnsiConsole.Write(summaryPanel);
            AnsiConsole.WriteLine();

            var confirmed = AnsiConsole.Prompt(
                new ConfirmationPrompt("[bold green]Begin adventure with this character?[/]")
                {
                    DefaultValue = true
                });

            if (!confirmed)
            {
                return null;
            }

            try
            {
                NewGame.Create(store, name, description, archetypeChoice, startLocation);
                return new CharacterCreationResult(HasStartLocation: startLocation is not null, Error: null);
            }
            catch (Exception ex)
            {
                return new CharacterCreationResult(HasStartLocation: false, Error: ex.Message);
            }
        }
    }

    internal readonly record struct CharacterCreationResult(bool HasStartLocation, string? Error);
}
