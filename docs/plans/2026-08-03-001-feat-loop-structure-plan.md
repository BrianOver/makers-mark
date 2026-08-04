---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
type: feat
title: Loop structure — the day becomes two acts and one answer
date: 2026-08-03
origin: owner playtest notes 2026-08-03 (fifth full playtest; third repetition of "the base loop STILL is not complete")
roadmap: docs/plans/2026-07-28-003-roadmap-post-skeleton.md
---

# Loop structure — the day becomes two acts and one answer

## Verdict

**Yes, the loop is structurally incomplete, and what is missing is the answer half of it: the
player acts in exactly one phase (Morning holds ~20 of the 24 verbs), and everything those acts
cause is computed invisibly inside two instantaneous ticks and paid back as a wall of text at
Night — while the player hand-cranks three named phases in which they can neither decide anything
nor watch anything happen.** The kernel's five-phase staged resolution is fine as a *reveal-gating
mechanism*; presenting each of its internal ticks as a player-rung, player-named phase is the
structural error. Three playtests of bug-fixes have not moved the feeling because the bugs were
never the problem: the day is shaped as a five-rung bell ladder when it is, in truth, two acts
(run your shop / read your night) with a raid in between that nobody can currently experience.
The smallest change that completes the loop is to stop asking the player to crank the middle:
**the raid span plays itself as a short watchable show and pauses exactly once, on the one real
question it contains (the camped party) — leaving the player two bells, both attached to
decisions they actually made.**

This plan is deliberately **zero-sim-diff** (no contracts edits, no re-baseline). Every unit is
adapter-side, because the structure that is wrong is the *presentation* of the phase machine, not
the phase machine.

---

## 1. Evidence: the day, phase by phase — what is asked, what comes back

The kernel walks Morning → Expedition → Camp → ExpeditionDeep → Evening
(`sim/GameSim/Kernel/GameKernel.cs:188-198`). The player sees SIX names for these five states —
Dawn, Prepare, Quest, Vigil, Deep Vigil, Night (`godot/scripts/ui/PhaseVocab.cs:27-45`; Morning
splits into Dawn/Prepare on the counter sub-state) — which is itself evidence: the vocabulary
pass (#339) polished names onto a structure whose real shape is not six, not five, but two-and-a-
show.

| Phase (shown as) | Player decisions here | What the player gets back | Verdict |
|---|---|---|---|
| Morning (Dawn/Prepare) | ~20 of 24 verbs: craft, buy, stock, price, the whole counter session, commissions, bounty, all four gold sinks, talents, professions (`sim/GameSim/Advisor/ActionLegality.cs:50-77`) | Immediate state changes (post-#358), muster forecast, commissions | **The game.** Decision + feedback both live here. |
| Expedition (Quest) | **Zero phase-specific verbs** (only the phase-agnostic craft/stock family stays legal) | Departure choreography at entry; then a bell labeled "Lower them into the mine" (`PhaseVocab.cs:76`) | Decision-free. The bell label is inverted — see below. |
| Camp (Vigil) | SendSupply / RecallParty — but **only if** a party cleanly cleared floor 1 and parked (`ActionLegality.cs:61-62`; checkpoint depth is 1, `sim/GameSim/Expedition/ExpeditionSystem.cs:26`); any stage-1 halt (gate / wipe / too-hurt / floor-lost) finalizes instead and Vigil is empty (`ExpeditionSystem.cs:96-99`) | The camp slate when someone parked; otherwise a bell reading "Close the vigil" | A real question **the screen never poses**, in a phase that is frequently about nothing. |
| ExpeditionDeep (Deep Vigil) | **Zero.** No legal phase-specific verb, and the deep tick emits zero events — results stage for the Evening reveal | The Mirror's delve show (#355), if the player finds it; a bell labeled "Ring the return bell" (`PhaseVocab.cs:78`) that is actually just "advance" | Decision-free AND feedback-silent by construction. |
| Evening (Night) | BuyOre (1-day-lag offers), PostBounty, HonorMemorial (`ActionLegality.cs:56-65`) | **The entire day at once**: the reveal system applies deaths/gold/ore, the ticker floods, the return ritual and ledger fire (`godot/scripts/MainUi.cs:760-767`) | Decisions exist; feedback arrives as a crushed wall. |

**Why "lower into the mine has them return" happens — and why it is not a bug.** The Expedition
tick resolves the whole of stage 1 *at departure* (`ExpeditionSystem.cs:87-106`): parties that
halt at floor 1 finalize into `PendingExpeditions` in the same instant. The bell the player rings
to end Quest is labeled "Lower them into the mine" — and ringing it runs that tick, completes the
phase, and `Town2D.OnPhaseCompleted(Expedition)` walks the finalized parties' survivors home
(`godot/scripts/town2d/Town2D.cs:802-827`). The button that says *down* visibly produces *out*,
because between "they went down" and "they came back" the sim let no time pass. #353 fixed the
replay-on-immediate-action bug; this remains because it is the honest rendering of the structure.
No relabel can fix a label whose referent is an instantaneous round-trip.

**Why Vigil reads as nothing.** Two reasons, both structural. (a) The question it holds
(supply / recall / press deeper) is only real when a party parked, and the screen poses it only
as two buttons in a modal plus a bell whose label ("Let them press deeper" / "Close the vigil",
`PhaseVocab.cs:77`) is the third option wearing an end-the-phase costume. (b) The answer to
whatever the player chooses arrives two bells later, inside the Night text flood — the vigil's
decision and its consequence never meet on screen.

**The empty-phase collapse shipped as dead code.** #358's `NoRaidToHost` collapse
(`GameKernel.cs:216-217`) fires only when `PartyFormation.FormParties` returns empty — and its
own doc comment concedes every living hero always joins some party, so it collapses the day only
when *the entire roster is dead*. The common empty case — no party parked, so Camp and
ExpeditionDeep are both about nothing — still walks both phases and costs two bell presses. The
last plan's R6 ("empty phases don't take the player's time") was satisfied by the letter and not
the spirit. This plan retires that debt without touching the kernel: under the conductor (U1) an
empty Camp/Deep costs ~a second of show, not a click.

**Why the counter dies after "Suggest".** `CustomerApproached` carries a bare `HeroId`
(`sim/GameSim/Contracts/Events.cs:182`) — the customer walks up and says nothing. The player
must *guess* what to present; a wrong Present resolves instantly to a walk-away
(`sim/GameSim/Counter/CounterQueueSystem.cs:83-92`). Suggest bumps an Interest integer and emits
no event at all — `ApplySuggest` doesn't even take an event sink
(`sim/GameSim/Counter/CounterHandlers.cs:122-140`). So the observed session — "customer appears,
pressed Suggest, interest went up, nothing happened" — is the system working exactly as built:
the player initiates everything, the customer initiates nothing, and the only next move
(Present) is a guess with an instant-fail penalty. The 07-27 measurement reached the same place
from the economics side: the counter underperforms the invisible atomic pass ~10× and "is
currently worse than not opening it" (`docs/design/2026-07-27-gameplay-loop-analysis.md` §1, §8.1).

**Why the forge minigame is "too long" for the third time.** Both fixes (#334, and the
drift-back fix before it) moved constants, measured honestly — and the complaint survived
because its cause is not a constant: the Anvil Map is a fill-a-meter-to-1000 *distance* design
(`godot/scripts/minigames/ForgeMinigame.cs:114`, `StrikeBaseAdvancePermille`, ~17-24s per craft),
and it is **mandatory per craft**. With ~3-4 crafts a day inside a 5-slot budget, the player
replays an identical ~20-second labor loop many times per session; the third "too long" is the
sound of repetition, not of any one run's duration. (U3 kills the repetition without touching
the sim-side scorer; resizing the track itself is a `ForgePath`/`ForgeScorer` re-baseline
decision and is explicitly deferred — see Scope boundaries.)

**Why the forge stations "all do the same thing."** They do: every station press opens the one
`ForgePanel` and calls `FocusSection` to scroll-and-flash a section of it
(`godot/scripts/MainUi.cs:2766`, `godot/scripts/panels/ForgePanel.cs:223`). Honest stations
(#349) made the focus honest; it did not make the stations *distinct*. Twice-raised, cheaply
fixed (U4).

## 2. Is the phase structure itself wrong? — the comparison

The kernel's five phases are a sound *resolution* design (staged resolution with a decision
checkpoint; the RNG-draw ordering depends on it). What is wrong is that the client presents
every kernel tick as a player-operated phase. The comparators all agree on the shape this game
is reaching for and missing:

- **Recettear** (closest analogue): the day is exactly two modes — run the shop, or watch/route
  an adventurer through a dungeon. In shop mode the *customer initiates*: they walk to an item
  or to the counter and ASK; the player's whole verb set is answering (price it, haggle it).
  There is no timeslice where the player can neither decide nor watch.
- **Moonlighter**: shop by day, dungeon by night — two acts, both operated. Never a third act
  that operates itself but demands your button presses anyway.
- **Stardew Valley**: one continuous day; "phases" are clock milestones that pass through the
  player's activity, never gates the player must crank. Crafting is instant; the day itself is
  the time-allocation puzzle.
- **Potion Craft**: the customer states the want, every time; the minigame (brewing) IS the
  content, and each customer is one complete call-and-answer beat.
- **Spiritfarer**: the day rhythm is request → provide → the spirit visibly reacts — the world
  takes turns with you, and its rituals (the Everdoor) are ceremonies you attend, not meters you
  advance.

Against that canon, Maker's Mark's day: Morning is Recettear's shop mode and works. The raid
span is Recettear's dungeon mode with the watching removed and the button-pressing retained —
three bells that each mean nothing ("what does pressing this decide?" has no answer at Quest or
Deep Vigil, and only sometimes an answer at Vigil). And the counter inverts Recettear's customer
model: the shopkeeper guesses, the customer judges silently.

One history note that shapes the fix: the project already tried a *global* auto-advancing day
(the U15 "living clock", ON by default) and the owner rejected it — it timed Morning at 45s and
put a clock on the shopkeeping (`godot/scripts/PhaseClock.cs:38-45`, the "Ring the Bell"
reversal). That verdict was right and is not being relitigated. The error was auto-advancing the
phases where the player ACTS. This plan auto-advances only the span where the player literally
cannot act — the opposite half of the same dial.

## 3. Where the craft→legend chain actually breaks

Traced end to end, every link EXISTS as data:

1. Craft → item carries the maker's mark (`sim/GameSim/Contracts/Items.cs:26,106`), sub-scores,
   modifiers.
2. Stock → shelf → sale: the atomic shopping pass (dominant, invisible, mid-tick) or the counter
   (a guessing game, §1). `ItemSold` narrates on the ticker
   (`godot/scripts/ui/AdventureTicker.cs:132-134`).
3. Equip → raid → deed: `AttributionEngine` gates beats to player-crafted items;
   `AttributionBeatEvent(Beat, Item, Hero, Floor, Detail)` (`Events.cs:77`).
4. Deed → surface: the Evening ticker line "Home safe: {item} — {detail}"
   (`AdventureTicker.cs:151-152`), ledger cards (#362), gossip, LegendsWall, ProvenanceCard,
   signing, heirloom reforge.

**No link is missing. The chain is broken as an *experience*, in two places:**

- **Temporally crushed.** Links 2 and 3 both resolve inside invisible instants (the Morning
  tick's shopping pass; the Expedition/Deep ticks), and link 4 lands entirely inside the Night
  flood — one line among a dozen, in the same breath as deaths, gold, rent and gossip. Cause and
  effect are never on screen at the same time, so "why did I do that" has no witnessed answer.
  The 07-25 audit named this "feedback shadow"; it is still the accurate name.
- **The weakest single link is the sale-to-deed handoff.** The moment a hero walks out wearing
  your work is the loop's hinge — and it is precisely the span the player currently spends
  hand-cranking empty bells. The game computes "your blade turned the killing blow" and then
  spends zero seconds of stage time on it.

The fix is not more legend machinery (the roadmap's §3 ruling stands untouched) — it is giving
the middle of the day back to the player as the *show of their own consequences* (U1) and
letting the ledger lead with the mark (U5).

## 4. Ranked options

Ranked by felt-improvement per implementation cost. **Recommendation: Option A.** It is the only
option that changes the answer to "what is this phase for?" at every point in the day, and it is
adapter-only.

| # | Option | Felt improvement | Cost | Verdict |
|---|---|---|---|---|
| **A** | **The two-bell day (raid-as-show conductor + the vigil interrupt)** — U1 below | Kills all three dead bells, kills the inverted labels, stages the raid as the visible answer to Morning's work, poses Vigil's question as an actual question | Medium-low: the show pieces already exist (send-off #335, delve watch #355, return ceremony #374, camp slate V7a); this builds the conductor that sequences them | **SHIP** |
| B | Kernel phase-merge (collapse the enum walk to Morning→Raid→Evening in the sim) | Same felt outcome as A, no more | High: re-baseline, save-envelope compat, `Contracts/` adjacency, harness rewrites — for a result A reaches without touching the sim | Loses. A delivers the identical player experience at a fraction of the risk; if A lands, B is unnecessary forever. |
| C | Counter overhaul alone (customer states wants, Suggest answers) | Fixes the sharpest single symptom; leaves the bell ladder intact | Low-medium | Loses as the headline — the owner's complaint is the LOOP, not the counter. Ships anyway as U2, because the counter is the loop's Morning face. |
| D | Finish the 08-02-003 plan's U4 phase cards ("every phase declares itself") | Explains each phase better | Low | Loses. A card on Deep Vigil explains a phase that shouldn't be asking for the player's button in the first place — declarative text on a wrong structure. The card *content* (the Camp question, the truthful bell verbs) survives inside U1's interrupt and show captions. |
| E | Cut the counter minigame | Removes a trap (the 07-27 measurement is real) | Low | Rejected. The counter is the game's only face-to-face beat and the natural home of the "customer asks, smith answers" fiction the comparators prove out. Its economics being redundant with the atomic pass is fixable by making it the *aimed* channel (U2); cutting it would delete the loop's best future surface to save fixing its worst present one. |

## 5. Key Technical Decisions

### KTD-A — The two-bell day: the bell means "I decided", the show means "the world answers"

The day keeps five kernel phases and gains a presentation contract:

> **A phase is player-operated iff the player has phase-specific verbs in it. Otherwise it is
> part of the show, and the show runs itself.**

Applied: Morning and Evening keep their bells ("Send them off" / "Snuff the lanterns") — both
label real commitments. Expedition, Camp and ExpeditionDeep lose their bells entirely; a
**conductor** in the client auto-ticks them on choreography-completion beats (never raw
wall-clock durations — each beat ends when its show element reports done, with a pinned max).
The bell row during the span shows the day-timeline plus one control: **Hurry** (skip to the
next stop). The three bell-riding verbs still flush on whichever tick fires next; the bell tray
(#372) renders them unchanged.

**One stop, one question:** after the Expedition tick, if any party parked (`state.InFlight`
non-empty), the conductor STOPS — indefinitely, no timer — and poses the vigil as a modal built
from the existing camp slate (V7a `CampPanel`, `MainUi.SyncCampModal` at `MainUi.cs:821`): the
party's HP/heals facts plus exactly three verbs — **Send supplies** / **Bring them home** /
**Send them deeper**. The third button ticks Camp; the phase bell stops cosplaying as a fourth
option. If nobody parked, there is no stop and no modal — Camp and Deep tick through inside the
show (~a beat each), which retires the empty-phase debt (§1) with zero kernel edits.

**Why this is not the rejected living clock:** U15 timed the phases where the player works and
was rightly killed for it (`PhaseClock.cs:38-45`). This conductor never times a phase with a
decision in it: Morning and Evening remain fully player-decided, and the one mid-span decision
point stops the world without a timer. Auto-advance in the old global sense stays exactly as it
is (opt-in escape hatch).

**Determinism:** the conductor calls the same `PhaseClock.AdvanceNow`/`SimAdapter.AdvancePhase`
path the bell does, with whatever actions are pending — identical replay format, identical RNG
draw order, zero sim diff.

### KTD-B — The customer speaks first

`ShoppingAi.EvaluateItem` and the gear-gap query (`RaidForecast`/`MissingItemSlots`) are pure,
sim-side, and callable read-only from the adapter — the exact precedent `ForgeMinigame` uses for
its preview scoring. On `CustomerApproached`, the counter renders a **want line** derived from
the active hero's own state: empty/weakest slot, class, and purse ("Looking for a shield —
about 45g on me."). Present becomes *answering a stated request*; Suggest gets a spoken reply
derived from the same evaluation ("A blade too? …I do lack one." / "No use for that."), so the
meter movement the owner saw becomes a conversation beat he can hear. No new events, no
contracts edits — the client reads state it already has. (The sim's verdict logic is unchanged;
this is legibility, not economics. If the counter still underperforms the atomic pass after it
speaks, THAT becomes a future economics question — measured, not assumed.)

### KTD-C — Skill once, labor never: "forge another like it"

The minigame stays the only road to high grades (that is its job — quality is deliberately
unaimable without it, 07-27 analysis §5.6). What dies is the mandatory replay: after a
minigame-crafted item, `ForgePanel` offers **"Forge another like it (grade ~N)"** for the same
recipe+material, which re-queues a `CraftAction` carrying the same captured trace
(`CraftAction.Puzzle` rides the action log by design — KTD4). Same materials, same slot cost,
same sim scoring path; the player's demonstrated skill is reused, their time is not. Re-playing
the minigame to beat your own grade stays one click away. Adapter-only; the golden trace never
contained client minigame runs, so nothing re-baselines.

### KTD-D — The night leads with the mark

The Evening reveal keeps its mechanics; the *ordering* changes. The return ritual and ledger
open on the player's consequences first: any `AttributionBeatEvent` of the day gets the leading
card slot ("Emberbite turned the killing blow on floor 3 — Torvald lives"), sale-and-deed lines
are grouped under the item they belong to, and only then the day's remaining news. One surface
(LedgerModal + return beat), no new queries — `LedgerQuery`/`Adapter.LastEvents` already carry
everything needed.

---

## Implementation Units

Ordered by structural weight. **If only one ships, ship U1.** Every unit: zero `sim/` diff
(CI-checked), conventional commits, one branch (`feat/uN-slug`) = one PR, receipts per the
make-it-visible discipline, engine suite orchestrator-run and serialized.

### U1 — The two-bell day: the raid conducts itself, and stops on the question

**Goal:** Expedition/Camp/ExpeditionDeep stop being player-cranked. One conductor sequences
send-off → stage-1 tick → (vigil stop iff a party parked) → deep tick → delve/return show →
Evening. The bells that remain are Morning's and Evening's.

**Files:**
- Create: `godot/scripts/RaidConductor.cs` — plain C# (PhaseClock's testability idiom), driven
  from `MainUi._Process`. States: `Idle → SendOff → Stage1Tick → VigilStop? → DeepTick →
  Homecoming → Idle`. Beat transitions wait on choreography-completion conditions (departure
  walk done, camp modal closed, Mirror show done) with pinned max durations; `Hurry()` jumps to
  the next stop. Ticks via the existing `PhaseClock.AdvanceNow` path only.
- Modify: `godot/scripts/MainUi.cs` — bell button hidden while the conductor owns the span
  (`_advance` at :1547 renders Hurry instead); `SyncCampModal` becomes the conductor's
  `VigilStop` (modal gains the third verb, "Send them deeper", wired to the Camp tick);
  conductor start on `PhaseCompleted(Morning)` when the phase actually advanced.
- Modify: `godot/scripts/ui/PhaseVocab.cs` — `BellVerb` cases for Expedition/Camp/
  ExpeditionDeep become the Hurry caption ("Hurry the day along"); "Lower them into the mine"
  and "Ring the return bell" die here.
- Modify: `godot/scripts/panels/CampPanel.cs` — third verb + the question copy ("They've made
  camp above the deep floors. Send supplies, bring them home — or send them deeper.").
- Modify: `godot/scripts/ui/TutorialFlow.cs` — registry rows only: any step copy referencing
  the retired bells; a one-row teach for the vigil stop the first time it fires.
- Create: `godot/tests/RaidConductorTests.cs`; modify the click-through sweep (#375) and any
  suite that rings the three retired bells.

**Approach:** conductor first with the modal stop stubbed (auto-continue), engine-suite green;
then the vigil stop; then bell-row/PhaseVocab swap last so every intermediate commit renders a
coherent day. All waits are on conditions, never frame counts (house rule).

**Test scenarios:** a day with a parked party stops exactly once and stops indefinitely (drive
past the max-beat duration, assert no tick until the modal answers); a day with no parked party
reaches Evening with zero player input after the Morning bell and never shows the modal; Hurry
from every beat lands at the next stop, never skipping the vigil stop; recall during the stop
still enters ExpeditionDeep; pending bell-rider actions flush on conductor ticks and the tray
empties; the retired bell labels appear nowhere (sweep the rendered bell text across all five
phases); Morning counter-hold unaffected (conductor never starts while Morning holds).

**Verification:** fast lane green; full engine suite green (orchestrator); zero `sim/` diff;
CI green. **Receipt:** screen recording — one full day: Morning bell, the raid plays itself,
the vigil modal stops it, Evening arrives. This is the plan's demonstrable thesis.

---

### U2 — The customer speaks first

**Goal:** every counter customer opens with a stated want; Suggest and Present get spoken
answers. The guessing game dies.

**Files:**
- Create: `godot/scripts/ui/CustomerVoice.cs` — pure functions: want line from (hero gear gaps,
  class, gold), reply lines for Suggest/Present outcomes, all derived by calling the sim's own
  pure evaluators read-only (`ShoppingAi.EvaluateItem`, the gap query) — never a second rule set
  (the ForgeMinigame preview precedent).
- Modify: `godot/scripts/panels/CounterPanel.cs` — speech bubble on the active-customer card
  (reuses `BuildSpeechBubble`, :329); Suggest handler renders the reply; the desk mat caption
  names the want.
- Modify: `godot/tests/CounterPanelTests.cs` + new `CustomerVoiceTests.cs`.

**Approach:** want line first (it alone converts Present from guess to answer), replies second.
Enumerate the line tables from the sim's own verdict enums — every `ShoppingVerdictKind` must
have a reply (pinned by test), so a new verdict kind fails loud.

**Test scenarios:** for a driven session, the want line names a slot the hero's own gap query
reports and a budget equal to the hero's gold; presenting the wanted slot at a fair price opens
a round (never a walk) on the same fixtures the sim tests use; every verdict kind renders a
non-empty reply; Suggest on a fitting empty slot renders the interested reply and the Interest
chip moves in the same refresh; no active customer → no bubble.

**Verification:** fast lane green; engine suite green; zero `sim/` diff. **Receipt:** frame of
the customer stating a want + frame of the Suggest reply.

---

### U3 — Forge another like it (skill once, labor never)

**Goal:** the minigame is played to SET your grade, not to re-earn it per unit. Repeat crafts of
the same recipe+material reuse the day's captured trace at one click.

**Files:**
- Modify: `godot/scripts/panels/ForgePanel.cs` — after a minigame craft resolves, the recipe row
  gains "Forge another like it (grade ~N)" re-queueing a `CraftAction` with the same
  `Puzzle` trace; legality mirrors the same gates as a fresh craft (materials, slots).
- Modify: `godot/scripts/MainUi.cs` — per-recipe last-trace memory (session-scoped, adapter-side;
  never in the sim save).
- Modify: `godot/tests/ForgePanelTests.cs`.

**Approach:** store the exact `ForgeTraceInput` the minigame emitted; the sim scores the repeat
identically by construction (deterministic scorer; RNG jitter drawn from the kernel stream as
for any craft). The button's shown grade is the same read-only preview the minigame already
computes.

**Test scenarios:** repeat craft consumes materials + a slot and lands within the scorer's
jitter band of the original; button absent before any minigame craft, absent for a different
recipe/material, disabled at zero slots/materials with the mirrored reason; auto-craft (no
minigame) never arms it.

**Verification:** fast lane green; engine suite green; zero `sim/` diff. **Receipt:** frame of
the armed button; two ledger entries proving one minigame run produced two crafts.

---

### U4 — Stations open their own slice

**Goal:** a forge station press opens ONLY its section — a scoped card titled by the station —
not the whole workshop scrolled-and-flashed. Two rooms answered when the owner asked twice.

**Files:**
- Modify: `godot/scripts/panels/ForgePanel.cs` — `FocusSection` (:223) gains a section-only
  render mode (other sections not built, not just not-scrolled-to); title = station name; a
  "whole workshop" affordance remains one click away.
- Modify: `godot/scripts/town2d/InteriorLayout2D.cs` — station specs pass the mode.
- Modify: `godot/tests/` ForgePanel/interior suites.

**Test scenarios:** each station spec resolves to a distinct non-empty section (enumerated from
the layout table, not hand-listed); section-only mode renders exactly one section's controls;
the whole-panel path is unchanged for the door/hotkey entry.

**Verification:** fast lane green; engine suite green; zero `sim/` diff. **Receipt:** two
frames — two different stations showing two different cards.

---

### U5 — The night leads with the mark

**Goal:** the Evening reveal opens on the player's consequences: attribution beats first,
sale-and-deed grouped by item, then the rest of the day's news.

**Files:**
- Modify: `godot/scripts/panels/LedgerModal.cs` — leading "Your work" section rendered from the
  day's `AttributionBeatEvent`s (item name, deed, bearer, floor), before the hero cards.
- Modify: `godot/scripts/MainUi.cs` — the return-ritual beat surfaces the day's single
  strongest beat line (if any) as the homecoming caption.
- Modify: `godot/tests/LedgerModalTests.cs`.

**Approach:** ordering and grouping only — `LedgerQuery`/`Adapter.LastEvents` already carry the
data; no new sim queries. Empty-beat days render no section (never an empty header).

**Test scenarios:** a driven day with a beat leads with it (assert the first rendered section);
a beatless day leads with hero cards; every beat line names an item that is player-marked
(enumerate from the event, assert `PlayerCrafted`).

**Verification:** fast lane green; engine suite green; zero `sim/` diff. **Receipt:**
before/after pair of the same day's ledger.

---

## Dependencies & parallelism

```
U1 (conductor)  ──►  U5 (return-beat caption touches MainUi after U1)
U2 (counter)    — independent
U3 ──► U4 (both edit ForgePanel — serialize; either order)
```

U1, U2, U3 can start immediately in parallel (disjoint files except MainUi — U2 does not touch
MainUi; U3's MainUi touch is a field + wiring, coordinate merge order with U1, U1 first).
Merging is serial across all units (engine suite is one-at-a-time, orchestrator-run).

## Verification contract

| Unit | Fast lane | Engine suite (orchestrator) | Zero sim diff | Receipt |
|---|---|---|---|---|
| U1 | required | required | required | full-day recording: two bells, one stop |
| U2 | required | required | required | want line + Suggest reply frames |
| U3 | required | required | required | armed repeat button + double-craft ledger |
| U4 | required | required | required | two stations, two different cards |
| U5 | required | required | required | ledger before/after |

Every Godot test waits on its condition, never a frame count. Every enumerable surface pins its
set from a registry (verdict kinds by enum, stations by the layout table, beats by the event
stream). No unit runs `dotnet test godot/tests` itself.

## Scope boundaries — what this plan is deliberately NOT proposing

- **No kernel or `Contracts/` change of any kind.** No `DayPhase` member added, renamed, merged
  or reordered; no new events, actions, or `ActionBudget` semantics; **zero re-baselines**. The
  five-phase staged resolution stays exactly as it is — this plan changes who operates it.
- **Not resizing the Anvil Map track.** If "too long" survives U3's repetition-kill, the next
  lever is sim-side (`ForgePath` length / scorer weights) and that is a deliberate, single
  re-baseline decision for its own micro-plan — not a side effect here.
- **Not touching counter economics.** U2 makes the counter legible; whether it should also
  out-earn the atomic pass is a post-measurement question (07-27 analysis §8.1), not a guess.
- **No legend sifter.** The roadmap §3 ruling ("retire the specced module; keep the promise")
  stands; U5 is ordering, not machinery. If legends still read as logs after U1+U5 put them on
  stage, revisit via the roadmap, not here.
- **Not resurrecting the global living clock.** Morning and Evening stay bell-operated forever;
  the opt-in auto mode is untouched.
- **Not shipping 08-02-003's U6** (budget pips/gear-gap badge teaching) — still worthwhile,
  still queued, but it is legibility polish, not loop structure; it should not dilute this wave.
- **Not building a general event-priority system** for the Night flood — U5 hand-orders one
  modal. If more surfaces need it later, that is its own small design.
- **Plans-index rule:** the commit landing this document adds its row to
  `docs/plans/README.md` (LIVE table), per that file's rule 2 — done in this commit.

## Open questions for the owner

1. **The vigil stop when nothing parked** — U1 ticks straight through with a one-line caption
   ("They never made the deep floors — the story comes home at dusk"). Alternative: a 2-3s hold
   on the gate. Which reads better is a taste call at the receipt.
2. **Hurry granularity** — Hurry jumps beat-by-beat (default) or straight to Evening? Default
   is beat-by-beat so the vigil stop can never be skipped past unseen.
3. **"Forge another like it" fiction** — reuse at the exact captured trace (default), or apply
   a small grade decay so the original stays the master work? Decay is one constant if wanted.
4. **The want line's precision** — exact purse ("45g on me") vs a band ("a fair purse")?
   Default exact: this game's counter is a numbers conversation already.

## Definition of done

1. A full day is playable with exactly two bell presses, and neither retired label ("Lower them
   into the mine", "Ring the return bell") exists anywhere in the build.
2. When a party camps, the game stops and asks the supply/recall/deeper question in one modal,
   and does not move until answered. When none camps, the span costs zero clicks.
3. Every counter customer states a want before the player acts, and Suggest/Present each
   produce a spoken answer in the same refresh.
4. One minigame run can gear a full day: repeat crafts of the proven recipe cost one click each.
5. Two different forge stations open two visibly different cards.
6. The first thing the Evening reveal shows on any day with an attribution beat is the player's
   item and its deed.
7. Zero `sim/` diff across the entire plan; no re-baseline commits exist; every PR carried its
   receipt; engine runs were orchestrator-serial.
