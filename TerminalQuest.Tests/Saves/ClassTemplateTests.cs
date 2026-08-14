using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The archetypes offered at character creation.
    /// </summary>
    /// <remarks>
    /// Asserted as invariants rather than as values. Nobody wants a test that has to be edited
    /// every time a class's flavour text changes, but the promises the picker rests on — equal
    /// totals, the six in order, a usable kit — are exactly the kind a future edit breaks in
    /// silence.
    /// <para>
    /// Nothing here may mutate <see cref="ClassTemplates.All"/>: it hands out shared instances,
    /// so a test that wrote to one would poison every test that ran after it.
    /// </para>
    /// </remarks>
    public sealed class ClassTemplateTests
    {
        public static TheoryData<string> ClassNames()
        {
            var data = new TheoryData<string>();

            foreach (var template in ClassTemplates.All)
            {
                data.Add(template.Name);
            }

            return data;
        }

        private static ClassTemplate Template(string name) =>
            ClassTemplates.All.Single(t => t.Name == name);

        [Fact]
        public void There_are_classes_to_choose_from()
        {
            Assert.NotEmpty(ClassTemplates.All);
        }

        [Fact]
        public void Every_class_is_dealt_the_same_total()
        {
            // The design promise the picker rests on: no archetype is stronger than another, only
            // shaped differently. A single transposed score would break it silently, and the
            // picker would quietly become a question with a right answer.
            var totals = ClassTemplates.All
                .Select(template => template.Attributes.Sum(attribute => attribute.Score))
                .Distinct()
                .ToList();

            Assert.Single(totals);
        }

        [Theory]
        [MemberData(nameof(ClassNames))]
        public void Every_class_names_the_six_in_core_order(string name)
        {
            // Named parameters at the call site protect against transposition; this protects
            // against the list itself being reordered or a name being misspelled.
            Assert.Equal(
                CharacterAttributes.Core,
                Template(name).Attributes.Select(attribute => attribute.Name).ToList());
        }

        [Theory]
        [MemberData(nameof(ClassNames))]
        public void Every_score_is_in_range(string name)
        {
            Assert.All(Template(name).Attributes, attribute =>
                Assert.InRange(attribute.Score, CharacterAttributes.MinScore, CharacterAttributes.MaxScore));
        }

        [Theory]
        [MemberData(nameof(ClassNames))]
        public void Every_class_can_survive_a_hit(string name)
        {
            Assert.True(Template(name).MaxHealth > 0);
        }

        [Theory]
        [MemberData(nameof(ClassNames))]
        public void Every_class_starts_solvent(string name)
        {
            Assert.True(Template(name).StartingMoney >= 0);
        }

        [Theory]
        [MemberData(nameof(ClassNames))]
        public void Every_kit_item_is_usable(string name)
        {
            var template = Template(name);

            Assert.NotEmpty(template.StartingItems);
            Assert.All(template.StartingItems, item =>
            {
                Assert.False(string.IsNullOrWhiteSpace(item.Name));
                Assert.True(item.Quantity > 0);
            });
        }

        [Theory]
        [MemberData(nameof(ClassNames))]
        public void Every_class_describes_itself(string name)
        {
            var template = Template(name);

            Assert.False(string.IsNullOrWhiteSpace(template.Summary));
            Assert.False(string.IsNullOrWhiteSpace(template.Aptitude));
        }

        [Fact]
        public void Class_names_are_unique()
        {
            var names = ClassTemplates.All.Select(template => template.Name).ToList();

            Assert.Equal(names.Count, names.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        }

        [Fact]
        public void A_kit_item_carries_no_id_until_a_save_gives_it_one()
        {
            // Ids are allocated per save by InventoryFile.TakeId; a template carrying one would
            // hand the same id to every character ever made.
            Assert.All(ClassTemplates.All, template =>
                Assert.All(template.StartingItems, item => Assert.True(string.IsNullOrEmpty(item.Id))));
        }
    }
}
