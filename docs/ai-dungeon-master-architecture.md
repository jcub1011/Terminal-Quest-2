# AI Dungeon Master Architecture

**Status:** Design proposal — not yet implemented
**Last updated:** 2026-08-13 (rev. 4)

> **Revision history**
>
> **rev. 2** — "Character Instance" renamed to **Narrator Instance** to reflect that it owns all player-facing prose. Added fact-authority policy (§5), split state write ownership between instances (§6), made directives version-stamped and expirable (§8).
>
> **rev. 3** — Director trigger policy specified (§7). Secret knowledge is per-NPC, forcing a call-splitting rule (§6). Contradiction became an explicit non-goal with structural guarantees, including the **revealed ledger** (§6, §9).
>
> **rev. 4** — Secrets get a **dormant/live/spent lifecycle** that also determines what enters Narrator context (§6). Ledger entries are **per-assertion**, emitted as structured output alongside prose (§6, §9). NPC dialogue is binding but inert until the Director **ratifies** it into canon (§9). All open design questions are now closed; §11 is a decisions record.

## 1. Overview

The AI Dungeon Master is split into two logical roles, run as **separate instances of the same underlying model**, rather than one model doing both jobs at once.

- **Narrator Instance** — produces everything the player reads: NPC dialogue, environmental description, action and outcome narration.
- **Director Instance** — manages story pacing: introduces twists, escalates or de-escalates tension, tracks the campaign at a plot level, adjudicates new world-state facts, and ratifies claims into canon.

Both instances are the same model. The split is achieved entirely through **different system prompts and different slices of game state**, not different models or fine-tunes. This keeps implementation simple while still separating concerns cleanly.

## 2. Motivation

Running both jobs in a single model/context leads to two recurring problems:

1. **Mismatched cadence** — Player-facing prose is needed every turn. Story pacing decisions don't need to be re-evaluated that often, and doing so wastes calls and risks over-intervening. A human DM does not replan the campaign after every line of dialogue.
2. **Poor debuggability** — With one context doing both, there's no artifact to inspect when the story goes sideways. With the split, you can read what the Director decided independently of how it got dramatized, which localizes most bugs to one side of the seam or the other.

**A note on leakage.** An earlier version of this doc justified the split primarily as leakage prevention — a model that knows an upcoming twist while voicing an NPC tends to let that knowledge bleed into dialogue. This is a real failure mode, but the split is not what fixes it. What fixes it is the **per-NPC knowledge partitioning and lifecycle gating** in §6: the Narrator is only ever given secrets its current speakers actually hold, and dormant secrets never enter its context at all. That partitioning is required regardless of architecture, and is needed in Phase 1 before any Director exists. The instance split makes it easier to enforce; it does not substitute for it.

## 3. Responsibilities

### Narrator Instance

- Voices all active NPCs in the current scene
- Narrates the environment, scene transitions, and the outcome of player actions
- Renders mechanical results supplied by the rules resolver into prose (it does **not** decide those results)
- Maintains NPC personality and relationship consistency
- Consumes any pending directive from the Director before generating a response
- Decides **how** things are dramatized, never **what** happens at a plot level
- Does not create new world-state facts — see §5
- Emits, alongside its prose, a **structured claims list** of every assertion made to the player this turn (§6)
- Writes scene state, NPC state, and the revealed ledger after each turn — see §6

### Director Instance

- Tracks the campaign at a plot/pacing level, not line-by-line dialogue
- Decides when to introduce twists, escalate tension, or nudge the story forward
- Adjudicates fact requests raised by the Narrator (§5)
- Manages secret lifecycle transitions (§6)
- Ratifies unratified ledger claims into canon (§9)
- Emits **structured directives** for the Narrator to act on — not narrative prose
- Never writes player-facing text

### Non-AI components

Out of scope for the two instances:

- **Deterministic rules/dice resolver** for all mechanical outcomes (combat, checks, inventory). Keeping mechanical resolution out of both LLMs is what prevents narrated outcomes that contradict the actual math. The resolver runs first; the Narrator describes what it returned. **Decision:** embed it alongside the state store rather than standing up a separate service. Extract it later if a second client (companion app, admin tooling, balance simulator) needs it — that extraction is mechanical, and the interface is the same either way.
- **Rules-based pacing tracker** (turn counters, fixed triggers) as the Phase 1 stand-in for the Director — see §10.
- **Summarizer** producing rolling scene summaries for the Director's context — see §7.
- **Divergence resolver** — a pure function over NPC secret sets that decides whether a scene runs as one pooled Narrator call or splits per NPC (§6). Deliberately not a model call.

## 4. Turn Loop

The canonical order of operations for a single player turn:

1. Player submits input.
2. Rules resolver runs if the input requires mechanical resolution; result is written to state.
3. Divergence resolver assembles Narrator context: one pooled call, or one call per NPC (§6).
4. Narrator Instance generates player-facing prose **and** its claims list, then writes scene state, NPC state, and ledger entries. Consumed directive is cleared.
5. Output is shown to the player.
6. **Asynchronously**, if a Director trigger has fired, the Director Instance runs — seeing the turn that just completed — and may queue a directive for the next turn, transition secret lifecycles, and ratify pending claims.

Step 6 does not block step 5. The Director always runs *after* the player action it is reacting to, and its output lands on the following turn.

**Latency affordance.** Blocking fact requests (§5) and per-NPC call splitting (§6) both make some turns noticeably slower than others. Build a "the DM is thinking" indicator early. A visible pause reads as deliberation and is nearly free in immersion terms; an invisible one reads as a hang. This also removes latency as a reason to loosen the correctness rules later.

## 5. Fact Authority

**The Narrator Instance may not invent world-state facts.** When a player probes something nobody specced — the innkeeper's brother, what's behind a door that was never defined, whether a faction has a presence in this town — the Narrator does not improvise an answer.

Instead it does two things in the same turn:

1. **Deflects in character.** The deflection must be diegetic, not a refusal. An NPC changes the subject, gives a partial or evasive answer, is interrupted, or genuinely doesn't know. This is a normal and realistic thing for a person to do, and it costs nothing in immersion if the prompt supplies the Narrator with a few deflection patterns rather than leaving it to guess.
2. **Raises a fact request** into pacing state: what was asked, which NPC was asked, and whether the scene can proceed without an answer.

The Director resolves fact requests on its next cycle and returns the canonical fact as part of a directive. The Narrator can then answer properly on a later turn — the NPC "remembers," or the player asks again, or another character brings it up.

**Blocking requests.** Some probes can't be deflected twice without the seams showing, and a few genuinely gate progress — the player is standing at the door asking what's through it. For these, the Narrator marks the fact request as blocking, which triggers a synchronous Director call before the Narrator's next response. This costs one extra round-trip on those turns only. Expect blocking requests to be a small fraction of the total; if they aren't, the world-state seeding is too thin, and the fix is more up-front content, not looser Narrator authority.

This is the strictest available policy and it is chosen deliberately: it guarantees every fact has exactly one author, which is the load-bearing mechanism behind §9. The cost is that deflection quality becomes a first-class prompt-engineering concern. Budget real effort there.

## 6. Shared World-State

Both instances read from and write to a **single, structured source of truth**. Chat history is not a coordination channel — neither instance should have to infer facts from prior conversation text.

### Categories

- **Scene state** — current location, characters present, recent events
- **NPC state** — personality profile, relationship to player, and the secrets *that specific NPC* holds
- **Plot state** — active quest threads, twists used, ratified canonical facts, secrets and their lifecycle stage
- **Pacing state** — turns since last major beat, tension level, pending directive, open fact requests, unratified claim queue
- **Revealed ledger** — per-assertion record of what has been said to the player, and by whom (§9)

### Write ownership

| State | Narrator writes | Director writes |
|---|---|---|
| Scene state | Yes | Rarely (scene-level directives) |
| NPC state | Yes (relationships, what's been revealed) | Yes (granting secrets) |
| Plot state | No | Yes (append-only, see §9) |
| Pacing state | Fact requests only | Yes |
| Revealed ledger | Yes (append-only) | No (read-only) |

The Narrator must be able to write scene and NPC state, because play *happens* in the Narrator. Relationships shift, an NPC lets something slip, the player learns a name. A read-only Narrator means scene and NPC state drift away from what actually happened at the table, and "recent events" has no author. The real boundary is not read/write — it is that **plot and pacing belong to the Director, scene and NPC belong to the Narrator.**

### Secret lifecycle

Every secret carries a stage, and the stage determines whether it enters Narrator context at all:

| Stage | In Narrator context? | Set by |
|---|---|---|
| **Dormant** | No — excluded entirely | Director (default on creation) |
| **Live** | Yes, only for NPCs that hold it | Director, via directive |
| **Spent** | Yes, for everyone in scene | Derived automatically from the ledger |

**Dormant** is the default and does most of the leak prevention: a secret the Director hasn't activated is not in the Narrator's context, so it cannot leak by any mechanism, prompt failure included. An NPC who "knows" a dormant secret simply behaves as though unaware — which is the desired behavior, since the secret isn't plot-relevant yet.

**Live** means the Director has activated the secret for the current stretch of play. Only live secrets participate in divergence detection below, which is what keeps call splitting affordable — splitting is proportional to how many secrets are *currently in play*, not to how many exist in the campaign.

**Spent** is derived, not assigned: when a secret appears in the revealed ledger, it transitions automatically. This keeps a Director round-trip off the critical path and keeps the divergence rule a pure function. Once spent, the secret is public knowledge and needs no partitioning — NPCs can discuss it freely.

Dormant→live is the only transition the Director controls, and it happens through the normal directive channel.

### Knowledge partitioning

Secret knowledge is tracked **per NPC**, not per faction. Faction membership may *seed* an NPC's secret set, but the authoritative record is per-NPC, because "who specifically knows this" is exactly the fact that determines whether a scene works.

Per-NPC granularity has a consequence that per-faction would have hidden: **if one Narrator call voices several NPCs, every secret in that call's context is available to all of them.** Storing secrets per-NPC and then pooling them at call time gives away the precision just paid for.

The divergence rule, evaluated per scene:

- Assemble the **live** secret set for each NPC present. Dormant secrets are excluded from context; spent secrets are shared.
- If those sets are identical, use **one pooled call**.
- If two present NPCs differ on any live secret, **split into one call per NPC**, giving each only its own knowledge.

Splitting costs N× the calls and some conversational coherence — NPCs in separate calls play off each other less naturally, since neither sees the other's line before generating. Mitigate by running split calls in a fixed speaking order and passing each prior line forward as ordinary scene state, which is public information and safe to share.

The important property: the split is **computed from state, not decided by a prompt**. The model is never asked to hold a secret it can see. Divergence detection is a pure function of the live sets, so it's unit-testable in isolation.

### Revealed ledger granularity

Entries are **per-assertion**. The Narrator emits a structured claims list alongside its prose in the same call — each entry carrying the claim, the speaker, the turn, and a truth-status flag (§9).

Per-scene entries were considered and rejected: they are too lossy for the consistency test in §9 to check anything meaningful. Per-turn summaries were also rejected, because they still require the Narrator to extract assertions from its own output — and if extraction is happening anyway, coarsening it only discards precision for no saving.

Two consequences worth designing for:

- The claims list is a **required output field**, not optional. A turn that produces prose with no claims list is a hard error in development, because an unextracted claim is invisible to consistency checking — it can be contradicted later and nothing will catch it.
- **Player claims are recorded too**, with the player as speaker. If the player lies to an NPC, that assertion is in the ledger like any other, which is what lets an NPC remember being deceived and lets the truth-status flag resolve against canon later. Deception in either direction uses one mechanism.

### Versioning

The state store is **append-only**, with each write producing a monotonic version number. This buys two things: directives can be checked for staleness (§8), and the whole campaign becomes replayable from the event log — the difference between "the story went weird once" and a reproducible test case. Design for this from the start; retrofitting it is painful.

## 7. Coordination Model

**Pattern: asynchronous overseer.** The Narrator runs every player turn. The Director wakes on triggers only, sees the completed turn, and queues a directive for the *next* turn rather than re-evaluating continuously. This is cheaper, matches real DM behavior, and keeps the Director off the critical path.

The alternative — turn-based gating, where the Director always runs first and the Narrator waits — is simpler to reason about but pays Director latency every turn and re-plans far more often than necessary. Not recommended, though the state seam is identical, so switching later is cheap.

### Trigger policy

Event-driven, with a turn ceiling as a backstop. Events are the real signal; the ceiling exists so the Director can never go dark during a long stretch of unremarkable play.

**Event triggers** (initial set — expect to tune):

- Player enters a new location, or a scene boundary is crossed
- Combat or another resolver-driven set piece ends
- A blocking fact request is raised (synchronous, per §5)
- A twist's trigger condition is satisfied
- Player idles or repeats similar actions past a threshold

**Backstop:** if no event has fired within **8 turns**, wake the Director anyway. The value is a flat count, not scaled by scene type — scene-type scaling adds a classification problem in exchange for tuning precision that only real play can inform, and the ceiling is meant to be a safety net rather than a pacing instrument. Lower it only if play feels unattended. Frequent ceiling firing is a diagnostic that the event set is missing something; treat it as a signal, not a load-bearing trigger.

Both paths record *which* trigger fired alongside the resulting directive. That field is what makes over-intervention debuggable later.

### Director context

Rolling summary, not full transcript. Full transcript defeats the cost argument and pushes the Director toward the same omniscience the partitioning is meant to avoid. Cheapest implementation: have the Narrator emit a short scene summary at scene boundaries, since it already has the material. The Director additionally reads the relevant slice of the revealed ledger before authoring or ratifying any fact (§9).

## 8. Directive Format

The Director emits **structured decisions**, not prose. The Narrator interprets *what* should happen and decides *how* it's dramatized. Blurring this line is the single most common source of contradiction bugs.

A directive carries:

- **Target state version** — the version it was generated against
- **Triggering event** — which trigger woke the Director (§7)
- **Expiry condition** — turn count, scene boundary, or event after which the directive is void
- **Tone/tension** target for the scene
- **Secret grants** — which NPC(s) now hold which secrets
- **Lifecycle transitions** — secrets moving dormant→live (§6)
- **Ratifications** — unratified claims promoted to canon (§9)
- **Pending twist** and its trigger condition
- **Fact resolutions** — answers to open fact requests, promoted to canonical plot state
- **Pacing note** — e.g. escalate, the player has been idle in this area too long

Schema is left to implementation but must stay structured (JSON-like), not prose, so the Narrator consumes it deterministically.

**Staleness.** Because directives are generated at turn N and consumed at N+1 or later, the world can change underneath them: the NPC a directive concerns dies, the player leaves the location, the twist's premise evaporates. Twists have trigger conditions, but tone and pacing notes need expiry too. On consumption, the Narrator compares the directive's target state version against current state; if an invalidating write landed in between, the directive is dropped and the Director re-triggered. Cheap to implement, and it removes a whole category of "why did the NPC say that" bugs.

## 9. Consistency Guarantees

Contradiction is a **bug to prevent structurally, not a mechanic to support**. The game does not retcon in-world: no NPC reinterprets what they said last scene, no narration quietly revises an earlier fact. If a contradiction does reach the player, it is logged and fixed out of band, and the game stays silent about it in play. A narrated self-correction is worse than the original error, because it tells the player the world is not stable.

That policy needs machinery, since a strict rule with no enforcement just relocates the failure.

### Three tiers of fact

| Tier | Binding on the Director? | Can be built upon? |
|---|---|---|
| **Unratified claim** — said to the player, not yet reviewed | Yes — cannot be contradicted | No |
| **Ratified canon** — Director-approved | Yes | Yes |
| **Not yet said** — internal plot state | n/a | Yes |

An NPC's offhand line lands in the first tier: **binding but inert.** The world will never contradict it, because the player heard it — but nothing else can be built on it until the Director ratifies. This is the distinction ratification actually buys. Without it, either every improvised line silently becomes load-bearing canon, or the world feels free to contradict things the player was told. Neither is acceptable.

**Ratification** runs on the Director's normal cycle over the unratified claim queue in pacing state. Two outcomes only: promote to canon, or leave inert. There is no *reject* — rejection would mean contradicting something the player heard, which the policy forbids. Because inert is the safe default, ratification never blocks a turn, and a backlog is harmless.

Note the asymmetry this creates, and lean into it: the Narrator can safely produce texture — a mention of bad weather last winter, a complaint about a cousin — knowing it will be honored and may later be promoted if the Director finds it useful. That is a considerably better authoring experience than either extreme, and in practice the queue doubles as a list of player-tested hooks the Director can pick up.

### Mechanisms

**1. Single authorship.** Most contradictions come from two components inventing the same fact differently. §5 makes that structurally impossible: the Narrator cannot author *world-state* facts, so canon has exactly one author. This does the majority of the work.

**2. Write-once canon.** The Director may *extend* a ratified fact but never overwrite it — add detail, never negate. An attempted overwrite is a hard error surfaced in development, not silent last-write-wins. Falls out of the append-only store; the only addition is a validation step on Director writes.

**3. The revealed ledger.** Canonical state and player knowledge are different things, and the contradiction that matters is with **what the player was told**, not what's in the store. Before authoring or ratifying, the Director reads the relevant ledger slice and is constrained by it.

The ledger records **claims, not truths.** An NPC who lies has said something false; that is a fact about what was said, so each entry carries the speaker plus a truth-status flag resolved against canon. Lying is not a contradiction — it is a claim the world knows to be false. Collapsing these would make every deceptive NPC look like a consistency bug and make it impossible to pay off a lie later.

**4. Directive version checks.** §8. Prevents the Narrator from acting on plot decisions the world has already invalidated.

### Testing

Because the store is append-only and the campaign is replayable, contradiction detection runs as a batch job over the event log rather than being caught only in live play. For each ledger entry, assert it was consistent with canon as of that turn, accounting for truth-status and tier. Also assert that no turn produced prose without a claims list, and that no ratified fact was ever overwritten. This is the highest-value test in the system and worth writing during Phase 1, while the ledger is still small.

## 10. Build-Out Path

1. **Phase 1** — Narrator Instance only, plus the rules resolver and a non-AI pacing tracker (turn counters, fixed triggers). **In scope:** per-NPC secrets with the lifecycle stages and divergence resolver, the append-only store, the per-assertion ledger with claims-list extraction, and the contradiction batch test. None of these are Director prerequisites. Fact requests and unratified claims queue up for a human to adjudicate, which doubles as a survey of what content the world is missing.
2. **Phase 2** — Introduce the Director Instance once the rules-based tracker feels too rigid. Start with the asynchronous overseer pattern; wire up event triggers plus the 8-turn ceiling, fact-request adjudication, dormant→live transitions, ratification, write-once validation, and directive expiry.
3. **Phase 3** — Tune against replayed sessions: the event trigger set, the ceiling value, how aggressively secrets go live (the main cost lever), and ratification throughput.

This avoids over-building the coordination layer before there's a concrete sense of what state it needs to carry.

## 11. Decisions Record

No open design questions remain. Decisions, and where each is specified:

| Decision | Where |
|---|---|
| Two instances of one model, split by prompt and state slice | §1 |
| Narrator owns all player-facing prose, not just dialogue | §3 |
| Narrator may not invent world-state facts; deflect and file a request | §5 |
| Blocking fact requests get a synchronous Director call | §5 |
| Rules resolver embedded beside the state store, not a service | §3 |
| Narrator writes scene/NPC state; Director owns plot/pacing | §6 |
| Secrets tracked per-NPC, seeded by faction | §6 |
| Dormant / live / spent lifecycle; dormant excluded from context; spent derived from ledger | §6 |
| Call splitting computed from live-set divergence, not prompted | §6 |
| Ledger entries per-assertion; claims list a required Narrator output | §6 |
| Player claims recorded in the ledger like NPC claims | §6 |
| Asynchronous overseer; Director runs after the player action, lands next turn | §4, §7 |
| Event triggers plus a flat 8-turn backstop | §7 |
| Director sees rolling summaries, never the full transcript | §7 |
| Directives structured, version-stamped, and expirable | §8 |
| Contradiction prevented structurally; never corrected in-world | §9 |
| Three fact tiers; NPC lines binding but inert until ratified | §9 |
| Ratification promotes or leaves inert — never rejects | §9 |
| Canon is write-once: extend, never negate | §9 |
| Contradiction detection as a batch job over the event log | §9 |

Remaining unknowns are **tuning values, not design choices** — the trigger set, the backstop count, and how liberally secrets are promoted to live. Each has a stated starting value and a place in the Phase 3 tuning pass. The one to watch is live-secret promotion: it is the single biggest driver of call volume, since it determines how often scenes split.
