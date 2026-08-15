using Terminal.Gui.App;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Makes the game answer a keystroke as soon as it can.
    /// <para>
    /// Terminal.Gui puts two throttles between a key being pressed and the character appearing.
    /// The input thread polls the terminal on a fixed 20ms delay, and the main loop - which is
    /// what drains that input and draws - runs no more often than
    /// <see cref="Application.MaximumIterationsPerSecond"/>, which defaults to one iteration
    /// every 25ms. Left alone the two add up to roughly 45ms in the worst case, and because both
    /// of them batch, two keys pressed close together are drawn in the same frame. That is what
    /// fast typing feels like: not a steady delay but characters arriving in clumps.
    /// </para>
    /// <para>
    /// Only the second throttle is ours to move, so this moves it. An iteration with nothing to
    /// redraw is nearly free - the loop only draws views whose
    /// <see cref="Terminal.Gui.ViewBase.View.NeedsDraw"/> is set - but the cap is not a free
    /// parameter either, because of how the loop honours it:
    /// <code>
    /// TimeSpan sleepFor = TimeSpan.FromMilliseconds (timeAllowed) - took;
    /// if (sleepFor.Milliseconds > 0) { Task.Delay (sleepFor).Wait (); }
    /// </code>
    /// An iteration that overruns its budget leaves nothing to sleep for, so the loop stops
    /// sleeping and spins - redrawing continuously and competing with the input thread it is
    /// supposed to be draining. The cap therefore has to leave room for the slowest honest draw.
    /// Little is given up by leaving that room, because <c>Task.Delay</c> cannot resolve finer than
    /// about 15ms on Windows by default - so 100 and 200 ask for very different things and get
    /// nearly the same one.
    /// </para>
    /// </summary>
    internal static class Responsiveness
    {
        /// <summary>One iteration every 10ms, against the framework default of one every 25ms.</summary>
        private const ushort DefaultIterationsPerSecond = 100;

        /// <summary>The framework's own default, and the floor this will accept.</summary>
        private const ushort MinimumIterationsPerSecond = 20;

        private const ushort MaximumIterationsPerSecond = 1000;

        /// <summary>
        /// Raises the main loop's iteration cap, and keeps it raised for the life of the
        /// application.
        /// <para>
        /// Re-asserted as each screen opens, for the same reason <see cref="MouseReporting"/> is:
        /// the game runs a fresh driver session per screen, and a setting applied once before any
        /// of them cannot be assumed to survive them all.
        /// </para>
        /// </summary>
        public static void Apply(IApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);

            Set();
            app.SessionBegun += (_, _) => Set();
        }

        private static void Set() => Application.MaximumIterationsPerSecond = Cap();

        /// <summary>
        /// TQ_FPS overrides the cap, for a machine where the extra wake-ups are not worth it -
        /// or one where they are not enough. Clamped, so a typo cannot stall the loop or spin it.
        /// </summary>
        internal static ushort Cap() =>
            ushort.TryParse(Environment.GetEnvironmentVariable("TQ_FPS"), out var fps)
                ? Math.Clamp(fps, MinimumIterationsPerSecond, MaximumIterationsPerSecond)
                : DefaultIterationsPerSecond;
    }
}
