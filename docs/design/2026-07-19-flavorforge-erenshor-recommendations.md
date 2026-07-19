# Recommendations — FlavorForge First Authored Run (#3) + Erenshor Mechanics (#5)

**Status: recommendations only — nothing executed.** Fable synthesis over two read-only research
dossiers (local machine probe + repo architecture dossier, file:line grounded). 2026-07-19.

---

## Part 1 — FlavorForge first authored run (#3)

### Ground truth

- Tool is shipped and merged (#98 + #99): propose/emit modes, engine-gated acceptance, stub-tested
  (31 tests). **Neither Ollama nor LM Studio is installed on this machine** — ports 11434/1234
  dead, no binaries. An install step precedes everything.
- Cell inventory (4 voices: gruff, dramatic, wry, omen):

  | Surface | Base keys | Cells | Variants/cell today | Exact-prose golden? |
  |---|---|---|---|---|
  | Narrator | 13 | 52 | **≥4 (thinnest)** | **No** |
  | Tavern | 8 | 32 | ≥12 (deepest) | Yes — re-pin on emit |
  | Faction | 2 | 8 | ≥18 | No |
  | Ledger | 2 | 8 | ≥12 | Yes — re-pin on emit |

- The verbatim-slot contract (`FlavorEngine.TryRenderTemplate`, FlavorEngine.cs:129-136) rejects
  any paraphrase — acceptance *yield* is the real quality metric per model.

### Recommendation

1. **Install Ollama** (not LM Studio): headless service, scriptable CLI, matches the shipped
   `LocalHttpModelClient` /api/generate path. One-time install, your approval needed.
2. **Model: `mistral-nemo` 12B Q4** (~8 GB VRAM) as primary — clear prose-quality step over 8B,
   fits RTX 5080 with room. Escalate to Mistral Small 24B only if lines feel flat; fall back to
   Llama 3.1 8B CPU-only if we want to run while the art lane still owns the GPU.
3. **Timing: after the art long-tail lane finishes** (GPU free). Not urgent-blocking anything.
4. **Surface order = risk-ranked:**
   1. **Narrator first** — thinnest pool (4/cell) on the most-replayed surface (every expedition),
      and ZERO golden re-pin cost. Perfect shakedown run.
   2. **Faction** — also no prose golden, tiny (8 cells).
   3. **Ledger** — one golden re-pin, small.
   4. **Tavern last** — already deepest pool (12+/cell), lowest marginal value, has re-pin ceremony.
5. **Volume:** 12 candidates/cell proposed, expect 40–70% engine acceptance, target +6/cell kept.
   Narrator wave ≈ +300 accepted lines → pool grows 4 → ~10 per cell.
6. **Workflow per surface:** `--stub` dry-run → live `propose` → **human skim of proposal file**
   (you, or a curation agent with your spot-check) → `--emit` → fast lane → re-pin (Tavern/Ledger
   only, human-read per plan rule) → normal PR with reviewable pack diff.
7. **Verify-first item:** confirm the shipped SurfaceContract actually wires the Narrator surface
   (it lives in `GameSim.Narrative`, different namespace than the other three packs) before the
   run — 5-minute check baked into the run task.

**Ask:** approve Ollama install + model pull, and this surface order. Then it's one lane, ~a
session per surface.

---

## Part 2 — Erenshor mechanics (#5): five verdicts

Grounding: full repo dossier (Drama/Expedition/Heroes/Economy/Contracts, file:line cited in the
session transcript). Key global facts:

- **No per-hero opinion, memory-of-player, rivalry, or personality state exists.** Hero =
  id/name/class/level(unused)/hp/gold/gear/item-memories/alive/deepest-floor.
- **QualityGrade (Poor→Masterwork) exists on every item but is read by NOTHING downstream** —
  shopping AI ignores it entirely. Huge free lever.
- **ShoppingAi is flat:** every alive hero shops every Morning; only gates are role-fit / afford /
  is-upgrade. Pass reasons enum lives module-side (NOT deny-listed).
- **Death cause is monster-prose only** — no gear-fail vs decision-fail split anywhere.
- Contracts changes = orchestrator micro-PRs; the trailing-optional-field save-pin pattern is
  proven (PlayerState.Standing precedent). Enums append-only.
- New gossip base keys = sim-content decisions owned by the plan adding the emitting event
  (FlavorForge plan boundary, then FlavorForge widens the pools afterward — good pipeline).

### M1 — Memory-anchored gossip. Verdict: DO FIRST (size S)

`AttributionBeatEvent` already carries hero+item+floor, and the killingBlow / lethalSave /
breakpointClear / provisioned / potionLifesave keys are already item-anchored. The gap is
narrower than the pitch: HeroDied gossip doesn't cite gear (though `WornGear` already rides the
event, unused).
**Phase A (zero contracts):** enrich GossipGenerator usage of existing beats + coarse
"died wearing X" line from WornGear. **Phase B (optional micro-PR):** track the fatal-fight item
properly. Golden cost: gossip prose pins re-pin (normal ceremony).

### M2 — Reputation-gated shop relationship. Verdict: DO SECOND (size M)

Needs one contracts micro-PR: per-hero `OpinionOfPlayer` scalar (trailing-optional, save-safe).
Everything else module-internal — and the repo already contains the exact mechanical shape to
clone: faction Standing = bounded int → per-mille price curve → threshold-crossing gossip events.
Boycott = state transition with recovery days, same pattern.
Risk: shopping-behavior change moves the economy → the 100-day Balance gate may need re-tuning.
Biggest payoff of the five: closes the loop player-action → hero-feeling → sales.

### M3 — Status-tier picky heroes. Verdict: DO WITH M2 (size S/M, same files)

Almost free structurally: QualityGrade exists unused; `PassReasonKind` enum is module-side (add
`QualityTooLow` without touching Contracts); veteran proxy = existing `DeepestFloorReached`.
Real costs: one new gossip base key (quality-mock) = new-key ceremony + pins; and one **critical
design guard — KD3 no-softlock:** new players craft Poor gear at start, so ONLY veterans
(floor ≥ N) get picky; rookies must keep buying anything. Bundle with M2 — both rewrite
ShoppingAi; one shopping-behavior wave, one balance re-tune.

### M4 — Attribution-clear failure causality. Verdict: DO THIRD (size M, standalone)

Needs one contracts micro-PR: `DeathCauseKind` enum field on HeroDied (append-only, save-safe).
The classification machinery half-exists: AttributionEngine already does counterfactual replay
over recorded rolls (the AE2 lethal-save pattern = "replay with modified gear" — reusable to
answer "would better gear have saved them?" vs "did they ignore the flee threshold?").
Erenshor lesson this serves: opaque failure reads as arbitrary; this makes every death legible.

### M5 — Hero rivalry over Depths records. Verdict: LAST — needs its own plan (size L)

Most expensive: new relationship state (Contracts), new event type (`DepthsRecordOvertaken`,
touches the deny-listed JsonDerivedType registry), first-ever two-hero slot scheme in flavor keys,
and party-friction touches PartyFormation + resolver → determinism + Balance ripple. Highest drama
ceiling, most systems crossed. Recommend a dedicated brainstorm → plan cycle (Plan 009) rather
than riding this wave.

### Proposed sequencing

| Wave | Content | Contracts micro-PRs | Size |
|---|---|---|---|
| A | M1 phase-A gossip enrichment (+ pairs naturally with FlavorForge Narrator run) | 0 | S |
| B | M2 + M3 shopping-behavior phase (one plan doc) | 1 (opinion scalar) | M |
| C | M4 death causality | 1 (DeathCauseKind) | M |
| D | M5 rivalry — dedicated Plan 009 brainstorm first | 2+ | L |

**Ask:** bless the sequencing (or reorder), and whether Wave A starts immediately or waits for
your playtest findings. Waves B–D each get a plan doc per your model before any code.

---

## Post-/clear bootstrap prompts

Owner /clears between phases. Paste the matching prompt into the fresh session. All assume:
caveman ultra every reply + all subagents; Fable = plan/status/verdict only, work via sonnet
subagents; shared root c:\Code\Game NEVER committed — worktrees ../Game-<slug> from origin/main;
one unit = one branch = one PR, auto-merge, rescue `gh pr update-branch --rebase`; gates =
sim fast lane + engine tests via .runsettings; read CLAUDE.md + HANDOFF.md first.

### A — Playtest-findings fix session
> Caveman ultra every reply + subagents; Fable orchestrates only. Read CLAUDE.md + HANDOFF.md
> (main). Worktree rules per HANDOFF. My playtest findings: [PASTE FINDINGS]. Triage into
> fix-units (one branch/PR each, sim purity R14 respected — UI fixes in godot/, sim bugs get
> failing test first), execute serially via sonnet subagents, gate each with fast lane + engine
> tests, report per unit terse.

### B — FlavorForge authored run (needs: Ollama installed, model pulled)
> Caveman ultra every reply + subagents; Fable orchestrates only. Read CLAUDE.md + HANDOFF.md +
> docs/design/2026-07-19-flavorforge-erenshor-recommendations.md Part 1. Execute FlavorForge
> authored run per that doc: verify Narrator surface wired in SurfaceContract, then per surface
> (Narrator → Faction → Ledger → Tavern): --stub dry-run, live propose vs http://127.0.0.1:11434
> mistral-nemo, STOP for my review of proposal file, then --emit + fast lane + golden re-pin
> (Tavern/Ledger only, read each new pin) + PR. One surface per PR. No pack key/fallback changes.

### C — Erenshor Wave A: memory-anchored gossip (M1 phase-A)
> Caveman ultra every reply + subagents; Fable orchestrates only. Read CLAUDE.md + HANDOFF.md +
> docs/design/2026-07-19-flavorforge-erenshor-recommendations.md Part 2 (M1). Implement M1
> phase-A: zero Contracts changes — enrich GossipGenerator to exploit existing attribution beats
> (hero+item already on events) + coarse "died wearing X" from HeroDied.WornGear. New gossip
> prose = golden re-pin ceremony (read every new pin). TDD, sim purity, one branch/PR, both gates.

### D — Erenshor Wave B: shopping-behavior plan (M2+M3)
> Caveman ultra every reply + subagents; Fable orchestrates only. Read CLAUDE.md + HANDOFF.md +
> docs/design/2026-07-19-flavorforge-erenshor-recommendations.md Part 2 (M2+M3). PLAN FIRST, no
> code: brainstorm-lite + write plan doc docs/plans/ for the shopping-behavior wave — per-hero
> opinion scalar (contracts micro-PR, trailing-optional pattern), reputation-gated
> frequency/price-tolerance/boycott (clone faction Standing shape), QualityTooLow pass reason +
> veteran pickiness (KD3 guard: rookies never picky), balance-gate impact. Show me plan for
> go/no-go before any implementation.
