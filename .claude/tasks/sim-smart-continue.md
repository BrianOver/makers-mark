# sim-smart-continue — TUNING-C smart continue (competence retreat, verdict C)
- lane: dedicated-agent
- agent: TUNING-C core worker
- status: blocked (impl complete; awaits orchestrator PR + G4/U3-window merge)
- branch: feat/sim-smart-continue
- pr: <pending — orchestrator opens>
- owned dirs: sim/GameSim/Expedition/ (resolver + systems), sim/GameSim.Tests/Expedition/
- must not edit: CLAUDE.md deny-list (Contracts/, Game.sln, Directory.Build.props, ...) + other claims' dirs
- test command: `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` and `--filter Category=Balance`
- gates: G4 (U3 staging + band re-fit) — do NOT merge standalone; orchestrator merges in the U3 window

## Escalations
none — implemented via ResolveStage2 exemption/reconstruction (no InFlightExpedition contract change needed).

## Log
- 2026-07-18 claimed; implementing competence retreat inside ExpeditionResolver.ResolveFloors per direction doc §5.
- 2026-07-18 implemented; fast lane 645 green (incl. 3 pins), Balance 25 green (no band re-fit), golden run-twice green. Sweep deaths 453->381 (-16%, all floors down); tripwire clear. Ready for orchestrator PR + U3-window merge.
