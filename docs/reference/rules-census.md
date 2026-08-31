# Maker's Mark — the rules census

Describes the code as of commit `28fd0452`. Every claim carries a `file:line` into that tree.

## Orientation

This document is an exhaustive census of what the simulation decides and how. The sim is a pure
.NET library (`sim/GameSim/`) with zero engine references: all game rules live here, the Godot
client only renders state and submits actions. One kernel (`sim/GameSim/Kernel/GameKernel.cs`)
advances an immutable `GameState` record one phase at a time; one composition root
(`sim/GameSim/GameComposition.cs`) fixes the system order that is itself the determinism contract;
one PCG32 stream (`sim/GameSim/Kernel/Pcg32.cs`) is the only randomness. Same seed + same actions
= byte-identical world, enforced by golden-replay tests.

Module layout: `Contracts/` (frozen shared types — state, actions, events), `Kernel/` (tick loop,
RNG, save codec, integer curves), `Crafting/` (recipes, quality rolls, forge scoring, modifiers,
signing), `Professions/` (four professions as data + their in-sim puzzle scorers), `Heroes/`
(roster, traits, shopping AI, needs, relationships, commissions, party formation, muster),
`Expedition/` (combat resolver, attribution engine, camp verbs, apprentice warrant), `Venues/`
(four dungeons as data + the rank router), `Economy/` (shelf, vendors, rent, guild dues, rival,
forge tiers), `Counter/` (the face-to-face haggle), `Bounties/`, `Drama/` (reveal, gossip,
memorials, director, read models), `Factions/`, `Arc/`, `Progression/`, `Chronicle/`, `Flavor/` +
`Narrative/` + `Presentation/` (deterministic prose selection and pacing — rules only are covered
here), and `Harness/` (scripted player policies). `sim/GameSim.Cli/` drives the same kernel from a
console and is covered only where it drives the sim.

Read §1 (clock) and §2 (verbs) first; they are the skeleton everything else hangs on. §11 (RNG)
and §12 (contracts) are the reference tables. §16 collects the orphans and surprises.

---

## 1. The clock

### 1.1 Phases and the day cycle

A day is five kernel phases. `DayPhase` (`sim/GameSim/Contracts/Enums.cs:9`) declares
`Morning, Expedition, Evening, Camp, ExpeditionDeep` — the numeric order is save-format only
(append-only, KTD4); the *day* order is defined solely by `GameKernel.Advance`
(`sim/GameSim/Kernel/GameKernel.cs:194-204`):

| From | To | Condition |
|---|---|---|
| Morning | Morning | a stepped counter session is open and not closed (`GameKernel.cs:196`) — the day HOLDS |
| Morning | Evening | `NoRaidToHost` — party formation over the post-systems roster returns zero parties, i.e. not one living hero (`GameKernel.cs:197`, `GameKernel.cs:222-223`) |
| Morning | Expedition | otherwise (`GameKernel.cs:198`) |
| Expedition | Camp | always (`GameKernel.cs:199`) |
| Camp | ExpeditionDeep | always (`GameKernel.cs:200`) |
| ExpeditionDeep | Evening | always (`GameKernel.cs:201`) |
| Evening | Morning, day + 1 | always (`GameKernel.cs:202`) |

### 1.2 One tick

`GameKernel.Tick` (`GameKernel.cs:108-179`) is indivisible and does, in order: (1) apply each
player action through the first handler whose `CanHandle(action, phase)` accepts it, collecting
typed `RejectedAction`s (`GameKernel.cs:114-133`); (2) run every registered `IPhaseSystem` whose
`Phase` matches, in registration order (`GameKernel.cs:135-142`); (3) stamp queued events with
sequential `EventId`s and the current day (`GameKernel.cs:144-150`); (4) advance the phase
machine, persist the RNG snapshot, append the action batch to `ActionLog`
(`GameKernel.cs:152-173`).

Two day-boundary resets live only in `Tick`: the counter session is torn down the instant the day
leaves Morning (`GameKernel.cs:166`), and `ActionSlotsRemaining` resets to
`ActionBudget.SlotsPerDay` only when `Day` actually increments (`GameKernel.cs:172`).

### 1.3 ApplyNow — the immediate lane

`GameKernel.ApplyNow` (`GameKernel.cs:59-106`) applies ONE action with no systems pass and no
phase advance; it persists the RNG snapshot and appends to the `ActionLog` exactly like `Tick`.
`ActionTiming.ResolvesImmediately` (`sim/GameSim/Kernel/ActionTiming.cs:75-134`) is the
deny-list-by-default split deciding which verbs the client may resolve through `ApplyNow`: the
nine workshop verbs (buy material/ore/supply, craft, reforge, masterwork, stock, unstock,
set-price), the five counter verbs, accept/decline commission, post bounty, send supply, recall,
unlock talent, honor memorial, and conclude apprenticeship are immediate; **exactly three verbs
still ride the bell**: `UpgradeForgeAction`, `SetProfessionsAction`,
`CommissionLegendaryWorkAction` (`ActionTiming.cs:126-133`). The Godot client uses `ApplyNow`
(`godot/scripts/SimAdapter.cs`); the CLI drives only `kernel.Tick` with a queued batch
(`sim/GameSim.Cli/Program.cs:1073`).

`GameKernel.Accepts` (`GameKernel.cs:30-31`) exposes the handler predicate so a UI can refuse a
phase-illegal action at input time; the CLI uses it (`Program.cs:1050-1062`).

### 1.4 System registration order (the determinism contract)

`GameComposition.BuildKernel` (`sim/GameSim/GameComposition.cs:59-77`), in order:

| # | System | Phase | RNG | Once-per-Morning guard |
|---|---|---|---|---|
| 1 | `DirectorSystem` | Morning | 1 draw/day | yes |
| 2 | `FactionDriftSystem` | Morning | none | no (idempotent-ish: steps standing each run — see guard note below) |
| 3 | `CounterQueueSystem` | Morning | none | n/a (no-op when `Counter` null) |
| 4 | `RentSystem` | Morning | none | yes |
| 5 | `GuildAssessmentSystem` | Morning | none | yes |
| 6 | `DestitutionRecoverySystem` | Morning | none | no (fires only at a true dead-end) |
| 7 | `RivalRestockSystem` | Morning | none | yes |
| 8 | `RecruitSystem` | Morning | 3 draws per recruit | yes |
| 9 | `GossipSystem` | Morning | none | yes |
| 10 | `HeroShoppingSystem` | Morning | none | skips while counter open |
| 11 | `CommissionSystem` | Morning | none | yes |
| 12 | `MusterSystem` | Morning | none | no guard (re-emits `PartiesFormed` per held tick) |
| 13 | `BountyJudgingSystem` | Expedition | none | — |
| 14 | `ExpeditionSystem` | Expedition | combat draws | — |
| 15 | `ExpeditionDeepSystem` | ExpeditionDeep | combat draws | — |
| 16 | `ExpeditionRevealSystem` | Evening | none | — |
| 17 | `BountyPayoutSystem` | Evening | none | — |
| 18 | `ArcDirectorSystem` | Evening | none | — |
| 19 | `MarketShareSystem` | Evening | none | — (last in Evening by contract) |

Load-bearing order facts stated in the composition doc (`GameComposition.cs:16-53`): drift settles
standing before anything reads it; restock precedes shopping; `CommissionSystem` runs after
shopping so a hero who just bought their own fix is not offered a commission for it;
`MusterSystem` is last in Morning so its `PartiesFormed` prediction byte-matches what
`ExpeditionSystem` forms two phases later; `ArcDirectorSystem` runs after the reveal so today's
depth record is visible; `MarketShareSystem` reads `ActionSlotsRemaining` after every handler has
had its chance to spend one but before the kernel's own reset.

Guard note: `FactionDriftSystem` carries no held-Morning guard (`sim/GameSim/Factions/FactionDriftSystem.cs:29-70`),
so on a held Morning (open counter session across multiple ticks) drift steps once per tick, not
once per calendar day. Whether that is intended is listed in §17.

### 1.5 Day boundary, budget, and campaign length

- Action budget: `ActionBudget.SlotsPerDay = 5` (`sim/GameSim/Contracts/ActionBudget.cs:18`).
  `ActionBudget.ConsumesSlot` (`ActionBudget.cs:50-59`) names exactly the TEN slot-spending
  actions: Craft, BuyOre, BuyMaterial, PostBounty, ReforgeHeirloom, BuyForgeSupply, UpgradeForge,
  MasterworkAttempt, CommissionLegendaryWork, UnlockTalent. Everything else is free.
- The kernel has NO fixed campaign length. The 3-act arc (§9.7) fires an ending event
  (`CampaignEnded`) `EndingDelayDays = 5` days after the climax
  (`sim/GameSim/Arc/ArcDirectorSystem.cs:62`), and the world stays open afterward — the arc
  director simply stops (`ArcDirectorSystem.cs:77-80`). The 100-day horizon used everywhere is a
  test/telemetry convention (`sim/GameSim.Cli/BatchRunner.cs:43`), not a rule.
- A fresh campaign: day 1, Morning, 100 gold (`sim/GameSim/Kernel/GameFactory.cs:10`), blacksmith
  selected (`sim/GameSim/Contracts/Player.cs:51-56`), six fixed heroes installed
  (`GameComposition.cs:188-189`); the chosen-profession overload adds 6 starter copper
  (`GameFactory.cs:14`, `GameFactory.cs:267-282`).

---

## 2. Every player action verb

All 25 concrete `PlayerAction` types (`sim/GameSim/Contracts/Actions.cs:10-36`). "Slot" = spends
one of the day's 5 action slots (`ActionBudget.cs:50`). "Now" = resolves via `ApplyNow`
(`ActionTiming.cs:75`). Phase legality is the handler's `CanHandle`; the full Apply-level guard
chain is mirrored (deliberately duplicated) in `Advisor/ActionLegality.IsLegal`
(`sim/GameSim/Advisor/ActionLegality.cs:50-78`), whose fallthrough THROWS for an unmirrored type.

| Action | Handler | Phases | Slot | Now | Effect |
|---|---|---|---|---|---|
| `CraftAction(recipeId, materialKey, grade?, puzzle?, subScores?, oil?, rune?, fitting?)` | `CraftingHandlers` (`sim/GameSim/Crafting/CraftingHandlers.cs:35-36`) | all | yes | yes | mints an item (§4.4); one `Roll100`; emits `ItemCrafted` (+`ItemSigned` on a signing proc) |
| `StockAction(item, price)` | `ShopHandlers` (`sim/GameSim/Economy/ShopHandlers.cs:25-26`) | all | no | yes | shelf entry added; guards: exists, player-crafted, not equipped by any hero alive or dead, sold consumables never restock, one slot per item, price > 0 (`ShopHandlers.cs:42-97`) |
| `SetPriceAction(item, price)` | `ShopHandlers` | all | no | yes | reprices a shelf entry (`ShopHandlers.cs:100-121`) |
| `UnstockAction(item)` | `ShopHandlers` | all | no | yes | removes the shelf entry; the item stays in `GameState.Items` forever (`ShopHandlers.cs:123-141`) |
| `BuyOreAction(from, materialKey, qty)` | `OreMarketHandlers` (`sim/GameSim/Economy/OreMarketHandlers.cs:35-36`) | Evening | yes | yes | player pays faction-tariffed cost, hero receives the base ask, materials up, standing rises (§7.4) |
| `BuyMaterialAction(materialKey, qty)` | `MaterialVendorHandlers` (`sim/GameSim/Economy/MaterialVendorHandlers.cs:45-46`) | Morning | yes | yes | vendor sells any `PricedPool` key at +25% markup, ceiling-divided (§7.3); emits `MaterialPurchased` |
| `PostBountyAction(floor, gold)` | `BountyHandlers` (`sim/GameSim/Bounties/BountyHandlers.cs:15-16`) | Morning, Evening | yes | yes | escrows gold from the purse into a `Bounty` (§8); emits `BountyPosted` |
| `UnlockTalentAction(nodeId, profession)` | `CraftingHandlers` (`CraftingHandlers.cs:311-368`) | all | yes | yes | adds the node; prereqs must be held; the two smithing tier gates additionally need Forge Tier II/III (`CraftingHandlers.cs:343-351`, `sim/GameSim/Crafting/TalentTree.cs:249-254`) |
| `SetProfessionsAction(set)` | `ProfessionHandlers` (`sim/GameSim/Professions/ProfessionHandlers.cs:25`) | all | no | NO — bell | replaces `SelectedProfessions`; 1..2 registered ids (`ProfessionHandlers.cs:34-57`) |
| `SendSupplyAction(to, item)` | `CampHandlers` (`sim/GameSim/Expedition/CampHandlers.cs:34-35`) | Camp | no | yes | runner fee paid; consumable front-inserted into working pack AND `Hero.Pack` (§6.7) |
| `RecallPartyAction(member)` | `CampHandlers` | Camp | no | yes | flips `InFlightExpedition.Recalled`; the Deep tick banks and surfaces with no rolls (§6.7) |
| `OpenCounterAction` | `CounterHandlers` (`sim/GameSim/Counter/CounterHandlers.cs:29-31`) | Morning | no | yes | opens stepped service; queue = alive heroes, band desc then HeroId asc (§7.2) |
| `PresentItemAction(item)` | `CounterHandlers` (`CounterHandlers.cs:86`) | Morning | no | yes | shows a shelved item; resolves the verdict in the same call (walk, or open a haggle round) |
| `SuggestItemAction(item)` | `CounterHandlers` | Morning | no | yes | +80‰ Interest if the item lands on a complementary empty wearable slot; legal no-op otherwise |
| `HaggleResponseAction(kind, price?)` | `CounterHandlers` (`CounterHandlers.cs:40`) | Morning | no | yes | Accept / HoldFirm / Counter against the standing offer (§7.2) |
| `CloseCounterAction` | `CounterHandlers` (`CounterHandlers.cs:41`) | Morning | no | yes | flips `Closed`; unserved heroes fall back to the atomic pass the same tick |
| `AcceptCommissionAction(hero)` | `CommissionHandlers` (`sim/GameSim/Heroes/CommissionHandlers.cs:19-20`) | Morning | no | yes | flips `Commission.Accepted` on the hero's single open commission |
| `DeclineCommissionAction(hero)` | `CommissionHandlers` | Morning | no | yes | removes the open commission, no obligation |
| `HonorMemorialAction(hero)` | `FarewellHandlers` (`sim/GameSim/Drama/FarewellHandlers.cs:20-21`) | Evening | no | yes | flips `Memorial.Honored` once; a second rite is a clean no-op, not a rejection |
| `ReforgeHeirloomAction(sourceItem, recipeId, materialKey)` | `HeirloomHandlers` (`sim/GameSim/Crafting/HeirloomHandlers.cs:40`) | all | yes | yes | crafts a new item stamped with `HeirloomLineage`; source must appear in some `HeroDied.WornGear` and never have been reforged (§4.8) |
| `UpgradeForgeAction` | `ForgeTierHandlers` (`sim/GameSim/Economy/ForgeTierHandlers.cs:61-62`) | Morning | yes | NO — bell | +1 forge tier for gold + 25 floor-ore (§7.7) |
| `BuyForgeSupplyAction(key, qty)` | `ForgeSupplyHandlers` (`sim/GameSim/Economy/ForgeSupplyHandlers.cs:44-45`) | Morning | yes | yes | coal 4g / flux 40g per unit, flat (§7.7) |
| `MasterworkAttemptAction(recipeId, materialKey)` | `MasterworkAttemptHandlers` (`sim/GameSim/Economy/MasterworkAttemptHandlers.cs:47`) | all | yes | yes | RNG-free guaranteed Superior (Masterwork if material outgrades recipe) for coal+flux+gold+materials (§7.7) |
| `CommissionLegendaryWorkAction(recipeId, materialKey)` | `LegendaryCommissionHandlers` (`sim/GameSim/Economy/LegendaryCommissionHandlers.cs:40`) | all | yes | NO — bell | guaranteed Masterwork; 3000g x tier, double materials, 4 per campaign (§7.7) |
| `ConcludeApprenticeshipAction` | `ConcludeApprenticeshipHandlers` (`sim/GameSim/Kernel/ConcludeApprenticeshipHandlers.cs:18-22` — accepts in every phase, mutates nothing) | all | no | yes | its entire meaning is its presence in `ActionLog`; `ApprenticeWarrant.Concluded` scans for it (§6.8) |

Rejections are always typed (`RejectedAction`, `Actions.cs:205`), never silent, and every handler
checks the action-slot gate LAST so a slot-exhausted day never masks a more specific reason
(e.g. `CraftingHandlers.cs:131-139`). No handler draws RNG before all rejections are cleared
(`CraftingHandlers.cs:13-14`).

---

## 3. Crafting

### 3.1 Professions

Four registered professions (`sim/GameSim/Professions/ProfessionRegistry.cs:169-175`):
blacksmith, tanning, engineering, alchemy — ALL four have `ActiveCraft: true`
(`ProfessionRegistry.cs:154`, `sim/GameSim/Professions/Tanning/TanningProfession.cs:100`,
`sim/GameSim/Professions/Engineering/EngineeringProfession.cs:116`,
`sim/GameSim/Professions/Alchemy/AlchemyProfession.cs:119`), so every craft in the game resolves
through the active dominance roll (§3.3), never the passive table (§3.4). A save selects 1–2
professions (`ProfessionHandlers.cs:23`); only selected professions' recipes may be crafted
(`CraftingHandlers.cs:61-64`).

Each `ProfessionDefinition` (`sim/GameSim/Professions/ProfessionDefinition.cs:64-73`) carries:
recipes, an 8-node talent tree, tier gates ({2: tier-2 node, 3: tier-3 node}), a
material-efficiency node (−1 material, floor 1), a quality model whose only live field under the
active model is `MaterialMasteryNode` (+1 effective material grade), and `MinigameAssists` — the
retired quality-shift nodes remapped to per-mille forgiveness (50/70/80 chain + a slot-scoped 50:
blacksmith weapon, alchemy consumable, tanning armor, engineering trinket;
`ProfessionRegistry.cs:155-166`, `AlchemyProfession.cs:120-131`,
`TanningProfession.cs:101-112`, `EngineeringProfession.cs:120-127`).

### 3.2 Recipes and materials

45 recipes total. Blacksmith: 22 (`sim/GameSim/Crafting/RecipeTable.cs:54-129`) — 15 gear (5 per
slot, tiers 1–3), Field Salve (Heal 6, `RecipeTable.cs:80-81`), 3 Gloomwood-ore recipes tier 8–9
(Gloomsteel Blade Atk 60 / Wardenweave Mail Def 50 / Moonresin Draught Heal 18,
`RecipeTable.cs:94-101`), 3 Emberfall-ore recipes tier 12–14 (Cinderforge Blade Atk 90 / Ashguild
Plate Def 75 / Emberglass Draught Heal 30, `RecipeTable.cs:121-128`). Alchemy: 8
(`AlchemyProfession.cs:68-91`) — a heal ladder 6/10/15/22/30 plus a robe and two trinkets.
Tanning: 7 (`TanningProfession.cs:60-76`) — light armor/shields plus Field Poultice (Heal 5).
Engineering: 8 (`EngineeringProfession.cs:72-90`) — weapons/shield/armor/trinkets plus Field
Repair Kit (Heal 5; `ConsumableKind` has only `Heal`, so every "utility" consumable is
mechanically a heal — `sim/GameSim/Contracts/Enums.cs:70-77`,
`AlchemyProfession.cs:31-34`).

Materials are one registry (`sim/GameSim/Materials/MaterialRegistry.cs:82-110`): the five Mine
ores copper/iron/steel/mithril/adamant at price 3/5/8/12/18 and grade 1/2/3/4/5; Crypt ores
verdigris..abyss-pearl mirroring 3–18 / grade 1–5; Gloomwood greenheart/amberpitch/moonresin/
heartwood 36/42/48/54 grade 8–11; Emberfall firebrick..heartcoal 60–84 grade 12–16; plus INERT
electrum (24/6) and orichalcum (30/7) — registered, in no `PricedPool`, minted by no venue.
`PricedPool` (`MaterialRegistry.cs:94-110`) is the live surface: the vendor sells it, ore pricing
prices it, `RecipeTable.MaterialGrades` grades exactly it (`RecipeTable.cs:44-48`). A craft with a
non-pool material is rejected as an unknown material (`CraftingHandlers.cs:67-70`).

Material cost per craft: `needed = max(1, recipe.MaterialQuantity − (efficiency node ? 1 : 0))`
(`CraftingHandlers.cs:80-85`).

### 3.3 Quality — the active model (the only live path)

`QualityRoller.RollActive` (`sim/GameSim/Crafting/QualityRoller.cs:145-186` — table doc at
`QualityRoller.cs:110-144`): exactly ONE `Roll100` draw per craft.

```
effective = clamp(performanceGrade ?? 800, 0, 1000) + jitter
jitter    = Roll100() * 51 / 100 − 25              // [0,99] → [−25,+25]
band:  < 200 Poor | < 550 Common | < 780 Fine | < 930 Superior | else Masterwork
```
(`QualityRoller.cs:107` ActiveJitterMax=25, `QualityRoller.cs:112` AutoCraftGrade=800,
band table `QualityRoller.cs:187-194`.)

Then two ceilings: `materialStep = materialGrade + (mastery node ? 1 : 0) − recipe.Tier`; step
≤ −1 caps Fine, step 0 caps Superior, step ≥ +1 uncapped (`QualityRoller.cs:197-202`). Auto-craft
(null grade AND null puzzle) additionally hard-caps at Superior — the minigame is the only road to
Masterwork (`QualityRoller.cs:168-171`). Talent shift nodes never touch this roll; only material
mastery matters, and only for the ceiling.

Where `performanceGrade` comes from (`CraftingHandlers.cs:151-179`), first non-null wins:
1. a blacksmith `ForgeTraceInput` scored by `ForgeScorer` (§3.5);
2. the batch echo (§3.6);
3. an alchemy/tanning/engineering puzzle scored in-sim (§3.7);
4. the action's Godot-captured `PerformanceGrade`;
5. null → auto-craft at 800 minus nothing, Superior-capped.

### 3.4 Quality — the passive model (dormant)

`QualityRoller.Roll` (`QualityRoller.cs:46-95`): `effective = Roll100() + shift`;
`shift = 8·(materialGrade + mastery − tier) + Σ flat shifts + Σ matching slot shifts`; grade
thresholds `≤14 Poor | ≤64 Common | ≤89 Fine | ≤98 Superior | ≥99 Masterwork`
(`QualityRoller.cs:79-87`), plus an optional per-mille grade mapped to ±8
(`QualityRoller.cs:44` PerformanceShiftMax=8, `QualityRoller.cs:55-60`). Base odds at shift 0:
Poor 15%, Common 50%, Fine 25%, Superior 9%, Masterwork 1% (`QualityRoller.cs:33-35` doc). This
path is reachable only for a profession with `ActiveCraft: false` — none is registered today.

### 3.5 The blacksmith forge minigame (Anvil Map)

Input: `ForgeTraceInput(Samples, Strikes, PathSeed)`
(`sim/GameSim/Crafting/ForgeTraceInput.cs:35-38`): flat per-mille (x,y) sample pairs (≤256
pairs), (x, tempoError) strike pairs, and a seed. The target polyline is regenerated in-sim by
`ForgePath.Generate(tier, slot, weight, pathSeed)` (`sim/GameSim/Crafting/ForgePath.cs:75-150`)
using `StableHash` only — smelt zone x ≤ 333, forge ≤ 666, quench to 1000
(`ForgePath.cs:32-35`), tier-scaled interior vertices, tier-sharpened quench plunge
(`QuenchSpanTier1 = 260`, −70 per tier step, `ForgePath.cs:61-64`).

`ForgeScorer.Score` (`sim/GameSim/Crafting/ForgeScorer.cs:104-230`): each sample's |y − target|
becomes a sub-score `clamp(1000 − dev·4, 0, 1000)` (`ForgeScorer.cs:75` DevScale=4,
`ForgeScorer.cs:248`); zone sub-scores fold 300/400/300 (smelt/forge/quench,
`ForgeScorer.cs:91-93`); the forge zone averages sample tracking with strike tempo accuracy over
ALL strikes (`ForgeScorer.cs:177-196`). Talent assists subtract from deviation / tempo error
(`ForgeScorer.cs:256-281`). Moments (bitflags, `ForgeScorer.cs:13-31`): ForgedInOneHeat (≤1
rising edge through y=650), NeverScorched (no y > 900), PerfectQuench (avg quench deviation < 50),
RecoveredFromTheBrink (touched a scorch >900 or a forge-zone crack <400 yet graded ≥ 550)
(`ForgeScorer.cs:78-98`, `ForgeScorer.cs:202-226`). A hand-forge with ≥1 moment writes the item's
first History entry "forged ..." (`CraftingHandlers.cs:212-215`, prose at
`CraftingHandlers.cs:250-260`).

There is ALSO an alternative sim-side forge depth, `QualityRoller.SimulateActiveForge`
(`QualityRoller.cs:230-257`): heat-band strikes against
seeded condition windows — one `Roll100` per strike thrown, capped by the material durability
budget `min(6 + 2·(grade−1), 40)` (`sim/GameSim/Crafting/HeatBandForge.cs:83-88`); window odds
Perfect 5% / Good 25% / Normal 70% (`HeatBandForge.cs:43-48`), pity forces Good on the 4th
window-less strike (`HeatBandForge.cs:54`, `HeatBandForge.cs:99-118`); progress in-band 2 /
out 1, multiplier Normal 1 / Good 2 / Perfect 4 (`HeatBandForge.cs:27-39`); grade =
`progress·1000 / (min(strikes,budget)·2·4)` (`HeatBandForge.cs:146-161`). It is reachable only
from a minigame path, never from auto-craft.

### 3.6 Batch echo

A null-grade, null-puzzle auto-craft repeating the last hand-forge's recipe on the SAME day
inherits `max(550, seedGrade − 80·(uses+1))`, at most 4 echoes
(`CraftingHandlers.cs:27-33` — BatchEchoCount 4, decay 80‰/copy, floor 550;
`CraftingHandlers.cs:160-165`; state in `BatchEchoState`,
`sim/GameSim/Contracts/Player.cs:17`). A hand-forge reseeds the memory; a new day or different
recipe silently stales it (`CraftingHandlers.cs:220-224`).

### 3.7 The other three professions' in-sim puzzle scorers

All three are pure, total, integer-only, zero-RNG; their output feeds `RollActive` as the
performance grade. Talent assists sum the unlocked nodes' three fields into one flat per-mille
bonus, slot-scoped for the specialist node.

- **Alchemy** (`sim/GameSim/Professions/Alchemy/AlchemyPuzzleScorer.cs:105-156`): each pour
  scores 2 for right-reagent-right-position, 1 for right-reagent-wrong-position (multiset-aware),
  0 otherwise; `base = points·1000/(2·idealLength)`. Ideal sequences per recipe are fixed data
  (`AlchemyPuzzleScorer.cs:50-60`); an unlisted recipe derives one from an ordinal char sum,
  length `clamp(tier+2, 3, 5)` (`AlchemyPuzzleScorer.cs:68-99`). Six reagents
  (`sim/GameSim/Professions/Alchemy/AlchemyReagents.cs:146-155`).
- **Tanning** (`sim/GameSim/Professions/Tanning/TanningScrapeScorer.cs`): an 8x5 grid
  (`TanningScrapeScorer.cs:165-171`); 5 flaw patches (want 3–4 passes), 4 thin patches (tolerate
  exactly 1), plain cells want 1–2 (`TanningScrapeScorer.cs:183-185`, ideal bands at the
  `IdealPassesFor` switch); patches derive from `PatchSeed` via an LCG walk
  (`TanningScrapeScorer.cs:204+`); perfect cell 2 pts, partially-worked flaw 1 pt; over-scraped
  cells score 0 AND dock the grade 12‰ each (`TanningScrapeScorer.cs:174-181`).
- **Engineering** (`sim/GameSim/Professions/Engineering/EngineeringAssemblyScorer.cs`): sockets =
  `clamp(tier+2, 3, 5)` (`EngineeringAssemblyScorer.cs:45-59`); schematic derives from recipe id
  char sum mod 6 parts (`EngineeringAssemblyScorer.cs:68-84`); first placement per socket counts;
  exact 2 pts, called-for-but-misplaced 1 pt (multiset-aware); a consecutive ascending-socket
  fill prefix earns an order bonus up to 90‰ (`EngineeringAssemblyScorer.cs:42`,
  order math after `EngineeringAssemblyScorer.cs:158`).

A puzzle submitted to the wrong profession's recipe is rejected, never mis-scored
(`CraftingHandlers.cs:97-129`).

### 3.8 Minting, stats, and the mark

`ItemForge.Forge` (`sim/GameSim/Crafting/ItemForge.cs:34-57`; quality table `ItemForge.cs:20-27`): Attack and Defense scale by quality percent — Poor 80 /
Common 100 / Fine 115 / Superior 135 / Masterwork 160; Weight never scales; a consumable's Heal
`Magnitude` scales by the same table. Every player craft is stamped `MakersMark("You", day)`
(`sim/GameSim/Contracts/Items.cs:26`; stamped in `ItemForge.Forge`);
`Item.PlayerCrafted` is simply `Mark is not null` (`Items.cs:106`). Rival goods are minted with
`Mark: null` (`sim/GameSim/Economy/RivalCatalog.cs:169-177`). There is no durability on items —
nothing decays or breaks; gear persists until displaced, and a displaced item stays in
`GameState.Items` forever with its history (`sim/GameSim/Heroes/HeroShoppingSystem.cs:370-375`).

### 3.9 Craft modifiers (oils, runes, fittings)

Registry of four (`sim/GameSim/Crafting/CraftModifiers.cs:23-47`): Coward's Oil (flee threshold
+8%·tier), Braveheart Oil (−8%·tier), Leech Rune (heal 3·tier on kill), Lodestone Fitting (+1·tier
ore per loot roll) (`CraftModifiers.cs:104-107`). One modifier per family per item
(`Items.cs:13`); slots by grade — Poor 0, Common 1, Fine 2, Superior/Masterwork 3
(`CraftModifiers.cs:146-152`); tier capped by material — mithril/adamant/orichalcum tier 2, all
else 1 (`CraftModifiers.cs:134-138`); a Masterwork grants +1 tier overshoot on the FIRST fitted
modifier (`CraftModifiers.cs:156`, applied in `CraftingHandlers.cs:270-309`). Invalid or
over-budget requests are silently dropped — a craft never fails over modifiers
(`Actions.cs:51-57`). Effects aggregate once per hero per expedition over all equipped items
including trinket (`CraftModifiers.cs:113-128`).

### 3.10 Signed Works

`ArtifactSigning.Qualifies` (`sim/GameSim/Crafting/ArtifactSigning.cs:52-57` — const at
`ArtifactSigning.cs:36`): Masterwork AND exactly 3 `CraftSubScores` each ≥ 950. Only a
hand-forged Anvil-Map craft carries 3 sub-scores, so auto-crafts and purchased masterworks can
never sign. The legend name is a pure hash pick over (campaign `Rng.Inc`, item id, recipe id,
day) from a frozen 12-name pool (`ArtifactSigning.cs:43-48` pool;
`ArtifactSigning.cs` `LegendName`). `SignedName` is DATA — no sim rule keys off it beyond
`LegendQuery.DiedBearingSignedWork` (§9.5) (`Items.cs:160-165` doc).

### 3.11 Heirloom reforge

`HeirloomHandlers.Apply` (`sim/GameSim/Crafting/HeirloomHandlers.cs:42-159`): the source item must
be found in some `HeroDied.WornGear` (first match in log order) and never previously reforged
(one heirloom per fallen piece, ever — checked against `HeirloomReforged` events); then the exact
craft guard chain runs and the mint takes the auto-craft path (one `Roll100`, Superior-capped).
The new item carries `HeirloomLineage = "forged from the {item} of {hero}"`
(`HeirloomHandlers.cs:140-142`) — presentation-only data (`Items.cs:170-175`).

### 3.12 Talents

Blacksmith tree (`sim/GameSim/Crafting/TalentTree.cs:228-238`): keen-eye → master's-touch →
legendary-craft; keen-eye → weapon-specialist; material-efficiency → material-mastery;
tier-2-smithing → tier-3-smithing. Unlocks cost prerequisites plus (since U-T1-9) one action slot,
and the two tier gates require Forge Tier II / III respectively (`TalentTree.cs:249-254`). The
other professions' trees mirror this 8-node shape (§3.1). There is no talent-point currency.

---

## 4. Heroes

### 4.1 The Hero record

`Hero` (`sim/GameSim/Contracts/Heroes.cs:39-108`): `Id, Name, ClassId, Level, MaxHp, Gold, Gear
(Weapon/Shield/Armor/Trinket item ids), Memories (per-item kills/saves), Alive, DeepestFloorReached,
DiedOnDay` + init members `Pack` (carried consumables, order = quaff order, `Heroes.cs:58`),
`MoodPermille` (signed, unbounded opinion of the shop; influence-only by contract,
`Heroes.cs:68`), `Xp` (`Heroes.cs:78`), `LadderRank` (monotonic dungeon-graduation count,
`Heroes.cs:93`). `Hero.GearScore` = Σ (Attack+Defense) over all four slots including Trinket
(`Heroes.cs:96-108`) — read ONLY by `ShoppingAi` (`sim/GameSim/Heroes/ShoppingAi.cs:154-155`);
its doc's claim of "floor gates" is not what the code does (floor gates use
`CombatMath.EffectivePower`, which excludes trinkets — §16.5).

### 4.2 Classes

Six registered, all recruitable (`sim/GameSim/Classes/ClassRegistry.cs:59-95`):

| Class | BaseHp | BaseAtk | Anchor | Shield | Weight cap | Haggle factor ‰ |
|---|---|---|---|---|---|---|
| Vanguard | 29 | 4 | yes | yes | — | 1150 |
| Sentinel | 32 | 3 | yes | yes | — | 1120 |
| Striker | 24 | 6 | no | no | — | 1000 |
| Skirmisher | 26 | 5 | no | no | 6 | 820 |
| Mystic | 20 | 3 | no | no | 4 | 950 |
| Occultist | 18 | 5 | no | no | 4 | 980 |

(`ClassRegistry.cs:26-56`, `sim/GameSim/Classes/Sentinel/SentinelClass.cs:28-36`,
`Skirmisher/SkirmisherClass.cs:28-36`, `Occultist/OccultistClass.cs:27-35`; factors at
`sim/GameSim/Counter/WillingnessModel.cs:39-44`.)

The starting six (`sim/GameSim/Heroes/HeroRoster.cs:42-47`): Torvald (Vanguard, 30 HP, 40g),
Brunhilde (Vanguard, 28, 35), Kael (Striker, 25, 55), Sable (Striker, 23, 60), Elowen (Mystic,
20, 45), Moss (Mystic, 21, 30) — fixed data, no RNG, ids 1–6, `NextHeroId` then 7
(`HeroRoster.cs:19`).

### 4.3 Traits

Every hero carries exactly 2 traits from 2 distinct axes, derived (never stored, never drawn) from
`StableHash` over `(HeroId, Name)` — campaign-invariant
(`sim/GameSim/Heroes/TraitDefinition.cs:111-129`). Axes/sides
(`TraitDefinition.cs:91-99`): PriceSensitivity (Spendthrift/Thrifty), QualityDemand
(Discerning/Unfussy), Sentiment (Sentimental/Practical), HagglePatience (Patient/Stubborn),
ConsumableStocking (Prepared/Reckless). Effects (`sim/GameSim/Heroes/TraitEffects.cs`):

| Trait | Effect | Value |
|---|---|---|
| Spendthrift / Thrifty | willingness factor ‰ | +90 / −90 (`TraitEffects.cs:18`, `:22`) |
| Spendthrift / Thrifty | bounty greed | 14 / 6 vs base 10 (`sim/GameSim/Bounties/BountyRules.cs:33-41`) |
| Discerning / Unfussy | veteran min quality grade | +1 / −1 step (`TraitEffects.cs:41`, `:45`) |
| Sentimental / Practical | storied-gear deed threshold | −2 (min 1) / +1000 (`TraitEffects.cs:73`, `:77`) |
| Patient / Stubborn | haggle patience rounds | +1 / −1 (min 1) (`TraitEffects.cs:96`, `:100`) |
| Prepared / Reckless | consumable stock target | 2 / 0 vs base 1 (`TraitEffects.cs:121-129`) |

Traits drive shopping, haggling, stocking, and bounty appetite; the raid resolver reads none of
them directly (only their downstream purchases).

### 4.4 Morning shopping (the atomic pass)

`HeroShoppingSystem.Process` (`sim/GameSim/Heroes/HeroShoppingSystem.cs:39-75`): every ALIVE hero
in ascending HeroId order runs a gear pass, then everyone runs a consumable pass. Strictly
sequential — earlier heroes thin the shelf for later ones. Zero RNG.

Gear pass per hero (`HeroShoppingSystem.cs:78-138`): first, an ACCEPTED commission short-circuit
(§4.7); else evaluate every entry on BOTH shelves through `ShoppingAi.EvaluateItem` and buy the
single best `Buy` verdict by gear-score-gain per gold (cross-multiplied, ties → raw gain → lower
ItemId; `ShoppingAi.cs:251-267`). `ShoppingAi.EvaluateItem` check order
(`ShoppingAi.cs:110-187`):
1. role fit — Shield needs `AllowsShield`; weight cap per class;
2. veteran quality gate — a hero with `DeepestFloorReached ≥ 3` refuses anything below Common
   (trait-shifted ±1) (`ShoppingAi.cs:68`, `:81`, `:137-143`);
3. affordability (`price ≤ hero.Gold`);
4. storied-gear loyalty — if the worn item's memories total ≥ 3 deeds (trait-shifted), a gain
   < 5 passes Sentimental (`ShoppingAi.cs:86-90`, `:164-175`);
5. strict upgrade — gain ≤ 0 passes NotAnUpgrade.

A boycotting hero (§4.5) reads player-shelf candidates 40% pricier for RANKING only — the actual
purchase always pays the listed price (`HeroShoppingSystem.cs:185-198`). Every player-shelf item
looked at and not bought emits `HeroPassedOnItem` with the legible reason; rival-shelf passes stay
silent (`HeroShoppingSystem.cs:19-22`, `:100-123`). A gear buy involving the player's shelf also
emits one `HeroDecisionExplained` naming the runner-up and a per-mille gap
(`HeroShoppingSystem.cs:251-287`).

Consumable pass (`HeroShoppingSystem.cs:299-347`): while `Pack.Count <` the hero's trait stock
target, buy the single cheapest affordable Heal item (player shelf wins price ties, then lower
ItemId), at most one per hero per Morning. Consumables never enter the gear pass.

Purchase application (`HeroShoppingSystem.cs:370-409`): consumables append to `Pack`; gear
replaces the slot (the displaced item is simply dropped — no resale); player sales credit the
purse and clear the shelf entry; rival sales just remove the entry (the rival's gold is
unmodelled). Emits `ItemSold(item, buyer, price, fromPlayerShop)`.

### 4.5 Needs / boycott (derived, never stored)

`NeedsSystem` (`sim/GameSim/Heroes/NeedsSystem.cs`): the unmet-demand streak = days since the
hero's last player-shop purchase (or arrival day), recomputed from the event log on every read
(`NeedsSystem.cs:76-94`). Telegraph at 4 days, boycott at 6 (`NeedsSystem.cs:49`, `:53`);
boycott = a 400‰ comparison-only price penalty on player-shelf candidates
(`NeedsSystem.cs:61`) — never a block; one sale resets the streak. `Snapshot`
(`NeedsSystem.cs:132+`) yields entries only for telegraphed/boycotting/just-recovered heroes.

### 4.6 Relationships

Player↔hero band (`sim/GameSim/Heroes/RelationshipBands.cs:30-35`, `:43-64`): Sworn = ≥5
player-shop purchases AND mood ≥ 300; Patron = ≥3 purchases OR mood ≥ 200; Regular = ≥1 purchase
OR mood ≥ 80; else Stranger. Derived from the event log + `MoodPermille`; used ONLY to order the
counter queue, scale commission asks, and drive prose (PKD7).

Hero↔hero edges (`sim/GameSim/Heroes/RelationshipSystem.cs:87-137`): three mechanisms —
shared `PartyDeparted` (+20/event), witnessed party-death (+35 grief per survivor pair), and
same-day outbid (miss then sale of the same item, −30, escalating to RivalrySeed at 2 distinct
events) (`RelationshipSystem.cs:68-81`); each contribution decays linearly to zero over 40 days
(`RelationshipSystem.cs:77`, `:274-289`). Edges feed gossip salience only
(`sim/GameSim/Drama/GossipSystem.cs:21-25`) — never a raid decision.

### 4.7 Commissions

`CommissionSystem` (Morning, `sim/GameSim/Heroes/CommissionSystem.cs:62-77`): expiry first, then
top the board up to 3 open commissions (`CommissionSystem.cs:44`), scanning heroes in id order.
Only a hero actually mustering today gets one (their plan's target floor scales the ask,
`CommissionSystem.cs:145-151`). Gap order is survival-first: Weapon, Shield (skipped for
shield-less classes), Armor, then a Heal-in-pack presence test, then Trinket — trinket only for
Regular+ band (`CommissionSystem.cs:252-301`). MinQuality = max(floor bar, band bar): floor ≥5 →
Superior, ≥3 → Fine, else Common (`CommissionSystem.cs:189-194`); band Sworn → Superior, Patron →
Fine (`CommissionSystem.cs:198-203`). Premium = 15 + 10·floor + band bonus (Sworn 50 / Patron 25 /
Regular 10) (`CommissionSystem.cs:50-53`, `:212-221`). Deadline = day + 5
(`CommissionSystem.cs:47`).

Expiry: an ACCEPTED commission past deadline emits `CommissionExpired` and −100 mood
(`CommissionSystem.cs:56`, `:82-107`); a posted-never-accepted one vanishes silently.

Fulfillment (`sim/GameSim/Heroes/CommissionHandlers.cs:74-141`): checked BEFORE ordinary shopping;
the first (lowest ItemId) player-shelf item matching slot + MinQuality + role-fit + weight-cap is
bought at list + premium, GUARANTEED — it bypasses veteran/upgrade/value gates. If the hero cannot
cover the full premium they pay list + whatever premium they can (`CommissionHandlers.cs:136`);
if they cannot cover list, nothing happens this Morning. Emits `ItemSold` + `CommissionFulfilled`
and +100 mood (`CommissionHandlers.cs:58`).

### 4.8 XP, rank, level

At the Evening reveal each survivor earns `10 + 5·deepestFloorCleared + 15·creditedBeats`
(KillingBlow/LethalSave beats naming them this run) (`sim/GameSim/Heroes/HeroXp.cs:19-30`;
applied at `sim/GameSim/Drama/ExpeditionRevealSystem.cs:240-273`). Rank ladder: Novice 0, Delver
50, Journeyman 150, Veteran 300, Champion 500, Legend 800 (`HeroXp.cs:44-49`); `Hero.Level` is the
1-based rank index off the SAME ladder (`HeroXp.cs:75-89`), and Level feeds combat:
`HeroAttack = classBase + 2·Level + weapon.Attack`, `HeroDefense = Level + shield.Defense +
armor.Defense` (`sim/GameSim/Expedition/CombatMath.cs:52`, `:54-57`).

### 4.9 Death, replacement, the roster

Permadeath: `Alive` flips once at the reveal, `DiedOnDay` set, a `Memorial` raised naming the worn
gear (`ExpeditionRevealSystem.cs:85-103`). Dead heroes keep their gear and records; nothing ever
removes a hero from `GameState.Heroes`. `RecruitSystem` (Morning,
`sim/GameSim/Drama/RecruitSystem.cs:41-87`): while living heroes < 6 (`RecruitSystem.cs:22`) and
the 2-day gate (`RecruitSystem.cs:27`) is at zero, mint one recruit — three RNG draws in fixed
order: name from a 24-name append-only pool, class from `ClassRegistry.RecruitPool` (all six),
gold 30–60 (`HeroRoster.cs:67-85`). If any memorialized hero qualifies as a famous-dead legend,
the recruit arrives with +60 mood (`RecruitSystem.cs:35`, `:75-78`). Wounds never persist: the
resolver starts every expedition at MaxHp (`sim/GameSim/Expedition/ExpeditionResolver.cs:34`).

### 4.10 Party formation and the muster

`PartyFormation.FormParties` (`sim/GameSim/Heroes/PartyFormation.cs:33-45`): alive heroes are
cohorted by `LadderRank` FIRST (ascending), then within each cohort: parties of 3, each taking one
anchor-class hero when available, filled by lowest HeroId (anchors beyond the reserve may fill);
leftovers form one smaller party, even solo (`PartyFormation.cs:52-90`). Postcondition: every
party is rank-uniform. Pure, no RNG.

`MusterPlan.Compute` (`sim/GameSim/Heroes/MusterSystem.cs:36-83`) predicts the Expedition tick at
Morning by running the identical helpers: predicted bounty first-accept (silent), the same
formation, the same venue router with the same shared queue counts, the same
`ExpeditionSystem.TargetFloorFor`. `MusterSystem.Process` emits `PartiesFormed` (empty list
included) plus a `HeroDecisionExplained` only when a bounty overrode the default floor
(`MusterSystem.cs:290+` — silent when target == clamp(deepest+1, 1, venue floors)).

---

## 5. Venues and routing

Four venues, all LIVE (`sim/GameSim/Venues/VenueRegistry.cs:59-66`):

| Venue | Rank | Floors | Gates | Monster HP | Monster Atk | Def | Gold/kill | Ore keys |
|---|---|---|---|---|---|---|---|---|
| Mine (`mine`) | 0 | 5 | 0/15/35/60/70 | 12+10f, floor 5 = 50 | 5+6f, floor 5 = 26 | 2+2f | 5+3f | copper..adamant |
| Sunken Crypt (`sunken-crypt`) | 0 | 5 | 0/15/35/60/70 | same as Mine | same | same | same | verdigris..abyss-pearl |
| Gloomwood (`gloomwood`) | 1 | 4 | 0/20/45/73 | 20+14f | 6+5f | 3+2f | 6+4f | greenheart..heartwood |
| Emberfall Foundry (`emberfall`) | 2 | 5 | 0/15/35/60/73 | 12+10f (floor 5 = 62, NOT dialed down) | 5+6f (floor 5 = 35) | 2+2f | 5+3f | firebrick..heartcoal |

(Mine: `VenueRegistry.cs:99-145` — floor-5 re-gate rationale at `VenueRegistry.cs:104-123`;
Crypt: `sim/GameSim/Venues/SunkenCrypt/SunkenCryptVenue.cs:75`, `:107`; Gloomwood:
`sim/GameSim/Venues/Gloomwood/GloomwoodVenue.cs:65-79`, `:107`; Emberfall:
`sim/GameSim/Venues/Emberfall/EmberfallFoundryVenue.cs:97`, `:109`, `:138`. Emberfall keeps the
raw floor-5 formula — see §16.7.)

Monster names: Mine Cave Rat/Tunnel Spider/Deep Ghoul/Ore Golem/The Forgeworm
(`VenueRegistry.cs`); Crypt Crab/Bog-Wight/Choir of Teeth/Reliquary Mimic/The Undertow; Bramble
Boar/Lantern Moth/The Wicker Shepherd/Old Mossjaw; Cinder Imp/Slag Hound/The Bellows-Mad/Molten
Archivist/The Undying Forge-Heart.

Routing (`sim/GameSim/Venues/VenueRouter.cs:69-96`, comparator `:99-148`): a bounty-free party
goes to the live venue chosen by a draw-free total-order comparator — eligible
(`partyRank ≥ venue.LadderRank`) beats ineligible; among eligible, HIGHEST venue rank (the
frontier); among ineligible (no rank-0 venue live), LOWEST; then fewest parties already routed
this tick; then ordinal id. A party with an accepted bounty routes straight to the Mine — bounties
carry no venue id (`sim/GameSim/Expedition/ExpeditionSystem.cs:214-227` — the bounty branch inside `Process`).

Graduation: clearing a venue's bottom floor promotes every SURVIVING member whose `LadderRank`
equals the venue's rank to rank+1 — the only write site, monotonic by construction
(`ExpeditionRevealSystem.cs:139-168`); emits `VenueGraduated`.

---

## 6. Expedition

### 6.1 Target floor

`clamp(max(party DeepestFloorReached) + 1, 1, venue.FloorCount)`, overridden by an accepted
bounty's floor (`ExpeditionSystem.cs:146-166` — `TargetFloorFor`, shared verbatim by the Morning
prediction and the authoritative tick).

### 6.2 Staged resolution

`CheckpointFor(target) = min(1, target − 1)` (`ExpeditionSystem.cs:26`, `:31`): every run targeting
floor ≥ 2 resolves stage 1 (floor 1) at the Expedition tick; a target of floor 1 resolves whole.
A party that clears every stage-1 floor with nobody dead and nobody too hurt PARKS as an
`InFlightExpedition` and a `PartyCampReport` is emitted (party, camped-below floor, target, HP by
hero, Heal count by hero — `ExpeditionSystem.cs:319-331` builder); any other stage-1 ending
finalizes immediately with its raw halt (`sim/GameSim/Expedition/ExpeditionResolver.cs:70-131`).
Stage 2 (`ExpeditionResolver.cs:142-189`) resumes the identical loop at the ExpeditionDeep tick on
the live kernel stream — stage-2 rolls are provably undrawn while the party camps
(`sim/GameSim/Expedition/ExpeditionDeepSystem.cs:254-288`). A recalled party banks stage-1
clears/ore and surfaces with zero draws, halt = `Recalled` (`ExpeditionResolver.cs:162-168`).
One camp per run regardless of depth.

### 6.3 The floor loop

`ResolveFloors` (`ExpeditionResolver.cs:250-401`), per floor in [from..to]:

1. Fighters = party minus dead minus retreated; none left → halt `PartyWiped`
   (`ExpeditionResolver.cs:284-289`).
2. **Structural gate**: `PartyAveragePower(fighters) < venue.Gate(floor)` → halt `GateHeld`, no
   roll (`ExpeditionResolver.cs:292-296`). `PartyAveragePower` = mean of
   `HeroAttack + HeroDefense` (`CombatMath.cs:60-67`).
3. Each fighter fights the floor's monster solo, in HeroId order (§6.4). A death leaves the floor
   uncleared; a flee leaves it uncleared; a kill banks `venue.GoldPerKill(floor)` into that hero's
   expedition purse (`ExpeditionResolver.cs:301-317`).
4. If cleared: post-floor drink check — any standing hero below the drink line quaffs (recorded at
   round = roundsFought+1), then `tooHurtToContinue` |= still below the drink line
   (`ExpeditionResolver.cs:345-358`).
5. Floor outcome sealed. Uncleared → halt `FloorLost` (someone still stands) or `PartyWiped`
   (`ExpeditionResolver.cs:360-368`).
6. Ore loot for every standing, unretreated hero: `rng.NextInt(1,4)` + Lodestone bonus, of
   `venue.OreKey(floor)` (`ExpeditionResolver.cs:375-381`).
7. `tooHurtToContinue` → halt `TooHurt` after banking the clear (`ExpeditionResolver.cs:384-388`).
8. **Competence retreat**: each standing hero retreats iff `nextFloor > DeepestFloorReached + 1`,
   unless she is the bounty acceptor within the bounty's floor band
   (`ExpeditionResolver.cs:397`, `:409-430`; exemption from `ExpeditionSystem.RetreatExemption`,
   `ExpeditionSystem.cs:132`). A retreated hero banks what she has, stays a Survivor, and fights
   no deeper floor. Stage 2 reconstructs the stage-1 retreated set by replaying the same rule
   (`ExpeditionResolver.cs:440-454`).

Halt precedence: `DeepestCleared == TargetFloor` is ALWAYS `TargetReached`, whatever exit ended
the loop (`ExpeditionResolver.cs:197-198`; enum at `sim/GameSim/Contracts/Enums.cs:79-105`).

### 6.4 One fight, in full

`FightMonster` (`ExpeditionResolver.cs:463-639`). Constants: rolls are `NextInt(0, 6)`
(`CombatMath.cs:14`), flee below 25% MaxHp (`CombatMath.cs:15`), drink below 50%
(`CombatMath.cs:20`), both shifted together by the bearer's quench-oil delta and clamped [0,100]
(`CombatMath.cs:85-89`, `:108-112`).

Per round, in order:
1. **Flee first** — at/below the flee line the hero leaves; no salve overrides it
   (`ExpeditionResolver.cs:508-511`).
2. **Doomed-salve flee** — if wounded and the monster's worst-case blow
   (`CouldDieNextRound`: `hp ≤ MonsterDamage(atk, 5, def)`, `CombatMath.cs:37-38`) would kill even
   after a full quaff of the first Heal in pack, flee instead of wasting it
   (`ExpeditionResolver.cs:541-554`).
3. **Quaff** — if `hp < MaxHp` AND (below the drink line OR at one-shot risk), drink the FIRST
   Heal item in pack order, capped at MaxHp, recorded as `ConsumableUse(item, round, before,
   after)`; at most one per round; no RNG (`ExpeditionResolver.cs:560-566`, `:666-690`).
4. **Hero roll** — `dealt = max(1, heroAttack + roll − monsterDefense)`; monster HP down; killed
   → the hero's WEAPON id is recorded as `KillingItem` (`ExpeditionResolver.cs:568-574`, `:620`).
5. **Monster roll** (only if alive) — `taken = max(1, monsterAttack + roll − heroDefense)`
   (`ExpeditionResolver.cs:576-583`).
6. **Apprentice warrant** — while it covers, a blow that would land hp ≤ 0 is clamped to 1 HP,
   recorded as a positive `ModifierHpDelta`; the recorded `DamageTaken` stays the true roll
   (`ExpeditionResolver.cs:594-598`; §6.8).
7. **Leech rune** — on a kill, heal `HealOnKill`, capped, recorded as `ModifierHpDelta`
   (`ExpeditionResolver.cs:604-610`). Warrant and Leech can never collide (kill vs non-kill).
8. Record the `CombatEvent` (floor, hero, monster kind, rolls, dealt, taken, killed, killing
   item, uses, modifier delta) (`ExpeditionResolver.cs:612-624`).
9. Kill → `MonsterKilled`; hp ≤ 0 → `HeroDied`; else next round.

Every roll is recorded; attribution replays from data alone and never draws
(`sim/GameSim/Contracts/Expedition.cs:20-28`).

### 6.5 Attribution — the counterfactual replay

`AttributionEngine.ComputeBeats` (`sim/GameSim/Expedition/AttributionEngine.cs:19-145`) runs once
per expedition over the merged floors, replaying per-hero HP from the recorded stream. Beats are
emitted ONLY for player-crafted items (`AttributionEngine.cs:231-232`); no participation credit.

- **KillingBlow** — a kill whose recorded `KillingItem` is player-crafted
  (`AttributionEngine.cs:59-67`).
- **LethalSave** — for each recorded hit, each player-crafted Shield and Armor is removed
  independently: `takenWithout = MonsterDamage(atk, recordedRoll, def − item.Defense)`; if the
  hero actually survived (`actualAfter > 0`) but `hpBefore − takenWithout ≤ 0`, that item earns a
  beat. Two independently-decisive items each earn one (`AttributionEngine.cs:69-96`).
- **BreakpointClear** — on a cleared floor, removing a player item from the floor-start fighters'
  Weapon/Shield/Armor (trinket excluded) that drops `PartyAveragePower` below the gate that was
  passed earns a beat (`AttributionEngine.cs:114-139`).
- **Provisioned / PotionLifesave** — one beat per hero per expedition, for the hero's FIRST
  player-marked `ConsumableUse`. It upgrades to PotionLifesave when replaying the same fight's
  recorded damage from the use's round shows `HpBefore − damage ≤ 0` while the actual trajectory
  survived (`AttributionEngine.cs:147-229`).
- **ToolAssist** — declared (`Enums.cs:63`) but has NO emitter anywhere.

### 6.6 The Evening reveal

`ExpeditionRevealSystem.Process` (`ExpeditionRevealSystem.cs:49-63`) consumes every pending result
in departure order. Fixed per-result emission order (`ExpeditionRevealSystem.cs:16-17`):
`PartyReturned`, a `DecisionExplained` naming the halt ("expedition-halt:{venue}", unconditional —
`ExpeditionRevealSystem.cs:73-82`), `HeroDied*` (death floor/cause from the hero's last recorded
combat; memorial raised), `LootIncomeReceived*` (survivors only — a dead hero's expedition gold is
lost, `:106-117`), `FloorRecordSet*` (survivors, strict improvement; DepthsBoard updated),
`VenueGraduated?`, `AttributionBeatEvent*` (KillingBlow appends "kill" and LethalSave "save" to
the item's History and the bearer's `ItemMemory`; BreakpointClear is event/gossip only,
`:170-206`), pack depletion for every recorded use (applies to the fallen too, `:212-229`), XP +
rank + level (§4.8), then `OreOffered*` — survivors' loot priced at
`MaterialRegistry.UnitPrice` (`:275-289`). `OpenOreOffers` holds exactly one Evening's market;
last night's unsold offers are swept (`:58-62`).

### 6.7 The vigil (Camp phase)

`CampHandlers` (`sim/GameSim/Expedition/CampHandlers.cs`). SendSupply guard order, each a typed
rejection (`CampHandlers.cs:52-121`): party camped with the target hero; hero not dead below;
recall not already rung; one runner per party per day (`SupplySent`); item exists; item IS a
consumable; item is the player's own — marked, not shelved, not on the rival shelf, not in any
hero's pack; fee affordable. Fee = `6 + 3·checkpointFloor` (`CampHandlers.cs:28-32`) — 9g at the
v1 floor-1 camp, deliberately above the pinned 8g salve price. The item front-inserts into BOTH
the working pack (stage 2 quaffs it first) and `Hero.Pack` (`CampHandlers.cs:126-139`). Recall
(`CampHandlers.cs:150-171`): flips `Recalled`, emits `PartyRecalled`; rejects a second ring.
Neither verb spends a slot or draws RNG. "Send them deeper" is not an action — it is the absence
of both verbs (§16.10).

### 6.8 The apprenticeship warrant

`ApprenticeWarrant` (`sim/GameSim/Expedition/ApprenticeWarrant.cs`): `Covers(state) =
Day ≤ 3 && !Concluded(state)` (`ApprenticeWarrant.cs:44` LastGraceDay=3, `:53`), where
`Concluded` scans `ActionLog` for any `ConcludeApprenticeshipAction` (`:61-62` region). While it
covers, every otherwise-lethal blow clamps to 1 HP through the `ModifierHpDelta` channel
(`:71-83` TryClamp; wired at `ExpeditionResolver.cs:594`). `FiredIn(result)` re-derives every
warrant save from the recorded stream — a `!MonsterKilled` exchange with positive
`ModifierHpDelta` (`:97+`). Both expedition ticks recompute `Covers` fresh
(`ExpeditionSystem.cs` and `ExpeditionDeepSystem.cs:275-279`).

---

## 7. Economy

### 7.1 Gold flows (the complete map)

Player gold IN: shelf sale at list (`HeroShoppingSystem.cs:389-399`), commission sale at list +
premium (`CommissionHandlers.cs:136-137` and the purse credit inside `TryFulfillFromShelf`),
counter sale at the resolved price (`sim/GameSim/Counter/HaggleResolver.cs:199+` `CloseSale`),
bounty escrow refund — dead acceptor or 3-day lapse
(`sim/GameSim/Bounties/BountySystems.cs:65-68`, `:70-77`), destitution stipend — top-up to
`max(10, cheapestPathCost)` (`sim/GameSim/Economy/DestitutionRecoverySystem.cs:316-328`).

Player gold OUT: ore purchase at the tariffed cost (`sim/GameSim/Economy/OreMarketHandlers.cs:251-259`),
vendor materials (`MaterialVendorHandlers.cs:88-97`), coal/flux (`ForgeSupplyHandlers.cs:83-92`),
bounty escrow at post time (`BountyHandlers.cs:281-287` region), runner fee
(`sim/GameSim/Expedition/CampHandlers.cs:134-139`), rent when affordable
(`sim/GameSim/Economy/RentSystem.cs:167-181`), guild dues when affordable
(`sim/GameSim/Economy/GuildAssessmentSystem.cs:114-126`), forge tier
(`ForgeTierHandlers.cs:104-114` apply region), masterwork surcharge
(`MasterworkAttemptHandlers.cs:216-221`), legendary commission
(`LegendaryCommissionHandlers.cs:354-360`).

Hero gold IN: expedition kill gold at the reveal, survivors only
(`ExpeditionRevealSystem.cs:106-117`), ore sale base ask (`OreMarketHandlers.cs:286`), bounty
reward (`BountySystems.cs:53-58`). Hero gold OUT: purchases (atomic pass, counter, commission).
The rival's purse is unmodelled; a rival sale destroys the hero's gold from the town total
(`HeroShoppingSystem.cs:401-405`). Three flows move player gold with NO event: a neutral-standing
ore purchase, a bounty escrow refund, and a forge-tier upgrade — the gold ledger takes them as
caller-fed rows (`sim/GameSim/Drama/GoldLedger.cs:34-49` doc, `DayDeltas` at `GoldLedger.cs:53`).

### 7.2 The counter (stepped Morning service)

State machine: `CounterState` (`sim/GameSim/Contracts/World.cs:52-75`) — queue, active customer,
round, Interest ‰, Patience rounds, session Goodwill ‰, presented item, standing offer, served
set, closed flag. Opening builds the queue from `CounterForecast.Queue` — alive heroes,
relationship band descending, HeroId ascending (`sim/GameSim/Drama/CounterForecast.cs:41-47`;
handler at `CounterHandlers.cs:56-84`). While open and unclosed the day holds at Morning (§1.1)
and every once-per-Morning system waits.

Present (`CounterHandlers.cs:86+`, resolution `sim/GameSim/Counter/CounterQueueSystem.cs:52-93`):
the presented item runs the SAME `ShoppingAi.EvaluateItem` verdict as the atomic pass — a Pass
verdict walks the customer immediately (`CustomerWalked` with the reason); a Buy verdict opens
round 1 via `HaggleResolver.OpenRound` (`HaggleResolver.cs:50-79`). Presenting a different item
mid-round abandons the round; re-presenting the same item is a no-op
(`CounterHandlers.cs:86+` doc).

Willingness (`sim/GameSim/Counter/WillingnessModel.cs:135-147`):

```
factor      = classFactor + Interest + MoodPermille + qualityBonus + traitPermille   (floor 100)
willingness = min(listPrice * factor / 1000, hero.Gold)
```

Class factors §4.2; quality bonus Poor −120 / Common 0 / Fine +60 / Superior +130 / Masterwork
+220 (`WillingnessModel.cs:103-111`); trait ±90 (§4.3). Interest: +150‰ opener when a Shield is
presented to a shield-bearing anchor (`WillingnessModel.cs:50`, `HaggleResolver.cs:25-27`), +80‰
upsell on a complementary empty wearable slot (`WillingnessModel.cs:54`), capped at 300‰
(`WillingnessModel.cs:58`).

The Recettear band (`WillingnessModel.cs:152-160`): round r in [1,3] →
`floor = willingness·(820 + 90·(r−1))/1000`, `ceiling = willingness·(980 + 90·(r−1))/1000`. The
customer's standing offer each round is the band FLOOR (`HaggleResolver.cs:64-79`,
HoldFirm at `:112-145`).

Responses (`HaggleResolver.cs:268-282` dispatch):

- **Accept** — the sale closes at the standing offer (the hero's own lowball).
- **HoldFirm** — consumes ONE patience round (patience starts at 3, trait ±1,
  `WillingnessModel.cs:62`, `CounterQueueSystem.cs:137-152`); at 0 the customer walks; otherwise
  the round advances (cap 3) and a new, higher offer is made. Only HoldFirm consumes patience —
  the contract doc's "each response consumes one round" (`Actions.cs:96`) does not match the code
  (§16.9).
- **Counter(price)** — ALWAYS closes the sale (all three branches call `CloseSale`,
  `HaggleResolver.cs:146-197`): above the round ceiling = fleece (session goodwill −120‰, hero
  mood −80); within ±60‰ of true willingness = pin (`Pinned: true`, mood +60); else a plain sale.
  Rejections only for a missing/non-positive price or a price above the hero's gold.

CloseSale (`HaggleResolver.cs:199+`): gold moves, gear equips / consumable packs, the mood delta
lands, the queue advances (`CounterQueueSystem.cs:104-127`) — the next customer is promoted with
reset meters (Goodwill is session-wide and never resets mid-session), `Closed` latches when the
queue runs dry. On the closing tick `HeroShoppingSystem` runs its normal pass but skips every
hero in `Served` — nobody shops twice, nobody starves (`HeroShoppingSystem.cs:23-31`). Zero RNG
anywhere in the counter (PA4, `WillingnessModel.cs:8-11`).

### 7.3 The Morning vendor

`MaterialVendorHandlers.QuoteCost` (`MaterialVendorHandlers.cs:39-43`):
`ceil(qty · unitPrice · 1.25)` — ceiling division keeps the markup alive on one unit (1 copper =
4g). Sells the whole `PricedPool`, Morning only. Shared verbatim by the destitution floor's
cheapest-path arithmetic (`DestitutionRecoverySystem.cs:250-268`).

### 7.4 The Evening ore market and factions

`BuyOreAction` (`OreMarketHandlers.cs:180-307`): matches the FIRST open offer with (hero,
materialKey); the hero must be alive; quantity ≤ offered. Pricing: the hero always receives the
base ask `qty · unitPrice`; the player pays `MulDiv(base, 1000 − adj, 1000)` where
`adj = clamp(MulDiv(standing, MaxAdjPerMille, StandingCap), ±MaxAdjPerMille)`
(`OreMarketHandlers.cs:317-322`) — positive standing means a discount, never a surcharge in the
live game (standing never goes below 0). The signed delta is recorded as `TariffApplied` only
when non-zero (`OreMarketHandlers.cs:297-304`).

A successful buy then raises the supplying faction's standing by `RiseStep`, clamped to
`StandingCap`, AFTER pricing — the discount applies to subsequent buys
(`OreMarketHandlers.cs:271-281`). Each Morning `FactionDriftSystem` steps every non-neutral
standing `DriftStep` toward 0, never past (`FactionDriftSystem.cs:29-70`, step math `:215-216`).

Voicing thresholds (`sim/GameSim/Factions/FactionStandingThresholds.cs:36-39`): favored ENTER at
cap/2, EXIT at cap·2/5 — a hysteresis deadband of cap/10; crossings are edge-triggered and emit
`FactionStandingShifted` (rise → Favored from the buy handler, fall → Cooled from drift). The
gossip generator suppresses a same-day contradictory pair for one faction
(`sim/GameSim/Drama/GossipGenerator.cs:131-143` region).

Factions (`sim/GameSim/Factions/FactionRegistry.cs:72-89` + add-on files):

| Faction | Supplies | Cap | Rise | Drift | Max adj ‰ |
|---|---|---|---|---|---|
| Deepvein Consortium | the 5 Mine ores | 100 | 5 | 2 | 100 (`FactionRegistry.cs:31-34` region) |
| Gloomwood Wardens | the 4 Gloomwood ores | 100 | 2 | 1 | 50 (`Wardens/WardensFaction.cs:49-56`) |
| Tidewrit Salvors | the 5 Crypt ores | 90 | 4 | 2 | 90 (`Tidewrit/TidewritFaction.cs:48-55`) |
| The Ashguild | the 5 Emberfall ores | 100 | 6 | 3 | 100 (`Ashguild/AshguildFaction.cs:51-58`) |
| Crownsguard Armory | electrum, orichalcum (inert — nothing mints them) | 120 | 4 | 3 | 80 (`Crownsguard/CrownsguardFaction.cs:38-45`) |

### 7.5 Rent and the Guild Assessment (two heartbeats, one confidence meter)

Rent (`RentSystem.cs:146-201`): due every 10 Mornings (`World.cs:112`), base 30g (`World.cs:115`).
Paid → next ask ×1.15; missed → no gold moves, next ask ×1.35; cap 500g; confidence +40‰ on pay /
−150‰ on miss, clamped [0,1000] (`RentSystem.cs:25-37`). A missed payment is never game-over.

Guild Assessment (`GuildAssessmentSystem.cs:79-176`): its OWN 7-day cadence (`World.cs:152`), base
dues 20g (`World.cs:155`); paid → ×1.5, missed → ×1.75, cap 800g
(`GuildAssessmentSystem.cs:54-61`). It also moves the SHARED confidence meter
(`RentState.ConfidencePermille`) every Morning: −10‰ passive decay, then per yesterday's stamped
events +80‰ per `FloorRecordSet`, +50‰ per KillingBlow/LethalSave/BreakpointClear beat, −100‰ per
`HeroDied` (`GuildAssessmentSystem.cs:42-46`, `:88-105`); +100/−50‰ on pass/miss. Threshold
consequences (`GuildAssessmentSystem.cs:144-167`): below 400‰, +60‰ rival market share EVERY
Morning (edge-triggered `RivalExpansionTriggered` once per crossing); below 200‰, one
`HeroConsideringLeaving` naming the most discontented alive hero (a warning only — nothing ever
removes a hero, `GuildAssessmentSystem.cs:184-207`); at 0, `TownConfidenceCollapsed` fires once,
latched by `GuildAssessmentState.SoftFailed` — no era-reset mechanics exist (`World.cs:138-142`).

### 7.6 The rival shop

`RivalCatalog` (`sim/GameSim/Economy/RivalCatalog.cs:146-163` — entries at file lines 55-72):
6 fixed lines, all Common, never marked — Traveler's Sword (Atk 9), Soldier's Longsword (Atk 20),
Pine Buckler (Def 6), Banded Kite Shield (Def 16), Padded Jerkin (Def 6), Riveted Hauberk
(Def 18); price = (Atk+Def)·2 (`RivalCatalog.cs:19` in-file `Price`). Hard AE3 caps — Attack ≤ 20,
shield Def ≤ 16, armor Def ≤ 18; the best all-rival loadout scores 54, below every venue's
floor-5 gate (`RivalCatalog.cs:23-33` doc region). Every Morning `RivalRestockSystem` mints any
missing line in declaration order at a price discounted by market share:
`discount‰ = share·400/1000`, floored at 1g (`sim/GameSim/Economy/RivalRestockSystem.cs:36-90`).

`MarketShareSystem` (Evening, last): a day where zero slots were spent moves
`RivalMarketSharePermille` +150 toward the rival; any real-work day −100, clamped [0,1000]
(`sim/GameSim/Economy/MarketShareSystem.cs:24-27`, apply at `:33-48`).

### 7.7 The Foundry (forge tiers, supplies, guaranteed work)

Forge tier (`ForgeTierHandlers.cs`): progress rides `Player.Materials["forge-tier-progress"]`
(`ForgeTierHandlers.cs:39`) — 0 = Forge I .. 4 = Forge V. Upgrade i costs
{400, 1600, 6400, 25600} gold (`ForgeTierHandlers.cs:46`) PLUS 25 units of Mine floor-(i+1) ore —
copper/iron/steel/mithril (`ForgeTierHandlers.cs:50-54`). Morning-only, bell-riding, no event
emitted (`ForgeTierHandlers.cs:27-32` doc).

Supplies (`ForgeSupplyHandlers.cs:37-42`): coal 4g, flux 40g, flat, Morning-only; stock rides
`Player.Materials`; emits `MaterialPurchased`.

Masterwork attempt (`MasterworkAttemptHandlers.cs`): requires Forge Tier ≥ II
(`MasterworkAttemptHandlers.cs:34`), costs 3 coal + 1 flux + the recipe's normal materials
(efficiency node honored) + 100g·(tierIndex+1) (`MasterworkAttemptHandlers.cs:37-45`). ZERO RNG:
quality = Masterwork if `materialGrade + mastery − tier ≥ 1`, else Superior
(`MasterworkAttemptHandlers.cs:229-233`). All phases legal.

Legendary commission (`LegendaryCommissionHandlers.cs`): 4 per campaign, counted in
`Player.Materials["legendary-commissions-used"]` (`LegendaryCommissionHandlers.cs:28-31`); costs
3000g·(tierIndex+1) + DOUBLE materials with no efficiency discount
(`LegendaryCommissionHandlers.cs:34-38`, `:345-347` region); mints a guaranteed Masterwork, no
roll. All phases legal, but bell-riding.

### 7.8 The destitution floor (no-softlock)

`DestitutionRecoverySystem` (`DestitutionRecoverySystem.cs:246-329`): fires only when ALL hold —
(1) gold below the cheapest guaranteed craft path (top the best-stocked pool material up to the
smallest selected-profession tier-1 recipe quantity at the vendor's own quote; cost 0 =
craftable now), (2) no stockable player craft exists (unshelved, unequipped, un-packed), (3) the
shelf is empty. Then gold tops up to `max(10, cheapestPathCost)`
(`DestitutionRecoverySystem.cs:38` floor) and `RecoveryStipendGranted` is emitted. Never fires on
a solvent trace.

---

## 8. Bounties

Posting (§2): gold escrows immediately; the floor must be 1..5 — Mine floors; bounties are
structurally Mine-scoped (`BountyHandlers.cs:20-23` guards, `ExpeditionSystem.cs:214-218`).

Judging (`sim/GameSim/Bounties/BountyRules.cs:85-109`), at the Expedition tick: every alive hero
in HeroId order weighs every unaccepted bounty; the first acceptance claims it
(`BountyRules.cs:121-142`); every judgment emits `BountyJudged` with the arithmetic spelled out
in the reason (`sim/GameSim/Bounties/BountySystems.cs:22-28` region):

```
reach gate:  TargetFloor ≤ DeepestFloorReached + 1, else decline
D_q       =  greed × RewardGold − (20 × Level) / TargetFloor     (integer division)
accept iff   D_q ≥ 10 × (10 × TargetFloor)                        (AcceptanceThreshold)
```

Greed: 10 base, Spendthrift 14, Thrifty 6 (`BountyRules.cs:33-41`, `:50-59`); reputation =
20·Level (`BountyRules.cs:46`); distance = target floor (`BountyRules.cs:68`); minimum-reward
price hint = floor × 10 (`BountyRules.cs:19`); threshold = minimum × 10 (`BountyRules.cs:80`).

An accepted bounty overrides the whole party's target floor (§6.1) and exempts the ACCEPTOR
(only) from the competence retreat through the bounty's floor (`ExpeditionSystem.cs:132-137`).

Payout (Evening, after the reveal — `BountySystems.cs:40-80` region): pays the acceptor
`RewardGold` when alive and `DeepestFloorReached ≥ TargetFloor` (emits `BountyPaid`, bounty
removed); refunds the player silently when the acceptor died (`BountySystems.cs:65-68`); refunds
silently when `Day − PostedOnDay ≥ 3` (`BountyRules.cs:14`, `BountySystems.cs:70-77`) — which
also catches an acceptor who lived but never reached the floor. `Bounty.Paid` is never set true
anywhere — paid bounties are removed instead (§16.1).

---

## 9. Drama, gossip, legends, chronicle, memorial

### 9.1 What gets recorded, and when

Everything the town remembers is a `GameEvent` in `GameState.EventLog`, stamped by the kernel
with a sequential id and the day (§1.2). The reveal's fixed emission order is §6.6. Memorials
additionally live in `DramaState.Memorials` (hero, name, day, gear summary, honored flag —
`sim/GameSim/Contracts/World.cs:11`); the gear summary puts player-crafted pieces first, "(your
make)" suffixed, or "nothing but courage" (`ExpeditionRevealSystem.cs:328-345`). The Depths
Progress board is `DramaState.DepthsBoard` (heroId → deepest floor; survivors on strict
improvement only, `ExpeditionRevealSystem.cs:119-137`).

### 9.2 Gossip

Morning, about YESTERDAY only — event ids are stamped after a system returns, so same-day gossip
would have to invent ids (`sim/GameSim/Drama/GossipSystem.cs:7-15`; day slice via `DayLog.For`,
`sim/GameSim/Drama/DayLog.cs:14-35` region). `GossipGenerator.Generate`
(`GossipGenerator.cs:60-134` region): told kinds are `HeroDied`, `AttributionBeatEvent` (every
type except ToolAssist), `FloorRecordSet`, `RecruitArrived`, `VenueGraduated`, and the hero-less
`FactionStandingShifted` (the `Describe` switch, `GossipGenerator.cs:244-265` region). Unstamped
events are refused; contradictory same-faction direction pairs are suppressed. Cap 3 lines/day
(`GossipGenerator.cs:50`), ranked by involvement (how many of yesterday's tellable events name
the subject) desc, then relationship-affinity sum desc, then EventId desc
(`GossipGenerator.cs:155-159` region). Every line is a `GossipEmitted(sourceEventId, line)` — a
line can only cite a real logged event (R14). Prose renders through `FlavorEngine` with the
protagonist's seed-derived voice; the variant pick is
`Avalanche(Mix(campaignId, eventId, HashString(key))) % variants` — campaign identity is
`GameState.Rng.Inc`, and NO RNG is drawn (`sim/GameSim/Flavor/FlavorEngine.cs:10-13`,
`sim/GameSim/Flavor/VoiceProfile.cs:130-146`, 4 frozen voices at `VoiceProfile.cs:127`).

### 9.3 The drama director

Morning, first system, exactly ONE `NextInt` per calendar day drawn UNCONDITIONALLY
(`sim/GameSim/Drama/DirectorSystem.cs:75-79`). Pacing: tension 0–1000; each day tension −40
decay + yesterday's deltas (+220 per death, +45 per floor record, +25 per party return);
BuildUp→Peak at ≥600 after ≥2 days dwell; Peak→Relax below 200 or after 4 days (min 1);
Relax→BuildUp after 2 days (`DirectorSystem.cs:119-137`, `DirectorPacing` at `:374-433`). An
incident fires at Peak if ≥3 days since the last fire, or is force-fired by 12 drought days;
firing releases 450 tension (`DirectorSystem.cs:83-99`). The catalog (5 incidents,
`DirectorSystem.cs:237-242`) gates CATEGORY by progression tier (deepest floor ever reached by
anyone, dead included: `clamp(deepest−1, 0, 3)`, `DirectorSystem.cs:146-155`) and MAGNITUDE by
survived-count — NEVER by gold (the pinned wealth-spiral invariant, `DirectorSystem.cs:22-26`).
Weighted pick 40/30/20/12/6 over the eligible subset.

Den escalation rides the same pass, RNG-free (`DirectorSystem.cs:270-343`): each live venue's
`InfectionPerMille` moves +18/day, −30 per party that returned yesterday, +120 when the fired
incident targets it, clamped [0,1000]; the tier steps at 250/500/750; `Closed` latches at 1000.
`DenThreatShifted` fires on any tier/lockdown change. **No routing or combat rule reads any of it
back** — den state is recorded drama only (`World.cs:86-89` doc; §16.4).

### 9.4 The demand board (read model)

`DemandBoard.Snapshot` (`sim/GameSim/Drama/DemandBoard.cs:88-96`): (a) `HeroPassedOnItem` reasons
from the trailing 3 days, grouped verbatim, count desc (`DemandBoard.cs:79`, `:104-130`); (b)
open commissions of ALIVE heroes only (`DemandBoard.cs:139-163`); (c) depth stalls — alive
heroes ≥2 days without a new floor record and below the Mine's top floor, naming the first empty
W/S/A slot or, with full gear, the worst carried grade vs the next floor's
`CommissionSystem.FloorMinQuality` bar (`DemandBoard.cs:84`, `:175-232`); (d) bounty price floors
(floor × 10) and open postings.

### 9.5 Legends

`LegendQuery` (`sim/GameSim/Drama/LegendQuery.cs`): a hero is "famous dead" with ≥3 attribution
beats naming them (`LegendQuery.cs:27`) OR by dying while wearing a Signed Work
(`LegendQuery.cs:37-51` region). Feeds the recruit kin-of-the-dead mood seed (§4.9) and the
ending tallies.

### 9.6 The chronicle

`ChronicleCodec` (`sim/GameSim/Chronicle/ChronicleCodec.cs:159-183` region): an export of (seed,
day, phase, heroes, full event log) to JSON — written by the CLI batch farm, read by
`tools/Analytics`. Pure codec; no rule.

### 9.7 The campaign arc

`ArcDirectorSystem` (Evening, `sim/GameSim/Arc/ArcDirectorSystem.cs:73-122`): Act I→II when any
hero has ever reached floor 3 (`ArcDirectorSystem.cs:46`); Act II→III when any hero's
`LadderRank` reaches the registry's top venue rank (2 today, derived, not pinned —
`ArcDirectorSystem.cs:52`); the Climax fires separately when a rank reaches 3 = the terminal
venue's own bottom floor fell (`ArcDirectorSystem.cs:57`, `:108-112`); the Ending fires 5 days
after the climax with derived tallies — memorials, honored count, total beats, gossip count,
legendary dead + living heroes with ≥3 beats (`ArcDirectorSystem.cs:127-144`). Acts never
regress; the world stays open after `Ended`.

### 9.8 Presentation pacing (rules only)

`PresentationScheduler` (`sim/GameSim/Presentation/PresentationScheduler.cs:55-81`): transforms a
resolved expedition into paced beats — at most 1 pull-focus and 6 glance beats per raid
(`PresentationScheduler.cs:60`, `:65`), promoted by a stakes score (death 1000, proven save 450,
killing blow 220, breakpoint 210, provisioned 150, near-miss 100 + 5·severity, +60 item debut;
pull-focus floor 400 — `PresentationScheduler.cs:72-81`); an honest near miss is a hero who
ended a round alive at ≤15% MaxHp (`PresentationScheduler.cs:57`); floors schedule strictly
ascending (no leak). All prose comes from the existing packs; variant picks are the same
StableHash contract, zero RNG. `ExpeditionNarrator` / `NarratorPack` / `TavernPack` /
`LedgerPack` / `FactionPack` are content, not rules, and are out of scope beyond that contract.

---

## 10. The advisor and the mirror

`ActionLegality` (`sim/GameSim/Advisor/ActionLegality.cs:43-78`): a deliberate second copy of
every handler's Apply-level guard chain, kept honest by a 100-day kernel-parity property test;
the switch THROWS on an unmirrored action type so silent drift is impossible
(`ActionLegality.cs:29-38`). `LegalActions` (`ActionLegality.cs:88+`) enumerates one canonical
legal instance per opportunity. `SuggestedPrice.For(item) = max(1, qualityFloor, (Atk+Def)·2,
healMagnitude·2)` with quality floors 4/8/14/22/34
(`sim/GameSim/Advisor/SuggestedPrice.cs:42-64`). `ObjectiveAdvisor.Suggest`
(`sim/GameSim/Advisor/ObjectiveAdvisor.cs:47+`): priority order — un-honored memorial (Evening),
the demand board's top open commission / depth stall, then the cheapest-productive-path fallback;
every suggestion is re-checked through `IsLegal` before being returned. `HeroForecast`
(`sim/GameSim/Advisor/HeroForecast.cs:96-111`) calls the shopping system's own
`EvaluateGearCandidates`, so the forecast can never disagree with the next real pass.

---

## 11. RNG — every stream and every draw

One stream: PCG32 (`sim/GameSim/Kernel/Pcg32.cs:11` — multiplier 6364136223846793005;
Lemire-style debiased `NextInt`, `Pcg32.cs:33-55`), state `RngState(State, Inc)` seeded by
splitmix64 expansion (`sim/GameSim/Contracts/Rng.cs:21-33`). `Inc` is the campaign-constant
stream identity — flavor uses it as the campaign id and it must never change
(`GossipSystem.cs:17-20`, `DirectorSystem.cs:13-15`). The kernel snapshots the stream on every
Tick/ApplyNow; systems receive it in registration order — draw ORDER is the determinism contract
(`sim/GameSim/Contracts/Rng.cs:4-6`, `IPhaseSystem.cs:5-9`).

Complete census of draw sites in `sim/GameSim/` (grep `rng.NextInt|rng.Roll100|rng.NextUInt`):

| Site | Draws | When |
|---|---|---|
| `DirectorSystem.Process` (`DirectorSystem.cs:78`) | 1 × `NextInt(0, totalWeight)` | once per calendar Morning, unconditionally |
| `HeroRoster.CreateRecruit` (`HeroRoster.cs:69-71`) | 3 — name, class, gold 30+`NextInt(0,31)` | per recruit minted |
| `QualityRoller.Roll` (`QualityRoller.cs:78`) | 1 × `Roll100` | per passive craft (unreachable today — §16.13) |
| `QualityRoller.RollActive` (`QualityRoller.cs:154`) | 1 × `Roll100` | per craft / heirloom reforge |
| `QualityRoller.SimulateActiveForge` (`QualityRoller.cs:248`) | 1 × `Roll100` per strike thrown | minigame-only path |
| `ExpeditionResolver.FightMonster` (`ExpeditionResolver.cs:570`, `:580`) | 1–2 per combat round (hero roll; monster roll only if the monster lives) | per round |
| `ExpeditionResolver.ResolveFloors` ore (`ExpeditionResolver.cs:379`) | 1 × `NextInt(1,4)` per standing hero per cleared floor | after each clear |

Within one day the draw order is therefore: director poll → any recruit draws → (nothing else in
Morning) → per-party stage-1 combat/ore draws in formation order → stage-2 draws at the Deep
tick → nothing at Evening. Handlers draw only through the craft roll; every rejection precedes
any draw, so a refused action never advances the stream (`CraftingHandlers.cs:13-14`).

Deterministic non-RNG "randomness": `StableHash` (FNV-1a 64 + splitmix avalanche,
`sim/GameSim/Flavor/StableHash.cs:20-95`) drives flavor variants, voices, trait derivation,
forge-path shapes, tanning patches, engineering schematics, and artifact names;
`SmithSkill.Grade` mixes Day and NextItemId (`sim/GameSim/Harness/SmithSkill.cs:57-81` region).
None of these touch the kernel stream.

---

## 12. Contracts — every shared type

`sim/GameSim/Contracts/` is deny-listed; every change lands as an orchestrator micro-PR. Reader
notes name production readers (sim + CLI + godot), never tests.

**Ids.cs** — `HeroId`, `ItemId`, `BountyId`, `EventId`: deterministic integer ids allocated by
kernel counters (`Ids.cs:4-25`).

**Enums.cs** — `DayPhase` (§1.1); `ItemSlot` (Weapon/Shield/Armor/Consumable/Trinket — trinket
content exists via alchemy/engineering recipes; heroes buy them in the gear pass but combat math
never reads trinket stats, §16.5); `HaggleResponseKind`; `QualityGrade`; `BeatType` (`ToolAssist`
has no emitter, `Enums.cs:63`); `ConsumableKind` (Heal only, `Enums.cs:70-77`); `ExpeditionHalt`
(six values, `Enums.cs:79-105`); `StandingShiftDirection`; `CampaignAct`.

**Rng.cs** — `IDeterministicRng`, `RngState` (§11).

**IPhaseSystem.cs** — `IPhaseSystem`, `IEventSink`, `IActionHandler` (`IPhaseSystem.cs:11-41`).
The kernel's private sink also implements `ITraceSink` (`sim/GameSim/Kernel/ITraceSink.cs:22-30`
region) — the only way a `DecisionTrace` reaches `TickResult.Traces`.

**ActionBudget.cs** — §1.5. The ten-action list is pinned by reflection in `ActionBudgetTests`
(`ActionBudget.cs:44-48` doc).

**Actions.cs** — the 25 `PlayerAction` types (§2) + `CraftPuzzleInput` (abstract; four derived
types registered at runtime by `SaveCodec.AddCraftPuzzlePolymorphism`,
`sim/GameSim/Kernel/SaveCodec.cs:69-93` — forgetting a registration silently breaks autosaves,
per the codec's own warning) + `RejectedAction`.

**Items.cs** — `ItemStats(Attack, Defense, Weight)` (no floats); `ModifierFamily`;
`CraftModifier(Id, Family, Tier)`; `MakersMark(CrafterName, CraftedOnDay)`;
`ItemHistoryEntry(Day, Kind, Detail)` — the literal strings "kill"/"save" are load-bearing:
`LedgerQuery.MarkTally` string-matches them (`sim/GameSim/Drama/LedgerQuery.cs:302-324` region);
`ConsumableEffect(Kind, Magnitude)`; `Item` with init members `CraftSubScores` (read by
`ArtifactSigning` + godot ProvenanceCard), `SignedName`, `HeirloomLineage` (both
presentation-only by contract), `QuenchOil`/`Rune`/`Fitting`, `Modifiers`, `PlayerCrafted`.

**Heroes.cs** — `GearSet` (+ trailing `Trinket`), `ItemMemory(Item, Kills, Saves)`, `Hero`
(§4.1).

**Expedition.cs** — `ConsumableUse` (`Expedition.cs:12`), `CombatEvent` (+ `Uses`,
`ModifierHpDelta`; `Expedition.cs:20`), `FloorOutcome`, `AttributionBeat` (`Expedition.cs:46`),
`OreLoot`, `ExpeditionResult` (+ trailing `VenueId`="mine", `Halt`=TargetReached;
`Expedition.cs:60`), `InFlightExpedition` (+ `SupplySent`, `Recalled`; carries NO RngState — the
kernel stream is the single authority; `Dead` is always empty under the v1 park invariant;
`Expedition.cs:84-104`), `Bounty` (`Expedition.cs:106`; `Paid` is constant false — §16.1).

**Player.cs** — `ShelfEntry`, `BatchEchoState` (`Player.cs:17`), `PlayerState` (gold, materials
— which also carries the two reserved counter keys `forge-tier-progress` and
`legendary-commissions-used` — per-profession talents, selected professions, shelf, nullable
`Standing`, `BatchEcho`; `Player.cs:38-45`).

**World.cs** — `Memorial` (`World.cs:11`), `DramaState` (`World.cs:14`), `LoggedBatch`
(`World.cs:26`), `CounterState` (`World.cs:52`), `VenueState` (`World.cs:94`; `DaysUntouched`
has zero readers — §16.4), `RentState` (`World.cs:109`), `GuildAssessmentState`
(`World.cs:143`), `ArcState` (+ `ClimaxDay`; `World.cs:175-190`), `GameState` (`World.cs:197` —
positional core + init members `InFlight`, `Venues`, `Counter`, `ActionSlotsRemaining`, `Rent`,
`RivalMarketSharePermille`, `Commissions`, `Director`, `Assessment`, `Arc`), `Commission`
(`World.cs:284`), `DecisionTrace` (`World.cs:321`), `TickResult` (+ `Traces` — zero production
readers, §16.3; `World.cs:328-348`).

**Director.cs** — `DirectorPhase`, `IncidentCategory`, `IncidentMagnitude`, `DirectorState`
(`Director.cs:8-91`).

**Events.cs** — 46 event types (`Events.cs:11-60` registration). Reason-carrying events:
`HeroPassedOnItem`, `HeroDecisionExplained`, `BountyJudged`, `CustomerWalked`, `HeroDied`,
`AttributionBeatEvent`, `DecisionExplained` (the generic ad hoc reason channel; first emitter is
the reveal's expedition-halt line, `Events.cs:328-354`). Conservation-reconciling records:
`TariffApplied`, `SupplyDelivered`, `MaterialPurchased`, `RecoveryStipendGranted`.
`PartyCampReport` (`Events.cs:151`) is the winch-house slate. `PartiesFormed` carries
`PartyPlan(Roster, TargetFloor, VenueId)` (`Events.cs:180`).

Save format: the serialized `GameState` IS the save (`SaveCodec.cs:105`); byte-deterministic;
every schema evolution is a trailing-optional/init member so old saves deserialize to the old
meaning (the save-shape notes at `SaveCodec.cs:10-45` region).

---

## 13. The seven laws and their tripwires

`ConstitutionTests` (`sim/GameSim.Tests/ConstitutionTests.cs:32-49`) pins the law list both ways:
exactly 7 laws (`:51`), each tag must exist in its named file (`:61-83`), no unknown `LAW:` tag
may exist (`:86-115`), every law phrase must appear in CLAUDE.md rule 12 (`:123-144`), and
CLAUDE.md must lead with the game (`:159-200`).

| Law | Tripwire file(s) | Mechanism |
|---|---|---|
| influence-never-orders | `Kernel/HeroSovereigntyCensusTests.cs` | forks a real campaign at every decision point; any verb whose `ApplyNow` moves serialized hero state must appear in a pinned 4-entry `HonestChannels` map — BuyOre, SendSupply, RecallParty, SendDeeper (`HeroSovereigntyCensusTests.cs:41-56`); channels pinned unexercised: {RecallParty, SendDeeper, SendSupply} (`:150-151`). "SendDeeper" names no action type — §16.10 |
| no-decision-timers | `Presentation/ClientAuthorityCensusTests.cs` | token census over godot scripts banning client clocks; 2 pinned exceptions, both save-file timestamps, each citing §11.7.8 (`ClientAuthorityCensusTests.cs:46-58`) |
| verbs-change-outcomes | `Balance/VerbConsequenceFloorTests.cs` | forks decision points with/without each legal verb and compares the durable-world fingerprint ticks later; asserts only the sound direction — no verb is inert every time it is offered (`VerbConsequenceFloorTests.cs:23-39`) |
| show-only-sim-decided | `Presentation/ClientAuthorityCensusTests.cs` | the same census bans client RNG (`new Random`, `GD.Rand*`) |
| sim-purity-determinism | `Kernel/SimPurityCensusTests.cs` + `Kernel/DeterminismTests.cs` | token census over every sim source banning DateTime/Stopwatch/Random/Guid/transcendental Math/float/double/decimal/Godot/network (`SimPurityCensusTests.cs:35-49`), pinned exception count **0** (`:63`); plus the golden replay |
| no-runtime-llm | `Kernel/SimPurityCensusTests.cs` | the network-token half of the same census |
| skipping-stays-legal | `Economy/NoSoftlockTests.cs` | the un-losability proof: the destitution floor fires at a true dead-end and only there (`NoSoftlockTests.cs:11-14`) |

Pinned exceptions: sim purity 0; client authority 2 (both cited). Every exception must cite
`§11.7.x` or `P<n>` — asserted mechanically (`SimPurityCensusTests.cs:72-83`).

---

## 14. The scripted player policies (Harness/)

All pure functions of `GameState` — no IO, no RNG, no clock.

**`BaselinePlayer.ActionsFor`** (`sim/GameSim/Harness/BaselinePlayer.cs:23-303`) — the balance
gate and telemetry-farm policy. Submits EXACTLY these action types:

- Morning: `UpgradeForgeAction` when legal, checked first (`BaselinePlayer.cs:42-46`); ONE
  `UnlockTalentAction` in prereq order (`:60-70`); `AcceptCommissionAction` for every open
  NON-consumable commission (`:96-103`); `StockAction` for every stockable player craft —
  consumables priced at 2·Magnitude, gear at 2·(Atk+Def), min 1 (`:126-142`).
- Expedition: at most ONE `CraftAction(recipeId, materialKey)` — highest tier then stat sum,
  legality asked of `ActionLegality`, gated by `HasBuyer` (some alive, role-compatible hero
  whose worn gear AND the unsold shelf are both weaker; consumables: someone below their trait
  stock target and no unsold Heal shelved) (`:146-183`, `:323-380`).
- Camp / ExpeditionDeep: nothing, by design (`:186-202`).
- Evening: `BuyOreAction` per affordable offer in offer order, bounded by remaining slots; skips
  materials no unlocked recipe can spend unless it is the forge ladder's own lock-and-key ore;
  stops re-buying banked ladder ore; reserves gold toward the next tier (`:204-297`).

It never submits: `PostBountyAction`, any counter action, `BuyMaterialAction`,
`SendSupplyAction`, `RecallPartyAction`, `SetProfessionsAction`, `SetPriceAction`,
`UnstockAction`, `DeclineCommissionAction`, `HonorMemorialAction`, `ReforgeHeirloomAction`,
`MasterworkAttemptAction`, `BuyForgeSupplyAction`, `CommissionLegendaryWorkAction`,
`ConcludeApprenticeshipAction`. Any plan claiming baseline coverage of bounties, the counter,
the vigil, or the Morning vendor is wrong.

**`CounterPlayer.ActionsFor`** (`sim/GameSim/Harness/CounterPlayer.cs:29-84`): Morning only —
`OpenCounterAction`; then per state: `PresentItemAction` (best role-fit shelf item),
`HaggleResponseAction(Counter, band-center)` (never Accept, never HoldFirm),
`CloseCounterAction` when nothing to present or no customer. Nothing else, no other phase.

**`MasterworkSeekingPlayer.ActionsFor`** (`sim/GameSim/Harness/MasterworkSeekingPlayer.cs:179-248`
region): the ONLY policy that constructs `UpgradeForgeAction` + `BuyForgeSupplyAction` (restock
coal/flux to 12) + `MasterworkAttemptAction` (greedy — a masterwork attempt over a hand-craft of
the same recipe whenever legal); falls back to `CraftAction`. Morning + Expedition only.

**`SkilledSmithPlayer.ActionsFor`** (`sim/GameSim/Harness/SkilledSmithPlayer.cs:34-38` region):
delegates entirely to `BaselinePlayer` and re-stamps each `CraftAction` with a `SmithSkill`
grade — Novice centre 460 spread 280, Veteran centre 850 spread 100 (`SmithSkill.cs:331`,
`:336` — verified as the `Novice`/`Veteran` definitions); the grade is a pure hash of
(Day, NextItemId).

**`ScenarioBuilder.BuildDay`** (`sim/GameSim/Harness/ScenarioBuilder.cs:415-433` region): ticks a
fresh campaign under `BaselinePlayer` to the start of day N — the deterministic save-fixture
factory.

---

## 15. The CLI as a sim driver

`sim/GameSim.Cli/Program.cs` builds the composed kernel and a `NewCampaign(seed)`
(`Program.cs:128-129`), queues actions only when `kernel.Accepts` passes
(`Program.cs:1050-1062`), and advances exclusively through `kernel.Tick(current, batch)`
(`Program.cs:1073`) — the CLI never calls `ApplyNow` (grep: no hits under `sim/GameSim.Cli/`).
`batch` runs seed sweeps under `BaselinePlayer` (default 20 seeds × 100 days, `--policy counter`
switches to `CounterPlayer`) writing chronicles to `runs/` (`sim/GameSim.Cli/BatchRunner.cs:36`,
`:41-45`). `ConsequenceProbe` (`sim/GameSim.Cli/ConsequenceProbe.cs`) owns the whole-state
fingerprint the verbs-change-outcomes law test reuses. `Characterize`
(`sim/GameSim.Cli/Characterize.cs`) prints party-power/floor tables under `BaselinePlayer`.
`DecisionLogger`/`DecisionPlay` drive the same kernel for playtest telemetry. None of these add
rules.

---

## 16. Findings — orphans, dead fields, and rules with no surface

1. **`Bounty.Paid` is constant false, and a live milestone reads it.** The only construction site
   sets `Paid: false` (`BountyHandlers.cs:48-49`); `BountyPayoutSystem` REMOVES a paid bounty
   instead of flipping the flag (`BountySystems.cs:59`); no `with { Paid = ... }` exists anywhere
   (grep over sim/ and godot/scripts). Yet `godot/scripts/ui/TutorialFlow.cs:2835` gates the
   earn-2nd-profession milestone on `state.Bounties.Any(b => b.Paid)` — a predicate that can
   never be true from sim state.
2. **The vigil is invisible to the balance corpus.** Camp offers two verbs (`SendSupply`,
   `RecallParty`) that no scripted policy ever submits (§14), so every balance number and every
   chronicle in `runs/` describes a world where the runner never leaves and no recall bell ever
   rings.
3. **`TickResult.Traces` / `DecisionTrace` have zero production readers.** The kernel collects
   them (`GameKernel.cs:232-244`); producers exist — the quality roll's shift/jitter/ceiling
   story (`QualityRoller.cs:286-288` region, `:373-378` region) and the haggle band/counter
   verdicts (`HaggleResolver.cs:243-247` region); nothing in `sim/GameSim.Cli/`,
   `godot/scripts/`, or `tools/` reads `.Traces` (grep). The reasons are computed and dropped at
   the API boundary.
4. **`VenueState.DaysUntouched` is written and never read** — reset/incremented in
   `DirectorSystem.DenStep` (`DirectorSystem.cs:304`), read by no rule and no surface (grep:
   writers only). More broadly the whole den-escalation block is recorded drama by design:
   nothing reads `InfectionPerMille`/`ThreatTier`/`Closed` back into routing or combat
   (`World.cs:86-89`) — a lockdown-latched venue still hosts raids.
5. **Trinket stats never reach combat.** `CombatMath.HeroAttack` reads Weapon only
   (`CombatMath.cs:52`); `HeroDefense` reads Shield + Armor (`CombatMath.cs:54-57`);
   `EffectivePower` (floor gates) is their sum (`CombatMath.cs:60-61`); the attribution
   breakpoint loop iterates Weapon/Shield/Armor (`AttributionEngine.cs:123`). But
   `Hero.GearScore` includes Trinket (`Heroes.cs:99`), so heroes BUY trinkets on a stat that then
   does nothing underground — only a trinket's craft modifiers act (`CraftModifiers.cs:113-128`).
   `Heroes.cs:95`'s doc ("used by shopping and floor gates") is wrong about floor gates.
6. **`CounterState.GoodwillPermille` feeds no rule.** It is decremented on a fleece
   (`HaggleResolver.cs:180-182` region) and read only by the godot CounterPanel display
   (`godot/scripts/panels/CounterPanel.cs:249-250`); the penalty a hero actually remembers is
   the direct `MoodPermille` delta. The contract doc's "the fleece memory that feeds
   Hero.MoodPermille and gossip" (`World.cs:33-35`) describes a mechanism that does not exist.
7. **Emberfall's floor 5 keeps the raw monster formula** — HP 62, Attack 35
   (`EmberfallFoundryVenue.cs:109-110` uses `12 + 10·f` / `5 + 6·f` with no floor-5 override) —
   while the Mine and the Crypt dial floor 5 down to HP 50 / Attack 26 after measurement
   (`VenueRegistry.cs:142-143`, `SunkenCryptVenue.cs:90-91` region). The campaign's climax boss
   is the one boss still at the pre-retune deadliness, behind gate 73.
8. **`BeatType.ToolAssist` has no emitter** (`Enums.cs:63`); `ConsumableKind` has only `Heal`, so
   every "utility" consumable (Transmuter's Tonic, Field Repair Kit) mechanically heals
   (`AlchemyProfession.cs:31-34`, `EngineeringProfession.cs:13`).
9. **Doc-comment vs code on haggle patience**: `Actions.cs:96` says every haggle response
   consumes one patience round; only HoldFirm does (`HaggleResolver.cs:112-145`; Accept and
   Counter close the sale immediately). Also: a Counter above the ceiling still SELLS — a fleece
   with a mood penalty, not a refusal; once a round is open the customer never walks over price,
   only over patience.
10. **"SendDeeper" is a pinned honest channel with no action type** — the sovereignty census maps
    four channels (`HeroSovereigntyCensusTests.cs:41-56`) and pins SendDeeper as never exercised
    (`:150-151`); "send them deeper" is mechanically the absence of both Camp verbs.
11. **`ProfessionHandlers`'s class doc claims it is "NOT yet wired into GameComposition"**
    (`ProfessionHandlers.cs:16-19` region) while `GameComposition.cs:84` registers it — a stale
    comment contradicting the code it sits on.
12. **`InFlightExpedition.Dead` is always empty** under the v1 park invariant (a party only parks
    when nobody died in stage 1 — `ExpeditionResolver.cs:105-113`, `Expedition.cs:90-92` doc);
    the field exists for a v2 that fights past deaths.
13. **The passive quality model is dead code on the live path** — all four professions are
    `ActiveCraft: true` (§3.1), so `QualityRoller.Roll`'s ±8 threshold table (§3.4) is reachable
    only from tests and a hypothetical passive add-on.
14. **A dead hero's wealth evaporates**: expedition gold reaches survivors only
    (`ExpeditionRevealSystem.cs:106-117`), ore offers are minted for survivors only
    (`ExpeditionRevealSystem.cs:275-289`), and a stale offer from a hero who died later is
    refused at purchase (`OreMarketHandlers.cs:205-213`).
15. **The one place the sim explains its own dice to the player is the bounty**: the full D_q
    arithmetic is printed in the `BountyJudged` reason string (`BountyRules.cs:99-108`). The
    quality roll's equivalent story (shift, jitter, ceiling clipping) exists only in
    `DecisionTrace`s nothing reads (finding 3).

---

## 17. Unverified — worth checking

Phrased as questions; none of these was mechanically verified.

- `FactionDriftSystem` has no held-Morning guard (§1.4). During a multi-tick counter Morning,
  does standing drift once per tick on purpose, or should it carry the same guard as
  Rent/Gossip/Recruit? (BaselinePlayer never opens the counter, so no gated trace exercises it.)
- `MusterSystem` re-runs `MusterPlan.Compute` and re-emits `PartiesFormed` on every held-Morning
  tick — is the event-log duplication during stepped service intended?
- Does anything intend `TutorialFlow.SecondProfessionMilestoneReached` (finding 16.1) to fire?
  If yes, the sim needs either a `Paid = true` flip before removal or a `BountyPaid`-event scan.
- If a hero somehow held two accepted commissions, `TryFulfillFromShelf` serves the
  first-in-list — is that reachable at all, given `CommissionSystem` never posts a second to a
  hero with a live one?
- In the CLI's Tick-only model, tonight's ore offers are purchasable only via actions applied at
  the NEXT Evening tick (`ExpeditionRevealSystem.cs:37-41` doc), while a Godot client using
  `ApplyNow` buys them the same Evening — is that client-model difference understood?
- `SmithSkill` deliberately excludes recipe tier, arguing NextItemId decorrelates from Day
  (`SmithSkill.cs:302-320` doc region) — has the decorrelation been measured, or only argued?
