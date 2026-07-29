---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
type: feat
title: Close the open work — wiring, teardown, and the feel-test gate
date: 2026-07-28
product_contract_source: ce-plan-bootstrap
origin: docs/plans/2026-07-28-003-roadmap-post-skeleton.md
roadmap: docs/plans/2026-07-28-003-roadmap-post-skeleton.md
---

# Close the open work — wiring, teardown, and the feel-test gate

## Goal Capsule

Six items of **already-begun** work are open, and one of them — merged sim code that nothing calls —
is actively misleading: a future session reading `main` would see tanning and engineering scorers with
full test coverage and reasonably conclude those professions are interactive. They are not.

This plan closes all six, in dependency order, and ends at the gate the roadmap has been pointing at
since the skeleton completed: **a human actually playing the game**. There is no new design here. Every
unit either finishes something half-landed or deletes something dead.

## Scope Boundaries

- **No new systems, no new content.** Post-skeleton design work is deliberately held until after U6
  (the feel-test), because the feel-test's findings should shape it. See roadmap §5.
- **No resurrection of the phantom waves.** A full 5-need Zubek `NeedsEngine` and a second ending
  design were both on the task list and both contradict shipped decisions (`2026-07-25-002` rejected
  the former; #230 shipped the latter). They stay deleted.
- **One re-baseliner only.** U1 is the sole unit that may move the golden replay. Nothing else in this
  plan touches the RNG stream.
- **No `.github/` edits.** CI throughput work stays parked pending owner sign-off (roadmap §10.4).

## Key Technical Decisions

- **KTD-A — The `ActiveCraft` flip lands alone, behind the balance gate.** Flipping it changes which
  quality-roll path a profession takes (`QualityRoller.RollActive` vs `Roll`), so it can move both the
  golden replay and the balance envelope. This is exactly why #265 merged the scorers inert. U1 is one
  PR, re-run against the 39/39 balance baseline, and it does not share a PR with any overlay work.
- **KTD-B — The advisor parity test becomes reflective, not enumerated.** The existing hand-written
  parity check is what let four Phase-D action types fall through `_ => false` unnoticed. Fixing the
  four cases without fixing the test just resets the clock. The test must discover action types from
  Contracts (reflection over the `PlayerAction` hierarchy) so a new action fails the build until it is
  mirrored.
- **KTD-C — Teardown deletes tests too, deliberately.** The ~17 test files exercising `town3d/` are not
  coverage; they are coverage of code no player can reach, paid for on every CI run. They go with the
  code they test. Anything that turns out to be *shared* rather than 3D-specific gets rewritten against
  the 2D layer instead of deleted — decide per file, not in bulk.
- **KTD-D — Gestures terminate in integer seam methods.** Carried forward unchanged from
  `2026-07-28-002` KTD-A/B: `ScrapeCell(int)`, `Place(int,int)`, quantized drags, keyboard parity
  mandatory. The two new overlays follow the pattern the forge and cauldron already established.

---

## Implementation Units

### U1. Wire tanning + engineering into crafting  *(the flip moved to U3b — see the correction)*

> **CORRECTION, 2026-07-29.** This unit originally bundled the `ActiveCraft` flip and let U2/U3 depend
> on it. **That ordering ships a regression, and the attempt proved it.** The flip changes what happens
> when *no* overlay is present, and with the overlays unbuilt every tanning/engineering craft becomes
> auto-craft:
>
> - `QualityRoller.RollActive` on a null puzzle uses the synthetic auto-craft grade 550 ± 25 jitter,
>   landing in Common or Fine and hard-capped at Superior (PKD4). Measured: Superior → **Fine** on
>   seed 4242 for both professions.
> - `RollActive` deliberately never reads `FlatShifts`/`SlotShifts` (the PKD3 double-count fix), so the
>   entire quality-talent chain becomes **dead data** — a player unlocks Master Tanner for "+8 quality"
>   and receives nothing. That is exactly why alchemy retired its shift chain into `MinigameAssists` at
>   its own flip; any profession going active must do the same remap.
>
> So the flip is now **U3b**, landing *with* the overlays, so there is always a way to earn the grade.
> U1 is the inert wiring only, which is safe alone and makes the overlay work reviewable in isolation.
> Shipped as #269.

**Goal:** the two scorers merged in #265 stop being unreachable code — the handlers accept both puzzle
shapes and score them — with **no behaviour change yet.**

**Files:**
- Modify: `sim/GameSim/Crafting/CraftingHandlers.cs` — extend the unsupported-puzzle guard (~line 91)
  to accept `TanningScrapeInput` and `EngineeringAssemblyInput`; add profession gates mirroring the
  existing alchemy/forge pattern (~lines 96–106); fold both grades into `performanceGrade` (~line 144).

**Verification:** fast lane green and **unchanged** at 1428 — because `ActiveCraft` is still false, both
new gates still reject and the fold is unreachable. That inertness is the deliverable.

---

### U3b. Flip `ActiveCraft`, with the talent remap and the balance gate

**Depends on U2 AND U3** — never lands before both overlays.

**Files:**
- Modify: `sim/GameSim/Professions/Tanning/TanningProfession.cs`, `.../Engineering/EngineeringProfession.cs`
  — `ActiveCraft: true`, **and** retire `FlatShifts`/`SlotShifts` into `MinigameAssists` 1:1 at alchemy's
  50/70/80 ladder. The remap is mandatory, not optional polish: skipping it ships dead talents.
- Modify: both scorers' `AssistBonusPermille` — keep `Armorer` Armor-scoped and `Gadgeteer`
  Trinket-scoped, the way `AlchemyPuzzleScorer` scopes Potent Brews. A slot-scoped talent must not
  silently widen to every recipe.
- Test: re-pin the two auto-craft goldens (they WILL drop to Fine — that is the deliberate PKD4
  consequence, and alchemy's golden did the same at its flip), and re-aim
  `PassiveProfessions_..._NeverActive` to assert (a) the exact set of active professions, so no
  profession flips by accident, and (b) that every active profession has retired its shifts. Half (b)
  is the guard that would have caught the dead-talent bug.

**Verification:** fast lane green; **balance gate re-run** (`--filter Category=Balance`) against the
39/39 baseline; golden-replay outcome stated in the PR body either way — if it moved, by how much and
why that is expected.

**Execution note:** the one unit where a green fast lane is not sufficient evidence. Report the balance
numbers, not a summary of them.

---

### U2. Tanning frame overlay

**Goal:** tanning becomes coverage-with-restraint — scrape the whole hide, don't scrape through.

**Files:** Create `godot/scripts/minigames/TanningFrame.cs`; modify `godot/scripts/panels/ForgePanel.cs`
(overlay routing); test `godot/tests/`.

**Approach:** 8×5 cell grid matching `TanningScrapeScorer.Columns/Rows`; render the *same* patches the
scorer will grade by calling `TanningScrapeScorer.PatchesFor(seed)` — never a parallel implementation.
Drag strokes accumulate into `ScrapeCell(int)` at a fixed pixel threshold; flaws read as visibly
stubborn, thin patches as visibly delicate; dragging the finished hide off the frame submits.

**Patterns to follow:** `AlchemyBrewPuzzle.cs` for the diegetic-scene shape and one-shot FX diffing;
`ForgeMinigame.cs` for drag quantization and the `WouldHit`/seam-method split.

**Verification:** engine suite green; headless test drives real synthesized mouse sequences via the
`GuiInput` event (see `UiTestSupport.Click`), not by calling seams only.

---

### U3. Engineering bench overlay

**Goal:** engineering becomes spatial planning and part identification — no clock anywhere. The
deliberate anti-forge.

**Files:** Create `godot/scripts/minigames/EngineeringBench.cs`; modify `ForgePanel.cs`; test
`godot/tests/`.

**Approach:** sockets from `EngineeringAssemblyScorer.SocketCountFor(recipe)`, wants from
`SchematicFor(recipe)` — again, read the scorer, never re-derive. Drag-drop parts into sockets via
`Place(int,int)`; reseating before submit is free (the scorer keeps only the first placement per
socket, so the UI must not punish what the sim forgives). Several parts are near-duplicates by design —
that is the identification skill. A wind-the-crank finale submits.

**Verification:** engine suite green; a test proving reseat-then-submit scores identically to
first-time-right, so UI and scorer agree on forgiveness.

---

### U4. Advisor legality mirror, 20 → 24, with a reflective parity test

**Goal:** the advisor stops being blind to every Phase-D sink verb.

**Files:** Modify `sim/GameSim/Advisor/ActionLegality.cs` (add `UpgradeForgeAction`,
`BuyForgeSupplyAction`, `MasterworkAttemptAction`, `CommissionLegendaryWorkAction`); modify the existing
advisor parity test.

**Approach:** add the four cases, then rewrite the parity test per KTD-B to enumerate action types by
reflection. Confirm the rewritten test **fails** against the pre-fix mirror before accepting it —
otherwise it is not actually a tripwire.

**Verification:** fast lane green; the reflective test demonstrated red-then-green in the PR body.

---

### U5. Pivot U8 teardown

**Goal:** delete the dead 3D render layer and stop paying for it in CI. Closes `2026-07-27-006`.

**Files:** Delete all 16 files in `godot/scripts/town3d/`; delete the ~17 coupled test files
(`Town3DSceneTests`, `CameraRigTests`, `MineZone*Tests`, `HeroActor3DTests`, `PlayerController3DTests`,
`Building3DInteractionTests`, `GenAsset*Tests`, `*MeshTests`, `TownStation*Tests`, `AmbientLifeTests`,
`TownsfolkNpcsTests` — enumerate from the actual tree, not from this list); modify `ForgePanel.cs`
(drop the stale `using GodotClient.Town3d;`).

**Decision required before starting:** `godot/scripts/panels/MonsterView3D.cs` is **live** —
`BestiaryPanel` and `MineWatch` both render gen monsters through it. Either keep it (a 3D viewport
inside a 2D game) or replace it with the `town2d-monster-*.png` set. Roadmap §10.2.

**Verification:** engine suite green with the deleted tests gone; test count drops and the PR body
states the before/after numbers so the drop is auditable rather than alarming. Real-launch playtest
confirms Bestiary and MineWatch still render.

**Execution note:** per-file judgment, not a bulk delete — anything shared rather than 3D-specific gets
rewritten against the 2D layer.

---

### U6. Land or kill `origin/feat/decision-surface-logger`

**Goal:** stop the drift on an unmerged branch carrying real value.

**Approach:** the branch has no PR and is not an ancestor of `main`. It holds the `decisions` CLI tool,
the gameplay-loop and profession analyses, and a genuine BountyPanel phase/affordability fix
(`defaaf4`). Rebase, split if the docs and the fix want separate review, or cherry-pick the fix and
archive the rest — but decide. Also rule on `origin/docs/overnight-strategy-synthesis`.

**Verification:** either merged with gates green, or deleted with a one-line note in the roadmap's open
items saying what was salvaged.

---

### U7. The human feel-test (the gate)

**Goal:** answer the five questions in roadmap §4 by playing the game.

**Approach:** `play.ps1` (it refuses stale builds). This is not automatable and must not be simulated
by screenshots — the properties under test are timing and resistance, which only hands can judge.

**Verification:** written findings, in the repo, keyed to the five questions. Question 3 ("does a legend
read as a story or a log?") settles the Legend Engine ruling in roadmap §3.

---

## Dependencies

- **U1 depends on nothing** and lands first — it is inert by construction.
- **U2 and U3 do NOT depend on U1's flip.** Each overlay ships DORMANT behind the `ActiveCraft` gate,
  the same way the scorers shipped inert. They are independent of each other and safely parallel
  (disjoint files), and each is reviewable on its own.
- **U3b (the flip) depends on U2 AND U3.** This is the correction: the flip is what activates both
  overlays, so it must never precede them or the professions get strictly worse in the shipped game.
- U4, U5, U6 are independent of everything else and of each other.
- **U7 depends on U3b** — feel-testing four crafts requires four *reachable* crafts, which means the
  flip, not merely the overlays.

## Verification Contract

| Unit | Fast lane | Engine suite | Balance gate | Real-launch playtest |
|---|---|---|---|---|
| U1 | required (unchanged count — inertness IS the evidence) | — | — | — |
| U2 | — | required | — | required |
| U3 | — | required | — | required |
| U3b | required | required | **required** (39/39 baseline) | required |
| U4 | required | — | — | — |
| U5 | — | required (count delta stated) | — | required |
| U6 | required | required | — | — |
| U7 | — | — | — | **is** the verification |

Known flaky pre-step: if the engine suite reports ~54 tests plus `Rebuild Godot Project ends with exit
code: -1`, kill stray `Godot_v4.6.3-stable_mono_win64` processes, re-run with
`--headless --build-solutions --quit`, then re-run the tests. Also `git restore -- '*.import'` before
staging — a build rewrites ~1000 import files.

## Definition of Done

1. All four professions have a real, distinct craft interaction reachable from the running game.
2. No merged sim scorer is unreachable from gameplay.
3. `godot/scripts/town3d/` does not exist; the engine suite is smaller and green.
4. A new Contracts action cannot be added without the advisor parity test failing.
5. `origin/feat/decision-surface-logger` is merged or deleted, not pending.
6. Written feel-test findings exist in the repo, and roadmap §3's Legend Engine ruling is taken.
