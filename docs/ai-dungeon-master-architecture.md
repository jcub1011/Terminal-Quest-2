# AI Dungeon Master Architecture

**Status:** Design proposal, adapted to the shipped code — partly implemented (§2)
**Last updated:** 2026-08-14 (rev. 5)

> **Revision history**
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

**Not built:** the Director, directives, pacing state, secrets and their lifecycle, the divergence rule, the revealed ledger, the claims list, ratification, the append-only journal, and the contradiction batch test.

**Built, but contradicting rev. 4:** the Narrator authors the world. `Mcp/QuestTools.cs:9-15` states the posture outright — *"The model is trusted here. It decides what happens in the story, so it decides what gets written."* §5 is where that is resolved.

## 3. Motivation

Running both jobs in a single model and context leads to two recurring problems:

1. **Mismatched cadence** — Player-facing prose is needed every turn. Story pacing decisions don't need to be re-evaluated that often, and doing so wastes calls and risks over-intervening. A human DM does not replan the campaign after every line of dialogue.
2. **Poor debuggability** — With one context doing both, there's no artifact to inspect when the story goes sideways. With the split, you can read what the Director decided independently of how it got dramatized, which localizes most bugs to one side of the seam or the other.

**A note on leakage.** An earlier version of this document justified the split primarily as leakage prevention — a model that knows an upcoming twist while voicing an NPC tends to let that knowledge bleed into dialogue. This is a real failure mode, but the split is not what fixes it. What fixes it is the **per-NPC knowledge partitioning and lifecycle gating** in §6. That partitioning is required regardless of architecture, and is needed in Phase 1 before any Director exists. The instance split makes it easier to enforce; it does not substitute for it.

The leakage argument is in fact weaker here than rev. 3 assumed, and honesty about why is useful. Today the Narrator can read everything: `get_memories` will answer for any character named, and `get_character` advertises itself as returning a character *"in full, including everything they know"* (`Mcp/QuestTools.cs:50`). There is no partition to leak across yet. That single sentence in the tool description is the first thing §6 has to change.

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

**Secrets need names, and the reason is a house rule rather than a preference.** Ids never leave the save layer (`Saves/EntityIds.cs`), so neither the ledger nor a directive can refer to a secret by id — the Narrator would have no handle to use and no way to report which secret a line revealed. A secret therefore gets a short name when it is created, the way everything else the model talks about does, and translation runs through the existing name↔id seam in `Saves/WorldIndex.cs`. "The innkeeper's brother", "the sealed cellar". The Director names them; the Narrator says the name back in a claim's `reveals` field, which is what drives the spent transition.

**Phase 1 has no Director, so dormant cannot be the default yet.** A secret created dormant with nothing able to wake it is invisible for the rest of the campaign, which is worse than no secret at all. Until the Director exists, a secret is created **live for its holder**, and the human adjudicates by hand-editing the save — which the file format was built for, and which `Saves/EntityIds.cs` cites as the reason ids are short and prefixed rather than GUIDs. Dormant becomes the default in Phase 2, when there is something to promote it.

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

### Versioning

Rev. 4 called for an append-only store where every write produces a monotonic version number, buying directive staleness checks (§8) and a replayable campaign (§9).

The store is not append-only. `SaveStore.Write` replaces whole documents through one generic method (`Saves/SaveStore.cs:331`), and threading a counter through it recurses, because `save.json` — where such a counter would naturally live — is written by that same method. Rewriting the store into an event-sourced one is a large change and buys nothing else.

**Decision: the journal is the version.** Every mutating tool call appends one line to `journal.jsonl` carrying its sequence number, the turn, the tool, and its arguments. That sequence number *is* the version a directive stamps itself against. One mechanism satisfies all three of rev. 4's asks — a monotonic version, staleness detection, and a replayable log — without touching `SaveStore`'s document handling.

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

Because the journal is append-only and the campaign is replayable from it, contradiction detection runs as a batch job over the log rather than being caught only in live play. For each ledger entry, assert it was consistent with canon as of that turn, accounting for truth-status and tier. Also assert that no turn produced prose without a claims list, that no ratified fact was ever negated, and that no fetch returned a dormant secret. This is the highest-value test in the system and worth writing during Phase 1, while the ledger is still small.

`TerminalQuest.Tests` exists for this and is scaffold-only today. It also has a convention that suits writing the test before the mechanism: `Infrastructure/Categories.cs:16` defines a `KnownBug` trait for *"a test that asserts what the code should do and therefore fails today… the suite doubles as an executable bug list, and a red test is harder to ignore than a comment."* The assertions above can land red under that trait and go green as each phase completes, which is a better record of intent than this document is.

## 10. Build-Out Path

Re-cut against the repository. Phase 0 is new; it exists because everything after it wants a version number and a log to test against, and neither exists today.

1. **Phase 0 — the journal.** `journal.jsonl`, appended by the tool dispatch layer (`QuestTools.Invoke`), carrying sequence, turn, tool and arguments. No behaviour change, nothing the model can see, and it is the version counter §8 needs and the log §9 tests over. Cheapest possible first step and everything else assumes it.
2. **Phase 1 — partitioning and the ledger. Narrator only.** Secrets with lifecycle stages and names, the fetch-boundary gate, the divergence function as a pure predicate over journal and turn, `record_claims` plus the ledger, spent-derivation, and the contradiction batch test. `get_character`'s "including everything they know" is retired here. Secrets are created live and adjudicated by hand-editing the save; unratified claims queue up for a human, which doubles as a survey of what the Director will need to do.
3. **Phase 2 — the Director.** A second `IAgentSession` from `AgentSessionFactory`, its own system prompt, its own tool slice derived from role-tagged definitions, and its own model setting. Asynchronous overseer; event triggers plus the 8-turn ceiling; dormant becomes the default secret stage; ratification, description-negation validation, directive rendering and expiry.
4. **Phase 3 — tune against replayed sessions:** the event trigger set, the ceiling value, how aggressively secrets go live (the main cost lever), and ratification throughput.

**Adopting any of this costs every existing save.** `CurrentSchemaVersion` goes 2 → 3, and `RequireSupportedSchema` refuses a save it does not recognise rather than migrating it — deliberately, because a misread save is one the narrator would build a new world on top of (`Saves/SaveStore.cs:102-120`, and the reasoning at `Program.cs:232-235`). Phase 1 is where the bump lands. There is no conversion path and there should not be one; say so in the release notes rather than discovering it in a bug report.

## 11. Decisions Record

No open design questions remain. Remaining unknowns are **tuning values, not design choices** — the trigger set, the backstop count, and how liberally secrets are promoted to live. The one to watch is live-secret promotion: it is the biggest driver of how often the divergence gate refuses a fetch.

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
| Contradiction detection as a batch job over the event log | §9 | Kept; the log is the journal, and the `KnownBug` trait is how it lands before the mechanism does |
| Latency affordance: build a "the DM is thinking" indicator early | §4 (rev. 4) | **Dropped as work.** Already built (`IsBusy`, `IsWaiting`, and the mid-turn roll poll), and the blocking calls that motivated it are gone |
