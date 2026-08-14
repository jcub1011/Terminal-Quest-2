namespace TerminalQuest.Saves
{
    /// <summary>
    /// One of the archetypes offered when a character is made.
    /// <para>
    /// A class is not stored anywhere. It is a recipe applied once, at creation, and everything it
    /// decides lands in fields the save already has: the kit becomes <c>inventory.json</c>,
    /// <see cref="MaxHealth"/> becomes the player's health, and <see cref="Aptitude"/> is folded
    /// into their description. Nothing downstream - the tool schemas, the renderer, the status
    /// pane - has to learn a new concept, and a character who ends up nothing like the class they
    /// started as is simply a character whose description has moved on.
    /// </para>
    /// </summary>
    /// <param name="Name">Shown in the picker, and read back in the aptitude line.</param>
    /// <param name="Summary">One line describing the archetype, shown beside the name.</param>
    /// <param name="Aptitude">Appended to whatever the player typed about themselves.</param>
    /// <param name="MaxHealth">Both the cap and the starting health.</param>
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
        int StartingMoney,
        IReadOnlyList<Item> StartingItems);

    /// <summary>The archetypes offered on the character screen, in the order they are listed.</summary>
    internal static class ClassTemplates
    {
        public static IReadOnlyList<ClassTemplate> All { get; } =
        [
            new ClassTemplate(
                "Warrior",
                "Arms and armour. Hardy.",
                "A warrior, trained to arms and armour, at home in a shield wall and slow to yield.",
                30,
                15,
                [
                    Make("iron longsword", 1, "Notched along one edge and sharp along the rest."),
                    Make("battered shield", 1, "Oak faced with hide, and dented past straightening."),
                    Make("chain hauberk", 1, "Heavy, well-oiled, and missing a few links at the hem."),
                    Make("bandages", 2, "Clean linen, rolled tight."),
                    Make("rations", 3, "Hard bread and salt meat, a day's worth apiece."),
                ]),

            new ClassTemplate(
                "Mage",
                "Old syllables and wards. Frail.",
                "A mage, schooled in the old syllables, quick to read a ward and quicker to raise one.",
                18,
                25,
                [
                    Make("ashwood staff", 1, "Worn pale where a hand has always held it."),
                    Make("spellbook", 1, "Half its pages are still blank."),
                    Make("chalk", 3, "For circles, wards and the marks that hold them."),
                    Make("vial of ink", 1, "Iron gall, dark enough to write on stone."),
                    Make("rations", 3, "Hard bread and salt meat, a day's worth apiece."),
                ]),

            new ClassTemplate(
                "Rogue",
                "Locks, shadows and quick hands.",
                "A rogue, light-fingered and lighter-footed, who reads a room before entering it.",
                22,
                30,
                [
                    Make("dagger", 2, "Plain, balanced, and easy to lose without regret."),
                    Make("lockpicks", 1, "A wrap of oiled leather holding six picks and a tension wrench."),
                    Make("dark cloak", 1, "Undyed wool gone the colour of a wet street."),
                    Make("coil of rope", 1, "Thirty feet of hemp, knotted every arm's length."),
                    Make("rations", 3, "Hard bread and salt meat, a day's worth apiece."),
                ]),

            new ClassTemplate(
                "Ranger",
                "Bow, trail and wild country.",
                "A ranger, at ease in wild country, who can follow a day-old trail and put an arrow where they look.",
                24,
                12,
                [
                    Make("yew shortbow", 1, "Strung with waxed sinew, and a spare string in the grip."),
                    Make("arrows", 20, "Goose-fletched, in a stiffened leather quiver."),
                    Make("hunting knife", 1, "For skinning, mostly."),
                    Make("snare wire", 2, "A loop of brass wire, enough for a hare."),
                    Make("rations", 3, "Dried meat and berries, a day's worth apiece."),
                ]),

            new ClassTemplate(
                "Cleric",
                "A quiet god, and mending hands.",
                "A cleric, sworn to a quiet god, whose hands mend more often than they harm.",
                24,
                18,
                [
                    Make("oak mace", 1, "Banded in iron. It breaks bones without breaking an oath."),
                    Make("holy symbol", 1, "Worn smooth by a thumb that returns to it when afraid."),
                    Make("vial of blessed oil", 2, "Sweet-smelling, and it burns longer than it should."),
                    Make("bandages", 3, "Clean linen, rolled tight."),
                    Make("rations", 3, "Hard bread and salt meat, a day's worth apiece."),
                ]),
        ];

        private static Item Make(string name, int quantity, string description) =>
            new() { Name = name, Quantity = quantity, Description = description };
    }
}
