# Observability & NPC-Telemetry Loop — design (proposed)

> Brian's asks (2026-07-17): (1) debugging logs + docs good enough that Claudes fix their own
> detected issues; (2) NPC logic/behavior — semi-random, player-influenced — logged so the game
> self-improves as it grows. Semi-automatic: heavy events trigger analysis; a human approves tuning.
> Status: **proposed** — pending sign-off. Nothing here is implemented yet.

## What already exists (grounded)

| Piece | Where | What it gives |
|---|---|---|
| Event-sourced sim | `Contracts/Events.cs` (18 kinds), `GameState.EventLog` | Every drama/economy/combat fact, stamped, serialized in saves |
| Chronicle export | `Chronicle/ChronicleCodec.cs`, CLI `export` | Seed + day + roster + full event log as JSON to `runs/` |
| Analytics | `tools/Analytics` | Aggregate report: deaths by floor/role, beats by type, pass reasons, player-vs-rival sales, bounty accept/decline |
| Deterministic repro | kernel + golden replay | **Any bug reproduces byte-identically from seed + action log** — the debugging superpower |

## Gap 1 — Claudes can't see WHY an NPC chose

Events record *what happened* (HeroDied, ItemSold), not *why chosen* (shopping scores, floor pick,
bounty accept). `HeroPassedOnItem.Reason` is the lone why-string. A Claude debugging "heroes never
buy potions" today must re-derive scoring by reading code + stepping the sim.

**Fix: decision-trace events** (new contract kinds, orchestrator PR):

- `ShoppingScored` — hero, top-3 candidate items with integer score breakdown (fit/price/weight
  terms), winner or pass, reason-code enum (not free text).
- `FloorTargetScored` — party, per-floor scores, chosen target, bounty influence term.
- `BountyJudged` — hero, bounty, accept/decline, deciding term.

Rules (KTD-safe): emitted from ALREADY-computed integer scores — zero new RNG draws, no stream
shift; enum reason-codes + integers only (no floats, no prose); events land in the normal log so
they export in chronicles and serialize in saves. Cost: log growth — cap at top-3 candidates,
emit only when a decision was contested (winner margin < threshold) or ended in a pass.
Golden fixture re-records once (deliberate, reviewed — the standing rule).

**This is also the player-influence ledger**: score terms carry the player's fingerprints (price
set, stock offered, bounty posted, standing tier) — the report can answer "did Brian's pricing
change hero behavior" mechanically.

## Gap 2 — nothing flags anomalies (the "heavy events")

Analytics reports averages; nobody watches them. **Fix: anomaly rules in `tools/Analytics`**
(pure aggregation, no sim change) — each rule emits severity + the seed/day window to repro:

- Death-rate spike by floor/class vs campaign baseline
- **Beat starvation** — attribution beats/day trending to zero = the fun thesis (attribution
  pride) failing; the single most important gauge
- Economy drift — gold inflation slope, player-vs-rival sale share collapse
- Dead content — item/recipe/profession never bought/used in N days
- Standing saturation — faction pegged at cap (tariff stops mattering)
- Decision monoculture — one candidate wins a decision kind >95% (scores degenerate)

Output: `runs/anomalies.md` — severity-ranked, each with `seed`, day window, and the repro
command. That file IS the "heavy event" trigger: a Claude (or Brian) reads it and acts.

## Gap 3 — no debugging manual

**Fix: `docs/debugging.md`** (one page, written once):

1. **Repro recipe** — export chronicle → note seed + action log → replay = byte-identical state;
   bisect by day; `dotnet test --filter <suite>` per lane; GODOT_BIN/xvfb notes for engine lane.
2. Where logs live: `runs/` chronicles, `runs/anomalies.md`, CI trx artifacts, Godot user logs.
3. Common failure shapes: golden mismatch = RNG-stream shift (find the new draw), net8 TFM
   injection, engine-lane display timeout, save-compat breaks (trailing-optional rule).
4. The tuning loop (below) — so any session knows the workflow.

## The self-improvement loop (semi-automatic by design)

```
play / batch-sim N days  →  CLI export → runs/*.json
        →  Analytics → report.md + anomalies.md          [automatic]
        →  Claude session reads anomalies, proposes fix   [Claude]
             - data-tuning (registry params, curves, pack lines): Claude PRs it
             - mechanism change: escalate to Brian first
        →  fast lane + balance gate green → PR → auto-merge  [automatic gates]
        →  Brian approves merge / plays next session       [human]
```

Semi-automatic: gates and reports are mechanical; judgment (is this fun? is the fix right?) stays
human/Claude-reviewed. Batch-sim = the existing balance harness pattern (100-day runs across seed
sweep) reused as a telemetry farm. Optional later: a `/loop` or cron that runs sim+analytics
nightly and opens an issue when anomalies.md is non-empty.

## Sequencing (proposed)

1. `docs/debugging.md` — costs nothing, immediate Claude-self-serve payoff.
2. Anomaly rules in Analytics — pure tooling, no sim change, no golden impact.
3. Decision-trace events — orchestrator contract PR + one golden re-record; unlocks the
   player-influence ledger and decision-monoculture detection.
4. (later) nightly batch-sim telemetry farm + auto-issue.

Open: whether decision-trace lands before or after the expedition-tension architecture PR (both
touch Expedition/) — sequence them, never parallel.
