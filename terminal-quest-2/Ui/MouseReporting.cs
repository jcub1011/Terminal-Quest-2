using Terminal.Gui.App;
using Terminal.Gui.Drivers;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Hands the mouse back to the terminal.
    /// <para>
    /// Terminal.Gui turns on full mouse reporting as it starts, which tells the terminal to send
    /// every click and drag to the application instead of acting on them itself. In Windows
    /// Terminal that costs the two things a player actually wants from the mouse here: dragging to
    /// select text, and right-clicking to copy or paste. Nothing in this game reads a mouse event -
    /// every view is <c>CanFocus = false</c> and driven by the keyboard - so the reporting is paid
    /// for and never used.
    /// </para>
    /// <para>
    /// Turning it off restores the terminal's own selection and clipboard, which is a better
    /// clipboard than the game could offer: it copies what is on screen, including the narration,
    /// which is hand-drawn and has no selection model of its own.
    /// </para>
    /// </summary>
    internal static class MouseReporting
    {
        /// <summary>
        /// Stops mouse reporting, and keeps it stopped for the life of the application.
        /// <para>
        /// Re-asserted as each screen opens because the driver enables reporting when it starts a
        /// session, and this game runs three of them in turn - the save menu, the character screen
        /// and the game itself.
        /// </para>
        /// </summary>
        public static void Disable(IApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);

            Apply(app);
            app.SessionBegun += (_, _) => Apply(app);
        }

        /// <summary>
        /// Says it again, for a caller that has reason to think the terminal was talked to behind the
        /// driver's back.
        /// <para>
        /// <see cref="ExternalEditor"/> is that caller: a terminal editor run through Ctrl+G writes
        /// its own escape sequences to this console, and re-enabling mouse reporting is one of the
        /// things it can leave behind.
        /// </para>
        /// </summary>
        public static void Reapply(IApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);
            Apply(app);
        }

        private static void Apply(IApplication app)
        {
            // Stops Terminal.Gui dispatching mouse events internally...
            app.Mouse.IsMouseDisabled = true;

            // ...and this tells the terminal to stop sending them, which is the half that gives
            // selection and right-click back.
            app.Driver?.WriteRaw(EscSeqUtils.CSI_DisableMouseEvents);
        }
    }
}
