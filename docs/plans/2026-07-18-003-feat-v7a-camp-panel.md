---
title: "feat: V7a — camp decision panel (Send/Recall slate in Godot)"
date: 2026-07-18
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
origin: grind schedule 2026-07-18-002 D1 (B1 orchestrator spec, B4 worker execution)
execution: code — VISUALS worker (single unit)
related: docs/plans/2026-07-17-002-feat-staged-resolution-plan.md (U4 defines the handlers this panel drives)
---

## Goal Capsule

The camp decision window (staged resolution's whole point) must be PLAYABLE in Godot: when a
party parks at Camp, the player sees the winch-house slate (who's below, hp, heals left,
target vs checkpoint) and can send ONE supply or ring the recall bell — or hold. This is DoD
D1's only new Godot surface. Ships functional, not beautiful (schedule §4).

## Grounding (read these, cite in PR)

- Panel pattern: `godot/scripts/panels/BountyPanel.cs` — `SimPanel` base, `EnsureBuilt()` in
  `_Ready`, `Refresh()` reads `Adapter.CurrentState`, verbs call `Adapter.Queue(action)`.
- Tab wiring: `godot/scripts/MainUi.cs` (tab list pinned by `MainUiTests` titles assertion —
  ADDING a tab means updating that pin; alternative below avoids it).
- Contracts (merged #34): `GameState.InFlight` (`ImmutableList<InFlightExpedition>`),
  `PartyCampReport` event (Party, CampedBelowFloor, TargetFloor, HpByHero, HealsLeftByHero),
  `SendSupplyAction(HeroId To, ItemId Item)`, `RecallPartyAction(HeroId Member)`.
- Handlers: land in staged-U4 (Camp-phase-only acceptance, one-delivery rule, fee sink,
  rejection reasons). The panel NEVER enforces rules itself — it submits and renders
  `TickResult.Rejected` reasons verbatim (AE4 legibility pattern).

## Decisions (locked)

1. **Surface: extend `TavernPanel`?? No — new `CampPanel` inside the existing Town tab area is
   wrong too. Use a MODAL slate like `LedgerModal`** (`godot/scripts/panels/LedgerModal.cs`
   pattern): auto-opens when `Adapter.CurrentState.Phase == DayPhase.Camp` AND `InFlight` is
   non-empty; closable (hold = close). No new tab → `MainUiTests` tab pin untouched.
2. **Supply picker**: list the player's HELD consumables (`state.Player` inventory of
   Consumable-slot items) + camped heroes; one send disables the button (server-side
   `SupplySent` is the truth — panel re-reads state after tick).
3. **Recall**: one button per party, confirmation not required (it's reversible-by-design in
   game terms — banking is safe).
4. **Rejection surfacing**: a `RejectionLabel` renders `TickResult.Rejected` reasons for camp
   actions — exact strings from U4 handlers.
5. **Fee display**: read the fee constant via the U4-exposed const (worker: find it in
   `CampHandlers` when U4 merges; display "Runner: Ng").

## Files

- Create: `godot/scripts/panels/CampPanel.cs` (LedgerModal-style modal, SimPanel-compatible
  refresh), `godot/tests/CampPanelTests.cs`.
- Modify: `godot/scripts/MainUi.cs` — ONLY the modal instantiation + phase-change hook
  (mirror LedgerModal's wiring); no tab changes.

## Test scenarios (gdUnit4Net, engine lane)

1. Camp phase + non-empty InFlight → modal visible; lists every camped hero with hp/heals.
2. Not Camp phase (or empty InFlight) → modal absent/hidden.
3. Send supply: pick hero+item → `SendSupplyAction` queued with exact ids (ScriptedSession
   inspection, `UiTestSupport.Click`).
4. Recall: button → `RecallPartyAction` queued with a party member's id.
5. Rejected camp action → reason string rendered verbatim.
6. Hold: close modal → no action queued; day advances normally.

## Definition of done

Engine lane green (`dotnet test godot/tests/GodotClient.Tests.csproj --settings .runsettings`);
fast lane untouched-green; all 6 scenarios; no sim edits; no tab-pin churn.

## Dependencies / gates

U4 merged (gate G5) — handlers + rejection strings must exist before this worker spawns.
Claim: `V7a-camp-panel`, branch `feat/v7a-camp-panel`, worktree `../Game-v7a`.
