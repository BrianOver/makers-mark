---
title: "feat: game completion master plan — pillar close-out, mechanism waves, addon fan-out"
date: 2026-07-18
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
origin: Example.txt (11-pillar catalog) transposed per docs/design/catalog-prompt-transposition.md; per-pillar delta audit + content census + mechanism roadmap (2026-07-17), cross-examined and corrected against origin/main @ 1e9dd41
execution: orchestrator (contracts micro-PRs, registration lines, merges, goldens) + core workers + VISUALS/ENGINE lanes + addon swarm, per docs/design/lane-operating-model.md §13 v2.1
related: docs/plans/2026-07-17-002-feat-staged-resolution-plan.md (U2–U5), docs/plans/2026-07-17-003-feat-town-2p5d-migration-plan.md (O1/V0–V5b), docs/plans/2026-07-18-002-grind-block-schedule.md (weekend execution), docs/plans/2026-07-13-001-feat-inverted-mmo-game-plan.md (plan of record), docs/addon-guide.md, docs/telemetry-loop.md
---

## Goal Capsule

Close all 11 pillars of the catalog (`Example.txt`) on the locked architecture: deterministic integer C# sim (`sim/GameSim/`, zero Godot refs, one kernel RNG stream, no wall-clock/transcendentals), Godot 4.6.3 .NET adapter (render + submit only), template FlavorEngine (runtime LLM DEFERRED by ruling — packs only), reflection/registry content model, local $0 art pipeline (2.5D CanvasTexture + PointLight2D, approved). Two horizons: **v1-playable** (this weekend, `docs/plans/2026-07-18-002`) and **v1.0 complete game** (the MIN set below). Every catalog formula transposes per `docs/design/catalog-prompt-transposition.md`: GOAP → integer utility with legible verdicts (`ShoppingAi` pattern); Ollama/System-2 → FlavorEngine packs (PARKED for sim decisions, permanent); floats/log10/e^-λt → `Kernel/IntegerCurves.cs` (MulDiv/DecayPerTick/Log10PerMille/LutEval); ∫dt → IPhaseSystem day-phase steps; singletons → GameState fields + static registries + one orchestrator-applied registration line; minigames → Godot presentation feeding graded action inputs; every new gold flow emits a recorded delta event so `GameSim.Tests/Economy/GoldConservationTests.cs` reconciles.

## Ground truth (verified 2026-07-18, origin/main @ `1e9dd41`)

- **PR #43 (U2, 5-phase kernel) is OPEN, not merged.** `Kernel/GameKernel.cs` `Advance` is 3-phase and throws on Camp/ExpeditionDeep. U1 contracts ARE merged (#34): `Camp=3`/`ExpeditionDeep=4`, `SendSupplyAction`/`RecallPartyAction`, `PartyCampReport`/`SupplyDelivered`/`PartyRecalled`, `GameState.InFlight`.
- **V5a is MERGED (#38, gate G2 flipped)** — `TownScene.OnPhaseCompleted` has the unknown-phase no-op default arm; `AdvanceDay` is loop-until-Morning across 20 call sites. V5a appears in NO wave below. U2's only remaining gate is merging #43.
- **O1 (LFS) is MERGED (#35, gate G7 flipped).** V2/V3-gen PNG commits are unblocked.
- **`Hero.Level` already feeds combat**: `Expedition/CombatMath.cs` `HeroAttack = RoleBaseAttack + hero.Level * 2 + weapon` and `HeroDefense = hero.Level + shields/armor` → `EffectivePower` → gates/flee/draws. Level is never *written* (grep-confirmed), but any unit that increments it is a band+replay re-baseline. M2a below is therefore **XP-bookkeeping only**; the level flip rides M8.
- **G8 is a three-part gate**, not one choke point: (a) `Drama/OrePricing.cs:18` throws on any key outside the Mine's 5; (b) `Crafting/CraftingHandlers.cs:52` rejects keys outside `RecipeTable.MaterialGrades` (`RecipeTable.cs:39-47`); (c) `sim/GameSim.Tests/Factions/FactionConformanceTests.cs` asserts every registered faction's ore key ∈ KnownOreKeys with positive `OrePricing.UnitPrice` — **Crownsguard registration is test-red today**, which is the proof of the shipped-inert framing and the acceptance pin for M1.
- **Working-tree note:** the `docs/graphics-2.5d` checkout (HEAD `daa1283` = the #38 merge) is missing #39–#42 and the post-#38 BOARD lines. Read all coordination docs (`lane-operating-model.md` §13, `BOARD.md`) from `origin/main`, per §13 seam rule 3.
- **`godot/scripts/IconRegistry.cs` has `Sprite`/`Building` (non-null-tolerant `GD.Load`) and null-returning `Art()` only. `IconRegistry.Lit` does not exist — it is V4a's deliverable.** Any unit claiming graceful SVG-fallback degradation (V5b-lite) depends on V4a merged first.
- Content census (exact): 3 classes (RecruitPool frozen, `Classes/ClassRegistry.cs:66-78`), 2 professions, 23 recipes, 16 talent nodes, 2 Heal consumables (`ConsumableKind`={Heal}), 0 trinket content (slot fully wired — data-only NOW), 1 venue live (`VenueRegistry.LiveRotation` frozen), 5 monsters (1/floor, single-string `MonsterKind`), 5 priced ores + 2 inert consts, 1 registered faction + Crownsguard inert, 3 flavor packs / 204 lines / 4 frozen voices, 6 BeatTypes (ToolAssist reserved), 20 GameEvent records (11 voiced), 1 bounty shape, 7 art specs / 1 shipped pair.

## Rulings on record (critique resolutions — cite this section, do not relitigate)

| R | Ruling |
|---|---|
| R1 | **M2a grants XP only; `Hero.Level` stays 1.** Level flip (XP→Level curve applied) lands inside M8's combined re-baseline, because CombatMath reads Level today. |
| R2 | **Classes ship registered-inert this weekend (T1a). RecruitPool expansion (T1b) is CUT from the weekend and lands inside M8** (one combined re-baseline: level flip + healer archetype + RecruitPool + abilities), per addon-guide rule 4. The census "6 recruitable" target moves to the M8 milestone. |
| R3 | **Re-baseline chain (corrected): U3 → M7a → M6 → M8 → M11a → M12 → M10.** M7a ships with distance=1 stub and precedes M6, which replaces the stub. Only ONE band/golden re-baseline in flight at a time; everything else is draw-neutral or dark-launched with neutral constants. |
| R4 | **T7a = M1's lookup-only scope, draw-neutral, NOT a band-mover.** 5 Mine keys byte-identical price/grade; unknown-key still throws; goldens untouched. Any tariff/price *data* change is a separate, explicitly-declared PR. |
| R5 | **ID namespaces:** roadmap unit IDs are M1–M15/U/V/T (this doc is canonical). The weekend definition-of-done uses D1–D6 (never M-prefixed). The weekend tag is **`v1-playable`**, not `v1.0` — v1.0 requires the full MIN set below. |
| R6 | **2.5D path is a Brian verdict (CP-1), presented against plan 003's decided rule** ("do not relitigate: stay on 4.6.3 until gdUnit4Net ships stable; V4b lands only on 4.7.1"; sanctioned filler = additive pilot-style scenes, no `town_scene.tscn` edit, throwaway; do NOT start V4b on 4.6.3 — the 4.7.1 re-import churns every touched scene). Options (a) and (b) below are *ruling changes*; only (c) is plan-compliant. |
| R7 | **M15 ships dark:** no Harness policy submits supply-contract actions, so bands provably don't move (class-addon precedent). Flip = later policy/data PR declared as a band touch. |
| R8 | **T8a is not "free":** FlavorEngine variant pick is hash-mod-count, so appending variants moves every existing render for that key. The packet contract includes re-pinning the prose goldens (`TavernPackTests` pinned prose, `LedgerPackTests.cs:128`) in-packet. |
| R9 | **V5b-lite's graceful degradation requires V4a merged** (`IconRegistry.Lit` null-tolerant chain does not exist until then). |

## Pillar delta table (corrected)

| P | Shipped today (files) | Missing vs catalog | Closing units | Gate |
|---|---|---|---|---|
| P1 Classes & Sociology | Data-driven classes (`Classes/ClassDefinition.cs`, `ClassRegistry.cs` — 3 built-ins, RecruitPool frozen); `Heroes/PartyFormation.cs`, `HeroRoster.cs`; utility-AI pattern (`Heroes/ShoppingAi.cs`) | Trauma/trust-fracture, affinity tables, SelfPreservation/Altruism/Pride, P_party, β_class, 4th archetype + healer role, leveling (Level never written; **but Level IS read by CombatMath — increments are band-movers, R1**) | M9, M2a, M8; T1a data | RecruitPool expansion = G12 (M8) |
| P2 NPCs & Factions | Standing→tariff core (`Factions/`, `FactionDriftSystem.cs`, hysteresis, per-mille MulDiv tariffs); Crownsguard merged INERT; `Flavor/Packs/FactionPack.cs`; rival merchant; `IntegerCurves.Log10PerMille` | Material registry (**G8, three parts — see Ground truth**), tax engine T_town, TownChaosValue, inspections/fines, perks/blockades, faction-gated vendors, Syndicate + Conservatory, forbidden materials | M1, M13; T7b data ×2 + Crownsguard line | G8 (M1) |
| P3 Quests & Bounties | Bounty spine (escrow, threshold judge + legible reasons, payout/refund/expiry); influence-not-orders floor (`Expedition/ExpeditionSystem.cs:36-41`) | D_q utility (greed/reputation/distance), crowdfunding (town-NPC gold unmodeled), party competition/disgruntlement, cleared-node arrival, quest-type registry, hero rep-with-shop | M7a, M7b, M5; T10 contract | reputation ← M9; distance ← M6; crowdfunding ← G14 (M5) |
| P4 Workstation Missions | Craft pipeline (`Crafting/CraftingHandlers.cs`, `QualityRoller.cs` 1-roll, `ItemForge.cs`); `godot/scripts/panels/ForgePanel.cs` (buttons only) | `CraftAction` performance-grade seam, supply contracts (time-gated orders + grading + rep payout + penalty), workstation stability/hazards, thermodynamic forge minigame | M3 (G9), M15, V6 | G9 (M3) |
| P5 Map Nodes & Graphs | Multi-venue data model (`Venues/VenueDefinition.cs`, `VenueRegistry.cs` LiveRotation frozen); VenueId threaded through Expedition | Node graph (edges/distances/hazard), F_travel fatigue, routing (`ExpeditionSystem.cs:25` hardcodes Mine), closures, live rotation + re-baseline | M6 (G13), M4 (G10); T3a data ×2, T3b live | G13 (M6); no AStar — day-granular hops |
| P6 Chronicle & Lore | Event-sourced spine (append-only stamped EventLog, `Drama/DayLog.cs`); gossip citing real events; FlavorEngine + StableHash + 3 packs; ChronicleCodec; batch farm + Analytics; `IntegerCurves.DecayPerTick` | I_lore prominence (gossip = first-3 in log order), retention window, multi-day rumor pool, item-provenance gossip (data exists, unused), U5 camp narrator | U5, M14; T8a/T8b data | no SQLite per ruling; Ollama PARKED |
| P7 Vanity & Ego | Trinket slot fully wired (zero content); ColorRgb hint precedent; 2.5D light pipeline (`LitTavernPilot.cs`) | EgoValue, Θ_threat targeting hook (`ExpeditionSystem.cs:32` reads DeepestFloorReached+1 only), bragging state, vanity shopping arm (ShoppingAi refuses zero-gain), cosmetics, glows | M10, V8; T6 data (stat trinkets NOW) | after M9; Θ_threat clamped to venue.FloorCount |
| P8 Spells & Abilities | Player talents (`Crafting/TalentTree.cs`); deterministic in-combat trigger template = TryQuaff (`ExpeditionResolver.cs:207-218`, data-keyed, no RNG); RecordedRolls; reserved ToolAssist beat | AbilityDefinition registry, ability slots, U_ability trigger at resolver, scroll transcription, 8 launch abilities, hero purchases, leveling spend | M2a (G11), M8 (G12); abilities data ×8 | G11 → G12 |
| P9 Enemies & Dens | Static per-floor monster stats; `MonsterTable.cs` = shim over VenueRegistry.Mine | Per-venue mutable state (none on GameState), den escalation/InfectionRate, S_mob days-untouched scaling, elite mutations (kernel-RNG, recorded), world-boss lockdown, town raids | M4 (G10), M11a, M11b; T4 contract; mutation-trait data | G10 (M4); lockdown ← G13 |
| P10 Threats & Disasters | Substrate: item provenance (`Contracts/Items.cs` History+MakersMark), standing+thresholds, seeded RNG, recorded-sink precedent | Disaster scheduler, suspicion/V_bandit, mitigation spends (new actions + sinks), provenance audit → fine, extortion choices, templates, panels | M12; disaster-template data; panel GODOT S | audit materials ← G8; sinks extend GoldConservationTests |
| P11 Master Professions | LARGELY SHIPPED: registries + tier gates + talent trees; blacksmith 16 + tanning 7 recipes; consumable spine end-to-end; U_buy ≈ ShoppingAi (NeedWeight/Affordability/ItemQuality live) | TrustFactor (no hero↔shop state), legendary behavior-altering items, tier-4 (data — TierGate generic), non-ore materials (G8), minigames (G9), more professions | T2 data ×2 NOW; T5a/T6 NOW; legendary hook inside M10; V6 | TrustFactor ← M9; herbs ← G8 |

## Gates table (extends BOARD G1–G7; statuses as of 2026-07-18)

| Gate | Definition | Blocks | Owner | Status |
|---|---|---|---|---|
| G1 | U1 contracts micro-PR merged | U2 | orchestrator | **MERGED (#34)** |
| G2 | V5a 5-phase tolerance merged | U2 | VISUALS | **MERGED (#38)** |
| G3 | U2 kernel (5-phase) merged | U3 | AI-NPC | **PR #43 OPEN — first orchestrator action** |
| G4 | U3 staging + band re-fit + registration line merged | U4; V5b choreography | AI-NPC + orchestrator | waiting on G3 |
| G5 | U4 camp verbs merged | U5; telemetry-U4 | AI-NPC | waiting on G4 |
| G6 | gdUnit4Net stable 4.7 → V0 infra PR merged | V4b, V5b proper | ENGINE (external watch) | watching upstream |
| G7 | O1 LFS infra merged | V2, V3-gen PNG commits | ENGINE | **MERGED (#35)** |
| **G8** | **M1 material registry merged** — retires all three choke points: `OrePricing.cs:18` throw, `RecipeTable.MaterialGrades`/`CraftingHandlers.cs:52`, and the `FactionConformanceTests` KnownOreKeys+positive-price pin (which flips from repo-frozen list to registry lookup) | Crownsguard registration line; Syndicate/Conservatory factions (T7b); herb/leather professions; new-ore venues | core worker + orchestrator merge | open |
| **G9** | M3 performance-grade seam merged (`CraftAction` trailing `int? PerformanceGrade = null`) | V6 forge minigame; M15 grading; all P11 minigames | orchestrator micro-PR | open |
| **G10** | M4 per-venue mutable state merged (`GameState.Venues : ImmutableSortedDictionary<string, VenueState>`) | M11a/M11b; M6 closures | orchestrator micro-PR | open |
| **G11** | M2a XP bookkeeping merged (XP only — R1) | M8 spend side | orchestrator micro + core worker | open |
| **G12** | M8 merged (level flip + healer archetype + RecruitPool expansion + AbilityRegistry + U_ability — ONE combined re-baseline) | ability data fan-out ×8; talent-planner data; "6 recruitable" census target | core worker + orchestrator goldens | open |
| **G13** | M6 venue graph + routing + LiveRotation expansion merged | T3b live venues; M11b lockdown | core worker + orchestrator goldens | open |
| **G14** | M5 town-NPC gold purses merged (extends conservation invariant) | M7b crowdfunding | core worker | open |

## Unit catalog

Worker tiers: **ORCH** = orchestrator micro-PR (deny-listed files); **CORE** = core worker in per-claim worktree; **DATA** = addon packet, fully automatic, registration line orchestrator-applied; **VIS/ENG** = lane per charter. Fast lane = `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance`. Balance = same with `--filter Category=Balance`. Engine = `dotnet test godot/tests --settings .runsettings`. Art = `dotnet test art/GameArt.Tests`.

### Wave 0 — unblockers (start all now; all draw-neutral)

| ID | Goal | Files | Deps | Size | Test contract | Tier |
|---|---|---|---|---|---|---|
| A0 | Merge PR #43 (U2 5-phase kernel) — flip G3 | branch `feat/u2-five-phase-kernel` exists, BOARD "push when green" | G1+G2 (both flipped) | — | plan 002 U2 contract: bands UNTOUCHED (empty-tick proof) | ORCH |
| M1 | Material registry — kill G8 (all three parts). New `sim/GameSim/Materials/{MaterialDefinition,MaterialRegistry}.cs` (id, unitPrice, grade, tags: forbidden/herb/leather, sourceVenue); `OrePricing.UnitPrice` and `RecipeTable.MaterialGrades` become registry lookups; frozen `PricedPool` mirrors `VenueRegistry.LiveRotation` | `sim/GameSim/Materials/` (new), `Drama/OrePricing.cs`, `Crafting/RecipeTable.cs`, `sim/GameSim.Tests/Materials/` | none | M→L | fast lane + Balance untouched; conformance pin: 5 Mine keys byte-identical price/grade; unknown-key still throws; **acceptance proof = `FactionConformanceTests` re-pointed at registry and Crownsguard-with-registry goes green in a follow-up registration PR, red until then**; goldens untouched (lookup relocation, zero draws — R4) | CORE |
| M3 | Performance-grade seam: trailing `int? PerformanceGrade = null` on `CraftAction` (`Contracts/Actions.cs`); `QualityRoller.Roll` gains clamped per-mille grade modifier, null = byte-identical | `Contracts/Actions.cs`, `Crafting/QualityRoller.cs` | none | S | save round-trip pin (old action JSON → null); null-parity QualityRoller test; goldens safe — grade rides ActionLog | ORCH |
| M4 | Per-venue mutable state: non-positional-init `ImmutableSortedDictionary<string, VenueState>` on `GameState`; `VenueState(DaysUntouched, InfectionPerMille, Closed)` record | `Contracts/World.cs`, new Contracts type | none | S | round-trip: absent property → empty (InFlight precedent); zero behavior | ORCH |

### Wave 1 — staged resolution + art queue (existing plans slotted verbatim; weekend scope)

| ID | Goal | Deps | Size | Test contract | Tier |
|---|---|---|---|---|---|
| U3 | Staging park/finalize + **band re-fit #1** (the weekend's ONLY band-mover) | A0 (G3) | M | plan 002 §U3: seed-sweep diff, tripwires (grin collapse / insolvency / >30% shift → park, re-examine CampCheckpointDepth) | CORE |
| U4 | Camp verbs — `Expedition/CampHandlers.cs` (new); fee = recorded sink extending GoldConservationTests | U3 (G4) | M | plan 002 §U4 incl. camp-delivery PotionLifesave marquee test | CORE |
| U5 | Narrator — `sim/GameSim/Narrative/` reading `ExpeditionHalt` | U4 (G5) | M | plan 002 §U5; stable-hash variant picks | CORE |
| telemetry-U4 | Decision-trace emitters (edits `Expedition/`) | staged-U4 merged (directory ownership) | S-M | fast lane; post-weekend slot, before M7a | CORE |
| V1 | Pipeline scripts (ComfyUI+SDXL → BiRefNet cutout → sobel normals) | none (G7 done) | S | art lane; scripts only, no PNG commits | VIS |
| V4a | `IconRegistry.Lit` (CanvasTexture lit+normal loader, null-tolerant fallback chain) | none | S | engine lane; **prerequisite for V5b-lite degradation (R9)** | VIS |
| V2 | Building assets commit (cutout→normals→moving-light QA→build-half JSONs incl. tavern backfill) | V1, G7, curation CP-2 | M | art lane + engine lane | VIS |
| V3-gen | Hero figure trio commit (specs merged #40) | V1, G7, curation CP-3 | M | art lane + engine lane | VIS |
| V5b-lite | Camp/Deep tint rows + Deep-complete survivor walk-back on the CURRENT Control town — script-level (`TownScene.cs` TintFor + choreography), 4.6.3-safe, **no `.tscn` surgery** (plan 003 filler rule intact) | U3 (choreography), V4a (R9) | S-M | engine lane; staged-party non-stranding test per plan 003 §V5b step 2 | VIS |
| V7a | Camp panel: render `PartyCampReport`, submit SendSupply/Recall with rejection reasons | U4, spec written B1 | M | engine lane; ScriptedSession camp-day walk | VIS |
| V7b | Narrator drip in Godot (Evening/ledger panel surfaces `Narrative/` output) + `AttributionBeat` pride surfacing | U5, V7a | S-M | engine lane | VIS |
| V0→V4b→V5b | 4.7.1 infra → Control→Node2D town → 5-phase ambience proper | G6 (external) + G4 | S/L/M | plan 003 verbatim; slot whenever upstream ships | ENG/VIS |

### Wave 2 — content-multiplication mechanisms (post-U5; draw-neutral except M7a)

| ID | Goal | Files | Deps | Size | Test contract | Tier |
|---|---|---|---|---|---|---|
| M2a | **XP bookkeeping only (R1):** `Hero.Xp` non-positional init; Evening reveal grants XP from revealed floors via `IntegerCurves.LutEval`; `XpAwarded` recorded event. **`Hero.Level` is NOT written — CombatMath reads it** | `Contracts/Heroes.cs`+`Events.cs` (ORCH micro), `Drama/ExpeditionRevealSystem.cs` | U-chain done | S | fast lane; run-twice determinism; bands byte-identical (nothing reads Xp); event stays out of gossip told-kinds until M14 | ORCH+CORE |
| M15 | Supply contracts: time-gated faction orders, grading (Quality × M3 grade), rep payout via `PlayerState.WithStanding`, default penalty. **Ships dark (R7): no Harness policy uses it → bands provably unmoved** | new `sim/GameSim/Orders/`, contracts micro for state+actions | M3 (G9) | M | conservation: every payout/penalty = recorded delta event; fast lane; dark-launch parity proof | ORCH micro + CORE |
| V6 | Thermodynamic forge minigame feeding `PerformanceGrade` into submitted `CraftAction`; sim untouched | `godot/scripts/panels/ForgePanel.cs` + new scene | M3 (G9), CP-7 intensity verdict | M | engine lane; grade-wiring test; sim lanes must not change | VIS |
| M14 | Chronicle I_lore: replace first-3-in-log-order (`Drama/GossipGenerator.cs:39,64-70`) with severity × recency-decay (`DecayPerTick`) × standing weight; multi-day rumor pool; retention window on reads; item-provenance pack lines off `Item.History`/`MakersMark` | `Drama/GossipGenerator.cs`, `Flavor/Packs/` | U5 | S-M | gossip draws no RNG (KTD2) — goldens safe; re-pin chronicle-text tests | CORE |
| M7a | Bounty D_q: threshold judge → integer utility (greed MulDiv, reputation-with-shop from `ItemMemory`, **distance=1 stub until M6**); party competition/disgruntlement events; cleared-node arrival. **Re-baseline #2 — precedes M6 (R3)** | `Bounties/BountyRules.cs`+`BountySystems.cs`, `Expedition/ExpeditionSystem.cs:36-41` | staged-U4 + telemetry-U4 merged | M | fast lane; golden re-record + band re-fit, deliberate, chain slot #2 | CORE + ORCH goldens |
| M9 | Sociology core: `Personality` record (SelfPreservation/Altruism/Pride per-mille, **neutral defaults = zero behavior change**), trust/affinity map, P_party hook in `Heroes/PartyFormation.cs`, trust-fracture at reveal, β_class slot. RecruitPool expansion explicitly NOT here (R2) | contracts micro + `Heroes/` | U3 | M-L | fast lane; neutral-constants parity (formation byte-identical); flip = later data PR riding a chain slot | ORCH micro + CORE |

### Wave 3 — depth mechanisms (chain slots #3–#7, strictly serial — R3 order)

| ID | Goal | Files | Deps | Size | Chain slot | Tier |
|---|---|---|---|---|---|---|
| M6 | Venue graph + routing + fatigue: `Venues/VenueGraph.cs` (edges/distances/hazard, day-granular hops, no AStar); replace hardcoded Mine (`ExpeditionSystem.cs:25`) with per-party venue utility; F_travel MulDiv into `CombatMath`; expand `LiveRotation`; closures read M4 state; venue-2 data; replaces M7a's distance stub | `Venues/`, `Expedition/` | M1, M4, M7a, telemetry-U4 | L | **#3** — biggest; goldens + bands; 20-seed batch diff mandatory | CORE + ORCH |
| M8 | Abilities + leveling: static `AbilityRegistry`; ability slots on `ClassDefinition`/Hero; U_ability integer-threshold trigger generalizing TryQuaff seam (`ExpeditionResolver.cs:207-218`, recorded like ConsumableUse); scroll item kind (enum append); hero-side purchases in ShoppingAi; **level flip (Xp→Level, CombatMath now moves — R1) + healer archetype + RecruitPool expansion (T1b — R2) land HERE as one combined re-baseline** | `Expedition/ExpeditionResolver.cs`, `Heroes/`, `Classes/`, contracts micro | M2a, M9, M6 | L | **#4** | CORE + ORCH |
| M11a | Den escalation: Morning system ticks `VenueState` (DaysUntouched/Infection); S_mob = MulDiv on monster stats at resolve entry; elite mutations = kernel-RNG modifiers recorded onto `FloorOutcome` (contracts append) | new system + `Expedition/` read path | M4, M8 | M | **#5** | CORE + ORCH micro |
| M12 | Disaster scheduler + suspicion state + mitigation actions (recorded sinks) + provenance-audit (`Item.History`/Mark scan → fine) + 1 reference disaster as the addon pattern | new `Drama/Threats/`, contracts micro | M1, M11a | M | **#6**; sinks extend GoldConservationTests | CORE + ORCH micro |
| M10 | Ego/vanity: EgoValue; Θ_threat per-mille bump at targeting (`ExpeditionSystem.cs:32`, clamped to `venue.FloorCount`); bragging tavern beats; vanity arm in ShoppingAi (buys zero-stat trinkets); legendary `BehaviorModifier` item hook (P11 pairing) | `Heroes/`, `Expedition/ExpeditionSystem.cs`, contracts micro | M9, M12 | M | **#7** (NICE tier) | CORE + ORCH micro |

### Wave 4 — nice-to-have breadth

| ID | Goal | Deps | Size | Tier |
|---|---|---|---|---|
| M11b | Lockdown/world-boss/town-raid (adjacency spread; raid template shared with M12) | M6, M11a | M | CORE |
| M13 | Tax engine T_town (`Log10PerMille`), TownChaosValue, inspections/fines, perks/blockades, faction-gated vendor pools | M1 | M | CORE |
| M5 | Town-NPC gold purses extending the conservation invariant (G14) | none | S-M | CORE |
| M7b | Quest-type registry (escort/cull/retrieval) + crowdfunding | M5, M7a | S-M + data-S/type | CORE + DATA |
| V8 | Vanity glows/particles (trinket PointLight2D on HeroSprite) | V4b, M10, V3-gen | S-M | VIS |
| T4 | Per-floor monster table contract (`VenueFloor.MonsterKind` single string → table + RNG draw) | ORCH micro + kernel; golden re-record | M | ORCH+CORE |
| T10 | Bounty-type contract (`Bounty` fixed shape → typed) + `BountyRules.Judge` extension | ORCH micro; golden implications | M | ORCH+CORE |

### Addon packet catalog (DATA tier — fully automatic; registration line in PR description, orchestrator applies)

| Packet | Dirs | Conformance command | Size | Gate |
|---|---|---|---|---|
| T2-engineering, T2-alchemy | `sim/GameSim/Professions/<Name>/` + `sim/GameSim.Tests/Professions/<Name>/` | fast lane (ProfessionConformanceTests auto-sweeps; ToolAssist beat reserved for Engineering) | S each | none — Tanning precedent (#41) |
| T6-trinkets | recipes in owning profession dirs + tests (charms/talismans, stat-only Attack/Defense/Weight) | fast lane; TrinketGearSetTests pins slot plumbing | S | none — verified data-only NOW |
| T5a-consumables | Heal-variant recipes in owning profession dirs | fast lane | S | none; new ConsumableKind (Damage/Buff) = ORCH enum append, STRETCH |
| T8a-flavor-breadth | `sim/GameSim/Flavor/Packs/` + `sim/GameSim.Tests/Flavor/` | fast lane; **includes re-pinning TavernPack/LedgerPack prose goldens (R8)** | S | none |
| T8b-flavor-new-surfaces | `Flavor/Packs/` + `Drama/GossipGenerator.cs` Describe case per event family (ItemSold/BountyPaid/TariffApplied) | fast lane + golden replay | M | mechanism seam → CORE, not pure addon |
| T9a-art-specs | `art/specs/items/`, `art/specs/monsters/` (new reflection-discovered modules; ~23 recipe + 7 ore + 5 monster icons) | `dotnet test art/GameArt.Tests` (AssetSpecRules) | S | none; generation gated on V1 |
| T1a-classes ×3 | `sim/GameSim/Classes/<Name>/` + `sim/GameSim.Tests/Classes/<Name>/` | fast lane (ClassConformanceTests auto-sweeps); **registered-inert (R2)** | S each | recruitability = G12 |
| T3a-venues ×2 | `sim/GameSim/Venues/<Name>/` + tests; Mine-ore loot keys until G8 | fast lane (registry conformance) | M each | live = G13 (T3b) |
| T7b-factions ×2 + Crownsguard registration line | `sim/GameSim/Factions/<Name>/` + tests | fast lane (FactionConformanceTests auto-sweeps — goes green only post-M1) | S each | **G8 strictly** |
| Post-G12 fan-outs | abilities ×8, tier-4 recipes, mutation traits, disaster templates, cosmetics, camp/narrator pack (post-U5) | fast lane each | S each | respective M-unit |

## MUST / SHOULD / STRETCH

**Horizon 1 — `v1-playable` (this weekend; DoD = D1–D6 in `docs/plans/2026-07-18-002`):**
MUST: A0, U3, U4, U5, V1, V4a, V2, V3-gen, V5b-lite, V7a, V7b, plus D5 balance-green sweep.
SHOULD: M1 (G8), T2 ×2, T1a ×3 (registered-inert), T5a, T6, T8a, T8b, T9a, T3a ×2, T7b ×2 + Crownsguard line, M3, M4.
STRETCH: T4 or T10 (one, not both), new ConsumableKind, extra flavor voices, V0/V4b if upstream ships.

**Horizon 2 — v1.0 complete game (MIN set):** everything in Horizon 1 MUST+SHOULD, plus M2a, M15, V6, M14, M7a (chain #2), M9, M6 (chain #3), M8 (chain #4, absorbs T1b/level-flip/healer), M11a (chain #5), M12 (chain #6), telemetry-U4, V0→V4b→V5b when G6 clears.
NICE: M10 (chain #7), M11b, M13, M5, M7b, V8, T4+T10, all data fan-outs.

## Brian checkpoints (Example.txt idea→confirm→build, batched)

Rule: every question ships a recommended default. If a checkpoint window passes unanswered, the default applies and work proceeds — EXCEPT starred (*) verdicts, which block their dependent unit only. Workers produce PHASE-1/2 proposal material during gated waits so each checkpoint is answer-only, never research-time.

| CP | When | Duration | Batch |
|---|---|---|---|
| CP-1 | Fri B1, before bed | 30 min | *Q1 2.5D path: (a) V4b on 4.6.3 — ruling change vs plan 003 "do not relitigate", cost = 4.7.1 re-import churn on every touched scene; (b) 2.5D-lite editing the live Control town — ruling change vs the "no town_scene.tscn edit" filler rule; (c) additive pilot-style overlay scenes — plan-compliant sanctioned filler, cost = throwaway work. **Default (c).** Q2 confirm weekend DoD D1–D6 + SHOULD amendment "classes registered-inert" (default: yes). Q3 confirm ruling R2 (T1b rides M8) (default: yes). Q4 wave-A creative briefs: Engineering + Alchemy identity — names, theme, signature consumable each (defaults provided in claim stubs). |
| CP-2 | Sat B3 | 45 min | Art curation #1: building contact sheets, pick 1 per building from top-3 pre-culls. CLI playtest of 5-phase day + camp verbs (`dotnet run --project sim/GameSim.Cli`). Q5 wave-C creative briefs: venue #2/#3 themes (default: Gloomwood + Volcano Den per Example.txt P5), trinket set theme (default: charms/talismans on Tanning). |
| CP-3 | Sat B4 | 30 min | Art curation #2: hero figure trio (silhouette consistency). V7a camp-panel layout eyeball. Q6 T7b faction identities (default: Shadow Syndicate + Grand Conservatory transposed per Example.txt P2; forbidden/catalyst material tags ride M1's registry). |
| CP-4 | Sat B5 | 60 min | First full Godot playtest — 5-phase day, camp decision, narrator lines, 2.5D per Q1 path. File defects as BOARD items. *Band-acceptance verdict if U3 re-fit shifted bands. |
| CP-5 | Sun B8 | 90 min | Art curation #3 (icons, fast). D1–D6 checklist walk. *Cut-line verdict: anything unmerged and non-MUST dies. |
| CP-6 | Sun B9–B10 | 45 min | Sign-off playtest; final acceptance (30-minute grin-moment criterion, plan-of-record Success Criteria); tag `v1-playable`. |
| CP-7 | Post-weekend, one sitting | 60–90 min | Wave 2/3 creative batch (idea→confirm before each M-unit builds): V6 minigame intensity (P4 catalog Phase-3 question); M13 faction punitiveness/lockout severity; M12 disaster severity + frequency; M8 the 8 launch abilities — 2 per archetype, priority order; M6 map graph size/layout (default: Example.txt P5 5-node topography); M11a den-escalation aggression; M10/V8 vanity styling + behavioral impact scaling. Defaults let Wave 2 start dark regardless; answers finalize the data flips. |

## Coordination notes

- Contract touches (M3, M4, M2a/M15/M9/M11a/M12/M10 contract parts, T4, T10) are orchestrator micro-PRs per CLAUDE.md deny-list (`sim/GameSim/Contracts/`); merged before dependent module PRs; in-flight agents rebase.
- Registration lines follow plan-002 D6 procedure; `GameComposition.cs` system order is a determinism contract.
- Crownsguard flip after M1 = data/registration PR, not a mechanism.
- One band/golden re-baseline in flight at a time, chain order per R3. "Golden replay" = run-twice byte-identity (`Ae5_HundredDay_ByteIdenticalReplay`), orchestrator-owned re-records.
- Read `.claude/tasks/BOARD.md` and `docs/design/lane-operating-model.md` §13 from origin/main; the docs/graphics-2.5d checkout is stale (missing #39–#42).
- Every new gold flow emits a recorded delta event (TariffApplied/SupplyDelivered precedent) so GoldConservationTests reconciles. Sim purity (KTD2) and determinism (CLAUDE.md rules 4–5) bind every unit above.
