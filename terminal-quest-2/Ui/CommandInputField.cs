using Terminal.Gui.Input;
using Terminal.Gui.Views;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The command box. A <see cref="TextField"/> that gives transcript scrolling priority over
    /// its own caret moves.
    /// <para>
    /// A single-line field binds Ctrl+Up/Down/Home/End to caret moves and is offered every key
    /// first, so those would never reach the window - the player would press Ctrl+Down to read
    /// on and watch the caret twitch instead. This view intercepts the scroll set and offers it
    /// to the transcript before the field sees it. What is left - typing, plain arrows and
    /// Home/End, Ctrl+Left/Right word jumps - edits exactly as a stock field.
    /// </para>
    /// </summary>
    internal sealed class CommandInputField : TextField
    {
        /// <summary>
        /// Raised for each scroll key, before the field handles it. Return true when the key
        /// scrolled something; then the caret is left alone.
        /// </summary>
        public event Func<Key, bool>? ScrollRequested;

        /// <summary>
        /// Whether <paramref name="key"/> scrolls the transcript rather than editing the line.
        /// Ctrl+Left/Right are deliberately absent: sideways has no transcript meaning, and
        /// word jumps are the field's own - the one Ctrl binding that stays an editing key.
        /// </summary>
        internal static bool IsScrollKey(Key key) =>
            key == Key.CursorUp.WithCtrl
            || key == Key.CursorDown.WithCtrl
            || key == Key.Home.WithCtrl
            || key == Key.End.WithCtrl
            || key == Key.PageUp.WithCtrl
            || key == Key.PageDown.WithCtrl;

        protected override bool OnKeyDown(Key key)
        {
            // Intercepted before the field, which would otherwise eat the arrows and Home/End
            // as caret moves. Ctrl+PgUp/PgDn would bubble on their own - a single-line field
            // implements no paging - but they are claimed here too, so the whole set behaves
            // the same however focus reached this field.
            //
            // Falls through to the field when nobody scrolled, so the keys keep their editing
            // meaning wherever there is no transcript to scroll.
            if (IsScrollKey(key) && ScrollRequested?.Invoke(key) == true)
            {
                return true;
            }

            return base.OnKeyDown(key);
        }
    }
}
