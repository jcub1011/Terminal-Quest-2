namespace TerminalQuest.Ui
{
    /// <summary>One of the player's own commands, as both <c>/help</c> and the suggestions read it.</summary>
    /// <param name="Name">
    /// The word after the slash, lower case. Never contains a space: <see cref="PlayerCommands"/>
    /// splits input positionally, so a two-word name could not be dispatched.
    /// </param>
    /// <param name="Arguments">
    /// What may follow the name, in the usual shorthand - <c>[name]</c> for optional and
    /// <c>&lt;name&gt;</c> for required. Empty for a command that takes nothing.
    /// </param>
    /// <param name="Summary">What the command is for, in the words <c>/help</c> uses.</param>
    /// <param name="IsAlias">
    /// Whether this is a second name for a command listed elsewhere in the table. Aliases are
    /// offered as suggestions - they are commands, and a player typing <c>/in</c> means one of
    /// them - but kept out of <c>/help</c>, which is a list of what the game does rather than of
    /// every spelling it accepts.
    /// </param>
    internal readonly record struct PlayerCommandInfo(
        string Name,
        string Arguments,
        string Summary,
        bool IsAlias = false)
    {
        /// <summary>The command as it is written down: the slash, the name, and what may follow.</summary>
        public string Usage => Arguments.Length == 0 ? $"/{Name}" : $"/{Name} {Arguments}";
    }

    /// <summary>
    /// A suggestion shown in the command suggestion strip, for completing a command or argument.
    /// </summary>
    /// <param name="InsertText">What replacing the input with this suggestion produces.</param>
    /// <param name="DisplayText">What appears in the suggestion list (e.g. command usage or argument name).</param>
    /// <param name="Summary">The description or hint shown beside the suggestion.</param>
    /// <param name="Role">The text role to use when rendering the suggestion name.</param>
    internal readonly record struct SuggestionItem(
        string InsertText,
        string DisplayText,
        string Summary = "",
        TextRole Role = TextRole.Command);
}

