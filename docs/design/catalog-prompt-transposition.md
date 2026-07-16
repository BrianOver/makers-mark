# Catalog prompt transposition — the standing conversion contract (2026-07-16)

For add-on Claudes and pillar dispatches. Every pillar in `master-systems-catalog.md` ends in a
"CLAUDE TASK PROMPT" targeting Godot 4.3 GDScript + GOAP + runtime Ollama. This doc is the ONE
place that conversion to our stack (C#, pure deterministic sim, adapter-only Godot 4.6.3) is
derived. WHAT lands where (core vs add-on) is ruled in `master-systems-catalog-division.md`;
HOW a data unit ships is `docs/addon-guide.md`; this doc is HOW a catalog prompt becomes a repo
task. Binding requirements: R11–R13 of
`docs/plans/2026-07-16-002-feat-catalog-adaptation-policies-plan.md`.

## The two rules

1. **Design content is verbatim.** Mechanics, scenarios, ability/faction/item lists, and math
   INTENT (the shape of each curve) transfer unaltered from the catalog into the transposed
   brief. Numeric constants (β values, decay rates, thresholds) transfer as tuning starting
   points, never commitments — balance owns them after the pin tests exist.
2. **Dispatch = fill the template.** Transposing a prompt means applying the table below row by
   row — never re-deriving the conversion, never re-researching the stack (the catalog's
   "engine research" phases are pre-answered here and in `CLAUDE.md`). A construct that doesn't
   map through the table is a signal, not a license: stop and flag it to the orchestrator.

## The mapping table

| Catalog construct | This repo | Pattern to copy |
|---|---|---|
| GDScript file list (`Foo.gd`, `BarManager.gd`) | Rules → one sim module `sim/GameSim/<Module>/`; display → one thin panel `godot/scripts/panels/<X>Panel.cs` that renders state and submits actions, nothing else | Any existing module + `TavernPanel.cs` |
| GOAP planner / actions / blackboards | Deterministic utility scoring inside phase systems: integer score per option, fixed evaluation order, single-step decisions per phase. Every verdict records a legible reason (typed kind + human string) — decisions are UI, not just logic | `Heroes/ShoppingAi.cs` (`ShoppingVerdict`, `PassReasonKind`) |
| Personality / class modifiers (the β_class pattern) | Data, not code (R13): per-mille integer multipliers (`score * weightPerMille / 1000`, weight 0 = hard veto) or trait-shifted thresholds. Weights live on the definition record | `ProfessionQualityModel` shift dictionaries |
| Godot signals (`faction_changed`, timeout triggers) | Typed `GameEvent` records emitted via `IEventSink` into the event log; kernel stamps Id + Day; consumers (ledger, gossip, chronicle, panels) read the log, never subscribe | `Contracts/Events.cs` |
| Autoload singletons / `*Manager` classes | Fields on `GameState` for mutable state; static registries of immutable definitions for content, `ImmutableSortedDictionary` + ordinal keys, one orchestrator-applied registration line | `Professions/ProfessionRegistry.cs` |
| `_process` / `_physics_process` / real-time ticks / `∫…dt` | Day-phase ticks only: an `IPhaseSystem` in Morning, Expedition, or Evening. Continuous decay becomes a per-day (or per-floor) integer step; there is no frame time in the sim | `Contracts/IPhaseSystem.cs` |
| Ollama / "System 2" / LLM calls | The flavor-layer contract (plan R1–R6): sim emits structured events ONLY; text renders from committed content packs; fact slots substituted from the event and validated post-render; static fallback line mandatory. NO LLM in sim decisions, hero AI, balance, or CI — permanent | `Flavor/` engine (plan U3–U5) |
| Float math, `log10`, `e^-λt` | Integer redesign per curve intent: per-mille (or coarser) granularity, `Math.BigMul`/64-bit intermediates, rounding explicit per formula, golden-value pins at boundaries + a curve-shape test (monotonicity, decade steps, half-life landmarks) | `Kernel/IntegerCurves.cs` (plan U2) |
| 6-phase pipeline (Idea→Research→Confirm→Plan→Confirm→Code) | brainstorm → plan → work, with the SAME two user gates: confirm the brainstorm output before planning, confirm the plan before code. Research phases collapse into reading this doc + the division ruling | — |

Pillar-specific seams (minigame-as-graded-action, chronicle-over-event-log, …) are ruled per
pillar in the division doc — check your pillar's entry before filling the table.

## Worked example — Pillar 8 (Spells, Talents & Abilities), transposed

The catalog's "SPELL TRANSCRIPTION & ABILITY DECISION ENGINE (GODOT 4.3)" prompt, run through
every row. This is the brief a dispatch would receive — compact on purpose; the plan phase
expands it.

> **Task: Ability system — registry, combat triggers, scroll transcription.**
>
> **Division ruling:** MAIN = `AbilityDefinition` contract + registry + resolver trigger hook +
> scroll output on the profession path; ADD-ON = the 8 launch abilities as data units, scroll
> recipe packs. Contract types (`AbilityDefinition`, ability slots on Hero/ClassDefinition —
> P3 dependency) land as orchestrator micro-PRs first.
>
> **Design content (verbatim from the catalog):** 8 abilities, 2 per class, with mechanics,
> cooldowns, costs, tactical triggers; talent-planner training scrolls purchased at level-up;
> scroll transcription from rare paper + magical ink, higher transcription quality reducing the
> ability's cost/cooldown for its user; trigger intent — a cornered, low-HP caster prioritizes
> Blink over generic damage when it can pay the cost, firing at U_ability ≥ 0.75.
>
> **File mapping (row 1):**
> - `AbilityRegistry.gd` → `sim/GameSim/Abilities/AbilityDefinition.cs` + `AbilityRegistry.cs`
>   — static registry, ProfessionRegistry pattern (rows 1+5): immutable definitions, ordinal
>   keys, one registration line per add-on ability.
> - `SpellScrollTranscripter.gd` → no new handler: scrolls are ordinary profession recipes
>   (addon-guide path) whose output carries ability-effect data; quality rides the existing
>   universal quality model, exactly as `ConsumableEffect` magnitude already does.
> - `GOAPCombatAbilityTrigger.gd` → an integer threshold rule inside
>   `Expedition/ExpeditionResolver`/`CombatMath`, evaluated per hero per floor fight in HeroId
>   order (deterministic trigger order).
> - Display: ability lines join the existing panels — no new scene, no engine logic.
>
> **Trigger math (rows 3 + 8 — redesign per intent, constants are starting points):**
> - `missingHpPermille = (MaxHp - hp) * 1000 / MaxHp` (truncating divide, documented).
> - `HasMana` → hard veto: weight 0 when the cost can't be paid; no score can override (R13).
> - `EnemyProximityDistance` has no analog in the floor-fight model; its INTENT is danger
>   pressure — transpose as a per-mille monster-power-vs-hero-power factor. Flag at the
>   brainstorm gate; the proxy is a design decision, not a given.
> - Fire when the composed score ≥ 750 per mille (the catalog's 0.75). Transcription quality
>   shifts the threshold down per grade (trait-shifted threshold, R13).
> - Tests: golden pins at hp = 0, hp = MaxHp, score = 749/750; curve-shape: monotone
>   non-decreasing in missing HP; no floats, `BigMul` where factors compose.
>
> **Legible decisions (row 2):** every evaluation records fired-or-passed with a typed reason
> (`AbilityVerdict` mirroring `ShoppingVerdict`) — "Blink: held, mana short" is UI material.
>
> **Signals → events (row 4):** firings enter the expedition's recorded combat rolls, and a
> proven save via a player-transcribed scroll emits an `AttributionBeatEvent` (new `BeatType`
> via contract micro-PR) so scroll sales earn attribution like potions do.
>
> **No per-frame anything (row 6):** triggers exist only inside Expedition-phase resolution;
> transcription is a Morning `CraftAction`; scroll purchases ride the Evening shopping pass.
>
> **No LLM (row 7):** "teaching a Mage to cast Blink" tavern color = flavor-pack entries keyed
> to the new beat type; facts validated post-render, fallback line mandatory.
>
> **Pipeline (row 9):** brainstorm (ability variations, danger-pressure proxy — catalog
> Phase 1) → USER GATE → plan (definition schema, resolver hook, test scenarios — catalog
> Phase 4) → USER GATE → work. Catalog Phase 2's engine research is void — this doc answered it.

Every future pillar dispatch reads like the block above: same rows, that pillar's verbatim
design content.
