---
title: "Phase B — Living Heroes (re-scoped)"
date: 2026-07-25
type: feat
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
supersedes_scope_of: docs/plans/2026-07-21-006-phaseB-living-heroes.md
roadmap: docs/plans/2026-07-21-003-phased-roadmap.md
research: fable Phase-B research pass 2026-07-25 (in-session)
status: build-ready (fable-checked, fixes 1-7 applied)
---

# Phase B — Living Heroes (re-scoped 2026-07-25)

## Goal Capsule

Make the heroes read as **individuals** — the content of this inverted-MMO is the NPC counterparties,
and today they are one hero photocopied (thin: always Level 1, no traits, no needs, distinguishable
only by class/gold/floor). **"Living heroes" = heroes whose decisions differ under identical
circumstances, and whose differences are narrated on surfaces the player already reads.** Not "build
a deeper AI" — the legibility machine already exists (typed pass-reason cards, demand board, advisor,
gossip cap, ticker, memorials/legends that already *name* heroes). Phase B gives that machine
per-hero variance to report. Design axiom (research + audit both confirm): **feels alive = narrated
state, not deeper AI.**

**Re-scope note:** the 07-21-006 plan is stale — the 07-24/25 waves already shipped much of its
U-B3: `Hero.MoodPermille` (Erenshor M2 opinion, willingness-wired), veteran quality pickiness +
rookie guard (`ShoppingAi.cs`), the Sentimental storied-gear gate, derived `RelationshipBands`
(Stranger→Sworn), CommissionSystem, memorials/heirlooms, `LegendQuery` recruit-opinion seeding. This
plan re-scopes around what's already live.

## Gate B (target, made measurable)

"A stranger can name three heroes by personality after watching a run." Decomposed into four
observable, testable properties:
1. **Divergence (sim, deterministic):** on a pinned seed, for ≥3 hero pairs, the identical shelf item
   on the identical day yields a different verdict/typed reason, attributable to an inspectable trait/state.
2. **Attribution density (transcript, grep-able):** a 15-day scripted CLI run has ≥5
   personality-diagnostic lines for each of ≥3 distinct heroes (lines that would NOT fire for a hero
   with different traits), countable by line-template id.
3. **Recoverability (LLM):** a blind reader given only the transcript names 3 heroes + a ≤10-word
   personality phrase each, matching their registry traits.
4. **Identity integrity (prerequisite):** no undisambiguated duplicate names (FR-16); a hero has an
   inspectable card (`hero <name>` CLI + Godot panel) showing traits, mood/band, deeds, XP/rank.

**Gate B passes** when 1-4 hold for ≥3 heroes on ≥2 of 3 pinned seeds, AND the advisor's hero
forecast is exactly correct, AND the same LLM rubric run against a **pre-Phase-B build FAILS**
(control arm — otherwise the test proves nothing).

## Key Technical Decisions — the golden-safety taxonomy (central to Phase B)

Unlike the core-loop slices (all presentation, byte-identical golden), Phase B changes sim behavior.
The repo's test topology has **three classes** — scope every unit by class:

- **KTD-B0 — Class 0, split into 0a / 0b (fable-check fix 1):**
  - **0a — shape-only:** a new trailing-init `Hero` field that nothing reads. RNG stream AND every
    serialized value identical; the idle-hash re-pin note reads "shape-only, values identical"
    (`SignedName`, `Memorial.Honored` precedent). B0's `Hero.Xp` is 0a **only while unread**.
  - **0b — additive events/values, draw-free + decision-free:** new stamped events or accrued values
    (Xp counting up, `HeroRankUp`, `HeroDecisionExplained`, re-ranked gossip prose). No decision
    reads them and no draw is added, BUT the idle-hash moves because **values differ** (new events
    shift downstream `EventId`s; `GossipGenerator` feeds `eventId` into the FlavorEngine variant pick,
    so gossip prose changes; these serialize). Re-pin note MUST say "values changed (events/prose),
    draw-free & decision-free — deliberate re-baseline" (`CommissionPosted` precedent,
    `AtomicEquivalenceTests.cs:37-43`). **B1a/B1c/B1e are 0b, not 0a** — a builder copying the
    "shape-only" note onto a 0b re-pin corrupts the hash header's audit trail.
- **KTD-B1 — Class 1 (behavior-without-draws):** no new/reordered RNG draws, but hero decisions
  change → purchases/deaths/loot shift → idle hash moves for real + the 100-day Balance bands may need
  a re-fit. **Precedented and routine** (U9 veteran pickiness shipped exactly this). Determinism
  invariants hold throughout (same seed+actions = identical state). One deliberate re-pin + Balance
  re-fit per Class-1 unit, orchestrator-scheduled.
- **KTD-B2 — Class 2 (stream perturbation):** new kernel draws, reordered draws, or CombatMath change
  (the XP **level-flip**). Invalidates batch-farm tables → the serial Phase C hardening window.
  **NOTHING in Phase B may be Class 2.**

- **KTD-B3 — TRAIT ASSIGNMENT MUST BE `StableHash`, NEVER A DRAW. (Read this twice.)** The sim's
  entire RNG surface is three sites (`ExpeditionResolver` combat+loot, `QualityRoller` jitter,
  `HeroRoster.CreateRecruit` name/class/gold). Inserting `rng.NextInt` into `CreateRecruit` to pick
  traits would shift **every downstream draw** and silently make traits a **Class-2** change. Derive
  traits as a pure function of `StableHash(HeroId, Name)` (the `ForgePath` precedent) — zero draws,
  zero state, golden-stable. A trait is **derived, not stored**, until something *grants* one at
  runtime (Phase C+).
- **KTD-B4 — Contracts micro-PRs first (KTD9).** New `Hero.Xp` (trailing-init, save-compat, the
  `MoodPermille` pattern) and new event types land as an orchestrator-authored micro-PR (B0) merged
  before dependent modules. New Hero fields MUST be trailing-init.
- **KTD-B5 — no-draw property is a CI gate.** Extend the existing draw-count pin pattern
  (`QualityRoller`): assert the kernel draw-count over a 30-day trace is byte-identical before/after
  each Class-0 system. This is the tripwire that keeps traits/edges/salience out of Class 2.

## Scope Boundaries (non-goals — deferred to Phase C/D)

- **XP level-flip** (Xp → real Level → CombatMath) — Class 2, Phase C hardening window. Phase B ships
  XP bookkeeping + a **cosmetic rank ladder** only.
- **Trait RAID teeth** (RiskPm → target floor / flee offsets) — moves survival/depth bands (the
  Balance gate's core), batches with the level-flip in Phase C. Phase B traits carry **shop teeth only**.
- **Full Zubek 5-need LUT engine** — deferred; today heroes have essentially one activity (shop→raid),
  so the delta-scorer buys almost no visible behavior and risks replacing honest typed reasons with an
  illegible utility layer. Revisit when Phase C creates real activity choices. Phase B ships
  **needs-lite** (unmet-demand streak → telegraphed boycott).
- **True "leave town" roster removal** — collides with commissions/memorials/legends/permadeath;
  drama-director territory (Phase C). Phase B ships telegraphed dissatisfaction → boycott/shop-at-rival
  with a recovery path.
- **Erenshor wave D (full rivalry)** — its own plan; B3 only lays the relationship edges it will consume.

## Requirements

- **R-B1** Player-relevant hero decisions (player-shelf verdicts, muster target) stamp a
  `HeroDecisionExplained` card (chosen, runner-up, dominant reason, score gap), capped anti-spam like
  `HeroPassedOnItem`. (Legibility spine.)
- **R-B2** The advisor forecasts a hero's next-day decision by re-running the pure scorer against
  projected state; forecast == actual on a pinned seed (exact, because draw-free).
- **R-B3** `Hero.Xp` accrues (Evening reveal) and crosses cosmetic **rank** thresholds with a named
  event ("Torvald reaches Delver"). No CombatMath.
- **R-B4** No two roster entries share an undisambiguated name (FR-16); `hero <name>` CLI card + Godot
  hero panel show traits/mood-band/deeds/XP-rank.
- **R-B5** ~10 derived traits (StableHash), 2/hero, each with **shop teeth** read by
  `ShoppingAi`/`WillingnessModel`; trait chips surface in tooltips/gossip/barks.
- **R-B6** Hero↔hero relationship edges (`RelDelta`) stamped only at significant events (Nemesis
  rule); gossip salience ranks the day's log per speaker.
- **R-B7** Needs-lite: a per-hero unmet-demand streak → telegraphed boycott (demand-board card) →
  farewell + recovery path. No roster removal.
- **R-B8** Golden discipline: B1/B3-edges are Class 0 (hash re-pin only, no-draw gate green); B2/B4-need
  are Class 1 (one scheduled re-pin + Balance re-fit each); nothing Class 2.

## Implementation Units (sequenced by golden class)

### B0. Contracts micro-PR (orchestrator-only, lands FIRST) — Class 0a/0b
- **Files (`sim/GameSim/Contracts/`, deny-listed, orchestrator-authored) — TRIMMED per fable fix 5,
  only what B1 needs:** `Hero.Xp` (trailing-init `int = 0`, the `MoodPermille` pattern);
  `HeroDecisionExplained(HeroId Hero, string Chosen, string RunnerUp, string Reason, int GapPermille)`;
  `HeroRankUp(HeroId Hero, string Rank)`. **NOT in B0:** `RelDelta`/`GameState.Relationships` — B3
  derives edges from the event log (RelationshipBands precedent: shared expeditions, `HeroDied`,
  outbids are already stamped; decay = day-delta math), so no stored edge surface. `HeroBoycott`/
  `HeroReturned` land in a **B0.2 micro-PR** right before B4, not speculatively now.
- **Verify:** builds; `Hero.Xp` unread ⇒ `AtomicEquivalenceTests` unchanged (0a); the two event types
  are unstamped in B0 (declared only) so the hash is untouched until B1 stamps them; fast lane green.
  Committed first on the branch; B1 workers branch from it.

### B1. Legibility & identity spine — **Class 0** (the golden-safe first slice)
Narrate the individuality that already exists latently (mood trajectory, band, sentiment, veteran
pickiness, deeds, deepest floor) + add cosmetic progression + identity. Parallel-safe, no serial gate.
- **B1a — Decision cards:** stamp `HeroDecisionExplained` alongside the shopping/muster decisions the
  sim already makes (`HeroShoppingSystem`/`MusterSystem`), capped to player-relevant decisions. CLI +
  ticker render.
- **B1b — Advisor hero forecast (fable fix 2 — presentation-side shadow-tick, NO re-pin):** a
  same-day/conditional forecast: clone the current `GameState` and shadow-tick the pure
  `ShoppingAi`/`MusterPlan` scorers against it ("as the shelf stands, Torvald buys X"). Exact by
  construction, deterministic, presentation-side — it MUTATES NOTHING in the real sim and stamps no
  event, so it needs **no re-pin**. Do NOT try to predict the *next day* post-expedition (the Night
  expedition draws RNG + mutates hero state — an exact next-day forecast is impossible without
  replaying the RNG). Forecast-exactness test asserts the shadow-tick result == what the real scorer
  produces on that same state.
- **B1c — XP + cosmetic ranks:** `Hero.Xp` accrual at Evening reveal; rank thresholds emit
  `HeroRankUp`. **TRIPWIRE (fable fix 3):** rank NEVER writes `Hero.Level` — `CombatMath.cs:29,32`
  read `hero.Level` into Attack; touching Level is a Class-2 / Balance break, STOP. Rank is a pure
  label off `Xp` thresholds. (0b: Xp values accrue on the idle trace, so the hash moves with values.)
- **B1d — Identity (fable fix 4 — DISPLAY-LAYER, not gen-time):** a pure module-side disambiguation
  helper (the `RelationshipBands` derived-view precedent) that resolves duplicate names for the
  `hero <name>` lookup + Godot panel via a collision-ordinal epithet ("the Younger") — computed at
  read time, **NOT** by mutating `Hero.Name` at recruit-gen. This satisfies FR-16 with ZERO sim
  change, zero re-pin, and leaves B2's `(HeroId, Name)` trait-hash input clean. `hero <name>` CLI card
  (band/deeds/XP-rank; traits after B2) + Godot hero-panel enrichment; wire any dead CLI narration
  lines still present.
- **B1e — Gossip salience v1:** rank the day's log per speaker by involvement + recency (deterministic
  sort, 3-line cap preserved, no edges yet).
- **Verify:** no-draw property gate (KTD-B5: the serialized `state.Rng` after the 30-day idle trace
  is byte-identical before/after — clone the `AlchemyActiveCraftTests` `Assert.Equal(a.Rng, b.Rng)`
  pattern; this survives 0b value re-pins and isolates any accidental draw); forecast-exactness test;
  fast lane + Balance green.
- **Build batching (fable fix 7 — hash-mover contention):** B1a/B1c/B1e all move the single
  `ExpectedPreCounterSha256` pin, and B1a/B1c/B1d all edit `Program.cs`/`EventNarration.cs`. So the
  whole B1 sim+CLI spine is built by ONE worker (serial internally); the **orchestrator owns the ONE
  re-pin** after merge (recompute the hash on the merged tree, update the constant with the 0b note) —
  workers implement + self-test everything EXCEPT the golden pin and report "hash moved". B1's Godot
  hero-panel half is an independent parallel worker (godot/ only).
- **Honest caveat:** B1 alone gets a rubric reader to *describe* heroes circumstantially ("the sworn
  regular", "the veteran who refuses Poor work") — **Gate B is only safely passable after B2.** B1 is
  still the right first slice: it builds the whole reporting pipeline B2 plugs variance into.

### B2. Traits with shop teeth — **Class 1** (the Gate B slice; ONE scheduled re-pin)
- **Files:** new `sim/GameSim/Heroes/TraitDefinition.cs` + registry (~**10** traits, all with teeth,
  no flavor-only entries); derive 2/hero via `StableHash(HeroId, Name)` (KTD-B3). Traits write onto
  knobs `ShoppingAi`/`WillingnessModel` already read: price sensitivity, quality-demand offset,
  sentimental threshold, consumable stocking, haggle patience, gossip credulity. Trait chips into
  tooltips/gossip/barks/the hero card.
- **Golden:** ONE deliberate idle-hash re-pin + ONE Balance re-fit (U9 playbook — the hash header
  narrates the re-baseline). **Shop teeth only** — raid teeth withheld to Phase C.
- **Verify (fable fix 6 — the Class-1 gate is a GREP, not a stream pin):** trait teeth change
  purchases → gear → fight lengths → the *number* of `ExpeditionResolver` draws changes → the RngState
  MOVES (that's expected Class 1, U9 did it). So the gate is **"no new draw SITES"**: grep confirms
  `rng.(NextInt|Roll100)` stays confined to the same 3 files and the trait/registry code contains no
  `rng.` at all — a code-review/grep gate. Only Class-0 units get the byte-identical `state.Rng` pin.
  Plus: divergence test (≥3 trait pairs, same item → different verdict/reason); trait-registry manifest
  conformance; **one deliberate idle-hash re-pin + Balance re-fit** (flag the consumable-stocking knob
  — pack Heals directly gate deaths — as the strongest survival bleed for the re-fit note); Gate B
  rubric with the failing pre-B control arm (manual one-time: checkout pre-B commit → CLI transcript →
  LLM rubric).
- **Trait-variance decision (fable):** derive from `StableHash(HeroId, Name)` ONLY ⇒ the starting six
  have identical traits every campaign (fixed ids) — ADOPTED as the default (a consistent anchor cast;
  makes the divergence test trivially stable over the fixed six). If per-campaign variance is later
  wanted, mix in `state.Rng.Inc` (the campaign-constant, already-serialized id `GossipSystem` uses,
  draw-free) — a deliberate choice, never an accidental one.

### B3. Hero↔hero edges + gossip salience v2 — Class 0 (edges/prose) / small Class 1 (mood delta)
- **Files:** `sim/GameSim/Drama/RelationshipSystem.cs` — sparse `ImmutableSortedDictionary` of decaying
  signed `RelDelta` stamped only at significant events (shared expeditions, witnessed deaths, outbids
  — Nemesis rule); gossip salience v2 weights by affinity × credulity; heard gossip → small mood
  delta (mood already has willingness teeth = small Class 1). **4 relationship kinds** (comrade-bond,
  grudge, grief, rivalry-seed) — not 8; wave D owns full rivalry.
- **Verify:** no-draw gate for edges/prose; the mood-delta path re-pinned if it shifts the hash.

### B4. Needs-lite + departure valve — Class 1
- **Files:** `sim/GameSim/Heroes/NeedsSystem.cs` (lite) — per-hero unmet-demand streak (clone the
  demand board's depth-stall streak pattern); at threshold, telegraphed dissatisfaction card ("Sera
  has found nothing worth buying for 6 days"), then boycott/shop-at-rival with a farewell event +
  recovery path (M2's boycott shape, never shipped). **No roster removal.**
- **Verify:** one re-pin + Balance re-fit; the boycott is telegraphed ≥3 days before it bites.

### B5. Bark rule-DB enrichment — Class 0
- **Files:** the bark/criteria matcher — feed traits/edges/deeds into a Valve/Ruskin criteria-count
  matcher so heroes sound *aware*. Flavor/Godot, rides B2/B3 fact supply.

## Verification Contract

- **Per Class-0 unit (B0/B1/B3-edges/B5):** the **no-draw property gate** (30-day trace draw-count
  byte-identical before/after — KTD-B5) MUST be green; `AtomicEquivalenceTests` re-pinned only with
  the documented shape-only note; fast lane + Balance green.
- **Per Class-1 unit (B2/B3-mood/B4):** ONE deliberate idle-hash re-pin + Balance band re-fit,
  narrated in the hash header + PR body (U9 precedent); determinism invariants (same seed+actions =
  identical state) proven unchanged; NO new draws (the change is decision logic over existing draws).
- **Nothing Class 2** — any diff that adds/reorders a kernel draw or touches CombatMath STOPS and
  routes to Phase C.
- **Forecast-exactness test** (R-B2): advisor day-N hero forecast == day-N+1 actual, zero tolerance,
  pinned seed.
- **Gate B (run at B2, re-run after B4):** divergence + attribution-density + LLM-recoverability (with
  the mandatory failing pre-Phase-B control arm) + identity-integrity, ≥3 heroes on ≥2 of 3 pinned
  seeds. Godot: hero panel (trait chips/band/deeds/rank), ticker decision card, tavern salience gossip
  — all naming the same heroes the rubric identified.
- **Repetition-onset re-measurement** after B2 (audit's day-11-13 method) — Phase B doesn't own the
  flatline (C/D does) but personality variance should measurably delay verbatim-line repetition;
  capture the number either way.

## Definition of Done

- B0 (Contracts) + B1 (Class-0 spine) shipped, no-draw gate green, golden shape-only re-pin only.
- B2 (traits) shipped with its one scheduled re-pin + Balance re-fit; **Gate B PASS** (≥3 heroes, ≥2/3
  seeds, failing pre-B control confirmed).
- B3/B4/B5 shipped (edges, needs-lite, barks).
- Phase C carries: XP level-flip, trait raid teeth, true departure/drama-director.

## Open forks resolved (fable defaults adopted; flag to re-open if wrong)

needs-lite not full Zubek · traits derived not stored · 10 traits all-teeth (cut flavor-only) ·
"leave town" = telegraphed boycott not removal · decision legibility event yes (B0/B1, capped) · raid
teeth in Phase C · 4 relationship kinds · XP shown as cosmetic rank pre-flip.
