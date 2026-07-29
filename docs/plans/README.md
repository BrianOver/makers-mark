# Plans index — what is live, what is history

**Read this before executing any plan in this directory.** As of 2026-07-28 there are ~38 plan docs
here and **only three are live**. Most are complete work kept as the record of what was decided and
why. One is specced-but-not-cleared and must not be picked up without a ruling.

This index exists because of a real failure: on 2026-07-28 an audit found the code was roughly five
days ahead of every planning document, two active task-list items contradicted designs the project had
already decided against, and a fully-specced plan for an unbuilt module was still labelled "plan of
record" for work that other systems had largely obsoleted. Individual docs are now stamped with a
`STATUS:` banner; this file is the one place to look first.

---

## LIVE — safe to execute

| Doc | What it is |
|---|---|
| `2026-07-28-003-roadmap-post-skeleton.md` | **Sequencing authority.** Where the project is, what happens next, open decisions. Start here. |
| `2026-07-28-004-feat-close-the-open-work-plan.md` | The current work plan: wire the two new crafts, teardown the dead 3D layer, advisor mirror, then the human feel-test gate. |
| `2026-07-28-002-feat-interactive-professions-and-trade-plan.md` | U1–U6 shipped; **U7–U10 still open** (U7/U8 continue in `-004` above). Its research origin is `docs/design/2026-07-28-002-interaction-design-research.md`. |

## ON HOLD — do NOT execute without an owner ruling

| Doc | Why |
|---|---|
| `2026-07-21-004-phaseA-legend-engine.md` | Reads as a complete plan of record. **Nothing in it was built** (no `sim/GameSim/Legends/`), but its payload arrived by another route (AttributionEngine, LegendQuery, Chronicle, memorials). Whether the specced sifter is still owed is open — roadmap `-003` §3. |

## NEARLY DONE — one unit left

| Doc | Remaining |
|---|---|
| `2026-07-27-006-feat-2p5d-stardew-pivot-plan.md` | U8 teardown of `godot/scripts/town3d/`. Tracked as U5 in `-004`. |
| `2026-07-21-005-watch-surfaces.md` | MVP shipped; U-W4 (scrying mirror) and U-W5 (town ceremony) were deferred post-skeleton by the plan itself. U-W3 unconfirmed — verify before assuming. |

## SUPERSEDED — read for context, never execute

| Doc | Superseded by |
|---|---|
| `2026-07-21-003-phased-roadmap.md` | `2026-07-28-003` (Phases A–D closed; its 3D-gen model is dead) |
| `2026-07-21-006-phaseB-living-heroes.md` | `2026-07-25-002` (rejected its Zubek engine + 16 traits) |
| `2026-07-24-004-feat-wave5-tactile-forge-plan.md` | `2026-07-28-002` U3 (Anvil Map replaced twice over) |
| `2026-07-15-001-roadmap-beyond-v1.md`, `2026-07-18-001-feat-game-completion-master-plan.md` | `2026-07-21-003`, then `2026-07-28-003` — both remain valid as the *mechanism/pillar backlog*, not as sequencing |

## COMPLETE — historical record

Everything else, including: `2026-07-13-001` (the original 13-unit plan), `2026-07-21-007` (Phase C),
`2026-07-21-008` (Phase D), `2026-07-25-001` (core loop), `2026-07-25-002` (Phase B), `2026-07-28-001`
(animation), and the 07-16 → 07-24 waves. Each carries its shipped PR numbers in its banner where one
has been stamped.

---

## Rules for adding a plan here

1. **Date-and-number the filename** (`YYYY-MM-DD-NNN-<slug>.md`) and put YAML frontmatter FIRST — a
   banner above the frontmatter breaks every tool that parses it.
2. **Add a row to this index** in the same commit. A plan that is not indexed is a plan a future session
   will either miss or mistake for live work.
3. **Stamp the banner when the work lands** (`> **STATUS: COMPLETE (stamped <date>).** <PRs>`). Progress
   belongs in git and the task tracker; the banner is only the live/dead marker.
4. **When a plan supersedes another, stamp the older one in the same PR** — and say what was *rejected*,
   not just what replaced it. The Phase B pair is the model: the successor's value is largely in
   recording which design the project chose against.

## Known stale pointer

`CLAUDE.md` names `docs/plans/2026-07-13-001-feat-inverted-mmo-game-plan.md` as the plan of record.
That plan is complete; the current authority is `2026-07-28-003-roadmap-post-skeleton.md`. `CLAUDE.md`
is owner-authored (agent deny-list), so this is flagged rather than changed.
