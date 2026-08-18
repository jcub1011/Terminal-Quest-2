using Spectre.Console;
using TerminalQuest.Agents.LmStudio;
using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Interactive CLI controller for configuring application settings, LLM providers, and external editors.
    /// </summary>
    internal static class SettingsController
    {
        private readonly record struct ModelOption(string Id, string DisplayName, string? Detail = null);

        public static async Task RunAsync(AppSettings settings, ExternalEditor editor)
        {
            while (true)
            {
                var providerText = settings.Provider switch
                {
                    AgentProvider.ClaudeCode => $"Claude ({settings.ClaudeModel})",
                    AgentProvider.LmStudio => $"{settings.OpenAiPreset} ({settings.LmStudioModel} @ {settings.LmStudioBaseUrl})",
                    _ => settings.Provider.ToString()
                };

                var options = new[]
                {
                    $"LLM Engine / Provider [dim]({providerText})[/]",
                    $"Context Memory Limit [dim]({settings.TranscriptRecallCharacters} characters)[/]",
                    $"External Editor Command [dim]({settings.EditorCommand})[/]",
                    "Test External Editor",
                    "Open Configuration Folder",
                    "Restore Defaults",
                    "Save & Return to Main Menu"
                };

                void RenderSettingsHeader()
                {
                    AnsiConsole.Write(new Rule("[bold cyan]Settings[/]")
                    {
                        Border = BoxBorder.Rounded,
                        Style = new Style(new Color(0x8f, 0xb2, 0x6a))
                    });
                    AnsiConsole.WriteLine();
                }

                var choice = CliMenu.Prompt(
                    "[bold #8fb26a]Select an option to configure:[/]",
                    options,
                    o => o,
                    defaultIndex: 0,
                    renderHeader: RenderSettingsHeader,
                    allowCancel: true,
                    cancelHint: "return to main menu");

                if (choice is null || choice.StartsWith("Save"))
                {
                    SettingsStore.Write(settings);
                    break;
                }
                else if (choice.StartsWith("LLM"))
                {
                    await ConfigureProviderAsync(settings);
                }
                else if (choice.StartsWith("Context"))
                {
                    ConfigureMemory(settings);
                }
                else if (choice.StartsWith("External"))
                {
                    ConfigureEditor(settings);
                }
                else if (choice.StartsWith("Test"))
                {
                    await TestEditorAsync(settings, editor);
                }
                else if (choice.StartsWith("Open"))
                {
                    if (FileExplorer.TryOpen(PathProvider.Root, out var err))
                    {
                        AnsiConsole.MarkupLine($"[green]Opened configuration folder in file explorer: {Markup.Escape(PathProvider.Root)}[/]");
                    }
                    else
                    {
                        AnsiConsole.MarkupLine($"[red]{Markup.Escape(err ?? "Failed to open folder.")}[/]");
                    }
                    Thread.Sleep(1000);
                }
                else if (choice.StartsWith("Restore"))
                {
                    if (CliPrompt.Confirm("Reset all settings to defaults?", defaultValue: false) == true)
                    {
                        var defaults = new AppSettings();
                        settings.Provider = defaults.Provider;
                        settings.ClaudeModel = defaults.ClaudeModel;
                        settings.OpenAiPreset = defaults.OpenAiPreset;
                        settings.LmStudioBaseUrl = defaults.LmStudioBaseUrl;
                        settings.LmStudioApiKey = defaults.LmStudioApiKey;
                        settings.LmStudioModel = defaults.LmStudioModel;
                        settings.TranscriptRecallCharacters = defaults.TranscriptRecallCharacters;
                        settings.EditorCommand = defaults.EditorCommand;
                        SettingsStore.Write(settings);
                        AnsiConsole.MarkupLine("[green]Settings reset to defaults.[/]");
                        Thread.Sleep(800);
                    }
                }
            }
        }

        private static async Task ConfigureProviderAsync(AppSettings settings)
        {
            void RenderProviderHeader()
            {
                AnsiConsole.Write(new Rule("[bold cyan]Configure Narrator Provider[/]")
                {
                    Border = BoxBorder.Rounded,
                    Style = new Style(new Color(0x8f, 0xb2, 0x6a))
                });
                AnsiConsole.WriteLine();
            }

            var providers = new[]
            {
                "Google Gemini (via OpenAI-compatible endpoint)",
                "Anthropic Claude (via Claude CLI)",
                "OpenAI (Official API)",
                "Local / Custom (LM Studio, Ollama, vLLM)",
                "Back"
            };

            var providerChoice = CliMenu.Prompt(
                "[bold #8fb26a]Choose Narrator Provider:[/]",
                providers,
                p => p,
                defaultIndex: 0,
                renderHeader: RenderProviderHeader,
                allowCancel: true,
                cancelHint: "return to settings");

            if (providerChoice is null || providerChoice.StartsWith("Back"))
            {
                return;
            }

            if (providerChoice.StartsWith("Google"))
            {
                AnsiConsole.Clear();
                RenderProviderHeader();

                var currentKey = (settings.LmStudioApiKey is "lm-studio" or "") ? string.Empty : settings.LmStudioApiKey;

                if (!string.IsNullOrEmpty(currentKey))
                {
                    var masked = currentKey.Length > 8 ? $"{currentKey[..4]}...{currentKey[^4..]}" : "***";
                    AnsiConsole.MarkupLine($"[dim]Current saved key: {Markup.Escape(masked)} (press Enter to keep, or paste new key)[/]");
                }

                var apiKey = CliPrompt.AskString(
                    "[bold #e0b050]Google Gemini API Key:[/] ",
                    defaultValue: currentKey,
                    allowEmpty: true);

                if (apiKey is null)
                {
                    return;
                }

                var resolvedKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey.Trim() : currentKey;

                if (string.IsNullOrWhiteSpace(resolvedKey))
                {
                    AnsiConsole.MarkupLine("[bold red]API key cannot be empty for Google Gemini.[/]");
                    AnsiConsole.MarkupLine("[dim]Selection cancelled.[/]");
                    Thread.Sleep(1000);
                    return;
                }

                var model = await SelectModelAsync(
                    "Google Gemini",
                    string.IsNullOrEmpty(settings.LmStudioModel) ? OpenAiPresets.Google.DefaultModel : settings.LmStudioModel,
                    OpenAiPresets.Google.BaseUrl,
                    resolvedKey,
                    []);

                if (model is not null)
                {
                    settings.Provider = AgentProvider.LmStudio;
                    settings.OpenAiPreset = OpenAiPresets.Google.Name;
                    settings.LmStudioBaseUrl = OpenAiPresets.Google.BaseUrl;
                    settings.LmStudioApiKey = resolvedKey;
                    settings.LmStudioModel = model;
                    AnsiConsole.MarkupLine("[green]Provider updated to Google Gemini.[/]");
                    Thread.Sleep(600);
                }
            }
            else if (providerChoice.StartsWith("Anthropic"))
            {
                var claudePresets = new List<ModelOption>
                {
                    new(string.Empty, "Default", "whatever the CLI is configured for"),
                    new("claude-haiku-4-5", "claude-haiku-4-5", "fastest and cheapest"),
                    new("claude-sonnet-5", "claude-sonnet-5", "balanced"),
                    new("claude-opus-5", "claude-opus-5", "most capable"),
                    new("claude-3-5-sonnet-20241022", "claude-3-5-sonnet-20241022", "Claude 3.5 Sonnet"),
                    new("claude-3-5-haiku-20241022", "claude-3-5-haiku-20241022", "Claude 3.5 Haiku"),
                };

                var model = await SelectModelAsync(
                    "Claude",
                    settings.ClaudeModel,
                    baseUrl: null,
                    apiKey: null,
                    claudePresets);

                if (model is not null)
                {
                    settings.Provider = AgentProvider.ClaudeCode;
                    settings.ClaudeModel = model;
                    AnsiConsole.MarkupLine("[green]Provider updated to Anthropic Claude.[/]");
                    Thread.Sleep(600);
                }
            }
            else if (providerChoice.StartsWith("OpenAI"))
            {
                AnsiConsole.Clear();
                RenderProviderHeader();

                var currentKey = (settings.LmStudioApiKey is "lm-studio" or "") ? string.Empty : settings.LmStudioApiKey;

                if (!string.IsNullOrEmpty(currentKey))
                {
                    var masked = currentKey.Length > 8 ? $"{currentKey[..4]}...{currentKey[^4..]}" : "***";
                    AnsiConsole.MarkupLine($"[dim]Current saved key: {Markup.Escape(masked)} (press Enter to keep, or paste new key)[/]");
                }

                var apiKey = CliPrompt.AskString(
                    "[bold #e0b050]OpenAI API Key:[/] ",
                    defaultValue: currentKey,
                    allowEmpty: true);

                if (apiKey is null)
                {
                    return;
                }

                var resolvedKey = !string.IsNullOrWhiteSpace(apiKey) ? apiKey.Trim() : currentKey;

                if (string.IsNullOrWhiteSpace(resolvedKey))
                {
                    AnsiConsole.MarkupLine("[bold red]API key cannot be empty for OpenAI.[/]");
                    AnsiConsole.MarkupLine("[dim]Selection cancelled.[/]");
                    Thread.Sleep(1000);
                    return;
                }

                var model = await SelectModelAsync(
                    "OpenAI",
                    string.IsNullOrEmpty(settings.LmStudioModel) ? OpenAiPresets.OpenAI.DefaultModel : settings.LmStudioModel,
                    OpenAiPresets.OpenAI.BaseUrl,
                    resolvedKey,
                    []);

                if (model is not null)
                {
                    settings.Provider = AgentProvider.LmStudio;
                    settings.OpenAiPreset = OpenAiPresets.OpenAI.Name;
                    settings.LmStudioBaseUrl = OpenAiPresets.OpenAI.BaseUrl;
                    settings.LmStudioApiKey = resolvedKey;
                    settings.LmStudioModel = model;
                    AnsiConsole.MarkupLine("[green]Provider updated to OpenAI.[/]");
                    Thread.Sleep(600);
                }
            }
            else if (providerChoice.StartsWith("Local"))
            {
                AnsiConsole.Clear();
                RenderProviderHeader();

                var baseUrl = CliPrompt.AskString(
                    "[bold #e0b050]Base URL:[/] ",
                    defaultValue: settings.LmStudioBaseUrl ?? "http://localhost:1234/v1",
                    allowEmpty: false);

                if (baseUrl is null)
                {
                    return;
                }
                var resolvedUrl = baseUrl.Trim();

                var apiKey = CliPrompt.AskString(
                    "[bold #e0b050]API Key (optional):[/] ",
                    defaultValue: settings.LmStudioApiKey ?? "lm-studio",
                    allowEmpty: true);

                if (apiKey is null)
                {
                    return;
                }
                var resolvedKey = string.IsNullOrWhiteSpace(apiKey) ? "lm-studio" : apiKey.Trim();

                var model = await SelectModelAsync(
                    "Local Server",
                    settings.LmStudioModel,
                    resolvedUrl,
                    resolvedKey,
                    []);

                if (model is not null)
                {
                    settings.Provider = AgentProvider.LmStudio;
                    settings.OpenAiPreset = OpenAiPresets.Custom.Name;
                    settings.LmStudioBaseUrl = resolvedUrl;
                    settings.LmStudioApiKey = resolvedKey;
                    settings.LmStudioModel = model;
                    AnsiConsole.MarkupLine("[green]Provider updated to Local Server.[/]");
                    Thread.Sleep(600);
                }
            }
        }

        private static async Task<string?> SelectModelAsync(
            string providerTitle,
            string currentModel,
            string? baseUrl,
            string? apiKey,
            IReadOnlyList<ModelOption> fallbackPresets)
        {
            var options = new List<ModelOption>();
            var discoveredCount = 0;

            if (!string.IsNullOrEmpty(baseUrl))
            {
                List<string> discovered = [];
                string? errorMessage = null;

                await AnsiConsole.Status()
                    .Spinner(Spinner.Known.Dots)
                    .StartAsync($"Connecting to {providerTitle} and querying models...", async ctx =>
                    {
                        try
                        {
                            var list = await LmStudioModels.ListAsync(baseUrl, apiKey, TimeSpan.FromSeconds(6));
                            discovered.AddRange(list);
                        }
                        catch (Exception ex)
                        {
                            errorMessage = ex.Message;
                        }
                    });

                if (errorMessage is not null)
                {
                    AnsiConsole.WriteLine();
                    AnsiConsole.MarkupLine($"[bold red]Connection failed:[/] {Markup.Escape(errorMessage)}");
                    CliPrompt.WaitKeyOrCancel("[dim]Selection cancelled. Press Enter to return to settings...[/]");
                    return null;
                }

                discoveredCount = discovered.Count;
                AnsiConsole.MarkupLine($"[bold cyan]Discovered {discoveredCount} models...[/]");

                if (discoveredCount == 0)
                {
                    AnsiConsole.MarkupLine($"[bold red]No models discovered from {providerTitle}.[/]");
                    CliPrompt.WaitKeyOrCancel("[dim]Selection cancelled. Press Enter to return to settings...[/]");
                    return null;
                }

                // Prioritize text/chat models over embeddings/audio
                var sorted = discovered
                    .OrderBy(m => m.Contains("embed", StringComparison.OrdinalIgnoreCase) ||
                                  m.Contains("tts", StringComparison.OrdinalIgnoreCase) ||
                                  m.Contains("dall-e", StringComparison.OrdinalIgnoreCase) ||
                                  m.Contains("whisper", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                    .ThenBy(m => m)
                    .ToList();

                foreach (var m in sorted)
                {
                    options.Add(new ModelOption(m, m));
                }
            }
            else
            {
                options.AddRange(fallbackPresets);
            }

            // If current saved model is not in the options list and is not empty, insert it near top
            if (!string.IsNullOrEmpty(currentModel) && !options.Any(o => string.Equals(o.Id, currentModel, StringComparison.OrdinalIgnoreCase)))
            {
                options.Insert(0, new ModelOption(currentModel, $"{currentModel} [dim](saved)[/]"));
            }

            // Always add a custom manual entry option
            options.Add(new ModelOption("__custom__", "[dim]Custom / Type model ID manually...[/]"));

            // Find default index based on current saved model
            var defaultIndex = options.FindIndex(o => string.Equals(o.Id, currentModel ?? string.Empty, StringComparison.OrdinalIgnoreCase));
            if (defaultIndex < 0)
            {
                defaultIndex = 0;
            }

            void RenderModelHeader()
            {
                AnsiConsole.Write(new Rule($"[bold cyan]{providerTitle} Model[/]")
                {
                    Border = BoxBorder.Rounded,
                    Style = new Style(new Color(0x8f, 0xb2, 0x6a))
                });
                if (discoveredCount > 0)
                {
                    AnsiConsole.MarkupLine($"[dim]Discovered {discoveredCount} models from endpoint.[/]");
                }
                AnsiConsole.WriteLine();
            }

            var chosen = CliMenu.Prompt(
                $"[bold #8fb26a]Select {providerTitle} Model:[/]",
                options,
                o => string.IsNullOrEmpty(o.Detail) ? o.DisplayName : $"{o.DisplayName} — [dim]{o.Detail}[/]",
                o => o.Id,
                defaultIndex,
                renderHeader: RenderModelHeader,
                allowCancel: true);

            if (chosen.Id is null)
            {
                return null;
            }

            if (chosen.Id == "__custom__")
            {
                AnsiConsole.Clear();
                RenderModelHeader();
                var manualModel = CliPrompt.AskString(
                    "[bold #e0b050]Model ID:[/] ",
                    defaultValue: currentModel ?? string.Empty,
                    allowEmpty: true);

                if (manualModel is null)
                {
                    return null;
                }

                return string.IsNullOrWhiteSpace(manualModel) ? string.Empty : manualModel.Trim();
            }

            AnsiConsole.MarkupLine($"[green]Selected model: {Markup.Escape(chosen.Id.Length == 0 ? "(default)" : chosen.Id)}[/]");
            Thread.Sleep(500);
            return chosen.Id;
        }

        private static void ConfigureMemory(AppSettings settings)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]Context Memory Limit[/]")
            {
                Border = BoxBorder.Rounded,
                Style = new Style(new Color(0x8f, 0xb2, 0x6a))
            });
            AnsiConsole.WriteLine();

            var chars = CliPrompt.AskInt(
                "[bold #e0b050]Maximum characters of prior transcript to send to narrator:[/] ",
                defaultValue: settings.TranscriptRecallCharacters,
                validator: c => c is >= 1000 and <= 500000
                    ? ValidationResult.Success()
                    : ValidationResult.Error("Value must be between 1,000 and 500,000."));

            if (chars.HasValue)
            {
                settings.TranscriptRecallCharacters = chars.Value;
            }
        }

        private static void ConfigureEditor(AppSettings settings)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]External Editor Command[/]")
            {
                Border = BoxBorder.Rounded,
                Style = new Style(new Color(0x8f, 0xb2, 0x6a))
            });
            AnsiConsole.WriteLine();

            var cmd = CliPrompt.AskString(
                "[bold #e0b050]External Editor Command (e.g. 'code -w', 'notepad', 'micro'):[/] ",
                defaultValue: settings.EditorCommand,
                allowEmpty: false);

            if (cmd is not null)
            {
                settings.EditorCommand = cmd.Trim();
            }
        }

        private static async Task TestEditorAsync(AppSettings settings, ExternalEditor editor)
        {
            AnsiConsole.Clear();
            AnsiConsole.Write(new Rule("[bold cyan]Test External Editor[/]")
            {
                Border = BoxBorder.Rounded,
                Style = new Style(new Color(0x8f, 0xb2, 0x6a))
            });
            AnsiConsole.WriteLine();

            AnsiConsole.MarkupLine("[dim]Launching external editor test. Type some text, save and exit...[/]");
            var result = await editor.EditStringAsync("Hello from Terminal Quest! Edit this text to test your editor.", "Test Editor");
            if (result is not null)
            {
                AnsiConsole.MarkupLine($"[green]Editor returned successfully:[/] {Markup.Escape(result.Trim())}");
            }
            else
            {
                AnsiConsole.MarkupLine("[yellow]Editor returned nothing or was cancelled.[/]");
            }
            CliPrompt.WaitKeyOrCancel("Press Enter or ESC to continue...");
        }
    }
}
