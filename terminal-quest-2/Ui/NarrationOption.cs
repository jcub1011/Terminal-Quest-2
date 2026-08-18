namespace TerminalQuest.Ui
{
    /// <summary>
    /// A numbered choice presented to the player at the end of a narrator turn.
    /// </summary>
    /// <param name="Number">The 1-based option number (1, 2, 3, etc.).</param>
    /// <param name="Text">The full text of the option.</param>
    internal sealed record NarrationOption(int Number, string Text);
}
