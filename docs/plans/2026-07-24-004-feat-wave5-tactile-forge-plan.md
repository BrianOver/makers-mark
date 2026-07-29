---
type: feat
title: Wave 5 — Tactile Forge (Anvil Map)
created: 2026-07-24
origin: docs/brainstorms/2026-07-24-wave5-tactile-forge.md
parent_plan: docs/plans/2026-07-24-003-feat-player-phases-and-hero-depth-plan.md
product_contract_source: ce-brainstorm
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
feature: U23 tactile forge
---

# Wave 5 — Tactile Forge (Anvil Map)

> **STATUS: SUPERSEDED (stamped 2026-07-28).** The "Anvil Map" forge presentation this plan specced shipped (#205) and has since been replaced twice over — first by a diegetic forge scene, then by the workbench reframe (#257) and the feel pass in `docs/plans/2026-07-28-002-feat-interactive-professions-and-trade-plan.md` U3 (#261). Read `2026-07-28-002` for the current forge design. Kept as the historical record.

---

## Goal Capsule

Replace the blacksmith's three sequential forge beats (Smelt/Forge/Quench bars) with **one
tactile "Anvil Map"**: a 2D heat-vs-shape field where the player hammers, pumps the bellows, and
quenches along a per-recipe path. The moment-to-moment feel is Potion-Craft spatial mastery; the
outcome is **scored inside the pure sim**, not in Godot. This makes forging a skill you visibly get
better at, makes every recipe mechanically distinct (its path is its "song"), and makes the forging
itself the **first line of every item's legend** — "your craft writes the legends" made literal.

**Human-decided scope (2026-07-24):** hard-replace the old overlay (no orphan code); forge
performance feeds Signed-Work eligibility (mostly already true via U19); batch-echo is IN this wave.

## Key Technical Decision — reuse the existing dual-mode seam

The contract already has the exact seam for this: `CraftPuzzleInput` (abstract, `[JsonPolymorphic]`)
with `AlchemyReagentPuzzle` as a live derived type, scored sim-side by
`AlchemyPuzzleScorer.Score(...).GradePermille` in `CraftingHandlers.ApplyCraft`. Wave 5 adds a
**second derived type `ForgeTraceInput`** and a **`ForgeScorer`** mirroring the alchemist exactly.
This is architecturally pre-sanctioned (see `CraftPuzzleInput`'s own doc — "Phase B registers derived
types via a contracts micro-PR"). We do NOT invent a new `CraftAction` field.

Consequence for "forge → signing": **U19 already keys signing off `Item.CraftSubScores` + Masterwork**
(`ArtifactSigning.Qualifies`). `ForgeScorer` produces those three sub-scores, so an excellent Anvil-Map
forge already flows into a signature with zero extra rules. The only additive signing rule is the
moments-guarantee (a perfect quench + one-heat forge *guarantees eligibility*), and even that is
optional polish — flagged in U5.

## Determinism contract (KTD4 — non-negotiable)

- `ForgeTraceInput` is **all integers** (per-mille): sampled `(xPermille, yPermille)` cursor pairs
  (cap N=256), ordered strike events `(xPermille, tempoErrorPermille)`, and an integer path seed.
  No floats cross the Godot→sim boundary. Godot may use floats internally for animation only.
- `ForgeScorer` is **pure static, integer-only, zero RNG, zero wall-clock, no transcendental
  `Math.*`** — piecewise-linear polyline math only. Same class of code as `AlchemyPuzzleScorer`.
- The craft's single `Roll100` draw is unchanged — the scorer feeds `performanceGrade` into the
  existing `QualityRoller.RollActive` exactly where `action.PerformanceGrade` does today. **Draw
  count per craft is identical.**
- **Golden re-baseline expected:** adding `ForgeTraceInput` as a `[JsonDerivedType]` and adding
  moments flags to `Item.CraftSubScores` (or a sibling field) changes the serialized SHAPE.
  `BaselinePlayer` never crafts with a puzzle, so behavior is unchanged — this is a deliberate,
  documented shape-only re-baseline (orchestrator owns it), same class as Wave 3/4a.

## Implementation Units

### U23a — Contracts micro-PR (ORCHESTRATOR-authored, lands FIRST)
- **Goal:** Add the `ForgeTraceInput` derived type + any moments field. Nothing depends on Godot.
- **Files:**
  - Modify `sim/GameSim/Contracts/Actions.cs` — add `[JsonDerivedType(typeof(ForgeTraceInput), "forgeTrace")]` to `CraftPuzzleInput` (base already polymorphic — one line, no "attribute alone" risk) and define `public sealed record ForgeTraceInput(ImmutableList<int> Samples, ImmutableList<int> Strikes, int PathSeed) : CraftPuzzleInput;` (flatten pairs to a single int list, length even; documented).
  - Modify `sim/GameSim/Contracts/Items.cs` — if moments ship as data: add trailing `public ImmutableList<int> ForgeMoments { get; init; } = ImmutableList<int>.Empty;` (or fold moments into `CraftSubScores` tail — decide in U23b; prefer a separate field for clarity).
  - Modify `sim/GameSim/Contracts/Events.cs` — optional `ForgeMomentAchieved`? **No** — moments surface via History entry #0 (U23d), no new event needed.
- **Verification:** `Game.sln` builds; a round-trip serialize test for `ForgeTraceInput`; polymorphic config doesn't throw (base already has ≥1 derived type). Golden NOT touched yet (no consumer).
- **Deny-list note:** `Contracts/` is orchestrator-only. This unit is a standalone micro-PR merged BEFORE U23b–e; in-flight agents rebase.

### U23b — `ForgeScorer` (sim, pure) + path generator
- **Goal:** Score a `ForgeTraceInput` against a recipe's deterministically-generated forging path →
  `GradePermille` + three sub-scores (smelt/forge/quench zones) + integer moments bitflags.
- **Files:**
  - Create `sim/GameSim/Crafting/ForgeScorer.cs` — mirror `AlchemyPuzzleScorer`'s shape; return a
    small record `(int GradePermille, ImmutableList<int> SubScores, int Moments)`.
  - Create `sim/GameSim/Crafting/ForgePath.cs` — pure generator: `(recipe.Tier, recipe.Slot,
    BaseStats.Weight, pathSeed) → ImmutableList<int>` polyline vertices. Piecewise-linear only.
  - Create `sim/GameSim.Tests/Crafting/ForgeScorerTests.cs`, `ForgePathTests.cs`.
- **Approach:** deviation = Σ|actual − ideal| per sample, bucketed into 3 path zones → 3 sub-scores
  (per-mille, higher = tighter). Fold with integer weights `(s*300 + f*400 + q*300)/1000` →
  GradePermille. Moments: `ForgedInOneHeat` (no bellows after first climb), `NeverScorched`,
  `PerfectQuench` (tail deviation < threshold), `RecoveredFromTheBrink` (touched crack zone, finished ≥ Fine).
- **Test scenarios:** perfect trace → 1000/1000/1000 + all-clean moments; worst trace → floor;
  deviation monotonicity (a strictly-worse trace never scores higher); path generator determinism
  (same inputs → identical polyline, cross-run hash-stable); moments truth table; fold-weight pin;
  empty/short trace rejected gracefully.
- **Verification:** fast lane green; scorer referenced nowhere yet (no golden shift this unit).

### U23c — Wire `ForgeScorer` into `ApplyCraft` (sim)
- **Goal:** Blacksmith active-craft consumes `ForgeTraceInput` the way the alchemist consumes
  `AlchemyReagentPuzzle`.
- **Files:** Modify `sim/GameSim/Crafting/CraftingHandlers.cs`:
  - Extend the puzzle-type guard (line ~82) to accept `ForgeTraceInput` for a blacksmith active recipe.
  - Extend the grade branch (line ~108): `action.Puzzle is ForgeTraceInput trace ? ForgeScorer.Score(recipe!, trace, talents, profession).GradePermille : ...`.
  - Sub-scores now come from the scorer, not `action.SubScores`, when a trace is present; pass into `ItemForge.Forge`. Moments → the new field (U23a).
  - **U19 interplay:** signing already reads the resulting `CraftSubScores` — verify a Masterwork trace with all sub-scores ≥950 signs. Add the moments-guarantee only if U5 opts in.
- **Verification:** fast lane green **including golden re-baseline** (orchestrator captures new SHA via
  temp-write trick, documents reason in `AtomicEquivalenceTests`); an integration test: a hand-forge
  trace yields the same RNG draw count as an auto-craft (null puzzle).

### U23d — Anvil Map Godot overlay (hard-replace)
- **Goal:** Replace the three-beat `ForgeMinigame` overlay with the 2D Anvil Map; emit
  `ForgeTraceInput` integers.
- **Files:**
  - Rewrite `godot/scripts/minigames/ForgeMinigame.cs` as a 2D `Control` overlay: X=shape, Y=heat;
    hammer (Space/click), bellows (hold), quench-plunge finale. Real-time via the proven accumulated
    `Advance(delta)` clock (no wall-clock, no engine RNG). Sample cursor at fixed cadence → integer
    trace. **Delete** the old three-bar beat logic (no orphan code).
  - Modify the craft-submit call site (grep for `PerformanceGrade =` / `SubScores =` in `godot/scripts/`)
    to build a `ForgeTraceInput` instead of a Godot-computed grade.
  - Modify `godot/tests/minigames/ForgeMinigameTests.cs`: scripted `Advance` + injected input →
    assert emitted trace integers. **No 3D SubViewport, no frame pump** (headless hang trap).
  - History entry #0: on craft, mint the first `Item.History` entry from the moments (data/presentation).
- **Verification:** `dotnet test godot/tests --settings .runsettings` green; overlay renders windowed
  (visual check via `tools/shoot.ps1 -State Forge`).

### U23e — Batch echo (rules; own unit; balance-gated)
- **Goal:** After a hand-forge, the next K copies of the SAME recipe SAME day auto-craft at
  `max(550, grade − decay*n)` — deterministic integers, no extra draws.
- **Files:** Modify `sim/GameSim/Crafting/CraftingHandlers.cs` (or a small `BatchEchoState` on
  `PlayerState` if per-recipe memory is needed — trailing init member, save-compat). Add
  `sim/GameSim.Tests/Crafting/BatchEchoTests.cs`.
- **Approach:** track last hand-forged (recipe, grade, day, count) on `PlayerState` (trailing field).
  Decay constant tuned against the balance gate. Auto-craft of a DIFFERENT recipe or a new day resets.
- **Verification:** fast lane green (golden re-baseline for the new `PlayerState` field — shape-only,
  BaselinePlayer never hand-forges so behavior unchanged); **balance gate 25/25 in band** (this is the
  one unit that can move economics — decay must not let repeat-crafting flood Masterworks).

## Verification Contract
- Fast lane `Category!=Balance` green after every unit.
- Balance gate `Category=Balance` green — **mandatory for U23e** (economics change).
- Godot engine suite green (U23d) — property-only, no frame pump.
- Golden `AtomicEquivalenceTests` re-baselined deliberately at U23c and U23e (shape-only), documented.
- Human playtest gate: Wave 5 is DONE only when the new forge feels better than the old beats in the
  user's hands (hard-replace risk). Surface a `tools/shoot.ps1 -State Forge` capture + a play session.

## Definition of Done
All units merged to main; golden re-baselines documented; balance in band; the Anvil Map hard-replaces
the old overlay with no orphan code; an excellent forge deterministically earns a Signed Work via the
existing U19 proc; batch echo removes repeat-craft tedium without flooding Masterworks; forging writes
History entry #0.

## Scope Boundaries (non-goals)
- Sub-score → stat tilt (smelt→Defense etc.) — later; balance rabbit hole.
- Hand-authored signature paths — later; generated-only for this wave.
- Forge audio layers, gamepad/haptics, persistent scorch/crack decals on the 3D model — later.
- Trace pre-reduction in Godot — only if replay-log size becomes a real problem.

## Build order / division
1. **U23a** contracts micro-PR — orchestrator (opus), lands first.
2. **U23b + U23d** are largely independent (pure sim scorer vs Godot overlay) — divide to two sonnet
   workers in parallel (disjoint files). U23b before U23c.
3. **U23c** (opus integrates; owns golden re-baseline).
4. **U23e** last (sonnet impl, opus owns golden + balance gate).
