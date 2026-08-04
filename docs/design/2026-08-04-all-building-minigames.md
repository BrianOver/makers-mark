---
type: design
title: The minigame layer — two acts in every building, honestly
date: 2026-08-04
origin: owner asks 2026-08-03/04 — "insides should be distinct", "two mini games both fun but shorter", "do for ALL buildings", "why have the interactive things if they just open the same menu", "the gameplay flow/base loop STILL is not complete"
template: godot/scripts/minigames/ForgeMinigame.cs + QuenchMinigame.cs (shipped, PR #381)
related: docs/plans/2026-08-03-001-feat-loop-structure-plan.md (branch feat/loop-structure-plan — NOT yet landed, NOT yet ruled on)
---

# The minigame layer — two acts in every building, honestly

## 0. The honest lead: is this the right next build?

**Not first.** The owner's fifth complaint — "the base loop STILL is not complete" — is not a
minigame deficit, and five more minigames will not touch it. The loop's hole is structural and
already diagnosed in the un-ruled loop-structure plan (`2026-08-03-001`, branch
`feat/loop-structure-plan`): the player acts in exactly one phase (Morning holds ~20 of 24
verbs), the middle of the day is three dead bells the player hand-cranks through nothing, and
every consequence lands as one Night text flood. Minigames are all Morning-side work. Adding
five of them adds more DOING to the phase that already has all the doing, and adds nothing to
the answer half of the day. **Built alone, this layer makes the game busier, not more
complete** — that is the plain answer to the question the deliverable asks.

**But it is the right second build, and most of it is cheap.** The other four complaints
(distinct insides, two short acts, skill speeds you up, stations that do something) are real,
they are all about the feel of the acting half, and the forge already proved the template at
measured cost. The recommendation, in order:

1. **Rule on the loop-structure plan and ship its U1 (the two-bell day) + U3 (forge another
   like it) first, or in the same wave.** U1 gives the day a middle where the minigames' work
   gets ANSWERED (the raid as a watchable show). U3 is a hard co-requisite for this layer
   specifically: with 3-4 crafts a day, five two-act minigames without the repetition-kill is
   the third "too long" complaint reborn at 5x scale.
2. **Then this layer, in two waves:** Wave 1 is zero-sim-diff (Market/Tavern/Gate staging,
   the shared skill tracker, engineering's act split, all the `CombinesWith` data). Wave 2 is
   one small Contracts micro-PR (two additive puzzle-input fields) that gives alchemy and
   tanning their decisive second acts.

The owner can overrule the ordering; the layer below is designed to be buildable either way.

## 1. What the forge proved — the template and its five load-bearing properties

`ForgeMinigame` (Act 1: hold heat in the band with the bellows WHILE striking shape on the
anvil, stops at `ForgePath.ForgeZoneEnd` = 666‰) + `QuenchMinigame` (Act 2: one timed plunge,
band narrows by tier, owns the craft's single `CraftAction`). Measured: skilled ~9.7s
combined, beginner ~19.5s. Every building below inherits these properties **unchanged**:

- **P1 — Two acts, one commit.** A sustained act where two stations genuinely cooperate, then
  a short decisive act that commits the result. Act 1 never emits a sim action; Act 2 owns
  the ONE action (PKD8). Cancel at any point queues nothing and spends nothing.
- **P2 — Adapter-side skill expression, sim-side rules (KTD2).** The overlay captures integer
  input (trace, pours, passes, placements); the pure sim scorer grades it. Reflexes shape the
  *score*, never legality. Preview grades call the same pure scorer read-only.
- **P3 — Session skill = less labor, same standards.** `RequiredStrikes` 21→18 as demonstrated
  accuracy rises; session-scoped, owned by the panel, never persisted to the sim save. Skill
  makes each gesture BIGGER, it never widens the accuracy target.
- **P4 — Tier = precision.** Higher-tier material narrows the decisive act's band
  (140/100/70‰ by tier). Difficulty of the target, not length of the labor.
- **P5 — A bad player always finishes.** Timeout auto-commits (quench's auto-plunge), partial
  input is legal and scores what it scores. Failure lowers quality; it never softlocks and
  never wastes the material with nothing to show.
- **P6 — Accumulated clock, no RNG, no SubViewports.** `Advance(delta)` drives everything;
  scripted tests measure durations on the accumulated clock (frame count is not a duration).

## 2. Two shared mechanisms (not five bespoke systems)

### 2.1 SessionSkill — one tracker, one knob per act

One new adapter-side class, `godot/scripts/ui/SessionSkill.cs`: per craft-family key, a
demonstrated-accuracy score 0..1000, session-scoped, never persisted, never read by the sim.
`ForgePanel`'s existing tracker (the thing that feeds `demonstratedAccuracyPermille` into
`ForgeMinigame.Configure`) migrates into it as the first client. Each act maps the same
number onto its own **labor quantum** — the thing that shrinks as the player proves out:

| Act | Labor quantum at 0‰ → 1000‰ |
|---|---|
| Forge shape | `RequiredStrikes` 21 → 18 (shipped) |
| Alchemy brew | pour-settle gate 0.5s → 0.0s (cauldron accepts the next pour sooner); notes-from-memory already shipped (`NotesFamiliar`) |
| Tanning scrape | `ScrapePixelThreshold` 14px → 10px per recorded pass (each drag gesture covers more) |
| Engineering windup | `CrankStrokesRequired` 5 → 3 |
| Market close | quick-close affordance: Accept/Counter directly from the want line, skipping the presentation beat's ritual clicks |

The rule that keeps this honest, inherited from the forge: **the quantum shrinks the labor,
never the accuracy demand.** Bands, pour orders, patch layouts, willingness math are
untouched by SessionSkill. Tanning's coarser quantization records more passes per gesture —
the recorded input stays truthful; the player still chooses where to stop scraping.

### 2.2 The Set — one decisive-act shape, tier-narrowed

Every crafting Act 2 is a Quench-shaped timed set: a live gauge, ONE input to commit inside a
band, auto-commit at timeout, band narrows with material tier. The forge shipped it. To give
alchemy and tanning a *scored* Set, their puzzle inputs each gain **one additive field** —
the single Contracts micro-PR this whole layer needs (orchestrator-authored per the
deny-list, merged before dependent PRs):

- `AlchemyReagentPuzzle` gains `int? CutPermille = null`
- `TanningScrapeInput` gains `int? DipPermille = null`

Scoring rule (sim-side, in `AlchemyPuzzleScorer` / `TanningScrapeScorer`): `null` → score
byte-identically to today (**neutral default — the golden replay's recorded actions carry
null, so the golden stands without a re-baseline**); non-null → fold a bounded finish
component, `grade' = grade × 0.85 + finishScore × 0.15`, where `finishScore` is distance
from the target instant, tolerance from a new sim-side `FinishTolerance.ForTier(tier)`
helper (in `sim/GameSim/Crafting/`, NOT Contracts). The overlays render that same helper
read-only — the `TanningFrame`-calls-`PatchesFor` precedent, never an adapter-owned rule.
Balance gate re-runs with the wave; a 15% bounded fold cannot move the 100-day economy
outside the gate, but the gate is the proof, not this sentence.

**Where a building has no material, the precision axis is named honestly, not faked:**

| Building | "High-tier metal" equivalent |
|---|---|
| Workshop (all four professions) | Material/reagent/hide tier narrows the Set band; engineering instead scales Act 1 complexity (sockets = tier+2, clamped 3..5 — already in `EngineeringAssemblyScorer.SocketCountFor`) |
| Market | The item's own value: pinning Counter near true willingness is the existing precision mechanic (`WillingnessModel`, mood bonus for a close pin), and a given permille of miss costs more gold on a dearer item. **No new axis — the stakes scale it naturally.** |
| Tavern | **None. Stated plainly:** the tavern has no material and its committing verbs carry no performance parameter. Inventing a precision gauge here would be theater. |
| Mine Gate | Advisory only: a fair-reward band per target floor, derived read-only from the same forecast data the muster board shows. The sim accepts any reward — takers (heroes) are the real judgment feedback. No new acceptance rule. |

## 3. Per-building design

Every block follows the same contract: two acts, the cooperating pair (wired as a mutual
`CombinesWith` in existing station data), the ONE sim action each act commits, target
seconds (skilled / beginner), what the hands do, both levers, files, tests, art. All Act 2
canvases ship procedural-first — `QuenchCanvas` ships with zero textures and reads fine;
painted dressing is a separate art ask, never a blocker.

### 3.1 Forge (blacksmith workshop) — SHIPPED, the template

| | |
|---|---|
| Act 1 | **Shape** — anvil + bellows (`CombinesWith` mutual, already in `WorkshopVocab`). Hammer strikes advance shape and cost heat; bellows raise heat and drift shape back; mutually exclusive inputs; tempo window rewards rhythm. |
| Act 2 | **Quench** — one timed plunge into the band; band 140/100/70‰ by tier. |
| Commits | Act 2 owns the single `CraftAction` (trace-scored by `ForgeScorer`). |
| Duration | ~9.7s / ~19.5s combined (measured). |
| Skill / precision | `RequiredStrikes` 21→18 (migrates into SessionSkill, behavior identical) / tier band. |
| Files | None beyond the SessionSkill migration (`ForgePanel.cs`, new `SessionSkill.cs`). |
| Tests | `ForgeWinnabilityTests`, `ForgeTwoActTests` — already green; the migration must not change a single measured number (pin: same strikes at same accuracy). |
| Art | `assets/minigames/billet_0-3.png`, `hammer.png`, `forge_backdrop.png`; stations `town2d-station-anvil/-bellows/-furnace/-quench.png`. Complete. |

One data fix rides this wave: the blacksmith's `quench` station row is still honest-flavor
("the anvil handles the real quenching") — now that Act 2 exists, pressing the quench trough
during a pending Act 2 handoff should BE the plunge surface. Promote it to a real row
(Action "Forge", Verb "Quench", `CombinesWith: "anvil"` stays out — the guard's pair slot is
taken; give it its own route) or, cheaper, keep the drawer flow and update the flavor copy.
Implementer's choice; the current copy is now a lie either way.

### 3.2 Workshop — Alchemy

| | |
|---|---|
| Act 1 | **The Brew** — cauldron + still (new mutual `CombinesWith` in `WorkshopVocab`). The existing `AlchemyBrewPuzzle` pour-order body, unchanged input, unchanged scorer: drag reagent bottles into the cauldron in the recipe's order. New staging only: each correct pour visibly charges the still's condenser column (read-only preview feedback, existing precedent). Deliberately clockless — the discrete, thinking counterpart to the forge; that contrast is identity, keep it. |
| Act 2 | **The Draw** — at the still. Quench reskin: the condensate gauge climbs then falls; ONE input ("take the cut") inside the band; auto-cut at the 4s timeout. |
| Commits | Act 2 owns the single `CraftAction`; `AlchemyReagentPuzzle` carries the pours + `CutPermille` (§2.2). |
| Duration | Act 1 ~6s / ~14s (settle-gate + notes memory are the spread); Act 2 ~2s / ~4s. Total ~8s / ~18s — under the forge. |
| Hands | Act 1: drag bottle → cauldron mouth, repeat in order, undo free. Act 2: watch the gauge, one press. |
| Skill / precision | Pour-settle gate 0.5s→0s + notes memory (shipped) / reagent tier narrows the cut band via `FinishTolerance.ForTier`. |
| Files | `godot/scripts/minigames/AlchemyBrewPuzzle.cs` (settle gate, condenser staging, handoff event instead of self-Submit), new `godot/scripts/minigames/StillDrawMinigame.cs` (structure-copy of `QuenchMinigame`), `ForgePanel.cs` (act chaining, same pattern as forge's `ShapingDone` handoff), `WorkshopVocab.cs` (pair row). Sim: `AlchemyPuzzleScorer` + Contracts field (micro-PR). |
| Tests | New `AlchemyTwoActTests.cs`: (a) scripted zero-skill run completes via auto-cut and emits exactly one `CraftAction` (winnable); (b) scripted skilled run (1000‰ SessionSkill, notes familiar) finishes Act 1 in fewer accumulated-clock seconds than the zero-skill run (skilled-faster, on the clock, not frames); (c) `CutPermille=null` scores byte-identical to pre-change fixtures (neutral default); (d) tier 3 tolerance < tier 1 tolerance (sim-side unit test, no Godot). |
| Art | Has: `bottle.png`, `cauldron.png`, `brew_backdrop.png`. Stations `town2d-station-alch-*` are pinned ids with **no art files yet** — ship on the existing loud-placeholder fallback. Art asks (separate): four alch station sprites; a still/condenser column for the Draw canvas (procedural first). |

### 3.3 Workshop — Tanning

| | |
|---|---|
| Act 1 | **The Scrape** — scrape-frame + vats (new mutual pair; vats promote from flavor to real: Action "Forge", Verb "Cure", own Copy, Hover/FlavorLine dropped). Existing `TanningFrame` coverage-with-restraint body, unchanged input and scorer. Clockless and orderless by design — the quiet craft; keep it. |
| Act 2 | **The Dip** — at the vats. The take-the-hide-off-frame gesture no longer submits; it hands off. At the vat: the liquor-bite gauge rises as the hide steeps; ONE input ("pull it") inside the band; auto-pull at timeout. |
| Commits | Act 2 owns the single `CraftAction`; `TanningScrapeInput` carries passes + `DipPermille` (§2.2). |
| Duration | Act 1 ~10s / ~20s; Act 2 ~2s / ~4s. Total ~12s / ~24s. |
| Hands | Act 1: press-drag strokes across the hide grid, any order, stop-and-think free. Act 2: watch the bite gauge, one press. |
| Skill / precision | Stroke quantum 14px→10px per pass / hide tier narrows the pull band. |
| Files | `TanningFrame.cs` (quantum from SessionSkill; Submit becomes handoff), new `VatDipMinigame.cs` (QuenchMinigame structure-copy), `ForgePanel.cs` chaining, `WorkshopVocab.cs` (pair + vat promotion). Sim: `TanningScrapeScorer` + Contracts field (same micro-PR as alchemy). |
| Tests | New `TanningTwoActTests.cs`: same four-case contract as alchemy (auto-pull winnable; skilled covers the same fixture hide in less accumulated clock at the coarser quantum; `DipPermille=null` neutral; tier tolerance ordering). Plus `StationIdentityTests` picks up the vat promotion automatically (Verb/Copy required, route uniqueness). |
| Art | Stations `town2d-station-tan-*`: pinned ids, **no art files yet** — placeholder fallback. No minigame sprites exist; both canvases procedural-first. Art asks: four tan station sprites; hide + vat dressing. |

### 3.4 Workshop — Engineering (dormant — activation is its own gate)

| | |
|---|---|
| Act 1 | **The Fit** — bench + flywheel (new mutual pair; flywheel promotes from flavor: Action "Forge", Verb "Wind", own Copy). Existing `EngineeringBench` assembly body: read the schematic, tell near-twin parts apart (3 families × 2 variants), seat them; reseat free; partial legal. Clockless — the spatial-planning craft; keep it. |
| Act 2 | **The Windup** — the existing crank finale re-homed to the flywheel as its own short overlay: N strokes wind the mechanism, the last stroke commits. **Deliberately unscored** — engineering's precision already lives in Act 1 (sockets = tier+2; the twin-part discrimination), so the Set here is commit ceremony, not a second graded axis. This is the sanctioned exception to §2.2, stated so nobody "fixes" it later: one profession's decisive act is pure ritual because its skill is spatial, not temporal. No Contracts change for engineering. |
| Commits | Act 2 (final crank stroke) owns the single `CraftAction` (`EngineeringAssemblyInput`, unchanged). |
| Duration | Act 1 ~12s / ~25s (thinking time dominates — untimed); Act 2 ~2s / ~3s. |
| Hands | Act 1: drag parts into sockets (or keyboard cursor). Act 2: right-drag crank strokes or Space presses. |
| Skill / precision | `CrankStrokesRequired` 5→3 via SessionSkill / tier scales socket count + wanted-part spread (already sim-side). |
| Files | `EngineeringBench.cs` (crank extraction + handoff), new `FlywheelWindupMinigame.cs`, `ForgePanel.cs` chaining, `WorkshopVocab.cs` (pair + flywheel promotion). |
| Tests | New `EngineeringTwoActTests.cs`: zero-skill partial assembly still completes and commits (winnable — partial-legal is the existing contract); skilled windup takes fewer strokes and less accumulated clock; the emitted flattened placement list is byte-identical whether cranked at the bench or the flywheel (re-home is presentation-only). |
| Art | Stations `town2d-station-eng-*`: pinned ids, **no art files yet**. Canvases procedural-first. **Dependency:** `ProfessionDefinition.ActiveCraft` is false for engineering — flipping it is the orchestrator's call (talent remap + balance re-run, per `EngineeringBench`'s own doc). This design stages the two-act shape; it does not flip the flag. |

### 3.5 Market — the minigame already exists in the sim; it needs a stage

The counter session IS a deterministic, zero-RNG minigame sim-side (`CounterQueueSystem`,
`HaggleResolver`, `WillingnessModel`): queue → customer → present → haggle rounds under a
~3-round patience cap, with a whole-session fleece memory (`GoodwillPermille`) and a mood
bonus for pinning Counter near true willingness. What is missing is exactly what the owner
saw: the customer never speaks, and the stations open one drawer. **Zero new sim rules.**

| | |
|---|---|
| Act 1 | **The Ask** — wares shelf + sales counter (new mutual `CombinesWith` in `InteriorLayout2D`; shelf-b keeps its own Browse Curios route). The customer approaches and STATES a want — this is the loop plan's KTD-B (`CustomerVoice.cs`: want line derived read-only from the hero's own gear gaps, class, purse via `ShoppingAi.EvaluateItem` — the ForgeMinigame preview precedent, never a second rule set). The player pulls the matching item from the shelf rail and presents it, or Suggests and hears a spoken reply. **Shared unit warning: this IS loop-structure U2 — build it once.** |
| Act 2 | **The Close** — at the counter. The haggle: a price rail with the willingness fog band; Accept / HoldFirm / Counter-at-a-pin; patience pips count the rounds down. One decisive pin per round, ≤3 rounds. |
| Commits | Act 1: `PresentItemAction` / `SuggestItemAction` (both real, immediate-lane). Act 2: `HaggleResponseAction(Accept | HoldFirm | Counter, price)`; `OpenCounterAction`/`CloseCounterAction` bracket the session (close falls unserved customers back to the atomic pass — PKD5, nobody starves). |
| Duration | Per customer: Act 1 ~3s / ~8s; Act 2 ~5s (one-round close) / ~17s (all three rounds). Total ~8s / ~25s per customer. |
| Hands | Act 1: click the shelf item (rail filtered toward the stated want), one Present click. Act 2: drag the pin on the price rail, one of three buttons. |
| Skill / precision | SessionSkill quick-close (skip the presentation ritual once proven — fewer clicks, same math) / the willingness pin, stakes-scaled by item value (§2.2 — exists, no new axis). |
| Files | New `godot/scripts/ui/CustomerVoice.cs` (loop plan U2's file — shared), `CounterPanel.cs` (two-act staging, price rail, patience pips), `InteriorLayout2D.cs` (pair row), `MainUi.cs` (counter/shelf routing to the staged session). No sim files. |
| Tests | Extend `CounterPanelTests.cs` + new `CustomerVoiceTests.cs` (loop plan U2's list: want line names a real gap and the hero's own gold; every `ShoppingVerdictKind` renders a reply). New: a scripted beginner who never counters (Accept when offered, or lets patience lapse) always ends the customer — sale or walk — with the session advancing (winnable, no softlock); a scripted skilled close (fair Counter pin round 1) ends the same fixture customer in fewer rounds and less accumulated clock (skilled-faster). Economics (does the counter now out-earn the atomic pass?) stays a post-ship measurement, per the loop plan's scope boundary — not asserted here. |
| Art | Stations `town2d-station-market-counter/-shelf/-ledger/-crates.png` — all exist. Canvas procedural-first; art ask (separate): a desk-mat close-up strip for the haggle rail. |

### 3.6 Tavern — two real acts, and a recommendation against a reflex layer

The tavern today commits nothing: `TavernPanel` is read-only gossip + patron roster. The real
verbs that FIT the room already exist elsewhere: `AcceptCommissionAction`/`DeclineCommissionAction`
(Morning, surfaced on `CommissionBoard`) and `BuyOreAction` (Evening, 1-day-lag hero ore
offers, currently surfaced in `LedgerModal`). The design moves those handshakes to where the
fiction says they happen — over a table and sealed at the bar.

| | |
|---|---|
| Act 1 | **Work the Room** — corner table + bar (new mutual pair; table-a keeps its own Eavesdrop route). Patrons at tables speak: commission wants in the Morning, ore offers in the Evening — voice lines derived read-only from the same state those surfaces already render (CustomerVoice precedent again). The player walks the tables and picks the thread to pursue. Selection is staging, not a commit — same as picking a recipe before the forge. |
| Act 2 | **The Handshake** — at the bar. Commits the pursued thread: Morning → `AcceptCommissionAction`/`DeclineCommissionAction`; Evening → `BuyOreAction(From, MaterialKey, Quantity)` with the quantity slider (gold on the table is the decision weight). |
| Commits | As above — all existing actions, existing legality windows (commission verbs Morning-only, BuyOre Evening-only; the room's prompts phase-honestly, the same honest-flavor discipline the station data already enforces). |
| Duration | Act 1 ~5s / ~10s (read two speech bubbles); Act 2 ~2s / ~5s. |
| Hands | Walk to a table, E to hear; walk to the bar, one confirm (quantity slider for ore). |
| Skill / precision | **None mechanical, and no reflex layer — recommended plainly.** `BuyOreAction` and the commission verbs carry no performance parameter; a timing gauge on them would change nothing sim-side — exactly the "bounty theater" failure class the 07-25 audit named. The tavern's skill is reading the room (whose ore, how much, which commission your slots can honor). Speed comes from legibility. **The owner said ALL buildings; this is the exemption argued for his overrule:** the two-act structure and real commits are here, the dexterity gauge is not. If overruled, the least-fake reflex act is "pour the round" (fill mugs without overflow) — but for it to be non-theater it needs a real `BuyRoundAction` (gold → patron `MoodPermille`) which is a NEW sim rule: Contracts + handler + PKD7 review + balance gate. Priced, not smuggled. |
| Files | `TavernPanel.cs` (table speech + bar handshake staging), `CustomerVoice.cs` (shared line derivation), `InteriorLayout2D.cs` (pair row), `MainUi.cs` (routing table-b/bar to the staged session; `LedgerModal`/`CommissionBoard` keep working unchanged — the tavern is a second door to the same actions, not a fork of their rules). No sim files. |
| Tests | New `TavernActsTests.cs`: an Evening with a pending ore offer renders a speaking patron and the bar handshake commits a `BuyOreAction` matching the spoken offer (real outcome, not a toy); a Morning commission thread commits Accept and the `CommissionBoard` state agrees (one source of truth); outside the legal phase the bar prompt is honest-flavor (never a dead or lying click); zero-read player can still commit from the existing panels (no regression). |
| Art | Stations `town2d-station-tavern-bar/-table/-storywall/-hearth.png` — all exist. Speech bubbles reuse `BuildSpeechBubble`. No new art required. |

### 3.7 Mine Gate — the gate asks the day's questions; no reflex layer here either

The gate's committing verbs: `PostBountyAction(TargetFloor, RewardGold)` (Morning or Evening)
and — Camp phase only, only when a party actually parked — `SendSupplyAction`/`RecallPartyAction`.
These are judgment calls with real gold and real lives attached. Same honesty as the tavern:
no performance parameter exists, so no dexterity gauge — argued for overrule, not assumed.

| | |
|---|---|
| Act 1 | **The Muster** — muster board + overlook (new mutual pair; they keep their distinct routes — `CombinesWith` links the presentation, sharing a route is optional and here unwanted). The sustained read: parties forming, gear gaps, the fair-reward band per floor (advisory, read-only from forecast data, §2.2), while the overlook's Mirror shows the mine mouth. This is where the player aims the day. |
| Act 2 | **Post the Bounty** — at the bounty ledger. Floor pin + reward pin against the advisory band; one confirm commits `PostBountyAction`. The real feedback is sim-real already: do heroes take it, does the floor get cleared. Auto-nothing on walk-away — an unposted bounty is a legal choice, not a fail state. |
| The Vigil | **If/when the two-bell day (loop U1) ships, its one mid-day stop stages HERE.** The camp question (send supplies / bring them home / send them deeper) is the gate's decisive act in Camp phase: the winch — currently honest-flavor — gains a real route (new action string "Vigil" → the existing `CampPanel` slate; one `MainUi` routing case, the "Watch"→Mirror precedent), Verb "Wind the Winch". "Bring them home" IS `RecallPartyAction` at the winch; "Send supplies" IS `SendSupplyAction` at the muster board. Physical, distinct, zero new sim. If U1 has not shipped, the winch route still works — it opens the camp slate during Camp and honest-flavors otherwise (`CampPanel` needs a small "nobody camped below" empty state, the Mirror's precedent). |
| Commits | `PostBountyAction`; Camp: `SendSupplyAction` / `RecallPartyAction`. All existing, all in their existing legality windows. |
| Duration | Act 1 ~5s / ~10s; Act 2 ~3s / ~6s; vigil stop untimed by design (loop plan KTD-A — the one question the world waits on). |
| Hands | Walk board → overlook; at the ledger: two pins, one confirm; at the vigil: one of three buttons. |
| Skill / precision | None mechanical (recommended) / the advisory reward band per floor — judgment against a visible reference, never an acceptance rule. |
| Files | `InteriorLayout2D.cs` (pair row + winch promotion + "Vigil" action string), `MainUi.cs` (Vigil route), `BountyPanel.cs` (floor/reward pins + advisory band), `CampPanel.cs` (empty state + the third verb if U1 lands first), `RaidForecastBoard.cs`/`DepthsPanel.cs` (band derivation, read-only). No sim files. |
| Tests | Extend `StationIdentityTests` (winch promotion checked automatically). New `GateActsTests.cs`: posting from the ledger commits a `PostBountyAction` with the pinned floor/reward (real outcome); the advisory band is derived from forecast state and renders for every floor (enumerated, never hand-listed); during Camp with a parked party the winch route reaches the slate and Recall commits; outside Camp the winch is honest about itself; a player who ignores the band entirely can still post any legal bounty (advisory means advisory). |
| Art | Stations `town2d-station-gate-overlook/-muster/-bounty/-winch.png` — all exist. No new art required. |

## 4. The base loop — what this layer does and does not fix

Plainly: **the minigame layer makes Morning (and now Evening, via the tavern) richer; it does
not complete the loop.** The loop's incompleteness is the dead middle and the crushed answer
— the un-ruled loop-structure plan's diagnosis stands, and nothing in this document touches
it. If the two-bell day never ships, this layer's honest effect is: better verbs, same
broken day — busier.

Shipped together, they interlock tightly:

- **Morning** — the acts: forge/workshop two-act crafts, the market's Ask-and-Close, the
  tavern's commission handshake, the gate's bounty. Every one commits a real action the raid
  will answer.
- **The raid span (loop U1)** — the answer plays itself as a show; the one stop is the vigil,
  and §3.7 gives that stop a physical home at the gate instead of a floating modal.
- **Evening** — the tavern's ore handshake gives Evening its first hands-on ACT (today it is
  three menu verbs and a text flood), which is a small but real contribution to the loop's
  answer half.
- **Shared unit:** Market Act 1 IS loop-plan U2 (the customer speaks first). Build it once,
  under whichever wave lands first.
- **Hard co-requisite:** loop-plan U3 (forge another like it) must extend to every profession's
  two-act craft — the repeat button reuses the captured puzzle input for alchemy/tanning/
  engineering exactly as it does the forge trace. Without it, this layer multiplies the
  repetition complaint by five.

## 5. Waves and units

**Wave 0 (prerequisite, pending owner ruling):** loop plan U1 + U3. Not this document's scope.

**Wave 1 — zero sim diff, five parallel units:**

| Unit | Scope | Files |
|---|---|---|
| M1 | SessionSkill + forge migration (behavior-identical, pinned) | `SessionSkill.cs`, `ForgePanel.cs` |
| M2 | Market two-act staging (+ CustomerVoice — the shared loop-U2 build) | `CustomerVoice.cs`, `CounterPanel.cs`, `InteriorLayout2D.cs`, `MainUi.cs` |
| M3 | Tavern two acts | `TavernPanel.cs`, `InteriorLayout2D.cs`, `MainUi.cs` |
| M4 | Gate two acts + vigil-at-the-gate | `InteriorLayout2D.cs`, `MainUi.cs`, `BountyPanel.cs`, `CampPanel.cs` |
| M5 | Engineering act split + crank re-home (still dormant) | `EngineeringBench.cs`, `FlywheelWindupMinigame.cs`, `WorkshopVocab.cs`, `ForgePanel.cs` |

M2/M3/M4 all touch `InteriorLayout2D.cs` + `MainUi.cs` — serialize their merges (the U3/U4
ForgePanel precedent from the loop plan). Engine suite orchestrator-run, one worktree at a
time, full suite always (the green-54 rule).

**Wave 2 — one Contracts micro-PR, then two parallel units:**

| Unit | Scope |
|---|---|
| C1 | Contracts micro-PR: `CutPermille` + `DipPermille`, neutral defaults; `FinishTolerance.ForTier`; scorer folds; golden replay proven standing; balance gate re-run. Orchestrator-authored. |
| M6 | Alchemy two acts (`StillDrawMinigame`, brew handoff, settle gate) |
| M7 | Tanning two acts (`VatDipMinigame`, scrape handoff, stroke quantum) |

**Engineering activation** (ActiveCraft flip + talent remap + balance) stays its own
orchestrator gate, unblocked by M5 whenever the owner wants it.

## 6. The layer at a glance

| Building | Act 1 (sustained — cooperating pair) | Act 2 (decisive) | Sim action committed | Skilled / beginner | Skill lever | Precision lever |
|---|---|---|---|---|---|---|
| Forge (shipped) | Shape — anvil + bellows | Quench plunge | `CraftAction` | ~9.7s / ~19.5s | strikes 21→18 | tier band 140/100/70‰ |
| Workshop: Alchemy | The Brew — cauldron + still | The Draw (take the cut) | `CraftAction` | ~8s / ~18s | settle gate 0.5→0s + notes memory | reagent tier narrows cut band |
| Workshop: Tanning | The Scrape — frame + vats | The Dip (pull the hide) | `CraftAction` | ~12s / ~24s | stroke quantum 14→10px | hide tier narrows pull band |
| Workshop: Engineering | The Fit — bench + flywheel | The Windup (crank, unscored ceremony) | `CraftAction` | ~14s / ~28s | crank strokes 5→3 | tier scales sockets/twins (Act 1) |
| Market | The Ask — shelf + counter (customer speaks) | The Close (willingness pin) | `Present`/`Suggest` → `HaggleResponse` | ~8s / ~25s per customer | quick-close ritual skip | willingness pin, stakes-scaled |
| Tavern | Work the Room — tables + bar | The Handshake | `AcceptCommission` (AM) / `BuyOre` (PM) | ~7s / ~15s | none — reading the room | **none (honest) — no reflex layer, argued §3.6** |
| Mine Gate | The Muster — muster + overlook | Post the Bounty; the Vigil (Camp) | `PostBounty`; `SendSupply`/`RecallParty` | ~8s / ~16s | none — judgment | advisory reward band — **no reflex layer, argued §3.7** |

## 7. Open questions for the owner

1. **Ordering:** rule on the loop-structure plan first? This document's recommendation is yes
   (§0); the layer builds cleanly either way, but "busier vs complete" is decided there, not here.
2. **Tavern/Gate exemption from the reflex layer** (§3.6/§3.7): both get two real acts with
   real commits; neither gets a dexterity gauge, because their verbs carry no performance
   parameter and a gauge would be theater. Overrule path for the tavern is priced (a real
   `BuyRoundAction` — new sim rule).
3. **Engineering's unscored Windup** (§3.4): precision stays in Act 1 on purpose. If you want
   the crank scored too, that is a third Contracts field and a scorer change — same shape as C1.
4. **The quench-station data lie** (§3.1): promote the trough to a real plunge surface, or
   just fix the copy?
