using System.Text;

namespace TerminalQuest.Ui
{
    /// <summary>
    /// The two text rules behind <see cref="ExternalEditor"/>'s shadow map.
    /// </summary>
    /// <remarks>
    /// Both are pure, and both guard the same failure: a character described in three paragraphs
    /// reaching the save as one line. They live apart from <see cref="ExternalEditor"/> because
    /// that type cannot be exercised without a text field, a scratch file and a real editor
    /// process, whereas these are strings in and strings out.
    /// </remarks>
    internal static class EditorText
    {
        /// <summary>Joins a multi-line value into the one line a text field can show.</summary>
        /// <remarks>
        /// Every field in the game is single-line, so this is unconditional rather than a choice a
        /// caller makes. A run of breaks becomes one space - a paragraph break is one gap, not two -
        /// and a run at the end becomes nothing, because a pending break is only ever flushed by a
        /// character that follows it.
        /// </remarks>
        public static string Flatten(string text)
        {
            ArgumentNullException.ThrowIfNull(text);

            var flattened = new StringBuilder(text.Length);
            var pendingSpace = false;

            foreach (var character in text)
            {
                if (char.IsControl(character))
                {
                    pendingSpace = flattened.Length > 0;
                    continue;
                }

                if (pendingSpace)
                {
                    flattened.Append(' ');
                    pendingSpace = false;
                }

                flattened.Append(character);
            }

            return flattened.ToString();
        }

        /// <summary>
        /// What a field really holds, given what it is showing and what the editor last returned.
        /// </summary>
        /// <remarks>
        /// The joined form is remembered alongside the whole one and compared here, so the moment
        /// the player types over what the editor returned, what they typed is what this reports.
        /// </remarks>
        public static string Resolve(string shown, string raw, string flattened) =>
            flattened == shown ? raw : shown;
    }
}
