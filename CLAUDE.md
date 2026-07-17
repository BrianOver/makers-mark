# Maker's Mark — agent operating rules

Inverted-MMO game: player = blacksmith NPC, autonomous AI heroes raid the Mine. Plan of record: `docs/plans/2026-07-13-001-feat-inverted-mmo-game-plan.md` (13 units, U-IDs). Read the plan's Goal Capsule + your unit before working.

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

## Multi-agent rules

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
