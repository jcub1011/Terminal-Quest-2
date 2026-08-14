using System.Text.Json;

using TerminalQuest.Saves;

namespace TerminalQuest.Mcp
{
    /// <summary>
    /// The only way a secret leaves the save layer, and the refusal that stops one turn holding two
    /// differently informed characters.
    /// <para>
    /// This is a mechanism where a prompt would do, and that is the codebase's established argument
    /// rather than a new one. A hidden roll's total never enters the transcript line; the
    /// <c>[roll]</c> markup tag is withheld from the model entirely; the tool allowlist is derived
    /// from the definitions rather than written out beside them. A secret the narrator is never handed
    /// is one it cannot let slip, and no amount of instruction is as strong as that.
    /// </para>
    /// <para>
    /// Enforcement is in two halves, and the split is what makes it structural. The stage filter
    /// decides which of a character's secrets may come out, and lives in one function that is the only
    /// reader of <see cref="Character.Secrets"/> outside the save layer - there is no other way to
    /// obtain a <see cref="Secret"/>. The divergence check decides whether a fetch may be answered at
    /// all, and runs before dispatch, where no handler has had a chance to forget it.
    /// </para>
    /// </summary>
    internal static class SecretGate
    {
        /// <summary>
        /// The tools that can hand a secret over, and the argument each one names its subject with.
        /// </summary>
        /// <remarks>
        /// Data rather than a switch because the suite asserts over it: every name here has to be a
        /// tool that exists, and every argument here has to be one that tool's schema declares and
        /// requires. A knowledge fetch added to <see cref="QuestTools.Definitions"/> and forgotten here
        /// is caught from the other direction, by the sweep that checks no tool at all returns a
        /// dormant secret.
        /// <para>
        /// <c>get_state</c> is absent deliberately. It renders the player through
        /// <see cref="QuestRender.Character"/>, which carries no secrets, so it hands nothing over -
        /// and listing it would have every session's opening call poison the turn's fetch history.
        /// </para>
        /// </remarks>
        public static IReadOnlyDictionary<string, string> KnowledgeFetches { get; } =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["get_character"] = "name",
                ["get_memories"] = "character",
            };

        /// <summary>
        /// The refusal a knowledge fetch has earned, or null when it may be answered.
        /// </summary>
        /// <remarks>
        /// The second handler in this assembly that refuses something the fiction would allow - see
        /// <c>Roll</c>'s remarks for the first, and for the reason such a thing is ever justified. The
        /// resemblance is deliberate: both take a decision away from the model precisely because the
        /// model would otherwise make it.
        /// <para>
        /// Returns immediately for the tools that are not knowledge fetches, so the great majority of
        /// calls pay nothing - no roster read, no journal read. Nothing is refused either when the
        /// fetch named nobody or named nobody on record: a call that is about to fail on its own terms
        /// should fail with its own message, which is more use to the narrator than this one.
        /// </para>
        /// </remarks>
        public static ToolOutcome? Refusal(SaveStore store, string tool, JsonElement arguments)
        {
            if (!KnowledgeFetches.TryGetValue(tool, out var argument)
                || QuestTools.Text(arguments, argument) is not { Length: > 0 } name)
            {
                return null;
            }

            var characters = store.ReadCharacters();

            if (SaveStore.FindCharacter(characters, name) is not { } fetched)
            {
                return null;
            }

            var read = NamesReadThisTurn(store);

            if (SecretDivergence.BlockingHolder(fetched, characters.Characters, read) is not { } blocker)
            {
                return null;
            }

            return ToolOutcome.Fail(
                $"You have already read {blocker}'s secrets this turn, and {fetched.Name} does not "
              + $"share them. Voice {blocker} now and let {fetched.Name} hold their tongue, or let the "
              + $"scene take another turn - {fetched.Name} can be read on the next one.");
        }

        /// <summary>
        /// The characters a knowledge fetch has already been answered for this turn, in the order they
        /// were asked about.
        /// </summary>
        /// <remarks>
        /// Only calls that succeeded count. A refused fetch handed nothing over, and counting one would
        /// make the first refusal of a turn permanent - the narrator would be told to try again next
        /// turn and then refused again for having tried. This is why the journal records an outcome at
        /// all, and why it is written after the handler rather than before it.
        /// </remarks>
        public static List<string> NamesReadThisTurn(SaveStore store)
        {
            var names = new List<string>();

            foreach (var entry in store.Journal.ForTurn(store.CurrentTurn()))
            {
                if (entry.Failed
                    || !KnowledgeFetches.TryGetValue(entry.Tool, out var argument)
                    || QuestTools.Text(entry.Arguments, argument) is not { Length: > 0 } name)
                {
                    continue;
                }

                names.Add(name);
            }

            return names;
        }

        /// <summary>
        /// The secrets a fetch naming <paramref name="fetched"/> may be told, split by whose they are.
        /// The <b>only</b> reader of <see cref="Character.Secrets"/> outside the save layer.
        /// </summary>
        /// <remarks>
        /// Unconditional, taking no journal and no turn: the stage filter is not a judgement about the
        /// moment, which is the divergence rule's job. Dormant secrets are not filtered out of an
        /// assembled context here - nothing ever yields one, which is a stronger guarantee than
        /// filtering could be.
        /// <para>
        /// The shared half sweeps the whole roster rather than only the character being fetched, and
        /// that is a reading worth defending. "Spent is returned to anyone" only means something if a
        /// fetch of somebody who was never told still says the secret is out; otherwise the narrator
        /// voicing them goes on protecting something the player already knows, and the pacing cost of a
        /// live secret outlives the secret itself.
        /// </para>
        /// </remarks>
        /// <returns>
        /// What this character is holding, and what the player has already been told by anyone.
        /// </returns>
        public static (IReadOnlyList<Secret> Held, IReadOnlyList<Secret> Common) Release(
            Character fetched,
            IReadOnlyList<Character> roster)
        {
            ArgumentNullException.ThrowIfNull(fetched);
            ArgumentNullException.ThrowIfNull(roster);

            var held = Secrets.AtStage(fetched, SecretStage.Live).ToList();

            // By name, so that one secret several people were in on is reported once rather than once
            // per holder.
            var common = new List<Secret>();

            foreach (var character in roster)
            {
                foreach (var secret in Secrets.AtStage(character, SecretStage.Spent))
                {
                    if (!common.Exists(seen => SaveStore.Matches(seen.Name, secret.Name)))
                    {
                        common.Add(secret);
                    }
                }
            }

            return (held, common);
        }
    }
}
