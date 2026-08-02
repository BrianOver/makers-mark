---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
type: feat
title: Loop legibility — every click answers, every phase declares itself, the tutorial points
date: 2026-08-02
origin: owner playtest notes 2026-08-02 (fourth full playtest; build = main incl. #334/#335/#338/#339/#340/#341/#345/#348)
roadmap: docs/plans/2026-07-28-003-roadmap-post-skeleton.md
---

# Loop legibility — every click answers, every phase declares itself, the tutorial points

## Goal Capsule

The fourth playtest happened on the build where the entire playtest-three wave had already
landed — send-off show (#335), one phase vocabulary (#339), the three-day tutorial (#338) —
and the loop still did not read. The notes decompose into three faults, and this plan's
investigation located each one precisely:

1. **Half the verbs answer, half go silent.** `ActionTiming` (the 2026-07-30 split) made the
   nine workshop verbs resolve instantly via `GameKernel.ApplyNow`; the other **fifteen** verbs
   still ride the bell through a client-side queue (`SimAdapter._pending`) with **zero
   acknowledgment, zero visibility, zero cancel** (`SimAdapter.cs:23,106-134` — the list has
   `Add` and `Clear`, nothing else). "Posting the bounty queues it — nothing happens", "opening
   the counter queues", "Open counter does nothing", "you have a TON of past queued actions
   which don't interact with our game well" are all this one fault. The tutorial then compounds
   it: steps 3 and 6 complete on `BountyPosted` / `CounterSaleClosed` **events**, which only
   exist after a tick — so the tutorial waits forever on an action sitting in a queue the
   player cannot see.

2. **The screen replays phase theater on every immediate action.** `SimAdapter.StateChanged`
   fires both when a phase completes AND when an immediate action lands (`SimAdapter.cs:133`).
   `MainUi.OnPhaseCompleted` is the sole subscriber and forwards every firing to
   `Town.OnPhaseCompleted(completedPhase)` (`MainUi.cs:517`) — which runs the full
   phase-transition choreography with no boundary guard (`Town2D.cs:581-604`). So: **Stock an
   item during Morning → `DepartWanderingHeroes()` → every idle hero rallies and marches out
   the gate** ("hitting stock keeps sending the heroes out lol?"). **Any immediate action
   during a raid phase → `ReturnSurvivors()` → heroes visibly walk back into town mid-raid**
   ("why did the heroes come back to the town visually?"). The departure camera pan got exactly
   this guard (`MainUi.cs:1903`, `completedPhase != state.Phase`); the town choreography,
   interior stage, and ticker forwards did not.

3. **The phase machine and the tutorial narrate instead of directing.** The kernel day is
   Morning → Expedition → Camp → ExpeditionDeep → Evening (`GameKernel.cs:182-191`). Camp runs
   **zero systems** unless the player volunteers a camp verb; ExpeditionDeep has **zero legal
   player actions** and emits **zero events** (outcomes surface at the Evening reveal). When no
   party is underground the kernel still walks every raid phase — the harness's own comment
   calls them "the two empty ticks" (`BaselinePlayer.cs:86`). Nothing on screen says what Vigil
   is FOR, that Deep Vigil exists, or that "Close the vigil" sends the party *deeper* rather
   than ending the vigil. And the tutorial is a text log in a HUD chip (`ObjectiveTracker`
   top-slot override) with **no pointing capability at all** — `Building2D.SetHighlighted`
   exists but is wired only to mouse hover.

**Goal:** every click gets an answer within one frame — a state change, or a visible
cancellable promise naming its bell; the screen never replays theater the sim didn't perform;
every phase opens by declaring what is happening, what you can do, and what the bell will do;
empty raid phases stop taking the player's time; and the tutorial points at the thing, tracks
objectives with checkmarks, teaches the action budget, and graduates the gear gap from
tutorial objective to permanent affordance.

---

## Standing constraints (restated because every executing agent must obey them)

1. **Sim purity (KTD2):** zero Godot references in `sim/GameSim/`; no RNG outside the injected
   stream; no wall clock; no transcendental `Math.*`. All rule changes in this plan live in
   `sim/GameSim/Kernel/` and are consumed by the adapter read-only.
2. **Determinism / ONE re-baseline.** U1 changes when actions apply and which phases the
   kernel walks — that IS a golden-replay and balance-pin re-baseline. It lands as **one
   deliberate, labelled commit inside U1's PR** (`re-baseline: action timing + empty-phase
   collapse`), following #328's shape. **No other unit in this plan may touch `sim/` at all**
   — U2–U7 are adapter/test-only and must show a zero sim diff.
3. **Deny-list — never edit:** `Game.sln`, `godot/project.godot`, `.github/`,
   `sim/GameSim/Contracts/`, `CLAUDE.md`, `global.json`, `Directory.Build.props`,
   `.godot-version`. This plan is deliberately shaped to need **zero Contracts edits**: no new
   `DayPhase` member, no new `GameEvent` type, no new `PlayerAction` type, no `ActionBudget`
   change. If the owner's answers to the open questions require a contract change, that lands
   as an orchestrator-authored micro-PR first, per house rule — flagged, never inlined.
4. **Engine tests are SERIALIZED.** Implementing agents never run `dotnet test godot/tests`;
   the orchestrator runs the full engine suite once per branch. CI floor is
   `ENGINE_MIN_PASSED=300`; "Failed: 0" alone is not a pass.
5. **Godot tests wait on the CONDITION, never a frame count** — CI disables rendering and runs
   faster per frame than local.
6. **Tests pin the SET.** Every new surface in this plan is registry-enumerated in its tests
   (reflection over `PlayerAction` types, `Enum.GetValues<DayPhase>()`, the tutorial step
   registry) — never a hand-written list a new member can silently miss. This repo's recurring
   failure is silent fallbacks and dead clicks; a hand-listed test IS a silent fallback.
7. **Visible-difference receipts** (`tools/receipt.ps1`) for every player-facing unit —
   rebuilt binary, in-frame stamp, measured pixel diff. Text-scale changes may lower
   `-MinDiffPercent` with the justification stated in the PR body.
8. **One unit = one branch (`feat/uN-slug`) = one small PR.** Conventional commits, no
   `git add .`.

## Already handled — context, not work (do NOT re-plan)

- **Playtest-three wave shipped in full**: send-off show + Watch control (#335), phase
  vocabulary `PhaseVocab` (#339), forge pacing (#334), audio pass (#340), six hero bodies
  (#341), three-day tutorial (#338), forge interior U1 (#345) + painted shell (#342), sim
  re-baseline (#328). The owner's new notes are about the build WITH all of that — do not
  re-fix those units; this plan builds on them.
- **"Continue day 2 is still there"** — diagnosed honest twice (autosave fires at Evening
  completion; the label reports where you resume). What's left is wording, and #339 already
  routes it through `PhaseVocab`. U4 carries a one-line copy change ("Resume at Dawn of day
  2"), nothing more.
- **The Morning counter-hold** (`GameKernel.cs:184`) and its `MORNING-HOLD` toast/log guard
  shipped with #339's unit. U1 keeps the hold; the counter session becoming live-resolved
  makes the hold *shorter-lived*, not different.
- **"PT17"** — the open item named in this plan's brief does not exist under that ID anywhere
  in the repo (checked files, tracked content, `git log --all -S`). The underlying fact is
  real and better evidenced: `BaselinePlayer.cs:86`'s "the two empty ticks" comment and
  `ExpeditionDeepSystem.cs`'s event-silent resolution. U1 fixes the fact; nothing cites PT17.

---

## Requirements — each traced to the owner's words

- **R1 — Every verb answers within one frame.** Either the state visibly changes (immediate
  lane) or the screen acknowledges a promise and names its bell (deferred lane). No third
  outcome. *("Posting the bounty queues it - nothing happens"; "opening the counter queues.";
  "Open counter does nothing"; "I stopped because the shop didn't do anything.")*
- **R2 — What still waits is visible and cancellable.** A pending deferred action appears in
  an on-screen tray on the bell row, names what it will do, and can be withdrawn before the
  bell. *("you have a TON of past 'queued' actions which don't interact with our game well
  lol")*
- **R3 — The tutorial never waits on a queue.** Every tutorial step's completion fact exists
  the moment the player has done their part. *("the tutorial is stuck at 3"; "Open counter
  does nothing - tutorial stuck at 6"; "Still in tutorial 6 during the night")*
- **R4 — Immediate actions never replay phase theater.** Stocking an item cannot march heroes
  out the gate; crafting during a raid cannot walk them home. *("hitting stock keeps sending
  the heroes out lol?"; "why did the heroes come back to the town visually?")*
- **R5 — Every phase declares itself.** On phase entry the player is told, in one card: what
  is happening, what THEY can do (derived from the sim's own legality query, never hand-typed),
  and what the bell will do next. The bell's label is the truthful consequence ("Send them
  deeper", not "Close the vigil"). *("WHAT is the gate? HOW do we watch"; "Confusing what we
  are supposed to do during the 'vigil' phase??"; "now in 'deep vigil??' phase"; "Tutorial 6
  says press 'next/advance' assuming this should be 'close the vigil'")*
- **R6 — Empty phases don't take the player's time.** When nobody is underground, the kernel
  does not walk Expedition/Camp/ExpeditionDeep; the day folds to dusk with a narrated line.
  *("i think in general, the phases aren't correct / lining up - think about how to improve.
  Compare to other games")*
- **R7 — The tutorial points, tracks, and confirms.** Each step anchors to the actual thing
  (building or HUD control), the objective list shows checkmarks, and doing the step's action
  visibly ticks it — including intermediate acts like entering the right building. *("yeah
  tutorial overlay would be nice"; "tutorial overlays/guidance rather than just the tutorial
  log"; "Tutorial didn't update when i entered the forge")*
- **R8 — The action budget is taught and always visible.** The pip row stops being
  Morning-only trivia: a tutorial step explains it, and the pips carry a tooltip naming which
  verbs spend them. *("btw since we have the limited actions game mechanic, should make this
  more obvious / explain in the tutorial")*
- **R9 — The gear gap graduates.** During the tutorial it is an objective chain (find the gap
  → forge the piece → shelve it); after the tutorial it is a permanent, glanceable affordance
  (a gap-count badge on the Forecast button) the player can check any time. *("I love the
  'gear gap' type feature - expand that and make more helpful to the player. I think the
  tutorial should point and have as objectives then the full game leaves it up to the player
  (also need to be able to check this if needed)")*
- **R10 — The ledger reads like a reward and is taught once.** Better text boxes and visuals
  on the Evening Ledger; one tutorial line establishing what it is. *("the recap ledger is
  nice - improve the text boxes and maybe add visuals. Also need to explain with the tutorial
  if gameplay relevant")*

---

## Key Technical Decisions

### KTD-A — The timing model: two lanes, total classification, one question

The load-bearing decision. The 2026-07-30 split was right but stopped too early: it moved the
workshop and left every *conversation* and every *signal* in the queue. The rule that survives
contact with this playtest is:

> **An action resolves NOW unless the WORLD must move before the action means anything.
> The world moving is what the bell is for — and even then, the player's own part of the
> act resolves now.**

Applied to the fifteen deferred verbs:

| Verb | New lane | Why |
|---|---|---|
| `OpenCounter` / `PresentItem` / `SuggestItem` / `HaggleResponse` / `CloseCounter` | **Now** | A conversation with a hero standing at your counter. The kernel already owns the session's ordering (PA3 state machine) and `ApplyNow` runs the same handler predicates — sequencing is preserved by the handlers, not by the bell. Bell-stepping a haggle was the single most disorienting thing in the playtest. |
| `AcceptCommission` / `DeclineCommission` | **Now** | The old `ActionTiming` comment itself concedes these are "a conversation with someone who is standing there". |
| `PostBounty` | **Now** | Pinning paper to a board is the player's own hands. The *heroes reading it* is the world's part and still happens on subsequent ticks — unchanged. `BountyPosted` lands in the event log at click time, which is also what unsticks tutorial step 3. |
| `SendSupply` / `RecallParty` | **Now** | These are Vigil's only verbs; if they are dead clicks, the phase the owner already finds confusing has literally nothing that responds. Both are pure state edits (`CampHandlers`: fee + front-insert consumable; `Recalled = true`) — the *effect on the raid* still lands when `ExpeditionDeepSystem` resolves, which is the honest fiction: the runner leaves now, the deep answers later. |
| `UnlockTalent` | **Now** | Edits only player-owned progression state. A "reflection between days" that eats a click and shows nothing is a dead click, not a rite. |
| `HonorMemorial` | **Now** | The player's own act at the memorial. Kills a known dead click (the memorial nag family). |
| `UpgradeForge` | **Bell** | Construction. A beat between deciding and having is good fiction — but it becomes a *visible, cancellable* beat (KTD-B). |
| `SetProfessions` | **Bell** | Identity, settled at a day boundary. Rare enough that the beat reads as ceremony. |
| `CommissionLegendaryWork` | **Bell** | A pact the Guild acts on, not a bench task. |

Twelve verbs move to Now; **three remain bell-riders**. The classifier stays deny-by-default
in code, but a new conformance test removes the "silently forgotten" failure mode the default
was protecting against: it reflects every concrete `PlayerAction` type out of the GameSim
assembly and asserts each appears in an explicit expected-classification table inside the
test. A new action type fails the test until a human classifies it; flipping any single verb's
lane fails the test by name. That is the SET pinned from a registry (the type system), and it
is mutation-checkable.

Legality is untouched: `ApplyNow` runs the same `CanHandle(action, phase)` gates, so a camp
verb clicked outside Camp is still refused with a typed rejection — "when" moved, "whether"
did not (`ActionTiming`'s own contract, preserved).

### KTD-B — What still queues is a visible, cancellable promise

The three bell-riders get the treatment the queue never had: submitting one (a) raises an
immediate acknowledgment toast naming the bell ("At the bell: the Guild takes your
commission"), and (b) adds a chip to a **bell tray** rendered on the bell row, each chip with
a withdraw control. Withdrawal is `SimAdapter`-side removal from `_pending` **before** the
tick — the action never reaches the kernel, so determinism and the replay format are
untouched by construction. No kernel or contract change. The tray renders from
`SimAdapter.PendingActions` (which already exists) — the tray IS the queue, so it cannot lie.

### KTD-C — One event per meaning: split `StateChanged`

`SimAdapter.StateChanged` currently means "something happened" and every subscriber must
hand-roll the boundary guard that only `MainUi.cs:1903` actually has. The fix is the
generalization, not another copy: `SimAdapter` exposes two events — **`ActionApplied`**
(immediate lane landed; current phase/day) and **`PhaseCompleted`** (a tick ran; completed
phase/day). `MainUi` refreshes panels on both; phase theater (`Town.OnPhaseCompleted`,
`Interior.OnPhaseCompleted`, `Ticker.OnPhaseCompleted`, departure beats, ledger trigger)
subscribes to `PhaseCompleted` **only**. The `MainUi.cs:1903` hand guard becomes dead and is
removed. This kills both R4 quotes at the root and makes the next listener safe by default.
Adapter-only; `SimAdapter` is deliberately engine-free so the split is testable in plain
NUnit/gdUnit without a scene.

### KTD-D — Phases earn their bell: a decision, or a show — otherwise they collapse

The comparison the owner asked for, against games whose day-loop this game rhymes with:

- **Recettear / Moonlighter** (the shopkeeper-economy canon): the day has exactly the modes
  that have decisions in them — shop mode and dungeon mode. Neither game ever hands you a
  timeslice with nothing to decide and nothing to watch.
- **Persona**: a time slot only appears in order to ask its one question. The slot IS the
  question; when there is no question, the game moves time itself.
- **Darkest Dungeon**: embark → resolve → report. The resolution is watchable when there is
  something to watch, and the game compresses hard to the report when there is not.

Maker's Mark's day against that standard: Morning asks "what do you make and sell" (real
question). Expedition asks nothing but *shows* the departure and stage-1 (a show — keep).
Camp/Vigil asks "supply, recall, or let them press deeper" (a real question **that the screen
never poses**). ExpeditionDeep asks nothing and shows the mirror (a show — keep, but say so).
Evening asks "what do you take from the day" (real question). And when no party went down,
Expedition/Camp/ExpeditionDeep ask nothing and show nothing — they are the harness's "two
empty ticks" made of player clicks.

Decisions that follow:

1. **Kernel collapse rule (sim, U1):** `Advance` becomes state-aware — a raid phase is
   entered only if there is a raid to host (`InFlight` non-null, or staged results pending
   deep resolution). No party after Morning's systems → Morning's completion advances straight
   to Evening. No new `DayPhase` member, no contract edit — the enum is untouched; only the
   walk order becomes conditional. Recall during Camp still enters ExpeditionDeep (the deep
   still resolves the retreat); the collapse is strictly the *nobody-down-there* case.
2. **The adapter narrates the fold (U4):** when `PhaseCompleted(Morning)` lands in Evening,
   the ticker/card says "No one took the mine today — dusk comes early." No kernel event
   needed; the phase jump is observable client-side.
3. **Vigil finally poses its question (U4):** the phase card for Camp reads the party state
   and asks it: "They've made camp above the deep floors. Send supplies, recall them — or
   send them deeper." The bell verb becomes the truthful consequence: **"Send them deeper"**
   (or "Bring them home" when `Recalled`), replacing "Close the vigil", which the owner
   reasonably read as "end the vigil phase". Deep Vigil's card owns being a show: "They're
   beyond the bell now. Watch, if you can bear it" — with the Watch control called out.
4. **Phases are NOT merged or renamed in the kernel.** Five phases stay; Deep Vigil stays a
   distinct player-facing beat (it is the game's premise — the vigil deepens). What changes is
   that every phase now introduces itself and no empty phase costs a click.

### KTD-E — The tutorial becomes a data registry with anchors, and an overlay that points

The current tutorial is an enum ladder plus four parallel structures (`StepIndex`,
`StepText`, `StepMinDay`, `StepBuilding`) that must be edited in lockstep, rendered as one
line of text in the `ObjectiveTracker` top slot. It cannot point, cannot show progress, and
two steps complete on UI-navigation callbacks because no sim fact exists for them.

U5 converts it to **one registry of step records**: `(id, objective text, "when" text, anchor,
completion predicate, min-day, teach-card)`. Anchor is a discriminated value: a
`TownLayout2D` building id, a named HUD control, or none — and a conformance test enumerates
the registry and *resolves every anchor* (building ids against `TownLayout2D`'s table, control
names against the HUD scene) so a step pointing at nothing is a red test, not a shrug. The
overlay renders: the anchored building pulses (reusing the `Building2D.SetHighlighted` tint
path with a distinct tutorial style), HUD anchors get a pulsing outline, and the objective
list renders as a checklist with per-step checkmarks. Intermediate acts tick visibly:
entering the anchored building checks a sub-box ("Tutorial didn't update when i entered the
forge" dies here) via the existing `NotifyPanelOpened`-style hooks generalized to
`NotifyEnteredBuilding`.

Registry conversion also fixes the SET problem the investigator found: today no test
enumerates `TutorialStep` — the new conformance test enumerates the registry by construction.

### KTD-F — One re-baseline, in one labelled commit, in U1 — and nowhere else

U1's two rule changes (verb timing, phase collapse) each move the golden replay; batching
them into one PR with one labelled re-baseline commit is the serial-re-baseline discipline
applied — two separate sim PRs would mean two re-records in one week. The commit is exactly
`re-baseline: action timing + empty-phase collapse`, contains only pin/golden updates
(DeterminismTests pins, Balance suite pins, harness chronicle expectations), and its PR body
states the before/after of both rules. U2–U7 must each show `git diff --stat origin/main --
sim/` empty (CI's fast lane will also prove behavior unchanged).

### KTD-G — The gear gap graduates via a query that already exists

`RaidForecast.ForTomorrow` already computes per-hero `GearGaps` in the sim
(`RaidForecast.cs:59-68`, typed query `MissingItemSlots` at :85-104). Zero new sim. U6 adds a
badge to the HUD Forecast button showing the live gap count, a tooltip naming the gaps, and
the tutorial objective chain drives the player through one full gap-close (forecast → forge →
shelve) using real completion facts (`CraftAction`/`StockAction` events for the missing slot).
Tutorial teaches it; the badge answers "need to be able to check this if needed" forever.

---

## Implementation Units

Ordered so the root fault dies first. **If only one ships, ship U1; if two, U1 + U2.**

### U1 — The kernel answers now, or names its bell (timing + collapse + ONE re-baseline)

**Goal:** twelve verbs move to the immediate lane; the counter session becomes a live
conversation; raid phases exist only when there is a raid. The two sim rule changes and their
single re-baseline land together.

**Files:**
- Modify: `sim/GameSim/Kernel/ActionTiming.cs` — reclassify per KTD-A's table; rewrite the
  doc comment to state the new rule and name the three bell-riders and why.
- Modify: `sim/GameSim/Kernel/GameKernel.cs` — `Advance` (:182-191) becomes state-aware per
  KTD-D(1). Keep the Morning counter-hold (:184) exactly as is.
- Modify: `sim/GameSim/Harness/BaselinePlayer.cs` — the scripted policy no longer pumps "the
  two empty ticks" (:84-90); update the script and its comment to the collapsed walk.
- Create: `sim/GameSim.Tests/Kernel/ActionTimingConformanceTests.cs` — reflection-enumerate
  every concrete `PlayerAction` type in the GameSim assembly; assert each has an explicit
  entry in the test's expected-lane table (Now: 21 types, Bell: 3 types); assert
  `ActionTiming.ResolvesImmediately` agrees with the table for every type. A new action type
  or a flipped lane fails by name.
- Create: `sim/GameSim.Tests/Kernel/PhaseCollapseTests.cs` — seed-driven: (a) a day where no
  party forms walks Morning → Evening exactly (assert the full observed phase sequence, not
  one transition); (b) a day with a party walks all five; (c) recall during Camp still enters
  ExpeditionDeep; (d) determinism: same seed + same actions = identical state across the
  collapse boundary.
- Modify: `sim/GameSim.Tests/Kernel/SteppedMorningReplayTests.cs` + existing counter-session
  tests — counter verbs now resolve via `ApplyNow`; assert `CounterSaleClosed` (and the other
  session events) land in `state.EventLog` at apply time, because the tutorial reads durable
  event-log facts (`TutorialFlow.cs:530`).
- Modify (re-baseline commit ONLY): `sim/GameSim.Tests/Kernel/DeterminismTests.cs` pins,
  `sim/GameSim.Tests/Balance/*` pins, any harness chronicle expectations — one labelled
  commit per KTD-F.

**Approach:** timing first (mechanical — the switch arms move), then the counter-session
verification: `ApplyNow` uses the same handler predicates, so the session state machine needs
no change, but write the proving test before touching `Advance`. Then the collapse rule; the
condition is a pure function of post-systems state (`InFlight`, pending deep resolution) — no
RNG, no clock. Run the fast lane; expect determinism pins red; record the re-baseline as the
final labelled commit. Run the Balance category locally before pushing.

**Test scenarios:** the conformance table (all 24 types, both lanes); post a bounty via
`ApplyNow` and assert `BountyPosted` is in the event log before any tick; full counter session
(open → present → haggle → close) applied entirely via `ApplyNow` during a held Morning ends
with the same state as the golden's old bell-stepped equivalent modulo timing; the four
collapse scenarios above; `ActionLegality.IsLegal` unchanged for every (action, phase) pair —
pin by enumerating `LegalActions` per phase before/after on a fixed seed.

**Verification:** fast lane green; **Balance category green locally** (this PR moves pins);
golden replay green against the new baseline; CI green. PR body carries the before/after
phase-walk table and the lane table.

---

### U2 — One event per meaning: `ActionApplied` vs `PhaseCompleted`

**Goal:** immediate actions stop replaying phase theater. Stock never marches heroes out;
a mid-raid craft never walks them home. Independent of U1 — fixes live bugs on today's main.

**Files:**
- Modify: `godot/scripts/SimAdapter.cs` — split `StateChanged` per KTD-C: `ActionApplied`
  raised from `Queue`'s immediate branch (:133); `AdvancePhase` (:164) raises
  `PhaseCompleted` **only when the tick actually moved time** (`state.Phase` changed or the
  day rolled) and raises `ActionApplied` instead for a held tick (the Morning counter-hold at
  `GameKernel.cs:184` ticks without advancing — calling that "completed" would re-introduce
  the lie this unit exists to kill). Keep both signatures `(DayPhase, int)`.
- Modify: `godot/scripts/MainUi.cs` — `OnPhaseCompleted` (:421) subscribes `PhaseCompleted`
  only; a new `OnActionApplied` does the refresh/toast subset (panel refresh, rejection
  toasts, tutorial `Advance` — the tutorial must see immediate facts). The theater forwards
  (:517 `Town`, :520 `Interior`, :526 `Ticker`, departure beats, ledger trigger) stay in the
  `PhaseCompleted` path only. The hand guard at :1903 is superseded on its boundary-detection
  half; its other job — deciding whether a *departure* happened — moves to real evidence:
  the beat keys on the `PartyDeparted` event in `Adapter.LastEvents`, not on "Morning ended"
  (after U1's collapse a Morning can complete straight into Evening with nobody departing).
- Modify: `godot/scripts/town2d/Town2D.cs` — `DepartWanderingHeroes` (:615) derives its cast
  from the departed party's membership (`InFlight`), not "every wandering actor": today a
  partyless Morning completion would march the entire idle cast out the gate, which is
  exactly the U1 collapsed day. `ReturnSurvivors`/`SnapRemainingHeroesHome` get the same
  membership audit.
- Modify: `godot/tests/` — extend the adapter/choreography suites (locate by grep at
  execution: the SimAdapter fidelity tests and the Town2D choreography tests).

**Approach:** mechanical split, then an audit sweep: grep every `StateChanged +=` subscriber
and classify each into applied/completed/both, recording the classification in the PR body.
The known subscriber is `MainUi` only, but the audit is the point — the next `+=` is why the
split exists.

**Test scenarios:** immediate action during Morning → `ActionApplied` fired,
`PhaseCompleted` not, zero hero actors leave `Wandering` (wait on the actor-state condition,
not frames); `AdvancePhase` that advances → `PhaseCompleted` exactly once; `AdvancePhase`
against a held Morning (counter open) → no `PhaseCompleted`, panels still refresh; immediate
action during a raid phase → no `ReturnSurvivors` effect (no actor enters `WalkingIn`);
Morning completion with no party → no actor departs and no depart cue (drive with a seed
where no party forms); departure beat fires iff `PartyDeparted` is in the tick's events; the
Evening ledger still triggers exactly once per Evening completion; tutorial still advances on
an immediate craft (regression guard for the #338 suite).

**Verification:** full engine suite green (orchestrator-run, serialized); CI green; zero
`sim/` diff.

**Receipt:** screen recording or two-frame receipt — before: Stock click mid-Morning marches
the cast out; after: Stock click, heroes keep wandering. This is the plan's most
demonstrable fix.

---

### U3 — The bell tray: pending actions visible, acknowledged, cancellable

**Goal:** the three remaining bell-riders (and any future deferred verb) are never silent:
submit → acknowledgment toast naming the bell; a tray chip on the bell row; withdraw before
the tick.

**Files:**
- Modify: `godot/scripts/SimAdapter.cs` (serial after U2 — shared file) — add
  `Withdraw(PlayerAction action)` (reference-remove from `_pending`; returns bool); doc why
  withdrawal is determinism-free (the action never reaches the kernel).
- Modify: `godot/scripts/MainUi.cs` (serial after U2) — bell-row tray rendering from
  `Adapter.PendingActions` (chip per pending action: verb display name + withdraw button);
  acknowledgment toast on every deferred submit (hook the `Queue` call sites' shared path —
  the adapter's deferred branch return is the signal, not per-panel wiring).
- Create: `godot/scripts/ui/PendingVerbVocab.cs` — display name + bell-promise line per
  deferred verb ("The Guild takes your commission at the bell").
- Create: `godot/tests/BellTrayTests.cs`.

**Approach:** the tray renders the queue itself — no shadow list. Vocabulary is enumerated:
`PendingVerbVocab` must cover exactly the set of types `ActionTiming` defers; the test derives
that set from the U1 conformance table's source (reflection + `ResolvesImmediately == false`),
so a fourth bell-rider added later fails the vocab test until named.

**Test scenarios:** submit each deferred verb → tray shows exactly `PendingActions` (assert
set equality against the adapter, not a hand list); withdraw removes the chip and the pending
entry and the withdrawn action provably never applies (state unchanged after the tick);
tick clears the tray; ack toast text comes from `PendingVerbVocab` for every deferred type
(enumerated); immediate verbs never touch the tray.

**Verification:** full engine suite green (orchestrator-run, serialized); CI green; zero
`sim/` diff.

**Receipt:** frame of the tray holding a pending "Upgrade the forge" chip with its withdraw
control, plus the ack toast.

---

### U4 — Phase cards: every phase declares itself; the bell tells the truth

**Goal:** on every phase entry, one card: what is happening, what you can do (derived from
the sim's legality query), what the bell will do. Camp poses its question; Deep Vigil owns
being a show; a collapsed day narrates its fold; the continue screen reads "Resume at Dawn".

**Files:**
- Create: `godot/scripts/ui/PhaseCard.cs` — the entry card + a recall affordance (clicking
  the phase chip reopens it). Content per phase: intro line, a "you can" list derived at render
  time from `ActionLegality.LegalActions(state)` filtered to player-facing verbs and mapped
  through display vocab, bell-consequence line from `PhaseVocab.BellVerb`.
- Modify: `godot/scripts/ui/PhaseVocab.cs` — bell verbs per KTD-D(3): Camp → "Send them
  deeper" / "Bring them home" (when `Recalled`); ExpeditionDeep → "Meet them at the gate";
  keep the #339 display names (Dawn/Prepare, Quest, Vigil, Deep Vigil, Night).
- Modify: `godot/scripts/MainUi.cs` (serial after U3) — show the card on `PhaseCompleted`
  entry into each phase; collapsed-day narration when a `PhaseCompleted(Morning)` lands in
  Evening ("No one took the mine today — dusk comes early") through the card + ticker.
- Modify: `godot/scripts/NewGameSelect.cs` — continue copy "Resume at {PhaseVocab.Display} of
  day N".
- Create: `godot/tests/PhaseCardTests.cs`.

**Approach:** the card's verb list must be *derived*, never typed — `ActionLegality` is the
sim's own mirror of every handler's gates, so the card cannot drift from the rules. Keep the
card dismissible and quiet (one card per phase entry, never re-pops on refresh; recall via
the phase chip). The Deep Vigil card names the Watch control explicitly (the #335 affordance
— this is where "HOW do we watch" gets answered in-fiction).

**Test scenarios:** `Enum.GetValues<DayPhase>()` — every phase has a card with non-empty
intro and bell line (pins the set; a sixth phase fails here first); for each phase on a
driven seed, every verb line on the card corresponds to an entry in
`ActionLegality.LegalActions(state)` and vice versa for the player-facing subset
(mutation-checkable both directions); collapsed day shows the fold line and a normal day does
not; no raw enum spelling anywhere on the card (the #339 sweep extended); card re-opens from
the phase chip.

**Verification:** full engine suite green (orchestrator-run, serialized); CI green; zero
`sim/` diff.

**Receipt:** the Camp card posing the supply/recall/deeper question on screen, and the
collapsed-day fold line — two frames.

---

### U5 — The tutorial points: step registry + anchored overlay + checklist

**Goal:** the tutorial stops being a log line. Steps live in one registry with anchors and
real completion facts; the overlay pulses the actual building or control; the objective list
shows checkmarks; entering the right building visibly ticks.

**Files:**
- Modify: `godot/scripts/ui/TutorialFlow.cs` — convert the enum ladder + four parallel
  structures (`StepIndex` :461, `StepText` :225, `StepMinDay` :364, `StepBuilding` :297)
  into one step-record registry per KTD-E. Keep persistence at `user://tutorial_flow.json`;
  map existing saved step ids so mid-tutorial saves resume at the same step.
- Create: `godot/scripts/ui/TutorialOverlay.cs` — anchored pulse: building anchors via the
  `Building2D` highlight path (new tutorial style, distinct from hover); HUD anchors via a
  pulsing outline over the named control; drawn only for the active step.
- Modify: `godot/scripts/ui/ObjectiveTracker.cs` — the tutorial slot becomes a checklist
  (done steps ticked, active step highlighted, "when" text shown for day/phase-gated steps
  so "Still in tutorial 6 during the night" reads "a Morning task — rest until dawn").
- Modify: `godot/scripts/town2d/Building2D.cs` — tutorial pulse style parameter (hover tint
  untouched).
- Modify: `godot/scripts/MainUi.cs` (serial after U4) — `NotifyEnteredBuilding` hook
  generalizing the existing `NotifyMirrorOpened`/`NotifyPanelOpened` pattern; overlay
  lifecycle wiring.
- Create: `godot/tests/TutorialRegistryConformanceTests.cs`; modify
  `godot/tests/TutorialFlowTests.cs`, `TutorialAllProfessionsTests.cs`,
  `TutorialKeepsUpTests.cs` to drive the registry.

**Approach:** conversion first (behavior-identical, existing suites stay green), overlay
second. Anchor resolution is the conformance test's job: every registry row's building id
must exist in `TownLayout2D`'s table and every control name must resolve in the HUD scene at
test time — a step pointing at nothing is red. Steps 3 and 6 need no special handling once
U1 lands (their facts now exist at click time) — assert that explicitly in the scripted
drive.

**Test scenarios:** registry conformance (every step: non-empty text, resolvable anchor,
non-null predicate, valid min-day; enumerated from the registry, count pinned); the #338
three-day scripted drive still green for both professions; entering the anchored building
ticks the sub-box (condition-wait on the checklist state); overlay pulses exactly the active
step's anchor and nothing else; a day/phase-gated step displays its "when" text outside its
window; mid-tutorial save/load resumes the same step.

**Verification:** full engine suite green (orchestrator-run, serialized); CI green; zero
`sim/` diff.

**Receipt:** frame of the town with the Bounties board pulsing under the active step +
checklist with two ticks; frame of the "a Morning task" gating text during Night.

---

### U6 — Teach the budget and the tray; graduate the gear gap

**Goal:** the action budget and the bell tray are taught where they bite; the gear gap
becomes a tutorial objective chain and then a permanent badge.

**Files:**
- Modify: `godot/scripts/ui/TutorialFlow.cs` (serial after U5 — registry rows only): a
  budget step (fires the first time `ActionSlotsRemaining` drops, teaches the pips), a tray
  step (fires on first deferred submit, teaches the ack + withdraw), and the gear-gap chain
  (read forecast → forge the missing piece → shelve it), each completing on real event-log
  facts.
- Modify: `godot/scripts/MainUi.cs` (serial after U5) — pips (:766) get a tooltip naming the
  slot-consuming verbs (read from `ActionBudget.ConsumesSlot` behavior — displayed, never
  redefined; `ActionBudget` is Contracts and stays untouched); pips render in every phase,
  not just Morning; Forecast button badge showing `RaidForecast.ForTomorrow` gap count with
  a tooltip naming the gaps per hero.
- Modify: `godot/tests/TutorialFlowTests.cs`, `godot/tests/` HUD suite — badge + steps.

**Approach:** everything reads existing sim queries (`RaidForecast.ForTomorrow`,
`ActionSlotsRemaining`) — zero sim edits. The gear-gap chain uses the forecast the player can
actually open (Forecast button) as its anchor, completing each link on the matching event
(forecast opened → hook; craft of an item filling a named missing slot → event-log fact;
`StockAction` of that item → event-log fact).

**Test scenarios:** badge count equals `RaidForecast.ForTomorrow` gap total across a driven
multi-day session (assert per day, both professions); badge disappears at zero gaps;
gear-gap chain completes on the real events and cannot complete out of order; budget step
fires on first slot spend; pips visible in every phase; tooltip lists exactly the
slot-consuming verb set (derived by probing `ActionBudget.ConsumesSlot` over all 24 action
types via the U1 reflection idiom — pins the set without editing Contracts).

**Verification:** full engine suite green (orchestrator-run, serialized); CI green; zero
`sim/` diff.

**Receipt:** frame of the Forecast badge showing "2 gaps" with tooltip; frame of the
gear-gap objective chain mid-progress.

---

### U7 — The ledger reads like a reward

**Goal:** the Evening Ledger's cards get real typography and visuals (portraits/icons,
loot lines, death lines styled apart); one tutorial line establishes what the ledger is.

**Files:**
- Modify: `godot/scripts/panels/LedgerModal.cs` — card layout: hero portrait/class icon via
  `IconRegistry`, loot/ore lines with item icons, survivor vs death styling, section boxes.
- Modify: `godot/scripts/ui/TutorialFlow.cs` (one registry row; serial after U6 if
  concurrent) — first Evening ledger gets a "this is the day's story — read it" line.
- Modify: `godot/tests/` ledger suite (locate by grep near `LedgerModal`).

**Approach:** render from `LedgerQuery.ReturnCards` unchanged (sim untouched). Every icon
lookup asserts non-null for the known cast in tests — no null-tolerant silent fallback (house
rule: committed assets must not be invisible with no warning).

**Test scenarios:** driven day with survivors + a death + loot → cards render with resolved
portraits and icons (assert non-null textures for every card, enumerated from
`ReturnCards`' output, not a hand list); empty day renders the empty state, not a blank
modal.

**Verification:** full engine suite green (orchestrator-run, serialized); CI green; zero
`sim/` diff.

**Receipt:** before/after pair of the same day's ledger.

---

## Dependencies & parallelism

```
U1 (sim, re-baseline)  ──────────────►  U3 ──► U4 ──► U5 ──► U6 ──► U7*
U2 (adapter, independent) ──► U3            (U7 only serialized if TutorialFlow row
                                             lands while U5/U6 in flight)
```

- **U1 and U2 start immediately, in parallel** — disjoint (sim vs adapter), and U2 fixes
  live bugs regardless of U1's outcome.
- **U3 after both U1 and U2**: needs the final deferred set (U1) and shares
  `SimAdapter.cs`/`MainUi.cs` with U2.
- **U4 after U3, U5 after U4, U6 after U5** — the `MainUi.cs` serial chain; each is
  otherwise small. U5 additionally needs U1 (steps 3/6 unstick) and U4 (cards/vocab the
  steps reference).
- **U7 is file-disjoint** (LedgerModal) except its single TutorialFlow row — it may run any
  time, landing its tutorial row after U5's registry conversion (or as a follow-up commit).
- **Merging is serial across ALL units** — every godot unit needs the engine suite, engine
  runs are one at a time, orchestrator-run (constraint 4). Implementing agents never run it.
- **Re-baseline discipline:** U1 merges before any other sim-touching work anywhere in the
  repo resumes; everything else here is sim-diff-zero by construction.

## Verification contract

| Unit | Fast lane | Balance gate | Engine suite (orchestrator) | Zero sim diff | Receipt |
|---|---|---|---|---|---|
| U1 | required | **required (pins move — one labelled commit)** | not needed (sim-only) | n/a | lane table + phase-walk table in PR body |
| U2 | required | not touched | required | required | before/after: Stock no longer marches heroes |
| U3 | required | not touched | required | required | tray chip + ack toast frame |
| U4 | required | not touched | required | required | Camp question card + fold line frames |
| U5 | required | not touched | required | required | pulsing anchor + checklist frames |
| U6 | required | not touched | required | required | Forecast badge + objective chain frames |
| U7 | required | not touched | required | required | ledger before/after pair |

Every godot test in this plan waits on its condition (actor state, checklist state, card
visibility), never a frame count. Every enumerable surface is pinned from its registry:
`PlayerAction` types by reflection (U1, U3, U6), `DayPhase` by `Enum.GetValues` (U4), tutorial
steps by the registry itself (U5), ledger cards by `ReturnCards` output (U7). Known flaky
pre-step unchanged: engine suite reporting ~54 tests → kill stray Godot processes, rebuild
headless, re-run; `git restore -- '*.import'` before staging.

## Scope boundaries (deliberately deferred)

- **No Contracts edits.** `DayPhase`, `GameEvent`, `PlayerAction`, `ActionBudget` all
  untouched. If open questions 1 or 4 resolve toward semantics changes (e.g. budget rework),
  that is an orchestrator micro-PR first, then a follow-up plan.
- **No new phases, no phase renames, no phase merges.** The five-phase kernel stands; this
  plan makes it legible, not different (KTD-D(4)).
- **Not redesigning Mirror/MineWatch content** — U4's Deep Vigil card points at the Watch
  control; the content itself is still awaiting the owner's first real viewing verdict.
- **Not rebalancing anything.** U1's re-baseline records the timing/collapse consequences;
  it does not tune numbers. If the collapse shifts balance metrics beyond pins, that is
  evidence for the owner, not a knob to quietly turn.
- **Not building a general quest/objective system.** The tutorial registry is tutorial-only;
  if it later wants to be the quest spine, that is its own plan.
- **Counter/haggle UX content** (what the hero says, offer flow) untouched — only its timing
  moves. Same for commissions.
- **`AppliedThisPhase` / `LastEvents` accumulation semantics in `SimAdapter` untouched** —
  U2 splits the notification, not the record.
- **Plans-index rule:** the commit landing this document adds its row to
  `docs/plans/README.md` (LIVE table), per that file's rule 2.

## Open questions for the owner

1. **The three bell-riders (KTD-A).** UpgradeForge / SetProfessions / CommissionLegendaryWork
   stay on the bell as deliberate ceremony — now visible and cancellable. Right set? Or
   should everything just resolve now and the bell be purely "end the phase"?
2. **The collapsed day (KTD-D).** When nobody raids, default is a silent fold to dusk with
   one narrated line. Alternative: a brief beat (2–3 s of the quiet town) before Evening.
   Which reads better at the bell?
3. **Deep Vigil's existence.** This plan keeps it as the announced watch-phase ("they're
   beyond the bell — watch"). The alternative was merging it into one Vigil with a depth
   marker. If the card treatment still doesn't land at the next playtest, the merge is the
   fallback — say the word.
4. **Withdrawing SetProfessions.** Cancellable like the others, or should identity lock at
   submit?
5. **Overlay intensity (U5).** Default is a pulse on the anchored building/control. The
   heavier option — dim the world, spotlight the target — is one flag in `TutorialOverlay`.
   Taste call at the receipt.
6. **Gear-gap badge home (U6).** Default: count badge on the existing Forecast button.
   Alternative: its own HUD chip next to the pips. Placement is one line either way.

## Definition of done

1. Every one of the 24 verbs, clicked in a legal moment, produces a visible answer within
   one frame — twelve newly-immediate ones by state change, three bell-riders by ack + tray
   chip. The sentences "posting the bounty does nothing" and "open counter does nothing"
   cannot be written about this build.
2. Stocking an item never moves a hero. No immediate action fires phase choreography — pinned
   by tests on the split events.
3. A day nobody raids is Morning → Evening with one narrated fold line. The kernel never
   walks an empty raid phase again, and the golden replay pins the collapsed walk.
4. Every phase entry states what is happening, what you can do (derived from
   `ActionLegality`, tested both directions), and what the bell does; the Camp bell says
   "Send them deeper".
5. The tutorial points at the actual thing, ticks checkmarks (including on entering the
   right building), explains its own gating windows, teaches the budget and the tray, and
   walks one gear gap from forecast to shelf; the Forecast badge answers gear gaps forever
   after.
6. Exactly one re-baseline commit exists across the whole plan, labelled, in U1.
7. Every PR carried its receipt; every engine run was orchestrator-serial; U2–U7 show zero
   `sim/` diff.
