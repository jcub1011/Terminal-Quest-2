using System.Text.Json;

using TerminalQuest.Mcp;
using TerminalQuest.Saves;
using TerminalQuest.Tests.Infrastructure;

using Xunit;

namespace TerminalQuest.Tests.Mcp
{
    /// <summary>
    /// Granting a character something they know and others do not.
    /// </summary>
    public sealed class GrantSecretTests
    {
        private static JsonElement Args(string json) => JsonDocument.Parse(json).RootElement.Clone();

        private static ToolOutcome Call(SaveStore store, string arguments) =>
            QuestTools.Invoke(store, "grant_secret", Args(arguments));

        private static TempSave Seeded()
        {
            var save = new TempSave();
            NewGame.Create(save.Store, "Rowan", "A quiet sort.", ClassTemplates.All[0], "The Ford");
            save.Store.Touch(4);
            return save;
        }

        private static Secret Only(SaveStore store, string who) =>
            Assert.Single(SaveStore.FindCharacter(store.ReadCharacters(), who)!.Secrets);

        // ---- Granting ------------------------------------------------------------------------

        [Fact]
        public void A_granted_secret_is_live_and_stamped_with_the_turn()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, """
                {"character":"Rowan","name":"the sealed cellar","detail":"Bricked up the fever winter."}
                """);

            Assert.False(outcome.IsError);

            var secret = Only(save.Store, "Rowan");

            Assert.Equal("the sealed cellar", secret.Name);
            Assert.Equal("Bricked up the fever winter.", secret.Text);
            Assert.Equal(SecretStage.Live, secret.Stage);
            Assert.Equal(4, secret.Turn);
        }

        [Fact]
        public void The_result_names_the_secret_without_repeating_what_it_says()
        {
            // Echoing the detail back is how a tool result starts reading like something to narrate, and
            // whoever sent it already knows it.
            using var save = Seeded();

            var outcome = Call(save.Store, """
                {"character":"Rowan","name":"the sealed cellar","detail":"Bricked up the fever winter."}
                """);

            Assert.Contains("the sealed cellar", outcome.Text, StringComparison.Ordinal);
            Assert.DoesNotContain("Bricked up", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Two_characters_can_be_in_on_the_same_secret()
        {
            // The mechanism by which several people share one thing, and the case that must not be
            // mistaken for a collision.
            using var save = Seeded();
            var file = save.Store.ReadCharacters();
            file.Characters.Add(new Character { Id = file.TakeId(), Name = "Bess", Kind = CharacterKind.Npc });
            save.Store.WriteCharacters(file);

            Assert.False(Call(save.Store, """{"character":"Rowan","name":"the cellar","detail":"Bricked up."}""").IsError);
            Assert.False(Call(save.Store, """{"character":"Bess","name":"the cellar","detail":"Bricked up."}""").IsError);

            Assert.Equal("the cellar", Only(save.Store, "Bess").Name);
        }

        [Fact]
        public void A_name_is_trimmed_on_the_way_in()
        {
            using var save = Seeded();

            Call(save.Store, """{"character":"Rowan","name":"  the cellar  ","detail":"  Bricked up.  "}""");

            Assert.Equal("the cellar", Only(save.Store, "Rowan").Name);
            Assert.Equal("Bricked up.", Only(save.Store, "Rowan").Text);
        }

        // ---- Refusals ------------------------------------------------------------------------

        [Fact]
        public void Granting_needs_a_character()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, """{"name":"the cellar","detail":"Bricked up."}""");

            Assert.True(outcome.IsError);
            Assert.Contains("needs a character", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void Granting_needs_a_name_and_says_why()
        {
            // The name is the handle the narrator uses later to report what a line gave away, so it
            // cannot be optional.
            using var save = Seeded();

            var outcome = Call(save.Store, """{"character":"Rowan","detail":"Bricked up."}""");

            Assert.True(outcome.IsError);
            Assert.Contains("short name", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_blank_name_is_refused_like_a_missing_one()
        {
            using var save = Seeded();

            Assert.True(Call(save.Store, """{"character":"Rowan","name":"   ","detail":"Bricked up."}""").IsError);
        }

        [Fact]
        public void Granting_needs_a_detail()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, """{"character":"Rowan","name":"the cellar"}""");

            Assert.True(outcome.IsError);
            Assert.Contains("the cellar", outcome.Text, StringComparison.Ordinal);
            Assert.Contains("needs the detail", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void A_blank_detail_is_refused_like_a_missing_one()
        {
            using var save = Seeded();

            Assert.True(Call(save.Store, """{"character":"Rowan","name":"the cellar","detail":"  "}""").IsError);
        }

        [Fact]
        public void Granting_to_nobody_on_record_offers_the_way_out()
        {
            using var save = Seeded();

            var outcome = Call(save.Store, """{"character":"Nobody","name":"the cellar","detail":"Bricked up."}""");

            Assert.True(outcome.IsError);
            Assert.Contains("set_character", outcome.Text, StringComparison.Ordinal);
        }

        [Fact]
        public void The_same_name_twice_for_one_character_is_refused_rather_than_overwritten()
        {
            // A secret already on record may have been read and acted on. Quietly replacing what it says
            // is the one thing the world must never do: canon is extended, never negated.
            using var save = Seeded();
            Call(save.Store, """{"character":"Rowan","name":"the cellar","detail":"Bricked up."}""");

            var outcome = Call(save.Store, """{"character":"Rowan","name":"The Cellar","detail":"Actually wide open."}""");

            Assert.True(outcome.IsError);
            Assert.Contains("already holds", outcome.Text, StringComparison.Ordinal);
            Assert.Equal("Bricked up.", Only(save.Store, "Rowan").Text);
        }

        [Fact]
        public void A_refused_grant_writes_nothing()
        {
            using var save = Seeded();

            Call(save.Store, """{"character":"Nobody","name":"the cellar","detail":"Bricked up."}""");

            Assert.All(
                save.Store.ReadCharacters().Characters,
                character => Assert.Empty(character.Secrets));
        }

        // ---- What granting does to the turn ---------------------------------------------------

        [Fact]
        public void Granting_is_recorded_in_the_journal_like_any_other_call()
        {
            using var save = Seeded();

            Call(save.Store, """{"character":"Rowan","name":"the cellar","detail":"Bricked up."}""");

            Assert.Equal("grant_secret", Assert.Single(save.Store.Journal.Read().Entries).Tool);
        }

        [Fact]
        public void Granting_is_not_itself_a_knowledge_fetch()
        {
            // It hands nothing over, so it must not count towards the turn's reading - otherwise
            // granting to two characters in one turn would refuse itself.
            using var save = Seeded();
            var file = save.Store.ReadCharacters();
            file.Characters.Add(new Character { Id = file.TakeId(), Name = "Bess", Kind = CharacterKind.Npc });
            save.Store.WriteCharacters(file);

            Assert.False(Call(save.Store, """{"character":"Rowan","name":"one thing","detail":"A."}""").IsError);
            Assert.False(Call(save.Store, """{"character":"Bess","name":"another thing","detail":"B."}""").IsError);
        }
    }
}
