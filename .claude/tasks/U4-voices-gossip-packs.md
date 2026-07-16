# U4 — Voice profiles + gossip on packs (catalog-adaptation plan 2026-07-16-002)
- agent: claude-worker (U4, feat/catalog-adaptation)
- status: done (uncommitted — orchestrator commits)
- branch: feat/catalog-adaptation
- owned dirs: sim/GameSim/Flavor/ (VoiceProfile, Packs/), sim/GameSim/Drama/ (GossipGenerator, GossipSystem), sim/GameSim.Tests/Drama/GossipTests.cs, sim/GameSim.Tests/Flavor/TavernPackTests.cs
- must not edit: everything in the CLAUDE.md deny-list + other units' dirs
- test command: dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance
- notes: key scheme = "<baseKey>/<voice>"; beats use beat-type camelCase as base key (killingBlow, lethalSave, breakpointClear, provisioned, potionLifesave). Voices frozen: gruff, dramatic, wry, omen. Campaign identity = GameState.Rng.Inc (KTD3). Balance gate left for integration.
