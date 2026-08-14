using System.Text;

namespace TerminalQuest.Saves
{
    /// <summary>
    /// What a notation came to, once the dice had fallen.
    /// </summary>
    /// <param name="Notation">
    /// The expression as parsed and tidied - lower case, no spaces. What the player is shown, so it
    /// comes back from the parser rather than being echoed from what the narrator typed: the two
    /// differ whenever the narrator was sloppy, and the tidy one is the honest one.
    /// </param>
    /// <param name="Faces">
    /// Every die rolled, in the order the terms named them, kept and dropped alike. Dropped dice are
    /// here on purpose - seeing <c>17</c> beside a dropped <c>4</c> is what makes advantage legible
    /// as something that happened rather than a number that arrived.
    /// </param>
    /// <param name="Total">The sum, honouring keeps, flat terms and their signs.</param>
    internal sealed record DiceOutcome(string Notation, IReadOnlyList<int> Faces, int Total);

    /// <summary>
    /// The dice, and the only place in the game where chance lives.
    /// <para>
    /// This exists so that no model decides a mechanical outcome. The narrator says what is being
    /// attempted and the resolver says how it went; a number the narrator merely asserts is not a
    /// result, and there is nothing here it can reach to make one.
    /// </para>
    /// <para>
    /// Pure, in the sense that matters: no store, no I/O, no clock, and the randomness arrives as a
    /// parameter. That is what would let a future caller run it ahead of the narrator, or against a
    /// seeded sequence, without touching a line of the parser - see <see cref="TryRoll"/>.
    /// </para>
    /// </summary>
    internal static class Dice
    {
        /// <summary>
        /// Bounds, every one of them there because the notation is written by a language model.
        /// <para>
        /// These are not balance decisions. <c>999999d999999</c> is a plausible typo and would sit
        /// spinning while the CLI, the game and the player all wait on it, so the limits are set
        /// where a real expression could never reach them and a runaway one always does.
        /// </para>
        /// </summary>
        public const int MaxDice = 100;

        public const int MinSides = 2;

        public const int MaxSides = 1000;

        public const int MaxTerms = 8;

        public const int MaxFlat = 10000;

        /// <summary>How the notation should be written, said the same way in every failure.</summary>
        private const string Shape =
            "Write it like 2d6+3, d20, 4d6kh3, or 2d20kh1 for advantage and 2d20kl1 for disadvantage.";

        /// <summary>
        /// Parses standard dice notation and rolls it, or explains why it could not.
        /// </summary>
        /// <param name="notation">
        /// <c>2d6+3</c>, <c>d20</c>, <c>4d6kh3</c>, <c>1d8+1d4+2</c>, <c>1d20-1d4</c>. Case and
        /// spacing are ignored. <c>kh</c> and <c>kl</c> keep the highest or lowest dice of their
        /// term, defaulting to one; a bare <c>k</c> means <c>kh</c>.
        /// </param>
        /// <param name="random">
        /// Where the numbers come from. A parameter rather than a static reached for in here, so
        /// this stays a function of its inputs: seeding the game later, or replaying one, becomes a
        /// change at the call site instead of a change to the dice.
        /// </param>
        /// <param name="error">
        /// Why it failed, when it did. Written for the narrator to read and correct itself - so it
        /// names what to write instead, never merely what was wrong.
        /// </param>
        /// <returns>The outcome, or null when <paramref name="notation"/> could not be parsed.</returns>
        /// <remarks>
        /// A returned message rather than a thrown exception, matching <c>ToolOutcome</c>: a
        /// malformed expression is a sentence the model should act on, not a fault. The one thing
        /// that does throw is a null argument, which is a programming error and not a bad roll.
        /// </remarks>
        public static DiceOutcome? TryRoll(string notation, Random random, out string error)
        {
            ArgumentNullException.ThrowIfNull(random);

            if (notation is null || Compact(notation) is not { Length: > 0 } text)
            {
                error = $"A roll needs a notation. {Shape}";
                return null;
            }

            var faces = new List<int>();
            var total = 0;
            var terms = 0;
            var index = 0;

            while (index < text.Length)
            {
                if (terms == MaxTerms)
                {
                    error = $"'{text}' has too many terms. Use at most {MaxTerms}.";
                    return null;
                }

                // Every term after the first has to be joined by a sign. The first may carry one.
                var sign = 1;

                if (text[index] is '+' or '-')
                {
                    sign = text[index] == '-' ? -1 : 1;
                    index++;

                    if (index == text.Length)
                    {
                        error = $"'{text}' ends on a sign with nothing after it. {Shape}";
                        return null;
                    }
                }
                else if (terms > 0)
                {
                    error = $"'{text}' runs two terms together. Join them with + or -. {Shape}";
                    return null;
                }

                if (!TryTerm(text, ref index, sign, random, faces, ref total, out error))
                {
                    return null;
                }

                terms++;
            }

            if (terms == 0)
            {
                error = $"'{text}' is not dice notation. {Shape}";
                return null;
            }

            error = string.Empty;
            return new DiceOutcome(text, faces, total);
        }

        /// <summary>
        /// Reads one term - dice or a flat number - rolls it, and folds it into the running total.
        /// </summary>
        private static bool TryTerm(
            string text,
            ref int index,
            int sign,
            Random random,
            List<int> faces,
            ref int total,
            out string error)
        {
            var leading = ReadNumber(text, ref index);

            // No 'd' means the number stood alone, which is a flat bonus or penalty.
            if (index >= text.Length || text[index] is not 'd')
            {
                if (leading is not { } flat)
                {
                    error = $"'{text}' is not dice notation. {Shape}";
                    return false;
                }

                if (flat > MaxFlat)
                {
                    error = $"'{text}' carries a bonus of {flat}, which is too large. Use at most {MaxFlat}.";
                    return false;
                }

                total += sign * flat;
                error = string.Empty;
                return true;
            }

            index++;

            // "d20" is one d20: the count is what may be left out, not the die.
            var count = leading ?? 1;

            if (count == 0)
            {
                error = $"'{text}' rolls no dice. Roll at least one, or write the number on its own.";
                return false;
            }

            if (count > MaxDice)
            {
                error = $"'{text}' rolls too many dice. Roll at most {MaxDice} at once.";
                return false;
            }

            if (ReadNumber(text, ref index) is not { } sides)
            {
                error = $"'{text}' does not say how many sides the die has. {Shape}";
                return false;
            }

            if (sides < MinSides)
            {
                error = $"A die needs at least {MinSides} sides. {Shape}";
                return false;
            }

            if (sides > MaxSides)
            {
                error = $"'{text}' uses a die with too many sides. Use at most {MaxSides}.";
                return false;
            }

            if (!TryKeep(text, ref index, count, out var keep, out var keepHighest, out error))
            {
                return false;
            }

            // Rolled into a slice of the shared list, so the faces stay in the order the terms
            // named them and the keep below can sort a copy without disturbing that.
            var first = faces.Count;
            for (var i = 0; i < count; i++)
            {
                faces.Add(random.Next(1, sides + 1));
            }

            total += sign * Sum(faces, first, count, keep, keepHighest);

            error = string.Empty;
            return true;
        }

        /// <summary>
        /// Reads a trailing <c>kh</c>/<c>kl</c>/<c>k</c> clause, if there is one.
        /// </summary>
        /// <remarks>
        /// <c>dl</c> and <c>dh</c> - drop lowest, drop highest - are deliberately not accepted.
        /// <c>4d6kh3</c> and <c>4d6dl1</c> say the same thing, and supporting both would double the
        /// ways an expression can be got subtly wrong for no expressive gain. The failure text names
        /// <c>kh</c>/<c>kl</c>, so a narrator that reaches for the other spelling corrects itself in
        /// one round trip.
        /// </remarks>
        private static bool TryKeep(
            string text,
            ref int index,
            int count,
            out int keep,
            out bool keepHighest,
            out string error)
        {
            keep = count;
            keepHighest = true;
            error = string.Empty;

            if (index >= text.Length || text[index] is not 'k')
            {
                return true;
            }

            index++;

            if (index < text.Length && text[index] is 'h' or 'l')
            {
                keepHighest = text[index] == 'h';
                index++;
            }

            // A bare "k" keeps the highest, which is what everybody who writes it means.
            keep = ReadNumber(text, ref index) ?? 1;

            if (keep < 1)
            {
                error = $"'{text}' keeps no dice. Keep at least one.";
                return false;
            }

            if (keep > count)
            {
                error = $"'{text}' keeps more dice than it rolls. Keep at most {count}.";
                return false;
            }

            return true;
        }

        /// <summary>Sums a term's dice, taking only the highest or lowest few when a keep applies.</summary>
        private static int Sum(List<int> faces, int first, int count, int keep, bool keepHighest)
        {
            if (keep >= count)
            {
                var whole = 0;
                for (var i = 0; i < count; i++)
                {
                    whole += faces[first + i];
                }

                return whole;
            }

            // A copy, so that Faces keeps the order the dice were actually thrown in - the player is
            // shown that list, and sorting it would hide which die was dropped.
            var sorted = faces.GetRange(first, count);
            sorted.Sort();

            var total = 0;
            for (var i = 0; i < keep; i++)
            {
                total += keepHighest ? sorted[count - 1 - i] : sorted[i];
            }

            return total;
        }

        /// <summary>
        /// Reads a run of digits, or null when the next character is not one. Bounded so that a
        /// long run of digits cannot overflow before the limits above get a chance to refuse it.
        /// </summary>
        private static int? ReadNumber(string text, ref int index)
        {
            var start = index;
            var value = 0;

            while (index < text.Length && char.IsAsciiDigit(text[index]))
            {
                // Saturates rather than wrapping. Every caller compares against a limit far below
                // this, so a clamped value is refused with the same message a merely large one is.
                value = value > MaxFlat * 100 ? value : (value * 10) + (text[index] - '0');
                index++;
            }

            return index == start ? null : value;
        }

        /// <summary>Lower-cases and strips whitespace, so <c>2 D 6 + 3</c> is the expression it looks like.</summary>
        private static string Compact(string notation)
        {
            var text = new StringBuilder(notation.Length);

            foreach (var c in notation)
            {
                if (!char.IsWhiteSpace(c))
                {
                    text.Append(char.ToLowerInvariant(c));
                }
            }

            return text.ToString();
        }
    }
}
