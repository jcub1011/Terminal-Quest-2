namespace TerminalQuest.Ui
{
    /// <summary>
    /// Represents the width and height of the terminal in character cells.
    /// </summary>
    internal readonly record struct TerminalSize(int Width, int Height);

    /// <summary>
    /// Monitors terminal dimension changes and provides resize-aware key reading utilities.
    /// </summary>
    internal static class TerminalMonitor
    {
        private const int DefaultWidth = 80;
        private const int DefaultHeight = 24;

        /// <summary>
        /// Safely retrieves the current terminal dimensions.
        /// </summary>
        public static TerminalSize GetSize()
        {
            try
            {
                if (!Console.IsOutputRedirected)
                {
                    var width = Console.WindowWidth > 0 ? Console.WindowWidth : (Console.BufferWidth > 0 ? Console.BufferWidth : DefaultWidth);
                    var height = Console.WindowHeight > 0 ? Console.WindowHeight : (Console.BufferHeight > 0 ? Console.BufferHeight : DefaultHeight);
                    return new TerminalSize(width, height);
                }
            }
            catch
            {
                // Ignored: fallback to defaults when redirected or querying is unsupported
            }

            return new TerminalSize(DefaultWidth, DefaultHeight);
        }

        /// <summary>
        /// Checks if the terminal dimensions have changed compared to <paramref name="lastKnownSize"/>.
        /// If changed, updates <paramref name="lastKnownSize"/> with the new dimensions.
        /// </summary>
        public static bool HasResized(ref TerminalSize lastKnownSize)
        {
            var current = GetSize();
            if (current != lastKnownSize)
            {
                lastKnownSize = current;
                return true;
            }
            return false;
        }

        /// <summary>
        /// Reads a key from the console synchronously. While waiting for a key press, checks for terminal
        /// resize events and triggers <paramref name="onResize"/> when detected.
        /// </summary>
        public static ConsoleKeyInfo ReadKeyOrResize(
            Action? onResize,
            int pollIntervalMs = 30,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return default;
            }

            if (Console.IsInputRedirected)
            {
                try
                {
                    var ch = Console.Read();
                    if (ch == -1)
                    {
                        return default;
                    }
                    return new ConsoleKeyInfo((char)ch, (ConsoleKey)ch, false, false, false);
                }
                catch
                {
                    return default;
                }
            }

            var lastSize = GetSize();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        return Console.ReadKey(intercept: true);
                    }
                }
                catch
                {
                    try
                    {
                        var ch = Console.Read();
                        if (ch == -1)
                        {
                            return default;
                        }
                        return new ConsoleKeyInfo((char)ch, (ConsoleKey)ch, false, false, false);
                    }
                    catch
                    {
                        return default;
                    }
                }

                if (HasResized(ref lastSize))
                {
                    try
                    {
                        onResize?.Invoke();
                    }
                    catch
                    {
                        // Repaint is best-effort
                    }
                }

                try
                {
                    Thread.Sleep(pollIntervalMs);
                }
                catch
                {
                    break;
                }
            }

            return default;
        }

        /// <summary>
        /// Reads a key from the console asynchronously. While waiting for a key press, checks for terminal
        /// resize events and triggers <paramref name="onResize"/> when detected.
        /// </summary>
        public static async Task<ConsoleKeyInfo> ReadKeyOrResizeAsync(
            Action? onResize,
            int pollIntervalMs = 30,
            CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return default;
            }

            if (Console.IsInputRedirected)
            {
                try
                {
                    var ch = Console.Read();
                    if (ch == -1)
                    {
                        return default;
                    }
                    return new ConsoleKeyInfo((char)ch, (ConsoleKey)ch, false, false, false);
                }
                catch
                {
                    return default;
                }
            }

            var lastSize = GetSize();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        return Console.ReadKey(intercept: true);
                    }
                }
                catch
                {
                    try
                    {
                        var ch = Console.Read();
                        if (ch == -1)
                        {
                            return default;
                        }
                        return new ConsoleKeyInfo((char)ch, (ConsoleKey)ch, false, false, false);
                    }
                    catch
                    {
                        return default;
                    }
                }

                if (HasResized(ref lastSize))
                {
                    try
                    {
                        onResize?.Invoke();
                    }
                    catch
                    {
                        // Repaint is best-effort
                    }
                }

                try
                {
                    await Task.Delay(pollIntervalMs, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    break;
                }
            }

            return default;
        }
    }
}
