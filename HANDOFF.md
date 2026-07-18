# HANDOFF — Maker's Mark next-phase planning (2026-07-18)

> ## ⚠️ CAVEMAN ULTRA ONLY — EVERY REPLY ⚠️
> The owner (Brian) requires **caveman ultra** mode on every single response, plus on all
> spawned subagents. Terse smart-caveman: drop articles/filler/pleasantries/hedging, fragments
> OK, technical terms exact, code/commits/security written normally. This is non-negotiable and
> persists the whole session. If the plugin isn't active, behave as if it is.

**Handoff from** brian.over@fornida.com **to** brian.admin@fornida.com (usage rolled over).
**Repo:** c:\Code\Game (private GitHub, C#/net10.0 sim + Godot 4.6.3-stable mono). Owner: Brian.

---

## Where we are

Weekend shipped the full **sim** (complete, 722 tests green, `Category!=Balance`). A live
playtest proved the game is **not reachable or legible** in Godot: craft loop dead on day 1,
timing traps reject actions, generated art not wired into gameplay panels, UI unstyled by design.

A 6-agent read-only audit diagnosed it (see the foundation doc's Problem Frame). Brian's
direction: **brainstorm + plan the WHOLE next phase FIRST, as SEPARATE plan docs per large item,
then work them one phase at a time.** Core (playable + art + dev-LLM) before any new-mechanism
addons.

We are in the **planning** stage. **No implementation started. Do not code yet** — finish the
plans, get Brian's go, then work phase-by-phase.

---

## The confirmed decisions (from brainstorm dialogue — do NOT relitigate)

- **KD1 — Hybrid day clock, player-gated as source of truth.** Nothing advances without the
  player's "Advance"; optional auto-advance toggle fires the same advance on a timer. Root fix for
  the "REJECTED: BuyOreAction during Morning" timing-bug class.
- **KD2 — Direct vendor floor + hero offers as upside.** A Morning-legal base-materials vendor for
  every profession (day 1 always works); returning-hero Evening offers stay as the rarer/cheaper
  economy layer.
- **KD3 — Pick a starting profession + starter stock; game cannot be lost.** New-game picks one of
  the 4 professions + seeds a little stock. HARD no-softlock guarantee — forgiving by design, must
  be impossible to dead-end.
- **KD4 — Full UI rethink (not a re-skin), sequenced after playability.** Storefront dashboard,
  portrait roster, drag-to-craft, venue-map hub, art woven in. Playability lands first with
  functional styling; the full rethink is its own later wave.
- **KD5 — Dev-time flavor generation first; runtime LLM stays parked.** Build only the dev-time
  FlavorForge generator. Runtime LLamaSharp reword layer (Godot-only, 2 call sites) stays deferred
  until Brian un-parks it. Reason: repo ruling `docs/plans/2026-07-16-002-...`.

Remember: **blacksmith is ONE of FOUR professions** (blacksmith/tanning/alchemy/engineering). The
loop must work for all of them, not just ore.

---

## The plan set (1 foundation + 4 phase plans)

| # | Doc | Status | Owns R-IDs | Depends on |
|---|-----|--------|-----------|------------|
| — | `docs/plans/2026-07-18-004-feat-next-phase-scope-plan.md` | **written** (requirements-only foundation) | all | — |
| 1 | `docs/plans/2026-07-18-005-feat-playable-core-plan.md` | **written** (8 units, Deep) | R1–R7, R14, R15 + BOARD.md housekeeping | — |
| 2 | `docs/plans/2026-07-18-006-feat-art-pipeline-wiring-plan.md` | **written** (7 units, Deep) | R8, R9, R10, R14 | pipeline health |
| 3 | `docs/plans/2026-07-18-007-feat-ui-rethink-plan.md` | **written** (8 units, Deep) | R11, R12, R15 | Plan 1 + Plan 2 |
| 4 | `docs/plans/2026-07-18-008-feat-flavorforge-devtool-plan.md` | **written** (Standard) | R13, R14 | — (independent) |

**All 5 docs are written and pushed to branch `chore/next-phase-plans-handoff`.** The background
workflow completed after the first push. Review, then get Brian's go/no-go and work Plan 1 first.

**Work order:** 1 → 2 → 3, with 4 any time. Each worked as its own phase.

Read the foundation doc `...-004-...` first — it holds the full Requirements (R1–R15), Key
Decisions (KD1–KD5), Scope Boundaries, and Open Questions (OQ1–OQ4) that all four plans cite.

---

## Background workflow — the 4 plans are being authored right now

A `Workflow` (run id **wf_b7b4e912-9e3**) launched 4 parallel Opus planning agents, one per plan
above, each grounding in the repo and writing its own implementation-ready doc. At handoff time
**0 of 4 had landed** (only foundation 004 on disk).

**On resume, first check whether 005–008 exist on disk:**
```
ls docs/plans/2026-07-18-00[5-8]*
```
- **If present** (same machine finished the run): review them against the foundation, then proceed
  to Brian's go/no-go.
- **If absent** (cross-machine, or run didn't finish): re-author them. The workflow run is
  **not cross-session resumable** on a different account, so just re-launch a fresh
  plan-authoring workflow, OR author the 4 docs directly. Each plan's scope, owned R-IDs, ground
  files, and design intent are fully specified in the workflow script:
  `.../workflows/scripts/author-phase-plans-wf_b7b4e912-9e3.js` (in this session's dir) — and the
  design intent is recoverable from the KD list above + the foundation doc.

---

## To-dos (current)

1. ✅ Foundation scope doc 004 written.
2. ⏳ Author the 4 phase plans (005 playable, 006 art, 007 UI, 008 flavorforge) — in flight.
3. ⏭ Review all 5 docs; report paths + unit counts to Brian; get go/no-go.
4. ⏭ On approval: **work phase-at-a-time** — Plan 1 (Playable Core) first.

## After the plans: how to execute (Brian's model)

- One unit = one branch (`feat/uN-slug`) = one small PR. Green checks + up-to-date required;
  auto-merge on — rebase + re-run when stale (`gh pr update-branch <n> --rebase`).
- **Never commit in the shared `c:\Code\Game` root** — use `git worktree add ../Game-<slug> -b feat/<slug> origin/main`.
- Deny-list (never edit unassigned): `Game.sln`, `godot/project.godot`, `.github/`,
  `sim/GameSim/Contracts/`, `CLAUDE.md`, `global.json`, `Directory.Build.props`, `.godot-version`.
- Gate before "done": `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance`.
- Sim purity (KTD2): zero Godot refs in `sim/GameSim`, no RNG-outside-kernel / wall-clock /
  transcendental Math; golden-replay stays green. Presentation/dev-tooling never writes GameState.

## Launch the game (Brian)

- Visual: double-click `play.bat` (needs Godot 4.6.3-stable mono at `C:\Tools\Godot\...` or
  `GODOT_BIN`). Text: `play-cli.bat`. Editor: `edit.bat`.

## Key context pointers

- `CLAUDE.md` — agent operating rules (read first).
- `docs/plans/2026-07-18-001-...master-plan.md`, `-002-grind-block-schedule.md` — prior roadmap.
- `docs/design/2026-07-18-variety-tone-direction.md` — palette families, tuning-C.
- `docs/plans/2026-07-16-002-feat-catalog-adaptation-policies-plan.md` — the LLM ruling.
- `.claude/tasks/BOARD.md` — coordination board (HAS committed git conflict markers — Plan 1 fixes).
- Grounding: `sim/GameSim/Professions/ProfessionRegistry.cs`, `sim/GameSim/Economy/OreMarketHandlers.cs`,
  `godot/scripts/MainUi.cs`, `godot/scripts/panels/`, `sim/GameSim/Flavor/FlavorEngine.cs`.
