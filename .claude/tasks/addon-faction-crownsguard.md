# addon-faction-crownsguard — 2nd faction pack (wave-1 packet)
- lane: addon (orchestrator-spawned)
- agent: swarm worker
- status: done (registered at G8 flip — material registry PR)
- branch: feat/addon-faction-crownsguard
- pr: <pending>
- owned dirs: sim/GameSim/Factions/Crownsguard/, sim/GameSim.Tests/Factions/Crownsguard/
- must not edit: everything else; own ore keys (single-supplier invariant); only faction packet in flight (shared FactionPack.cs voicing untouched — {faction} slot already generic)
- test command: fast lane + Balance (baseline trades only with Deepvein — bands must not move)
- gates: none

## Escalations
CONTRACT-REQUEST (2026-07-17, worker + orchestrator concur): a 2nd faction cannot register green -
FactionConformanceTests requires every supplied ore key to be a Mine floor key AND priced by
OrePricing (universe = copper/iron/steel/mithril/adamant, all Deepvein's), so own-keys and
known-keys are jointly unsatisfiable until the material registry (addon-guide "coming registries")
lands. Orchestrator disposition: pack ships INERT (its tests are registry-independent); new BOARD
gate G8 = material-registry core lands + electrum/orichalcum priced; registration line
`Crownsguard.CrownsguardFaction.Definition,` (FactionRegistry.All, after Deepvein) applies at G8.

## Log
- 2026-07-17: stub cut by orchestrator (v2.1); follow docs/addon-guide.md "Adding a faction" verbatim
