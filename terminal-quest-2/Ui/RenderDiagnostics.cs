using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Counts how often the game actually draws, and writes the count to a log once a second.
    /// <para>
    /// The question this exists to answer is not "how expensive is a frame" but "how many frames
    /// are there". Terminal.Gui draws only views whose <see cref="Terminal.Gui.ViewBase.View.NeedsDraw"/>,
    /// <c>SubViewNeedsDraw</c> or <see cref="Terminal.Gui.ViewBase.View.NeedsLayout"/> is set, so a
    /// screen with nothing happening on it should draw nothing at all. A view that re-sets one of
    /// those flags from inside its own draw never lets the loop go idle, and the whole UI is
    /// repainted every iteration for as long as the screen is open - which is invisible in a
    /// profile of any single frame and obvious here.
    /// </para>
    /// <para>
    /// Off unless <c>TQ_DIAG=1</c>, and it never touches a view: the counters are incremented on
    /// the UI thread but read and written out on a timer thread.
    /// </para>
    /// </summary>
    internal static class RenderDiagnostics
    {
        /// <summary>The meter Terminal.Gui reports its own internals on.</summary>
        private const string MeterName = "Terminal.Gui";

        private static readonly object Gate = new();

        /// <summary>Per-instrument totals since the last line was written, keyed by instrument name.</summary>
        private static readonly Dictionary<string, (long Count, double Sum, double Max)> Instruments = new(StringComparer.Ordinal);

        private static long _iterations;
        private static long _draws;

        /// <summary>Ticks at which keys arrived and have not yet been followed by a draw.</summary>
        private static long _keyPressedAt;
        private static long _keys;
        private static long _mouse;
        private static long _wheel;

        /// <summary>Key-to-paint latencies for the current second, in milliseconds.</summary>
        private static readonly List<double> Latencies = [];

        /// <summary>Gaps between consecutive main loop iterations, in milliseconds.</summary>
        private static readonly List<double> IterationGaps = [];

        private static long _lastIterationAt;

        /// <summary>When the top view last finished drawing, so the flush after it can be timed.</summary>
        private static long _viewsDrawnAt;

        /// <summary>Where the changed cells of the last small frame were, for the log to print once.</summary>
        private static string _sample = string.Empty;

        private static View? _watched;

        /// <summary>The application, kept so the output buffer can be inspected after each draw.</summary>
        private static IApplication? _app;

        private static MeterListener? _listener;
        private static Timer? _timer;
        private static string _logPath = string.Empty;

        /// <summary>Whether the player asked for this, via <c>TQ_DIAG=1</c>.</summary>
        internal static bool Requested =>
            string.Equals(Environment.GetEnvironmentVariable("TQ_DIAG"), "1", StringComparison.Ordinal);

        /// <summary>
        /// Starts counting, for the life of the application. Does nothing unless <c>TQ_DIAG=1</c>.
        /// </summary>
        /// <remarks>
        /// Subscribed once rather than per session. Both events are the application's own and
        /// outlive the individual screens, so re-subscribing as each opens - which is what
        /// <see cref="MouseReporting"/> and <see cref="Responsiveness"/> have to do - would only
        /// count each iteration twice.
        /// </remarks>
        public static void Enable(IApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);

            if (!Requested || _timer is not null)
            {
                return;
            }

            _app = app;
            _logPath = Path.Combine(Path.GetTempPath(), "tq-render-diag.log");

            // Raised once per main loop iteration, before input, timeouts or rendering. The gap
            // between two of them is the loop's real cadence, which is what the iteration cap is
            // trying to set and what puts a floor under how soon a keystroke can be answered.
            app.Iteration += (_, _) =>
            {
                Interlocked.Increment(ref _iterations);

                WatchTopView(app);

                var now = Stopwatch.GetTimestamp();
                var previous = Interlocked.Exchange(ref _lastIterationAt, now);
                if (previous != 0)
                {
                    lock (Gate)
                    {
                        IterationGaps.Add(Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);
                    }
                }
            };

            // Raised only when at least one View was actually laid out or drawn - which is exactly
            // the "did this iteration cost anything" signal, and the one that should be silent on
            // a screen the player is not touching.
            app.LayoutAndDrawComplete += (_, _) =>
            {
                Interlocked.Increment(ref _draws);

                // The split that matters: everything the views did, versus everything that happened
                // to the frame after they were done with it. A cost that sits entirely on the second
                // side of this line is not ours to optimise in a view.
                var drawnAt = Interlocked.Exchange(ref _viewsDrawnAt, 0);
                if (drawnAt != 0)
                {
                    Record("frame:afterViews", Stopwatch.GetElapsedTime(drawnAt, Stopwatch.GetTimestamp()).TotalMilliseconds);
                }

                // The headline number: how long the player waited between pressing a key and the
                // screen changing. Only the first draw after a key counts; the rest are not waits.
                var pressedAt = Interlocked.Exchange(ref _keyPressedAt, 0);
                if (pressedAt != 0)
                {
                    lock (Gate)
                    {
                        Latencies.Add(Stopwatch.GetElapsedTime(pressedAt, Stopwatch.GetTimestamp()).TotalMilliseconds);
                    }
                }
            };

            // Counted because the wheel is the other way a frame gets asked for, and a mouse that
            // reports every notch separately is indistinguishable, from the inside, from a mouse
            // that reports ten - until the two numbers are put next to the draw count.
            app.Mouse.MouseEvent += (_, e) =>
            {
                Interlocked.Increment(ref _mouse);

                if (e.Flags.HasFlag(Terminal.Gui.Input.MouseFlags.WheeledUp)
                    || e.Flags.HasFlag(Terminal.Gui.Input.MouseFlags.WheeledDown))
                {
                    Interlocked.Increment(ref _wheel);
                }
            };

            app.Keyboard.KeyDown += (_, _) =>
            {
                Interlocked.Increment(ref _keys);

                // Compare-exchange rather than assign: if two keys land before a draw, the wait
                // being measured started with the first of them.
                Interlocked.CompareExchange(ref _keyPressedAt, Stopwatch.GetTimestamp(), 0);
            };

            StartMeterListener();

            Write($"# started {DateTime.Now:HH:mm:ss}  TQ_FPS={Responsiveness.Cap()}  " +
                  $"TQ_DRIVER={Environment.GetEnvironmentVariable("TQ_DRIVER") ?? "(default)"}  " +
                  $"TQ_MOUSE={Environment.GetEnvironmentVariable("TQ_MOUSE") ?? "1"}  " +
                  $"build={(IsDebugBuild() ? "Debug" : "Release")}");
            Write("# time     iters/s  draws/s  keys  mice  wheel | key->paint ms (mean/p50/max) | loop gap ms (mean/p50/max) | instruments (count/mean/max)");

            _timer = new Timer(_ => Sample(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        /// <summary>
        /// Keeps a <see cref="View.DrawComplete"/> subscription on whichever screen is on top, so
        /// the moment the view tree finished drawing is known. Each screen is a fresh session with a
        /// fresh top view, so this is re-checked rather than wired once.
        /// </summary>
        private static void WatchTopView(IApplication app)
        {
            var top = app.TopRunnableView;
            if (ReferenceEquals(top, _watched))
            {
                return;
            }

            if (_watched is { })
            {
                _watched.DrawComplete -= OnTopViewDrawn;
            }

            _watched = top;

            if (_watched is { })
            {
                _watched.DrawComplete += OnTopViewDrawn;
                WatchSubViews(_watched, 0);
            }
        }

        private static void OnTopViewDrawn(object? sender, EventArgs e)
        {
            Volatile.Write(ref _viewsDrawnAt, Stopwatch.GetTimestamp());
            RecordDirtyArea();
        }

        /// <summary>
        /// Counts which views actually repainted, by subscribing to each one's own
        /// <see cref="View.DrawComplete"/>.
        /// </summary>
        /// <remarks>
        /// Sampling the dirty flags before the draw does not work: they are set during input
        /// processing, which the main loop does after it raises
        /// <see cref="IApplication.Iteration"/>, so anything read there is a frame stale. Asking
        /// each view to say when it drew is the only account that lines up with the frame it
        /// belongs to.
        /// </remarks>
        private static void WatchSubViews(View parent, int depth)
        {
            if (depth > 2)
            {
                return;
            }

            foreach (var subView in parent.SubViews)
            {
                var name = subView.GetType().Name;
                subView.DrawComplete += (_, _) => Record($"drew:{name}", 1);
                WatchSubViews(subView, depth + 1);
            }
        }

        /// <summary>
        /// How much of the screen this frame is about to send to the terminal.
        /// <para>
        /// The driver skips rows with nothing dirty in them, so this is the real size of a frame -
        /// and the number to hold against the frame's cost. A view that invalidates its whole
        /// viewport rather than the part that changed rewrites every cell of it whether or not the
        /// contents differ, and the terminal is then asked to accept all of it.
        /// </para>
        /// </summary>
        private static void RecordDirtyArea()
        {
            IOutputBuffer? buffer;
            try
            {
                buffer = _app?.Driver?.GetOutputBuffer();
            }
            catch (Exception ex)
            {
                Record($"frame:bufferThrew:{ex.GetType().Name}", 1);
                return;
            }

            if (buffer is null)
            {
                // Said out loud rather than returned silently: an absent number in this log must
                // never be mistaken for a measured zero.
                Record("frame:noBuffer", 1);
                return;
            }

            try
            {
                var rowsFlagged = 0;
                var rowsWithCells = 0;
                var dirtyCells = 0;
                var colMin = int.MaxValue;
                var colMax = -1;
                var runs = 0;
                var cleanCellsInDirtyRows = 0;
                var contents = buffer.Contents;
                if (contents is null)
                {
                    return;
                }

                for (var row = 0; row < buffer.Rows && row < buffer.DirtyLines.Length; row++)
                {
                    if (!buffer.DirtyLines[row])
                    {
                        continue;
                    }

                    rowsFlagged++;

                    var inThisRow = 0;
                    var inRun = false;
                    for (var col = 0; col < buffer.Cols && col < contents.GetLength(1); col++)
                    {
                        if (contents[row, col].IsDirty)
                        {
                            inThisRow++;
                            colMin = Math.Min(colMin, col);
                            colMax = Math.Max(colMax, col);

                            if (!inRun)
                            {
                                runs++;
                                inRun = true;
                            }
                        }
                        else
                        {
                            inRun = false;
                            cleanCellsInDirtyRows++;
                        }
                    }

                    dirtyCells += inThisRow;
                    if (inThisRow > 0)
                    {
                        rowsWithCells++;
                    }
                }

                // The pair that matters. A row flagged dirty with nothing dirty in it is a row the
                // driver will walk, and move the cursor to, for no reason at all.
                Record("frame:rowsFlagged", rowsFlagged);
                Record("frame:rowsWithCells", rowsWithCells);
                Record("frame:dirtyCells", dirtyCells);
                Record("frame:screenRows", buffer.Rows);
                if (colMax >= 0)
                {
                    Record("frame:colMin", colMin);
                    Record("frame:colMax", colMax);
                }

                // The number the cost actually tracks. A run is a stretch of dirty cells with no
                // clean cell in it - one cursor move and one write for the driver. Seventy-two full
                // rows are seventy-two runs; the same seventy-two rows with a dozen scattered cells
                // in each are hundreds, and hundreds of little writes is what a terminal is slow at.
                // The number that actually predicts the cost. OutputBase.Write walks every column of
                // every row it has been told is dirty, and for each *clean* cell it steps the
                // terminal cursor forward one place - a fresh StringBuilder and a separate write
                // per cell. Dirty cells are batched; clean ones are not. So a frame is charged for
                // the gaps in it, which is why a full-screen scroll is cheaper than one keystroke.
                Record("frame:cursorMoves", cleanCellsInDirtyRows);

                Record("frame:runs", runs);

                // On a small frame - a keystroke rather than a scroll - say which columns actually
                // changed on a couple of rows. Fourteen scattered cells on every row of the screen
                // is a shape, and the shape names the view that made it.
                if (dirtyCells > 0 && dirtyCells < buffer.Rows * buffer.Cols / 4)
                {
                    _sample = DescribeSample(buffer, contents);
                }

                if (rowsWithCells > 0)
                {
                    Record("frame:runsPerRow", (double)runs / rowsWithCells);
                }
            }
            catch (Exception ex)
            {
                // The buffer can be resized underneath this; a diagnostic must not be the thing
                // that takes the game down - but it must say that it failed.
                Record($"frame:dirtyThrew:{ex.GetType().Name}", 1);
            }
        }

        /// <summary>
        /// Picks up Terminal.Gui's own metrics - <c>Redraws</c> above all, which the library
        /// documents as being there to catch "repainting entire UI every loop".
        /// </summary>
        private static void StartMeterListener()
        {
            var listener = new MeterListener
            {
                InstrumentPublished = (instrument, l) =>
                {
                    if (string.Equals(instrument.Meter.Name, MeterName, StringComparison.Ordinal))
                    {
                        l.EnableMeasurementEvents(instrument);
                    }
                },
            };

            // The library's instruments are a mix of counters and histograms, and the callbacks are
            // per measurement type, so every numeric type it might use has to be registered.
            listener.SetMeasurementEventCallback<int>((i, m, _, _) => Record(i.Name, m));
            listener.SetMeasurementEventCallback<long>((i, m, _, _) => Record(i.Name, m));
            listener.SetMeasurementEventCallback<double>((i, m, _, _) => Record(i.Name, m));
            listener.SetMeasurementEventCallback<float>((i, m, _, _) => Record(i.Name, m));

            listener.Start();
            _listener = listener;
        }

        /// <summary>
        /// Times a step and files it under <paramref name="name"/>, so it appears in the log beside
        /// the framework's own metrics. Free - not merely cheap - when TQ_DIAG is not set.
        /// </summary>
        public static void Time(string name, Action step)
        {
            if (_timer is null)
            {
                step();
                return;
            }

            var start = Stopwatch.GetTimestamp();
            try
            {
                step();
            }
            finally
            {
                Record(name, Stopwatch.GetElapsedTime(start, Stopwatch.GetTimestamp()).TotalMilliseconds);
            }
        }

        private static void Record(string name, double value)
        {
            lock (Gate)
            {
                Instruments.TryGetValue(name, out var entry);
                Instruments[name] = (entry.Count + 1, entry.Sum + value, Math.Max(entry.Max, value));
            }
        }

        /// <summary>Writes one line for the second just gone, and resets the counters.</summary>
        private static void Sample()
        {
            var iterations = Interlocked.Exchange(ref _iterations, 0);
            var draws = Interlocked.Exchange(ref _draws, 0);
            var keys = Interlocked.Exchange(ref _keys, 0);
            var mouse = Interlocked.Exchange(ref _mouse, 0);
            var wheel = Interlocked.Exchange(ref _wheel, 0);

            var line = new StringBuilder();
            line.Append(CultureInfo.InvariantCulture, $"{DateTime.Now:HH:mm:ss}  {iterations,7}  {draws,7}  {keys,5}  {mouse,5}  {wheel,5} | ");

            lock (Gate)
            {
                line.Append(Summarise(Latencies, "ms"));
                line.Append(" | ");
                line.Append(Summarise(IterationGaps, "gap"));
                line.Append(" | ");

                Latencies.Clear();
                IterationGaps.Clear();

                if (_sample.Length > 0)
                {
                    line.Append(_sample).Append("| ");
                    _sample = string.Empty;
                }

                foreach (var (name, entry) in Instruments.OrderBy(p => p.Key, StringComparer.Ordinal))
                {
                    var mean = entry.Count > 0 ? entry.Sum / entry.Count : 0;
                    line.Append(CultureInfo.InvariantCulture, $"{name}={entry.Count}/{mean:F2}/{entry.Max:F2}  ");
                }

                Instruments.Clear();
            }

            Write(line.ToString());
        }

        /// <summary>The dirty columns of the first two changed rows, capped so the log stays readable.</summary>
        private static string DescribeSample(IOutputBuffer buffer, Cell[,] contents)
        {
            var text = new StringBuilder();
            var rowsShown = 0;

            for (var row = 0; row < buffer.Rows && row < buffer.DirtyLines.Length && rowsShown < 2; row++)
            {
                if (!buffer.DirtyLines[row])
                {
                    continue;
                }

                var cols = new List<int>();
                for (var col = 0; col < buffer.Cols && col < contents.GetLength(1) && cols.Count < 24; col++)
                {
                    if (contents[row, col].IsDirty)
                    {
                        cols.Add(col);
                    }
                }

                if (cols.Count == 0)
                {
                    continue;
                }

                rowsShown++;
                text.Append(CultureInfo.InvariantCulture, $"r{row}:[{string.Join(",", cols)}] ");
            }

            return text.ToString();
        }

        /// <summary>Mean, median and worst of a sample, or a dash when there was nothing to say.</summary>
        private static string Summarise(List<double> samples, string label)
        {
            if (samples.Count == 0)
            {
                return $"{label} -";
            }

            samples.Sort();
            var mean = samples.Sum() / samples.Count;

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{label} {mean,6:F1}/{samples[samples.Count / 2],6:F1}/{samples[^1],6:F1}");
        }

        private static void Write(string line)
        {
            try
            {
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A diagnostic that takes the game down with it is worse than no diagnostic.
            }
        }

        private static bool IsDebugBuild()
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}
