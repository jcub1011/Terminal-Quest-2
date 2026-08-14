using TerminalQuest.Ui;

namespace TerminalQuest.Tests.Infrastructure
{
    /// <summary>
    /// Stands in for the transcript view, recording what the pump handed it.
    /// </summary>
    internal sealed class RecordingSink : INarrationSink
    {
        private readonly Lock _gate = new();
        private readonly List<string> _deltas = [];

        /// <summary>Runs on every <see cref="AppendDelta"/>, to stage a delta arriving mid-drain.</summary>
        public Action? OnAppend { get; set; }

        public IReadOnlyList<string> Deltas
        {
            get
            {
                lock (_gate)
                {
                    return [.. _deltas];
                }
            }
        }

        public int Commits { get; private set; }

        /// <summary>Everything appended, in order, as one string.</summary>
        public string Text
        {
            get
            {
                lock (_gate)
                {
                    return string.Concat(_deltas);
                }
            }
        }

        public void AppendDelta(string text)
        {
            lock (_gate)
            {
                _deltas.Add(text);
            }

            var hook = OnAppend;
            OnAppend = null;
            hook?.Invoke();
        }

        public void CommitBlock() => Commits++;
    }
}
