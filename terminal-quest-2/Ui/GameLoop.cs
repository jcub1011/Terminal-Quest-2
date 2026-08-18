using Spectre.Console;
using TerminalQuest.Agents;
using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Settings;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The main CLI game loop: coordinates token streaming, rolls, options, and player input.
    /// </summary>
    internal static class GameLoop
    {
        private const string OpeningPrompt =
            "This is the first scene. The player character and where they begin are already on "
          + "record. Call get_state, then record_claims for what you are about to say, then describe "
          + "the place as they stand in it. Do not create the player and do not ask who they are.";

        private const string OpeningPromptNoPlace =
            "This is the first scene. The player character is already on record but has nowhere to "
          + "be. Call get_state, then invent where they begin: upsert_location, move_character them "
          + "into it, record_claims for what you are about to say, and describe it. Do not create the "
          + "player and do not ask who they are.";

        private const string ContinuePrompt =
            "This save is being resumed and your memory of it is gone. Call get_transcript first: it "
          + "returns the end of the last session word for word, and says whether the player is owed an "
          + "answer. Then get_state, then record_claims for what you are about to say. Pick the thread "
          + "up in the voice the transcript is written in. Describe where the player is now rather than "
          + "recapping what they already lived through - and if their last line went unanswered, answer "
          + "it rather than opening a fresh scene over the top of it.";

        public static async Task RunAsync(
            AppSettings settings,
            SaveStore store,
            ExternalEditor editor)
        {
            store.RequireSupportedSchema();
            var systemPrompt = NarratorPromptFile.Ensure(store);
            var directorPrompt = DirectorPromptFile.Ensure(store);

            var isNewCharacter = store.ReadCharacters().Characters.Count == 0;
            var startedFresh = false;
            var hasStartLocation = false;

            if (isNewCharacter)
            {
                var created = await CharacterCreationWizard.RunAsync(store, editor);
                if (created is null)
                {
                    try
                    {
                        SavePaths.Delete(store.Name);
                    }
                    catch
                    {
                    }
                    return;
                }

                if (created.Value.Error is not null)
                {
                    AnsiConsole.MarkupLine($"[bold red]Failed to create character: {Markup.Escape(created.Value.Error)}[/]");
                    CliPrompt.WaitKeyOrCancel("Press any key to return to main menu...");
                    return;
                }

                startedFresh = true;
                hasStartLocation = created.Value.HasStartLocation;
            }

            var state = new GameState { SaveName = store.Name };
            var watcher = new RollWatcher(store);

            try
            {
                state.Turn = store.ReadMetadata().Turn;
                if (!startedFresh)
                {
                    var player = SaveStore.Player(store.ReadCharacters());
                    hasStartLocation = SaveStore.WhereIs(store.ReadLocations(), player?.Id) is not null;
                }
                state.RefreshFrom(store);
                watcher.CatchUp();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Error loading save: {Markup.Escape(ex.Message)}[/]");
                CliPrompt.WaitKeyOrCancel("Press any key to return to main menu...");
                return;
            }

            AnsiConsole.Clear();
            SpectreRenderer.RenderBanner(state.SaveName, state.Turn);
            AnsiConsole.MarkupLine("[dim]Type your action or command. /help lists player commands. (ESC to exit to menu)[/]");
            AnsiConsole.WriteLine();

            // Replay recalled scene if continuing existing save
            if (!startedFresh)
            {
                try
                {
                    var transcriptEntries = store.Transcript.Read().Entries;
                    var recalled = TranscriptRecall.Tail(transcriptEntries, settings.TranscriptRecallCharacters);
                    if (recalled.Count > 0)
                    {
                        var replayLines = TranscriptReplay.Lines(recalled, store.Rolls.Read().Entries, store.ReadCharacters());
                        foreach (var line in replayLines)
                        {
                            SpectreRenderer.RenderLine(line);
                        }
                        AnsiConsole.WriteLine();
                    }
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine($"[dim red]Warning: Could not replay transcript: {Markup.Escape(ex.Message)}[/]");
                }
            }

            IAgentSession narrator;
            IAgentSession director;
            try
            {
                narrator = AgentSessionFactory.CreateNarrator(settings, store, systemPrompt);
                director = AgentSessionFactory.CreateDirector(settings, store, directorPrompt);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Failed to initialize narrator agent:[/] {Markup.Escape(ex.Message)}");
                if (ex is AgentException agentEx && !string.IsNullOrEmpty(agentEx.Detail))
                {
                    AnsiConsole.MarkupLine($"[dim red]{Markup.Escape(agentEx.Detail)}[/]");
                }
                AnsiConsole.WriteLine();
                CliPrompt.WaitKeyOrCancel("Press any key to return to main menu...");
                return;
            }

            await using (narrator)
            await using (director)
            {
                var lastDirectorTurn = 0;
                string? lastPlayerLocationId = null;

                QuestJournal.OnFailure = message =>
                    Findings.Record(store, state.Turn, Finding.RecordUnwritable, message);

                try
                {
                    var cliPrompt = new CliPrompt(editor);
                    var lastTurnLines = new List<StyledLine>();

                    // Start sessions
                    try
                    {
                        await narrator.StartAsync();
                    }
                    catch (Exception ex)
                    {
                        AnsiConsole.MarkupLine($"[bold red]Failed to connect to narrator:[/] {Markup.Escape(ex.Message)}");
                        if (ex is AgentException agentEx && !string.IsNullOrEmpty(agentEx.Detail))
                        {
                            AnsiConsole.MarkupLine($"[dim red]{Markup.Escape(agentEx.Detail)}[/]");
                        }
                        AnsiConsole.WriteLine();
                        CliPrompt.WaitKeyOrCancel("Press any key to return to main menu...");
                        return;
                    }

                    try
                    {
                        await director.StartAsync();
                    }
                    catch
                    {
                        // Director failure is non-fatal
                    }

                    // Opening turn if starting fresh or turn is 0
                    if (state.Turn == 0 || startedFresh)
                    {
                        var openingInstruction = startedFresh
                            ? (hasStartLocation ? OpeningPrompt : OpeningPromptNoPlace)
                            : ContinuePrompt;

                        lastTurnLines = await ExecuteNarratorTurnAsync(narrator, openingInstruction, store, watcher, state.Turn);
                    }

                    // Main gameplay REPL loop
                    while (true)
                    {
                        state.RefreshFrom(store);
                        var activeOptions = NarrationOptionDetector.Detect(lastTurnLines);
                        SpectreRenderer.RenderOptions(activeOptions);

                        var input = await cliPrompt.ReadLineAsync(activeOptions);
                        if (input is null)
                        {
                            AnsiConsole.MarkupLine("[dim]Returning to main menu...[/]");
                            TryTouch(store, state.Turn);
                            break;
                        }

                        if (string.IsNullOrWhiteSpace(input))
                        {
                            continue;
                        }

                        // Handle Player Slash Commands
                        if (PlayerCommands.IsCommand(input))
                        {
                            var cmdName = input.TrimStart('/').Split(' ', 2)[0].ToLowerInvariant();
                            if (cmdName is "quit" or "exit")
                            {
                                TryTouch(store, state.Turn);
                                break;
                            }

                            if (cmdName is "system-prompt")
                            {
                                var promptFile = NarratorPromptFile.Ensure(store);
                                var path = Path.Combine(store.Directory, "system-prompt.txt");
                                var changed = await editor.EditFileAsync(path);
                                if (changed)
                                {
                                    AnsiConsole.MarkupLine("[green]System prompt updated. Returning to menu to reload narrator...[/]");
                                    TryTouch(store, state.Turn);
                                    Thread.Sleep(1200);
                                    break;
                                }
                                continue;
                            }

                            if (cmdName is "status")
                            {
                                SpectreRenderer.RenderStatus(state);
                                continue;
                            }

                            var result = PlayerCommands.Execute(input, store);
                            SpectreRenderer.RenderCommandResult(result);
                            continue;
                        }

                        // Check if user selected an active numbered option (e.g. "1", "2")
                        var actionText = input;
                        if (int.TryParse(input.Trim(), out var optionNum) && activeOptions.Count > 0)
                        {
                            var match = activeOptions.FirstOrDefault(o => o.Number == optionNum);
                            if (match is not null)
                            {
                                actionText = match.Text;
                                AnsiConsole.MarkupLine($"[bold #8fb26a]❯ Selected Option {optionNum}:[/] [bold #d7d2c4]{Markup.Escape(actionText)}[/]");
                            }
                        }

                        state.Turn++;
                        try
                        {
                            store.Transcript.Append(new TranscriptEntry
                            {
                                Turn = state.Turn,
                                Voice = TranscriptVoice.Player,
                                Text = actionText,
                            });
                        }
                        catch
                        {
                            // Best-effort
                        }

                        if (!TryTouch(store, state.Turn))
                        {
                            break;
                        }

                        // Run Director if needed
                        var playerLocation = SaveStore.WhereIs(store.ReadLocations(), SaveStore.Player(store.ReadCharacters())?.Id);
                        var isFirstTurn = state.Turn == 1;
                        var hasChangedLocation = playerLocation?.Id != lastPlayerLocationId;
                        var isPeriodicTurn = (state.Turn - lastDirectorTurn) >= 5;

                        if (isFirstTurn || hasChangedLocation || isPeriodicTurn)
                        {
                            try
                            {
                                var directorPromptText = isFirstTurn
                                    ? $"The game has just begun. The player is at {playerLocation?.Name ?? "unknown location"}."
                                    : $"Turn {state.Turn}. The player is at {playerLocation?.Name ?? "unknown location"}. Review recent story developments and adjust directives if needed.";

                                await director.SendAsync(directorPromptText, CancellationToken.None);
                                lastDirectorTurn = state.Turn;
                                lastPlayerLocationId = playerLocation?.Id;
                            }
                            catch
                            {
                                // Director errors are non-fatal
                            }
                        }

                        // Run Narrator Turn
                        lastTurnLines = await ExecuteNarratorTurnAsync(narrator, actionText, store, watcher, state.Turn);
                    }
                }
                finally
                {
                    QuestJournal.OnFailure = null;
                }
            }

            TryTouch(store, state.Turn);
        }

        private static async Task<List<StyledLine>> ExecuteNarratorTurnAsync(
            IAgentSession narrator,
            string input,
            SaveStore store,
            RollWatcher watcher,
            int turn)
        {
            var lines = new List<StyledLine>();
            var currentLine = new StyledLine();
            var parser = new MarkupParser();
            var recorder = new NarrationRecorder();
            var renderLock = new object();

            using var cts = new CancellationTokenSource();
            var pollTask = Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var rolls = watcher.Take();
                        if (rolls.Count > 0)
                        {
                            var characters = store.ReadCharacters();
                            lock (renderLock)
                            {
                                foreach (var r in rolls)
                                {
                                    var roller = SaveStore.FindCharacterById(characters, r.CharacterId)?.Name ?? r.CharacterId;
                                    var rollLine = RollWatcher.Line(r, roller);
                                    SpectreRenderer.RenderRoll(rollLine.ToPlainText());
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Polling is best-effort
                    }

                    await Task.Delay(300, cts.Token);
                }
            }, cts.Token);

            Action<string> deltaHandler = delta =>
            {
                lock (renderLock)
                {
                    recorder.Append(delta);
                    for (var i = 0; i < delta.Length; i++)
                    {
                        var c = delta[i];
                        if (c == '\n')
                        {
                            parser.Append(c.ToString(), currentLine);
                            lines.Add(currentLine);
                            SpectreRenderer.RenderLine(currentLine);
                            currentLine = new StyledLine();
                            parser.Reset();
                        }
                        else
                        {
                            parser.Append(c.ToString(), currentLine);
                        }
                    }
                }
            };

            narrator.OnTextDelta += deltaHandler;

            AnsiConsole.WriteLine();
            try
            {
                var result = await narrator.SendAsync(input, CancellationToken.None);
                lock (renderLock)
                {
                    if (currentLine.Length > 0)
                    {
                        lines.Add(currentLine);
                        SpectreRenderer.RenderLine(currentLine);
                        currentLine = new StyledLine();
                        parser.Reset();
                    }
                }

                var spoken = recorder.TakeAndClear();
                var prose = string.IsNullOrWhiteSpace(spoken) ? result.Text : spoken;
                if (!string.IsNullOrWhiteSpace(prose) && !result.IsError)
                {
                    try
                    {
                        store.Transcript.Append(new TranscriptEntry
                        {
                            Turn = turn,
                            Voice = TranscriptVoice.Narrator,
                            Text = prose,
                        });
                    }
                    catch
                    {
                        // Best-effort
                    }
                }

                AnsiConsole.WriteLine();
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[bold red]Turn failed:[/] {Markup.Escape(ex.Message)}");
                if (ex is AgentException agentEx && !string.IsNullOrEmpty(agentEx.Detail))
                {
                    AnsiConsole.MarkupLine($"[dim red]{Markup.Escape(agentEx.Detail)}[/]");
                }
            }
            finally
            {
                narrator.OnTextDelta -= deltaHandler;
                cts.Cancel();
                try { await pollTask; } catch { }
            }

            return lines;
        }

        private static bool TryTouch(SaveStore store, int turn)
        {
            try
            {
                store.Touch(turn);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
