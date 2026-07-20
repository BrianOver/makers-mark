# Playtest findings — 2026-07-20 (U24 acceptance gate (a) RE-RUN, post N1–N4 fix)

Re-run of the SP-1 self-playtest as the **U24 acceptance gate (a)**, after the CLI/sim feedback
fix (`fix(cli): craft/error/phase/death feedback — U24 gate(a) N1–N4`). Same instrument as the
2026-07-20 CONDITIONAL-FAIL run (`playtest-findings-2026-07-20-rework.md`, PR #153): three naive
LLM personas (min-maxer, cautious-casual, chaos-monkey) played `GameSim.Cli` **seed 2026** blind
(CLI output only — forbidden from reading source/plan/docs) via the deterministic re-pipe loop.

Gate scope is unchanged: **sim-side comprehension only (gate a)**. The visual/embodiment pillars
remain **Brian's Godot playtest (gate b)**, still pending.

---

## Verdict: PASS (gate a)

The CONDITIONAL FAIL's blocking defects — 1 P0 (silent craft) + 3 P1 (misleading non-id errors,
phase-illegal silent queue, inconsistent death signaling) — are **all fixed and independently
confirmed CLEAR by all three personas**. Comprehension improved across the board: the prior run's
one FAILED pillar (min-maxer progression) is gone; no persona scored any pillar FAILED this run.
Robustness stays GOOD (chaos-monkey: ~103 malformed/illegal commands + normal play to day 17, zero
crashes, graceful integer-overflow handling, clean exit). The residual PARTIAL is confined to the
known-deferred hero-leveling track, not a comprehension defect in the shipped loop.

### Comprehension rubric (naive player, ~10 min)

| Pillar | min-maxer | cautious-casual | chaos-monkey | Verdict |
|---|---|---|---|---|
| Professions | UNDERSTOOD | UNDERSTOOD | UNDERSTOOD | **UNDERSTOOD** |
| Heroes/NPCs | UNDERSTOOD | UNDERSTOOD | UNDERSTOOD | **UNDERSTOOD** |
| Progression | PARTIAL | PARTIAL | PARTIAL | **PARTIAL** (hero leveling only — deferred #6) |

Robustness: **GOOD** (unchanged from prior run — no crash across all three personas).

---

## Fix confirmation — the four gate blockers

All three personas were asked to probe each fix target and quote the exact line. Every target read
CLEAR.

| Prior | Fix | Confirmed line (verbatim) |
|---|---|---|
| **N1 (P0)** craft silent | success now narrated | `⚒ forged Dagger [Common]` (all 3) |
| **N1 (P0)** craft silent | illegal craft rejects clearly | `REJECTED: CraftAction — Recipe 'bulwark' is tier 3; requires talent 'tier-3-smithing'.` (all 3) |
| **N2 (P1)** misleading non-id error | names the real problem | `stock: 'dagger' isn't an item id — see 'items'/'shelf' for the I# to use.` / `buymat: 'abc' isn't a number.` (all 3) |
| **N3 (P1)** phase-illegal silent queue | rejected at INPUT, phase named | `can't do that during Morning — type 'advice' to see this phase's legal actions.` (all 3) |
| **N4 (P1)** inconsistent death signaling | death carries the `†` marker | `† Moss died on floor 1 — slain by a Cave Rat`; retreats stay unmarked (all 3) |

---

## New residuals (all P2 — none gating; none is a regression)

Surfaced by the re-run; none blocks the gate. Ordered by cross-persona weight.

- **R1 (P2) — `buyore` success is silent.** A correct `buyore` prints only `queued: …` and no
  resolution line, unlike craft's `⚒ forged`. Flagged by all three personas — it's the same feedback
  class as N1, one verb over. Cheap on-theme follow-up (add a buyore-success beat).
- **R2 (P2) — state-illegal actions echo `queued:` then reject at `next`.** The queue→resolve model
  is intended and N3 only ever targeted *phase*-illegal (now caught at input); but obviously-bad
  state input (unknown material, floor 99, insufficient mats) still reads as accepted until the next
  tick. All three noted it. Design call — closing it means running the full Apply-level guard chain
  at input (ActionLegality.IsLegal already replicates it), a deliberate scope decision, not a bug.
- **R3 (P2) — wrong-phase message is generic.** N3's rejection names the phase but not the offending
  verb, so a multi-command batch is harder to attribute (chaos-monkey). Cheap: thread the verb name
  into the message.
- **R4 (P2) — retreat prose is inconsistent / unmarked.** N4 made *death* unambiguous (`†` +
  explicit line); a living retreat still varies from explicit ("Sable pulls out") to metaphor-only
  ("Floor 1 let them pass"). Death is never mistaken for retreat now, but retreat itself isn't
  reliably legible from ledger prose alone (min-maxer, chaos-monkey).
- **R5 (P2, carried) — hero leveling static.** Every hero stayed L1 through 15–17 days despite
  repeated floor-3 clears; no level-up event exists. This is prior finding #6 (deferred to the
  Erenshor/progression track) and is the sole reason Progression scored PARTIAL, not a new defect.
- **Minor:** duplicate recruit names right after a same-name death (#9, deferred); `quit` prints no
  farewell; no upper bound on `price`.

---

## Disposition

- **Gate (a): PASS.** N1–N4 fixed and confirmed. Re-run needs no further iteration to clear the
  acceptance bar.
- **R1/R3** → cheapest on-theme feedback polish; fold into the same CLI PR or the next CLI pass.
- **R2/R4** → design rulings (queue-model surfacing; retreat legibility) — Brian's call.
- **R5 (leveling), #9 (name pool)** → already-planned waves (progression, Erenshor variety), unchanged.
- **Gate (b)** — Brian's Godot playtest of the visual/embodiment pillars — remains the other half of
  U24 acceptance and is not covered here. The Godot `ForgePanel` was verified NOT to share the N1
  silent-craft path (it gates craft buttons by affordability and toasts all rejections via `MainUi`).

Transcripts + per-persona command files archived in the session scratchpad; every finding re-runs
via `cat <persona>-cmds.txt | dotnet run --project sim/GameSim.Cli -- --seed 2026`.
