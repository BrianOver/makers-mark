---
title: "ops: grind-block schedule — v1-playable weekend (Fri 2026-07-17 21:30 CT → Sun 2026-07-19 EOD)"
date: 2026-07-18
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
origin: docs/plans/2026-07-18-001-feat-game-completion-master-plan.md (unit catalog + rulings R1–R9 are authority; this doc sequences its Horizon-1 scope)
execution: single orchestrator session (INTEGRATE/CUT loop) + Opus workers in per-claim worktrees per lane-operating-model §13 v2.1 (read from origin/main)
related: docs/plans/2026-07-17-002-feat-staged-resolution-plan.md, docs/plans/2026-07-17-003-feat-town-2p5d-migration-plan.md, docs/telemetry-loop.md
---

Ground truth at start: origin/main @ `1e9dd41` (#42). **PR #43 (U2) is OPEN** — B1's first action is merging it. V5a (#38) and O1/LFS (#35) are already merged; neither appears below. The shared checkout's `.claude/tasks/BOARD.md` is stale — trust `git show origin/main:.claude/tasks/BOARD.md`. Unit IDs, packet dirs, and rulings R1–R9 per the master plan; this doc never redefines a unit.

## 1. Definition of done — `v1-playable` (weekend tag; NOT v1.0, per ruling R5 — v1.0 additionally requires the master plan's Horizon-2 MIN set: M2a, M15, V6, M14, M7a, M9, M6, M8, M11a, M12)

### MUST (ship-blocking)
| # | Item | Ground |
|---|---|---|
| D1 | Playable end-to-end in Godot: craft/price/stock/bounty (exists — U11/U12 done) **plus camp Send/Recall slate in a Godot panel (V7a)** | Contracts merged (#34); handlers land in staged-U4; plan 002 §U6 scopes godot work to ambience only, so V7a is this schedule's new VISUALS unit (spec written B1) |
| D2 | Full 5-phase loop live: U3 + U4 + U5 merged; narrator lines surfaced in Godot (V7b); Camp/Deep tints + Deep-complete survivor walk-back (V5b-lite) | plan 002 U3–U5; choreography fix per plan 003 §V5b step 2 |
| D3 | 2.5D town visible: 4 lit normal-mapped buildings + 3 tinted hero figures + phase ambience, via the CP-1 Q1 path (default: additive pilot-style overlay scenes — plan-compliant) | V1/V2/V3-gen/V4a; CP-1 verdict Friday |
| D4 | Attribution pride visible: ledger return cards, tallies, gossip, memorials, camp-delivery `PotionLifesave` beat end-to-end | Mostly shipped (R11–R15); marquee test = staged-U4 DoD; V7b surfaces beats |
| D5 | Balance green: 100-day Balance suite + 20-seed batch + anomaly scan clean after ALL band-moving merges (`dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category=Balance`; `dotnet run --project sim/GameSim.Cli -- batch --seeds 20 --days 100`; `dotnet run --project tools/Analytics -- runs`) | the weekend's only planned band-mover is U3 (R3, R4) |
| D6 | No CLI-only features: every verb and narrative surface reachable in Godot | CLI keeps parity but is not the ship surface |

### SHOULD (census targets, amended per rulings)
4 professions live (T2 ×2); **6 classes REGISTERED-INERT (T1a ×3 — recruitability rides M8/G12 post-v1, ruling R2; T1b is CUT from this weekend)**; trinkets 4–6 (T6); consumables 6–8 Heal variants (T5a); flavor ~600 lines **with prose-golden re-pins in-packet (R8)** + ItemSold/BountyPaid/TariffApplied voiced (T8a + T8b); **G8 material registry (T7a = M1 lookup-only scope, draw-neutral, R4)** + Crownsguard registered + 2 faction packs (T7b); 3 venues registered-inert (T3a ×2, Mine-ore loot); ~35 art specs authored (T9a), town+hero pairs + partial icons shipped; M3 + M4 orchestrator micro-PRs (open the post-weekend lanes at zero draw risk).

### STRETCH (only if blocks run clean)
One of T4 (monster variety) or T10 (bounty types) — never both; new ConsumableKind (Damage/Buff) enum append; extra flavor voices; V0/V4b proper if gdUnit4Net ships stable 4.7 (external); LLM-flavor prep = one-page corpus-export note only (runtime LLM DEFERRED by ruling).

## 2. Block-by-block plan (10 × 5h, CT)

Worker costs: data packet 50–110k, mechanism 100–250k, orchestrator 30–50k/block. Fast lane before any "done" report: `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance`.

**B1 — Fri 21:30–02:30 (Brian awake early part)**
- Orchestrator: **merge #43 (flip G3)**; cut weekend claim stubs incl. wave-A/B/C creative defaults; write V7a camp-panel unit spec (~1h); land M3 + M4 contracts micro-PRs (both S, draw-neutral, zero golden risk — they unblock the entire post-weekend minigame/P9 lanes and cost nothing now).
- Spawn: **U3** core worker (differential parity + band re-fit per plan 002 §U3, 200–250k); **V1** + **V4a** (VISUALS, ~120k combined); wave-A: **T2-engineering** (`sim/GameSim/Professions/Engineering/` + tests), **T2-alchemy** (`.../Alchemy/`), **T8a-flavor-breadth** (`Flavor/Packs/` + `sim/GameSim.Tests/Flavor/`, **contract includes re-pinning TavernPack/LedgerPack prose goldens — R8**) (3 × ~80k).
- Brian CP-1 (30 min): *Q1 2.5D path — three options with honest costs per master plan (a: V4b-on-4.6.3 = ruling change, 4.7.1 re-import churn; b: edit live Control town = ruling change vs plan 003 filler rule; c: additive pilot-style overlays = plan-compliant, throwaway; **default c**); Q2 DoD confirm; Q3 R2 confirm; Q4 profession briefs. Then bed.
- Budget ~700k. Fallback: U3 tripwires (>30% band shift) → worker parks per protocol, drop one T2 packet to B2.

**B2 — Sat 02:30–07:30 (autonomous)**
- Orchestrator: INTEGRATE wave-A + V1/V4a (**V4a merge enables V5b-lite's null-tolerant fallback — R9**); babysit U3 to merge (flip G4, BOARD broadcast); apply registration lines.
- Spawn: **U4 camp verbs** (150–180k, opens the moment U3 merges); wave-B: **T1a-classes ×3** (`sim/GameSim/Classes/<Name>/` + tests, registered-inert), **T5a-consumables** (owning profession dirs), **T9a-art-specs** (`art/specs/items/`, `art/specs/monsters/`; conformance `dotnet test art/GameArt.Tests`) (5 × ~70k, cap at fit).
- GPU (zero tokens): ComfyUI batch — 8–16 candidates each for forge/market/mine-gate + 3 hero figures per plan 003 §V2/§V3-gen, to gitignored `art/pipeline/candidates/`.
- Budget ~600–750k. Fallback: U3 not merged by block end = **red flag**; B3 becomes U3-rescue, wave-B shrinks to 3 packets.

**B3 — Sat 07:30–12:30 (Brian awake)**
- Brian CP-2 (45 min): art curation #1 (building contact sheets, pre-culled to top-3 via silhouette/moving-light QA); CLI playtest (`dotnet run --project sim/GameSim.Cli`); Q5 wave-C briefs.
- Spawn: **V2 commit** (~100k); **V5b-lite** (Camp/Deep tint rows + walk-back, script-level `TownScene.cs` only, no `.tscn` surgery; **depends on V4a merged — R9**; ~80k); **U5 narrator** (opens on U4 merge, ~130k); wave-C: **T3a-venues ×2** (`sim/GameSim/Venues/<Name>/` + tests, Mine-ore loot), **T6-trinkets** (recipes in owning profession dirs) (3 × ~80k).
- Orchestrator: INTEGRATE wave-B; merge U4 (flip G5).
- Budget ~650k. Fallback: curation slips → V2 moves to B4; V5b-lite proceeds anyway once V4a is in (fallback chain then degrades gracefully).

**B4 — Sat 12:30–17:30**
- Brian CP-3 (30 min): curation #2 (hero trio); V7a layout eyeball; Q6 faction identities.
- Spawn: **V3-gen commit** (~90k); **V7a camp panel** (Godot slate + Send/Recall + rejection reasons against merged U4 handlers, engine tests, ~150k); **T7a = M1 material registry** (`sim/GameSim/Materials/` new + `Drama/OrePricing.cs` + `Crafting/RecipeTable.cs` + `sim/GameSim.Tests/Materials/`; **lookup-only scope per R4: 5 Mine keys byte-identical, unknown-key still throws, goldens untouched, NOT a band-mover — legal to run while nothing band-moving is in flight**; acceptance proof = FactionConformanceTests re-pointed at the registry; 200–250k).
- **T1b is CUT (ruling R2 — rides M8 post-v1).** Freed ~150k = slip buffer for B3 leftovers or defect burn-down.
- Orchestrator: INTEGRATE U5 + wave-C.
- Budget ~700k. Fallback: T7a is the first SHOULD to slip to B5.

**B5 — Sat 17:30–22:30**
- Brian CP-4 (60 min): first full Godot playtest — 5-phase day, camp decision, narrator lines, 2.5D per Q1 path. Defects as BOARD items. *Band-acceptance verdict if U3's re-fit shifted bands.
- Spawn: **V7b narrator drip** (surface `Narrative/` output in Evening/ledger panel + `AttributionBeat` pride, ~100k); **T7b faction packs ×2 + Crownsguard registration line** (`sim/GameSim/Factions/<Name>/` + tests; opens on T7a merge — FactionConformanceTests goes green only post-M1; 2 × ~80k); **T8b flavor-new-surfaces** (`Drama/GossipGenerator.cs` Describe seam + pack lines, CORE not addon, ~120k); defect-fix worker.
- Orchestrator: INTEGRATE; **Balance verify: U3 is the only band-mover — T7a merges with its byte-identical conformance proof, no re-fit (R4)**.
- Budget ~650k. Fallback: T8b slips to B7; playtest defects always outrank new content.

**B6 — Sat 22:30–03:30 (autonomous, slack block)**
- Spawn: slipped SHOULD packets; **camp/narrator flavor pack** (legal now — U5 defined the NarratorPack surface); T5a round 2.
- GPU: icon-set candidate batches from T9a specs (~35 small assets).
- Orchestrator: INTEGRATE loop; full 20-seed × 100-day batch + `dotnet run --project tools/Analytics -- runs` anomaly scan overnight.
- Budget ~500k. Anything from B3–B5 lands here.

**B7 — Sun 03:30–08:30 (autonomous)**
- Spawn: **T4 monster-table** (ORCH contract micro-PR + kernel, ~150k) ONLY if all D-items merged AND B6 anomaly scan clean; otherwise defect burn-down + rebase storms.
- Orchestrator: INTEGRATE; Balance re-run after any band-moving merge; stage final golden re-record.
- Budget ~400k. Fallback: skip T4 entirely (STRETCH).

**B8 — Sun 08:30–13:30 (Brian decision block)**
- Brian CP-5 (90 min): curation #3 (icons, fast); **D1–D6 checklist playtest — walk each literally**; *CUT-LINE VERDICT: everything unmerged and non-MUST dies at 13:30.
- Spawn: icon commit worker; playtest-defect workers (2–3 × ~100k); doc worker (README + `.claude` docs + this plan's status — org rule, no tribal knowledge).
- Budget ~550k.

**B9 — Sun 13:30–18:30 (hardening)**
- No new features. Defect fixes only. Orchestrator final sweep: fast lane, Balance, engine lane (`dotnet test godot/tests --settings .runsettings`), 20-seed batch, anomaly scan, `git grep net8.0` clean, run-twice replay green.
- Brian: second full playtest; sign-off list (CP-6 start).
- Budget ~400k. Fallback: a red MUST here → B10 becomes its dedicated fix block; all remaining SHOULD merges freeze.

**B10 — Sun 18:30–23:30 (ship)**
- Fix buffer → final CI green on main → **tag `v1-playable` (R5 — not v1.0)**; update `docs/plans/2026-07-13-001` status; CLAUDE.md/BOARD close-out; memory update; queue CP-7 (post-weekend creative batch) as the next session's opener.
- Brian: final acceptance playtest (30-minute grin-moment criterion).
- Budget ~300k reserve. If empty: cherry-pick one STRETCH (T10 best token/value).

## 3. Risk register — top 5 Sunday-killers

| # | Risk | Why it kills Sunday | Mitigation |
|---|---|---|---|
| 1 | **D3's build path vs plan 003's decided rule.** Plan 003 §V0: "Decided rule (do not relitigate): stay on 4.6.3 until gdUnit4Net ships stable… V4b lands only on 4.7.1"; sanctioned filler = additive pilot-style scenes, no `town_scene.tscn` edit, throwaway; do NOT start V4b on 4.6.3 (re-import churn). Upstream won't ship in 48h. | D3 unbuildable as V4b | CP-1 Friday verdict with three honestly-costed options: (a) V4b-on-4.6.3 = **ruling change**, accepts documented 4.7.1 re-import churn on every touched scene; (b) 2.5D-lite editing the live Control town = **ruling change** vs the no-town_scene-edit filler rule; (c) additive pilot-style overlay scenes = **plan-compliant**, cost is throwaway work. Default (c). Deciding Friday is the mitigation; deciding Sunday is the failure. |
| 2 | **U3 band re-fit blowup** — every seed diverges by construction (multi-party interleave, plan 002 grounding); tripwires: grin collapse, insolvency, >30% shift. | U3 blocks U4→U5→V5b-lite choreography→V7a/V7b — the whole D2 chain. Must merge by end of B2. | Tripwire protocol in-plan (re-examine `CampCheckpointDepth` before retuning); orchestrator runs the seed-sweep diff in parallel with the worker; timebox — red at B2 end → descope U4's kill-risk-1 A/B, land minimal re-fit; B3 pre-allocated as rescue. |
| 3 | **Art curation bottleneck** — single RTX 5080, serial pipeline, Brian-in-loop picks, VISUALS sole pixel writer. | Late art = D3 slips into playtest blocks. | Generate only overnight (B2/B6, zero tokens); pre-agreed auto-cull (silhouette + moving-light QA per plan 003 §V2 step 4) so Brian sees top-3 sheets, 30–45 min/checkpoint; **null-tolerant degradation exists only after V4a merges (R9) — V4a is therefore a B1 spawn and B2 merge, ahead of every asset commit**; icons are the first art cut. |
| 4 | **Stacked balance re-baselines.** | Each re-baseline = full Balance + sweep cycle; collisions eat blocks. | **This weekend has exactly ONE band-mover: U3.** T1b is cut (R2); T7a is draw-neutral lookup relocation with a byte-identical conformance pin (R4) and is NOT on the band-mover list; T3b is pre-cut; data packets keep Balance untouched (Tanning precedent #41). Any accidental band shift in a "neutral" PR = stop-the-line defect. Final sweep reserved at B9 with B10 buffer. Post-weekend chain order is R3: U3 → M7a → M6 → M8 → M11a → M12 → M10. |
| 5 | **CI/merge throughput, not work throughput** — ruleset needs branch-up-to-date + green on every PR incl. engine lane (no path filters, `ci.yml:4-6`); 5 parallel workers = rebase storms; engine-lane import flake is a known shape (`docs/debugging.md`). | 20+ PRs through a serialized gate; flake doubles merge latency. | Orchestrator batches INTEGRATE per wave; workers arm auto-merge and exit (§13 v2.1 seams 5/6); flake policy = one re-run then triage; godot-touching PRs single-file serial; overnight blocks absorb rebase grind. |

**Token overrun (standing):** drop order is fixed — STRETCH workers → SHOULD packets → art-generation prep → never the serial U3/U4/U5/V7a chain or defect fixes.

## 4. What CANNOT fit by Sunday (plain)

- **V4b/V0 proper (4.7.1 migration)** — external gate G6; ship on the CP-1 path, migrate post-v1.
- **T1b recruit-mechanism** — cut by ruling R2; rides M8's combined re-baseline post-v1. Classes ship registered-inert.
- **T3b live venue #2** — routing core + honest re-baseline ≈ a full day; rides M6 (G13). Venues ship registered-inert (T3a).
- **T4 AND T10 both** — at most one contract micro-PR fits (B7); T10 is the likelier cut.
- **All ~35 art assets shipped** — specs yes; expect 7 town/hero pairs + partial icons. Degradation is graceful only post-V4a (R9).
- **Telemetry-U4 decision traces** — gated behind staged-U4 by `Expedition/` ownership; slots post-weekend before M7a (its dep).
- **All Horizon-2 M-units** (M2a, M15, V6, M14, M7a, M9, M6, M8, M11a, M12) — that is v1.0, not this weekend; hence the tag is `v1-playable` (R5).
- **LLM-flavor prep** beyond a one-page corpus-export note (runtime LLM DEFERRED by ruling regardless).
- **V7a polish** — ships functional (slate + two verbs + rejection reasons), not beautiful.

Sources: master plan `docs/plans/2026-07-18-001` (units, gates, rulings R1–R9); `docs/plans/2026-07-17-002`; `docs/plans/2026-07-17-003`; `docs/design/lane-operating-model.md` §13 (origin/main @ `a938436`); verified 2026-07-18: PR #43 OPEN (`gh pr view 43`), V5a merged #38, O1 merged #35, `CombatMath` Level reads, `FactionConformanceTests` positive-price pin, `IconRegistry` lacks `Lit`.
