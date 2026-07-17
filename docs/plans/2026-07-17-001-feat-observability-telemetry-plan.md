---
title: "feat: Observability & NPC-telemetry loop — Plan"
date: 2026-07-17
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
product_contract_source: ce-brainstorm
execution: code
origin: docs/design/observability-and-npc-telemetry.md
---

# Observability & NPC-Telemetry Loop - Plan

## Goal Capsule

Give every Claude (and Brian) the ability to **see why NPCs behaved as they did, detect when the
game drifts unhealthy, and reproduce any issue deterministically** — then close the loop with
**non-human batch testing**: seed-sweep simulations recorded to chronicles, analyzed by Claude,
feeding data-tuning PRs. Manual trigger only ("when Brian says"). Product authority:
`docs/design/observability-and-npc-telemetry.md` + brainstorm dialogue 2026-07-17. Open blockers: none.
Sequencing note: U4 (decision-trace) touches `Expedition/` — must land AFTER the pending
expedition-tension architecture decision, never in parallel with it.

## Product Contract

**Problem.** Events record *what* happened, never *why chosen*; nothing watches aggregate health;
no debugging manual exists; all playtesting is human. NPC behavior — semi-random, player-influenced —
is the game's key component and must be observable to self-improve.

**Requirements**
- R1: A Claude can reproduce any reported anomaly from the report pointer alone (seed + day window + replay recipe).
- R2: Decision-trace answers "why did hero X buy/skip/target Y" from the event log, incl. which player levers (price, stock, bounty, standing) influenced the score.
- R3: Anomaly detection runs over exported chronicles and emits a severity-ranked `runs/anomalies.md` with repro pointers; beat-starvation (attribution beats/day → 0) is a first-class rule.
- R4: Batch harness: N seeds × M days, baseline player policy, one command, no human input; chronicles land in `runs/`. Later stages (player-policy personas, tuning A/B) bolt onto the same harness without rework.
- R5: Loop is semi-automatic: reports mechanical; Claude proposes data-tuning PRs (registry params/curves/pack lines); mechanism changes escalate to Brian; existing CI gates enforce.
- R6: Determinism preserved: no new RNG draws, no wall-clock, integer-only; golden replay re-recorded at most once (U4), as a deliberate reviewed act.

**Actors.** A1 Brian (triggers batches, approves tuning). A2 Orchestrator/AI-NPC-lane Claude (runs batches, analyzes, PRs tuning). A3 Task/mod-Claudes (consume debugging.md to self-fix).

**Scope: in.** Debugging manual; anomaly rules in Analytics; decision-trace events for shopping /
floor-target / bounty-accept; CLI batch subcommand (seed sweep); loop documentation.
**Scope: out (deferred).** Cron/nightly auto-runs; player-policy personas + tuning-A/B axes (design
for, don't build); runtime LLM; live-player analytics; Godot-side logging beyond existing.
**Out of identity.** Fully-automatic self-tuning without human approval.

**Success criteria.** 20 seeds × 100 days batch completes green and yields report + anomalies file;
a seeded anomaly (e.g., deliberately broken tuning) is detected and reproduced from the pointer;
conformance/goldens green; "why did hero skip my item" answerable without reading resolver code.

---

## Key Technical Decisions

- **KTD1 — Batch runner = CLI subcommand** (`GameSim.Cli batch`), not a new tool or test category:
  reuses `GameComposition` + `ChronicleCodec` + existing `export` path; no Godot; runs anywhere the
  fast lane runs. Balance tests stay what they are (assertions); batch = data farm (no assertions).
- **KTD2 — Decision-trace events are contract events emitted from already-computed integer scores.**
  Zero new RNG draws → kernel stream untouched; the ONLY golden impact is new events in the log
  (one deliberate re-record). Enum reason-codes + integers; no floats, no free prose (except reuse
  of existing `HeroPassedOnItem.Reason` which stays).
- **KTD3 — Emission is capped, not exhaustive:** top-3 candidates per decision, emitted only when
  contested (winner margin below threshold) or on pass/decline. Keeps log + save growth bounded.
- **KTD4 — Anomaly rules live in `tools/Analytics`** (pure aggregation over chronicles), never in
  the sim. Sim stays rules-only; observation lives at the edge.
- **KTD5 — Player-influence attribution rides the score terms:** each decision-trace event carries
  the per-term breakdown (fit/price/weight/bounty/standing) so Analytics can diff "with player
  lever vs without" statistically — no counterfactual machinery needed in v1.

---

## Implementation Units

### U1. Debugging manual

**Goal:** `docs/debugging.md` — Claude self-serve diagnosis.
**Requirements:** R1.
**Dependencies:** none.
**Files:** `docs/debugging.md`; pointer added to `CLAUDE.md` docs list (orchestrator).
**Approach:** One page: (1) deterministic repro recipe — export chronicle, seed + action log,
replay to byte-identical state, bisect by day; (2) log/artifact map — `runs/`, CI trx, Godot user
logs, ComfyUI logs; (3) failure shapes — golden mismatch = stream shift, net8 TFM injection,
engine-lane display timeout, save-compat trailing-optional rule; (4) the telemetry loop + batch
command usage. Content sourced from CLAUDE.md, memory, ci.yml — verified this session.
**Test scenarios:** Test expectation: none — documentation only; correctness enforced by review
against the named files.
**Verification:** A fresh Claude session can follow the recipe to reproduce a chosen day-N state
twice and diff-equal the serialized saves.

### U2. Batch harness — CLI `batch` subcommand (seed sweep)

**Goal:** `dotnet run --project sim/GameSim.Cli -- batch --seeds 20 --days 100 --out runs/` produces
one chronicle JSON per seed, no interaction.
**Requirements:** R4, R1.
**Dependencies:** none.
**Files:** `sim/GameSim.Cli/Program.cs` (subcommand + arg parse); `sim/GameSim.Tests/Cli/BatchRunnerTests.cs` (new).
**Approach:** Loop seeds; per seed: `GameComposition.NewCampaign(seed)`, tick M days with the
baseline player policy already used by the balance sim (reuse — do not fork a second policy);
`ChronicleCodec` serialize to `runs/batch-seed{seed}-days{days}.json` — DETERMINISTIC names, no
wall-clock stamp (re-runs overwrite; the runner clears stale `batch-*.json` first). Sim purity: IO stays in CLI.
Design the arg surface so later axes (`--policy`, `--param-override`) slot in without breaking
callers (R4 forward-fit); do NOT implement them.
**Test scenarios:** happy: 2 seeds × 3 days writes 2 parseable chronicles with Day==4/expected
event counts stable; determinism: same seed twice → byte-identical chronicle JSON; edge: `--seeds 0`
exits nonzero with usage; error: unwritable `--out` reports path and exits nonzero (no partial silent success).
**Verification:** 20×100 run completes locally; files parse via `ChronicleCodec.Deserialize`.

### U3. Anomaly rules + report

**Goal:** `tools/Analytics -- runs` additionally writes `runs/anomalies.md`: severity-ranked rule
hits, each with seed + day window + repro command.
**Requirements:** R3, R1, R5.
**Dependencies:** U2 (batch corpus to run against; rules testable on synthetic chronicles first).
**Files:** `tools/Analytics/Anomalies.cs` (new — rules + thresholds as consts), `tools/Analytics/Program.cs`
(wire + write file), `sim/GameSim.Tests/Analytics/AnomalyTests.cs` (new; Analytics already referenced by test project).
**Approach:** Pure functions over `ChronicleData` lists. v1 rules: beat-starvation (beats/day
trailing-window → threshold), death-rate spike by floor/class vs corpus baseline, gold-inflation
slope, dead-content (item kind never sold in N days), standing-saturation (pegged at cap ≥ K days),
decision-monoculture (deferred until U4 events exist — rule ships behind presence-check on event kind).
Every hit renders: severity, rule, seed, day window, one-line repro (`batch --seeds 1 --seed <s> --days <w>` + replay pointer).
**Test scenarios:** happy: synthetic chronicle with 0 beats after day 10 → beat-starvation HIGH;
synthetic balanced corpus → empty anomalies file (no false positives); edge: single-run corpus
(baseline = itself) does not divide-by-zero; error: malformed chronicle JSON in dir → skipped with
warning, run continues; integration: real 5-seed batch output parses and report cites real seeds.
**Verification:** Seeded broken tuning (e.g., RiseStep 50 locally) triggers standing-saturation; pointer reproduces it.

### U4. Decision-trace events (orchestrator, sequenced)

**Goal:** `ShoppingScored`, `FloorTargetScored`, `BountyScored` events with per-term integer score
breakdowns + enum reason codes; the player-influence ledger. NOTE: a `BountyJudged` event ALREADY
EXISTS (`Contracts/Events.cs`, accept/decline + free-text reason, save-serialized) — do NOT redefine
it; the new score-breakdown event is named `BountyScored` and coexists.
**Requirements:** R2, R6, R5.
**Dependencies:** U3 (rules consume it); HARD SEQUENCE: after expedition-tension architecture PR.
**Files:** `sim/GameSim/Contracts/Events.cs` (orchestrator-only), emitters in `sim/GameSim/Heroes/`
(shopping), `sim/GameSim/Expedition/` (floor target), `sim/GameSim/Bounties/` (judging);
`sim/GameSim.Tests/Drama/DecisionTraceTests.cs` (new); golden fixture re-record (deliberate, reviewed).
**Approach:** Emit from existing computed scores at the decision site — no recompute, no new RNG.
Payload: top-3 candidate ids + per-term integer breakdown (fit/price/weight/bounty/standing) +
chosen/passed + reason enum. Gate emission per KTD3 (contested margin threshold const, or pass/decline).
Save-compat: new event kinds are additive — old saves load (polymorphic list), pin with a
SaveLoadTests case mirroring `PreP4Save` pattern.
**Execution note:** Re-record goldens as its own reviewed commit; assert the non-golden
determinism property (same seed+actions → identical state) still passes before re-recording.
**Test scenarios:** happy: contested shopping emits event whose chosen id matches actual purchase and
term-sum matches total score; cap: runaway-winner decision emits nothing; pass path always emits with
reason enum; determinism: two runs same seed → identical trace events; save-compat: pre-U4 save loads,
post-U4 save round-trips byte-identical; integration: standing tier change flips the standing term
in the emitted breakdown (the influence ledger works).
**Verification:** Fast lane + balance + conformance green; golden re-record diff reviewed; CLI day
report can print "hero skipped sword: price term -12".

### U5. Telemetry loop runbook

**Goal:** The semi-automatic loop documented + one command away.
**Requirements:** R5.
**Dependencies:** U1-U3 (U4 enriches, not required).
**Files:** `docs/telemetry-loop.md` (new); `docs/debugging.md` cross-link (U1 file); `.claude/tasks/README` note on AI/NPC-lane ownership (orchestrator).
**Approach:** Document: trigger phrase → `batch` → `Analytics` → read `anomalies.md` → propose
data-tuning PR (registry params/curves/packs ONLY; mechanism = escalate) → gates → Brian approves.
Records lane-model context (visuals / AI-NPC / engine + addon swarm — UP TO DEBATE) and that this
loop belongs to the AI/NPC lane post-split; orchestrator until then.
**Test scenarios:** Test expectation: none — documentation; verified by U5 dry-run below.
**Verification:** Dry-run the loop once end-to-end on a real batch; the produced tuning proposal
(if any) references only data files.

---

## Verification Contract

- Fast lane: `dotnet test sim/GameSim.Tests --filter Category!=Balance` green after every unit.
- Balance gate green after U4 (only unit that can move bands — via goldens, not tuning).
- Art conformance untouched. Engine lane untouched (no godot/ changes).
- End-to-end: 20 seeds × 100 days → analytics + anomalies produced; seeded-defect detection drill (U3 verification) passes.

## Definition of Done

U1-U3 + U5 merged green (independent of expedition-tension); U4 merged after tension PR with
reviewed golden re-record; success criteria in Product Contract demonstrated once; memory +
CLAUDE.md pointers updated.

## Deferred to Follow-Up Work

- Player-policy personas + tuning-A/B batch axes (harness arg surface reserves room).
- Decision-monoculture rule activation once U4 events exist (shipped dormant in U3).
- Nightly cron batch + auto-issue on anomalies.
- Godot-side structured logging (visuals lane).
