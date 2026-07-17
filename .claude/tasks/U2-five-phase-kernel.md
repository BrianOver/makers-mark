# U2-five-phase-kernel — Kernel: the 5-phase day

- lane: ai-npc
- agent: AI-NPC core lane session (work completed there; orchestrator salvaged after v2.1 session closure)
- status: pr-open
- branch: feat/u2-five-phase-kernel (worktree c:\Code\Game-ai-npc, off origin/main)
- pr: <set at open>
- owned dirs: sim/GameSim/Kernel/GameKernel.cs, sim/GameSim/Bounties/BountyHandlers.cs, sim/GameSim/Harness/BaselinePlayer.cs + day-loop test fixups across sim/GameSim.Tests/** (Kernel, Chronicle, Balance, Drama, Economy, Bounties)
- must not edit: CLAUDE.md deny-list + AI-NPC deny-list (Contracts/, goldens, GameComposition.cs, godot/**, art/**); U2 touches NO GameComposition line (that is U3)
- test command: dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance  AND  --filter Category=Balance
- gates: G1 (flipped #34), G2 (flipped #38) — UNBLOCKED

## Escalations
none

## Log
- 2026-07-17: branch cut from origin/main @ 81c4032. Built U2 fully: Advance 5-phase, BountyHandlers D2 whitelist, BaselinePlayer D5 empty Camp/Deep arms, 2 new tests (EmptyCampAndDeepTicks_DrawNoRng, PostBounty_DuringCamp_IsRejected). Fast lane 481 green; Balance 24 green with UNCHANGED bands (stream-preservation proof); CLI smoke traverses 5 phases into day 2.
- 2026-07-17: PLAN GAP found + fixed — the plan's "grep-verified complete list of *3 sites" missed every day-loop that hardcoded the product (9/18/30/36/90 ticks) and every PREFIX-form multiplier (`3 * ExpiryDays`). 6 tests broke on first run (GoldConservation x2, Bounty x2, RecruitGate, HeroShopChoice); all fixed by aligning to 5 ticks/day. Flavor/gossip NotEmpty tests left untouched (green at reduced day count; avoid flavor-golden churn owned by orchestrator).
- 2026-07-17 late: G2 flipped (#38); AI-NPC session closed (v2.1 single-session model). Orchestrator salvaged the worktree: rebased onto main (post-#40/#41), re-verified, pushed, opened PR.
