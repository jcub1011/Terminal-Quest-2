namespace TerminalQuest.Saves
{
    /// <summary>
    /// The director's brief, and where each save keeps its own copy of it.
    /// </summary>
    internal static class DirectorPromptFile
    {
        private const string AssetRelativePath = "assets/director-prompt.md";

        public const string FileName = "director-prompt.txt";

        public static string Default => field ??= LoadDefault();

        private static string LoadDefault()
        {
            var path = Path.Combine(AppContext.BaseDirectory, AssetRelativePath);
            if (File.Exists(path))
            {
                try
                {
                    var content = File.ReadAllText(path);
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        return content;
                    }
                }
                catch
                {
                    // Fall back to built-in default
                }
            }

            return FallbackDefault;
        }

        private const string FallbackDefault =
            "### ROLE\n"
          + "You are the director and campaign overseer of a terminal adventure game. You manage overarching "
          + "plot pacing, introduce twists, manage tension, promote secrets into play, and ratify claims into canon.\n\n"

          + "### CORE DISCIPLINE\n"
          + "- You NEVER write player-facing prose or dialogue. The narrator owns all player-facing prose.\n"
          + "- You act ONLY by calling tools and issuing directives for the narrator to act upon.\n"
          + "- Every decision is emitted through tools: get_state, get_unratified_claims, ratify_claim, promote_secret, grant_secret, emit_directive.\n\n"

          + "### CAMPAIGN PACING & DIRECTIVES\n"
          + "When you are woken:\n"
          + "1. REVIEW STATE. Call get_state to inspect the player, active location, inventory, recent story events, and characters on record.\n"
          + "2. REVIEW CLAIMS. Call get_unratified_claims to inspect claims made in recent turns by characters or narration.\n"
          + "3. RATIFY. Call ratify_claim for claims that provide solid, compelling texture or facts that should become permanent canon.\n"
          + "4. SECRETS. If a dormant secret should become active for an NPC to use or conceal in upcoming scenes, call promote_secret to make it live. To give an NPC a new hidden truth, call grant_secret.\n"
          + "5. EMIT DIRECTIVE. Call emit_directive to deliver clear, structured instructions to the narrator for upcoming scenes.\n\n"

          + "### DIRECTIVE GUIDANCE\n"
          + "- Tone: Set a concrete mood/tension (e.g. \"eerie suspense\", \"rising urgency\", \"gritty intrigue\", \"quiet before the storm\").\n"
          + "- Pacing note: Give the narrator clear guidance on story direction, twists, NPC motives, or approaching complications. Do NOT write dialogue for them; tell them WHAT should develop, not verbatim words.\n"
          + "- Keep directives focused on the next 1-2 turns.";

        /// <summary>
        /// This save's prompt: what is in the file, or <see cref="Default"/> when there is nothing usable there.
        /// </summary>
        public static string Read(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            return store.ReadDirectorPrompt() is { } text && !text.AsSpan().IsWhiteSpace()
                ? text
                : Default;
        }

        /// <summary>
        /// Makes sure the save has a director prompt file, and returns the prompt it now holds.
        /// </summary>
        public static string Ensure(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            if (store.ReadDirectorPrompt() is { } existing && !existing.AsSpan().IsWhiteSpace())
            {
                return existing;
            }

            var seeded = Default.ReplaceLineEndings();
            store.WriteDirectorPrompt(seeded);
            return seeded;
        }
    }
}
