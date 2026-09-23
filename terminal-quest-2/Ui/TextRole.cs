namespace TerminalQuest.Ui
{
    /// <summary>
    /// The semantic role of a run of text. Spans carry a role rather than a concrete
    /// colour so that the palette lives in exactly one place (<see cref="Theme"/>) and
    /// the span model stays independent of the rendering library.
    /// </summary>
    internal enum TextRole
    {
        /// <summary>Ordinary narration.</summary>
        Normal = 0,

        /// <summary>An object the player could plausibly take or use.</summary>
        Item,

        /// <summary>A threat, injury, or something going wrong.</summary>
        Danger,

        /// <summary>Words spoken aloud by a character.</summary>
        Speech,

        /// <summary>A named location.</summary>
        Place,

        /// <summary>A character or person.</summary>
        Character,

        /// <summary>Out-of-fiction text from the game itself, not the narrator.</summary>
        System,

        /// <summary>The player's own input, echoed back into the transcript.</summary>
        Command,

        /// <summary>
        /// A die the world threw, shown inline in the transcript.
        /// <para>
        /// Like <see cref="Command"/> this is the game's own voice rather than the narrator's, and
        /// like <see cref="Command"/> it is deliberately absent from <see cref="MarkupParser"/> and
        /// from the prompt's list of tags. The narrator cannot write one, because a roll line is
        /// drawn from the save rather than from prose - which is the whole point, since a roll the
        /// model could type is a roll the model could invent or leak.
        /// </para>
        /// </summary>
        Roll,

        /// <summary>
        /// Dimmed chrome and guidance: placeholders, summaries, separators, key hints, scroll
        /// chrome. Read last, never first - anything in this role must be safe to skim past.
        /// </summary>
        Hint,

        /// <summary>
        /// Something the player can press or pick: a choice number, the command prompt, a hotkey.
        /// Distinct from <see cref="Item"/> (gold), which means furniture of the world.
        /// </summary>
        Button,

        /// <summary>
        /// Text the player is editing in the command box. Brighter than <see cref="Normal"/> so
        /// the line being typed never has to compete with the transcript above it.
        /// </summary>
        Input,

        /// <summary>
        /// A warning that is not yet an error: notices, high-context thresholds, section headers
        /// the eye should land on. <see cref="Danger"/> stays reserved for things going wrong.
        /// </summary>
        Important,
    }
}
