using System.Text.Json.Serialization;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// One roll the world made, kept so that it can be shown, read back and held against the story.
    /// <para>
    /// The reason this is written down rather than returned and forgotten: the narrator's tools run
    /// in another process, and a file is the only thing the two share. A roll that stayed in memory
    /// could never reach the screen, and the player would have nothing but the model's word for
    /// what the dice said - which is the arrangement this whole feature exists to end.
    /// </para>
    /// </summary>
    internal sealed class DiceRoll : ILogEntry
    {
        /// <summary>
        /// Stable sequence identifier within <c>rolls.jsonl</c>, assigned on append and never reused. The
        /// transcript uses it as a cursor - everything at or above it has been shown.
        /// </summary>
        public int Seq { get; set; }

        [JsonIgnore]
        public int Id
        {
            get => Seq;
            set => Seq = value;
        }

        public int Turn { get; set; }

        /// <summary>
        /// If this entry is revealing a previous roll, the Seq of the roll being revealed.
        /// Zero for an original roll.
        /// </summary>
        public int RevealsSeq { get; set; }

        /// <summary>
        /// Who rolled, by id, or empty for a roll nobody makes: a trap, the weather, the world
        /// deciding something on its own.
        /// </summary>
        public string CharacterId { get; set; } = string.Empty;

        /// <summary>
        /// What was being settled, in the narrator's own few words - "leaping the chasm", "whether
        /// the guard believes her". Required, because the player is shown it: a roll they cannot
        /// account for is worse than one they never saw.
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// The attribute whose modifier was applied, by name, or empty when none was.
        /// <para>
        /// A name rather than a reference. Freeform attributes are named things and have no id, and
        /// this is a record of what happened rather than a live pointer - if the attribute is
        /// renamed or removed later, what was rolled at the time does not change.
        /// </para>
        /// </summary>
        public string Attribute { get; set; } = string.Empty;

        /// <summary>What <see cref="Attribute"/> was worth, already counted in <see cref="Total"/>.</summary>
        public int Modifier { get; set; }

        /// <summary>The expression as the resolver tidied it, which is the form the player is shown.</summary>
        public string Notation { get; set; } = string.Empty;

        /// <summary>Every die face, kept and dropped alike, so an advantage can be seen working.</summary>
        public List<int> Faces { get; set; } = [];

        public int Total { get; set; }

        /// <summary>
        /// Whether the result is kept from the player. The roll itself never is: they are always
        /// shown that dice were thrown and what for, because a hidden roll nobody knew about is
        /// indistinguishable from the narrator simply deciding, and the point of rolling is that it
        /// is not.
        /// </summary>
        /// <remarks>
        /// The number is still written here in full, and that is intended. A save is meant to be
        /// opened and read - much of the point of keeping the world in files - and a player who goes
        /// looking in <c>rolls.json</c> has chosen to look. What hiding governs is what the game
        /// says, not what it records; a redacted file would also leave the narrator unable to read
        /// its own past rolls.
        /// </remarks>
        public bool Hidden { get; set; }

        /// <summary>
        /// Whether a hidden roll has since been shown - the trap sprung, the lie found out. The
        /// transcript draws it a second time, with its number, and <c>/rolls</c> stops withholding
        /// it. Meaningless unless <see cref="Hidden"/> is set.
        /// </summary>
        public bool Revealed { get; set; }
    }
}
