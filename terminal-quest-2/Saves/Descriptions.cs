namespace TerminalQuest.Saves
{
    /// <summary>
    /// The rule for changing a description: add to it, never replace it.
    /// <para>
    /// <see cref="Character.Description"/> and <see cref="Location.Description"/> are the only place in
    /// this save format where a contradiction can actually be committed. Everything else the narrator
    /// writes is appended - memories, location events, story events, rolls - and an append-only
    /// structure cannot contradict itself. So this is the one surface that needs a rule, and one rule
    /// serves both fields.
    /// </para>
    /// <para>
    /// The consequence is real and is accepted rather than worked around: a description grows and can
    /// never be corrected from inside the fiction. An oak door established early and wanted in iron
    /// later cannot be edited away. That is what <c>add_location_event</c> is for - "the oak door has
    /// been replaced with iron" is a lasting change to a place rather than a description edit - and
    /// what hand-editing the save is for when something simply came out wrong. A flag the model could
    /// set to overwrite was considered and rejected for the reason the narrator is not given a
    /// <c>[roll]</c> tag: a permission the model can grant itself is not a guarantee.
    /// </para>
    /// </summary>
    internal static class Descriptions
    {
        /// <summary>
        /// How much description one place or person may accumulate before further additions are refused.
        /// </summary>
        /// <remarks>
        /// A guess, and one real play will want to change. The cost it controls is not disk but attention:
        /// this text is read back on every fetch of that record for the rest of the campaign, so an
        /// unbounded field is a growing tax on every turn rather than merely an untidy document. The
        /// ceiling is what turns "grows forever" into a refusal that names somewhere better to put it.
        /// </remarks>
        public const int MaxLength = 2000;

        /// <summary>
        /// What a description becomes when something new is said about the thing it describes.
        /// </summary>
        /// <remarks>
        /// Joined with a space rather than a newline, so the field stays one paragraph of prose: it is
        /// rendered as a line among lines, and a newline inside it would read as two facts where there is
        /// one description.
        /// <para>
        /// The already-said check is the one that earns its keep in practice. A narrator that calls an
        /// upsert twice in a scene sends the same sentence twice, and without this the field doubles
        /// inside a single session - which would make extending look like a mistake within an evening of
        /// shipping it.
        /// </para>
        /// </remarks>
        /// <param name="existing">What the record says now. May be empty.</param>
        /// <param name="addition">What is newly true, or null when nothing was supplied.</param>
        /// <returns>
        /// The description to store, or null when the addition will not fit - so the caller can refuse
        /// and say where the change belongs instead.
        /// </returns>
        public static string? Extend(string existing, string? addition)
        {
            if (addition is null || addition.Trim() is not { Length: > 0 } trimmed)
            {
                return existing;
            }

            if (existing.Trim().Length == 0)
            {
                return trimmed.Length <= MaxLength ? trimmed : null;
            }

            if (existing.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return existing;
            }

            var extended = existing.TrimEnd() + " " + trimmed;

            return extended.Length <= MaxLength ? extended : null;
        }
    }
}
