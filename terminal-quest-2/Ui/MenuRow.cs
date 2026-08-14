namespace TerminalQuest.Ui
{
    /// <summary>One line of a menu, on the start page or anywhere in the settings.</summary>
    /// <param name="Label">What the row is called.</param>
    /// <param name="Value">
    /// What it is set to, or a word about what choosing it would mean. Empty draws nothing.
    /// </param>
    /// <param name="IsActive">
    /// Whether this is the one in force - the adapter a session would be built against, the model
    /// that would narrate. Drawn as an asterisk and in green, and deliberately separate from where
    /// the cursor happens to be resting.
    /// </param>
    /// <param name="HasSubmenu">
    /// Whether Right goes somewhere from here. Drawn as a chevron in the column just past the
    /// longest label, so a row that leads deeper says so without the player having to try it -
    /// close enough to the name to be read with it rather than stranded at the far edge.
    /// </param>
    internal readonly record struct MenuRow(
        string Label,
        string Value,
        bool IsActive = false,
        bool HasSubmenu = false);
}
