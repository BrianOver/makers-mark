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
its own worktree: `git worktree add ../Game-<lane> -b <branch> origin/main`. A branch switch
in the shared root already caused one cross-session commit collision (rode into #35, benign).

## Gates (live status)

| Gate | What | Blocks | Owner | Status |
|---|---|---|---|---|
| G1 | U1 contracts micro-PR merged | U2 | orchestrator | **MERGED (#34, 2026-07-17)** |
| **G2** | **V5a 5-phase tolerance merged (cross-lane hard deadline)** | **U2** | **VISUALS** | **MERGED (#38, 2026-07-17) — U2 UNBLOCKED** |
| G3 | U2 kernel (5-phase) merged | U3 | AI-NPC | waiting on G1+G2 |
| G4 | U3 staging + band re-fit + registration line merged | U4; V5b choreography | AI-NPC (+ orchestrator merge) | **MERGED (#51, 2026-07-18) — bands held UNCHANGED, no re-fit needed** |
| G5 | U4 camp verbs merged | U5 narrator; telemetry-plan U4 | AI-NPC | worker spawned (B2) |
| G6 | gdUnit4Net stable 4.7 → V0 infra PR merged | V4b town migration | ENGINE (VISUALS verifies, orchestrator merges) | watching upstream |
| G7 | O1 LFS infra merged | V2, V3-gen PNG commits | ENGINE (orchestrator merges) | **MERGED (#35, 2026-07-17)** |
| — | Wave-1 addons: no gates, run anytime (operating-model §10) | — | swarm | open |

## Open claims (live lock registry — orchestrator writes stubs at CUT, stamps `done` at merge)

| Claim | Lane | Status | Branch | Notes |
|---|---|---|---|---|
| V5a-phase-tolerance | visuals | done (#38) | feat/v5a-phase-tolerance | G2 flipped |
| O1-lfs-art | engine | done (#35) | ci/lfs-art | G7 flipped; ENGINE session closed |
| U2-five-phase-kernel | ai-npc | in-progress | feat/u2-five-phase-kernel | G1+G2 flipped — push when green |
| V1-pipeline-scripts | visuals | cut | feat/v1-pipeline-scripts | next VISUALS queue item |
| V4a-subviewport-shell | visuals | cut | feat/v4a-subviewport-shell | queued after V1 |
| addon-art-heroes | addon (spawned) | cut | feat/addon-art-heroes | swarm wave-1; unblocks V3-gen |
| addon-tanning | addon (spawned) | cut | feat/addon-tanning | swarm wave-1 |
| addon-faction-crownsguard | addon (spawned) | cut | feat/addon-faction-crownsguard | swarm wave-1; only faction packet in flight |

## Seam changes (dated; every contracts/GameComposition/registration merge gets a line)

- 2026-07-17: **U1 contracts MERGED (#34)** — DayPhase +Camp/+ExpeditionDeep, ExpeditionHalt, camp actions/events, InFlightExpedition, GameState.InFlight, 5 save pins. All 3 CI lanes green (engine lane unaffected, as planned). **All lanes rebase before next push. G1 open for U2 — waiting only on G2 (V5a).**
- 2026-07-17: **O1 LFS MERGED (#35, gate G7 flipped)** — `godot/assets/art/**/*.png` on LFS, tavern pair renormalized, engine-tests job cache-first `git lfs pull`. **V2 / V3-gen unblocked (VISUALS). All lanes rebase.** Note: #35 also carries the G1 BOARD flip (checkout-collision stray, benign).
- 2026-07-17 evening: **V5a MERGED (#38, gate G2 flipped)** — Evening/default split (unknown-phase no-op), loop-until-Morning AdvanceDay across 20 call sites, beyond-max phase test. **AI-NPC U2 unblocked.** All lanes rebase.
- 2026-07-17 evening: **COORDINATION V2.1 adopted (operating-model §13)** — parallel file-disjoint core tracks + automated GitHub seams (critic pass applied: claim stubs on main, gate-flip-at-merge, orchestrator stamps done/babysits, registry-line PRs never auto-merge, escalations push before parking). ENGINE session closed until engine work exists. Swarm wave-1 orchestrator-spawned: addon-art-heroes → addon-tanning → addon-faction-crownsguard.
