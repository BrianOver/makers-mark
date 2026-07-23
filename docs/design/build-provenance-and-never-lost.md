# Never-lost: build provenance + sync discipline (2026-07-21)

Root-cause writeup + permanent guards for the incident where fully-shipped work (Active Professions #157–165) was invisible in playtests.

## What happened (root cause)
The work was **never lost from the repo** — #157–165 were on `main` the whole time. The failure: the **playable checkout `C:\Code\Game\play` (branch `play-3d`) was forked 2026-07-20, the night before the features shipped (07-21), and was never synced.** Every playtest launched a frozen build that predated the features. Two systemic gaps let it go unnoticed:
1. **No sync gate** — nothing forced the played build to be current; you could (and did) launch a weeks-stale checkout.
2. **No provenance** — nothing in-game showed which branch/commit you were running, so "I don't see the feature" was indistinguishable from "the feature isn't built."
Compounding: multiple diverged branches (`play-3d`, `docs/playtest-gate-b`, `main`) with no single "this is THE build to test."

## The guards (permanent)
1. **Launch only via `tools/play.ps1`.** It rebuilds + reimports the current dev build and prints branch + commit + date + dirty-state *before* launching, and writes it to `godot/assets/build_info.txt`. You cannot test a stale build through it.
2. **In-game build stamp.** A corner HUD label reads `build_info.txt` and shows `branch @ sha | date | clean/dirty` — so every screenshot/playtest self-identifies. (Built as part of G1.)
3. **One canonical build.** Do active dev + playtest against ONE checkout. `play-3d` is superseded by `main` (which absorbed its 21 commits via #156) — **delete `play-3d` and its stale local `main`** once the current dev build is merged to `main` (no-orphans policy).
4. **Commit/merge promptly.** Uncommitted worktree work (e.g. the gen-building wiring, G1–G5) is itself a "lost" risk — land it on `main` so `play.ps1` picks it up. Track features in `docs/registry/CONTENT.md`; the build stamp + registry together answer "is this feature in the build I'm running?"

## Rule
**No playtest without `play.ps1`. No "it's not working" without reading the build stamp first.** If the stamp shows a commit older than the feature's PR, that's the bug — sync, don't debug.
