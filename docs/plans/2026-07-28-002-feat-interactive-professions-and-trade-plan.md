---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
type: feat
title: Interactive professions, activities and trade
date: 2026-07-28
product_contract_source: ce-plan-bootstrap
origin: docs/design/2026-07-28-002-interaction-design-research.md
---

# Interactive professions, activities and trade

## Goal Capsule

Every craft, trade and activity surface in the game should read as **a physical act the player
performs**, not a form they operate. Today only the blacksmith forge and the alchemy brew have any
interaction at all, and both are driven by buttons; tanning and engineering have none; and the
counter — which already has a complete haggle simulation behind it — is exposed as a SpinBox.

This plan makes the crafts genuinely interactive (four professions, four *different* skills), gives
trading and bounty-posting one real physical verb plus one meaningful decision, and deliberately
leaves the autonomy surfaces (hero roster, tavern, mine watch) hands-off.

Design source (research + citations): `docs/design/2026-07-28-002-interaction-design-research.md`.
That doc's tier split and priority order are adopted as-is; this plan is its execution schedule.

## Scope Boundaries

- **Not** a minigame per surface. Nine performances per day is how games earn minigame fatigue
  (Graveyard Keeper is the cited cautionary case). The tier table below is binding.
- **No new command verbs over heroes.** Heroes are autonomous by premise; a fake verb is worse than
  no verb. The roster gets attention (pins), never orders.
- **No sim rule changes to existing scoring.** New craft inputs are additive records with their own
  pure scorers. Golden-replay determinism must not move.
- **No wall-clock, no adapter RNG.** All animation is accumulated `Advance(delta)`; all path variants
  come from `StableHash(recipeId, day)`.

## Key Technical Decisions

- **KTD-A — Gestures terminate in seam methods.** Every recognizer (`_GuiInput`, drag accumulator,
  drag-and-drop) ends in one public method taking integers/enums (`ForgeStrike()`, `PumpStroke()`,
  `ScrapeCell(int)`, `Place(int,int)`, `AddCoins(int)`). Tests call those directly and never need a
  mouse. This generalizes the pattern `ForgeMinigame` already documents.
- **KTD-B — Drags are quantized before they cross anything.** Raw mouse deltas are accumulated in the
  recognizer and emitted as discrete integer events at fixed pixel thresholds. No float from an input
  device ever reaches a scorer, so determinism is structural.
- **KTD-C — Keyboard parity is mandatory, timing is optional.** Every surface has a keyboard path to
  the same seam methods. Only the forge has a clock at all; everything else is pausable by
  construction. Auto-craft stays a first-class path so no content is gated on reaction speed
  (Game Accessibility Guidelines: offer alternatives to precise timing).
- **KTD-D — New craft inputs are additive integer records.** `TanningScrapeInput(cellPassCounts,
  patchSeed)` and `EngineeringAssemblyInput(placements)` mirror `AlchemyReagentPuzzle`'s shape: flat
  ordered integers, scored by a new pure scorer beside `AlchemyPuzzleScorer`. Contracts land as
  orchestrator-authored micro-PRs before their dependent module PRs (CLAUDE.md lane rules).
- **KTD-E — Shared widgets are orchestrator-owned.** The coin-stack price control and price-tag
  helper live in `UiKit` and are authored once by the orchestrating session, because multiple lanes
  consume them. Lane agents must not edit `UiKit`.

## Tier table (binding)

| Tier | Surfaces | Skill tested |
|---|---|---|
| Full minigame (opt-in vs auto-craft, <=30s) | Forge (timing/pursuit) · Alchemy (memory/sequence) · Engineering (spatial/planning) · Tanning (coverage/restraint) | four different skills, one per profession |
| Light interaction (one physical verb + one real decision) | Counter haggle · Restock/pricing · Bounty posting | judgment, not execution |
| Plain list / ambient | Hero roster (pin only) · Tavern feed · Mine watch (lens only) | attention |

## Implementation Units

### U1. Shared interaction widgets (orchestrator-authored)
- **Goal:** a coin-stack integer control and a price-tag control in `UiKit`, so the counter, restock
  and bounty lanes all express money the same way and learn it once.
- **Files:** modify `godot/scripts/ui/UiKit.cs`; test `godot/tests/UiKitTests.cs`.
- **Approach:** `CoinStack` = a `Control` wrapping `int Value` with `AddCoins(int)/RemoveCoins(int)`
  seams, denomination click zones (100/10/1), scroll = +/-10, typed digits accepted for SpinBox
  parity. `PriceTag` = a small clickable tag rendering an int with the same seam surface.
- **Verification:** unit tests drive `AddCoins/RemoveCoins` and assert `Value`; no mouse needed.
- **Dependencies:** none. **Blocks:** U2, U5, U6.

### U2. Counter desk physicality (presentation-only)
- **Goal:** the haggle the sim already simulates becomes a desk you work: drag the item onto the
  counter mat to present, build a counter-offer by stacking coins, accept with a handshake click,
  and read the customer from posture/expression rather than a chip row. Walk-away reasons are spoken
  by the customer, not logged in a row.
- **Files:** modify `godot/scripts/panels/CounterPanel.cs`; test `godot/tests/CounterPanelTests.cs`.
- **Approach:** existing actions unchanged (`PresentItemAction`, `HaggleResponseAction(Kind, Price)`,
  `CloseCounterAction`). Drag-to-present routes to the same handler the button does; the coin stack
  (U1) composes the counter price; mood/patience per-mille drive sprite posture + a tapping foot.
  Keep every existing button as the keyboard path.
- **Verification:** existing CounterPanel tests stay green; add a test that a drag-drop present and
  a coin-composed counter-offer queue the identical actions the buttons produce.
- **Dependencies:** U1.

### U3. Forge feel pass (presentation-only)
- **Goal:** the strike lands where you aim it, the bellows are pumped, the quench is a drag.
- **Files:** modify `godot/scripts/minigames/ForgeMinigame.cs`; test `godot/tests/ForgeMinigameTests.cs`.
- **Approach:** aimed left-click must hit a generous billet rect (Space stays unaimed and always
  valid); `PumpStroke()` seam emits one heat quantum per N px of downward drag, with held-Shift
  producing identical quanta on the accumulated clock; drag the billet into the trough to plunge
  (Enter still works). Add hit-stop on on-tempo strikes.
- **Verification:** new tests drive `PumpStroke()` and assert heat rises in integer quanta; existing
  strike/plunge/trace tests unchanged.
- **Dependencies:** none (do not touch `UiKit`).

### U4. Alchemy phase 1 — drag-to-pour + memory (presentation-only)
- **Goal:** pouring is carrying a bottle and tipping it; and the recipe must eventually be *known*,
  not read off the screen.
- **Files:** modify `godot/scripts/minigames/AlchemyBrewPuzzle.cs`; test `godot/tests/AlchemyBrewPuzzleTests.cs`.
- **Approach:** click-drag a shelf bottle over the cauldron mouth, release to pour (routes to the
  existing `PourReagent(int)`); dropping elsewhere shelves it harmlessly. Recipe notes show in full
  for a recipe's first brew, then fade — with a recipe book you can hover to re-show at no cost
  (the accessible fallback; there is no timing on this surface).
- **Verification:** existing pour/undo/submit tests unchanged; add a test that notes-hidden state
  never changes the emitted action.
- **Dependencies:** none.

### U5. Restock as placement + price tags (presentation-only)
- **Goal:** dressing the shelf is dragging goods onto it and flipping price tags.
- **Files:** modify `godot/scripts/panels/ShopPanel.cs`; test `godot/tests/ShopPanelTests.cs`.
- **Approach:** drag a craft from a back-room strip onto an empty shelf slot -> existing
  `StockAction`; drag off -> `UnstockAction`; click a price tag (U1) to reprice -> `SetPriceAction`.
  Shelf *position* stays cosmetic — making position matter is a flagged sim-seam change, not to be
  faked in presentation.
- **Verification:** existing stock/price tests stay green; add a test that a drop and a tag edit
  queue the same actions the list rows produce.
- **Dependencies:** U1.

### U6. Bounty poster (presentation-only)
- **Goal:** posting is filling a poster and nailing it to the board.
- **Files:** modify `godot/scripts/panels/BountyPanel.cs`; test `godot/tests/BountyPanelTests.cs`.
- **Approach:** pick the floor by clicking a stratum on a small mine cross-section (teaches the
  mine's shape, replaces the floor SpinBox), set the reward with the coin stack (U1), then drag the
  poster onto the board to post -> existing `PostBountyAction(int,int)`. Tab/arrows/Enter is the
  keyboard path. Hero accept/decline judgments render as sticky notes on the poster.
- **Verification:** existing post tests green; add a test that stratum-click + coins + post queue the
  same action the form produced.
- **Dependencies:** U1.

### U7. Engineering assembly bench (SIM SEAM)
- **Goal:** the first active craft for a passive profession — a spatial, untimed puzzle.
- **Files:** create `sim/GameSim/Professions/Engineering/EngineeringAssemblyScorer.cs`; modify
  `sim/GameSim/Contracts/Actions.cs` (additive puzzle record), `sim/GameSim/Professions/Engineering/EngineeringProfession.cs`
  (`ActiveCraft = true`), `sim/GameSim/Crafting/CraftingHandlers.cs` (accept the new puzzle);
  create `godot/scripts/minigames/EngineeringBench.cs`; modify `godot/scripts/panels/ForgePanel.cs`
  (route the profession to its overlay); tests in `sim/GameSim.Tests/` + `godot/tests/`.
- **Approach:** sockets + a parts tray; drag-and-drop `Place(socketId, partId)`; near-duplicate parts
  so inspection matters; placement order carries a bonus; wind-the-crank finale animates the
  mechanism (correctly placed gears actually turn). Scorer mirrors `AlchemyPuzzleScorer`: exact-socket
  credit, right-part-wrong-socket partial credit, order bonus, `MinigameAssist` widening.
- **Verification:** sim unit tests pin the scorer (exact/partial/order/assist); golden-replay
  unchanged (additive record); engine test drives `Place`/`Submit` headlessly; **balance gate must be
  re-run** because a passive profession becoming active changes attainable quality.
- **Dependencies:** contract micro-PR lands first.

### U8. Tanning scrape frame (SIM SEAM)
- **Goal:** completes "every craft feels different" — motor coverage with restraint, no clock.
- **Files:** create `sim/GameSim/Professions/Tanning/TanningScrapeScorer.cs`; modify
  `sim/GameSim/Contracts/Actions.cs`, `sim/GameSim/Professions/Tanning/TanningProfession.cs`,
  `sim/GameSim/Crafting/CraftingHandlers.cs`; create `godot/scripts/minigames/TanningFrame.cs`;
  modify `godot/scripts/panels/ForgePanel.cs`; tests both sides.
- **Approach:** hide as a coarse cell grid; drag strokes call `ScrapeCell(int)` per newly entered
  cell; each cell wants 1-2 passes, over-scraping wears through; flaw patches (deterministic from a
  path seed) need more. Drag the finished hide off the frame to submit. Arrows+Space is the keyboard
  path. Scorer: per-cell ideal-band credit, worn-through penalty, weighted flaws, assist widening.
- **Verification:** as U7, including the balance gate re-run.
- **Dependencies:** contract micro-PR lands first.

### U9. Muscle-memory batch craft
- **Goal:** perform while learning, delegate once mastered — the anti-fatigue valve.
- **Files:** modify `godot/scripts/panels/ForgePanel.cs` + the overlays' emit path; tests in `godot/tests/`.
- **Approach:** once a recipe scores >= Fine via its minigame, remember that emitted input (it is
  already just data) and offer "repeat that craft" for the same recipe + day-seed variant at a small
  flat grade discount. Re-scoring a stored input is deterministic by construction.
- **Verification:** engine test proves a stored input re-submits and re-scores identically minus the
  discount; no sim change.
- **Dependencies:** U3, U4 (and U7/U8 once they exist).

### U10. Hero pins + mine-watch lens (presentation-only)
- **Goal:** attention, not control.
- **Files:** modify `godot/scripts/panels/HeroPanel.cs`, `godot/scripts/panels/MineWatch.cs`; tests in `godot/tests/`.
- **Approach:** pin up to 3 heroes (client-side preference, never sim state); pinned heroes surface
  first in gossip/watch/counter queues; click a hero in the watch strip to focus the feed on them
  (Tab cycles). Cards flip to a dossier.
- **Verification:** engine tests assert pinning reorders reads and never mutates sim state.
- **Dependencies:** none.

## Verification Contract

1. Fast lane green before any unit is reportable: `dotnet test sim/GameSim.Tests --filter Category!=Balance`.
2. Engine suite green: `dotnet test godot/tests --settings .runsettings`.
3. **Balance gate re-run for U7 and U8 only** (`--filter Category=Balance`); baseline at plan time is
   39/39 passing.
4. Real-launch playtest per wave: boot the actual game, drive each changed surface through **real
   interaction** (not the harness), capture screenshots.
5. Golden-replay determinism untouched throughout.

## Definition of Done

- Four professions each have a distinct interactive craft, each with an auto-craft alternative and a
  keyboard path.
- Counter, restock and bounty each have one physical verb and one real decision.
- Hero roster / tavern / mine watch remain hands-off, with attention affordances only.
- Every gesture is testable through a seam method; every suite above is green; the balance gate is
  re-baselined where professions changed.

## Deferred to Implementation

- Exact pixel thresholds for stroke quantization (tune against the real window).
- Whether alchemy's grind axis (phase 2) is worth its seam change — decide after U7/U8 prove the
  additive-record pattern.
- Shelf position mattering (would be a `StockAction` slot-index seam change) — only if playtests ask.
