using System.Runtime.InteropServices;
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
    /// On Windows, standard .NET <c>Task.Delay</c> and <c>Thread.Sleep</c> default to the system timer
    /// resolution (~15.6ms). We lower the Windows timer resolution to 1ms via <c>timeBeginPeriod</c>
    /// so the main loop can sleep for the requested fraction of a frame accurately instead of
    /// quantizing to 16ms boundaries.
    /// </para>
    /// </summary>
    internal static class Responsiveness
    {
        [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
        private static extern uint TimeBeginPeriod(uint uMilliseconds);

        [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
        private static extern uint TimeEndPeriod(uint uMilliseconds);

        private static bool _timerPeriodSet;

        /// <summary>One iteration every 10ms, against the framework default of one every 25ms.</summary>
        private const ushort DefaultIterationsPerSecond = 100;

        /// <summary>The framework's own default, and the floor this will accept.</summary>
        private const ushort MinimumIterationsPerSecond = 20;

        /// <summary>
        /// The ceiling, and it is not arbitrary. The main loop sleeps for what is left of the
        /// frame using the quoted code above, and <c>TimeSpan.Milliseconds</c> there is the
        /// whole-millisecond <em>component</em>, not the total. Ask for more than about 500
        /// iterations a second and the remainder is under a millisecond, that component is zero,
        /// and the loop stops sleeping at all: measured at <c>TQ_FPS=1000</c> it free-spun at
        /// 60,000 iterations a second, pegging a core.
        /// <para>
        /// It bought nothing. Key-to-paint latency was ~43ms at 100 iterations a second and ~43ms
        /// at 60,000, because the wait is not in this loop. Raising this is not a fix for input
        /// lag; it is only a way to spend a core discovering that.
        /// </para>
        /// </summary>
        private const ushort MaximumIterationsPerSecond = 500;

        /// <summary>
        /// Raises the main loop's iteration cap, sets high-resolution system timers, and keeps them
        /// active for the life of the application.
        /// <para>
        /// Re-asserted as each screen opens, for the same reason <see cref="MouseReporting"/> is:
        /// the game runs a fresh driver session per screen, and a setting applied once before any
        /// of them cannot be assumed to survive them all.
        /// </para>
        /// </summary>
        public static void Apply(IApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);

            EnsureHighResolutionTimer();
            Set();
            app.SessionBegun += (_, _) => Set();
        }

        private static void EnsureHighResolutionTimer()
        {
            if (_timerPeriodSet || !OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                if (TimeBeginPeriod(1) == 0)
                {
                    _timerPeriodSet = true;
                    AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                    {
                        if (_timerPeriodSet)
                        {
                            TimeEndPeriod(1);
                            _timerPeriodSet = false;
                        }
                    };
                }
            }
            catch
            {
                // Fallback to default timer resolution if winmm is unavailable
            }
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

