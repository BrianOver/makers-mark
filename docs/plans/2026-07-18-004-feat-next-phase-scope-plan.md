---
artifact_contract: ce-unified-plan/v1
artifact_readiness: requirements-only
product_contract_source: ce-brainstorm
title: Next-Phase Scope (Playable + Art + UI + Dev-LLM) - Plan
date: 2026-07-18
---

# Next-Phase Scope (Playable + Art + UI + Dev-LLM) — Plan

> **Role of this document.** This is the requirements-only foundation (the "what") for the
> next development phase of Maker's Mark. It is the shared north-star that the four
> implementation plans cite as their product contract. It does **not** contain implementation
> units — each large item is enriched into its own `implementation-ready` plan:
>
> | # | Plan | File | Depends on |
> |---|------|------|------------|
> | 1 | Playable Core | `2026-07-18-005-feat-playable-core-plan.md` | — |
> | 2 | Art Pipeline & Wiring | `2026-07-18-006-feat-art-pipeline-wiring-plan.md` | pipeline health |
> | 3 | Full UI Rethink | `2026-07-18-007-feat-ui-rethink-plan.md` | Plan 1 + Plan 2 |
> | 4 | Dev-Time Flavor LLM (FlavorForge) | `2026-07-18-008-feat-flavorforge-devtool-plan.md` | — (independent) |
>
> Work order: **1 → 2 → 3**, with **4** landing any time (independent). Each is worked as its
> own phase, one at a time.

---

## Goal Capsule

**Objective.** Turn a complete-but-unreachable simulation into a game a player can actually
sit down and play, that *looks* like the game the art was made for. The sim core is done and
green (722 sim tests, `Category!=Balance`); the entire gap is the Godot presentation layer, the
player on-ramp, and content wiring.

**Product authority.** This document. The four implementation plans enrich it; they do not
redefine scope. A substantive product-scope change loops back here first.

**Open blockers.** None. The three keystone forks (day-advance model, material acquisition,
UI ambition) were resolved in brainstorm dialogue and are recorded as Key Decisions below.

---

## Summary

Make Maker's Mark playable and good-looking: a reachable craft→sell loop for **all four
professions** (blacksmith, tanning, alchemy, engineering) with a pick-your-starting-profession
opening, a **hybrid day clock** (advances on the player's click, optional auto-play toggle),
a **full ground-up game UI** (storefront dashboard, portrait hero roster, drag-to-craft, venue-map
hub) that finally puts the generated art on the gameplay screens, plus a **dev-time flavor-content
generator**. New gameplay *mechanisms* are explicitly deferred until the core is playable.

---

## Problem Frame

The player is a crafting NPC who runs a shop; six autonomous AI heroes raid venues and buy the
gear the player makes. The attribution engine ("Torvald still carries your Fine Iron Blade — 34
kills") is the fun thesis. Blacksmith is **one of four** professions, not the whole game.

A weekend of work shipped the full staged-resolution sim, four professions, six hero classes,
four venues, material/faction economy, palette families, lighter-tone flavor, and a large art
catalog. But a live playtest surfaced that **none of it is reachable or legible in the actual
game**:

- **The craft loop is dead on day 1.** A fresh campaign starts with zero materials, zero heroes,
  zero material offers, and the only material source is returning heroes' Evening offers — so the
  player cannot buy anything to craft, cannot craft, cannot stock, cannot sell. The playtest saw
  `REJECTED: No handler accepts BuyOreAction during Morning | Not enough iron: need 2, have 0`.
- **Timing traps.** Actions are submitted against a real-time auto-advancing clock; the Evening
  Ledger modal opens after the Evening tick has already advanced the phase, so purchases queue
  against the wrong phase and are correctly rejected. This is a *class* of bug, not one instance.
- **The art is invisible in play.** Generated PNGs are wired only into the Town-tab backdrop; the
  six management panels use placeholder programmatic controls; most authored art specs were never
  generated to pixels; and most committed PNGs lack `.import` sidecars so they don't even render
  locally.
- **The UI is unstyled by design.** The panels carry a "placeholder look by design" note — no
  theme, no typography, no contrast, no layout. The "massive redesign" the owner wants is genuinely
  unbuilt, not a regression.

---

## Requirements

Traceability IDs (`R#`) are cited by the four implementation plans.

**Playability (Plan 1)**

- **R1.** A day advances only when the player chooses to advance it (player-gated), with an
  optional auto-advance toggle that fires the same advance on a timer. Gated is the source of
  truth; auto rides on top.
- **R2.** Every selected profession has a reachable material→craft→stock→sell loop in the Godot
  UI, not just blacksmith. Material acquisition is not tied exclusively to hero raids.
- **R3.** A direct materials vendor is available in a fixed, always-legal day phase (Morning), from
  which any selected profession can buy its base materials at a standard price. Returning heroes'
  Evening offers remain as a rarer/cheaper/exotic economy layer on top.
- **R4.** On new game, the player chooses a starting profession and receives a small starter
  material stock for it, so day 1 is immediately playable.
- **R5.** It is impossible to hard-lose or soft-lock the game. There is always an affordable path
  back to a productive loop (an economy floor: always-affordable base recipe(s) + a guaranteed
  recovery income or equivalent), so a player can never dead-end with no way to earn.
- **R6.** Illegal or unaffordable actions are prevented at the UI (disabled/hidden controls), not
  submitted-and-rejected. Any rejection that does surface is a transient, player-phrased message —
  never a raw kernel handler string dumped into a persistent status bar.
- **R7.** The Evening Ledger and Camp modals render normally (no one-character-per-line vertical
  text collapse).

**Art wiring (Plan 2)**

- **R8.** Committed art assets render from a fresh local checkout without a manual pre-import step
  (or a documented, automated import step exists that the launcher runs).
- **R9.** High-value authored art specs (item icons, venue backdrops, hero/monster portraits) are
  generated to pixels and committed.
- **R10.** Generated art is loadable by the gameplay UI through a manifest/registry the Godot layer
  consumes, not hand-placed per scene.

**UI rethink (Plan 3)**

- **R11.** A shared Godot Theme resource governs fonts, sizes, contrast, spacing, and panel
  styleboxes across every screen; all text is legible at target resolution.
- **R12.** The core screens are rebuilt as real game UI: a storefront/shop dashboard, a
  portrait-driven hero roster, a craft interaction (drag material→recipe or equivalent), and a
  venue-map hub — with the generated art woven in.

**Dev-time LLM (Plan 4)**

- **R13.** A dev-time tool generates flavor-pack content variants using a local model, validates
  every candidate through the existing flavor template contract, and emits committed pack data.
  Ships zero runtime weight and zero new runtime dependency.

**Cross-cutting**

- **R14.** All sim-purity and determinism invariants hold unchanged (KTD2/determinism in
  `CLAUDE.md`): zero Godot refs in `sim/GameSim`, no RNG/wall-clock/transcendental math in sim, and
  the golden-replay test stays green. Presentation and dev-tooling changes never write into
  `GameState`, saves, or chronicles.
- **R15.** The fast sim lane stays green and new Godot behavior gains engine-test coverage
  (gdUnit4Net) — both playtest blockers shipped CI-green because no test drives the UI loop or
  asserts label layout.

---

## Key Decisions

Decisions resolved in brainstorm dialogue, with rationale. Carried into the plans as constraints.

- **KD1 — Hybrid day clock, player-gated as the source of truth.** *Chosen over* pure timed
  auto-advance (current) and pure player-gated. Gated advance dissolves the entire class of
  "modal opened on the wrong side of the tick" timing bugs by construction — the ore-buy blocker
  cannot happen when nothing advances without the player. The optional auto-advance toggle
  preserves the cinematic "watch the heroes" feel by firing the same advance action on a timer.
  Trade-off accepted: a second (auto) code path to test, kept thin by routing it through the gated
  advance.
- **KD2 — Direct vendor floor + hero offers as upside.** *Chosen over* seed-stock-only and
  hero-offers-only. A base-materials vendor open every Morning gives every profession a reliable
  supply floor so day 1 (and every day) works; returning-hero Evening offers stay as the
  interesting economy "spice" (rarer, cheaper, exotic). Fixes the on-ramp for all four professions
  at once rather than patching the ore path alone.
- **KD3 — Pick a starting profession + starter stock; game cannot be lost.** New-game chooses one
  profession and seeds a little stock for it. The economy has a hard no-softlock floor: the game is
  forgiving by design, not ultra-difficult, and it must be *impossible* to dead-end. Exact floor
  mechanism (always-affordable recipe, recovery stipend, sell-back, or a combination) is a Plan 1
  design decision, but the guarantee is a hard requirement (R5).
- **KD4 — Full UI rethink, not a re-skin, sequenced after playability.** *Chosen over* a
  legibility-only theme and a functional-theme-only pass. The owner wants real game screens with
  the art woven in. To de-risk and get the game in hand fast, playability (Plan 1) lands first with
  *functional* styling; the full rethink (Plan 3) is its own later wave. Art wiring (Plan 2) sits
  between them because the rethink needs loadable art.
- **KD5 — Dev-time flavor generation first; runtime LLM stays parked.** The repo's prior research
  (`docs/plans/2026-07-16-002-feat-catalog-adaptation-policies-plan.md`) already ruled: build-time
  content packs first, optional runtime LLamaSharp reword layer second. This phase builds only the
  dev-time generator (the self-generate/test tool the owner wants). The runtime reword layer — which
  can live only in the Godot adapter, never the sim — remains deferred until explicitly un-parked.

---

## Scope Boundaries

**In scope (this phase, across the four plans):** the playable multi-profession loop, the hybrid
day clock, the materials vendor + starting-profession on-ramp + no-softlock floor, the ledger/camp
layout fix and rejection UX, art generation + wiring, the full UI rethink, and the dev-time
FlavorForge generator.

**Deferred for later (explicit non-goals this phase):**

- **New gameplay mechanisms** — venue-graph/routing (M6), abilities/leveling/recruit-pool (M8),
  monster dens (M11a), disasters (M12), XP bookkeeping (M2a), sociology (M9), bounty utility (M7a),
  vanity (M12), hero letters (D6), job board (T10). These are the "addons," explicitly after the
  core is playable.
- **Runtime in-game LLM** — the Godot reword layer (LLamaSharp, two call sites) stays parked until
  the owner un-parks it and pack variety proves insufficient in play.
- **Balance debt** — the standing gold-inflation anomaly (13/20 seeds) and tariff-lever saturation
  are known and tracked in `runs/anomalies.md`; retuning happens before the next band-moving
  mechanism, not in this phase, unless it directly blocks the playable loop.
- **Steam single-`.exe` export/packaging** — far-future (Godot export templates + preset).
- **Save/load persistence** — the sim has `SaveCodec`; whether the Godot layer wires quit/resume is
  a fast-follow to verify, not committed here.

**Housekeeping (do before/alongside Plan 1, not a "large item"):** resolve the committed git
conflict markers in `.claude/tasks/BOARD.md` and reconcile the stale claim/gate tables against git
history. Small, mechanical; folded into Plan 1's foundation, not its own plan.

---

## Open Questions

- **OQ1 (Plan 1).** No-softlock floor mechanism: always-affordable base recipe, a small daily
  recovery stipend, guaranteed sell-back to the vendor, or a combination? Resolve at Plan 1
  planning time; the *guarantee* (R5) is fixed regardless.
- **OQ2 (Plan 1).** Does the direct vendor sell *all* material keys (every profession's base mats)
  or only the selected profession's? Leaning all-keys so multi-profession dabbling works; confirm
  against economy balance.
- **OQ3 (Plan 2).** Is the local ComfyUI art pipeline currently healthy and does an
  AssetSpec→prompt→PNG path exist end-to-end? Verify first thing in Plan 2; if broken, generation
  scope shrinks to whatever can be produced.
- **OQ4 (Plan 3).** How far does "drag-to-craft" go vs a simpler click-to-craft with a strong
  visual recipe card? Resolve at Plan 3 planning time against Godot input effort.

---

## Sources

- Playtest findings + 6-agent read-only audit (this session): playability, art-wiring, UI, LLM,
  and project-state lanes.
- `docs/plans/2026-07-18-001-feat-game-completion-master-plan.md`, `-002-grind-block-schedule.md` —
  master plan + grind schedule.
- `docs/design/2026-07-18-variety-tone-direction.md` — palette families + tuning-C.
- `docs/plans/2026-07-16-002-feat-catalog-adaptation-policies-plan.md` — the standing LLM ruling
  (dev-time packs first, runtime LLamaSharp second).
- Grounding reads: `sim/GameSim/Professions/ProfessionRegistry.cs` (4 professions, `IsSelected`),
  `sim/GameSim/Economy/OreMarketHandlers.cs` (Evening-only hero-offer material path), `CLAUDE.md`
  (sim purity, determinism, multi-agent rules).
