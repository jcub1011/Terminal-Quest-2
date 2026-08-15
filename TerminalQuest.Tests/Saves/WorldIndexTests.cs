using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The seam where ids become names and back. The whole identity scheme rests on one rule —
    /// an id never leaves the save layer — and this is where it is kept.
    /// </summary>
    public sealed class WorldIndexTests
    {
        private static CharacterFile Characters(params (string Id, string Name)[] characters)
        {
            var file = new CharacterFile();

            foreach (var (id, name) in characters)
            {
                file.Characters.Add(new Character { Id = id, Name = name });
            }

            return file;
        }

        private static LocationFile Locations(params (string Id, string Name)[] locations)
        {
            var file = new LocationFile();

            foreach (var (id, name) in locations)
            {
                file.Locations.Add(new Location { Id = id, Name = name });
            }

            return file;
        }

        private static ItemFile Items(params (string Id, string Name)[] items)
        {
            var file = new ItemFile();

            foreach (var (id, name) in items)
            {
                file.Items.Add(new ItemDefinition { Id = id, Name = name });
            }

            return file;
        }

        [Fact]
        public void An_empty_index_answers_to_nothing()
        {
            var index = WorldIndex.Build();

            Assert.Null(index.NameOf("chr_1"));
            Assert.Null(index.IdOf("Rowan"));
        }

        [Fact]
        public void Names_and_ids_translate_both_ways()
        {
            var index = WorldIndex.Build(Characters(("chr_1", "Rowan")));

            Assert.Equal("Rowan", index.NameOf("chr_1"));
            Assert.Equal("chr_1", index.IdOf("Rowan"));
        }

        [Fact]
        public void Every_document_is_indexed()
        {
            var index = WorldIndex.Build(
                Characters(("chr_1", "Rowan")),
                Locations(("loc_1", "The Ford")),
                Items(("itm_1", "Rope")));

            Assert.Equal("Rowan", index.NameOf("chr_1"));
            Assert.Equal("The Ford", index.NameOf("loc_1"));
            Assert.Equal("Rope", index.NameOf("itm_1"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("chr_9")]
        public void An_id_nothing_answers_to_has_no_name(string? id)
        {
            var index = WorldIndex.Build(Characters(("chr_1", "Rowan")));

            Assert.Null(index.NameOf(id));
        }

        [Theory]
        [InlineData("rowan")]
        [InlineData("  Rowan  ")]
        [InlineData("ROWAN")]
        public void Name_lookup_trims_and_ignores_case(string name)
        {
            var index = WorldIndex.Build(Characters(("chr_1", "Rowan")));

            Assert.Equal("chr_1", index.IdOf(name));
        }

        [Fact]
        public void Name_lookup_is_exact_rather_than_partial()
        {
            // A substring that resolved to two entities would be worse than not resolving.
            var index = WorldIndex.Build(Characters(("chr_1", "Rowan")));

            Assert.Null(index.IdOf("Row"));
        }

        [Fact]
        public void Characters_win_over_locations_when_a_name_is_shared()
        {
            // Characters, then locations, then items — the order the narrator most often means.
            var index = WorldIndex.Build(
                Characters(("chr_1", "Ash")),
                Locations(("loc_1", "Ash")));

            Assert.Equal("chr_1", index.IdOf("Ash"));
        }

        [Fact]
        public void Locations_win_over_items_when_a_name_is_shared()
        {
            var index = WorldIndex.Build(
                locations: Locations(("loc_1", "Anvil")),
                items: Items(("itm_1", "Anvil")));

            Assert.Equal("loc_1", index.IdOf("Anvil"));
        }

        [Fact]
        public void A_blank_id_is_not_indexed()
        {
            var index = WorldIndex.Build(Characters((string.Empty, "Nameless")));

            Assert.Null(index.IdOf("Nameless"));
        }

        [Fact]
        public void A_duplicate_id_keeps_the_first_record_that_claimed_it()
        {
            // Only reachable through hand-editing, and rebinding halfway through a document
            // would be harder to explain than consistently meaning the first claim.
            var index = WorldIndex.Build(Characters(("chr_1", "Rowan"), ("chr_1", "Ash")));

            Assert.Equal("Rowan", index.NameOf("chr_1"));
        }

        [Fact]
        public void The_loser_of_a_duplicate_id_is_unreachable_by_name_too()
        {
            // Non-obvious consequence of first-writer-wins: the second record is dropped from
            // both directions, so its name resolves to nothing rather than to the shared id.
            var index = WorldIndex.Build(Characters(("chr_1", "Rowan"), ("chr_1", "Ash")));

            Assert.Null(index.IdOf("Ash"));
        }

        [Fact]
        public void Names_of_translates_a_list_in_order()
        {
            var index = WorldIndex.Build(Characters(("chr_1", "Rowan"), ("chr_2", "Ash")));

            Assert.Equal(["Ash", "Rowan"], index.NamesOf(["chr_2", "chr_1"]).ToList());
        }

        [Fact]
        public void A_dangling_reference_reads_as_absent_rather_than_as_an_id()
        {
            // Showing "chr_9" to the narrator or the player would break the one rule this class
            // exists to keep, and neither could do anything with it.
            var index = WorldIndex.Build(Characters(("chr_1", "Rowan")));

            Assert.Equal(["Rowan"], index.NamesOf(["chr_9", "chr_1"]).ToList());
        }

        [Fact]
        public void Names_of_checks_its_argument_once_enumeration_starts()
        {
            var sequence = WorldIndex.Build().NamesOf(null!);

            Assert.Throws<ArgumentNullException>(() => sequence.ToList());
        }
    }
}
