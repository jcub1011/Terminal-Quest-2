using Terminal.Gui.Input;
using Terminal.Gui.Views;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;
using TerminalQuest.Ui;

using Xunit;

namespace TerminalQuest.Tests.Ui
{
    /// <summary>
    /// The player's own commands, answered by the game rather than forwarded to the narrator.
    /// </summary>
    public sealed class PlayerCommandsTests
    {
        private static TempSave Seeded()
        {
            var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", "A quiet sort.", ClassTemplates.All[0], "The Ford");
            return save;
        }

        private static string TextOf(PlayerCommandResult result) =>
            string.Join(
                "\n",
                result.Lines.Select(line => string.Concat(line.Spans.Select(span => span.Text))));

        // ---- The table and the switch are a pair ---------------------------------------------

        [Fact]
        public void Every_command_in_the_table_actually_runs()
        {
            // A name in the table but not the switch is a suggestion that errors when taken.
            using var save = Seeded();

            foreach (var command in PlayerCommands.All)
            {
                var result = PlayerCommands.Execute($"/{command.Name}", save.Store);

                Assert.DoesNotContain(
                    $"There is no command '/{command.Name}'",
                    TextOf(result),
                    StringComparison.Ordinal);
            }
        }

        [Theory]
        [InlineData("dormant")]
        [InlineData("live")]
        [InlineData("spent")]
        public void No_player_command_ever_prints_a_secret(string stage)
        {
            // Secrets are the narrator's working notes, and these commands answer the player. Spent is
            // included deliberately: the player heard the prose a character chose to say, not the plot
            // note behind it, and printing the note would be the prose equivalent of showing them a
            // hidden roll's total.
            const string sentinel = "ZZQX-secret-detail-must-not-escape-ZZQX";

            using var save = Seeded();
            var file = save.Store.ReadCharacters();

            file.Characters[0].Secrets.Add(new Secret
            {
                Name = "the sealed cellar",
                Stage = Enum.Parse<SecretStage>(stage, ignoreCase: true),
                Text = sentinel,
                Turn = 1,
            });

            save.Store.WriteCharacters(file);

            foreach (var command in PlayerCommands.All)
            {
                foreach (var typed in new[] { $"/{command.Name}", $"/{command.Name} Rowan" })
                {
                    Assert.DoesNotContain(
                        sentinel,
                        TextOf(PlayerCommands.Execute(typed, save.Store)),
                        StringComparison.Ordinal);
                }
            }
        }

        [Fact]
        public void An_unknown_command_is_refused_rather_than_spoken_to_the_world()
        {
            // A typo must never quietly become a story prompt.
            using var save = Seeded();

            var result = PlayerCommands.Execute("/nosuchthing", save.Store);

            Assert.Contains("There is no command", TextOf(result), StringComparison.Ordinal);
            Assert.False(result.Quit);
        }

        [Fact]
        public void Help_lists_every_command_that_is_not_an_alias()
        {
            using var save = Seeded();

            var text = TextOf(PlayerCommands.Execute("/help", save.Store));

            foreach (var command in PlayerCommands.All.Where(c => !c.IsAlias))
            {
                Assert.Contains($"/{command.Name}", text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Command_names_are_unique()
        {
            var names = PlayerCommands.All.Select(command => command.Name).ToList();

            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void No_command_name_contains_a_space()
        {
            // Parsing is positional and space-delimited, so a name with a space could never be run.
            Assert.All(PlayerCommands.All, command => Assert.DoesNotContain(' ', command.Name));
        }

        [Fact]
        public void Every_alias_stands_for_a_command_that_does_the_same_thing()
        {
            using var save = Seeded();

            foreach (var alias in PlayerCommands.All.Where(command => command.IsAlias))
            {
                var twin = PlayerCommands.All.First(command =>
                    !command.IsAlias && command.Summary == alias.Summary);

                Assert.Equal(
                    TextOf(PlayerCommands.Execute($"/{twin.Name}", save.Store)),
                    TextOf(PlayerCommands.Execute($"/{alias.Name}", save.Store)));
            }
        }

        // ---- Recognising a command -------------------------------------------------------------

        [Theory]
        [InlineData("/help")]
        [InlineData("/")]
        [InlineData("/anything at all")]
        public void Input_starting_with_a_slash_is_addressed_to_the_game(string input)
        {
            Assert.True(PlayerCommands.IsCommand(input));
        }

        [Theory]
        [InlineData("look around")]
        [InlineData("")]
        [InlineData(" /help")]
        public void Anything_else_is_spoken_to_the_world(string input)
        {
            Assert.False(PlayerCommands.IsCommand(input));
        }

        // ---- Suggestions --------------------------------------------------------------------------

        [Fact]
        public void A_bare_slash_offers_everything()
        {
            Assert.Equal(PlayerCommands.All.Count, PlayerCommands.Matching("/").Count);
        }

        [Fact]
        public void A_prefix_narrows_the_offer()
        {
            var matches = PlayerCommands.Matching("/inv");

            Assert.Equal(["inventory", "inv"], matches.Select(command => command.Name).ToList());
        }

        [Fact]
        public void Suggestions_are_case_insensitive_because_execution_is()
        {
            // /INV runs, so /IN has to be offered /inventory.
            Assert.NotEmpty(PlayerCommands.Matching("/IN"));
        }

        [Fact]
        public void Nothing_is_offered_once_the_player_has_moved_on_to_the_argument()
        {
            // A list of commands is no longer an answer to anything.
            Assert.Empty(PlayerCommands.Matching("/delete "));
            Assert.Empty(PlayerCommands.Matching("/delete Riverbend"));
        }

        [Fact]
        public void Nothing_is_offered_for_input_that_is_not_a_command()
        {
            Assert.Empty(PlayerCommands.Matching("look around"));
        }

        [Fact]
        public void A_prefix_nothing_starts_with_offers_nothing()
        {
            Assert.Empty(PlayerCommands.Matching("/zzz"));
        }

        // ---- Describing ------------------------------------------------------------------------------

        [Fact]
        public void A_named_command_is_described_even_once_the_argument_has_begun()
        {
            // So /delete is still saying it wants a name while the player is typing it.
            var described = PlayerCommands.Describing("/delete Riverbend");

            Assert.NotNull(described);
            Assert.Equal("delete", described.Value.Name);
        }

        [Fact]
        public void A_partial_name_describes_nothing()
        {
            Assert.Null(PlayerCommands.Describing("/del"));
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/   ")]
        [InlineData("look around")]
        public void Input_naming_no_command_describes_nothing(string input)
        {
            Assert.Null(PlayerCommands.Describing(input));
        }

        [Fact]
        public void Describing_splits_exactly_as_execute_does()
        {
            // Otherwise the hint could describe a different command from the one that would run.
            using var save = Seeded();

            foreach (var input in new[] { "/help", "/HELP", "/help ", "/help extra", "  /help" })
            {
                var described = PlayerCommands.Describing(input);
                var ran = !TextOf(PlayerCommands.Execute(input, save.Store))
                    .Contains("There is no command", StringComparison.Ordinal);

                Assert.Equal(ran && PlayerCommands.IsCommand(input), described is not null);
            }
        }

        [Fact]
        public void Usage_shows_the_argument_when_there_is_one()
        {
            Assert.Equal("/delete <name>", PlayerCommands.All.First(c => c.Name == "delete").Usage);
            Assert.Equal("/help", PlayerCommands.All.First(c => c.Name == "help").Usage);
        }

        // ---- Running -----------------------------------------------------------------------------------

        [Theory]
        [InlineData("/quit")]
        [InlineData("/exit")]
        [InlineData("/QUIT")]
        public void Leaving_asks_the_session_to_end(string input)
        {
            using var save = Seeded();

            Assert.True(PlayerCommands.Execute(input, save.Store).Quit);
        }

        [Fact]
        public void No_other_command_ends_the_session()
        {
            using var save = Seeded();

            foreach (var command in PlayerCommands.All.Where(c => c.Name is not ("quit" or "exit")))
            {
                Assert.False(PlayerCommands.Execute($"/{command.Name}", save.Store).Quit);
            }
        }

        [Fact]
        public void The_inventory_read_is_the_record_rather_than_a_recollection()
        {
            using var save = Seeded();

            var text = TextOf(PlayerCommands.Execute("/inventory", save.Store));

            var player = SaveStore.Player(save.Store.ReadCharacters())!;
            var items = save.Store.ReadItems();
            foreach (var stack in save.Store.ReadInventory().Find(player.Id)!.Items)
            {
                var item = SaveStore.FindItemById(items, stack.ItemId)!;
                Assert.Contains(item.Name, text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void Character_lists_who_has_been_met()
        {
            using var save = Seeded();

            Assert.Contains("Rowan", TextOf(PlayerCommands.Execute("/character", save.Store)), StringComparison.Ordinal);
        }

        [Fact]
        public void Character_with_argument_shows_details()
        {
            using var save = Seeded();

            var text = TextOf(PlayerCommands.Execute("/character Rowan", save.Store));
            Assert.Contains("Rowan", text, StringComparison.Ordinal);
            Assert.Contains("A quiet sort.", text, StringComparison.Ordinal);
        }

        [Fact]
        public void Location_lists_where_the_player_has_been()
        {
            using var save = Seeded();

            Assert.Contains("The Ford", TextOf(PlayerCommands.Execute("/location", save.Store)), StringComparison.Ordinal);
        }

        [Fact]
        public void Location_with_argument_shows_details()
        {
            using var save = Seeded();

            var text = TextOf(PlayerCommands.Execute("/location The Ford", save.Store));
            Assert.Contains("The Ford", text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_command_never_shows_an_entity_id()
        {
            using var save = Seeded();

            foreach (var command in PlayerCommands.All)
            {
                var text = TextOf(PlayerCommands.Execute($"/{command.Name}", save.Store));

                Assert.DoesNotContain(EntityIds.Character, text, StringComparison.Ordinal);
                Assert.DoesNotContain(EntityIds.Location, text, StringComparison.Ordinal);
                Assert.DoesNotContain(EntityIds.Item, text, StringComparison.Ordinal);
            }
        }

        [Fact]
        public void A_broken_save_is_reported_rather_than_thrown_at_the_player()
        {
            using var save = Seeded();
            save.WriteRaw("characters.json", "{ not json");

            var result = PlayerCommands.Execute("/character", save.Store);

            Assert.NotEmpty(result.Lines);
            Assert.Contains("characters.json", TextOf(result), StringComparison.Ordinal);
        }

        [Fact]
        public void Deleting_without_a_name_asks_for_one_rather_than_guessing()
        {
            using var save = Seeded();

            var text = TextOf(PlayerCommands.Execute("/delete", save.Store));

            Assert.NotEmpty(text);
        }

        // ---- The narrator's brief -----------------------------------------------------------------------

        [Theory]
        [InlineData("/system-prompt")]
        [InlineData("/SYSTEM-PROMPT")]
        [InlineData("/story-prompt")]
        public void Rewriting_the_brief_asks_the_host_to_open_an_editor(string input)
        {
            using var save = Seeded();

            var result = PlayerCommands.Execute(input, save.Store);

            Assert.True(result.EditSystemPrompt);

            // Not Quit: the host ends the session itself, and only once the file has really changed.
            // Setting both would leave for the menu whether the player wrote anything or not.
            Assert.False(result.Quit);
        }

        [Fact]
        public void No_other_command_asks_to_edit_the_brief()
        {
            using var save = Seeded();

            foreach (var command in PlayerCommands.All.Where(c => c.Name is not ("system-prompt" or "story-prompt")))
            {
                Assert.False(PlayerCommands.Execute($"/{command.Name}", save.Store).EditSystemPrompt);
            }
        }

        [Theory]
        [InlineData("/update-prompts")]
        [InlineData("/sync-prompts")]
        [InlineData("/reset-prompts")]
        public void Update_prompts_updates_story_files_and_reports_success(string input)
        {
            using var save = Seeded();
            save.Store.WriteNarratorStory("Old custom narrator text");
            save.Store.WriteDirectorStory("Old custom director text");

            var result = PlayerCommands.Execute(input, save.Store);
            var text = TextOf(result);

            Assert.Contains("Updated narrator and director story prompts", text, StringComparison.Ordinal);
            Assert.Equal(NarratorPromptFile.StoryDefault.ReplaceLineEndings(), save.Store.ReadNarratorStory());
            Assert.Equal(DirectorPromptFile.StoryDefault.ReplaceLineEndings(), save.Store.ReadDirectorStory());
        }

        [Fact]
        public void The_warning_that_the_session_ends_comes_before_the_editor_does()
        {
            // The lines are printed by the host before it acts on the flag, so what this asserts is
            // that the warning exists at all: an editor opening unannounced, on a command that takes
            // the session away, would be the one surprise this feature must not have.
            using var save = Seeded();

            var text = TextOf(PlayerCommands.Execute("/system-prompt", save.Store));

            Assert.Contains("ends this session", text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_brief_itself_is_not_printed_into_the_transcript()
        {
            // The file is where it can be read. Emptying thousands of words of prompt into the scene
            // the player is in the middle of would answer a question nobody asked.
            const string Written = "ZZQX-the-whole-prompt-must-not-be-echoed-ZZQX";

            using var save = Seeded();
            save.Store.WriteNarratorStory(Written);

            var text = TextOf(PlayerCommands.Execute("/system-prompt", save.Store));

            Assert.DoesNotContain(Written, text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_half_typed_name_still_finds_it()
        {
            Assert.Contains(
                "system-prompt",
                PlayerCommands.Matching("/sys").Select(command => command.Name));
            Assert.Contains(
                "update-prompts",
                PlayerCommands.Matching("/upd").Select(command => command.Name));
        }

        // ---- Argument suggestions -------------------------------------------------------------------

        [Fact]
        public void Character_command_suggests_all_characters_when_argument_is_empty()
        {
            using var save = Seeded();

            var (suggestions, isChoosing) = PlayerCommands.GetSuggestions("/character ", save.Store);

            Assert.True(isChoosing);
            Assert.Contains(suggestions, s => s.DisplayText == "Rowan" && s.InsertText == "/character Rowan");
        }

        [Fact]
        public void Character_command_narrows_suggestions_by_prefix()
        {
            using var save = Seeded();

            var (suggestions, isChoosing) = PlayerCommands.GetSuggestions("/character Ro", save.Store);

            Assert.True(isChoosing);
            Assert.Single(suggestions);
            Assert.Equal("Rowan", suggestions[0].DisplayText);
            Assert.Equal("/character Rowan", suggestions[0].InsertText);
        }

        [Fact]
        public void Location_command_suggests_all_locations_when_argument_is_empty()
        {
            using var save = Seeded();

            var (suggestions, isChoosing) = PlayerCommands.GetSuggestions("/location ", save.Store);

            Assert.True(isChoosing);
            Assert.Contains(suggestions, s => s.DisplayText == "The Ford" && s.InsertText == "/location The Ford");
        }

        [Fact]
        public void Location_command_narrows_suggestions_by_prefix()
        {
            using var save = Seeded();

            var (suggestions, isChoosing) = PlayerCommands.GetSuggestions("/location The", save.Store);

            Assert.True(isChoosing);
            Assert.Single(suggestions);
            Assert.Equal("The Ford", suggestions[0].DisplayText);
            Assert.Equal("/location The Ford", suggestions[0].InsertText);
        }

        [Fact]
        public void Command_without_arguments_shows_reminder_when_space_typed()
        {
            using var save = Seeded();

            var (suggestions, isChoosing) = PlayerCommands.GetSuggestions("/inventory ", save.Store);

            Assert.False(isChoosing);
            Assert.Single(suggestions);
            Assert.Equal("/inventory", suggestions[0].DisplayText);
        }

        [Fact]
        public void Character_aliases_who_and_characters_also_suggest_arguments()
        {
            using var save = Seeded();

            var (whoSuggestions, whoChoosing) = PlayerCommands.GetSuggestions("/who ", save.Store);
            Assert.True(whoChoosing);
            Assert.Contains(whoSuggestions, s => s.DisplayText == "Rowan" && s.InsertText == "/who Rowan");

            var (charsSuggestions, charsChoosing) = PlayerCommands.GetSuggestions("/characters ", save.Store);
            Assert.True(charsChoosing);
            Assert.Contains(charsSuggestions, s => s.DisplayText == "Rowan" && s.InsertText == "/characters Rowan");
        }

        [Fact]
        public void Location_aliases_where_and_locations_also_suggest_arguments()
        {
            using var save = Seeded();

            var (whereSuggestions, whereChoosing) = PlayerCommands.GetSuggestions("/where ", save.Store);
            Assert.True(whereChoosing);
            Assert.Contains(whereSuggestions, s => s.DisplayText == "The Ford" && s.InsertText == "/where The Ford");

            var (locsSuggestions, locsChoosing) = PlayerCommands.GetSuggestions("/locations ", save.Store);
            Assert.True(locsChoosing);
            Assert.Contains(locsSuggestions, s => s.DisplayText == "The Ford" && s.InsertText == "/locations The Ford");
        }

        [Fact]
        public void Unknown_character_name_returns_no_argument_suggestions_falling_back_to_reminder()
        {
            using var save = Seeded();

            var (suggestions, isChoosing) = PlayerCommands.GetSuggestions("/character NonExistent", save.Store);

            Assert.False(isChoosing);
            Assert.Single(suggestions);
            Assert.Equal("/character [name]", suggestions[0].DisplayText);
        }

        [Fact]
        public void GameWindow_completes_argument_on_tab()
        {
            using var save = Seeded();
            var state = new GameState();
            using var window = new GameWindow(state) { Store = save.Store };

            var inputField = window.SubViews.OfType<TextField>().First();

            inputField.Text = "/character Ro";

            // Tab completes to "/character Rowan"
            window.NewKeyDownEvent(Key.Tab);

            Assert.Equal("/character Rowan", inputField.Text);
        }

        [Fact]
        public void GameWindow_executes_character_with_no_args_when_enter_pressed_on_empty_arg()
        {
            using var save = Seeded();
            var state = new GameState();
            using var window = new GameWindow(state) { Store = save.Store };

            string? entered = null;
            window.CommandEntered += cmd => entered = cmd;

            var inputField = window.SubViews.OfType<TextField>().First();
            inputField.Text = "/character ";

            // Enter on empty arg executes the command rather than completing suggestion
            inputField.NewKeyDownEvent(Key.Enter);

            Assert.Equal("/character", entered);
        }

        [Fact]
        public void History_command_returns_messages_for_turn()
        {
            using var save = Seeded();
            save.Store.Transcript.Append(new TranscriptEntry { Turn = 1, Voice = TranscriptVoice.Player, Text = "hello world" });
            save.Store.Transcript.Append(new TranscriptEntry { Turn = 1, Voice = TranscriptVoice.Narrator, Text = "The world replies." });

            var result = PlayerCommands.Execute("/history 1", save.Store);
            var text = TextOf(result);

            Assert.Contains("Messages for turn 1:", text, StringComparison.Ordinal);
            Assert.Contains("> hello world", text, StringComparison.Ordinal);
            Assert.Contains("The world replies.", text, StringComparison.Ordinal);
        }

        [Fact]
        public void History_command_returns_messages_for_entity_with_pagination()
        {
            using var save = Seeded();
            for (var i = 1; i <= 7; i++)
            {
                save.Store.Transcript.Append(new TranscriptEntry
                {
                    Turn = i,
                    Voice = TranscriptVoice.Narrator,
                    Text = $"Turn {i} mentioning [Rowan](chr_1)."
                });
            }

            var page1 = PlayerCommands.Execute("/history chr_1 1", save.Store);
            var text1 = TextOf(page1);
            Assert.Contains("Page 1 of 2", text1, StringComparison.Ordinal);
            Assert.Contains("5 of 7 matches", text1, StringComparison.Ordinal);
            Assert.Contains("Turn 1", text1, StringComparison.Ordinal);
            Assert.Contains("Turn 5", text1, StringComparison.Ordinal);

            var page2 = PlayerCommands.Execute("/history chr_1 2", save.Store);
            var text2 = TextOf(page2);
            Assert.Contains("Page 2 of 2", text2, StringComparison.Ordinal);
            Assert.Contains("2 of 7 matches", text2, StringComparison.Ordinal);
            Assert.Contains("Turn 6", text2, StringComparison.Ordinal);
            Assert.Contains("Turn 7", text2, StringComparison.Ordinal);
        }

        [Fact]
        public void History_command_suggests_entities()
        {
            using var save = Seeded();
            var (suggestions, isChoosing) = PlayerCommands.GetSuggestions("/history ", save.Store);

            Assert.True(isChoosing);
            Assert.Contains(suggestions, s => s.DisplayText.Contains("Rowan"));
            Assert.Contains(suggestions, s => s.DisplayText.Contains("The Ford"));
        }

        [Fact]
        public void Inspect_command_without_args_prompts_for_name()
        {
            using var save = Seeded();
            var result = PlayerCommands.Execute("/inspect", save.Store);

            Assert.Null(result.InspectEntityId);
            Assert.Contains("Name the character, location, or item to inspect", TextOf(result), StringComparison.Ordinal);
        }

        [Fact]
        public void Inspect_command_sets_InspectEntityId_for_character_location_or_item()
        {
            using var save = Seeded();
            var player = SaveStore.Player(save.Store.ReadCharacters())!;

            var charResult = PlayerCommands.Execute("/inspect Rowan", save.Store);
            Assert.Equal(player.Id, charResult.InspectEntityId);

            var loc = save.Store.ReadLocations().Locations.First();
            var locResult = PlayerCommands.Execute($"/inspect {loc.Name}", save.Store);
            Assert.Equal(loc.Id, locResult.InspectEntityId);

            var item = save.Store.ReadItems().Items.First();
            var itemResult = PlayerCommands.Execute($"/inspect {item.Name}", save.Store);
            Assert.Equal(item.Id, itemResult.InspectEntityId);
        }

        [Fact]
        public void Inspect_command_suggests_all_entity_types()
        {
            using var save = Seeded();
            var (suggestions, isChoosing) = PlayerCommands.GetSuggestions("/inspect ", save.Store);

            Assert.True(isChoosing);
            Assert.Contains(suggestions, s => s.DisplayText.StartsWith("Rowan") && s.Role == TextRole.Character);
            Assert.Contains(suggestions, s => s.DisplayText.StartsWith("The Ford") && s.Role == TextRole.Place);
            Assert.Contains(suggestions, s => s.DisplayText.Contains("longsword") && s.Role == TextRole.Item);
        }

        [Fact]
        public void Player_command_transfers_the_tag_and_sets_RefreshState()
        {
            using var save = Seeded();
            var file = save.Store.ReadCharacters();
            file.Characters.Add(new Character { Id = "chr_2", Name = "Bess", Kind = CharacterKind.Npc });
            save.Store.WriteCharacters(file);

            var result = PlayerCommands.Execute("/player Bess", save.Store);

            Assert.True(result.RefreshState);
            Assert.Contains("Transferred player tag to Bess", TextOf(result), StringComparison.Ordinal);

            var updated = save.Store.ReadCharacters();
            Assert.Equal("Bess", SaveStore.PlayerName(updated));
            Assert.Equal(CharacterKind.Npc, SaveStore.FindCharacter(updated, "Rowan")!.Kind);
            Assert.Equal(CharacterKind.Player, SaveStore.FindCharacter(updated, "Bess")!.Kind);
        }

        [Fact]
        public void Player_command_accepts_character_id()
        {
            using var save = Seeded();
            var file = save.Store.ReadCharacters();
            file.Characters.Add(new Character { Id = "chr_2", Name = "Bess", Kind = CharacterKind.Npc });
            save.Store.WriteCharacters(file);

            var result = PlayerCommands.Execute("/player chr_2", save.Store);

            Assert.True(result.RefreshState);
            Assert.Contains("Transferred player tag to Bess", TextOf(result), StringComparison.Ordinal);
            Assert.Equal("Bess", SaveStore.PlayerName(save.Store.ReadCharacters()));
        }

        [Theory]
        [InlineData("/switch")]
        [InlineData("/play")]
        public void Player_command_aliases_work_identically(string command)
        {
            using var save = Seeded();
            var file = save.Store.ReadCharacters();
            file.Characters.Add(new Character { Id = "chr_2", Name = "Bess", Kind = CharacterKind.Npc });
            save.Store.WriteCharacters(file);

            var result = PlayerCommands.Execute($"{command} Bess", save.Store);

            Assert.True(result.RefreshState);
            Assert.Contains("Transferred player tag to Bess", TextOf(result), StringComparison.Ordinal);
            Assert.Equal("Bess", SaveStore.PlayerName(save.Store.ReadCharacters()));
        }

        [Fact]
        public void Player_command_without_arguments_shows_current_player_and_instructions()
        {
            using var save = Seeded();

            var result = PlayerCommands.Execute("/player", save.Store);

            Assert.False(result.RefreshState);
            Assert.Contains("Currently playing as Rowan", TextOf(result), StringComparison.Ordinal);
            Assert.Contains("Name of the character to become the player", TextOf(result), StringComparison.Ordinal);
        }

        [Fact]
        public void Player_command_on_current_player_reports_already_playing()
        {
            using var save = Seeded();

            var result = PlayerCommands.Execute("/player Rowan", save.Store);

            Assert.False(result.RefreshState);
            Assert.Contains("already playing as Rowan", TextOf(result), StringComparison.Ordinal);
        }

        [Fact]
        public void Player_command_reports_danger_for_unknown_character()
        {
            using var save = Seeded();

            var result = PlayerCommands.Execute("/player Nonexistent", save.Store);

            Assert.False(result.RefreshState);
            Assert.Contains("know nobody called", TextOf(result), StringComparison.Ordinal);
            Assert.Equal("Rowan", SaveStore.PlayerName(save.Store.ReadCharacters()));
        }

        [Fact]
        public void Player_command_suggests_characters_with_current_player_status()
        {
            using var save = Seeded();
            var file = save.Store.ReadCharacters();
            file.Characters.Add(new Character { Id = "chr_2", Name = "Bess", Kind = CharacterKind.Npc, Health = 10, MaxHealth = 10 });
            save.Store.WriteCharacters(file);

            var (suggestions, isChoosing) = PlayerCommands.GetSuggestions("/player ", save.Store);

            Assert.True(isChoosing);
            var rowanSuggestion = Assert.Single(suggestions, s => s.DisplayText.StartsWith("Rowan"));
            Assert.Equal("(current player)", rowanSuggestion.Summary);

            var bessSuggestion = Assert.Single(suggestions, s => s.DisplayText.StartsWith("Bess"));
            Assert.Contains("10/10", bessSuggestion.Summary);
        }
    }
}
