# Telemetry loop — non-human testing & semi-automatic tuning

The loop that lets the game self-improve without a human playtest: batch sims record NPC
behavior, analytics flags drift, a Claude proposes tuning, gates + Brian approve. **Manual
trigger only** — runs when Brian says so; nothing fires on its own.

## The loop

```
Brian: "run a telemetry batch"
  1. dotnet run --project sim/GameSim.Cli -- batch --seeds 20 --days 100      [automatic]
  2. dotnet run --project tools/Analytics -- runs                              [automatic]
       -> runs/report.md content (stdout) + runs/anomalies.md (heavy events)
  3. Claude reads anomalies.md, reproduces what matters (docs/debugging.md §1) [Claude]
  4. Fix proposal:
       - DATA TUNING (registry params, IntegerCurves constants, flavor packs,
         Anomalies thresholds): Claude branches + PRs it directly
       - MECHANISM change (resolver, kernel, contracts, new systems):
         escalate to Brian first — never ship as "tuning"
  5. Fast lane + balance gate + conformance must be green                      [automatic gates]
  6. Brian approves the PR (auto-merge does the rest)                          [human]
```

Batch axes today: seed sweep only. The arg surface reserves room for `--policy <persona>`
(scripted player variants — tests player-influence) and tuning A/B overrides; they are
**deferred**, design in `docs/plans/2026-07-17-001-feat-observability-telemetry-plan.md`.

## Reading anomalies.md

Each hit: `[SEVERITY] rule — seed, day window`, a one-line detail, and a repro command.
Severity is a triage order, not a verdict — a LOW firing on every seed (see below) can matter
more than one MEDIUM on one seed. Rules and thresholds: `tools/Analytics/Anomalies.cs`
(consts at the top; tune with evidence).

**beat-starvation is the fun gauge.** The game's thesis is attribution pride ("MY sword saved
her"). If beats/day starves, the core promise is failing — treat as highest priority regardless
of anything else firing.

## Known first findings (2026-07-17, 5 seeds × 40 days)

- `tariff-saturation` fired on **all 5 seeds** — mechanically corroborates the P5 playtest note:
  BaselinePlayer buys every affordable offer, standing pegs at cap, the tariff lever stops
  mattering. Tuning candidate: DriftStep/RiseStep ratio, or a smarter baseline policy axis.
- `death-spike` (seeds 1-2) and `gold-mint-spike` (seeds 3-4) queued for a look.

## Ownership

Post core-split this loop belongs to the **AI/NPC lane** Claude (lane model — visuals / AI-NPC /
engine + addon swarm — is UP TO DEBATE, recorded in the plan). Until the split exists, the
orchestrator session runs it. Decision-trace events (plan U4 — per-decision score breakdowns,
the player-influence ledger) land after the expedition-tension architecture PR and activate the
dormant decision-monoculture rules.
