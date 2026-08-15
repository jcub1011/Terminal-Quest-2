using System.Text.RegularExpressions;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// A numbered choice presented to the player at the end of a narrator turn.
    /// </summary>
    /// <param name="Number">The 1-based option number (1, 2, 3, etc.).</param>
    /// <param name="Text">The full text of the option.</param>
    /// <param name="RowIndices">The indices of the rows in the transcript pane that comprise this option.</param>
    internal sealed record NarrationOption(int Number, string Text, IReadOnlyList<int> RowIndices);

    /// <summary>
    /// Detects active numbered choices at the end of the transcript.
    /// </summary>
    internal static partial class NarrationOptionDetector
    {
        [GeneratedRegex(@"^\s*(?:\[(\d+)\][\.\:\-]?|(\d+)\s*[\.\)\:\-])\s*(.*)$")]
        private static partial Regex ChoiceHeaderRegex();

        /// <summary>
        /// Scans transcript rows backwards to find the active numbered choices presented by the narrator.
        /// </summary>
        /// <param name="rows">The list of wrapped transcript rows.</param>
        /// <returns>A list of options ordered by choice number (1..N), or an empty list if none are active.</returns>
        public static IReadOnlyList<NarrationOption> Detect(IReadOnlyList<StyledLine> rows)
        {
            ArgumentNullException.ThrowIfNull(rows);

            if (rows.Count == 0)
            {
                return [];
            }

            var endIndex = rows.Count - 1;

            // Skip trailing blank rows and system-only notices (such as recall markers and "The narrator is ready.")
            while (endIndex >= 0)
            {
                var row = rows[endIndex];
                if (row.Length == 0 || string.IsNullOrWhiteSpace(TextOf(row)))
                {
                    endIndex--;
                    continue;
                }

                if (IsSystemRow(row))
                {
                    endIndex--;
                    continue;
                }

                break;
            }

            if (endIndex < 0)
            {
                return [];
            }

            // If the last non-system line is a player command, the choices are in the past.
            if (IsCommandRow(rows[endIndex]))
            {
                return [];
            }

            var foundOptions = new List<NarrationOption>();
            var pendingRows = new List<int>();
            int? expectedNumber = null;

            for (var i = endIndex; i >= 0; i--)
            {
                var row = rows[i];
                var text = TextOf(row);

                if (IsCommandRow(row))
                {
                    break;
                }

                if (row.Length == 0 || string.IsNullOrWhiteSpace(text))
                {
                    // A blank line between choices or before choices
                    if (foundOptions.Count > 0 && expectedNumber == 0)
                    {
                        break;
                    }

                    if (foundOptions.Count == 0)
                    {
                        // Trailing blank line inside the prose before any choice header was encountered
                        break;
                    }

                    // A blank line inside a choice is unexpected; stop
                    break;
                }

                var match = ChoiceHeaderRegex().Match(text);
                if (match.Success)
                {
                    var numStr = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
                    if (int.TryParse(numStr, out var num))
                    {
                        if (expectedNumber is null || num == expectedNumber.Value)
                        {
                            var rowIndices = new List<int> { i };
                            rowIndices.AddRange(pendingRows);
                            pendingRows.Clear();

                            var optionText = match.Groups[3].Value.Trim();
                            foundOptions.Add(new NarrationOption(num, optionText, rowIndices));

                            expectedNumber = num - 1;

                            if (num == 1)
                            {
                                // We found Option 1, which completes the contiguous choice set.
                                break;
                            }

                            continue;
                        }

                        // Number wasn't the expected consecutive number (e.g. unrelated numbered list)
                        break;
                    }
                }

                // Continuation line of a choice (or prose if no choice header found yet)
                if (foundOptions.Count > 0)
                {
                    pendingRows.Insert(0, i);
                    if (pendingRows.Count > 10)
                    {
                        // Choice shouldn't wrap over 10 rows; abort
                        break;
                    }
                }
                else
                {
                    pendingRows.Insert(0, i);
                    if (pendingRows.Count > 6)
                    {
                        // More than 6 rows without a choice header at the end means no choices at end
                        break;
                    }
                }
            }

            if (foundOptions.Count == 0 || expectedNumber != 0)
            {
                return [];
            }

            foundOptions.Reverse();

            // Verify options are 1..N
            for (var i = 0; i < foundOptions.Count; i++)
            {
                if (foundOptions[i].Number != i + 1)
                {
                    return [];
                }
            }

            return foundOptions;
        }

        private static string TextOf(StyledLine line) =>
            string.Concat(line.Spans.Select(s => s.Text));

        private static bool IsSystemRow(StyledLine line) =>
            line.Spans.Count > 0 && line.Spans.All(s => s.Role == TextRole.System);

        private static bool IsCommandRow(StyledLine line) =>
            line.Spans.Count > 0 && line.Spans.Any(s => s.Role == TextRole.Command);
    }
}
