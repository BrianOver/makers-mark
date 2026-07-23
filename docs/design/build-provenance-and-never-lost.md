# Never-lost: build provenance + sync discipline (2026-07-21)

Root-cause writeup + permanent guards for the incident where fully-shipped work (Active Professions #157–165) was invisible in playtests.

## What happened (root cause)
The work was **never lost from the repo** — #157–165 were on `main` the whole time. The failure: the **playable checkout `C:\Code\Game\play` (branch `play-3d`) was forked 2026-07-20, the night before the features shipped (07-21), and was never synced.** Every playtest launched a frozen build that predated the features. Two systemic gaps let it go unnoticed:
1. **No sync gate** — nothing forced the played build to be current; you could (and did) launch a weeks-stale checkout.
2. **No provenance** — nothing in-game showed which branch/commit you were running, so "I don't see the feature" was indistinguishable from "the feature isn't built."
Compounding: multiple diverged branches (`play-3d`, `docs/playtest-gate-b`, `main`) with no single "this is THE build to test."

## 2026-07-23 correction — the deeper root cause
The 2026-07-21 writeup blamed a stale *checkout*. That was only half of it. The real failure:
**`main` itself was not the game.** `main` was still the **2D town**; the entire 3D town
(`godot/scripts/town3d/`, navmesh click-to-move, 3D hero actors) lived ONLY on divergent
branches (`play-3d` and the `docs/playtest-gate-b`→`feat/game-feel-g1-g5` lineage). The earlier
claim that "#156 absorbed play-3d's 21 commits into `main`" was **FALSE** — it never landed. So
"sync `play` to `main`" would have handed you a 2D build that looks current. **Truth was scattered
across branches; no single branch held everything** until `feat/game-feel-g1-g5` (a strict superset
of `play-3d`) was fast-forwarded into `main` on 2026-07-23. Lesson: provenance/sync guards are
worthless if TRUNK ITSELF LAGS REALITY.

## The guards (permanent)
1. **Trunk = truth.** `main` is the ONE canonical playable build. **Nothing is "done" until it lands
   on `main`.** No side branch — including agent worktrees — may hold unique *game* state past its
   merge. A feature that only exists on a branch does not exist. If `main` can't be launched into the
   current game, that is a P0 defect, not a branching preference.
2. **Launch only via `tools/play.ps1` — now a FRESHNESS GATE, not just a launcher.** Before it builds
   it (a) requires the checkout to be ON `main`, (b) fetches + fast-forwards to the newest trunk tip,
   and (c) **REFUSES TO LAUNCH if the build is stale (behind trunk) or diverged.** `-AllowStale` is the
   only override and launches with a loud banner. If it launches, the build IS the trunk tip. That is
   the guarantee — staleness is now structurally impossible through the sanctioned path.
3. **In-game build stamp.** A corner HUD label reads `build_info.txt` and shows
   `branch @ sha | <freshness> | date` (freshness = `clean`/`dirty`, or `ahead N (unpushed)` /
   `STALE-OVERRIDE`) — so every screenshot/playtest self-identifies and staleness is glaring.
4. **`play` tracks `main` only — read-only playtest surface.** No dev edits in `play`; the gate refuses
   to launch with uncommitted code. Do dev in worktrees, merge to `main`, let `play.ps1` pull it.
5. **One checkout, no orphans.** `play-3d` is now fully superseded (its content ⊂ `main`) —
   **delete it** and any stale local `main`. Track shipped features in `docs/registry/CONTENT.md`; the
   stamp + registry together answer "is this feature in the build I'm running?"

## Rule
**No playtest without `play.ps1`. No "it's not working" without reading the build stamp first.**
The gate makes a stale launch impossible; if you ever suspect one, the stamp's freshness field is the
first thing to read. And when in doubt about whether a feature is "in the game" — ask "is it on
`main`?", because `main` is the game.
