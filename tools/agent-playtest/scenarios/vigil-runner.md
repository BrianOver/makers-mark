# Scenario: vigil-runner

Distinct from `-Scope Diff` (aims attention at changed *surfaces*): this probes a named *behaviour*
-- the send-supply verb at the camp's vigil stop -- regardless of what changed recently.

## Setup

A short, deterministic sequence that first crafts a sendable consumable, then advance-spams a fresh
campaign from day 1 Morning through the first vigil stop. Grounded in static analysis of the sim and
the client, not a live run (this unit's own hard constraint) -- traced here so a human can check it
fast rather than trust it blind:

- `SendSupplyAction` (`sim/GameSim/Expedition/CampHandlers.cs`, `ApplySend` guards 5-8) needs an item
  that is a consumable (`Effect` non-null), `PlayerCrafted`, not shelved, not on the rival's shelf, not
  already in a hero's pack, plus gold >= the runner's fee (`SupplyFee(checkpointFloor) = 6 + 3*floor`;
  day 2's checkpoint floor is 1, so 9g).
- `field-salve` is exactly that item, craftable from the DEFAULT campaign state: it is a Blacksmith
  recipe (`RecipeTable.All["field-salve"]`, pinned by `ConsumableRecipeTests.FieldSalve_MatchesTheDesignContract`),
  `Slot=Consumable`/`Effect=Heal(6)`, needs 2 copper, and `PlayerState.NewGame` selects `"blacksmith"`
  by default (`sim/GameSim/Contracts/Player.cs`) -- no second profession or talent unlock required
  (`CraftLegal`, `sim/GameSim/Advisor/ActionLegality.cs:366`, skips the tier gate entirely for tier-1
  recipes since Blacksmith's `TierGate` only has entries for tiers 2/3).
- `Town2D`'s constructor spawns the player AT THE FORGE DOOR every session (`Player.SpawnAt(forgeDoor)`,
  `godot/scripts/town2d/Town2D.cs:459-460`) -- so turn 1's first command can `key interact` immediately;
  "forge" has an `InteriorLayout2D` row, so this routes to `Town.EnterInterior("forge")`
  (`godot/scripts/MainUi.cs`, `OnTownBuildingClicked`), not the plain drawer.
- Inside the interior, the player spawns one tile north of the room's door tile (12,13), i.e. tile
  (12,12) (`RoomSpec.DoorTile`'s own doc, `godot/scripts/town2d/InteriorLayout2D.cs`). The anvil station
  sits at tile (12,7) (`WorkshopVocab.cs`) -- 5 tiles / 80px due north, at 16px/tile
  (`TownLayout2D.TileSize`). `PlayerController2D.Speed` is 90px/s (`godot/scripts/town2d/PlayerController2D.cs`),
  so a generous `move up` overshoots on purpose: the anvil's own solid footprint collision halts
  `MoveAndSlide` at its edge rather than letting the player walk through it, so overshooting the frame
  count is safe where undershooting would not be.
- Interacting with the anvil opens the SAME `ForgePanel` drawer any station on this room opens
  (`Focus:"craft"` just picks which section it scrolls to) -- `ScreenObservation.FindVisibleButtonByName`
  and `ObservedControls` only check `IsVisibleInTree()`, never viewport/scroll bounds
  (`godot/scripts/tools/ScreenObservation.cs`), so every button on the panel (vendor row AND craft row)
  is a legal `press` target the instant the drawer is open, regardless of which section it scrolled to.
- `BuyMat_copper` (`ForgePanel.cs:471`) queues `BuyMaterialAction("copper", 1)`, Morning-only legal
  (`ActionLegality.cs:57`); copper costs 4g/unit (`MaterialRegistry.UnitPrice`+`MaterialVendorHandlers.QuoteCost`'s
  25% markup), so two presses buy the 2 copper `field-salve` needs for 8g total -- well inside the 100g
  starting balance (`GameFactory.StartingPlayerGold`) alongside the 9g runner fee due on day 2.
- `Craft_field-salve` (`ForgePanel.cs:643`, `OnCraftPressed`) queues a BARE `CraftAction("field-salve",
  "copper")` -- no `Puzzle`, no `PerformanceGrade` -- the plain craft button next to (not the) minigame
  path, present for every recipe regardless of profession (`craftLabel` is "Auto-craft (competent)" for
  an active-craft profession like Blacksmith, but it is the SAME button, SAME bare `CraftAction` shape).
  `CraftingHandlers.ApplyCraft` accepts a null `Puzzle` unconditionally (every puzzle-shape guard is
  gated on `action.Puzzle is not null`) -- this predates the forge minigame (U7) and stays kernel-legal
  after it, so scripting this needs no minigame input at all.
- `key cancel` closes the `ForgePanel` drawer afterward (Escape closes modals --
  `godot/tests/EscapeClosesModalsTests.cs`) so the model's first real turn sees `town`, not a leftover
  open panel blocking `Surroundings()`/nearby data.
- After that, every hero's `DeepestFloorReached` starts at 0, so day 1's only party always targets
  floor 1 (`ExpeditionSystem.TargetFloorFor`). `CheckpointFor(1) = min(1, 1-1) = 0`, which is
  "unstaged" -- day 1 resolves the whole expedition immediately, with no camp and no vigil
  (`sim/GameSim/Expedition/ExpeditionSystem.cs`).
- Day 2's party then targets floor 2 (`DeepestFloorReached` is now 1). `CheckpointFor(2) =
  min(1, 2-1) = 1`, which STAGES it: the party clears stage 1 and camps below the checkpoint, and
  `RaidConductor.Resync` reports `beat: "VigilStop"` the instant `state.Phase` is `Camp` with
  `state.InFlight` non-empty (`godot/scripts/RaidConductor.cs`).
- Five phase transitions per day (`GameKernel.Advance`: Morning -> Expedition -> Camp ->
  ExpeditionDeep -> Evening -> Morning) puts the first vigil at advance #7 (counting only `advance`
  commands -- the craft prefix above advances nothing). `advance` is this harness's own bridge command
  (`AgentPlaytest.ApplyAdvance` -> `SimAdapter.AdvancePhase` directly, never refused) -- NOT a press of
  the client's own "AdvancePhase"/Skip button, which reopens the camp modal instead of ticking past a
  live vigil. A raw `advance` has no such gate, so agent-playtest.ps1 stops replaying this list the
  INSTANT `state.beat` reads `VigilStop`, whichever command that lands on, rather than trusting the
  count and risking a tick straight through it. The 12 `advance` entries below are a safety margin
  (covers a Morning counter-hold or a `NoRaidToHost` detour), not a promise every one fires.

```json
[
  "{\"action\":\"key\",\"target\":\"interact\",\"why\":\"setup: enter the forge (player spawns at its door every session)\"}",
  "{\"action\":\"move\",\"dir\":\"up\",\"frames\":80,\"why\":\"setup: approach the anvil station (5 tiles north of the interior spawn; overshoot is safe, its own collision halts the walk)\"}",
  "{\"action\":\"key\",\"target\":\"interact\",\"why\":\"setup: interact with the anvil -> opens the Forge panel\"}",
  "{\"action\":\"press\",\"target\":\"BuyMat_copper\",\"why\":\"setup: buy 1 copper (1 of 2 needed for field-salve)\"}",
  "{\"action\":\"press\",\"target\":\"BuyMat_copper\",\"why\":\"setup: buy 1 more copper (2 of 2 needed for field-salve)\"}",
  "{\"action\":\"press\",\"target\":\"Craft_field-salve\",\"why\":\"setup: craft field-salve directly (bare CraftAction, no minigame -- still kernel-legal)\"}",
  "{\"action\":\"key\",\"target\":\"cancel\",\"why\":\"setup: close the Forge panel before the advance-spam begins\"}",
  "{\"action\":\"advance\",\"why\":\"setup: day 1 Morning -> Expedition\"}",
  "{\"action\":\"advance\",\"why\":\"setup: day 1 Expedition -> Camp (floor 1, unstaged, no vigil)\"}",
  "{\"action\":\"advance\",\"why\":\"setup: day 1 Camp -> ExpeditionDeep\"}",
  "{\"action\":\"advance\",\"why\":\"setup: day 1 ExpeditionDeep -> Evening\"}",
  "{\"action\":\"advance\",\"why\":\"setup: day 1 Evening -> day 2 Morning\"}",
  "{\"action\":\"advance\",\"why\":\"setup: day 2 Morning -> Expedition\"}",
  "{\"action\":\"advance\",\"why\":\"setup: day 2 Expedition -> Camp (floor 2, staged -- expect VigilStop here)\"}",
  "{\"action\":\"advance\",\"why\":\"setup: safety margin 1 -- should not fire if VigilStop was already seen\"}",
  "{\"action\":\"advance\",\"why\":\"setup: safety margin 2\"}",
  "{\"action\":\"advance\",\"why\":\"setup: safety margin 3\"}",
  "{\"action\":\"advance\",\"why\":\"setup: safety margin 4\"}",
  "{\"action\":\"advance\",\"why\":\"setup: safety margin 5\"}"
]
```

## Brief

A party is camped in the mine; get a supply to them.

## Expected observation

The camp card offers a send-supply verb and the ledger names the delivery.

## Backend predicate

An `action` row (kind `action`, field `action`) equal to `SendSupplyAction` -- `PlaytestLog.Action`
records the kernel-accepted action's own type name the instant `CampPanel.OnSend` queues it
(`godot/scripts/PlaytestLog.cs`), the closest mechanical proxy the log can offer for `SupplyDelivered`
firing: event TYPES are never broken out in the log itself, only a raw per-tick COUNT survives (see
`backend.ps1`'s own `AttributionCaveat`).

```json
{"kind":"action","field":"action","equals":"SendSupplyAction"}
```
