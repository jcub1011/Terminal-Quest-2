namespace TerminalQuest.Ui
{
    /// <summary>
    /// The player-visible state shown in the status pane.
    /// <para>
    /// There is no simulation behind these yet - the narrator is currently the only authority on
    /// what is happening. They exist so the pane has something real to render and so the game has
    /// an obvious place to grow into.
    /// </para>
    /// </summary>
    internal sealed class GameState
    {
        public int Health { get; set; } = 20;

        public int MaxHealth { get; set; } = 20;

        public int Gold { get; set; }

        public int Turn { get; set; }

        public List<string> Inventory { get; } = [];

        /// <summary>Running total for the session, accumulated from each turn's reported cost.</summary>
        public double CostUsd { get; set; }

        /// <summary>Cache tokens read on the most recent turn.</summary>
        public int LastCacheRead { get; set; }

        /// <summary>Wall-clock duration of the most recent turn.</summary>
        public int LastDurationMs { get; set; }

        /// <summary>True while a turn is in flight, so the UI can show that it is waiting.</summary>
        public bool IsBusy { get; set; }
    }
}
