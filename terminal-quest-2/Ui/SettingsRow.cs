namespace TerminalQuest.Ui
{
    /// <summary>One line on a settings page.</summary>
    /// <param name="Label">What the setting is called.</param>
    /// <param name="Value">
    /// What it is set to, or a word about what choosing it would mean. Empty draws nothing.
    /// </param>
    /// <param name="IsActive">
    /// Whether this is the one in force - the adapter a session would be built against, the model
    /// that would narrate. Drawn as an asterisk and in green, and deliberately separate from where
    /// the cursor happens to be resting.
    /// </param>
    internal readonly record struct SettingsRow(string Label, string Value, bool IsActive);
}
