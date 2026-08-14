using Terminal.Gui.App;
using Terminal.Gui.Drivers;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// Keeps the mouse reported to the application.
    /// <para>
    /// This class used to do the opposite. The game was keyboard-only, so reporting was turned off
    /// and the terminal kept the mouse - which in Windows Terminal meant dragging to select text and
    /// right-clicking to copy or paste went on working as they do everywhere else.
    /// </para>
    /// <para>
    /// The wheel changed that. Scrolling the transcript needs wheel events, and no terminal reports
    /// the wheel without also reporting the buttons, so the application has to take the whole mouse
    /// or none of it. What it costs is the terminal's own selection and clipboard: in Windows
    /// Terminal those move onto Shift+drag and Shift+right-click, and Ctrl+Shift+C/V are unaffected.
    /// </para>
    /// <para>
    /// Windows Terminal's own Ctrl+Scroll font zoom is a third cost, and the one with no shifted
    /// spelling to move onto: with reporting on, the terminal forwards Ctrl+wheel to us rather than
    /// acting on it. There is no way to have both. Reporting is all-or-nothing, no escape sequence
    /// sets the font - Windows Terminal does not implement xterm's OSC 50 - and SetCurrentConsoleFontEx
    /// is a silent no-op under ConPTY, which is what Windows Terminal is. So the game cannot resize
    /// its own text and must not pretend to. Ctrl+= and Ctrl+- are untouched by any of this, being
    /// keyboard bindings the terminal consumes before we are offered them, and they are what the
    /// hints on the save menu, at the head of a session, and under /help point players at.
    /// </para>
    /// </summary>
    internal static class MouseReporting
    {
        /// <summary>
        /// Turns mouse reporting on, and keeps it on for the life of the application.
        /// <para>
        /// Terminal.Gui asks for reporting itself as each session starts, so this mostly matters for
        /// what happens between sessions - and for saying out loud, in one place, that the game wants
        /// the mouse. This game runs several sessions in turn: the save menu, the character screen,
        /// the settings and the game itself.
        /// </para>
        /// </summary>
        public static void Enable(IApplication app)
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
        /// its own escape sequences to this console, and disabling mouse reporting on its way out is
        /// one of the things it can leave behind.
        /// </para>
        /// </summary>
        public static void Reapply(IApplication app)
        {
            ArgumentNullException.ThrowIfNull(app);
            Apply(app);
        }

        private static void Apply(IApplication app)
        {
            // Lets Terminal.Gui dispatch mouse events internally...
            app.Mouse.IsMouseDisabled = false;

            // ...and this asks the terminal to send them, which is the half an outside program can
            // have undone.
            app.Driver?.WriteRaw(EscSeqUtils.CSI_EnableMouseEvents);
        }
    }
}
