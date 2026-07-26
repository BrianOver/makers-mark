# U-C4-second-venue — 2nd venue go-live + hero->venue routing
- lane: dedicated-agent
- agent: claude-agent-a98c62a23b88f9366
- status: in-progress
- branch: feat/phasec-uc4-second-venue
- pr: none (not opened per task instructions)
- owned dirs: sim/GameSim/Venues/, sim/GameSim/Materials/, sim/GameSim/Expedition/ExpeditionSystem.cs, sim/GameSim/Heroes/MusterSystem.cs, sim/GameSim/Heroes/RaidForecast.cs, sim/GameSim/Heroes/CommissionSystem.cs, sim/GameSim.Tests/Venues/, sim/GameSim.Tests/Materials/, sim/GameSim.Tests/Heroes/ (call-site updates only)
- must not edit: CLAUDE.md deny-list (Game.sln, godot/project.godot, .github/, sim/GameSim/Contracts/, CLAUDE.md, global.json, Directory.Build.props, .godot-version) — respected, zero Contracts edits
- test command: `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance` and `--filter Category=Balance`
- gates: none (per plan doc "M1 material registry first" — already complete pre-existing; this unit lands routing + go-live)

## Escalations
none

## Log
- 2026-07-26: claimed, M1 registry confirmed already complete, landing Gloomwood go-live + VenueRouter.
- 2026-07-26: done — Gloomwood live in VenueRegistry.LiveRotation, its 4 ores live in
  MaterialRegistry.PricedPool, new VenueRouter (draw-free utility+queue-length comparator) wired into
  ExpeditionSystem + MusterPlan.Compute. Fast lane green except the 2 named golden pins
  (AtomicEquivalenceTests SHA, PhaseBNoDrawGateTests RngState) + 2 ObjectiveAdvisorTests.QualityStall_*
  (confirmed routing-trace collateral via stash-diff against pre-change baseline, not a logic bug).
  Balance gate: core BalanceSimTests/CampProvisioning/ConsumableTraitMortality all green; 4
  FactionTariffBalanceTests + 1 SalveProvisioningBalanceTests fail (Deepvein standing accrual ~halved
  since only Mine ore raises it and routing now sends ~half of parties to Gloomwood; salve-engagement
  trajectory shifted on seed 2026) — left for orchestrator's consolidated Phase C re-baseline pass,
  same rationale as the golden pins. Full detail in final report.
