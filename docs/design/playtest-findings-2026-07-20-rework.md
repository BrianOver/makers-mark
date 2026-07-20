# Playtest findings — 2026-07-20 (U24 acceptance run, post world-rework)

Second SP-1 self-playtest, run as the **U24 acceptance gate** for the world-rework program
(plan `docs/plans/2026-07-19-002-feat-world-rework-plan.md`, all 26 units merged). Three naive
LLM personas (min-maxer, cautious-casual, chaos-monkey) played `GameSim.Cli` **seed 2026 × 25 days**
via the deterministic re-pipe loop, each forbidden from reading source/plan/docs — comprehension
had to come from CLI output alone. Every finding reproduces with seed 2026 + the persona transcript.

This gate covers **sim-side comprehension only (gate a)**. The visual/embodiment pillars (Y-sorted
town, avatar, interiors, drawers, live spectating) are invisible to the CLI instrument and remain
**Brian's Godot playtest (gate b)**.

Severity: **P0** = core loop unreachable/misleading or a real bug that blocks understanding,
**P1** = real defect hurting play, **P2** = polish/design.

---

## Verdict: CONDITIONAL FAIL (gate a) — close, blocked by one P0 + three P1

Large improvement over 2026-07-19: the graphics P0s are fixed, the H#/I# id-trap is mostly closed,
`advice` gives real onboarding, and robustness is now **GOOD** (chaos-monkey: ~190 commands, 25 days,
zero crashes, clean exit every run). But a clean PASS is blocked by a confirmed silent-craft bug and
three residual feedback defects. Fix the P0 + P1 list below, re-run this gate → expected PASS.

### Comprehension rubric (naive player, ~10 min)

| Pillar | min-maxer | cautious-casual | chaos-monkey | Verdict |
|---|---|---|---|---|
| Professions | PARTIAL | PARTIAL | UNDERSTOOD | **PARTIAL** |
| Heroes/NPCs | PARTIAL | PARTIAL | UNDERSTOOD | **PARTIAL** |
| Progression | FAILED | PARTIAL | UNDERSTOOD | **PARTIAL** (FAILED for the impatient player) |

The swing factor is the `advice` verb (U26): personas who found and leaned on it understood the loop;
the min-maxer who didn't hit the silent-craft wall and a static advice feed and scored progression
FAILED. Onboarding **exists but is neither surfaced forcefully nor evolves**, so a fast/impatient
player can still miss it — which is exactly the acceptance persona that must not fail.

---

## P0 — blocks a clean pass

### N1. Craft feedback is silent — both the illegal no-op and the success (all 3 personas)
The standout new defect, independently hit by every persona.
- **Illegal/untalented craft silently no-ops.** `craft longsword iron` without the `tier-2-smithing`
  talent prints the *same* `queued: craft longsword with iron` as a legal craft, then vanishes:
  no item ever appears, the material is never deducted, and **no `REJECTED` message is ever printed**
  — unlike every other illegal action, which the engine rejects clearly. min-maxer's `iron` sat at 4
  for 22 days; cautious-casual confirmed the identical no-op.
- **Successful craft logs nothing on resolution.** Only failures narrate; chaos-monkey confirmed 3-of-4
  queued daggers succeeded silently, only the insufficient-copper 4th was narrated.
- **Repro (seed 2026):** fresh game → `buymat iron 4` → `craft longsword iron` → `next` → `items`
  (no longsword, ever) → `mats` (`iron: 4`, untouched).
- **Fix direction:** the sim should reject a talent-gated craft the way it rejects other illegal actions
  (surface the `RejectedAction`), and the CLI/adapter should echo a resolution line on successful craft.
  **Check whether the Godot `ForgePanel` shares this silent-craft path — if so it affects gate (b) too.**

---

## P1 — real defects, fix before re-run

### N2. Misleading errors when an argument isn't a valid id (min-maxer, chaos-monkey)
Residual of the 2026-07-19 P0 #1/#2 id-trap. Ids now parse (`stock I1 50` works — the shelf loop is
reachable, the prior P0 is mostly fixed), but a *wrong* argument gives a generic arg-count error that
never names the real problem: `stock dagger 20` → `expected 'stock <itemId> <price>' — got 'stock dagger 20'`
(the arg count was correct; `dagger` just isn't an item id). `send H1 <bad-item>` and `stock I1 999`
behave the same. The engine clearly *can* produce good errors (`REJECTED: SetPriceAction — Item I1 is
not on the shelf.`) — these paths just don't.
- **Fix:** when an id-typed argument fails to resolve, say "unknown item id 'X'" — not an arg-count error.

### N3. Phase-illegal actions queue silently, fail only at `next` (chaos-monkey, min-maxer)
Residual of 2026-07-19 #5. `buymat` in Expedition, `bounty`/`buyore`/`recall` in the wrong phase all
print `queued: …` (looks accepted) then reject a full phase later at `next` with
`No handler accepts X during <Phase>`. Inconsistent with `talent <bad-id>`, which rejects immediately.
- **Fix:** reject phase-illegal actions at entry with the phase named, matching the `talent` path.

### N4. Death signaling is inconsistent (cautious-casual, chaos-monkey)
Same event, two clarity levels: day-6 `Elowen fell to the Tunnel Spider` (she is alive — a retreat),
her real death only shows that evening as euphemism (`Salt the doorstep for Elowen. Floor 2 keeps its
own.`), while day-9 uses a fully explicit `† Odd died … slain by a Deep Ghoul`. A live retreat can read
as a death; a real death can hide in flavor with no marker.
- **Fix:** one consistent, unambiguous death marker (the `†` / `DIED day N` form) everywhere a hero
  actually dies; keep combat-retreat prose visibly distinct from death.

---

## Prior 2026-07-19 findings — disposition

| Prior | Was | Now |
|---|---|---|
| #1 H#/I# id-format trap (P0) | shelf loop read as unimplemented | **Mostly fixed** — ids parse everywhere (U26); residual = N2 misleading non-id error |
| #2 `send` unusable (P0) | no working syntax found | **Mostly fixed** — `send H# I#` works; residual = N2 |
| #3 buyore timing reads broken (P0) | game taught the failing pattern | **Fixed** — per-offer "buyable at TOMORROW's Evening" hint added; residual: absent from `help` |
| #4 silent bounty lifecycle (P1) | no bounty state visible | **Still present** — `bounty` posts with no confirmation/board/effect visible (mm, cc) |
| #5 queued-action feedback (P1) | success/failure identical | **Partially present** — see N3 |
| #6 Hero Level always L1 (P1) | dead stat | **Still present** — no leveling in 25 days (known-deferred: Erenshor/progression track) |
| #7 empty-state inconsistency (P1) | typo-indistinguishable empties | Not re-surfaced this run — likely fixed or minor |
| #8 graphics (Depths collapse, dropped captions, mid-word wrap) (P1) | UI broken | **Fixed** — U3/U4/U5/U6 (captions, depths, shop, minewatch); engine suite green |
| #9 prose defects (name-pool collisions) (P1) | ambiguous gossip | **Still present** — 3× "Odd", 2× "Gorm" in one run (known: name-pool size, Erenshor/variety track) |
| #10–13 balance/design (P2) | flatline, free talents, learn-by-poking | Deferred to Brian / already-planned waves (unchanged) |

---

## New P2 / polish (not gating)

- **N5.** `profession <bad-id>` silently becomes an unlisted `queued: practise xyz` — accepts a bogus
  argument with no error (min-maxer). Reject unknown profession ids.
- **N6.** Item ids recycle (`I1` reused for a new dagger after the first sold) with no indication (min-maxer).
- **N7.** `advice` is static — repeats the same tier-1 suggestion for 20+ days, never escalates to
  tier-2/talents or reacts to a stalled economy (min-maxer). Making it evolve would materially lift the
  progression score.
- Combat-history surfacing via `gossip`/`items` (crafted items remember kills/saves) again delighted the
  cautious persona — a keeper for the living-world bark surfacing.

---

## Disposition

- **N1 (P0) + N2/N3/N4 (P1)** → one CLI/adapter feedback-fix PR (+ verify N1 isn't shared by `ForgePanel`).
  Re-run this gate at seed 2026 after; expected PASS.
- **N5/N6/N7** → small polish, fold into the same PR or the next CLI pass.
- **#4 bounty visibility, #6 leveling, #9 name pool, #10–13 balance** → design rulings / already-planned
  waves (Erenshor, progression), unchanged from 2026-07-19.
- **Gate (b)** — Brian's Godot playtest of the visual/embodiment pillars — remains the other half of U24
  acceptance and is not covered here.

Transcripts + per-persona journals archived in the session scratchpad; every finding re-runs via
`cat <persona>-transcript.txt | dotnet run --project sim/GameSim.Cli -- --seed 2026`.
