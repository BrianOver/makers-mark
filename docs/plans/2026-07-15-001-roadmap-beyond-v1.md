---
title: Maker's Mark — Roadmap Beyond v1
type: docs
date: 2026-07-15
topic: roadmap-beyond-v1
---

# Maker's Mark — Roadmap Beyond v1

Phased plan from the shipped v1 slice toward the original inverted-MMO vision (9 professions supporting a WoW-like hero class set, player as the quartermaster who enables autonomous AI heroes through supply, logistics, and intelligence). Each phase is shippable on its own and fans out across multiple Claude agents using v1's contracts-first-then-parallel model. Research-backed (see the note at the end on research provenance).

## North star

v1 is a deep, correct *vertical slice*: one profession (Blacksmith) proving the whole spine — deterministic seeded sim, event-sourced drama, and an attribution engine that proves the player's craft changed a hero's fate. The vision is that spine repeated in **breadth**: every profession earns attributable beats the way the blacksmith does, every hero class is a differentiated customer for those professions, and the player's agency grows from "craft + price + stock + bounty" into the full quartermaster fantasy — **supply** (gear/consumables), **logistics** (provisioning runs), and **intelligence** (scouting that shapes hero decisions).

## Execution model: cores here, add-ons fan out (decided 2026-07-15)

The work splits along its real seam:

- **CORE / BASE — built by the orchestrator session, in phase order.** The mechanisms, shared `Contracts/`, `GameComposition` registration (determinism-critical), and the resolver/attribution hooks. This is the entangled, single-owner work. Each core lands with **one reference implementation** that proves it — never untested plumbing:
  - P1 core proven by re-expressing the existing **Blacksmith as data** (generalization proven, zero new content).
  - P2 core proven by **one reference consumable** riding the loadout hook.
  - P3 core proven by **one reference class + one augment**.
  - P4 core proven by **one second venue + one scouting report**.
  - P5 core proven by **one personality trait + one relationship + one arc**.
  Each core ships as its own green PR with tests + balance passing before the next core begins.

- **ADD-ONS — fanned out to parallel task-Claudes, after all cores land.** The 2nd+ profession, hero class, map, or arc — pure data/"asset-type" content plugging into a proven core. Independent, disjoint directories (`Professions/<name>/` etc.), low determinism/balance risk. One task-Claude per add-on, claimed in `.claude/tasks/`, integrated by an orchestrator. This is where breadth (all 9 professions, the full class roster, many venues) gets filled in.

The seam: **a core is a mechanism proven by one example; an add-on is another example of a proven mechanism.** Front-load the entangled cores here; back-load the independent content to task-Claudes.

## What v1 already generalizes well (verified against the code)

- `PlayerState.Materials` is a generic `ImmutableSortedDictionary<string,int>` — hides, herbs, reagents, bone, essence, scrap all work as new keys with **zero refactor**.
- Drama is event-sourced and gossip already references real event IDs by contract — relationships/arcs are additive.
- The attribution engine's counterfactual method (remove-and-recompute over recorded rolls) extends to non-item and non-combatant contributors (potions, minions, companions, enchants).

## What v1 hardcodes and must open up first (verified against the code)

- `ItemSlot` enum + `GearSet(Weapon, Shield, Armor)` — fixed 3 slots via switch statements. New output categories (Consumable, Trinket) and slots touch every switch.
- `PlayerState.Talents` — a single flat `ImmutableSortedSet<string>`; no profession dimension. Blocks pick-1-or-2-per-save.
- `TalentTree`, `RecipeTable`, `QualityRoller`, `ItemForge`, `CraftingHandlers` — blacksmith-specific; must become profession-scoped and data-driven.
- `HeroRole` enum — 3 combat roles; must become a data-driven class model so professions have differentiated customers.
- `MonsterTable` — single-Mine, 5-floor; must generalize to a venue registry.

---

## Phase 1 — Profession Generalization Kernel + Tanning

**Goal:** Turn the blacksmith-specific crafting/talent/save layer into a profession-agnostic, data-driven substrate, and prove it by shipping a second profession (Tanning) with zero new mechanics. After this, the game plays like v1 + one profession, but the architecture is open for all the rest.

**Why first:** Hard prerequisite — no profession can be added until the kernel generalizes. Bundling Tanning (purest reuse: fills the existing Armor slot via the existing quality roller) proves the generalization against a real second profession, and immediately fixes the squishy-caster death loop by giving Strikers/Mystics real defense under the weight cap.

**Deliverables:** ProfessionId dimension on recipes + talents; `PlayerState` gains per-profession talent sets + a selected-professions field (pick 1-2); data-driven per-profession talent trees (Blacksmith + Tanning re-expressed as data, behavior-identical); profession-agnostic crafting pipeline; profession-selection action; Tanning leather-armor line + hide loot key; presentation/CLI slice.

**Parallel units (6):**
- **[spine, orchestrator]** Contracts + save-structure amendment — `Contracts/` (Enums, Player) + ProfessionId on Recipe. Merges first.
- Crafting generalization — `Crafting/` (RecipeTable, ItemForge, QualityRoller, CraftingHandlers); re-express Blacksmith as data.
- Talent-tree generalization — TalentTree abstraction + Blacksmith/Tanning trees as data.
- Tanning content — new `Professions/Tanning/`.
- Profession-selection system — selection action/handler + PlayerState wiring.
- Presentation & CLI — `godot/` + `Cli/` selection, per-profession screens, Tanning shelf.

---

## Phase 2 — Loadout/Provisioning spine + consumable professions

**Goal:** Introduce the shared consumable/loadout mechanic (the single spine that unblocks 5 of 9 professions) and deliver the **logistics/supply** agency pillar v1 lacks. Ship Potion Master, Food Raising, Engineering on top of it.

**Why second:** Highest-fan-out shared spine; delivers a whole missing vision pillar. Potions are the strongest lever on run depth; recurring consumable demand keeps the economy alive where durable gear saturates it.

**Deliverables:** `ItemSlot` gains Consumable + Trinket; per-expedition Loadout input (consumables + trinket attached in the Morning); deterministic loadout resolution in the resolver (heal auto-quaffed below flee threshold, bomb auto-thrown over an HP threshold — integer-only, attributable by the existing counterfactual method); new beats (PotionLifesave, ToolAssist, Provisioned); the three professions; GearSet/GearScore extended for Trinket.

**Parallel units (6):**
- **[spine, orchestrator-adjacent]** Loadout contract + resolver hook + Trinket slot — `Contracts/` + `Expedition/` (Resolver, CombatMath, AttributionEngine) + new `Loadout/`. **Riskiest contract in the whole roadmap — get it right before the professions build on it.**
- Potion Master — `Professions/Alchemy/` (herb/essence keys).
- Food Raising — `Professions/Food/` (crop day-tick IPhaseSystem).
- Engineering — `Professions/Engineering/` (scrap key, Trinket outputs).
- Presentation & CLI — provisioning, consumable shelves, feed-service, trinket equip.
- **[orchestrator]** Balance & attribution re-baseline.

---

## Phase 3 — Hero class breadth + caster-supply professions + companions

**Goal:** Complete the profession set (all 9 present) and expand the hero cast beyond 3 combat roles into the WoW-like class model, so professions have differentiated customers and the cross-profession supply web (husbandry → tanning/food/necro; enchanter → smith's gear) becomes real. Adds Necromancer's Assistant, Magician's Assistant, Animal Raising, Enchanter.

**Why third:** Depends on both prior spines and is the heaviest rebalancing risk (new caster classes + companions reshape survivability and economy at once) — land it only once crafting and loadout are stable. Necromancer and Magician are the only professions that *require* new hero classes.

**Deliverables:** `HeroRole` → data-driven class model; optional enchant/augment layer on Item; companion party-entity in expedition inputs; new beats (EnchantDecisive, CompanionSave, SummonDecisive, SpellDecisive); Necromancer + Magician classes (reagent-gated casters) with generalized class-fit shopping AI; the four professions; the flagship cross-profession interaction (smith AND enchanter both earn beats on one item — generalizes the multi-crafter overlap rule).

**Parallel units (6):**
- **[spine, orchestrator-adjacent]** Class-model + companion + augment-layer contracts + shopping-AI generalization — `Contracts/` + `Heroes/`.
- Necromancer class + Assistant — `Professions/Necromancy/` (bone key, minion combatant via contract).
- Magician class + Assistant — `Professions/Arcana/` (essence key, charged foci).
- Animal Raising + companion entity — `Professions/Husbandry/` + companion combat hook; wires byproduct keys to Tanning/Food/Necro.
- Enchanter + attribution multi-crafter extension — `Professions/Enchanting/` + CombatMath augment read + AttributionEngine multi-crafter rule.
- Presentation & CLI + balance re-baseline.

---

## Phase 4 — World venues + Intelligence pillar + economy depth

**Goal:** Break the single Mine into multiple venues; deliver the **intelligence** agency pillar (scouting/recon that shapes hero decisions, legibly, no runtime LLM); deepen the economy from one static rival + ore-only market into dynamic multi-material, multi-vendor with commissions. Completes all three quartermaster pillars (supply, logistics, intelligence).

**Why fourth:** Venues, scouting, and a dynamic multi-material economy are only meaningful once the full profession/class roster populates the markets and routes to venues — earlier would be balancing against an empty world.

**Deliverables:** venue registry (per-venue monster/loot tables) + hero venue-selection AI + optional travel-provisioning; scouting-report system feeding hero decisions as a legible, deterministically-scored input; dynamic pricing / supply-demand + multiple vendors + multi-material markets; commissions/pre-orders (heroes request, quartermaster fulfills — generalizes the bounty board).

**Parallel units (5):** venue registry + selection AI (`Venues/` + `Expedition/`); intelligence/scouting (`Intelligence/`); dynamic multi-material economy (`Economy/`); commissions (`Bounties/` extension); presentation & CLI + balance re-baseline.

---

## Phase 5 — Living-world drama deepening

**Goal:** Grow the event-sourced Drama layer from attribution-fed gossip into a living cast — personalities, goals, rivalries, friendships/mentorships, factions, authored arcs.

**Why last (but can overlap Phase 4):** Lowest-dependency, highest-delight polish; needs no new mechanical spine (the event-sourced base exists), pays off most with a large varied cast (post-Phase-3) across varied venues (post-Phase-4). Safest place to end a solo-dev arc — almost purely additive to `Drama/` and trait data, minimal determinism/balance blast radius.

**Deliverables:** personality/goal traits that bias existing AI scoring (data-driven weights, not a new brain); relationship system on the event-sourced base; faction/guild dynamics; authored arc state machines + richer gossip.

**Parallel units (5):** personality/goal traits (`Heroes/` scoring hooks); relationships/rivalries (`Drama/Relationships/` + Events taxonomy); factions/guilds; authored arcs + gossip expansion (`Drama/`); presentation & CLI.

---

## How to divide across Claudes

Mirror v1: each phase has a **sequential spine** and a **parallel body**.

- **Sequential (orchestrator-authored micro-PRs, merged first, in-flight agents rebase):** everything in `Contracts/`, the phase's shared mechanical hook, and every `GameComposition.cs` registration (registration *order* is the determinism contract — an agent inserting a system at the wrong position silently changes every seed's world). Golden-replay re-recording is a deliberate orchestrator act each time a new system changes RNG-stream consumption — keep a "determinism holds under identical seed+actions" assertion distinct from the golden fixture.
- **Parallel body:** one agent owns one `Professions/<name>/` (or `Loadout/`, `Venues/`, `Intelligence/`) directory exclusively + its mirror test dir, claimed in `.claude/tasks/` first. Professions are naturally isolated because `Materials` is generic string-keyed and each profession is data — the only shared touchpoint is the phase spine, which is why the spine goes first.
- The CLAUDE.md deny-list holds throughout: `Game.sln`, `Contracts/`, `GameComposition.cs`, `CLAUDE.md`, `Directory.Build.props`, `.godot-version`, `.github/`, `godot/project.godot` — never edited by module agents.
- Every phase carries one presentation/CLI unit (`godot/` + `Cli/`, adapter-only per sim purity) and one orchestrator balance/golden re-baseline step.

**Highest-leverage next: Phase 1.** It's a hard prerequisite for everything else and its own smallest coherent slice. Recommend running it through `ce-plan` to produce the implementation-ready unit breakdown, then fanning out the 6 units.

## Risks

- **Scope realism:** 8 professions + 2 classes + venues + dynamic economy + drama is a multi-year hobby arc. The sim parallelizes; the serial bottleneck is integration, balance, and presentation. Ship each phase fully green before the next.
- **Golden re-baselining discipline:** nearly every new system changes RNG consumption and intentionally breaks the golden replay — must be a deliberate reviewed step, or a real determinism regression hides in a sloppy re-record.
- **Phase 2 loadout/resolver hook is the single riskiest contract** — touches Resolver, CombatMath, AttributionEngine, and the beat taxonomy at once; must stay integer-only. Get it right before 5 professions build on it.
- **Balance blowup (Phase 3):** reagent-gated casters + companions reshape survivability and economy; budget for real 100-day balance re-tuning.
- **Economy inflation:** consumables + non-loot producers (Food/Animal) + dynamic pricing can spiral; the pricing system needs its own stability tests, not just determinism tests.
- **Attribution complexity:** once smith + enchanter + consumable + companion can all contribute to one survival, the counterfactual overlap rule gets combinatorially harder — extend it carefully and keep attribution tests exhaustive.
- **Engine/infra debt:** the deferred Godot 4.7.1 upgrade is a build-breaking hazard; keep it an isolated infra task, not bundled into a content phase.

## Research provenance

Roadmap synthesized from a research pass (vision-gap inventory, 8-profession mechanic specs, comparable-game patterns) plus a direct read of the repo. Two research inputs (dedicated code-readiness and comparables) returned placeholder stubs and the MMO-design agent failed, so the code-readiness claims here were re-derived from a direct read of the v1 source (confirmed: the hardcoded/generic surfaces listed above). Before executing Phase 4-5, a real comparables/economy research pass could sharpen the dynamic-economy and drama design.
