using TerminalQuest.Saves;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Represents an executable player slash command.
    /// </summary>
    internal interface IPlayerCommand
    {
        /// <summary>Metadata and usage information for this command.</summary>
        PlayerCommandInfo Info { get; }

        /// <summary>Strategy for providing auto-complete suggestions for arguments to this command.</summary>
        IArgumentCompleter Completer { get; }

        /// <summary>Executes the command with the given argument string.</summary>
        PlayerCommandResult Execute(string argument, SaveStore store);
    }

    /// <summary>
    /// A delegate-based implementation of <see cref="IPlayerCommand"/> for concise command registration.
    /// </summary>
    internal sealed class DelegatePlayerCommand : IPlayerCommand
    {
        private readonly Func<string, SaveStore, PlayerCommandResult> _handler;

        public DelegatePlayerCommand(
            PlayerCommandInfo info,
            Func<string, SaveStore, PlayerCommandResult> handler,
            IArgumentCompleter? completer = null)
        {
            Info = info;
            _handler = handler ?? throw new ArgumentNullException(nameof(handler));
            Completer = completer ?? ArgumentCompleters.Null;
        }

        public PlayerCommandInfo Info { get; }

        public IArgumentCompleter Completer { get; }

        public PlayerCommandResult Execute(string argument, SaveStore store) => _handler(argument, store);
    }
}
