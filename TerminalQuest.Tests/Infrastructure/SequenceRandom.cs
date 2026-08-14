namespace TerminalQuest.Tests.Infrastructure
{
    /// <summary>
    /// A <see cref="Random"/> that hands out the faces a test asked for, in order.
    /// </summary>
    /// <remarks>
    /// <c>Dice.TryRoll</c> takes its randomness as a parameter and <see cref="Random.Next(int, int)"/>
    /// is virtual, which is the only reason the resolver can be checked against exact totals rather
    /// than ranges. Every value is verified to be one the real generator could have produced for the
    /// range it was asked for, so a test cannot accidentally assert on an impossible die.
    /// </remarks>
    internal sealed class SequenceRandom : Random
    {
        private readonly int[] _values;
        private int _index;

        public SequenceRandom(params int[] values) => _values = values;

        /// <summary>How many draws have been taken, so a test can assert dice were not over-rolled.</summary>
        public int Draws => _index;

        public override int Next(int minValue, int maxValue)
        {
            if (_index >= _values.Length)
            {
                throw new InvalidOperationException(
                    $"The roll asked for {_index + 1} dice but the test supplied {_values.Length}.");
            }

            var value = _values[_index++];

            if (value < minValue || value >= maxValue)
            {
                throw new InvalidOperationException(
                    $"Face {value} is outside [{minValue}, {maxValue}); a real d{maxValue - 1} could never roll it.");
            }

            return value;
        }
    }
}
