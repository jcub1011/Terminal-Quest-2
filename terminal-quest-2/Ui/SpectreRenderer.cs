using Spectre.Console;
using TerminalQuest.Mcp;
using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Presentation utilities for rendering rich Spectre.Console widgets in Terminal Quest 2.
    /// </summary>
    internal static class SpectreRenderer
    {
        public static void RenderBanner(string saveName, int turn)
        {
            var rule = new Rule($"[bold cyan]Terminal Quest[/] [dim]•[/] [bold #8fb26a]{Markup.Escape(saveName)}[/] [dim]• Turn {turn}[/]")
            {
                Border = BoxBorder.Double,
                Style = new Style(new Color(0x8f, 0xb2, 0x6a))
            };
            AnsiConsole.WriteLine();
            AnsiConsole.Write(rule);
            AnsiConsole.WriteLine();
        }

        public static void RenderRoll(string text)
        {
            var rule = new Rule($"[bold #9a8fd0]🎲 {Markup.Escape(text)}[/]")
            {
                Border = BoxBorder.Rounded,
                Style = new Style(new Color(0x9a, 0x8f, 0xd0))
            };
            AnsiConsole.WriteLine();
            AnsiConsole.Write(rule);
            AnsiConsole.WriteLine();
        }

        public static void RenderStatus(GameState state)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(new Color(0x8a, 0x83, 0x75))
                .Title($"[bold #8fb26a]Status — {Markup.Escape(state.SaveName)} (Turn {state.Turn})[/]")
                .AddColumn(new TableColumn("[bold #8fb26a]Character[/]"))
                .AddColumn(new TableColumn("[bold #e0b050]Inventory[/]"))
                .AddColumn(new TableColumn("[bold #7fc3c8]Location[/]"));

            var charText = $"[bold]{Markup.Escape(state.PlayerName)}[/]\n[dim]Health:[/] [bold]{state.Health}/{state.MaxHealth}[/]";
            var items = state.Inventory.Count > 0
                ? string.Join(", ", state.Inventory.Select(i => Markup.Escape(i.Name)))
                : "[dim]Empty[/]";
            var locText = string.IsNullOrEmpty(state.Location) ? "[dim]Unknown[/]" : Markup.Escape(state.Location);

            table.AddRow(charText, items, locText);
            AnsiConsole.WriteLine();
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        public static void RenderInventory(IReadOnlyList<InventoryEntry> items)
        {
            var table = new Table()
                .Border(TableBorder.Rounded)
                .BorderColor(new Color(0xe0, 0xb0, 0x50))
                .Title("[bold #e0b050]Inventory[/]")
                .AddColumn(new TableColumn("[dim]Qty[/]"))
                .AddColumn(new TableColumn("[bold]Item[/]"))
                .AddColumn(new TableColumn("[dim]Identifier[/]"));

            if (items.Count == 0)
            {
                table.AddRow("-", "[dim]Your pack is empty.[/]", "-");
            }
            else
            {
                foreach (var item in items)
                {
                    table.AddRow(
                        item.Quantity.ToString(),
                        $"[bold #e0b050]{Markup.Escape(item.Name)}[/]",
                        $"[dim]{Markup.Escape(item.Id)}[/]");
                }
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(table);
            AnsiConsole.WriteLine();
        }

        public static void RenderOptions(IReadOnlyList<NarrationOption> options)
        {
            if (options.Count == 0)
            {
                return;
            }

            AnsiConsole.WriteLine();
            foreach (var opt in options)
            {
                AnsiConsole.MarkupLine($"  [bold #8fb26a]{opt.Number}.[/] [bold #d7d2c4]{Markup.Escape(opt.Text)}[/]");
            }
            AnsiConsole.WriteLine();
        }

        public static void RenderLine(StyledLine line)
        {
            AnsiConsole.MarkupLine(line.ToMarkup());
        }

        public static void RenderCommandResult(PlayerCommandResult result)
        {
            AnsiConsole.WriteLine();
            foreach (var line in result.Lines)
            {
                AnsiConsole.MarkupLine(line.ToMarkup());
            }
            AnsiConsole.WriteLine();
        }
    }
}
