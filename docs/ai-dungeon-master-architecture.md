# AI Dungeon Master Architecture

**Status:** Phases 0 and 1 implemented; the Director (Phase 2) is still a proposal — see §2
**Last updated:** 2026-08-14 (rev. 6)

> **Revision history**
>
> **rev. 6** — First revision written *after* implementing what it describes. Phases 0 and 1 are built, so §2 records them and §10 marks them done. Six of rev. 5's decisions did not survive contact with the code and are corrected in place, each with its reason: the journal must cover **every** tool call rather than only mutating ones, because rev. 5's own divergence rule is otherwise not computable (§6); a journal entry needs an **outcome flag** nobody had listed (§6); the schema bump to 3 turned out to be **unnecessary** and is withdrawn (§10); **three of §9's four assertions** turn out not to be writable as stated — two are not expressible over a log at all and are replaced by stronger or differently-placed checks, and the third needs a Director to make the judgement (§9); the ledger needed an **append-only way to record a later finding**, which rev. 5 left implicit while asking for both (§6, §9); and the claim that `TerminalQuest.Tests` is scaffold-only was already **stale** when rev. 5 said it (§9). One thing rev. 5 did not decide at all — who *creates* a secret in Phase 1 — is decided here (§6). A pre-existing turn-numbering bug that Phase 1 would have turned into a visible one is recorded in §2.
>
> One further correction came from *playing* rather than reading, and is the most interesting of the set: `record_claims` was specified as a tool "called each turn", which placed it after the prose — and for an agent-loop provider the final text is the end of the turn, so it never fired at all. Claims are now recorded before the prose (§6). The general form of that mistake is worth carrying into Phase 2: an instruction whose trigger is "the turn is over" cannot be given to the thing whose turn it is.
>
> **rev. 2** — "Character Instance" renamed to **Narrator Instance** to reflect that it owns all player-facing prose. Added fact-authority policy (§5), split state write ownership between instances (§6), made directives version-stamped and expirable (§8).
>
> **rev. 3** — Director trigger policy specified (§7). Secret knowledge is per-NPC, forcing a call-splitting rule (§6). Contradiction became an explicit non-goal with structural guarantees, including the **revealed ledger** (§6, §9).
>
> **rev. 4** — Secrets get a **dormant/live/spent lifecycle** that also determines what enters Narrator context (§6). Ledger entries are **per-assertion**, emitted as structured output alongside prose (§6, §9). NPC dialogue is binding but inert until the Director **ratifies** it into canon (§9). All open design questions are now closed; §11 is a decisions record.
>
> **rev. 5** — First revision written against the code rather than beside it. Revisions 1–4 were authored without reference to the implementation, and three of their load-bearing assumptions do not hold here: the Narrator is the world's only author by design, context is **pulled** by the model through tools rather than assembled and pushed, and there is no structured channel alongside the prose. §5 is therefore **inverted** — the Narrator invents and the Director audits, which deletes deflection prompting, blocking fact requests and most of §4's latency problem. Call splitting is replaced by **fetch-boundary gating** (§6). The claims list becomes a tool. The append-only journal, not a counter threaded through document writes, is the version (§6). §2 now records what is already built so the document stops proposing it, and §11 records the disposition of every rev. 4 decision.
>
> Section numbers are unchanged from rev. 4 on purpose: the revision history and the decisions record both reference them, and renumbering to make room for §2's new material would have silently broken every one of those references.

## 1. Overview

The AI Dungeon Master is split into two logical roles, run as **separate instances of the same underlying model**, rather than one model doing both jobs at once.

- **Narrator Instance** — produces everything the player reads: NPC dialogue, environmental description, action and outcome narration. Shipped: it is the `IAgentSession` built by `Agents/AgentSessionFactory.cs:23`.
- **Director Instance** — manages story pacing: introduces twists, escalates or de-escalates tension, tracks the campaign at a plot level, and ratifies claims into canon. Not built.

Both instances are the same model. The split is achieved entirely through **different system prompts and different slices of game state**, not different models or fine-tunes.

That split is unusually cheap to reach from where the code already stands, and the reason is worth stating early because it shapes everything downstream: the two instances do not need an inter-process channel invented for them. The world already lives in a folder of JSON documents that two processes read and write concurrently — the game and the narrator's tool server (`Saves/SaveStore.cs:9-15`) — so §6's requirement that the state store be the only coordination channel is not a constraint to impose. It is a description of what is already true.

## 2. Where the code already stands

Revisions 1–4 proposed a number of things that exist. This section is here so that rev. 5 stops arguing for them, and so that anyone reading the document can tell design intent from shipped behaviour.

**Built, and matching the design:**

| Mechanism | Where | Note |
|---|---|---|
| Deterministic rules resolver, embedded beside the state store | `Saves/Dice.cs:81` (`TryRoll`), wired at `Mcp/QuestTools.cs:803` | §3's decision, shipped. Pure; `Random` arrives as a parameter, so a seeded replay is a call-site change |
| Mechanical outcomes withheld from the model | `Mcp/QuestTools.cs:793-802`, `:1039` | The `roll` handler is described in the source as "the one handler that refuses things the fiction would allow", and refuses a flat bonus alongside a named attribute so the model cannot supply its own modifier |
| Structured state store as the only coordination channel | `Saves/SaveStore.cs:22` | Six documents per save folder; nothing cached, because "the file on disk is the only authority, and this process may not be the one that last changed it" |
| Per-character knowledge | `Saves/Memory.cs`, retrieved by `get_memories(character, about)` | Turn-stamped, subject-indexed by id so retrieval survives a rename. The substrate secrets need |
| Append-only history | `Location.Events`, `StoryFile.Events`, `RollFile.Rolls`, `Character.Memories` | All turn-stamped, all oldest-first, none rewritten |
| A turn clock, already on disk before the turn runs | `SaveMetadata.Turn`, stamped at `Program.cs:435` | Stamped early precisely so the out-of-process tool server can date what it writes |
| Enforcement in the mechanism rather than the prompt | see below | The house rule §6 depends on |

That last row is the most important thing in this section, because the document's central claim — that correctness must be structural and not prompted — is not a new idea being introduced here. It is already the established convention, in three places:

- A hidden roll's total **does not enter the line**. `Ui/RollWatcher.cs:167-171` returns before appending it: *"Not blanked and not masked — the number does not enter the line. That is where hiding is actually enforced; what the prompt asks of the narrator is only manners."*
- The `[roll]` and `[command]` markup tags are withheld from the model entirely (`Ui/MarkupParser.cs:169-180`), because *"giving the narrator a `[roll]` tag would let it type a roll line — which means inventing a number, or spelling out one it was asked to keep quiet."*
- Tools are gated per session by the CLI, not by instruction: both `--tools` and `--allowed-tools` are passed (`Agents/Claude/ClaudeSession.cs:278-289`), and the allowlist is derived from the tool definitions so a tool cannot be added and silently left unavailable (`Mcp/QuestTools.cs:314`).

**Built in Phases 0 and 1 (rev. 6):**

| Mechanism | Where | Note |
|---|---|---|
| The append-only journal | `Saves/AppendLog.cs`, `Saves/JournalEntry.cs`, hooked at `Mcp/QuestJournal.cs` and `QuestTools.Invoke` | One line per tool call. The sequence is allocated inside an exclusive file handle, so it is collision-free across the two processes |
| A line-oriented JSON context | `Saves/LogJsonContext.cs` | Separate from `SaveJsonContext` because that one indents deliberately, and an indented entry is not a formatting preference but a corrupt log |
| Secrets, with a dormant/live/spent lifecycle | `Saves/Secret.cs`, `Saves/SecretStage.cs`, on `Character.Secrets` | `Dormant` is zero, so a hand-written secret that forgets its stage fails closed |
| The divergence rule, as a pure predicate | `Saves/SecretDivergence.cs` | No store, no files, no clock. Tested without a save folder |
| The fetch-boundary gate | `Mcp/SecretGate.cs`, refusing in `QuestTools.Invoke` before dispatch | The second handler in the codebase that refuses what the fiction would allow; `roll` was the first |
| `grant_secret` | `Mcp/QuestTools.cs` | §6's open question, decided — see below |
| `record_claims` and the revealed ledger | `Mcp/QuestTools.cs`, `Saves/LedgerEntry.cs`, `Saves/ClaimTruth.cs` | Per-assertion. Called *before* the prose, for the reason in §6. Ledger written first, secrets spent second, deliberately |
| Spent-derivation | `record_claims`, via `Secrets.Spend` | Every live holder, not only the speaker — see §6 |
| Player claims recorded by the game | `Program.cs`, in `OnCommandEntered` | Free: the game already holds the typed line |
| The missing-claims report | `Program.cs`, beside the failed-turn report | The only place prose and the journal are both in hand |
| Extend-not-replace descriptions | `Saves/Descriptions.cs`, four call sites in `QuestTools` | Fixed two latent bugs in passing: both `update_*` handlers blanked the field when handed an empty value |
| The contradiction batch test | `TerminalQuest.Tests/Consistency/ContradictionBatchTests.cs` | Plus the tool-surface sweep in `SecretGateTests` — see §9 on why one of these is not the other |

**Not built:** the Director, directives, pacing state, plot state, and ratification. Everything in §7 and §8 remains a proposal.

**Built, but contradicting rev. 4:** the Narrator authors the world. `Mcp/QuestTools.cs:9-15` states the posture outright — *"The model is trusted here. It decides what happens in the story, so it decides what gets written."* §5 is where that is resolved.

**A bug Phase 1 turned up, and fixed.** `Program.cs` reads `state.Turn` from the save on load and then ran the opening or resuming turn *without* incrementing it, so the first turn of a resumed session reused the previous session's final turn number. Memories and events written during it were misdated, which was already wrong and invisible. Phase 1 would have made it visible in a worse way: the divergence rule asks what has been read "this turn", and would have counted the previous sitting's knowledge fetches as belonging to this one — refusing the opening scene of a resumed save. `OpenAsync` now stamps the turn the way `OnCommandEntered` does. Worth recording because it is the second time the turn clock has needed to be *earlier* than felt natural, and the reason is the same both times: the out-of-process tool server can only learn the turn by reading it off disk.

## 3. Motivation

Running both jobs in a single model and context leads to two recurring problems:

1. **Mismatched cadence** — Player-facing prose is needed every turn. Story pacing decisions don't need to be re-evaluated that often, and doing so wastes calls and risks over-intervening. A human DM does not replan the campaign after every line of dialogue.
2. **Poor debuggability** — With one context doing both, there's no artifact to inspect when the story goes sideways. With the split, you can read what the Director decided independently of how it got dramatized, which localizes most bugs to one side of the seam or the other.

**A note on leakage.** An earlier version of this document justified the split primarily as leakage prevention — a model that knows an upcoming twist while voicing an NPC tends to let that knowledge bleed into dialogue. This is a real failure mode, but the split is not what fixes it. What fixes it is the **per-NPC knowledge partitioning and lifecycle gating** in §6. That partitioning is required regardless of architecture, and is needed in Phase 1 before any Director exists. The instance split makes it easier to enforce; it does not substitute for it.

The leakage argument was in fact weaker at rev. 5 than rev. 3 assumed, and honesty about why is useful. Before Phase 1 the Narrator could read everything: `get_memories` answered for any character named, and `get_character` advertised itself as returning a character *"in full, including everything they know"*. There was no partition to leak across at all, so there was nothing for the instance split to have prevented.

**Rev. 6:** that sentence is retired, and a test asserts no tool description ever promises it again — it is exactly the kind of phrase a later edit reintroduces without noticing. `get_character` now says that what comes back is what the character may act on, which is not everything on record about them.

## 4. Responsibilities

### Narrator Instance

- Voices all active NPCs in the current scene
- Narrates the environment, scene transitions, and the outcome of player actions
- Renders mechanical results supplied by the rules resolver into prose (it does **not** decide those results)
- Maintains NPC personality and relationship consistency
- Consumes any pending directive from the Director before generating a response
- Decides **how** things are dramatized, and — unlike rev. 4 — also **what** exists, subject to §5's write-once discipline
- Emits a **structured claims list** of every assertion made to the player this turn, as a tool call (§6)
- Writes scene state, NPC state, and the revealed ledger after each turn (§6)

### Director Instance

- Tracks the campaign at a plot/pacing level, not line-by-line dialogue
- Decides when to introduce twists, escalate tension, or nudge the story forward
- **Audits** what the Narrator invented, promoting the useful and constraining the rest (§5)
- Manages secret lifecycle transitions and names new secrets (§6)
- Ratifies unratified ledger claims into canon (§9)
- Emits **structured directives** for the Narrator to act on — not narrative prose
- Never writes player-facing text

### Non-AI components

- **Deterministic rules/dice resolver** for all mechanical outcomes. Shipped as `Saves/Dice.cs`, embedded beside the state store rather than standing up a separate service — extract it later if a second client needs it.
- **Rules-based pacing tracker** (turn counters, fixed triggers) as the Phase 1 stand-in for the Director — see §10.
- **Summarizer** producing rolling scene summaries for the Director's context — see §7.
- **Divergence resolver** — a pure function over NPC secret sets and the current turn's journal, deciding whether a knowledge fetch may be answered (§6). Deliberately not a model call.
- **Journal** — the append-only event log that doubles as the version counter (§6).

## 5. Fact Authority

**Decision (rev. 5): inverted.** Revisions 2–4 held that the Narrator may not invent world-state facts — that a probe into something nobody specced should be deflected in character and filed as a fact request for the Director to answer. That policy cannot be adopted here, and the reason is not squeamishness about strictness; it is that the premise is absent.

A Terminal Quest save begins with a player character, the starting kit their class dealt them, and — only if the player named one — a location to stand in. `Saves/NewGame.cs:17` writes that and nothing else: no NPCs, no places beyond the first, no factions, no plot. There is no authored world to defer to. Under rev. 4's rule the very first turn would be a deflection, every probe thereafter would be a fact request, and the "small fraction" of blocking requests that §5 budgeted for would be nearly all of them. The document was describing a content-driven game; this is an improvising one, and the improvisation is the product.

So the direction of the guarantee reverses. The Narrator invents; the Director audits.

**What is kept.** The goal of rev. 4's §5 was never deflection for its own sake — it was that every fact has exactly one author, which is what §9 is built on. That survives intact, because in an improvising game single authorship is achieved at the moment of invention rather than before it:

1. **The Narrator invents freely**, as it does today. No deflection patterns, no in-character evasion, no prompt budget spent on either.
2. **What it invents becomes an unratified claim** (§9), binding but inert: the world will never contradict it, and nothing else may be built on it until the Director ratifies.
3. **Canon is extended, never negated.** Once written, a fact may gain detail. It may not be reversed — by either instance.

**What this deletes.** Following the inversion through removes more than it adds, and the removals are worth naming because rev. 4 had already conceded their cost:

- Deflection quality was to be "a first-class prompt-engineering concern" with "real effort" budgeted. Gone.
- Blocking fact requests, and with them the synchronous Director call on the critical path. Gone.
- Most of rev. 4's §4 latency affordance. What remains is already built: `window.IsBusy` and `NarrationView.IsWaiting` show the narrator thinking, and every roll it makes appears mid-turn through the 400 ms poll at `Program.cs:120`, so a long turn reads as deliberation rather than a hang.
- The fact-request queue itself, which inverts into the unratified claim queue. One queue, one direction, and the Director reads it on its normal cycle rather than being woken by it.

**Where the real exposure is.** Inverting §5 does not make contradiction impossible; it relocates it. Append-only structures cannot contradict themselves, so memories, location events, story events and rolls are safe by construction. The exposure is the two mutable prose fields — `Character.Description` and `Location.Description` — which `upsert_location` will overwrite on request, and whose tool description invites exactly that: *"Create a place or rewrite its description"* (`Mcp/QuestTools.cs:177`, handler at `:1142-1145`).

That is the contradiction this game can actually produce: a description rewritten to negate what the player was already told. Two changes follow, and they are small.

- A description is **extended, not replaced**, when the place or person is already on record. The tool's own wording has to change with it; a tool that advertises rewriting will be used to rewrite.
- Every description write is journalled (§6), so a negation that slips through is detectable after the fact by the batch test in §9 rather than only by a player noticing.

This is deliberately the weaker of the two available policies, and the trade is stated rather than hidden: rev. 4's rule would have prevented contradictions the Narrator can now still commit, at the price of a game that could not start.

**Rev. 6: built, and the surface was worse than this described.** `Descriptions.Extend` now governs all four call sites, the tool wording says "added to what it already says and never replaces it", and the system prompt points at `add_location_event` for a change that actually happened in the fiction. Two latent bugs turned up while doing it: both `update_character` and `update_location` assigned the new value *even when it was empty*, so a description could be blanked outright by a call that looked like a no-op. The extend rule fixes that as a side effect of refusing to replace anything.

Two consequences are accepted rather than solved, and both are cheap to revisit. A description now grows monotonically and cannot be corrected from inside the fiction, so there is a length ceiling whose refusal names `add_location_event` — the *in-fiction* answer, since "the oak door has been replaced with iron" is a lasting change to a place rather than a description edit. And genuine mistakes are repaired by hand-editing the save, which is already the adjudication path for secrets. A `replace` flag was rejected for the reason the Narrator is not given a `[roll]` tag: a permission the model can grant itself is not a guarantee. The right long-term answer is a `rewrite_description` tool withheld from the Narrator's allowlist, which costs nothing to defer to Phase 2's role-tagged slices.

## 6. Shared World-State

Both instances read from and write to a **single, structured source of truth** — the save folder. Chat history is not a coordination channel, and here it cannot be: for the Claude provider the Narrator's tool calls run in a different process entirely (`Program.cs:130-132`), so neither instance can infer anything from the other's conversation even in principle.

### Categories

- **Scene state** — current location, characters present, recent events. Shipped: `LocationFile`, `Location.CharacterIds`, `Location.Events`
- **NPC state** — personality profile, relationship to player, and the secrets *that specific NPC* holds. Partly shipped: `Character` + `Character.Memories`; secrets are new
- **Plot state** — active quest threads, twists used, ratified canonical facts, secrets and their lifecycle stage. New
- **Pacing state** — turns since last major beat, tension level, pending directive, unratified claim queue. New
- **Revealed ledger** — per-assertion record of what has been said to the player, and by whom (§9). New
- **Journal** — every mutating tool call, in order, numbered. New; see *Versioning* below

### Write ownership

| State | Narrator writes | Director writes |
|---|---|---|
| Scene state | Yes | Rarely (scene-level directives) |
| NPC state | Yes (relationships, what's been revealed) | Yes (granting and naming secrets) |
| Plot state | No | Yes (append-only, see §9) |
| Pacing state | Claims queue only | Yes |
| Revealed ledger | Yes (append-only) | No (read-only) |
| Journal | Written by the tool layer, not by either instance | — |

The Narrator must be able to write scene and NPC state, because play *happens* in the Narrator. The real boundary is not read/write — it is that **plot and pacing belong to the Director, scene and NPC belong to the Narrator.**

Enforcement is by tool slice, not by instruction. `QuestTools.AllowedTools()` already derives the CLI allowlist from the tool definitions (`Mcp/QuestTools.cs:314`); giving each definition a role and deriving two allowlists from it means the boundary is enforced by the process launch. The Director is never offered `roll` or the claims tool; the Narrator is never offered ratification or lifecycle promotion. A tool the model cannot see is one it cannot misuse, which is the same argument `Ui/MarkupParser.cs:169-180` makes about the `[roll]` tag.

### Secret lifecycle

Every secret carries a stage, and the stage determines whether it can leave the save layer at all:

| Stage | Returned by a knowledge fetch? | Set by |
|---|---|---|
| **Dormant** | No — never, to anyone | Director (default on creation, from Phase 2) |
| **Live** | Only to a fetch naming a holder (see the divergence rule) | Director, via directive |
| **Spent** | Yes, to anyone | Derived automatically from the ledger |

**Dormant** does most of the leak prevention: a secret the Director has not activated is not returned by any tool, so it cannot leak by any mechanism, prompt failure included. An NPC who "knows" a dormant secret behaves as though unaware — which is the desired behaviour, since it is not plot-relevant yet.

**Live** means the Director has activated it for the current stretch of play. Only live secrets participate in divergence detection, which keeps the cost proportional to how many secrets are *currently in play* rather than how many exist in the campaign.

**Spent** is derived: when a secret appears in the revealed ledger it transitions automatically, keeping a Director round-trip off the critical path.

**Whose copy transitions, which the table left ambiguous (rev. 6).** "Returned to anyone" says what a spent secret does, not which holder's record changes when a claim reveals it. **Decision: every live holder's.** Spent means *the player knows*, which is a fact about the player rather than about whoever happened to voice it; leaving a second holder's copy live would keep the divergence gate refusing fetches over something that is already out, so the pacing cost of a secret would outlive the secret. The reciprocal reading is also implemented: a fetch of *anybody* returns every spent secret in the save, marked as common knowledge, because otherwise a character who was never told goes on protecting a thing the player heard two scenes ago.

**Secrets need names, and the reason is a house rule rather than a preference.** Ids never leave the save layer (`Saves/EntityIds.cs`), so neither the ledger nor a directive can refer to a secret by id — the Narrator would have no handle to use and no way to report which secret a line revealed. A secret therefore gets a short name when it is created, the way everything else the model talks about does, and translation runs through the existing name↔id seam in `Saves/WorldIndex.cs`. "The innkeeper's brother", "the sealed cellar". The Director names them; the Narrator says the name back in a claim's `reveals` field, which is what drives the spent transition.

**Phase 1 has no Director, so dormant cannot be the default yet.** A secret created dormant with nothing able to wake it is invisible for the rest of the campaign, which is worse than no secret at all. Until the Director exists, a secret is created **live for its holder**, and the human adjudicates by hand-editing the save — which the file format was built for, and which `Saves/EntityIds.cs` cites as the reason ids are short and prefixed rather than GUIDs. Dormant becomes the default in Phase 2, when there is something to promote it.

**Who creates one, which rev. 5 never said (rev. 6 decision).** The write-ownership table above assigns "granting and naming secrets" to the Director, and Phase 1 has no Director — so read literally, no secret could come into existence during play at all, and the gate, the divergence rule and spent-derivation would be machinery that only a hand-edited save could ever exercise. **Decision: the Narrator gets `grant_secret` in Phase 1**, and it moves to the Director's tool slice in Phase 2. This follows §5's inversion rather than fighting it: the Narrator already invents everybody in the world, so inventing that one of them is keeping something back is the same act. What the Narrator is *not* trusted with is the consequence — it cannot promote a stage, cannot read a secret it was not handed, and cannot un-say one, because those are mechanisms rather than instructions.

Note the asymmetry that makes this safe, because it is the same one `roll` relies on: the Narrator decides *that* there is a secret, and the gate decides *who may be told*. Granting is unrestricted; reading is not.

**A secret's name is a global handle, and uniqueness is not enforced.** Two characters holding a secret of the same name hold, for every purpose in the code, the same secret — which is how several people are in on one thing, and is usually what was meant. The costs are real and accepted: a name is not an identity, so renaming one by hand orphans every ledger entry that named it, and there is no site at which a collision could sensibly be refused. `grant_secret` does refuse giving the *same* character two secrets of one name, because that would be an overwrite, and an overwrite is the negation §5 forbids.

### Knowledge partitioning, and why call splitting is replaced

Secret knowledge is tracked **per NPC**, not per faction. Faction membership may *seed* an NPC's secret set, but the authoritative record is per-NPC, because "who specifically knows this" is exactly the fact that determines whether a scene works.

Revisions 3 and 4 drew a consequence from this: if one Narrator call voices several NPCs, every secret in that call's context is available to all of them, so a scene whose NPCs differ on any live secret must be **split into one call per NPC**. That reasoning is sound and its conclusion does not transfer, for two reasons.

First, there is nothing to split. Context here is **pulled, not pushed**: the Narrator holds only what it asked for, through `get_character` and `get_memories`, and there is no assembly step where a divergence resolver could hand it a filtered slice. Second, a split would be ruinous. One narrator session is one long-lived `claude` process held open for the whole conversation precisely so the prompt cache survives across turns (`Agents/Claude/ClaudeSession.cs:12-14`); a process per NPC means a cold cache each, and a single streaming transcript that cannot carry two speakers at once.

**The lifecycle gate at the fetch boundary replaces it**, and for dormant secrets it is strictly stronger than rev. 4's rule — a dormant secret is not filtered out of an assembled context, it is never returned by anything. What the gate does not cover is **intra-turn accumulation**: once the session has asked about Bess and then about Tam, it holds Bess's live secrets while voicing Tam, and cannot be un-told.

So the divergence rule survives, with a different verb. Evaluated per fetch:

- Assemble the **live** secret set of the character named. Dormant secrets are excluded; spent secrets are shared and never trigger anything.
- If a live secret has already been handed over this turn for a holder, and this fetch names a character who does not hold it, **refuse the fetch** — with a message saying who has already been read this turn and that the other may be voiced on the next one.

The turn becomes the unit of speaking order that rev. 4 achieved by fixing an order across split calls, and the cost of splitting disappears entirely. A refusal of this shape is not a new idea in the codebase either: it is what `roll` already does when the fiction would allow something the mechanism must not (`Mcp/QuestTools.cs:793-802`).

Two properties are worth keeping hold of:

- **It is computed from state, not decided by a prompt.** The model is never asked to hold a secret it can see.
- **It needs no new state.** "Which live secrets were handed over this turn" is a pure function over the journal and the current turn number, so the rule is unit-testable in isolation — which was rev. 4's stated reason for wanting it pure in the first place.

The trade, stated plainly: a scene with two differently-informed NPCs can only have one of them speak knowingly per turn. In practice that reads as a scene taking two turns instead of one, which is a pacing cost rather than a correctness one, and cheaper than either leaking or splitting.

### Revealed ledger granularity

Entries are **per-assertion**. Rev. 4 had the Narrator emit a claims list as structured output alongside its prose in the same call; that channel does not exist. Prose reaches the game as a stream of text deltas with inline markup (`Agents/IAgentSession.cs`, `Ui/MarkupParser.cs`), and the only structured channel the Narrator has is a tool call.

**Decision: the claims list is a tool.** `record_claims`, called each turn, each entry carrying the claim, the speaker, the turn, a truth-status flag, and the name of any secret it reveals. Two refinements make it cheaper than rev. 4's version:

- **The game records the player's claims itself.** Rev. 4 wanted player assertions in the ledger too, so that an NPC can remember being deceived and a lie can be paid off later. The game already holds the player's literal typed line (`Program.cs:441`) and can append it as a player-speaker entry with no model cooperation at all — which is both free and more reliable than asking for it.
- **The hard error has a home.** A turn that produced prose and no claims call is a hard error in development, because an unextracted claim is invisible to consistency checking. `Program.cs:548-551` already writes a failed turn into the transcript as `TextRole.Danger`; this reports the same way.

Per-scene entries were considered and rejected in rev. 3: they are too lossy for the consistency test in §9 to check anything meaningful. Per-turn summaries were also rejected, because they still require the Narrator to extract assertions from its own output — and if extraction is happening anyway, coarsening it discards precision for no saving. Both judgements stand. A second extraction pass over the finished prose was considered here and rejected in turn: it costs a model call per turn to recover information the Narrator had for free while writing.

The cost of the tool version is honest and should be measured rather than argued about: one extra tool round-trip per narrated turn.

**That cost was the wrong thing to worry about (rev. 6, found by playing).** The round-trip is cheap; the *ordering* is not. Rev. 4 had the claims list emitted alongside the prose, and rev. 5 replaced it with a tool "called each turn" without noticing that it had thereby placed a tool call **after** the prose — and for an agent-loop provider the final text *is* the end of the turn. Once the narrator starts narrating it stops calling tools, so the one instruction whose trigger was "you have finished" fired at exactly the moment nothing more would be called. The first two turns of the first real playthrough recorded thirteen tool calls between them and not one `record_claims`.

Worth noting what this was *not*, because both were the obvious suspects and both were wrong: the turn clock was correct, and the instruction was being read. The narrator followed every other tool instruction in the prompt attentively — `get_state`, the word seeds, `upsert_location`, `move_character`, `record_event` — because each of those is tied to something happening *while* it works. This one was tied to being done.

**Decision: claims are recorded before the prose, not after it.** "Settle what this turn will assert, record it, then write it, and write what you recorded." That fits the loop the model actually runs, and it is how the two neighbouring tools already behave — `record_event` and `add_memory` are called around narration rather than after it, and the prompt's own framing has always been "record what happens as it happens". The three per-turn opening prompts name the tool too, since the first turn follows their short script rather than the system prompt's general advice.

The trade, stated: the ledger now holds what the narrator was *about to* assert rather than a reading of what it finally wrote. Drift is possible and should be small, since the prose follows immediately in the same turn and the instruction binds the two. If it turns out not to be small, the fix is not to move the call back — it is a second extraction pass, which §6 already rejected on cost and would now be reconsidered on these grounds instead.

The general lesson, since it will apply to the Director's tools too: **an instruction whose trigger is "the turn is over" cannot be carried out by the thing whose turn it is.** Anything that must happen after the prose belongs to the game or to the Director, not to the Narrator.

**An append-only ledger cannot restate an entry, and rev. 5 asked for both (rev. 6 correction).** §6's write-ownership table calls the ledger "Narrator writes (append-only)", while §9 requires a truth status *resolved against canon* — a judgement that can only be made later, by something that is not the Narrator. Those cannot both be satisfied by editing a line. **Resolution: the recorded status is the speaker's stance at the time, and any later finding arrives as a new entry naming the earlier one's sequence** (`LedgerEntry.Adjudicates`). This is §5's "extend, never negate" applied to the log that records canon, which is where it should have been applied first. Writing it down matters even though nothing produces such an entry in Phase 1: a reader who found no way to record a finding would reach for editing a line instead, and quietly destroy the one property the log has.

The status is therefore a small enumeration rather than a flag, and it distinguishes three stances a speaker can take — **true**, **lie** (they knew better), **mistaken** (they believed it and were wrong) — from **unverified**, which is what the player's own lines are, and **contradiction**, which is never a stance but a finding. `mistaken` is the case rev. 4 and rev. 5 both missed: an honestly wrong NPC is neither a liar nor a bug, is correctable in the fiction, and must not be reported as a consistency failure.

### Versioning

Rev. 4 called for an append-only store where every write produces a monotonic version number, buying directive staleness checks (§8) and a replayable campaign (§9).

The store is not append-only. `SaveStore.Write` replaces whole documents through one generic method (`Saves/SaveStore.cs:331`), and threading a counter through it recurses, because `save.json` — where such a counter would naturally live — is written by that same method. Rewriting the store into an event-sourced one is a large change and buys nothing else.

**Decision: the journal is the version.** Every tool call appends one line to `journal.jsonl` carrying its sequence number, the turn, the tool, its arguments, and whether it succeeded. That sequence number *is* the version a directive stamps itself against. One mechanism satisfies all three of rev. 4's asks — a monotonic version, staleness detection, and a replayable log — without touching `SaveStore`'s document handling.

**Every call, not every mutating call (rev. 6 correction).** Rev. 5 wrote "every mutating tool call" here and, two subsections down, defined the divergence rule as a pure function over the journal asking *which live secrets were handed over this turn*. Those two sentences contradict each other: handing a secret over happens in `get_character` and `get_memories`, which mutate nothing, so under the narrower rule the log would not contain the thing the rule reads. Journalling everything resolves it, and pays for itself twice over — it removes the need for a read-or-write flag on all twenty-odd tool definitions, and it records the narrator reaching for a tool that does not exist, which is the single most useful line in the file when working out why a turn went wrong. The cost is a larger log: a turn is five to fifteen calls, so a long campaign runs to thousands of lines rather than hundreds.

**And an outcome flag, which nothing had listed.** Neither §6 nor §10 mentioned recording whether a call worked, and the divergence rule cannot do without it: a *refused* fetch handed nothing over, so counting it would make the first refusal of a turn permanent — the narrator is told to try again next turn, and is then refused for having tried. This is also why the line is written **after** the handler rather than before it.

State the reduction rather than gloss it: documents remain last-write-wins, so the store cannot *prevent* a negating overwrite the way a true append-only store would. What the journal buys is that it cannot happen *undetectably*, which is what §9's batch test needs. `jsonl` rather than a JSON document because it must be appendable without a read-modify-write, which is the one thing `SaveStore`'s temp-file-plus-rename pattern is not.

## 7. Coordination Model

**Pattern: asynchronous overseer.** The Narrator runs every player turn. The Director wakes on triggers only, sees the completed turn, and queues a directive for the *next* turn rather than re-evaluating continuously. This is cheaper, matches real DM behaviour, and keeps the Director off the critical path.

It also fits the existing turn loop without restructuring it. A turn already runs on a fire-and-forget `Task.Run` with a catch-all handler (`Program.cs:441`, and the reasoning at `:474-478`), and a background poll already runs alongside it for the duration of the turn (`WatchRollsAsync`, `:594`). A Director wake is the same shape, with one difference to get right: it must be scoped to the session lifetime rather than the turn's, because it runs *after* the turn it is reacting to has finished.

The alternative — turn-based gating, where the Director always runs first and the Narrator waits — is simpler to reason about but pays Director latency every turn and re-plans far more often than necessary. Not recommended, though the state seam is identical, so switching later is cheap.

### Trigger policy

Event-driven, with a turn ceiling as a backstop. Events are the real signal; the ceiling exists so the Director can never go dark during a long stretch of unremarkable play.

**Event triggers** (initial set — expect to tune):

- Player enters a new location, or a scene boundary is crossed
- Combat or another resolver-driven set piece ends
- A twist's trigger condition is satisfied
- Player idles or repeats similar actions past a threshold

Rev. 4 listed a fifth — a blocking fact request, firing a synchronous call. §5's inversion removes it, and with it the only trigger that was on the critical path.

**Backstop:** if no event has fired within **8 turns**, wake the Director anyway. A flat count, not scaled by scene type — scene-type scaling adds a classification problem in exchange for tuning precision that only real play can inform. Frequent ceiling firing is a diagnostic that the event set is missing something; treat it as a signal, not a load-bearing trigger.

Both paths record *which* trigger fired alongside the resulting directive. That field is what makes over-intervention debuggable later.

### Director context

Rolling summary, not full transcript. Rev. 4 gave two reasons — cost, and that a full transcript pushes the Director toward the omniscience the partitioning exists to avoid. There is a third here, and it is the concrete one: the LM Studio provider keeps its whole transcript in memory and resends it in full on every request, with no trimming or summarization (`Agents/LmStudio/LmStudioSession.cs:47`, resent at `:527`). A Director on that provider handed the transcript would grow without bound within a session.

Cheapest implementation: have the Narrator emit a short scene summary at scene boundaries, since it already has the material. The Director additionally reads the relevant slice of the revealed ledger before authoring or ratifying any fact (§9).

**Cost.** Two long-lived sessions means two prompt caches, and the Narrator's default model is `claude-haiku-4-5` (`Settings/AppSettings.cs:53`). The Director should get its own model setting alongside it in `Settings/ClaudeModels.cs`, defaulting no dearer than the Narrator's; `GameState.CostUsd` already accumulates the bill and the status pane already shows it, so the effect of getting this wrong will be visible immediately rather than at the end of the month.

## 8. Directive Format

The Director emits **structured decisions**, not prose. The Narrator interprets *what* should happen and decides *how* it's dramatized. Blurring this line is the single most common source of contradiction bugs.

A directive carries:

- **Target journal sequence** — the version it was generated against (§6)
- **Triggering event** — which trigger woke the Director (§7)
- **Expiry condition** — turn count, scene boundary, or event after which the directive is void
- **Tone/tension** target for the scene
- **Secret grants** — which NPC(s) now hold which named secrets
- **Lifecycle transitions** — secrets moving dormant→live (§6)
- **Ratifications** — unratified claims promoted to canon (§9)
- **Pending twist** and its trigger condition
- **Pacing note** — e.g. escalate, the player has been idle in this area too long

Rev. 4's *fact resolutions* field is gone with §5's inversion; there are no open fact requests to answer.

**Structured on disk, prose on the way in.** Rev. 4 asked for a JSON-like schema "so the Narrator consumes it deterministically". Half of that is right and half of it is contradicted by the code. `Mcp/QuestRender.cs:7-19` is explicit that tool results are plain text rather than JSON *deliberately*, because the consumer "is a language model that is about to write prose from this, and it reads a line like `Bess (npc) - HP 12/12` more reliably than the same fact wrapped in braces and quotes — for a fraction of the tokens, on every call, for the whole session." A directive is read by the same consumer for the same purpose. So: JSON on disk, where determinism matters and the Director writes it; rendered to text through `QuestRender` on the way to the Narrator, where legibility matters.

**A directive cannot ride in the system prompt.** The system prompt is the cached prefix — the whole reason one process is held open across turns — and it must stay byte-stable or every turn pays to rebuild it (`Program.cs:19-21`, `Agents/Claude/ClaudeSession.cs:12-14`). The directive is prepended to the per-turn user message instead, which today is the player's raw typed line and nothing else (`Program.cs:441`).

**Staleness.** Because directives are generated at turn N and consumed at N+1 or later, the world can change underneath them: the NPC a directive concerns dies, the player leaves the location, the twist's premise evaporates. On consumption, the Narrator compares the directive's target sequence against the journal's current head; if an invalidating write landed in between, the directive is dropped and the Director re-triggered. Cheap, and it removes a whole category of "why did the NPC say that" bugs.

## 9. Consistency Guarantees

Contradiction is a **bug to prevent structurally, not a mechanic to support**. The game does not retcon in-world: no NPC reinterprets what they said last scene, no narration quietly revises an earlier fact. If a contradiction does reach the player, it is logged and fixed out of band, and the game stays silent about it in play. A narrated self-correction is worse than the original error, because it tells the player the world is not stable.

This is already the shipped instinct in the one place it comes up. A rename leaves prose written before it alone, and the tool says so when it happens: memories *"written before now still spell out the old name — they are not rewritten"* (`Mcp/QuestTools.cs:633-636`), and the system prompt tells the narrator to *"treat that as the character's own recollection rather than a mistake to correct, and never narrate the correction"* (`Program.cs:78-82`). Rev. 5's §9 is that instinct generalised.

### Three tiers of fact

| Tier | Binding on the Director? | Can be built upon? |
|---|---|---|
| **Unratified claim** — said to the player, not yet reviewed | Yes — cannot be contradicted | No |
| **Ratified canon** — Director-approved | Yes | Yes |
| **Not yet said** — internal plot state | n/a | Yes |

An NPC's offhand line lands in the first tier: **binding but inert.** The world will never contradict it, because the player heard it — but nothing else can be built on it until the Director ratifies.

With §5 inverted this stops being a backstop and becomes the whole mechanism. Rev. 4 had ratification catching what slipped past a Narrator forbidden to invent; here everything the Narrator invents arrives this way by default, which makes the tier distinction load-bearing rather than incidental. It is also what makes the inversion safe: an improvising Narrator whose inventions were immediately canon would make every throwaway line a constraint on the campaign.

**Ratification** runs on the Director's normal cycle over the unratified claim queue. Two outcomes only: promote to canon, or leave inert. There is no *reject* — rejection would mean contradicting something the player heard, which the policy forbids. Because inert is the safe default, ratification never blocks a turn, and a backlog is harmless.

Lean into the asymmetry this creates: the Narrator can safely produce texture — a mention of bad weather last winter, a complaint about a cousin — knowing it will be honoured and may later be promoted if the Director finds it useful. In practice the queue doubles as a list of player-tested hooks.

### Mechanisms

**1. Single authorship, at the moment of invention.** Most contradictions come from two components inventing the same fact differently. Here there is only one inventor: the Narrator authors, and the Director may extend and promote but never re-author. Rev. 4 achieved the same property by forbidding the Narrator to invent at all; the property is what matters, not which side of the seam holds the pen.

**2. Write-once canon.** Canon may be *extended* but never overwritten — add detail, never negate. Append-only structures give this for free. The exposed surface is the two mutable description fields named in §5; those are journalled, and an attempted negation is surfaced by the batch test below rather than silently taking effect.

**3. The revealed ledger.** Canonical state and player knowledge are different things, and the contradiction that matters is with **what the player was told**, not what's in the store. Before authoring or ratifying, the Director reads the relevant ledger slice and is constrained by it.

The ledger records **claims, not truths.** An NPC who lies has said something false; that is a fact about what was said, so each entry carries the speaker plus a truth-status flag resolved against canon. Lying is not a contradiction — it is a claim the world knows to be false. Collapsing these would make every deceptive NPC look like a consistency bug and make it impossible to pay off a lie later. The game already keeps this distinction on the mechanical side, where a hidden roll's total is withheld from the player but never from the narrator (`Ui/RollWatcher.cs:167-171`, `Mcp/QuestRender.cs:83-87`); the ledger is the same idea applied to prose.

**4. Directive version checks.** §8. Prevents the Narrator from acting on plot decisions the world has already invalidated.

### Testing

Because the journal is append-only and the campaign is replayable from it, contradiction detection runs as a batch job over the log rather than being caught only in live play. It lives in `TerminalQuest.Tests/Consistency/ContradictionBatchTests.cs`, over a session played through the real tools rather than a hand-written log.

**Rev. 5 asked for four assertions. Two are expressible over the log, and two are not — which is worth stating precisely, because a green suite must not imply a guarantee it does not give.**

*Expressible, and built:*

- **Every description ever asserted is still on record.** Replay the journal's description arguments and assert each is still a substring of the document. This is the Phase 1 subset of "consistent with canon", and it is the assertion §5 says the journal was bought for. Non-tautological — the reference is the document rather than a replay of the same function, so it catches a hand-edit, a lost update between the two processes, and any tool added later that assigns where it should append. A companion test commits the contradiction deliberately and asserts the audit finds it, and another asserts the audit had something to check, because an audit over an empty set is the failure mode that looks most like success.
- **The ledger is well formed and its sequence climbs.** Every speaker id resolves, every revealed secret names something somebody holds, and no secret the ledger says was revealed is still live. The sequence check is also the backstop for the one hole the append path admits: the number is allocated from a window at the end of the file, so a hand-edit burying a higher one further back could reissue it.

*Not expressible, and replaced:*

- **"No fetch returned a dormant secret."** The journal records a call's *inputs*; it does not and must not record its output. Storing replies would put every secret and every hidden roll total into a plain text file the player can open — a worse leak than the one being tested for. So this becomes a sweep over **every advertised tool** in `SecretGateTests`, asserting a sentinel planted in a dormant secret appears in no tool's output. That is strictly stronger than the log version: it holds for every log that could ever be written, and it covers a tool nobody has written yet.
- **"No turn produced prose without a claims list."** The journal holds no prose, so a turn that read the world and narrated nothing is indistinguishable from a turn that narrated and forgot. The live check in the game is the mechanism, because that is the only place both facts are ever in hand at once. The batch job checks the weaker real property — that a turn which *did* record claims recorded them once.

*Not written at all, and honestly so:*

- **"No ratified fact was ever negated."** There is no ratification, so it would pass over an empty set. Better absent than vacuous.
- **"Every claim recorded true is consistent with canon as of that turn."** The same reason twice over. There is no canon — ratification is what promotes a claim out of the binding-but-inert tier into something else can be measured against, so with nothing ratifying, the assertion has no subject. And given canon, comparing two pieces of free prose for agreement is a *judgement* rather than an assertion, i.e. a model call; the Director is the thing that can make one, which is exactly why §9 places this after it exists. What *is* tested is the input that check will need: that claims arrive labelled, in quantity, and untouched by any adjudication.

So three of §9's four assertions turn out not to be writable as stated, and only the description-negation one survives in the form the document imagined. That is the single most useful thing implementing this revealed about §9, and it is not a reason to distrust the section — the surviving check is the one §5 identified as guarding the only surface where a contradiction can actually be committed.

`TerminalQuest.Tests` was described here as scaffold-only, which was already untrue when rev. 5 said it: it had several hundred facts.

**A note on `KnownBug`, because this document recommended reaching for it and that was wrong.** `Infrastructure/Categories.cs:16` defines the trait for *"a test that asserts what the code should do and therefore fails today… the suite doubles as an executable bug list, and a red test is harder to ignore than a comment."* The canon assertion was briefly written as an unconditional failure under it, to keep the Phase 2 debt visible. That misreads the trait: a test that can never go green by fixing code — only by being rewritten once a feature exists — is not a known bug, it is a comment wearing a test's clothes. It also cost more than it looked. The trait had no other user, so the filter that hides known bugs had exactly one thing to hide, and a genuinely broken suite would have reported the same single failure as a healthy one. The debt is recorded in the test class's remarks instead, and `dotnet test` is green with no filter. Reserve the trait for behaviour that is actually wrong.

## 10. Build-Out Path

Re-cut against the repository. Phase 0 is new; it exists because everything after it wants a version number and a log to test against, and neither exists today.

1. **Phase 0 — the journal. Done (rev. 6).** `journal.jsonl`, appended by the tool dispatch layer (`QuestTools.Invoke`), carrying sequence, turn, tool, arguments and outcome. No behaviour change, nothing the model can see, and it is the version counter §8 needs and the log §9 tests over. Cheapest possible first step and everything else assumes it.
2. **Phase 1 — partitioning and the ledger. Narrator only. Done (rev. 6).** Secrets with lifecycle stages and names, the fetch-boundary gate, the divergence function as a pure predicate, `record_claims` plus the ledger, spent-derivation, extend-not-replace descriptions, and the contradiction batch test. `get_character`'s "including everything they know" is retired here, and a test asserts the sentence stays retired. Secrets are created live by the Narrator through `grant_secret` and adjudicated by hand-editing the save; unratified claims queue up for a human, which doubles as a survey of what the Director will need to do.
3. **Phase 2 — the Director.** A second `IAgentSession` from `AgentSessionFactory`, its own system prompt, its own tool slice derived from role-tagged definitions, and its own model setting. Asynchronous overseer; event triggers plus the 8-turn ceiling; dormant becomes the default secret stage; ratification, description-negation validation, directive rendering and expiry.
4. **Phase 3 — tune against replayed sessions:** the event trigger set, the ceiling value, how aggressively secrets go live (the main cost lever), and ratification throughput.

**~~Adopting any of this costs every existing save.~~ Withdrawn (rev. 6): it cost nothing, and the schema stayed at 2.**

Rev. 5 asserted that `CurrentSchemaVersion` had to go 2 → 3 in Phase 1, with no conversion path. That was wrong, and the reason is worth keeping because it applies to the next change of this shape too. Nothing Phase 1 added changes the shape of an existing document: secrets are a new *optional* property on a character, and the journal and ledger are new *files*. `SaveStore` already treats a missing document as an empty one, and `System.Text.Json` already ignores a property it does not know. So an old save opens, reads as holding no secrets — which is exactly true, nobody was keeping anything — and starts a journal at sequence 1, which is also right, because there is no earlier history to number. A test asserts precisely this against a hand-written pre-secrets `characters.json`.

Bumping anyway would have destroyed every existing save to no purpose, since `RequireSupportedSchema` compares for *equality* and refuses rather than migrating (`Saves/SaveStore.cs:102-120`) — and it would have done so while reporting the message about stable identifiers, which would have been the wrong explanation.

The one real cost is asymmetric and belongs in release notes rather than in a version number: **an older build that writes `characters.json` will silently drop any secrets in the save**, because it deserialises without the property and serialises without it. That is the thing that will eventually justify a bump — not this change.

The general rule, since this is the second time the question has come up: bump when an existing document changes *meaning*, as version 2 did when names stopped being identities. Adding a field, or adding a file, is not that.

## 11. Decisions Record

No open design questions remain for Phases 0 and 1, which are built. Phase 2's remaining unknowns are **tuning values, not design choices** — the trigger set, the backstop count, and how liberally secrets are promoted to live. The one to watch is live-secret promotion: it is the biggest driver of how often the divergence gate refuses a fetch.

### Rev. 6: what implementing it changed

Six corrections and one decision rev. 5 never made. Each is argued where it belongs in the body; this is the index.

| Rev. 5 said | Disposition in rev. 6 |
|---|---|
| The journal carries "every **mutating** tool call" | **Corrected** to every call (§6). Rev. 5's own divergence rule reads two read-only tools, so the narrower log would not contain what the rule needs. Also removes the need for a read-or-write flag on every tool definition |
| A journal entry carries sequence, turn, tool, arguments | **Extended** with an outcome flag (§6). A refused fetch handed nothing over, and counting one would make the first refusal of a turn permanent |
| Phase 1 bumps the schema 2 → 3; "adopting any of this costs every existing save" | **Withdrawn** (§10). Nothing changed an existing document's shape; secrets are an optional property and the logs are new files. Existing saves open unharmed. The residual cost is one-directional and belongs in release notes |
| §9: assert over the log that no fetch returned a dormant secret | **Replaced** (§9) by a sweep over every advertised tool. Not expressible over a log that records inputs, and recording outputs would be a worse leak than the one under test. The replacement is stronger — it holds for every possible log |
| §9: assert over the log that no turn produced prose without a claims list | **Replaced** (§9) by the live check in the game. The journal holds no prose, so the batch version cannot tell "narrated nothing" from "narrated and forgot" |
| The ledger is append-only, *and* truth status is resolved against canon later | **Reconciled** (§6, §9). A later finding is a new entry naming the earlier one's sequence, never an edit. The status becomes a five-value enumeration, adding `mistaken` — an honestly wrong character is neither a liar nor a bug |
| §9: `TerminalQuest.Tests` "is scaffold-only today" | **Was already stale.** It had several hundred facts when rev. 5 said it |
| §9: unwritable assertions can land red under the `KnownBug` trait until their phase arrives | **Withdrawn** (§9). Tried, and wrong: that trait is for behaviour that is actually broken, and a test which can only go green by being rewritten is a comment in a test's clothes. It also left the hide-known-bugs filter with one entry, so a genuinely red suite looked healthy. Phase 2's debt is recorded in remarks; the suite is green with no filter |
| §9: four batch assertions | **Three of the four are not writable as stated** (§9). Only the description-negation check survives in the imagined form — which is the one guarding the surface §5 identified as the only place a contradiction can actually be committed |
| Who creates a secret in Phase 1 — *never stated*; the ownership table implies the Director, which does not exist | **Decided** (§6): the Narrator, via `grant_secret`, moving to the Director's slice in Phase 2. Read literally, rev. 5 left no way for a secret to exist in play, making the whole gate unreachable except by hand-editing |
| Which holder's copy goes spent — *never stated* | **Decided** (§6): every live holder's, and a spent secret is returned to any fetch. Otherwise a character who was never told keeps protecting something the player already heard |
| §6: the divergence rule "needs no new state… a pure function over the journal and the current turn" | **True but incomplete.** It also needs the roster, to know who holds what. Three inputs, not two |
| §5: descriptions are the only real contradiction surface | **Confirmed, and it was worse than described.** Both `update_*` handlers assigned the new value even when empty, so a description could be blanked outright. Fixed with the extend rule |
| §6: `record_claims` "called each turn", costing one extra round-trip | **Re-sequenced** (§6). The cost was never the problem; the ordering was. Called after the prose it never fires at all, because for an agent-loop provider the final text is the end of the turn. Claims are now recorded *before* the prose. Found by playing two turns, not by reading |

### Rev. 4 decisions, as recorded by rev. 5

Every rev. 4 decision is accounted for below. Nothing was dropped silently.

| Decision | Where | Disposition |
|---|---|---|
| Two instances of one model, split by prompt and state slice | §1 | Kept |
| Narrator owns all player-facing prose, not just dialogue | §4 | Kept |
| Rules resolver embedded beside the state store, not a service | §4 | Kept — shipped (§2) |
| Narrator may not invent world-state facts; deflect and file a request | §5 | **Reversed.** No authored world to defer to; the Narrator invents and the Director audits |
| Blocking fact requests get a synchronous Director call | §5 | **Dropped** with the above. Removes the only trigger on the critical path |
| Canon is write-once: extend, never negate | §5, §9 | Kept, and now the primary guarantee rather than a secondary one |
| Narrator writes scene/NPC state; Director owns plot/pacing | §6 | Kept; enforced by tool slice rather than by instruction |
| Secrets tracked per-NPC, seeded by faction | §6 | Kept |
| Dormant / live / spent lifecycle; dormant excluded from context; spent derived from ledger | §6 | Kept, and strengthened: dormant is never returned by any tool, not merely excluded from an assembled context |
| Call splitting computed from live-set divergence, not prompted | §6 | **Replaced.** Context is pulled, not assembled, and one session per NPC would cost a cold prompt cache each. The divergence function survives as a fetch-boundary refusal, still pure and still computed from state |
| Ledger entries per-assertion; claims list a required Narrator output | §6 | Kept as per-assertion; the channel becomes a tool call, there being no structured output beside the prose |
| Player claims recorded in the ledger like NPC claims | §6 | Kept, and made free: the game appends the player's own line without asking the model |
| Append-only store with a monotonic version per write | §6 | **Narrowed.** Documents stay last-write-wins; an append-only journal supplies the version, the staleness check and the replay log |
| Asynchronous overseer; Director runs after the player action, lands next turn | §7 | Kept |
| Event triggers plus a flat 8-turn backstop | §7 | Kept, minus the blocking-fact-request trigger |
| Director sees rolling summaries, never the full transcript | §7 | Kept, with a third reason: unbounded transcript growth on the LM Studio path |
| Directives structured, version-stamped, and expirable | §8 | Kept; version is now a journal sequence, and the directive is rendered to text on the way to the Narrator |
| Contradiction prevented structurally; never corrected in-world | §9 | Kept — already the shipped instinct for renames |
| Three fact tiers; NPC lines binding but inert until ratified | §9 | Kept, and promoted from backstop to primary mechanism |
| Ratification promotes or leaves inert — never rejects | §9 | Kept |
| Contradiction detection as a batch job over the event log | §9 | Kept, and the log is the journal — but only one of the four assertions it was to make is writable today, and the rest are recorded as what Phase 2 owes rather than as red tests (§9) |
| Latency affordance: build a "the DM is thinking" indicator early | §4 (rev. 4) | **Dropped as work.** Already built (`IsBusy`, `IsWaiting`, and the mid-turn roll poll), and the blocking calls that motivated it are gone |
