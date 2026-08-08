# Maker's Mark — agent operating rules

## The game, before anything else

**A specific person's fate provably turned on work your hands did, and you were watching when it happened.**

*Emberbite turned the killing blow on floor 3. Torvald lives.*

That line is the product. You are the blacksmith NPC in someone else's RPG; six autonomous heroes raid the Mine on their own judgment and die permanently. Five links carry the sentence, and every one of them is a real mechanism:

1. **You make a thing, and it is provably yours** — every craft is stamped `MakersMark`, and the whole chain keys on that stamp.
2. **It reaches a hero through four honest channels** — shelf, counter, commission, vigil runner. Each ends with the hero deciding.
3. **The hero carries it into the dark on their own judgment** — parties form without you and pick their own depth.
4. **The game proves it mattered** — a counterfactual replay of the recorded fight, with your item removed. Only player-crafted items earn beats. There is no participation credit.
5. **The outcome becomes the town's memory, with your name in it** — ledger, gossip, legends wall, chronicle, memorial.

Six decisions are what the game is actually made of: sell the good one or hold it for the hero who needs it; price for the sale or the relationship; fill the empty slot or upgrade the full one; spend the slot or bank it; buy the ore or buy the goodwill; send the runner or trust their judgment.

**Read this before you pick up work, not after.** The failure this heading exists to prevent is real and has happened repeatedly: a session opens this file, reads the commands and the rules, reasons entirely about process, and spends the night fixing whatever was broken in front of it. Fifteen playtest findings and three rounds of scattered fixes did not move the owner's complaint; the structural waves did. Work that serves no link above is work that made the repo tidier and the game no better.

So: **before taking work, name the link or plan item it serves — that is the `Serves:` line, and it chooses the work rather than justifying it afterwards.** If nothing you are about to do serves a link, do not build it and do not stop to ask: book it and take the topmost item on the critical path (§11.4) that does. Autonomy is the point; drifting while autonomous is the thing to avoid. What the game IS lives in `docs/design/THE-GAME.md`; the plan of record is `docs/design/MAKERS-MARK.md` §11, and the owner's standing direction is §11.7.

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

1. **Merged is done. Nothing else is.** No work is reportable as complete until the fast lane passes locally, CI is green, and the PR is squash-merged to `main`. Arm `gh pr merge --auto --squash --delete-branch` in the same breath as `gh pr create` — every PR, docs included. A green unmerged PR is not done; it is inventory, and inventory is where work goes missing.
2. **Engine pin.** Godot 4.6.3-stable .NET ONLY (`.godot-version` is the source of truth). Never open or re-save `godot/` with any other editor version — newer editors silently rewrite scenes/import metadata and break CI.
3. **TargetFramework lives in `Directory.Build.props` only — EXCEPT `godot/GodotClient.csproj`, which pins `net10.0` explicitly.** Godot injects `net8.0` whenever the element is absent (import + gdUnit4Net adapter rebuilds), so absence is the hazard there. Never add a TFM to any other csproj, and never commit a `net8.0` value anywhere.
4. **Sim purity (KTD2).** `sim/GameSim/` has ZERO Godot references. All game rules live there. `godot/` is adapter-only: render state, submit actions. No RNG outside the kernel's injected stream; no wall-clock reads in the sim; no transcendental `Math.*` in sim code (cross-OS float drift).
5. **Determinism.** Same seed + same actions = identical state. The golden-replay test enforces it; breaking it is a build-failing defect.

6. **One plan.** `docs/design/MAKERS-MARK.md` §11 is the only plan. `docs/plans/` holds at most TWO wave docs — the executing wave and the queued one, each granted by name in §11.6 rule 4. A third means planning has outrun shipping. A plan doc lives on `main` from its first commit: a plan only on a branch does not exist.

7. **Docs die on merge.** The PR landing a wave's last unit deletes its wave doc. No COMPLETE stamp, no archive folder — git history is the archive. Any doc no open PR has referenced in 14 days is abandoned twice over at this repo's cadence: delete it.

8. **Git outranks every doc.** No checkboxes, status banners, or shipped/unwired counts in any doc — progress lives in `git log` and PRs. A doc caught asserting what git contradicts is **deleted in your current PR, not corrected.** A stale doc is not clutter; it is an instruction the next session obeys.

9. **Branch = open PR. Worktree = live session.** A branch with no open PR is deleted on sight, local and remote (`gh pr list --state merged` is the truth — squash-merge makes `git branch --merged` a liar). Remove every worktree you created before the session ends; max 5 exist at once, since engine tests serialize anyway and more workers only queue collisions. Worker prompts name their base ref, and the worker's FIRST command greps for a symbol the prompt claims is already there — a miss is stop-and-report, never reimplement.

10. **Raw output outranks any harness.** Completion reports quote the runner's own `Failed: N, Passed: N` line, never a wrapper's verdict. A wrapper computing PASS from an exit code is itself the defect — `tools/engine-test.ps1` has done it twice.

11. **Merge is not a question, and merged is not deployed.** "Want it merged?", "Merge order?", "Say the word" — those sentences do not exist here. Review happens on `main`; `git revert` is the undo, not an unmerged branch. A turn may not end while a PR this session opened sits green-and-unmerged, or a commit sits unpushed. And landing on `main` is still not the game: the playable checkout is the shared root, `play.bat` is its only sync point, and a completion report quotes `origin/main`'s SHA and says what will launch. A report that stops at "PR opened" is reporting work that does not yet exist.

12. **The seven laws are the game, and they are executable.** Influence never orders; no timers on decisions; every verb changes an outcome or reveals the player's stake; show only what the sim decided; sim purity and determinism (rules 4–5); no runtime LLMs in the sim; skipping stays legal and its cost is named in copy, never engineered. Each law has a tripwire test tagged `LAW:` — `ConstitutionTests` pins the set both ways, so deleting a tripwire or editing this list alone is a red build. Work that bends a law goes red, and the fix is never softening the test: the only door is a pinned exception citing the owner ruling (`§11.7.x` or `P<n>`) that grants it, and exception counts are pinned, so every grant is a red-then-reviewed diff in a compiled file. Erosion arrives as a hundred reasonable PRs, not one bad one — so every PR body carries one line, `Serves: P<n>` / `Serves: link<1-5>` / `Serves: substrate` / `Serves: overhead — booked` (§11.6 rule 3), and the week's merged list is auditable in one grep. The receipt can lie; the census cannot; a false receipt is a rule-8 lie living in git and is treated as one.

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
