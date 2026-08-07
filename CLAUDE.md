# Maker's Mark — agent operating rules

Inverted-MMO game: player = blacksmith NPC, autonomous AI heroes raid the Mine. Plan: `docs/design/MAKERS-MARK.md` §11 + the active wave doc in `docs/plans/`, if one exists. Read both before working.

## Commands

```bash
# Fast lane (no Godot needed) — run before reporting ANY work done
dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance

# Balance gate (100-day sim, exists from U10)
dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance

# Engine tests (needs Godot; GODOT_BIN via .runsettings or env)
dotnet test godot/tests --settings .runsettings

# Console play (from U13)
dotnet run --project sim/GameSim.Cli

# Batch telemetry farm (seed sweep, chronicles to runs/)
dotnet run --project sim/GameSim.Cli -- batch --seeds 20 --days 100

# Analytics + anomaly report over runs/
dotnet run --project tools/Analytics -- runs

# Build everything
dotnet build Game.sln
```

Debugging anything? `docs/debugging.md` — deterministic repro recipe, log map, known failure shapes.

## Hard rules

1. **Tests green before done.** No work is reportable as complete until the fast lane passes locally and CI is green on the PR.
2. **Engine pin.** Godot 4.6.3-stable .NET ONLY (`.godot-version` is the source of truth). Never open or re-save `godot/` with any other editor version — newer editors silently rewrite scenes/import metadata and break CI.
3. **TargetFramework lives in `Directory.Build.props` only — EXCEPT `godot/GodotClient.csproj`, which pins `net10.0` explicitly.** Godot injects `net8.0` whenever the element is absent (import + gdUnit4Net adapter rebuilds), so absence is the hazard there. Never add a TFM to any other csproj, and never commit a `net8.0` value anywhere.
4. **Sim purity (KTD2).** `sim/GameSim/` has ZERO Godot references. All game rules live there. `godot/` is adapter-only: render state, submit actions. No RNG outside the kernel's injected stream; no wall-clock reads in the sim; no transcendental `Math.*` in sim code (cross-OS float drift).
5. **Determinism.** Same seed + same actions = identical state. The golden-replay test enforces it; breaking it is a build-failing defect.

6. **One plan.** `docs/design/MAKERS-MARK.md` §11 is the only plan. `docs/plans/` holds at most TWO wave docs — the executing wave and the queued one, each granted by name in §11.6 rule 4. A third means planning has outrun shipping. A plan doc lives on `main` from its first commit: a plan only on a branch does not exist.

7. **Docs die on merge.** The PR landing a wave's last unit deletes its wave doc. No COMPLETE stamp, no archive folder — git history is the archive. Any doc no open PR has referenced in 14 days is abandoned twice over at this repo's cadence: delete it.

8. **Git outranks every doc.** No checkboxes, status banners, or shipped/unwired counts in any doc — progress lives in `git log` and PRs. A doc caught asserting what git contradicts is **deleted in your current PR, not corrected.** A stale doc is not clutter; it is an instruction the next session obeys.

9. **Branch = open PR. Worktree = live session.** A branch with no open PR is deleted on sight, local and remote (`gh pr list --state merged` is the truth — squash-merge makes `git branch --merged` a liar). Remove every worktree you created before the session ends; max 5 exist at once, since engine tests serialize anyway and more workers only queue collisions. Worker prompts name their base ref, and the worker's FIRST command greps for a symbol the prompt claims is already there — a miss is stop-and-report, never reimplement.

10. **Raw output outranks any harness.** Completion reports quote the runner's own `Failed: N, Passed: N` line, never a wrapper's verdict. A wrapper computing PASS from an exit code is itself the defect — `tools/engine-test.ps1` has done it twice.

## Multi-agent rules

- **Lane model:** core lanes (VISUALS / AI-NPC / ENGINE-DEPLOY) + addon swarm + orchestrator — charters, per-lane deny-list amendments, gates, and the CONTRACT-REQUEST escalation format live in `docs/design/lane-operating-model.md`. Cross-lane gates + seam broadcasts: `.claude/tasks/BOARD.md` (read at session start and after any rebase failure).
- **Directory ownership:** one agent owns one unit's directory exclusively. Claim it in `.claude/tasks/` (see README there) before starting.
- **Deny-list — never edit unassigned:** `Game.sln`, `godot/project.godot`, `.github/`, `sim/GameSim/Contracts/`, `CLAUDE.md`, `global.json`, `Directory.Build.props`, `.godot-version`.
- **Contract amendments:** changes to `sim/GameSim/Contracts/` land as dedicated micro-PRs authored by the orchestrating session only, merged before dependent module PRs; in-flight agents rebase.
- **Branches/PRs:** one unit = one branch (`feat/uN-slug`) = one small PR. Ruleset requires green checks + branch up to date; auto-merge is on — rebase and re-run when stale.
- **Commits:** conventional messages (`feat(sim): ...`, `ci: ...`). No `git add .` — stage the unit's files.

## Layout

- `sim/GameSim/` — pure .NET sim core: `Contracts/` (shared types), `Kernel/`, `Harness/` (scripted player policies — pure, no IO/RNG/clock), then per-module dirs (`Crafting/`, `Heroes/`, `Expedition/`, `Economy/`, `Drama/`, `Bounties/`)
- `art/` — asset pipeline: `GameArt/` (AssetSpec contract + registry, orchestrator-only), `GameArt.Tests/`, `specs/<module>/` (fan-out-owned)
- `sim/GameSim.Tests/` — xUnit; `Category=Balance` for the 100-day sim
- `sim/GameSim.Cli/` — console runner (first playable surface)
- `godot/` — Godot 4.6.3 .NET project; `scripts/` = C# adapters, `scenes/`, `tests/` = gdUnit4Net
