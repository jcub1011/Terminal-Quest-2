namespace TerminalQuest.Saves
{
    /// <summary>
    /// One of the archetypes offered when a character is made.
    /// <para>
    /// A class is not stored anywhere. It is a recipe applied once, at creation, and everything it
    /// decides lands in fields the save already has for everybody: the kit becomes
    /// <c>inventory.json</c>, <see cref="MaxHealth"/> becomes the player's health,
    /// <see cref="Aptitude"/> is folded into their description, and <see cref="Attributes"/> writes
    /// scores that every character has whether or not a class ever chose them. So a character who
    /// ends up nothing like the class they started as is still simply a character whose record has
    /// moved on - there is no archetype underneath to contradict it.
    /// </para>
    /// </summary>
    /// <param name="Name">Shown in the picker, and read back in the aptitude line.</param>
    /// <param name="Summary">One line describing the archetype, shown beside the name.</param>
    /// <param name="Aptitude">Appended to whatever the player typed about themselves.</param>
    /// <param name="MaxHealth">Both the cap and the starting health.</param>
    /// <param name="Attributes">
    /// The opening spread. Shared statics like <see cref="StartingItems"/>, and copied for the same
    /// reason - see <see cref="CharacterAttributes.Seed"/>.
    /// <para>
    /// Every class is dealt the same total, so no archetype is stronger than another, only shaped
    /// differently. That is what keeps the picker a choice about how you want to play rather than a
    /// question with a right answer.
    /// </para>
    /// </param>
    /// <param name="StartingMoney">
    /// The purse. Small enough that the first real purchase is still a decision, and varied by
    /// class because a rogue's takings and a ranger's are not the same story.
    /// </param>
    /// <param name="StartingItems">
    /// The kit. These are templates shared by every save, so <see cref="NewGame"/> copies them
    /// rather than handing them out - <see cref="Item"/> is mutable and the narrator will edit
    /// whatever it is given.
    /// </param>
    internal sealed record ClassTemplate(
        string Name,
        string Summary,
        string Aptitude,
        int MaxHealth,
        IReadOnlyList<CharacterAttribute> Attributes,
        int StartingMoney,
        IReadOnlyList<Item> StartingItems);

    /// <summary>The archetypes offered on the character screen, in the order they are listed.</summary>
    internal static class ClassTemplates
    {
        public const int StandardStartingMoney = 15;
        public const int StandardHealingItemQuantity = 2;
        public const int StandardRationsQuantity = 3;
        public const int DefaultPointBudget = 74;

        public static IReadOnlyList<ClassTemplate> All { get; } =
        [
            new ClassTemplate(
                "Warrior",
                "Arms and armour. Hardy.",
                "A warrior, trained to arms and armour, at home in a shield wall and slow to yield.",
                30,
                Spread(strength: 16, dexterity: 11, constitution: 15, intelligence: 9, wisdom: 11, charisma: 12),
                StandardStartingMoney,
                [
                    Make("iron longsword", 1, "Notched along one edge and sharp along the rest."),
                    Make("battered shield", 1, "Oak faced with hide, and dented past straightening."),
                    Make("chain hauberk", 1, "Heavy, well-oiled, and missing a few links at the hem."),
                    MakeHealingItem(),
                    MakeRations(),
                ]),

            new ClassTemplate(
                "Mage",
                "Old syllables and wards. Frail.",
                "A mage, schooled in the old syllables, quick to read a ward and quicker to raise one.",
                18,
                Spread(strength: 8, dexterity: 12, constitution: 11, intelligence: 16, wisdom: 14, charisma: 13),
                StandardStartingMoney,
                [
                    Make("ashwood staff", 1, "Worn pale where a hand has always held it."),
                    Make("spellbook", 1, "Half its pages are still blank."),
                    Make("chalk", 3, "For circles, wards and the marks that hold them."),
                    Make("vial of ink", 1, "Iron gall, dark enough to write on stone."),
                    MakeHealingItem(),
                    MakeRations(),
                ]),

            new ClassTemplate(
                "Rogue",
                "Locks, shadows and quick hands.",
                "A rogue, light-fingered and lighter-footed, who reads a room before entering it.",
                22,
                Spread(strength: 10, dexterity: 16, constitution: 12, intelligence: 13, wisdom: 11, charisma: 12),
                StandardStartingMoney,
                [
                    Make("dagger", 2, "Plain, balanced, and easy to lose without regret."),
                    Make("lockpicks", 1, "A wrap of oiled leather holding six picks and a tension wrench."),
                    Make("dark cloak", 1, "Undyed wool gone the colour of a wet street."),
                    Make("coil of rope", 1, "Thirty feet of hemp, knotted every arm's length."),
                    MakeHealingItem(),
                    MakeRations(),
                ]),

            new ClassTemplate(
                "Ranger",
                "Bow, trail and wild country.",
                "A ranger, at ease in wild country, who can follow a day-old trail and put an arrow where they look.",
                24,
                Spread(strength: 12, dexterity: 15, constitution: 13, intelligence: 10, wisdom: 15, charisma: 9),
                StandardStartingMoney,
                [
                    Make("yew shortbow", 1, "Strung with waxed sinew, and a spare string in the grip."),
                    Make("arrows", 20, "Goose-fletched, in a stiffened leather quiver."),
                    Make("hunting knife", 1, "For skinning, mostly."),
                    Make("snare wire", 2, "A loop of brass wire, enough for a hare."),
                    MakeHealingItem(),
                    MakeRations("Dried meat and berries, a day's worth apiece."),
                ]),

            new ClassTemplate(
                "Cleric",
                "A quiet god, and mending hands.",
                "A cleric, sworn to a quiet god, whose hands mend more often than they harm.",
                24,
                Spread(strength: 12, dexterity: 10, constitution: 13, intelligence: 11, wisdom: 16, charisma: 12),
                StandardStartingMoney,
                [
                    Make("oak mace", 1, "Banded in iron. It breaks bones without breaking an oath."),
                    Make("holy symbol", 1, "Worn smooth by a thumb that returns to it when afraid."),
                    Make("vial of blessed oil", 2, "Sweet-smelling, and it burns longer than it should."),
                    MakeHealingItem(),
                    MakeRations(),
                ]),
        ];

        public static Item MakeHealingItem() =>
            Make("bandages", StandardHealingItemQuantity, "Clean linen, rolled tight.");

        public static Item MakeRations(string description = "Hard bread and salt meat, a day's worth apiece.") =>
            Make("rations", StandardRationsQuantity, description);

        public static ClassTemplate CreateDefaultCustom() =>
            new(
                "Custom",
                "A custom archetype tailored to your design.",
                "A traveler forged by their own path, unbound by conventional traditions.",
                24,
                Spread(strength: 12, dexterity: 12, constitution: 12, intelligence: 12, wisdom: 12, charisma: 14),
                StandardStartingMoney,
                [
                    Make("iron broadsword", 1, "Well-balanced steel with a crossguard wrapped in cord."),
                    Make("wooden roundshield", 1, "Planks of pine bound in brass, light enough for a skirmish."),
                    Make("traveler's charm", 1, "A carved river stone hung from a loop of braided leather."),
                    MakeHealingItem(),
                    MakeRations(),
                ]);

        public static ClassTemplate BuildCustom(
            string name,
            string summary,
            string aptitude,
            int maxHealth,
            IReadOnlyList<CharacterAttribute> attributes,
            Item weapon,
            Item offhand,
            Item specialItem)
        {
            var archetypeItems = new List<Item>
            {
                new() { Name = weapon.Name, Quantity = Math.Max(1, weapon.Quantity), Description = weapon.Description },
                new() { Name = offhand.Name, Quantity = Math.Max(1, offhand.Quantity), Description = offhand.Description },
                new() { Name = specialItem.Name, Quantity = Math.Max(1, specialItem.Quantity), Description = specialItem.Description },
                MakeHealingItem(),
                MakeRations(),
            };

            return new ClassTemplate(
                string.IsNullOrWhiteSpace(name) ? "Custom" : name.Trim(),
                string.IsNullOrWhiteSpace(summary) ? "A custom archetype tailored to your design." : summary.Trim(),
                string.IsNullOrWhiteSpace(aptitude) ? "A traveler forged by their own path, unbound by conventional traditions." : aptitude.Trim(),
                Math.Max(1, maxHealth),
                attributes,
                StandardStartingMoney,
                archetypeItems);
        }

        private static Item Make(string name, int quantity, string description) =>
            new() { Name = name, Quantity = quantity, Description = description };

        /// <summary>
        /// The six, in <see cref="CharacterAttributes.Core"/> order.
        /// </summary>
        /// <remarks>
        /// Named arguments at every call site, which is the whole reason this takes six parameters
        /// rather than a list of numbers. A bare <c>[16, 11, 15, 9, 11, 12]</c> would be six chances
        /// to transpose two scores in a way that compiles, reads plausibly, and is wrong for the
        /// life of the archetype.
        /// </remarks>
        public static IReadOnlyList<CharacterAttribute> Spread(
            int strength,
            int dexterity,
            int constitution,
            int intelligence,
            int wisdom,
            int charisma) =>
            [
                Score("Strength", strength),
                Score("Dexterity", dexterity),
                Score("Constitution", constitution),
                Score("Intelligence", intelligence),
                Score("Wisdom", wisdom),
                Score("Charisma", charisma),
            ];

        public static CharacterAttribute Score(string name, int score) =>
            new() { Name = name, Score = score };
    }
}
