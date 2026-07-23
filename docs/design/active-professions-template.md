---
title: Active Professions Template — building the Phase B profession loop
type: reference
status: living — extend when Phase B ships a real puzzle-scored profession
date: 2026-07-21
owner: any session building a Phase B profession (alchemist reagent puzzle, enchanter glyph pattern)
related:
  - docs/plans/2026-07-21-002-feat-active-professions-shop-plan.md
  - docs/design/2026-07-21-active-professions-shop-design.md
  - docs/design/asset-manifest.md
---

# Active Professions Template — building the Phase B profession loop

> **Phase B status (2026-07-23): the ALCHEMIST shipped** as the first in-sim-scored profession
> (branch `feat/alchemist-active-craft`). One deliberate divergence from §1 below: the
> `[JsonPolymorphic]` contracts micro-PR was NOT needed — `SaveCodec` registers the
> `CraftPuzzleInput` → `AlchemyReagentPuzzle` mapping at runtime via a
> `DefaultJsonTypeInfoResolver` modifier (discriminator `"$puzzle"`), keeping `Contracts/`
> untouched. A second puzzle profession (enchanter) adds one `DerivedTypes.Add` line in
> `SaveCodec.AddCraftPuzzlePolymorphism`; if the list grows past a couple of entries, consider
> consolidating into the attribute-based micro-PR this doc originally planned. Reference files:
> `sim/GameSim/Professions/Alchemy/AlchemyReagentPuzzle.cs` / `AlchemyPuzzleScorer.cs` (scorer
> consumes `MinigameAssists` as flat forgiveness — the sim IS the "adapter" for a puzzle
> profession), the `action.Puzzle` branch in `CraftingHandlers.ApplyCraft` (steps 6/8), and
> `godot/scripts/minigames/AlchemyBrewPuzzle.cs` (+ its "Brew" routing in `ForgePanel`).
> The outdoor 3D station (§5) is deferred — the puzzle opens from `ForgePanel`; see the
> asset manifest's "Alchemist station (3D world prop)" row.

Phase A built one profession (blacksmith) end-to-end as a *template*, not a one-off. This doc is
the "no tribal knowledge" handoff for whoever builds the next one (alchemist reagent puzzle first,
per the design's Phase B roster). Read this before touching code. Every code reference below is a
real file at Phase A's integrated head (post-PA9) — if a line number has drifted, the file/class
name is still the right search anchor.

## 0. The two profession shapes, and which one you're building

Phase A's blacksmith is **captured-grade**: Godot runs a real-time minigame, folds it into one
per-mille integer, and the sim only ever sees that integer. The design's Phase B seam consequence
(`docs/design/2026-07-21-active-professions-shop-design.md` §"Seam consequence") calls out that
**alchemist scores entirely in-sim** instead — an ingredient list + grind fractions + path choices
that the SIM itself grades, because that's strictly better balance-gate coverage (no Godot needed
to exercise the full quality distribution in `Category=Balance`). Decide which shape your
profession is before you write a line of code:

| | Captured-grade (blacksmith, PA2/PA6) | In-sim-scored (`CraftPuzzleInput`, alchemist/enchanter target) |
|---|---|---|
| Who computes quality | Godot overlay (`ForgeMinigame.FoldGrade`) | The sim (a new scorer function you write) |
| What rides the action | `CraftAction.PerformanceGrade: int?` | `CraftAction.Puzzle: CraftPuzzleInput?` (your derived type) |
| Balance-gate coverage | Only the auto-craft competent constant (Godot minigame never runs headless) | Full quality distribution, because `BaselinePlayer` can submit real puzzle data deterministically |
| Godot's job | Real-time skill overlay | Presentation of a discrete/turn-based puzzle (still real-time FEELING, but every meaningful choice is a discrete action) |

Both shapes are legal today; `CraftAction` carries both optional params and Phase A never uses
`Puzzle`. Pick per-profession, don't force one pattern on both.

## 1. The dual-mode craft seam — how `CraftPuzzleInput` plugs in

`sim/GameSim/Contracts/Actions.cs`:

```csharp
public sealed record CraftAction(
    string RecipeId,
    string MaterialKey,
    int? PerformanceGrade = null,
    CraftPuzzleInput? Puzzle = null,
    ImmutableList<int>? SubScores = null) : PlayerAction;

public abstract record CraftPuzzleInput;
```

`CraftPuzzleInput` is `abstract` with **zero derived types** in Phase A — deliberately. This is
the load-bearing detail: `System.Text.Json`'s `[JsonPolymorphic]` attribute throws at
configuration/startup time if it decorates a base type with no registered derived types. So the
attribute is NOT on `CraftPuzzleInput` yet. Your Phase B unit's first move, in **one contracts
micro-PR** (Contracts is deny-listed — this lands as the orchestrator-authored PA1-equivalent for
your wave, same KTD9 rule PA1 followed):

1. Add `[JsonPolymorphic(TypeDiscriminatorPropertyName = "$puzzle")]` to `CraftPuzzleInput` in
   `Actions.cs`.
2. In the **same commit**, add your first `[JsonDerivedType(typeof(AlchemyReagentPuzzle), "alchemyReagent")]`
   attribute and define that sealed record, right there in `Contracts/`. The attribute and its
   first derived type MUST land together — never as two commits, never as a bare attribute with an
   empty derived-type list (it will not compile a working serializer; every round-trip test in
   `CounterContractTests`-style coverage will throw at first (de)serialization).
3. Add round-trip + save-compat tests mirroring PA1's `CounterContractTests`: a `CraftAction` with
   your `Puzzle` populated serializes → deserializes to an equal record; a pre-your-PR log/save
   (Puzzle always null) still deserializes fine (it already does — the field predates you).

Then, OUTSIDE Contracts (your own module directory, e.g. `sim/GameSim/Alchemy/`), wire the
consuming side. Today `CraftingHandlers.ApplyCraft` (`sim/GameSim/Crafting/CraftingHandlers.cs`,
step 6) branches only on `profession.ActiveCraft`:

```csharp
var quality = profession.ActiveCraft
    ? QualityRoller.RollActive(recipe, materialGrade, talents, profession.Quality, rng, action.PerformanceGrade)
    : QualityRoller.Roll(recipe, materialGrade, talents, profession.Quality, rng, action.PerformanceGrade);
```

`action.Puzzle` is **never read anywhere in Phase A** — that's your seam. Add a third branch (a
new profession-shape flag or a pattern match on `action.Puzzle is AlchemyReagentPuzzle puzzle`)
that scores the puzzle in-sim (integer-only — KTD2/no transcendental `Math.*`) and returns a
`QualityGrade` the same way the other two paths do. This is new logic in YOUR directory
(`sim/GameSim/Alchemy/` or wherever your profession lives), not an edit to `QualityRoller` unless
the grade-band table itself is genuinely shared — prefer a new scorer function over a bigger
branch in the shared roller.

**Why this is strictly better for the balance gate:** `BaselinePlayer` (`sim/GameSim/Harness/BaselinePlayer.cs`)
can construct a deterministic `AlchemyReagentPuzzle` value directly — no Godot, no real-time
capture — so `Category=Balance` exercises your profession's FULL quality distribution, not just an
auto-craft floor constant the way blacksmith's headless path does.

## 2. The Counter — extension points, not a rebuild

The counter loop (`sim/GameSim/Counter/`) is profession-agnostic already: it moves shelved items,
not profession-specific data. You almost certainly don't need to touch it for a new profession —
alchemist potions sell through the SAME `PresentItemAction`/`HaggleResponseAction` loop a
blacksmith dagger does. Know these four files before you touch anything:

- `CounterHandlers.cs` — action validation + immediate resolution. Read the class-level doc
  comment FIRST: `HaggleResponseAction` resolves **immediately** (not deferred to the systems
  pass) because `Contracts/` is frozen and there's no spare field to stash intent across the
  handler/systems boundary the way `PresentItemAction` uses `CounterState.Presented`. If you ever
  need a new deferred field, that's a CONTRACT-REQUEST, not a workaround.
- `CounterQueueSystem.cs` — the systems pass that runs once after every action in a batch resolves
  the active customer (approach → present → walk/haggle-open), dequeues the next, and closes the
  session when the queue empties.
- `WillingnessModel.cs` — willingness-to-pay math (per-class price factor, role-fit, budget
  headroom) — reused from the EXISTING `ShoppingAi.EvaluateItem` utility, not a second AI.
- `HaggleResolver.cs` — round resolution: band math, Patience, the pin bonus, Goodwill/mood.

**If your profession needs a new counter verb** (e.g., an alchemist "sample" action before
presenting), that's new state on `CounterState` — which lives in `Contracts/World.cs`, so it's
another CONTRACT-REQUEST, following the exact non-positional/save-compat pattern `CounterState`
itself already uses (`InFlight`/`Venues` precedent, documented on `GameState`). Don't try to smuggle
profession data through an existing field.

## 3. The minigame-overlay skeleton (captured-grade path only)

If your profession IS captured-grade (skip this section for a puzzle profession — see §1), copy
the shape of `godot/scripts/minigames/ForgeMinigame.cs`, not its beats. The reusable skeleton:

1. **Public `Advance(double delta)`.** This is the ENTIRE testability story — `ForgeMinigame`
   drives itself from `_Process(double delta) => Advance(delta)` in real play, but every gdUnit
   test (`godot/tests/minigames/ForgeMinigameTests.cs`) calls `Advance` directly with scripted
   deltas — no wall-clock, no engine RNG, so a scripted "perfect run" produces the exact same grade
   every time. Your beats need the same shape: a pure `Advance(delta)` + discrete input methods
   (`Stop()`/`Strike()`/`Lock()` in the forge's case) that never read `Time.GetTicksMsec()` or
   anything non-deterministic.
2. **`Configure(recipe, materialKey, profession, unlockedTalents)`.** Reads
   `ProfessionDefinition.MinigameAssists` for your profession's retired quality-talents (see §4)
   and derives your difficulty/band parameters from recipe tier + material grade — mirror
   `ForgeMinigame.ComputeDifficultyPermille`'s "better ore eases the act" relationship so the
   player reads the SAME material story the sim's quality ceiling already tells.
3. **Exactly one action, on completion.** `Finished?.Invoke(action)` fires ONCE, carrying a
   `CraftAction(recipeId, materialKey, performanceGrade, Puzzle: null, subScores)`. `Cancel()`
   raises `Cancelled` instead and the caller (your panel) queues NOTHING. Test both paths
   explicitly — "one queued action on completion, zero on cancel" is the single-action contract
   PA6's tests pin (`ForgeMinigameTests`), and it's the thing that keeps replays exact.
4. **The fold is exported data, not sim rules.** `ForgeMinigame.FoldGrade` — a `public static`
   pure function from `ImmutableList<int>` sub-scores to one `int` grade, with the beat WEIGHTS as
   named `const double` fields (`SmeltWeight`/`ForgeWeight`/`QuenchWeight`), not magic numbers
   buried in an expression. Playtest tuning of your fold weights never touches sim code — that's
   the point.
5. **`ForgePanel.cs` is your `ForgePanel` template** for the "Work the forge" button beside the
   relabeled "Auto-craft (competent)" path — copy its wiring into your own panel, don't add
   professions to `ForgePanel` itself.

## 4. Talent remap: retire quality-shift nodes into `MinigameAssist` data

Only applies if your profession flips `ActiveCraft: true` (captured-grade path). Look at
`sim/GameSim/Professions/ProfessionRegistry.cs`'s `Blacksmith` definition as the exact pattern:

- `ProfessionQualityModel.FlatShifts`/`SlotShifts` go EMPTY for an active profession — the old
  roll-shifting nodes must not ALSO shift the active dominance roll (that's the double-count PKD3
  explicitly resolved; a test pins "talents shift the active roll by exactly 0").
- `MaterialMasteryNode` is KEPT — the material axis has no overlap with the minigame and still
  raises the roll's ceiling.
- Each retired node gets a `MinigameAssist(SweetZoneWidthBonus, DriftRateReduction, OffBeatForgiveness)`
  entry in `MinigameAssists`, read by your Godot overlay's `Configure` (see §3.2) — mastery makes
  the ACT easier and is visible in the minigame, never a hidden number.

If your profession is puzzle-scored instead (§1), there's no overlay to widen bands for — your
puzzle-scorer function reads the SAME unlocked-talent set directly and can shift the in-sim score
however you define it (that logic lives entirely in your sim-side scorer, no Godot involvement,
no `MinigameAssist` needed at all).

## 5. Station + camera dolly hookup

`godot/scripts/town3d/Town3D.cs` already has the interaction-volume pattern for your outdoor
station prop:

```csharp
("forge-station", "Anvil", "ForgeStation", new Vector3(-8f, 0f, -12f)),
("counter-station", "Counter", "CounterStation", new Vector3(8f, 0f, -12f)),
```

Add a `("your-station", "DisplayName", "YourStationKey", position)` tuple, a
`BuildYourStationCluster()` primitive-mesh builder (copy `BuildAnvilFurnaceCluster` — stacked
`BoxMesh`es, logged in the asset manifest as `primitive`, never shipped as final art), and register
it in the `"forge-station" => BuildAnvilFurnaceCluster()` switch. Then in `MainUi.cs`'s
`OnTownBuildingClicked`, add your `if (building == "YourStationKey")` branch calling
`Town.Camera.PushIn(Town.FindBuilding("your-station"), StationPushInDistance)` before opening your
panel/overlay — this is the ENTIRE PA8 pattern, copy it exactly rather than re-deriving it.
`CameraRig.Release()` on overlay close, mirroring the forge/counter close handlers.

## 6. Your per-unit asset-manifest duty

Every placeholder you ship — station props, overlay art, item icons — gets a row in
`docs/design/asset-manifest.md` **in the same PR**, following the existing two-row split pattern
for a station that has both an interior focus AND an outdoor world prop (see the Forge/Shop-counter
rows: "X station (interior focus)" vs "X station (3D world prop)" — don't collapse them into one
row, and don't duplicate a row across two units either; if two units touch the same slot, ONE row
gets updated, not a second one added — PA9 had to dedup exactly that mistake from PA7/PA8, see the
manifest's git history). Priority: P1 if it's something the player looks at during the core loop
(station, overlay chrome), P2/P3 for ambient dressing. Status starts `primitive` or `needed`, never
skip logging it "because it's temporary" — temporary is exactly what this file tracks.

## 7. Quick-reference: files you'll touch, by shape

**Puzzle-scored profession (recommended for alchemist, per design):**
- `sim/GameSim/Contracts/Actions.cs` — `[JsonPolymorphic]` + your first `[JsonDerivedType]`, ONE
  commit (contracts micro-PR, orchestrator-authored)
- `sim/GameSim/<YourProfession>/` — new module dir: puzzle record, in-sim scorer, recipe/talent data
- `sim/GameSim/Crafting/CraftingHandlers.cs` — the `action.Puzzle` branch (small, additive)
- `sim/GameSim/Professions/ProfessionRegistry.cs` — register your `ProfessionDefinition`
- `sim/GameSim/Harness/BaselinePlayer.cs` — construct a deterministic puzzle value for the balance gate
- `godot/scripts/panels/` — a presentation-only puzzle UI (discrete choices, no minigame skill test needed)
- `docs/design/asset-manifest.md`

**Captured-grade profession (mirrors blacksmith exactly):**
- Everything above EXCEPT the Contracts change (you use the existing `PerformanceGrade`/`SubScores` fields)
- `sim/GameSim/Crafting/QualityRoller.cs` — only if your grade→band table genuinely needs to differ
  from the shared active-dominance math (usually it doesn't — reuse `RollActive`)
- `godot/scenes/minigames/` + `godot/scripts/minigames/` — your beats, `Advance`-driven (§3)
- `godot/scripts/town3d/`, `MainUi.cs` — station + dolly hookup (§5)
- `docs/design/asset-manifest.md`

## 8. What NOT to copy from Phase A

- Don't touch `sim/GameSim/Kernel/GameKernel.cs`'s Morning-hold — it's profession-agnostic already;
  your craft/counter actions ride the existing phase machine unchanged.
- Don't add a second AI/utility system for your profession's counter economics — `WillingnessModel`
  already derives from the shared `ShoppingAi.EvaluateItem`; profession-specific counter behavior
  should be data (price factors, role-fit tables), not new code paths.
- Don't hardcode your profession into `ForgePanel`/`ForgeMinigame`/`Town3D`'s forge branch — those
  are the BLACKSMITH's instances of the pattern, not shared infrastructure. Build your own
  panel/overlay/station alongside them.
