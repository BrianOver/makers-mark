# U-D2 — Guild Assessment heartbeat + Confidence meter

**Status:** in progress
**Owner:** agent (worktree `agent-a63dd0ff46fffb234`, branch `feat/phased-ud2-guild-heartbeat`)
**Plan:** `docs/plans/2026-07-21-008-phaseD-economy-arc.md` — U-D2 section.

## Scope
- Reuse `RentState.ConfidencePermille` (0-1000, already stored, doc-flagged as "a hook for a
  later unit... deliberately NOT wired yet") as THE Confidence meter — do NOT add a second,
  parallel confidence field.
- New `GuildAssessmentState` (Contracts, additive): 7-day dues cadence, escalating ×~1.5/period,
  separate from the existing 10-day `RentState` cadence (kept untouched).
- New `Economy/GuildAssessmentSystem.cs` (Morning, held-Morning guarded like `RentSystem`):
  passive daily decay, + depth-record / attribution-beat / hero-death deltas read off
  yesterday's `EventLog` (same pattern as `GossipSystem`), the 7-day dues due/pay/miss cycle,
  and legible threshold consequences (rival-share bump reusing `RivalMarketSharePermille`,
  a named hero "considers leaving" event, a telegraphed soft-fail event at 0).
- Full era-reset mechanics ("restart era keeping talents+recipes") are U-D5 (prestige era,
  POST-v1, explicitly deferred) — U-D2 only telegraphs the collapse (event + flag), does not
  implement the reset.

## Files touched (additive only in Contracts)
- `sim/GameSim/Contracts/World.cs` — `GuildAssessmentState` record + `GameState.Assessment`.
- `sim/GameSim/Contracts/Events.cs` — 5 new event records + JsonDerivedType entries.
- `sim/GameSim/Economy/GuildAssessmentSystem.cs` — new system.
- `sim/GameSim/GameComposition.cs` — insert `new GuildAssessmentSystem()` between `RentSystem`
  and `DestitutionRecoverySystem` (append, no reorder of existing pairs).
- `sim/GameSim.Tests/Economy/GuildAssessmentSystemTests.cs` — new unit tests.
- `sim/GameSim.Tests/Balance/GuildAssessmentBalanceTests.cs` — new Category=Balance test.

## Golden pins
- `AtomicEquivalenceTests` SHA — expected to FAIL (idle trace now moves). Left failing per
  instructions; new SHA reported in the final message.
- `PhaseBNoDrawGateTests` RngState pin — expected to stay GREEN (zero new RNG).
