# addon-faction-crownsguard — 2nd faction pack (wave-1 packet)
- lane: addon (orchestrator-spawned)
- agent: swarm worker
- status: cut
- branch: feat/addon-faction-crownsguard
- pr: <pending>
- owned dirs: sim/GameSim/Factions/Crownsguard/, sim/GameSim.Tests/Factions/Crownsguard/
- must not edit: everything else; own ore keys (single-supplier invariant); only faction packet in flight (shared FactionPack.cs voicing untouched — {faction} slot already generic)
- test command: fast lane + Balance (baseline trades only with Deepvein — bands must not move)
- gates: none

## Escalations
none

## Log
- 2026-07-17: stub cut by orchestrator (v2.1); follow docs/addon-guide.md "Adding a faction" verbatim
