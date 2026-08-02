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
| `2026-08-02-002-feat-playtest-three-plan.md` | **Current execution wave**, from the owner's third playtest: send-off opens the show (the spectating that shipped but was unreachable), one vocabulary for phases/buildings, forge pacing, a generated day track, quieter cues, a 26×44 hero cast, and the 3-day tutorial. |
| `2026-08-02-003-feat-loop-legibility-plan.md` | **Next execution wave**, from the owner's fourth playtest: the queued-action model (12 verbs go immediate, 3 visible bell-riders with a cancellable tray), the split of "action applied" from "phase completed" (Stock no longer marches heroes out), empty raid phases collapse, per-phase cards, and the tutorial becomes a pointing overlay with a step registry. One re-baseline, in its U1 only. |
| `2026-08-02-001-feat-painted-interiors-plan.md` | The walkable painted forge interior with clickable stations. Sequenced behind the wave above; `InteriorStage` turned out to be dead code the pivot orphaned. |
| `2026-08-01-001-feat-make-it-visible-plan.md` | The receipts discipline: every player-facing change carries proof a human would see or hear it. `tools/receipt.ps1` came from its U1. |
| `2026-07-28-004-feat-close-the-open-work-plan.md` | The current work plan: wire the two new crafts, teardown the dead 3D layer, advisor mirror, then the human feel-test gate. |
| `2026-07-28-002-feat-interactive-professions-and-trade-plan.md` | U1–U6 shipped; **U7–U10 still open** (U7/U8 continue in `-004` above). Its research origin is `docs/design/2026-07-28-002-interaction-design-research.md`. |

## ON HOLD — do NOT execute without an owner ruling

| Doc | Why |
|---|---|
| `2026-07-21-004-phaseA-legend-engine.md` | Reads as a complete plan of record. **Nothing in it was built** (no `sim/GameSim/Legends/`), but its payload arrived by another route (AttributionEngine, LegendQuery, Chronicle, memorials). Whether the specced sifter is still owed is open — roadmap `-003` §3. |
| `2026-07-27-five-pillars-design-synthesis.md` (in `docs/design/`) + plans `2026-07-27-001` … `-005` | **RECOVERED 2026-07-29, UNRECONCILED.** See the note below — these were authored on 2026-07-27 but never committed, so the 07-28 replan wrote its roadmap without knowing they existed. They must be reconciled against `2026-07-28-003` before any of them is executed. |

### The 001–005 gap, and why it matters

The 07-27 numbering in this directory jumped straight to `-006` (the 2.5D pivot). That gap was not a
numbering quirk: **plans `-001` through `-005` and the design synthesis they came from existed only as
uncommitted files in a stale agent worktree**, and were found on 2026-07-29 while clearing worktrees.
They are recovered here verbatim, unedited.

This matters more than a filing error, for two reasons:

1. **The 07-28 replan could not see them.** `2026-07-28-003` cites `-006` and the two measurement docs
   (`2026-07-27-how-you-play.md`, `2026-07-27-gameplay-loop-analysis.md`) that the synthesis names as
   its own predecessors — but never the synthesis or `-001`…`-005`. So the current sequencing authority
   was written in ignorance of five implementation-ready plans derived from the same measurements.
2. **They carry a root-cause claim the roadmap does not contain.** Five independent deep-dives
   converged on one finding: the demand map is weapon-first at two levels (commissions are ~75% Weapon,
   and every depth-stall gate is Weapon/Shield), so *two professions have a craft and two have only a
   customer*. Nothing in `2026-07-28-003` says this.

**Do not execute these, and do not assume the roadmap already covers them.** Either the synthesis's
build order supersedes part of `-003`, or `-003`'s ordering stands and these get stamped SUPERSEDED —
that is an owner call, not something to settle by whichever doc was read most recently. Tracked in
roadmap `-003` §10 (open decisions).

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
