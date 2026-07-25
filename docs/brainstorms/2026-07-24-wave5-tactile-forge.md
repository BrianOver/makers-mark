---
title: Wave 5 Tactile Forge — Brainstorm
created: 2026-07-24
status: brainstorm
origin: docs/plans/2026-07-24-003-feat-player-phases-and-hero-depth-plan.md
source: ce-brainstorm (fable)
feature: U23 tactile forge
---

# Wave 5 Tactile Forge — Brainstorm

> Ideation pass by fable, grounded in the current forge code. Feeds a formal `ce-plan`.
> The human still owns the open questions at the bottom (signing-rule promotion, hard-replace,
> batch echo) — do not treat defaults as decided until confirmed.

Context grounded in the code as it exists today: Phase A's three-beat overlay lives in
`godot/scripts/minigames/ForgeMinigame.cs` (Smelt = stop a rising bar in a band, Forge = on-beat
hammer taps, Quench = catch an oscillating band), folds three per-mille sub-scores into one
`PerformanceGrade` in Godot, and the sim consumes it in `QualityRoller.RollActive` (bands <120 Poor
… ≥930 Masterwork, ±25 jitter from the single RNG draw, auto-craft = flat 550). Sub-scores are
stamped verbatim as `Item.CraftSubScores` — explicitly "DATA, never rules." Wave 4's
`ItemSigned(ItemId, SignedName)` event exists in `sim/GameSim/Contracts/Events.cs`. The alchemist
already proved the better architecture: Godot captures a puzzle *state*, and a **pure integer scorer
sim-side** computes the grade (`CraftingHandlers.cs` ~line 103). Wave 5 should move the blacksmith to
that pattern.

## The core fantasy

You are not filling bars — you are *working hot metal*. The billet has heat, and heat is a resource
that drains; every hammer blow spends it, every pump of the bellows buys it back, and the blade's
final character is literally the path your hands took. A masterwork isn't a lucky roll: it's a
forging you can *feel* you performed well — and the item remembers exactly how it was made.

## Candidate mechanics

### A. The Anvil Map (Potion-Craft-style heat/shape field) — RECOMMENDED
One 2D canvas replaces the three sequential beats. **X = shape progress** (raw billet → finished
form), **Y = heat** (cold at bottom, burning at top). Each recipe defines a *forging line* — a
polyline path through this field (dagger = short shallow arc; greatsword = long line with two heat
re-climbs; armor = wide/forgiving; blades = narrow). The billet is a cursor:
- **Hammer strike** (click / Space): advances X proportional to current heat, *costs* heat. Strikes
  inside a soft tempo window move farther.
- **Bellows** (hold): raises heat, but shape *drifts backward slightly* while pumping — can't hammer
  and pump at once.
- **The path is the target**: score = how tightly the actual trajectory hugged the recipe's line.
  Too hot → scorch (permanent sub-score damage); too cold → crack ticks.
- **Quench finale**: at the path's end the line plunges — drag the blade down into the trough at the
  right steepness. Too fast = brittle, too slow = soft.

Smelt/forge/quench survive as **zones of the one path**, so the three-entry `CraftSubScores` shape
maps 1:1 onto path segments.

Pros: directly the Potion Craft feel; per-recipe paths give every recipe its own "song" (replayability
lever); heat-as-resource = real decisions not reflex; deepens rather than discards the beats; 2D
`Control`-only → headless-test safe. Cons: biggest build; needs a path generation scheme
(deterministic generator from recipe tier/slot/weight → polyline, hand-authored overrides later);
strike/heat economy needs tuning.

### B. Strike Ladder (evolve rhythm beat into positional hammering)
Blade silhouette on the anvil; glowing target spots appear in sequence; click on the beat — position
× timing per strike. Cheapest path (current ForgeBeat + a position axis), very juicy. But still a QTE
— plateaus fast, turns to chore by craft #30, doesn't feel like Potion Craft. Fallback if Wave 5 must
shrink.

### C. One Continuous Heat (pursuit curve)
Single falling temperature curve ~25s; do the right thing while in the right band. Elegant, tense,
tiny scope — but one-dimensional; every recipe feels the same. A degenerate case of A; shouldn't ship
alone.

## Recommended mechanic + why

**A, the Anvil Map — with C's heat-decay economy inside it.** Heat drains in real time (pursuit
pressure), strikes advance shape, bellows trade shape drift for heat. Only candidate that delivers the
Potion-Craft spatial mastery, makes every recipe mechanically distinct via its path (multiplies
content value in a small-content game), gives three independent skill axes (heat/rhythm/precision),
and produces a rich deterministic trace that becomes the item's story.

## Deterministic outcome model

**Adopt the alchemist pattern: Godot captures, sim scores.** Single most important architectural
decision — moves grade math out of `ForgeMinigame.ComputeGrade` (Godot floats today) into pure,
unit-testable C#.

1. **Godot side** (floats fine, never cross the boundary): overlay runs the proven accumulated-clock
   `Advance(delta)` pattern (no wall clock, no engine RNG). At a fixed sample cadence (~every 100ms,
   cap N=256) it quantizes the cursor to an integer grid: `(x_permille, y_permille)` pairs + ordered
   integer strike events `(x_permille, tempo_error_permille)`.
2. **The wire type:** `CraftAction` gains a `ForgeTrace` (immutable record of `ImmutableList<int>`
   pairs + strike list + integer path id/seed). All integers. `PerformanceGrade` becomes *derived*.
3. **Sim side, pure scorer:** new `ForgeScorer` in `sim/GameSim/Crafting/` — pure static, zero RNG,
   integer-only. Regenerates the recipe's forging polyline deterministically from
   `(recipe.Tier, recipe.Slot, BaseStats.Weight)` — piecewise-linear only, no transcendentals.
   Per-mille deviation = Σ|actual − ideal| per sample per zone → the same three smelt/forge/quench
   sub-scores, same `CraftSubScores` slot/order. Fold with integer 30/40/30 weights
   `(s*300 + f*400 + q*300)/1000` → `PerformanceGrade` → straight into the untouched
   `QualityRoller.RollActive` band table. **Zero change to RNG draw count** — golden replay shifts
   only from the new action field (deliberate re-baseline), never from new randomness.
4. **Auto-craft untouched:** null trace = null grade = 550, exactly as today.
5. **Moments:** scorer emits a small integer bitflag set — `ForgedInOneHeat`, `NeverScorched`,
   `PerfectQuench`, `RecoveredFromTheBrink` — appended after the three sub-scores (one deliberate
   golden shift).

Difficulty stays a Godot presentation concern (band width, drain rate), same as today's
`ComputeDifficultyPermille` — the sim only ever sees the trace + recipe.

## Fun & anti-tedium

- **Auto-craft stays first-class.** Hand-forge is opt-in per craft. Trash/restock = auto (550 cap);
  masterworks/commissions/signing candidates = hands-on. Never gate the economy on the minigame.
- **Batch echo:** after a hand-forge, the next K copies of the *same recipe, same day* auto-craft at
  `max(550, grade − decay*n)` — deterministic integers, no extra draws. Kills the "hand-forge five
  identical swords" tedium. *Rules change — separate unit inside Wave 5.*
- **Mastery is spatial:** `MinigameAssists` talent → wider path tolerance + slower drain, visible on
  the canvas. Higher tiers = longer paths, narrower tolerance, forced heat re-climbs.
- **Session length target: 20–40s per forge.**
- **Replayability from paths, not randomness:** each recipe's line is fixed and learnable; material
  grade shifts tolerance not shape → improvement is real and attributable.

## Tie to "craft writes legends" + Wave 4 Signed Works

- **The forging becomes History entry #0.** Wave 5 mints the item's first History entry from the
  moments flags: *"Forged in a single heat, never once cooled."* A Signed Work's inscription then
  *begins with how it was made* — the spine made literal.
- **Signing-eligibility hook:** feed `PerformanceGrade` deterministically — grade ≥930 adds fixed
  integer weight to signing odds; `PerfectQuench` + `ForgedInOneHeat` guarantee *eligibility* (not
  signing). This promotes forge data from "DATA, never rules" to a rule — a `Contracts/`-adjacent
  decision needing the orchestrator micro-PR path (see open questions).
- **FlavorForge fuel:** moments flags are integer hooks the flavor packs key phrases off.

## Testability plan

- **Sim (real coverage):** `ForgeScorer` pure static C# — xUnit with zero Godot: deterministic path
  generator (same inputs → identical polyline, cross-run hash), perfect trace → 1000/1000/1000, worst
  trace → floor, deviation monotonicity, moments truth tables, fold weights pinned, golden re-baseline
  once. Beside `ActiveQualityModelTests.cs`.
- **Godot (thin):** overlay stays a 2D `Control` (no 3D SubViewport — respects headless hang trap)
  driven by public `Advance(delta)`; extend `godot/tests/minigames/ForgeMinigameTests.cs` — scripted
  `Advance` + injected input → assert emitted `ForgeTrace` integers. No frame pumping.
- **Contract check:** one test pins that a `CraftAction` with a trace and one without produce
  identical RNG draw counts.

## Scope: core vs later

**Core Wave 5 (ship together, small):**
1. Anvil Map overlay (2D canvas: hammer/bellows/quench-plunge, heat drain) replacing the three beats.
2. Deterministic integer path generator (tier/slot/weight → polyline).
3. `ForgeTrace` on `CraftAction` + sim-side `ForgeScorer` (sub-scores, grade, moments flags) + golden
   re-baseline.
4. Moments → History entry #0 (data/presentation, no rules change).
5. Auto-craft path byte-identical to today.

**Later / nice-to-have:**
- Batch echo (rules change; separate unit even if in-wave).
- Signing-odds promotion of grade/moments (needs contracts micro-PR + Wave 4 emitter).
- Sub-score → stat tilt (smelt→Defense, forge→Attack, quench→durability) — balance rabbit hole.
- Hand-authored signature paths for marquee/commission recipes; forge audio keyed to heat;
  gamepad/haptics; persistent scorch/crack decals on the 3D model.

## Open questions for the human — RESOLVED 2026-07-24

1. **Promote forge performance into signing rules?** → **YES, in Wave 5.** A masterwork forge
   (+ `PerfectQuench` / `ForgedInOneHeat` moments) raises signing odds / guarantees eligibility,
   deterministic integers. Orchestrator authors the `Contracts/` change as a dedicated micro-PR
   BEFORE the dependent implementation units; it lands after Wave 4's `ItemSigned` emitter exists.
2. **Batch echo:** → **INCLUDE in Wave 5**, as its own unit. Decay constant gets a balance-gate look.
3. **Time budget per forge:** default **20–40s** (Godot presentation tuning, not a plan blocker).
4. **Path identity:** default **generated-only** for Wave 5 core; hand-authored signature paths for
   marquee recipes are a later nice-to-have.
5. **Trace size:** default **~256 sampled int pairs** on the `CraftAction`; revisit only if the
   replay log size becomes a problem (Godot could pre-reduce to per-zone sums).
6. **Old three-beat overlay:** → **HARD-REPLACE.** Anvil Map fully replaces the beats; no orphan
   code. Wave 5 blocks on the new forge feeling better in a human playtest.
