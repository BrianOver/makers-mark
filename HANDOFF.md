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

**UPDATE 2026-07-19 — Plans 1–4 SHIPPED. Next-phase wave COMPLETE.**

- **Plan 1 (Playable Core, 005): SHIPPED.** All 8 units merged as PRs #78–#88. Gates at close:
  767 sim + 25 balance + 63 engine tests green, golden replay byte-identical throughout. Playable
  now: pick profession → Morning vendor buy → craft → stock → sell; gated day clock (Advance
  button, auto toggle); no-softlock destitution floor (R5 proof: 60-cell sweep zero dead cells);
  legality-gated controls + player-phrased toast; label collapse fixed; PlayableLoopTests locks
  the loop. NewGameSelect screen exists but `run/main_scene` still boots main_ui — needs a
  one-line orchestrator `project.godot` micro-PR.
- **Plan 2 (Art Pipeline & Wiring, 006): SHIPPED.** PRs #89–#94 merged (U6+U7 combined in #94).
  55 art ids in the manifest; AssetCatalog seam live. Deferred long tail (~22 specs) tracked in
  `docs/design/art-pipeline-health-2026-07-18.md`. Known CI flake: the godot engine-tests job
  SIGABRTs (exit 134) on shutdown *after* all tests pass — rerun the failed job, not a real
  failure.
- **Plan 3 (UI Rethink, 007): SHIPPED.** PRs #97 (U1+U2 theme+kit), #100 (U3+U4 storefront+roster),
  #101 (U5+U6 craft cards+venue hub), #102 (U7 HUD), #103 (U8 render harness). All merged.
- **Plan 4 (FlavorForge, 008): SHIPPED.** #98 (tool, propose-mode, engine-gated) + #99 (Game.sln
  wiring micro-PR). CI does not yet run the tool test suite (.github wiring deferred — flagged
  as known follow-up below).

**Whole next-phase wave (Plans 1–4) COMPLETE as of 2026-07-19.** Remaining known follow-ups:
art long tail (~22 specs), CI engine-tests SIGABRT-134 flake (rerun protocol, see above), CI
wiring for FlavorForge.Tests, drag-and-drop craft polish, Erenshor-derived ideas (in owner's
memory/backlog), dev-time music generation (future wave).

**Residuals:** formal 8-persona review panel aborted on Plan 1 (session token limit) — every unit
was TDD-red-first, per-PR diff-reviewed, CI green throughout; optional lean re-review later. Token
rules mandatory (see memory): Fable plans only, cheap models for work, caveman ultra everywhere.

Prior context: weekend shipped the full sim; a live playtest proved it unreachable in Godot
(craft loop dead day 1, timing traps, art invisible, UI unstyled). A 6-agent audit diagnosed it;
Brian's direction: plan whole next phase first as separate docs, then work one phase at a time.

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
| — | `docs/plans/2026-07-18-004-feat-next-phase-scope-plan.md` | foundation (requirements-only) | all | — |
| 1 | `docs/plans/2026-07-18-005-feat-playable-core-plan.md` | **SHIPPED** (PRs #78–#88) | R1–R7, R14, R15 + BOARD.md housekeeping | — |
| 2 | `docs/plans/2026-07-18-006-feat-art-pipeline-wiring-plan.md` | **SHIPPED** (PRs #89–#94) | R8, R9, R10, R14 | pipeline health |
| 3 | `docs/plans/2026-07-18-007-feat-ui-rethink-plan.md` | **SHIPPED** (PRs #97, #100–#103) | R11, R12, R15 | Plan 1 + Plan 2 |
| 4 | `docs/plans/2026-07-18-008-feat-flavorforge-devtool-plan.md` | **SHIPPED** (PRs #98–#99) | R13, R14 | — (independent) |

**Work order:** 1 → 2 → 3, with 4 any time. Each worked as its own phase.

Read the foundation doc `...-004-...` first — it holds the full Requirements (R1–R15), Key
Decisions (KD1–KD5), Scope Boundaries, and Open Questions (OQ1–OQ4) that all four plans cite.

---

## To-dos (current)

1. ✅ Foundation scope doc 004 written.
2. ✅ All 4 phase plans authored (005 playable, 006 art, 007 UI, 008 flavorforge).
3. ✅ Plan 1 (Playable Core) shipped — PRs #78–#88.
4. ✅ Plan 2 (Art Pipeline & Wiring) shipped — PRs #89–#94.
5. ✅ Plan 3 (UI Rethink) shipped — PRs #97, #100, #101, #102, #103.
6. ✅ Plan 4 (FlavorForge) shipped — PRs #98–#99.
7. ⏳ Follow-ups (not blocking): art long tail (~22 specs), CI engine-tests SIGABRT-134 rerun
   protocol, CI wiring for FlavorForge.Tests, drag-and-drop craft polish, Erenshor-derived ideas
   (owner's backlog), dev-time music generation (future wave).

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
