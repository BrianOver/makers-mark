# BOARD — cross-lane gates + claims (orchestrator-owned)

**COORDINATION V2.1 IN EFFECT (2026-07-17 evening)** — operating-model **§13**: up to 3 parallel
core sessions on file-disjoint feature tracks + orchestrator-spawned addon swarm, all GitHub seams
automated by the orchestrator (gate flips AT merge, claim stubs on main, `done` stamped at merge,
registry-line PRs never auto-merge). Lifecycle rules mandatory in every bootstrap prompt: worktree
first; never ask the user routing/status questions (gate truth = `git show
origin/main:.claude/tasks/BOARD.md`, and a MERGED gate PR beats a stale BOARD line); gated = next
ungated queue item, else escalate (push branch + claim) and move on; exit only on empty queue.

Single broadcast channel (`docs/design/lane-operating-model.md` §6). Lanes read this at
session start and after any rebase failure. Everything else is per-claim.

**WORKTREE RULE (mandatory, 2026-07-17):** the `c:\Code\Game` checkout is SHARED by all
concurrent sessions — never `git checkout`/commit a work branch there. Every lane works in
its own worktree: `git worktree add .claude/worktrees/<lane> -b <branch> origin/main` (path is
gitignored; sibling `C:\Code\Game-*` folders are the old convention — create no new ones; the
human playable build is the `play/` worktree in the root). A branch switch
in the shared root already caused one cross-session commit collision (rode into #35, benign).

> **NOTE (2026-07-18 reconcile):** the gate/claim tables below are **historical** — every listed
> gate has flipped and every claim is stamped. A merge collision between #44 and #52 committed raw
> git conflict markers into this file; this reconcile collapses them to merged reality from
> `git log` / merged PRs. The current plan of record is `docs/plans/2026-07-13-001` plus the
> next-phase wave `docs/plans/2026-07-18-004` (foundation) and `-005..-008` (phase plans).

## Gates (historical — all flipped; truth = git log)

| Gate | What | Blocks | Owner | Status |
|---|---|---|---|---|
| G1 | U1 contracts micro-PR merged | U2 | orchestrator | **MERGED (#34, 2026-07-17)** |
| G2 | V5a 5-phase tolerance merged (cross-lane hard deadline) | U2 | VISUALS | **MERGED (#38, 2026-07-17)** |
| G3 | U2 kernel (5-phase) merged | U3 | AI-NPC | **MERGED (#43, 2026-07-17)** |
| G4 | U3 staging + band re-fit + registration line merged | U4; V5b choreography | AI-NPC (+ orchestrator merge) | **MERGED (#51, 2026-07-18) — bands held UNCHANGED, no re-fit needed** |
| G5 | U4 camp verbs merged | U5 narrator; telemetry-plan U4 | AI-NPC | **MERGED (#54, 2026-07-18)** |
| G6 | gdUnit4Net stable 4.7 → V0 infra PR merged | V4b town migration | ENGINE (VISUALS verifies, orchestrator merges) | historical — see git log (was: watching upstream) |
| G7 | O1 LFS infra merged | V2, V3-gen PNG commits | ENGINE (orchestrator merges) | **MERGED (#35, 2026-07-17)** |
| G8 | Material registry (M1/T7a): OrePricing+RecipeTable derive from registry; new keys priceable | Crownsguard registration; all faction packs (T7b) | orchestrator merges (core worker builds) | **MERGED (#62, 2026-07-18) — Crownsguard live** |
| — | Wave-1 addons: no gates, run anytime (operating-model §10) | — | swarm | closed — wave-1 complete |

## Open claims (historical — all stamped done)

| Claim | Lane | Status | Branch | Notes |
|---|---|---|---|---|
| V5a-phase-tolerance | visuals | done (#38) | feat/v5a-phase-tolerance | G2 flipped |
| O1-lfs-art | engine | done (#35) | ci/lfs-art | G7 flipped; ENGINE session closed |
| U2-five-phase-kernel | ai-npc | done (#43) | feat/u2-five-phase-kernel | G3 flipped; orchestrator-salvaged after session closure |
| V1-pipeline-scripts | visuals | done (#46) | feat/v1-pipeline-scripts | pipeline scripts + runbook landed |
| V4a-subviewport-shell | visuals | done (#45) | feat/v4a-subviewport-shell | IconRegistry.Lit + town shell landed |
| addon-art-heroes | addon (spawned) | done (#40) | feat/addon-art-heroes | V3-gen unblocked |
| addon-tanning | addon (spawned) | done (#41) | feat/addon-tanning | registered live |
| addon-faction-crownsguard | addon (spawned) | done (#42; registration live via #62/G8) | feat/addon-faction-crownsguard | material registry landed, Crownsguard live |

## Seam changes (dated; every contracts/GameComposition/registration merge gets a line)

- 2026-07-17: **U1 contracts MERGED (#34)** — DayPhase +Camp/+ExpeditionDeep, ExpeditionHalt, camp actions/events, InFlightExpedition, GameState.InFlight, 5 save pins. All 3 CI lanes green (engine lane unaffected, as planned). **All lanes rebase before next push. G1 open for U2 — waiting only on G2 (V5a).**
- 2026-07-17: **O1 LFS MERGED (#35, gate G7 flipped)** — `godot/assets/art/**/*.png` on LFS, tavern pair renormalized, engine-tests job cache-first `git lfs pull`. **V2 / V3-gen unblocked (VISUALS). All lanes rebase.** Note: #35 also carries the G1 BOARD flip (checkout-collision stray, benign).
- 2026-07-17 evening: **V5a MERGED (#38, gate G2 flipped)** — Evening/default split (unknown-phase no-op), loop-until-Morning AdvanceDay across 20 call sites, beyond-max phase test. **AI-NPC U2 unblocked.** All lanes rebase.
- 2026-07-17 night: **wave-1 swarm CLOSED** — hero specs MERGED (#40), Tanning MERGED registered (#41), Crownsguard MERGED INERT (#42, gate G8 cut — material registry). U2 salvaged from closed session's worktree, PR #43.
- 2026-07-18: **WEEKEND PLANS ADOPTED** — master completion plan `docs/plans/2026-07-18-001` (M-units, T-packets, rulings R1-R9, Horizon-1/2) + grind-block schedule `docs/plans/2026-07-18-002` (B1-B10, DoD D1-D6, tag v1-playable). Single band-mover this weekend: U3. All work spawns from the orchestrator session per §13.
- 2026-07-17 evening: **COORDINATION V2.1 adopted (operating-model §13)** — parallel file-disjoint core tracks + automated GitHub seams (critic pass applied: claim stubs on main, gate-flip-at-merge, orchestrator stamps done/babysits, registry-line PRs never auto-merge, escalations push before parking). ENGINE session closed until engine work exists. Swarm wave-1 orchestrator-spawned: addon-art-heroes → addon-tanning → addon-faction-crownsguard.
- 2026-07-18: **BOARD RECONCILED (next-phase U1)** — removed committed conflict markers (#44 vs #52 collision at the G3/G4/G5 rows), collapsed gate/claim tables to merged reality from git log (G3 #43, G4 #51, G5 #54, G8 #62; V1 #46, V4a #45), marked tables historical. Next-phase wave begins: Playable Core (`docs/plans/2026-07-18-005`), worked one unit = one branch = one PR from the orchestrating session.
