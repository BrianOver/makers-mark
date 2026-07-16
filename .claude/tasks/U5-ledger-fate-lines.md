# U5 — Ledger fate lines through the engine (catalog-adaptation plan 2026-07-16-002)
- agent: claude-worker (U5, feat/catalog-adaptation)
- status: done (uncommitted — orchestrator commits)
- branch: feat/catalog-adaptation
- owned dirs: sim/GameSim/Flavor/Packs/LedgerPack.cs, sim/GameSim/Drama/LedgerQuery.cs, sim/GameSim.Cli/Program.cs (PrintLedger), godot/scripts/panels/LedgerModal.cs, godot/tests (ledger assertions), sim/GameSim.Tests/Drama/LedgerQueryTests.cs, sim/GameSim.Tests/Flavor/LedgerPackTests.cs, docs/addon-guide.md (pack-authoring note)
- must not edit: everything in the CLAUDE.md deny-list + other units' dirs
- test command: dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance
- notes: ReturnCard gains FateLine (pack-rendered). Pick ids: death = stamped HeroDied event id; survivor = StableHash.Mix(day, heroId). Campaign identity = GameState.Rng.Inc (KTD3). Zero RNG. Fallbacks = current CLI fate lines ("{hero}: ..." prefix so the fallback passes slot validation and reproduces today's composed CLI line byte-for-byte).
