using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.ViewBase;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Fills in the gaps of a frame before it is sent, because gaps are what a frame is charged for.
    /// <para>
    /// <b>The defect this works around.</b> <c>OutputBase.Write</c> walks every column of every row
    /// it has been told is dirty. Consecutive dirty cells are batched into one write; every
    /// <em>clean</em> cell instead steps the terminal cursor forward one place, and on Windows that
    /// is a fresh <c>StringBuilder</c> and a separate console write of a <c>CSI row;colH</c> escape.
    /// A row with a dozen scattered cells in it therefore costs hundreds of individual writes, while
    /// a row that changed completely costs one.
    /// </para>
    /// <para>
    /// Measured on a 72x270 terminal: a keystroke painted about a thousand cells spread over every
    /// row and cost ~240ms, while a wheel scroll painted fifteen thousand - contiguously - and cost
    /// ~70ms. Fifteen times the content for a third of the time. The cost is the gaps.
    /// </para>
    /// <para>
    /// So this marks every cell of an already-dirty row as dirty, which turns each of those rows
    /// from hundreds of cursor moves into one batched run. It never dirties a row that was clean, so
    /// it cannot cause a frame that would not otherwise have happened - it only changes the shape of
    /// one that is already going out.
    /// </para>
    /// <para>
    /// Measured on the same terminal after this went in: <c>frame:afterViews</c> fell from ~240ms to
    /// ~6ms and the clean-cell count from ~18,000 to zero. Note that it is the whole of the fix -
    /// removing the window border was tried alongside it and changed nothing, because every row was
    /// still being flagged dirty without one.
    /// </para>
    /// <para><c>TQ_NOFILL=1</c> turns it off, for measuring what it is worth.</para>
    /// </summary>
    internal static class FrameCompaction
    {
        private static IApplication? _app;
        private static View? _watched;

        internal static bool Disabled =>
            string.Equals(Environment.GetEnvironmentVariable("TQ_NOFILL"), "1", StringComparison.Ordinal);

        /// <summary>
        /// Hooks the top screen's draw, for the life of the application.
        /// </summary>
        /// <remarks>
        /// The seam is the top view's <see cref="View.DrawComplete"/>: every view has painted by
        /// then, and the frame has not yet been written to the terminal. Re-checked each iteration
        /// because each screen is a fresh session with a fresh top view.
        /// </remarks>
        public static void Enable(IApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);

            if (Disabled)
            {
                return;
            }

            _app = app;
            app.Iteration += (_, _) => WatchTopView(app);
        }

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
            }
        }

        private static void OnTopViewDrawn(object? sender, EventArgs e) => Fill();

        private static void Fill()
        {
            IOutputBuffer? buffer;
            try
            {
                buffer = _app?.Driver?.GetOutputBuffer();
            }
            catch (Exception ex) when (ex is InvalidOperationException or NullReferenceException)
            {
                return;
            }

            if (buffer?.Contents is not { } contents)
            {
                return;
            }

            try
            {
                var rows = Math.Min(buffer.Rows, Math.Min(contents.GetLength(0), buffer.DirtyLines.Length));
                var cols = Math.Min(buffer.Cols, contents.GetLength(1));

                for (var row = 0; row < rows; row++)
                {
                    if (!buffer.DirtyLines[row])
                    {
                        continue;
                    }

                    for (var col = 0; col < cols; col++)
                    {
                        contents[row, col].IsDirty = true;
                    }
                }
            }
            catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                // The buffer can be resized underneath this between the draw and here. A frame drawn
                // the old way is the cost of losing that race, which is the right way to lose it.
            }
        }
    }
}
