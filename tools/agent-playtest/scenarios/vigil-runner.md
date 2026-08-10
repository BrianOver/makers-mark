# Scenario: vigil-runner

Distinct from `-Scope Diff` (aims attention at changed *surfaces*): this probes a named *behaviour*
-- the send-supply verb at the camp's vigil stop -- regardless of what changed recently.

## Setup

A short, deterministic advance-spam that carries a fresh campaign from day 1 Morning through the
first vigil stop. Grounded in static analysis of the sim, not a live run (this unit's own hard
constraint) -- traced here so a human can check it fast rather than trust it blind:

- Every hero's `DeepestFloorReached` starts at 0, so day 1's only party always targets floor 1
  (`ExpeditionSystem.TargetFloorFor`). `CheckpointFor(1) = min(1, 1-1) = 0`, which is "unstaged" --
  day 1 resolves the whole expedition immediately, with no camp and no vigil
  (`sim/GameSim/Expedition/ExpeditionSystem.cs`).
- Day 2's party then targets floor 2 (`DeepestFloorReached` is now 1). `CheckpointFor(2) =
  min(1, 2-1) = 1`, which STAGES it: the party clears stage 1 and camps below the checkpoint, and
  `RaidConductor.Resync` reports `beat: "VigilStop"` the instant `state.Phase` is `Camp` with
  `state.InFlight` non-empty (`godot/scripts/RaidConductor.cs`).
- Five phase transitions per day (`GameKernel.Advance`: Morning -> Expedition -> Camp ->
  ExpeditionDeep -> Evening -> Morning) puts the first vigil at advance #7. `advance` is this
  harness's own bridge command (`AgentPlaytest.ApplyAdvance` -> `SimAdapter.AdvancePhase` directly,
  never refused) -- NOT a press of the client's own "AdvancePhase"/Skip button, which reopens the
  camp modal instead of ticking past a live vigil. A raw `advance` has no such gate, so
  agent-playtest.ps1 stops replaying this list the INSTANT `state.beat` reads `VigilStop`, whichever
  command that lands on, rather than trusting the count and risking a tick straight through it. The
  12 entries below are a safety margin (covers a Morning counter-hold or a `NoRaidToHost` detour),
  not a promise every one fires.

KNOWN GAP, flagged rather than fixed: reaching the vigil says nothing about whether the player is
HOLDING a sendable item -- `CampPanel`'s Send button needs a player-crafted consumable in hand plus
the runner's fee in gold (`godot/scripts/panels/CampPanel.cs`, `RenderParty`). This Setup does not
craft one (a forge craft is a real-time shape-then-quench minigame this card does not attempt to
script blindly, and getting that wrong un-verified would be worse than naming the gap). If the fresh
campaign's day-1/day-2 economy leaves nothing held, the verb is still OFFERED (disabled, with its own
reason text) but a send cannot complete -- the verdict below will then honestly read NOT SEEN rather
than fabricate a delivery. The first live run (this wave's own "instrument shakedown" sweep) is
where this gets confirmed, or this Setup gets a craft step added.

```json
[
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
