using TerminalQuest.Saves;

using Xunit;

namespace TerminalQuest.Tests.Saves
{
    /// <summary>
    /// The rules that turn a stored score into a number the dice can use.
    /// </summary>
    public sealed class CharacterAttributesTests
    {
        private static Character WithAttributes(params (string Name, int Score)[] attributes)
        {
            var character = new Character { Id = "chr_1", Name = "Tam" };

            foreach (var (name, score) in attributes)
            {
                character.Attributes.Add(new CharacterAttribute { Name = name, Score = score });
            }

            return character;
        }

        // ---- Modifier: floored, not truncated --------------------------------------------

        [Theory]
        [InlineData(1, -5)]
        [InlineData(2, -4)]
        [InlineData(3, -4)]
        [InlineData(7, -2)]   // (7-10)/2 truncates to -1; the rule says -2
        [InlineData(8, -1)]
        [InlineData(9, -1)]
        [InlineData(10, 0)]
        [InlineData(11, 0)]
        [InlineData(12, 1)]
        [InlineData(20, 5)]
        [InlineData(30, 10)]
        public void Modifier_floors_rather_than_truncating(int score, int expected)
        {
            Assert.Equal(expected, CharacterAttributes.Modifier(score));
        }

        [Fact]
        public void Every_odd_score_below_neutral_floors_downward()
        {
            // The whole below-average half is where truncation and flooring disagree, so sweep it
            // rather than trusting the handful of cases above.
            for (var score = CharacterAttributes.MinScore; score < CharacterAttributes.Neutral; score++)
            {
                var expected = (int)Math.Floor((score - CharacterAttributes.Neutral) / 2.0);

                Assert.Equal(expected, CharacterAttributes.Modifier(score));
                Assert.True(CharacterAttributes.Modifier(score) <= (score - CharacterAttributes.Neutral) / 2);
            }
        }

        [Theory]
        [InlineData(0, "+0")]   // "+0" reads as a fact; "0" reads as a gap
        [InlineData(3, "+3")]
        [InlineData(-2, "-2")]
        public void Sign_always_shows_itself(int modifier, string expected)
        {
            Assert.Equal(expected, CharacterAttributes.Sign(modifier));
        }

        // ---- Naming ----------------------------------------------------------------------

        [Theory]
        [InlineData("Strength")]
        [InlineData("strength")]
        [InlineData("  DEX  ")]
        [InlineData("con")]
        [InlineData("Charisma")]
        public void Core_attributes_are_recognised_however_they_are_written(string name)
        {
            Assert.True(CharacterAttributes.IsCore(name));
        }

        [Theory]
        [InlineData("Luck")]
        [InlineData("Standing in the guild")]
        [InlineData("")]
        [InlineData(null)]
        public void Anything_else_is_not_core(string? name)
        {
            Assert.False(CharacterAttributes.IsCore(name));
        }

        [Theory]
        [InlineData(" dex ", "Dexterity")]
        [InlineData("STR", "Strength")]
        [InlineData("wisdom", "Wisdom")]
        [InlineData("  Luck  ", "Luck")]
        public void Canonical_name_spells_the_six_out_and_trims_the_rest(string name, string expected)
        {
            Assert.Equal(expected, CharacterAttributes.CanonicalName(name));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Canonical_name_is_null_only_when_nothing_was_asked_for(string? name)
        {
            Assert.Null(CharacterAttributes.CanonicalName(name));
        }

        [Fact]
        public void Core_is_listed_in_a_fixed_order()
        {
            // The order is what stops a rewrite of one attribute reshuffling the document.
            Assert.Equal(
                ["Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma"],
                CharacterAttributes.Core);
        }

        // ---- Find and All ----------------------------------------------------------------

        [Fact]
        public void Find_locates_an_attribute_by_an_abbreviation()
        {
            var character = WithAttributes(("Strength", 18));

            var found = CharacterAttributes.Find(character, "str");

            Assert.NotNull(found);
            Assert.Equal(18, found.Score);
        }

        [Fact]
        public void Find_returns_null_for_an_attribute_the_character_lacks()
        {
            Assert.Null(CharacterAttributes.Find(WithAttributes(), "Luck"));
        }

        [Fact]
        public void All_yields_the_six_first_and_the_story_added_ones_after()
        {
            var character = WithAttributes(("Luck", 14), ("Dexterity", 16));

            var names = CharacterAttributes.All(character).Select(a => a.Name).ToList();

            Assert.Equal(
                ["Strength", "Dexterity", "Constitution", "Intelligence", "Wisdom", "Charisma", "Luck"],
                names);
        }

        [Fact]
        public void An_unmentioned_core_attribute_reads_as_neutral_without_being_written()
        {
            var character = WithAttributes();

            var all = CharacterAttributes.All(character).ToList();

            Assert.Equal(6, all.Count);
            Assert.All(all, attribute => Assert.Equal(CharacterAttributes.Neutral, attribute.Score));

            // Reading a character must not rewrite them. A save made before attributes existed
            // stays exactly as it was.
            Assert.Empty(character.Attributes);
        }

        [Fact]
        public void All_reports_stored_scores_over_the_baseline()
        {
            var character = WithAttributes(("Wisdom", 7));

            var wisdom = CharacterAttributes.All(character).Single(a => a.Name == "Wisdom");

            Assert.Equal(7, wisdom.Score);
        }

        [Fact]
        public void All_checks_its_argument_only_once_enumeration_starts()
        {
            // ThrowIfNull inside an iterator is deferred to the first MoveNext, so a test that
            // merely calls All would pass while the guard did nothing.
            var sequence = CharacterAttributes.All(null!);

            Assert.Throws<ArgumentNullException>(() => sequence.ToList());
        }

        [Fact]
        public void Find_checks_its_argument_immediately()
        {
            Assert.Throws<ArgumentNullException>(() => CharacterAttributes.Find(null!, "Strength"));
        }

        // ---- Set -------------------------------------------------------------------------

        [Fact]
        public void Set_stores_under_the_canonical_name()
        {
            var character = WithAttributes();

            var attribute = CharacterAttributes.Set(character, "str", 15);

            Assert.Equal("Strength", attribute.Name);
            Assert.Equal("Strength", Assert.Single(character.Attributes).Name);
        }

        [Fact]
        public void Set_updates_in_place_rather_than_adding_a_second_entry()
        {
            var character = WithAttributes(("Strength", 10));

            CharacterAttributes.Set(character, "STR", 17);

            var only = Assert.Single(character.Attributes);
            Assert.Equal(17, only.Score);
        }

        [Theory]
        [InlineData(-5, CharacterAttributes.MinScore)]
        [InlineData(0, CharacterAttributes.MinScore)]
        [InlineData(999, CharacterAttributes.MaxScore)]
        [InlineData(15, 15)]
        public void Set_clamps_the_score_into_range(int given, int expected)
        {
            var character = WithAttributes();

            Assert.Equal(expected, CharacterAttributes.Set(character, "Strength", given).Score);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void Set_refuses_a_blank_name(string name)
        {
            var exception = Assert.Throws<ArgumentException>(
                () => CharacterAttributes.Set(WithAttributes(), name, 10));

            Assert.Equal("name", exception.ParamName);
        }

        // ---- Seed ------------------------------------------------------------------------

        [Fact]
        public void Seed_gives_a_character_all_six()
        {
            var character = WithAttributes();

            CharacterAttributes.Seed(character, null);

            Assert.Equal(CharacterAttributes.Core, character.Attributes.Select(a => a.Name).ToList());
            Assert.All(character.Attributes, a => Assert.Equal(CharacterAttributes.Neutral, a.Score));
        }

        [Fact]
        public void A_spread_wins_over_the_baseline()
        {
            var character = WithAttributes();

            CharacterAttributes.Seed(character, [new CharacterAttribute { Name = "Strength", Score = 16 }]);

            Assert.Equal(16, CharacterAttributes.Find(character, "Strength")!.Score);
            Assert.Equal(6, character.Attributes.Count);
        }

        [Fact]
        public void Seed_leaves_what_the_character_already_had()
        {
            var character = WithAttributes(("Charisma", 4));

            CharacterAttributes.Seed(character, null);

            Assert.Equal(4, CharacterAttributes.Find(character, "Charisma")!.Score);
        }

        [Fact]
        public void Seed_copies_the_spread_rather_than_sharing_it()
        {
            // The spread is typically a shared static and the narrator edits attributes in place,
            // so storing the instance would spend the next character's score.
            var spread = new CharacterAttribute { Name = "Strength", Score = 16 };
            var character = WithAttributes();

            CharacterAttributes.Seed(character, [spread]);
            CharacterAttributes.Set(character, "Strength", 3);

            Assert.Equal(16, spread.Score);
        }

        [Fact]
        public void Seed_carries_a_non_core_attribute_through()
        {
            var character = WithAttributes();

            CharacterAttributes.Seed(character, [new CharacterAttribute { Name = "Luck", Score = 13 }]);

            Assert.Equal(13, CharacterAttributes.Find(character, "Luck")!.Score);
            Assert.Equal(7, character.Attributes.Count);
        }

        // ---- A save that spells an attribute the short way -------------------------------

        [Fact]
        public void An_attribute_stored_under_an_abbreviation_is_still_the_characters_score()
        {
            // A hand-edited save — which EntityIds and SaveStore both go out of their way to
            // tolerate — can hold "str" rather than "Strength". Both sides of the comparison in
            // Find are canonicalised so that score stays visible: comparing the stored name raw
            // would leave the core loop in All yielding a fresh Strength at Neutral while the
            // tail loop skipped the stored entry, because IsCore("str") is true. The player's 18
            // would silently become a 10 on the sheet and in every roll made against it.
            var character = WithAttributes(("str", 18));

            var strength = CharacterAttributes.All(character).Single(a => a.Name == "Strength");

            Assert.Equal(18, strength.Score);
        }

        [Fact]
        public void An_attribute_stored_under_an_abbreviation_is_not_dropped_from_the_sheet()
        {
            // The same case from the other side: the score must not merely be read correctly, it
            // must be somewhere in the enumeration at all.
            var character = WithAttributes(("str", 18));

            var all = CharacterAttributes.All(character).ToList();

            Assert.Contains(all, attribute => attribute.Score == 18);
        }
    }
}
