# BOARD — cross-lane gates + claims (orchestrator-owned)

Single broadcast channel (`docs/design/lane-operating-model.md` §6). Lanes read this at
session start and after any rebase failure. Everything else is per-claim.

## Gates (live status)

| Gate | What | Blocks | Owner | Status |
|---|---|---|---|---|
| G1 | U1 contracts micro-PR merged | U2 | orchestrator | **OPEN — PR up, auto-merge armed** |
| **G2** | **V5a 5-phase tolerance merged (cross-lane hard deadline)** | **U2** | **VISUALS** | not started — VISUALS first action |
| G3 | U2 kernel (5-phase) merged | U3 | AI-NPC | waiting on G1+G2 |
| G4 | U3 staging + band re-fit + registration line merged | U4; V5b choreography | AI-NPC (+ orchestrator merge) | waiting on G3 |
| G5 | U4 camp verbs merged | U5 narrator; telemetry-plan U4 | AI-NPC | waiting on G4 |
| G6 | gdUnit4Net stable 4.7 → V0 infra PR merged | V4b town migration | ENGINE (VISUALS verifies, orchestrator merges) | watching upstream |
| G7 | O1 LFS infra merged | V2, V3-gen PNG commits | ENGINE (orchestrator merges) | not started — ENGINE first action |
| — | Wave-1 addons: no gates, run anytime (operating-model §10) | — | swarm | open |

## Open claims

(none — stale P2/P3/U4/U5/U11/U12-era claims stamped `done`; new claims per operating-model §5)

## Seam changes (dated; every contracts/GameComposition/registration merge gets a line)

- 2026-07-17: U1 contracts micro-PR opened (`chore/contracts-staged-resolution`) — DayPhase +Camp/+ExpeditionDeep, ExpeditionHalt, camp actions/events, InFlightExpedition, GameState.InFlight, 5 save pins. All lanes rebase once merged.
