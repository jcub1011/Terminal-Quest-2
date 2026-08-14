namespace TerminalQuest.Saves
{
    /// <summary>
    /// A person in the world - the player or anyone the narrator voices.
    /// </summary>
    internal sealed class Character
    {
        /// <summary>
        /// What every other record points at them by. Opaque, permanent, and never shown to the
        /// player or the narrator - see <see cref="EntityIds"/>.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// What they are called, which is theirs to change. Renaming touches this and nothing
        /// else: rosters and memory subjects point at <see cref="Id"/>.
        /// </summary>
        public string Name { get; set; } = string.Empty;

        public CharacterKind Kind { get; set; }

        public int Health { get; set; }

        public int MaxHealth { get; set; }

        /// <summary>Background and aptitude: who they are and what they are good at.</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// What they are made of. The six core attributes are always present in effect - seeded at
        /// creation, and filled in at <see cref="CharacterAttributes.Neutral"/> on read for anyone
        /// who predates them - and the narrator may add named ones of its own as the story earns
        /// them.
        /// <para>
        /// Where <see cref="Description"/> says what someone is good at, this says how good, in a
        /// form the dice can use. The two are not redundant: prose is what the narrator reads to
        /// voice them, and a score is what the resolver reads so that the narrator cannot decide
        /// the outcome by describing it.
        /// </para>
        /// <para>
        /// A list rather than a dictionary. Order carries meaning - the six in their canonical
        /// order, then whatever the story grew, in the order it grew them - and a list is what
        /// every other collection in the save already is, so it hand-edits the same way.
        /// </para>
        /// </summary>
        public List<CharacterAttribute> Attributes { get; set; } = [];

        /// <summary>Everything they know, oldest first.</summary>
        public List<Memory> Memories { get; set; } = [];

        /// <summary>
        /// What they know that not everybody may. Absent on a save made before secrets existed, which
        /// reads as an empty list and is exactly right: nobody was keeping anything.
        /// </summary>
        /// <remarks>
        /// Beside <see cref="Memories"/> rather than inside it, because the two differ only in who may
        /// be told and that difference is the entire feature. Folding secrets into memories would put
        /// a gated field on the type that every existing render path already prints in full.
        /// <para>
        /// Nothing renders this. Not <see cref="Mcp.QuestRender.Character"/>, not the status pane, not
        /// the player commands. The only reader is the lifecycle gate, and that is not tidiness: a
        /// secret reaches the narrator through one function or it does not reach it at all, which is
        /// what makes withholding structural rather than a matter of the prompt asking nicely.
        /// </para>
        /// </remarks>
        public List<Secret> Secrets { get; set; } = [];
    }
}
