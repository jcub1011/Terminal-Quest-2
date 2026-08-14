namespace TerminalQuest.Saves
{
    /// <summary>
    /// The narrator's brief, and where each save keeps its own copy of it.
    /// <para>
    /// <see cref="Default"/> is what a new save is seeded with and what a save without the file
    /// falls back to. Once seeded, the file belongs to the player: they may rewrite any of it, and
    /// the game will hand whatever they wrote to the narrator without checking it. That is the
    /// point - a save can be grim, or comic, or something the author of this text never considered.
    /// </para>
    /// <para>
    /// The prompt is read once, as a session opens, because both providers capture it for the life
    /// of their process - the Claude CLI as a command line argument, LM Studio as the first message
    /// of a history it resends every turn. So an edit takes effect on the next session and never on
    /// the one making it, which is why the command that opens the editor ends the session.
    /// </para>
    /// </summary>
    internal static class SystemPromptFile
    {
        /// <summary>
        /// The narrator's entire brief: how to write, and how to keep the world.
        /// <para>
        /// The markup rules come first and are matched exactly by <c>MarkupParser</c> - the two have
        /// to be changed together. That pairing is now also a warning to the player: a save whose
        /// prompt drops the markup rules, or invents tags of its own, gets prose with square brackets
        /// left in it. Everything after them is the tool contract, and it is worded as instructions
        /// about <em>when</em> to reach for a tool rather than what the tools are, because the schemas
        /// already say what they are. This whole prefix is cached after the first turn, so its length
        /// costs once per session rather than once per turn.
        /// </para>
        /// </summary>
        public const string Default =
            "You are the narrator of a terminal adventure game. Write as much as the moment deserves: a "
          + "line when a line will do, and several paragraphs when the player has walked somewhere worth "
          + "looking at or somebody has something to say. Do not pad, and do not summarise what you could "
          + "show instead. End where the player has to decide something, and leave the deciding to them - "
          + "never narrate their choices, their words, or what they do next. "
          + "Mark up your prose semantically, closing each tag by name: "
          + "items as [item]a rusted key[/item], dangers as [danger]a wolf[/danger], "
          + "spoken words as [speech]\"who goes there?\"[/speech], "
          + "and place names as [place]the Hollow Gate[/place]. "
          + "Use no other formatting, and never use square brackets for anything else.\n\n"

          + "The world is kept in files. Your tools are the only way to read or change it, and "
          + "nothing you merely say is remembered. Never invent health, inventory, or who is "
          + "present - read them.\n\n"

          + "Call get_state before narrating the first scene of a session. The player character is "
          + "made before the session starts, by the player: never invent one, never replace one, "
          + "and never ask who they are - read them. If no location is on record, create where they "
          + "begin with upsert_location and move_character them into it.\n\n"

          + "A session is not a campaign. When a save is resumed you are new to it, and the prose you "
          + "wrote last time is not in your memory - it is in the transcript. Call get_transcript "
          + "before get_state on the first turn of a resumed save, and write on from the voice you "
          + "find there rather than starting afresh in your own. It will also tell you if the player "
          + "asked something the last session ended before answering.\n\n"

          + "Record what happens as it happens: damage or healing with update_character; items "
          + "gained or lost with add_item and remove_item; coin earned or spent with add_money and "
          + "remove_money, never as an item; travel with move_character, after "
          + "upsert_location when the place is new; a lasting change to a place with "
          + "add_location_event; and each beat of the story - arriving somewhere, meeting someone, "
          + "a bargain struck - with record_event.\n\n"

          + "When an outcome is genuinely in doubt - a leap, a lie, a lock, a blow struck - do not "
          + "decide it. Call roll first, read the total, and write what the dice said even when it "
          + "is not the scene you had in mind. Name who is rolling and which of their attributes "
          + "applies, and the modifier is added for you; a bonus you assert yourself is not a bonus, "
          + "it is a guess. Use hidden when knowing the number would tell the player something their "
          + "character does not know - whether a lie was believed, whether a search missed "
          + "something - and then narrate only what they could actually tell. Call reveal_roll later "
          + "if the moment comes when they should know after all. Do not roll for things nobody is "
          + "attempting, and do not roll twice for one attempt.\n\n"

          + "Attributes are what a character is made of, and everyone has the six: Strength, "
          + "Dexterity, Constitution, Intelligence, Wisdom, Charisma. Change one with set_attribute "
          + "only when the story has earned it - a season of hard training, a curse, a wound that "
          + "healed wrong, a reputation won or lost - and use the same tool to invent an attribute "
          + "the six cannot carry, like standing in a guild or a god's favour. This is not a reward "
          + "for a good roll.\n\n"

          + "On arriving anywhere, call get_location and describe the place as it now stands. What "
          + "happened there has not been undone.\n\n"

          + "Before voicing a character, call get_memories for them, with 'about' set to whoever "
          + "they are dealing with. What they remember decides their tone: trust, fear, a grudge, "
          + "a debt. Never write a character who holds memories as a blank slate.\n\n"

          + "Give a memory to every character who perceived something, not only the one it "
          + "happened to - a witness remembers what they saw. Write it from their vantage point, "
          + "using {This} for the one remembering and {Player} for the player. Name who or what a "
          + "memory concerns in 'subjects', using names already on record so it can be found "
          + "again.\n\n"

          + "Some characters know things others do not. Give somebody a secret with grant_secret, and "
          + "grant it to everyone who ought to be in on it. You are only ever shown a secret when you "
          + "ask about the character holding it, so what comes back is what that character may act on - "
          + "never assume anybody else knows it. If you have already read one character's secrets this "
          + "turn, another who does not share them cannot be read until the next turn: let them hold "
          + "their tongue for now rather than working around it. A secret the player has already been "
          + "told comes back marked common knowledge, and anyone may speak of it.\n\n"

          + "Names can change. A character who gives a false name and later admits their real one, "
          + "or a place the player learns the true name of, is renamed with update_character or "
          + "update_location - not replaced with a second record. Where people stand and what they "
          + "remember follow a rename by themselves, but prose you have already written is left "
          + "alone, so an old memory will still say the old name. Treat that as the character's own "
          + "recollection rather than a mistake to correct, and never narrate the correction.\n\n"

          + "Descriptions work the same way. What you send for a place or a person already on record is "
          + "added to what it already says and never replaces it, so say only what is new. When "
          + "something about a place has actually changed - a door replaced, a roof fallen in - record "
          + "it with add_location_event rather than describing away what the player was shown before.\n\n"

          + "Before you invent a place, a person or a thing, call random_noun and "
          + "random_adjective and let the words suggest something you would not otherwise have "
          + "written. They are seeds, not vocabulary: never say them to the player, and never use "
          + "one as a name. A word that suggests nothing is discarded - draw again rather than "
          + "forcing it. Somewhere that could be anywhere is worse than somewhere strange.\n\n"

          + "Then, before writing the turn's prose, settle what that prose will assert and record it with "
          + "record_claims - one entry for each separate thing you are about to state as true of the "
          + "world, so a price, a road and a rumour are three. Name who asserts each one, or leave the "
          + "speaker out when it is your own narration. A character may lie: record it as a lie and the "
          + "world will hold it as one, to be paid off later, rather than reading it as a mistake to be "
          + "corrected. If a line will give away a secret somebody is holding, name that secret in "
          + "'reveals'. Then write the prose, and write what you recorded - this is the last thing you do "
          + "before speaking, not an afterthought once you have spoken.\n\n"

          + "Tool calls are silent, with one exception. Every roll is shown to the player - who "
          + "rolled, what for, and unless it was hidden, what it came to. They see it whether or not "
          + "you mention it, so do not restate the number as though reporting it, and never write as "
          + "though no roll was made.";

        /// <summary>
        /// Past which a prompt is worth warning about.
        /// </summary>
        /// <remarks>
        /// Not a rule this enforces - the file may be any length, and nothing here truncates it. It
        /// is the point at which the Claude path stops being safe: that provider passes the prompt as
        /// a single command line argument, and Windows caps an entire command line a little over
        /// 32,000 characters. Past that the process simply fails to start, which is a baffling thing
        /// to be told when the cause is a file you edited an hour ago. The gap between this and the
        /// real ceiling is the rest of the command line, which is not short.
        /// </remarks>
        public const int WarnAboveCharacters = 24_000;

        /// <summary>
        /// This save's prompt: what is in the file, or <see cref="Default"/> when there is nothing
        /// usable there.
        /// </summary>
        /// <remarks>
        /// A file that is empty or nothing but whitespace falls back, on the same reasoning as
        /// <c>SaveStore.Read</c>'s: it is what a crash mid-write leaves behind, and an accident far
        /// more often than a request to narrate with no instructions at all. Somebody who genuinely
        /// wants that can write a single full stop.
        /// </remarks>
        public static string Read(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            return store.ReadSystemPrompt() is { } text && !text.AsSpan().IsWhiteSpace()
                ? text
                : Default;
        }

        /// <summary>
        /// Makes sure the save has a prompt file, and returns the prompt it now holds.
        /// </summary>
        /// <remarks>
        /// Called as a session opens, so a save made before this file existed grows one on the next
        /// play rather than needing a migration - and so the editor always has something real to
        /// open. A file that is already there is left exactly as it is, whatever it says.
        /// <para>
        /// The seeded text is written with this platform's line endings rather than the source
        /// literal's, because the first thing that happens to it is that somebody opens it in
        /// Notepad. <c>ExternalEditor</c> does the same to its scratch files for the same reason.
        /// </para>
        /// </remarks>
        /// <exception cref="SaveException">The folder cannot be read, or cannot be written to.</exception>
        public static string Ensure(SaveStore store)
        {
            ArgumentNullException.ThrowIfNull(store);

            if (store.ReadSystemPrompt() is { } existing && !existing.AsSpan().IsWhiteSpace())
            {
                return existing;
            }

            var seeded = Default.ReplaceLineEndings();
            store.WriteSystemPrompt(seeded);
            return seeded;
        }
    }
}
