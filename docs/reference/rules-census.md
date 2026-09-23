# Maker's Mark — the rules census

Describes the code as of commit `2a9c27ca`. Every claim carries a `file:line` into that tree.

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
`ActionTiming.ResolvesImmediately` (`sim/GameSim/Kernel/ActionTiming.cs:75-148`) is the
deny-list-by-default split deciding which verbs the client may resolve through `ApplyNow`: the
ten workshop verbs (buy material/ore/forge-supply, craft, reforge, masterwork, stock, unstock,
earmark, set-price), the five counter verbs, accept/decline commission, post bounty, send supply,
recall, unlock talent, honor memorial, place grave marker, choose remembrance, conclude
apprenticeship, and pledge dues are immediate; **exactly three verbs still ride the bell**:
`UpgradeForgeAction`, `SetProfessionsAction`,
`CommissionLegendaryWorkAction` (`ActionTiming.cs:140-147`). The Godot client uses `ApplyNow`
(`godot/scripts/SimAdapter.cs:136`); the CLI drives only `kernel.Tick` with a queued batch
(`sim/GameSim.Cli/Program.cs:1218`).

`GameKernel.Accepts` (`GameKernel.cs:30-31`) exposes the handler predicate so a UI can refuse a
phase-illegal action at input time; the CLI uses it (`Program.cs:1196-1213`).

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
  `ActionBudget.ConsumesSlot` (`ActionBudget.cs:50-60`) names exactly the TEN slot-spending
  actions: Craft, BuyOre, BuyMaterial, PostBounty, ReforgeHeirloom, BuyForgeSupply, UpgradeForge,
  MasterworkAttempt, CommissionLegendaryWork, UnlockTalent — none of the four new action types
  (Earmark, PlaceGraveMarker, ChooseRemembrance, PledgeDues) is in this list, so all four are free.
- The kernel has NO fixed campaign length. The 3-act arc (§9.7) fires an ending event
  (`CampaignEnded`) `EndingDelayDays = 5` days after the climax
  (`sim/GameSim/Arc/ArcDirectorSystem.cs:62`), and the world stays open afterward — the arc
  director simply stops (`ArcDirectorSystem.cs:77-80`). The 100-day horizon used everywhere is a
  test/telemetry convention (`sim/GameSim.Cli/BatchRunner.cs:99`), not a rule.
- A fresh campaign: day 1, Morning, 100 gold (`sim/GameSim/Kernel/GameFactory.cs:10`), blacksmith
  selected (`sim/GameSim/Contracts/Player.cs:53-62`), six fixed heroes installed
  (`GameComposition.cs:97-99`); the chosen-profession overload adds 6 starter copper
  (`GameFactory.cs:14`, `GameFactory.cs:16-37`).

---

## 2. Every player action verb

All 29 concrete `PlayerAction` types (`sim/GameSim/Contracts/Actions.cs:10-39` declares each via
`[JsonDerivedType]`; the records themselves run `Actions.cs:67-251`). "Slot" = spends
one of the day's 5 action slots (`ActionBudget.cs:50`). "Now" = resolves via `ApplyNow`
(`ActionTiming.cs:75`). Phase legality is the handler's `CanHandle`; the full Apply-level guard
chain is mirrored (deliberately duplicated) in `Advisor/ActionLegality.IsLegal`
(`sim/GameSim/Advisor/ActionLegality.cs:50-82`), whose fallthrough THROWS for an unmirrored type.

| Action | Handler | Phases | Slot | Now | Effect |
|---|---|---|---|---|---|
| `CraftAction(recipeId, materialKey, grade?, puzzle?, subScores?, oil?, rune?, fitting?)` | `CraftingHandlers` (`sim/GameSim/Crafting/CraftingHandlers.cs:63-64`) | all | yes | yes | mints an item (§4.4); one `Roll100`; emits `ItemCrafted` (+`ItemSigned` on a signing proc) |
| `StockAction(item, price)` | `ShopHandlers` (`sim/GameSim/Economy/ShopHandlers.cs:26-27`) | all | no | yes | shelf entry added; guards: exists, player-crafted, not equipped by any hero alive or dead, sold consumables never restock, one slot per item, price > 0 (`ShopHandlers.cs:44-101`) |
| `SetPriceAction(item, price)` | `ShopHandlers` | all | no | yes | reprices a shelf entry (`ShopHandlers.cs:104-125`) |
| `UnstockAction(item)` | `ShopHandlers` | all | no | yes | removes the shelf entry; the item stays in `GameState.Items` forever (`ShopHandlers.cs:127-145`) |
| `EarmarkAction(item, hero?)` | `ShopHandlers` | all | no | yes | sets or clears the ONE hero a shelved piece is held for ("hold it for Torvald", P2-PEOPLE-28); while held, ordinary shopping and commission fulfilment skip it for every other hero and the hero still decides; `hero: null` reopens it (`ShopHandlers.cs:151-189`; §4.7) |
| `BuyOreAction(from, materialKey, qty)` | `OreMarketHandlers` (`sim/GameSim/Economy/OreMarketHandlers.cs:35-36`) | Evening | yes | yes | player pays faction-tariffed cost, hero receives the base ask, materials up, standing rises (§7.4) |
| `BuyMaterialAction(materialKey, qty)` | `MaterialVendorHandlers` (`sim/GameSim/Economy/MaterialVendorHandlers.cs:45-46`) | Morning | yes | yes | vendor sells any `PricedPool` key at +25% markup, ceiling-divided (§7.3); emits `MaterialPurchased` |
| `PostBountyAction(floor, gold)` | `BountyHandlers` (`sim/GameSim/Bounties/BountyHandlers.cs:15-16`) | Morning, Evening | yes | yes | escrows gold from the purse into a `Bounty` (§8); emits `BountyPosted` |
| `UnlockTalentAction(nodeId, profession)` | `CraftingHandlers` (`ApplyUnlock`, `CraftingHandlers.cs:337-394`) | all | yes | yes | adds the node; prereqs must be held; the two smithing tier gates additionally need Forge Tier II/III (`CraftingHandlers.cs:369-377`, `sim/GameSim/Crafting/TalentTree.cs:60-65`) |
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
| `HonorMemorialAction(hero)` | `FarewellHandlers` (`sim/GameSim/Drama/FarewellHandlers.cs:19-22`) | Evening | no | yes | flips `Memorial.Honored` once; a second rite is a clean no-op, not a rejection |
| `PlaceGraveMarkerAction(hero, item)` | `FarewellHandlers` (`FarewellHandlers.cs:69-119`) | Evening | no | yes | wake verb one (P2-PEOPLE-05): sets `Memorial.MarkerItem` to a player-crafted item that is unworn, unshelved, and not already marking another grave; one marker ever; emits `GraveMarkerPlaced` |
| `ChooseRemembranceAction(hero, source)` | `FarewellHandlers` (`FarewellHandlers.cs:121-146`) | Evening | no | yes | wake verb two: sets `Memorial.Remembrance` to a logged `EventId` that truly names the hero (`RemembranceQuery.NamesHero`); one remembrance ever; emits `RemembranceChosen` |
| `ReforgeHeirloomAction(sourceItem, recipeId, materialKey)` | `HeirloomHandlers` (`sim/GameSim/Crafting/HeirloomHandlers.cs:40`) | all | yes | yes | crafts a new item stamped with `HeirloomLineage`; source must appear in some `HeroDied.WornGear` and never have been reforged (§4.8) |
| `UpgradeForgeAction` | `ForgeTierHandlers` (`sim/GameSim/Economy/ForgeTierHandlers.cs:61-62`) | Morning | yes | NO — bell | +1 forge tier for gold + 25 floor-ore (§7.7) |
| `BuyForgeSupplyAction(key, qty)` | `ForgeSupplyHandlers` (`sim/GameSim/Economy/ForgeSupplyHandlers.cs:44-45`) | Morning | yes | yes | coal 4g / flux 40g per unit, flat (§7.7) |
| `MasterworkAttemptAction(recipeId, materialKey)` | `MasterworkAttemptHandlers` (`sim/GameSim/Economy/MasterworkAttemptHandlers.cs:47`) | all | yes | yes | RNG-free guaranteed Superior (Masterwork if material outgrades recipe) for coal+flux+gold+materials (§7.7) |
| `CommissionLegendaryWorkAction(recipeId, materialKey)` | `LegendaryCommissionHandlers` (`sim/GameSim/Economy/LegendaryCommissionHandlers.cs:40`) | all | yes | NO — bell | guaranteed Masterwork; 3000g x tier, double materials, 4 per campaign (§7.7) |
| `ConcludeApprenticeshipAction` | `ConcludeApprenticeshipHandlers` (`sim/GameSim/Kernel/ConcludeApprenticeshipHandlers.cs:18-22` — accepts in every phase, mutates nothing) | all | no | yes | its entire meaning is its presence in `ActionLog`; `ApprenticeWarrant.Concluded` scans for it (§6.8) |
| `PledgeDuesAction(item)` | `PledgeDuesHandlers` (`sim/GameSim/Economy/PledgeDuesHandlers.cs:27,29`) | all | no | yes | "the pledge" (P2-LONG-18, §11.15): permanently removes a player-crafted, unequipped, unsold, unpacked item appraising at or above the current dues from `GameState.Items`, settling this cycle's Guild Assessment for 0 gold; one per cycle; emits `DuesPledged` (§7.5) |

Rejections are always typed (`RejectedAction`, `Actions.cs:254`), never silent, and every handler
checks the action-slot gate LAST so a slot-exhausted day never masks a more specific reason
(e.g. `CraftingHandlers.cs:160-168`). No handler draws RNG before all rejections are cleared
(`CraftingHandlers.cs:14-15`).

---

## 3. Crafting

### 3.1 Professions

Four registered professions (`sim/GameSim/Professions/ProfessionRegistry.cs:66-72`):
blacksmith, tanning, engineering, alchemy — ALL four have `ActiveCraft: true`
(`ProfessionRegistry.cs:51`, `sim/GameSim/Professions/Tanning/TanningProfession.cs:100`,
`sim/GameSim/Professions/Engineering/EngineeringProfession.cs:116`,
`sim/GameSim/Professions/Alchemy/AlchemyProfession.cs:119`), so every craft in the game resolves
through the active dominance roll (§3.3), never the passive table (§3.4). A save selects 1–2
professions (`ProfessionHandlers.cs:23`); only selected professions' recipes may be crafted
(`CraftingHandlers.cs:89-92`).

Each `ProfessionDefinition` (`sim/GameSim/Professions/ProfessionDefinition.cs:64-73`) carries:
recipes, an 8-node talent tree, tier gates ({2: tier-2 node, 3: tier-3 node}), a
material-efficiency node (−1 material, floor 1), a quality model whose only live field under the
active model is `MaterialMasteryNode` (+1 effective material grade), and `MinigameAssists` — the
retired quality-shift nodes remapped to per-mille forgiveness (50/70/80 chain + a slot-scoped 50:
blacksmith weapon, alchemy consumable, tanning armor, engineering trinket;
`ProfessionRegistry.cs:52-63`, `AlchemyProfession.cs:120-131`,
`TanningProfession.cs:101-112`, `EngineeringProfession.cs:117-128`).

### 3.2 Recipes and materials

48 recipes total. Blacksmith: 25 (`sim/GameSim/Crafting/RecipeTable.cs`) — 17 gear (5 weapon /
5 shield / 7 armor, tiers 1–3; P2-LONG-36 added two Tier-2/3 LIGHT armor recipes, Quilted Jack
Def 14 and Silkweave Cuirass Def 26, both weight 4 so the three light classes — mystic/occultist/
skirmisher, who could previously wear only the Tier-1 Chain Vest — have an upgrade path;
`RecipeTable.cs:88-89`), Field Salve (Heal 6), 1 Mine-ore recipe tier 4 (Mithril Warblade Atk 46 —
P2-END-01, the rung-0 answer: mithril is minted on the Mine's floor 4, gated 60, strictly below
the floor-5 gate of 70 a party must pass to graduate), 3 Gloomwood-ore recipes tier 8–9
(Gloomsteel Blade Atk 60 / Wardenweave Mail Def 50 / Moonresin Draught Heal 18), 3 Emberfall-ore recipes tier 12–14 (Cinderforge Blade Atk 90 / Ashguild
Plate Def 75 / Emberglass Draught Heal 30). Alchemy: 8
(`AlchemyProfession.cs:68-91`) — a heal ladder 6/10/15/22/30 plus a robe and two trinkets.
Tanning: 7 (`TanningProfession.cs:60-76`) — light armor/shields plus Field Poultice (Heal 5).
Engineering: 8 (`EngineeringProfession.cs:72-90`) — weapons/shield/armor/trinkets plus Field
Repair Kit (Heal 5; `ConsumableKind` has only `Heal`, so every "utility" consumable is
mechanically a heal — `sim/GameSim/Contracts/Enums.cs:70-73`,
`AlchemyProfession.cs:31-34`).

Materials are one registry (`sim/GameSim/Materials/MaterialRegistry.cs:82-110`): the five Mine
ores copper/iron/steel/mithril/adamant at price 3/5/8/12/18 and grade 1/2/3/4/5; Crypt ores
verdigris..abyss-pearl mirroring 3–18 / grade 1–5; Gloomwood greenheart/amberpitch/moonresin/
heartwood 36/42/48/54 grade 8–11; Emberfall firebrick..heartcoal 60–84 grade 12–16; plus INERT
electrum (24/6) and orichalcum (30/7) — registered, in no `PricedPool`, minted by no venue.
`PricedPool` (`MaterialRegistry.cs:94-104`) is the live surface: the vendor sells it, ore pricing
prices it, `RecipeTable.MaterialGrades` grades exactly it (`RecipeTable.cs:47-51`). A craft with a
non-pool material is rejected as an unknown material (`CraftingHandlers.cs:94-98`); the rejection
text (and every other player-facing mention) reads a `DisplayName` field (P2-HONEST-05 — Title
Case, e.g. "Drowned Silver") added to `MaterialDefinition`, never the raw kebab-case `Id`
(`sim/GameSim/Materials/MaterialDefinition.cs:18-19,33-38`).

Material cost per craft: `needed = max(1, recipe.MaterialQuantity − (efficiency node ? 1 : 0))`
(`CraftingHandlers.cs:107-113`).

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

Where `performanceGrade` comes from (`CraftingHandlers.cs:180-205`), first non-null wins:
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

`ForgeScorer.Score` (`sim/GameSim/Crafting/ForgeScorer.cs:147-280`): each sample's |y − target|
becomes a sub-score of up to 1000, falling off at `DevScale=4` per-mille per unit of deviation
(`ForgeScorer.cs:98`); zone sub-scores fold 300/400/300 (smelt/forge/quench,
`ForgeScorer.cs:134-136`); the forge zone averages sample tracking with strike tempo accuracy over
ALL strikes (`ForgeScorer.cs:229-236`). Talent assists no longer subtract from the deviation before
scoring it (owner ruling 2026-09-03, §11.7.11, superseding an earlier same-date ruling): that
subtractive shape had a dead zone where any deviation at or under the accumulated forgiveness
scored identically to a flawless trace, measured to flatten skill's effect on grade by about day 6
under heavy talent stacking. Forgiveness is now proportional — `penalty = dev · retained ·
DevScale / 1000`, `retained = 1000 − clamp(forgiveness · 3, 0, 750)` (`ForgeScorer.cs:108`
ForgivenessGain=3, `:118` MaxForgivenessPermille=750, `RetainedPermille` at `:305-306`,
`SubscoreFor` at `:314-315`) — so a strictly worse swing always scores strictly worse at every
talent level (the slope is never zero), while a fully forgiven mistake still costs at most 75% of
its raw penalty, never 100%. An untalented smith (forgiveness 0) scores by the exact old
`dev·DevScale` slope. Moments (bitflags, `ForgeScorer.cs:13-31`): ForgedInOneHeat (≤1
rising edge through y=650), NeverScorched (no y > 900), PerfectQuench (avg quench deviation < 50),
RecoveredFromTheBrink (touched a scorch >900 or a forge-zone crack <400 yet graded ≥ 550)
(`ForgeScorer.cs:121-141`, `ForgeScorer.cs:255-277`). A hand-forge with ≥1 moment writes the item's
first History entry "forged ..." (`CraftingHandlers.cs:240`, prose at
`CraftingHandlers.cs:276-286`). A Signed Work's growing History renders as one inscription via
`SignedWorkInscription.Render` — a pure read model, day-ascending, appending each entry's clause
rather than replacing the last (`sim/GameSim/Crafting/SignedWorkInscription.cs:26-41`).

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
inherits `max(800, seedGrade − 80·(uses+1))`, at most 4 echoes
(`CraftingHandlers.cs:30-58` — BatchEchoCount 4, decay 80‰/copy, floor 800 (raised from 550,
owner ruling 2026-09-03: the floor must track `QualityRoller.AutoCraftGrade`, itself raised to 800
by #583, or an echoed craft can land worse than a plain auto-craft — restoring an invariant that
broke silently when only one of the two constants moved); `CraftingHandlers.cs:191`, computed by
the shared `PendingEchoGrade` helper the Forge panel also previews from, `:58-62`; state in
`BatchEchoState`, `sim/GameSim/Contracts/Player.cs:23`). A hand-forge reseeds the memory; a new day
or different recipe silently stales it (`CraftingHandlers.cs:191`, `PendingEchoGrade`'s own match
check).

### 3.7 The other three professions' in-sim puzzle scorers

All three are pure, total, integer-only, zero-RNG; each converts its own puzzle into an integer
POINT count, then maps points to a per-mille grade through the ONE shared calibration curve,
`CraftCurve.GradeFor(points, indifferentPoints, flawlessPoints)`
(`sim/GameSim/Crafting/CraftCurve.cs:88-128`, owner ruling 2026-09-04, §11.7.12 P2-OQ11) — an
indifferent hand (the best a player gets while ignoring what the puzzle shows) anchors to
`IndifferentAnchorPermille = 450` (`CraftCurve.cs:74`, the middle of Common) and a flawless one to
1000 (Masterwork); everything between and below is linear. This replaced four unrelated per-craft
formulas after #715 measured that the blacksmith's own indifferent hand (an off-by-14%-of-the-axis
forge trace) already sat near 450, while Alchemy's raw fraction gave an indifferent pour 500,
Tanning's gave an indifferent scrape 937 (a near-free Masterwork), and Engineering's gave an
indifferent assembly 590 — so the three puzzle scorers now calibrate to the forge's own response
rather than the forge being retrofitted to them. Talent assists still sum the unlocked nodes'
three `MinigameAssist` fields into one flat per-mille bonus, slot-scoped for the specialist node,
but are now added to the curve's RESULT (never to the points), clamped to [0, 1000] — mastery
raises the floor without ever flattening the slope, the same shape §11.7.11 gave the forge (§3.5).

- **Alchemy** (`sim/GameSim/Professions/Alchemy/AlchemyPuzzleScorer.cs:119-179`): each pour
  scores `ExactPoints=2` for right-reagent-right-position, `MisplacedPoints=1` for
  right-reagent-wrong-position (multiset-aware), 0 otherwise (`:53,56`); graded via
  `CraftCurve.GradeFor(points, MisplacedPoints·length, ExactPoints·length)` — the indifferent hand
  is every reagent called for, none in place; flawless is every reagent exact (`:169-170`). Ideal
  sequences per recipe are fixed data (`AlchemyPuzzleScorer.cs:63-73`); an unlisted recipe derives
  one from an ordinal char sum, length `clamp(tier+2, 3, 5)` (`AlchemyPuzzleScorer.cs:81-108`). Six
  reagents (`sim/GameSim/Professions/Alchemy/AlchemyReagents.cs:14-24`).
- **Tanning** (`sim/GameSim/Professions/Tanning/TanningScrapeScorer.cs:139-192`): an 8x5=40-cell
  grid (`TanningScrapeScorer.cs:41-47`); 5 flaw patches (want 3–4 passes), 4 thin patches (tolerate
  exactly 1), plain cells want 1–2 (enum doc at `:76-84`, bands at `IdealPassesFor`, `:129-134`);
  patches derive from `PatchSeed` via an LCG walk (`:92-116`); perfect cell 2 pts, partially-worked
  flaw 1 pt; over-scraped cells score 0 points and count toward the `RuinPermille` sub-axis but no
  longer carry a separate grade dock — a 2026-08 revision (P2-OQ11) DELETED the additional flat
  12‰-per-cell penalty that used to apply on top of the forfeited points, because at 40 cells that
  second channel was worth about 2.4% of the scale (nothing); the forfeited points alone now cost
  roughly a full band under the shared curve (`:139-183`). Graded via `CraftCurve.GradeFor(points,
  IndifferentHandPoints, CellPoints·CellCount)` where `IndifferentHandPoints = (40−5)·2 + 5·1 = 75`
  is "one unvarying pass over the whole hide" and flawless is `2·40 = 80` (`:50-72`, `:185`).
- **Engineering** (`sim/GameSim/Professions/Engineering/EngineeringAssemblyScorer.cs:120-229`):
  sockets = `clamp(tier+2, 3, 5)` (`EngineeringAssemblyScorer.cs:56-69`); schematic derives from
  recipe id char sum, invertible mod the part count (`EngineeringAssemblyScorer.cs:71-119`); first
  placement per socket counts; exact `ExactPoints=2` pts, called-for-but-misplaced
  `MisplacedPoints=1` pt (multiset-aware); graded via `CraftCurve.GradeFor(points,
  MisplacedPoints·sockets, ExactPoints·sockets)` (`:47,50,187-195`). An order bonus — a
  consecutive ascending-socket fill run that ALSO seated the correct part in each socket — is added
  AFTER the curve, capped at `OrderBonusMaxPermille=90‰` (`:53`, run logic `:207-228`; a 2026-09
  correctness fix, still P2-OQ11: the run used to award full order credit for any ascending fill
  regardless of which part was seated, letting an all-wrong-but-tidy assembly outscore a
  strictly-better one — measured on a 4-socket recipe, 540 vs. 495).

A puzzle submitted to the wrong profession's recipe is rejected, never mis-scored
(`CraftingHandlers.cs:135-158`).

### 3.8 Minting, stats, and the mark

`ItemForge.Forge` (`sim/GameSim/Crafting/ItemForge.cs:41-64`; quality table `ItemForge.cs:20-28`): Attack and Defense scale by quality percent — Poor 80 /
Common 100 / Fine 115 / Superior 135 / Masterwork 160; Weight never scales; a consumable's Heal
`Magnitude` scales by the same table. Every player craft is stamped `MakersMark("You", day)`
(`sim/GameSim/Contracts/Items.cs:26`; stamped in `ItemForge.Forge`);
`Item.PlayerCrafted` is simply `Mark is not null` (`Items.cs:106`). Rival goods are minted with
`Mark: null` (`sim/GameSim/Economy/RivalCatalog.cs:78-86`). There is no durability on items —
nothing decays or breaks; gear persists until displaced, and a displaced item stays in
`GameState.Items` forever with its history (`sim/GameSim/Heroes/HeroShoppingSystem.cs:516-519`).

### 3.9 Craft modifiers (oils, runes, fittings)

Registry of four (`sim/GameSim/Crafting/CraftModifiers.cs:23-47`): Coward's Oil (flee threshold
+8%·tier), Braveheart Oil (−8%·tier), Leech Rune (heal 3·tier on kill), Lodestone Fitting (+1·tier
ore per loot roll) (`CraftModifiers.cs:104-107`). One modifier per family per item
(`Items.cs:13`); slots by grade — Poor 0, Common 1, Fine 2, Superior/Masterwork 3
(`CraftModifiers.cs:146-152`); tier capped by material — mithril/adamant/orichalcum tier 2, all
else 1 (`CraftModifiers.cs:134-138`); a Masterwork grants +1 tier overshoot on the FIRST fitted
modifier (`CraftModifiers.cs:156`, applied in `CraftingHandlers.cs:296-335`). Invalid or
over-budget requests are silently dropped — a craft never fails over modifiers
(`Actions.cs:58-63`). Effects aggregate once per hero per expedition over all equipped items
including trinket (`CraftModifiers.cs:113-128`).

### 3.10 Signed Works

`ArtifactSigning.Qualifies` (`sim/GameSim/Crafting/ArtifactSigning.cs:52-57` — const at
`ArtifactSigning.cs:36`): Masterwork AND exactly 3 `CraftSubScores` each ≥ 950. Only a
hand-forged Anvil-Map craft carries 3 sub-scores, so auto-crafts and purchased masterworks can
never sign. The legend name is a pure hash pick over (campaign `Rng.Inc`, item id, recipe id,
day) from a frozen 12-name pool (`ArtifactSigning.cs:43-48` pool;
`ArtifactSigning.cs` `LegendName`). `SignedName` is DATA — no sim rule keys off it beyond
`LegendQuery.DiedBearingSignedWork` (§9.5) (`Items.cs:62-66` doc).

### 3.11 Heirloom reforge

`HeirloomHandlers.Apply` (`sim/GameSim/Crafting/HeirloomHandlers.cs:43-161`): the source item must
be found in some `HeroDied.WornGear` (first match in log order) and never previously reforged
(one heirloom per fallen piece, ever — checked against `HeirloomReforged` events); then the exact
craft guard chain runs and the mint takes the auto-craft path (one `Roll100`, Superior-capped).
The new item carries `HeirloomLineage = LineageOf(sourceItemName, fallenHeroName)`, a
`"forged from the {item} of {hero}"` string built by its own extracted static method
(`HeirloomHandlers.cs:142-144`, `LineageOf` at `:175-176` — pulled out so a Godot legends-wall
preview and this handler's actual write can never drift into two different strings) —
presentation-only data (`Items.cs:72-76`).

### 3.12 Talents

Blacksmith tree (`sim/GameSim/Crafting/TalentTree.cs:39-49`): keen-eye → master's-touch →
legendary-craft; keen-eye → weapon-specialist; material-efficiency → material-mastery;
tier-2-smithing → tier-3-smithing. Unlocks cost prerequisites plus (since U-T1-9) one action slot,
and the two tier gates require Forge Tier II / III respectively (`TalentTree.cs:60-65`). The
other professions' trees mirror this 8-node shape (§3.1). There is no talent-point currency.

---

## 4. Heroes

### 4.1 The Hero record

`Hero` (`sim/GameSim/Contracts/Heroes.cs:39-108`): `Id, Name, ClassId, Level, MaxHp, Gold, Gear
(Weapon/Shield/Armor/Trinket item ids), Memories (per-item kills/saves), Alive, DeepestFloorReached,
DiedOnDay` + init members `Pack` (carried consumables, order = quaff order, `Heroes.cs:58`),
`MoodPermille` (signed, unbounded opinion of the shop; influence-only by contract,
`Heroes.cs:68`), `Xp` (`Heroes.cs:78`), `LadderRank` (monotonic dungeon-graduation count,
`Heroes.cs:93`). `Hero.GearScore` = Σ (Attack+Defense) over Weapon/Shield/Armor ONLY
(`Heroes.cs:96-115`) — Trinket is deliberately excluded (owner ruling 2026-09-03, P2-HONEST-11):
a trinket's Attack/Defense never reach combat (`CombatMath.HeroAttack`/`HeroDefense` read Weapon
and Shield+Armor only, never Trinket, `CombatMath.cs:88-99`), so summing it here used to make heroes pay gold for
stats that do nothing underground; a trinket's only real value is its craft modifier, which an
integer stat sum cannot see. Read ONLY by `ShoppingAi` (`sim/GameSim/Heroes/ShoppingAi.cs:155-156`)
— never floor gates, which use `CombatMath.EffectivePower` (`CombatMath.cs:101-109`; trinket
deliberately excluded from that sum too, a pinned honesty ruling, not an oversight). One
consequence: because
`GearSet.WithSlot` only ever changes the slot it's given, buying ANY trinket through the ordinary
gear pass (§4.4) always computes `gain = 0` (neither the before- nor after-score includes Trinket)
and is rejected `NotAnUpgrade` — a trinket can only ever reach a hero through a commission
(§4.7), which bypasses the gain gate entirely.

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

`HeroShoppingSystem.Process` (`sim/GameSim/Heroes/HeroShoppingSystem.cs:53-78`): every ALIVE hero
in ascending HeroId order runs a gear pass, then everyone runs a consumable pass, then a THIRD
pass (`FulfillServedHeroCommissions`, `:92-112`, P2-HONEST-33) walks heroes the counter already
served this Morning and fulfills any accepted commission `TryFulfillFromShelf` (§4.7) can still
reach — closing a gap where a served hero's accepted commission went unfulfilled all day (measured
0-of-1,018 fulfillments under a real policy before the fix). Strictly sequential — earlier heroes
thin the shelf for later ones. Zero RNG.

Gear pass per hero (`ShopOnce`, `HeroShoppingSystem.cs:160-220`): first, an ACCEPTED commission
short-circuit (§4.7); else evaluate every entry on BOTH shelves through `ShoppingAi.EvaluateItem`
and buy the single best `Buy` verdict by gear-score-gain per gold (cross-multiplied, ties → raw
gain → lower ItemId; `IsBetterValue`, `ShoppingAi.cs:256-272`). A player-shelf entry earmarked for
a DIFFERENT hero (`ShelfEntry.EarmarkedFor`, §2 `EarmarkAction`) is excluded from candidates before
evaluation ever runs (`CollectCandidates`/`AddShelf`, `:480-506`) — `IsHeldForSomeoneElse`
(`HeroShoppingSystem.cs:511-512`), shared verbatim by commission fulfillment (§4.7) so the two
shelf readers can never disagree about who a held piece is for. `ShoppingAi.EvaluateItem` check
order (`ShoppingAi.cs:111-188`):
1. role fit — Shield needs `AllowsShield`; weight cap per class;
2. veteran quality gate — a hero with `DeepestFloorReached ≥ 3` refuses anything below Common
   (trait-shifted ±1) (`ShoppingAi.cs:69`, `:82`, `:138-144`);
3. affordability (`price ≤ hero.Gold`);
4. storied-gear loyalty — if the worn item's memories total ≥ 3 deeds (trait-shifted), a gain
   < 5 passes Sentimental (`ShoppingAi.cs:87-91`, `:165-176`);
5. strict upgrade — gain ≤ 0 passes NotAnUpgrade.

A boycotting hero (§4.5) reads player-shelf candidates 40% pricier for RANKING only — the actual
purchase always pays the listed price (`BoycottEffectivePrice`, `HeroShoppingSystem.cs:271-280`).
Every player-shelf item looked at and not bought emits `HeroPassedOnItem` with the legible reason
(`:20-22`, `:183-205`), now distinguishing `LostToBoycott` from a plain gear-score loss
(`:289-305`) so a relationship problem can no longer be misfiled as a quality one; rival-shelf
passes stay silent. A gear buy involving the player's shelf also emits one `HeroDecisionExplained`
naming the runner-up and a per-mille gap (`RunnerUpGearCandidate`/`StampGearDecision`,
`:312-349`), and — when the winner and runner-up share a name — names what actually split them
(grade, then ranked price, then shelf, then "an identical X"; `DisambiguatedRunnerUpName`,
`:365-396`).

Consumable pass (`ShopConsumableOnce`, `HeroShoppingSystem.cs:430-478`): while `Pack.Count <` the
hero's trait stock target, buy the single cheapest affordable Heal item (player shelf wins price
ties, then lower ItemId), at most one per hero per Morning. Consumables never enter the gear pass.

Purchase application (`ApplyPurchase`, `HeroShoppingSystem.cs:514-553`): consumables append to
`Pack`; gear replaces the slot (the displaced item is simply dropped — no resale); player sales
credit the purse and clear the shelf entry; rival sales just remove the entry (the rival's gold is
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
`CommissionSystem.cs:164-170`). Gap order is survival-first: Weapon, Shield (skipped for
shield-less classes), Armor, then a Heal-in-pack presence test, then Trinket — trinket only for
Regular+ band (`FindGapSlot`, `CommissionSystem.cs:273-322`). MinQuality = max(floor bar, band
bar): floor ≥5 → Superior, ≥3 → Fine, else Common (`FloorMinQuality`, `:210-215`); band Sworn →
Superior, Patron → Fine (`BandMinQuality`, `:219-224`). Premium = 15 + 10·floor + band bonus
(Sworn 50 / Patron 25 / Regular 10) (`PremiumBonusFor`/`PremiumFor`, `:50-53`, `:233-242`).
Deadline = day + 5 (`CommissionSystem.cs:47`).

A posted commission may additionally carry PROOF: `CommissionProof.For` (`sim/GameSim/Heroes/
CommissionProof.cs:20-43`) names the player-crafted item, and the party-mate it happened for, whose
most recent legend deed (`LegendQuery.IsLegendDeed`, §9.5 — decisive, never a KillingBlow) was
earned by a piece in this slot — "one like the one that held" (P2-PEOPLE-27). A party-mate is any
hero who has ever departed in the same party (`PartyDeparted`); the asker's own deeds never count,
only what they watched happen to someone beside them. `Commission.ProvenBy`/`ProvedFor` and the
matching `CommissionPosted` fields carry the proof when found, null otherwise
(`CommissionSystem.cs:187-193`). A Trinket ask additionally appends an honesty clause,
`CommissionSystem.SlotHonestyNote` — " — a favor, not fighting gear" (`CommissionSystem.cs:335-336`,
owner ruling 2026-09-03, P2-HONEST-11) — because a trinket's stats never reach combat (§4.1); every
other slot's sentence is unchanged.

Expiry: an ACCEPTED commission past deadline emits `CommissionExpired` and −100 mood
(`ExpireCommissions`, `CommissionSystem.cs:56`, `:91-126`, accept/mood-hit at `:116-120`); a
posted-never-accepted one vanishes silently. A commission whose hero has DIED is voided FIRST,
before the deadline check — no event, no mood change, regardless of `Accepted` (`:101-108`): a
dead hero cannot give up waiting, so a death no longer fires `CommissionExpired` on a corpse days
later.

Fulfillment (`TryFulfillFromShelf`, `sim/GameSim/Heroes/CommissionHandlers.cs:82-164`): checked
BEFORE ordinary shopping; the first (lowest ItemId) player-shelf item matching slot + MinQuality +
role-fit + weight-cap, EXCLUDING any entry earmarked for a different hero
(`HeroShoppingSystem.IsHeldForSomeoneElse`, §4.4), is bought at list + premium, GUARANTEED — it
bypasses veteran/upgrade/value gates. If the hero cannot cover the full premium they pay list +
whatever premium they can (`:128`); if they cannot cover list, nothing happens this Morning. Emits
`ItemSold` + `CommissionFulfilled` and +100 mood (`FulfillMoodBonus`, `CommissionHandlers.cs:66`).

### 4.8 XP, rank, level

At the Evening reveal each survivor earns `10 + 5·deepestFloorCleared + 15·creditedBeats`
(KillingBlow/LethalSave beats naming them this run) (`sim/GameSim/Heroes/HeroXp.cs:19-30`;
applied at `sim/GameSim/Drama/ExpeditionRevealSystem.cs:313-355`, `HeroXp.ForExpedition` call at
`:338`). Rank ladder: Novice 0, Delver 50, Journeyman 150, Veteran 300, Champion 500, Legend 800
(`HeroXp.cs:44-49`); `Hero.Level` is the 1-based rank index off the SAME ladder (`HeroXp.cs:75-89`),
and Level feeds combat: `HeroAttack = classBase + 2·Level + weapon.Attack`
(`sim/GameSim/Expedition/CombatMath.cs:94`), `HeroDefense = Level + shield.Defense +
armor.Defense` (`CombatMath.cs:96-99`).

### 4.9 Death, replacement, the roster

Permadeath: `Alive` flips once at the reveal, `DiedOnDay` set, a `Memorial` raised naming the worn
gear — now all four slots including Trinket (`ExpeditionRevealSystem.cs:116-134`). Dead heroes keep their gear and records; nothing ever
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
included, `MusterSystem.cs:105`) plus a `HeroDecisionExplained` only when a bounty overrode the
default floor (`StampTargetFloorDecision`, `:126-159` — silent when target ==
clamp(deepest+1, 1, venue floors), check at `:145-149`).

`RaidForecast.ForTomorrow` (`sim/GameSim/Heroes/RaidForecast.cs:97-175`) is the "tomorrow's
telegraph" muster board (Game-Feel G4): a pure, adapter-only layer over `MusterPlan.Compute`'s own
prediction adding per-floor threat and gear-gap/worn-gear detail the sim never reads back.
`WornSlot` (`:24-30`, P2-SCREEN-18) names what a marching hero's FILLED slots hold (quality,
player-crafted, crafted day) — the upgrade arm of decision 3, alongside the existing gap arm.
`GapCommission` (`:32-43`, P2-SCREEN-36) surfaces when a hero marching with an empty slot also has
a live commission open for that exact slot — hero, slot, and deadline day only, never a premium or
a recommendation (law 12). `MissingItemSlots(Hero)` (`:190-201`, P2-HONEST-37) is class-aware, so a
shield-less class's permanently-empty Shield slot is never reported as a gap.

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
`sim/GameSim/Venues/Emberfall/EmberfallFoundryVenue.cs:76-104` (gates), `:106-114` (monster kind),
`:115-126` (HP/Attack, with the un-fixed-floor-5 comment in place). Emberfall keeps the raw
floor-5 formula — flagged P2-HONEST-08, not fixed — see §16.4.) Monster kinds derive their
article/definite/attributive forms through one shared helper, `MonsterName`
(`sim/GameSim/Venues/MonsterName.cs:27-62`): a proper-name boss (prefixed `"The "`) takes no
second article ("the Cave Rat" vs "The Forgeworm"), and `AttributiveBlow` reshapes a proper name
into a prepositional phrase ("blow from The Forgeworm") rather than "a lethal The Forgeworm hit".
The "a"/"an" choice itself is `ArticleText.Indefinite` (`sim/GameSim/Flavor/ArticleText.cs:31-32`)
— the ONE place any sim prose picks an English indefinite article, by first-letter vowel check;
every caller across the sim (class names, item slots, monster kinds) routes through it instead of
hand-writing its own "a {noun}" (P2-MEMORY-31 — measured 774 wrong articles across a 40-campaign
sweep before this existed).

Monster names: Mine Cave Rat/Tunnel Spider/Deep Ghoul/Ore Golem/The Forgeworm
(`VenueRegistry.cs`); Crypt Crab/Bog-Wight/Choir of Teeth/Reliquary Mimic/The Undertow; Bramble
Boar/Lantern Moth/The Wicker Shepherd/Old Mossjaw; Cinder Imp/Slag Hound/The Bellows-Mad/Molten
Archivist/The Undying Forge-Heart.

Routing (`sim/GameSim/Venues/VenueRouter.cs:69-96`, comparator `:99-148`): a bounty-free party
goes to the live venue chosen by a draw-free total-order comparator — eligible
(`partyRank ≥ venue.LadderRank`) beats ineligible; among eligible, HIGHEST venue rank (the
frontier); among ineligible (no rank-0 venue live), LOWEST; then fewest parties already routed
this tick; then ordinal id. A party with an accepted bounty routes straight to the Mine — bounties
carry no venue id (`sim/GameSim/Expedition/ExpeditionSystem.cs:58-80` — bounty lookup `:58-59`,
Mine-routing branch `:61-80`).

Graduation: clearing a venue's bottom floor promotes every SURVIVING member whose `LadderRank`
equals the venue's rank to rank+1 — the only write site, monotonic by construction
(`ExpeditionRevealSystem.cs:196-225`); emits `VenueGraduated`.

---

## 6. Expedition

### 6.1 Target floor

`clamp(max(party DeepestFloorReached) + 1, 1, venue.FloorCount)`, overridden by an accepted
bounty's floor (`ExpeditionSystem.cs:151-167` — `TargetFloorFor`, shared verbatim by the Morning
prediction and the authoritative tick; `RetreatExemption` at `:137-140`).

### 6.2 Staged resolution

`CheckpointFor(target) = target − CampCheckpointDepth` where `CampCheckpointDepth = 1`
(`ExpeditionSystem.cs:28`, `:36`): stage 1 resolves every floor in `[1..target-1]` at the
Expedition tick; a target of floor 1 resolves whole. This is a retune (P2-LONG-29, §11.7.5/
§11.7.13, "checkpoints scale with depth") of an older `min(1, target-1)` rule that always parked
below floor 1 regardless of target depth — a floor-5 run used to camp with floors 2–5 still
undrawn; now it resolves floors 1–4 in stage 1 and camps only before floor 5. The two rules agree
only at target floor 2. A party that clears every stage-1 floor with nobody dead and nobody too
hurt PARKS as an
`InFlightExpedition` and a `PartyCampReport` is emitted (party, camped-below floor, target, HP by
hero, Heal count by hero — `ExpeditionSystem.cs:319-331` builder); any other stage-1 ending
finalizes immediately with its raw halt (`sim/GameSim/Expedition/ExpeditionResolver.cs:70-131`).
Stage 2 (`ExpeditionResolver.cs:142-189`) resumes the identical loop at the ExpeditionDeep tick on
the live kernel stream — stage-2 rolls are provably undrawn while the party camps (the parked
record carries no RNG state; class doc, `sim/GameSim/Expedition/ExpeditionDeepSystem.cs:7-19`). A recalled party banks stage-1
clears/ore and surfaces with zero draws, halt = `Recalled` (`ExpeditionResolver.cs:162-168`).
One camp per run regardless of depth.

### 6.3 The floor loop

`ResolveFloors` (`ExpeditionResolver.cs:250-401` region), per floor in [from..to]:

1. Fighters = party minus dead minus retreated; none left → halt `PartyWiped`
   (`ExpeditionResolver.cs:299-304`).
2. **Structural gate**: `PartyAveragePower(fighters) < venue.Gate(floor)` → halt `GateHeld`, no
   roll (`ExpeditionResolver.cs:306-316`). `PartyAveragePower` = mean of
   `HeroAttack + HeroDefense` (`CombatMath.cs:102-109`).
3. Each fighter fights the floor's monster solo, in HeroId order (§6.4). A death leaves the floor
   uncleared; a flee leaves it uncleared; a kill banks `venue.GoldPerKill(floor)` into that hero's
   expedition purse (`ExpeditionResolver.cs:321-337`).
4. If cleared: post-floor drink check — any standing hero below the DRINK line (50%) quaffs
   (recorded at round = roundsFought+1), then `tooHurtToContinue` |= still below the HALT line —
   its own THIRD threshold, `CombatMath.IsTooHurtToContinue` at 30% (`TooHurtThresholdPct`,
   `CombatMath.cs:28`, method at `:58-62`), strictly between the 25% flee line and the 50% drink
   line (`ExpeditionResolver.cs:378-391`). Owner ruling 2026-09-03 (P2-LONG-25): this used to reuse
   the DRINK line itself, which fused "too hurt to press deeper" to "wounded enough to drink" and
   moved the camp's park floor from 25% to 50% — the vigil's send-supply verb (§6.7) aims at the
   `[25%,40%)` band, and a party never camped there anymore (measured: 62 deliveries under the old
   line on 2026-07-18, zero by 2026-09-02 over an identical sweep). The dedicated 30% line restores
   a party that camps genuinely hurt (empty pack, HP in the 25–30% band) without reopening the
   earlier bug where flee-first ordering made the halt unreachable at 25%.
5. Floor outcome sealed. Uncleared → halt `FloorLost` (someone still stands) or `PartyWiped`.
6. Ore loot for every standing, unretreated hero: `rng.NextInt(1,4)` + Lodestone bonus, of
   `venue.OreKey(floor)` (`ExpeditionResolver.cs:408-414`).
7. `tooHurtToContinue` → halt `TooHurt` after banking the clear.
   A `GateHeld` halt additionally records `GateReading(Floor, PartyPower, GateRequired)` on
   `ExpeditionResult.GateHeldAt` (`ExpeditionResolver.cs:264-314`, `Contracts/Expedition.cs:130`) —
   the recorded fact `GateHeldStreakQuery.ConsecutiveNights` (§9.4) and the reference smith's own
   bounty-diversion policy (§14) read back, rather than re-deriving the gate comparison.
8. **Competence retreat**: each standing hero retreats iff `nextFloor > DeepestFloorReached + 1`,
   unless she is the bounty acceptor within the bounty's floor band
   (`ExpeditionResolver.cs:397`, `:409-430`; exemption from `ExpeditionSystem.RetreatExemption`,
   `ExpeditionSystem.cs:137-140`). A retreated hero banks what she has, stays a Survivor, and fights
   no deeper floor. Stage 2 reconstructs the stage-1 retreated set by replaying the same rule
   (`ExpeditionResolver.cs:440-454`).

Halt precedence: `DeepestCleared == TargetFloor` is ALWAYS `TargetReached`, whatever exit ended
the loop (`ExpeditionResolver.cs:197-198`; enum at `sim/GameSim/Contracts/Enums.cs:79-105`).

### 6.4 One fight, in full

`FightMonster` (`ExpeditionResolver.cs:496-672` region). Constants: rolls are `NextInt(0, 6)`
(`RollSides`, `CombatMath.cs:14`), flee below 25% MaxHp (`FleeThresholdPct`, `:15`), drink below
50% (`DrinkThresholdPct`, `:20`), too-hurt-to-continue below 30% (post-floor only, §6.3 step 4;
`TooHurtThresholdPct`, `:28`), all three shifted together by the bearer's quench-oil delta and
clamped [0,100].

Per round, in order:
1. **Flee first** — at/below the flee line the hero leaves; no salve overrides it
   (`ShouldFlee`, `ExpeditionResolver.cs:541`).
2. **Doomed-salve flee** — if wounded and the monster's worst-case blow
   (`CouldDieNextRound`: `hp ≤ MonsterDamage(atk, RollSides−1, def)`, `CombatMath.cs:80`) would kill
   even after a full quaff of the first Heal in pack, flee instead of wasting it
   (`ExpeditionResolver.cs:574-587`).
3. **Quaff** — if `hp < MaxHp` AND (below the drink line OR at one-shot risk), drink the FIRST
   Heal item in pack order, capped at MaxHp, recorded as `ConsumableUse(item, round, before,
   after)`; at most one per round; no RNG.
4. **Hero roll** — `dealt = max(1, heroAttack + roll − monsterDefense)`; monster HP down; killed
   → the hero's WEAPON id is recorded as `KillingItem`.
5. **Monster roll** (only if alive) — `taken = max(1, monsterAttack + roll − heroDefense)`.
6. **Apprentice warrant** — while it covers, a blow that would land hp ≤ 0 is clamped to 1 HP,
   recorded as a positive `ModifierHpDelta`; the recorded `DamageTaken` stays the true roll
   (`ApprenticeWarrant.TryClamp`, `ExpeditionResolver.cs:627`; §6.8).
7. **Leech rune** — on a kill, heal `HealOnKill`, capped, recorded as `ModifierHpDelta`
   (`ExpeditionResolver.cs:637-643`). Warrant and Leech can never collide (kill vs non-kill).
8. Record the `CombatEvent` (floor, hero, monster kind, rolls, dealt, taken, killed, killing
   item, uses, modifier delta) (`ExpeditionResolver.cs:645-657`).
9. Kill → `MonsterKilled`; hp ≤ 0 → `HeroDied`; else next round.

Every roll is recorded; attribution replays from data alone and never draws
(`sim/GameSim/Contracts/Expedition.cs:20-28`).

### 6.5 Attribution — the counterfactual replay

`AttributionEngine.ComputeBeats` (`sim/GameSim/Expedition/AttributionEngine.cs:19-188`) runs once
per expedition over the merged floors, replaying per-hero HP from the recorded stream. Beats are
emitted ONLY for player-crafted items (`IsPlayerCrafted`, `AttributionEngine.cs:288-289`); no
participation credit.

- **KillingBlow** — a kill whose recorded `KillingItem` is player-crafted
  (`AttributionEngine.cs:66-79`).
- **LethalSave** — for each recorded hit, each player-crafted Shield and Armor is removed
  independently: `takenWithout = MonsterDamage(atk, recordedRoll, def − item.Defense)`; if the
  hero actually survived (`actualAfter > 0`) but `hpBefore − takenWithout ≤ 0`, that item earns a
  beat. Two independently-decisive items each earn one (`AttributionEngine.cs:82-117`).
- **BreakpointClear** — on a cleared floor, removing a player item from the floor-start fighters'
  Weapon/Shield/Armor (trinket deliberately excluded — `CombatMath.EffectivePower` never reads it
  either, a pinned honesty ruling P2-HONEST-11, not an oversight) that drops `PartyAveragePower`
  below the gate that was passed earns a beat (`AttributionEngine.cs:137-182`).
- **Provisioned / PotionLifesave** — one beat per hero per expedition, for the hero's FIRST
  player-marked `ConsumableUse`. It upgrades to PotionLifesave when replaying the same fight's
  recorded damage from the use's round shows `HpBefore − damage ≤ 0` while the actual trajectory
  survived (`AttributionEngine.cs:199-286`).
- **ToolAssist** — declared (`Enums.cs:63`) but has NO emitter anywhere (grepped whole
  `sim/GameSim/`; every reference is a reserved-enum comment or a caller's explicit "not yet"
  branch).

Each beat's `Decisive` flag (`AttributionBeatEvent.Decisive`, `Contracts/Events.cs:91`) is stamped
at reveal by `TellingQuery.IsDecisiveBeat`/`KillingBlowIsDecisive` — every listed type except
`KillingBlow` is always decisive; a `KillingBlow` is decisive only if the counterfactual replay
shows the monster would have survived the recorded kill round without the item. `Decisive` feeds
gossip's rank bucket (§9.2) and the fame redefinition (§9.5).

"The Telling" ("ask how it happened") stages the SAME counterfactual data as a full replay rather
than a one-line beat: `TellingQuery.Build` (`sim/GameSim/Expedition/TellingQuery.cs:179-203`)
dispatches on beat kind to five shape-builders, each rebuilding the beat's floor round-by-round
from recorded data and computing ONE counterfactual with the exact same `items.Remove(id)` pattern
`AttributionEngine` itself uses, never a parallel formula: `KillingBlowShape` (the recorded fact,
one recomputed number — what the monster's HP would read without the item's Attack, no further
rounds staged, `:233-255`); `LethalSaveShape` (replays to the exact round removing the item's
Defense would have dropped the hero to ≤0, `:348-405`); `BreakpointClearShape` (`PartyAveragePower`
recomputed with the item removed vs. the recorded gate, `:418-444`); `ProvisionedShape` (`:448-463`,
`:564-592`); `PotionLifesaveShape` — a strict round-by-round replay omitting only the target
quaff's heal, downgrading to a `MarginOnly` payload (the honest low-water mark, not a
contradiction) when a later independent quaff would have saved the hero anyway (`:467-560`). The
shared replay (`ReplayHpThroughFloor`/`ReplayHp`/`ReplayHpPerRound`, `:610-683`) is reused verbatim
by the Drama read models in §9.9. Sole production caller: `godot/scripts/panels/TellingPanel.cs`.

### 6.6 The Evening reveal

`ExpeditionRevealSystem.Process` (`ExpeditionRevealSystem.cs:78`) consumes every pending result in
departure order. Fixed per-result emission order:

0. A `DecisionExplained` naming the halt unconditionally (`"expedition-halt:{venue}"`, §11.14.8,
   `:105-115`).
1. Deaths: flip `Alive`, name the worn gear, raise a memorial (`:116-134`); ANY shelf hold
   (`ShelfEntry.EarmarkedFor`, §4.4) on this hero is released the same night — "a hold for the dead
   is released at the wake" (§9.10), emitting the same `ShelfEarmarked(item, null)` shape an
   ordinary release uses (`:135-160`).
2. Loot gold, survivors only — a dead hero's expedition gold is lost (`:162-173`).
3. Depth records, survivors who strictly beat their personal best; `VenueGraduated?` on a bottom
   floor clear (`:176-223`).
4. Attribution beats: every proven beat becomes an `AttributionBeatEvent`, stamped with its
   `Decisive` flag from `TellingQuery.IsDecisiveBeat` right here (decisiveness is decided at
   reveal because the fight itself is gone from `LastNightExpeditions` after one night, but fame
   needs a lifetime count, §9.5) — a legend deed (`LegendQuery.IsLegendDeed`) on a player-crafted
   item ALSO moves its living bearer's mood by `SavedByYourWorkMood = +40` (P2-PEOPLE-26,
   `:237-254`; gated alive so a fallen hero's mood is never moved). KillingBlow then appends "kill"
   and LethalSave "save" to the item's History and the bearer's `ItemMemory`; BreakpointClear is
   event/gossip only (`:255-283`).
5. Pack depletion for every recorded use, applies to the fallen too — the salve was drunk either
   way, emits nothing (quaffing already surfaced as a Provisioned/PotionLifesave beat, `:284-313`).
6. XP + rank + level (§4.8, `:313-352`).
7. `OreOffered*` — survivors' loot priced via `OrePricing.UnitPrice` (`:365`;
   `sim/GameSim/Drama/OrePricing.cs:21-24`, a thin delegation to `MaterialRegistry.UnitPrice` for
   anything in the frozen `PricedPool`, else throws) (`:357-371`). `OpenOreOffers` holds exactly one
   Evening's market; last night's unsold offers are swept.

### 6.7 The vigil (Camp phase)

`CampHandlers` (`sim/GameSim/Expedition/CampHandlers.cs`). SendSupply guard order, each a typed
rejection (`CampHandlers.cs:87-153`): party camped with the target hero; hero not dead below;
recall not already rung; one runner per party per day (`SupplySent`); item exists; item IS a
consumable; item is the player's own — marked, not shelved, not on the rival shelf, not in any
hero's pack; fee affordable. Fee = `SupplyFee(checkpointFloor) = 6 + 3·checkpointFloor`
(`SupplyFeeBase=6`, `SupplyFeePerFloor=3`, `CampHandlers.cs:50-56`) — 9g at a floor-2-target camp
(checkpoint 1), deliberately above the pinned 8g salve price; since §6.2's checkpoint now scales
with target depth, a deeper-target camp's fee scales with it too (e.g. target 5 → checkpoint 4 →
18g). The item front-inserts into BOTH the working pack (stage 2 quaffs it first) and `Hero.Pack`
(`CampHandlers.cs:158-171`). Recall (`ApplyRecall`, `CampHandlers.cs:182-203`): flips `Recalled`,
emits `PartyRecalled`; rejects a second ring. Neither verb spends a slot or draws RNG.
`RunnerBandPct = 40` (`CampHandlers.cs:44`, `IsInRunnerBand` at `:48`, P2-HONEST-39) names, in
code, the HP band the runner verb is actually for — ≤40% MaxHp — after a scripted policy that
aimed at the 30% halt line (§6.3/§6.4) instead delivered zero supplies against 289 camped parties,
31 of which sat in the true band. "Send them deeper" is not an action — it is the absence of both
verbs (§16). `SendSupplyAction` is now submitted by two scripted policies (`ForgeCounterPlayer`,
`ApprenticePlayer` — §14); `RecallPartyAction` remains unexercised by every policy in `Harness/`
(§16).

### 6.8 The apprenticeship warrant

`ApprenticeWarrant` (`sim/GameSim/Expedition/ApprenticeWarrant.cs`): `Covers(state) =
Day ≤ 3 && !Concluded(state)` (`ApprenticeWarrant.cs:44` LastGraceDay=3, `:53`), where
`Concluded` scans `ActionLog` for any `ConcludeApprenticeshipAction` (`:61-62` region). While it
covers, every otherwise-lethal blow clamps to 1 HP through the `ModifierHpDelta` channel
(`:71-83` TryClamp; wired at `ExpeditionResolver.cs:627`). `FiredIn(result)` re-derives every
warrant save from the recorded stream — a `!MonsterKilled` exchange with positive
`ModifierHpDelta` (`:97+`). Both expedition ticks recompute `Covers` fresh
(`ExpeditionSystem.cs:96` and `ExpeditionDeepSystem.cs:41`).

---

## 7. Economy

### 7.1 Gold flows (the complete map)

Player gold IN: shelf sale at list, commission sale at list + premium (`CommissionHandlers.cs`,
the purse credit inside `TryFulfillFromShelf`), counter sale at the resolved price
(`sim/GameSim/Counter/HaggleResolver.cs` `CloseSale`), bounty escrow refund — dead acceptor or
3-day lapse (`sim/GameSim/Bounties/BountySystems.cs:67`, `:78`; now also emits `BountyRefunded`,
§8), destitution stipend — top-up to `max(10, cheapestPathCost)`
(`sim/GameSim/Economy/DestitutionRecoverySystem.cs:118-126`).

Player gold OUT: ore purchase at the tariffed cost (`sim/GameSim/Economy/OreMarketHandlers.cs:117`),
vendor materials (`MaterialVendorHandlers.cs`), coal/flux (`ForgeSupplyHandlers.cs`), bounty
escrow at post time (`BountyHandlers.cs`), runner fee (`sim/GameSim/Expedition/CampHandlers.cs:56`
`SupplyFee`), rent when affordable (`sim/GameSim/Economy/RentSystem.cs`), guild dues when
affordable (`sim/GameSim/Economy/GuildAssessmentSystem.cs:114-133`), forge tier
(`ForgeTierHandlers.cs`), masterwork surcharge (`MasterworkAttemptHandlers.cs`), legendary
commission (`LegendaryCommissionHandlers.cs`).

Hero gold IN: expedition kill gold at the reveal, survivors only (`ExpeditionRevealSystem.cs`),
ore sale base ask, always the FULL `qty · unitPrice` regardless of tariff
(`OreMarketHandlers.cs:146` `ApplyLootIncome`), bounty reward (`BountySystems.cs`). Hero gold OUT:
purchases (atomic pass, counter, commission). The rival's purse is unmodelled; a rival sale
destroys the hero's gold from the town total (`HeroShoppingSystem.cs`). Three flows move player
gold with NO event: a neutral-standing ore purchase, a bounty escrow refund, and a forge-tier
upgrade — the gold ledger takes them as caller-fed rows (`sim/GameSim/Drama/GoldLedger.cs:13-42`
doc, `DayDeltas` at `GoldLedger.cs:54`). A FOURTH zero-event flow now exists deliberately: a
Guild Assessment cycle settled by `PledgeDuesAction` (§7.5) moves no gold at all, and
`GoldLedger.cs:93-99` documents why it writes no row rather than a misleading 0g line.

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

Responses (`HaggleResolver.cs:96-107` dispatch):

- **Accept** — the sale closes at the standing offer (the hero's own lowball), with NO mood delta
  at all (`HaggleResolver.cs:98-99` passes no `moodDelta`, so `CloseSale`'s default of null leaves
  `Hero.MoodPermille` untouched) — distinct from a Counter landing in the same band, which earns
  `FairDealMood` (below).
- **HoldFirm** — consumes ONE patience round (patience starts at 3, trait ±1,
  `WillingnessModel.cs:62`, `CounterQueueSystem.cs:137-152`); at 0 the customer walks; otherwise
  the round advances (cap 3) and a new, higher offer is made. Only HoldFirm consumes patience —
  the contract doc's "each response consumes one round" (`Actions.cs:103`) does not match the code
  (§16.6).
- **Counter(price)** — ALWAYS closes the sale (all three branches call `CloseSale`,
  `HaggleResolver.cs:146-197`): above the round ceiling = **fleece** (session Goodwill −120‰ flat,
  `FleeceGoodwillPenaltyPermille`; hero mood scaled by `WillingnessModel.FleeceMoodDelta` from −1
  at the ceiling up to a cap of −80 once the excess over the ceiling reaches 60‰ of true
  willingness, `WillingnessModel.cs:221-233`); within ±60‰ of true willingness = **pin**
  (`Pinned: true`; mood scaled by `PinMoodDelta` from `FairDealMood+1`=5 at zero gap up to a cap of
  60 at the pin window's edge, `WillingnessModel.cs:205-219`); else a **plain sale** at a flat
  `FairDealMood` = +4 (`WillingnessModel.cs:103`). Owner ruling 2026-09-03 (P2-PEOPLE-29): mood used
  to be a flat +60/−80 regardless of how close the read was, and a plain in-band sale paid nothing
  at all (0 mood on 477 measured closes, one live arm of decision 2); scaling both and giving the
  honest middle its own small reward gives "price for the sale or the relationship" a second live
  arm. Rejections only for a missing/non-positive price or a price above the hero's gold.

CloseSale (`HaggleResolver.cs:204-229`): gold moves, gear equips / consumable packs, the mood delta
lands, the queue advances (`CounterQueueSystem.cs:104-133`) — the next customer is promoted with
reset meters (Goodwill is session-wide and never resets mid-session), `Closed` latches when the
queue runs dry. Only a hero who actually BOUGHT something joins `Served`
(`CounterQueueSystem.Advance`'s `bought` parameter, `CounterQueueSystem.cs:104-133`) — owner ruling
P2-HONEST-42: marking a WALK as served used to bar that hero from the Morning atomic pass too
(both the player's and the rival's shelf), measured starving the town — 5,449 of 5,655 walks (96%)
bought nothing and were then locked out, six of 20 campaigns never reached an ending, and
`GateHeld` halts ran 45% of all halts against a 24% baseline. A hero who walks the counter still
gets their ordinary shopping chance the same Morning; `HeroShoppingSystem`'s closing-tick pass
skips only the Served BUYERS (`HeroShoppingSystem.cs:23-31`). Zero RNG anywhere in the counter
(PA4, `WillingnessModel.cs:8-11`).

### 7.3 The Morning vendor

`MaterialVendorHandlers.QuoteCost` (`MaterialVendorHandlers.cs:39-43`):
`ceil(qty · unitPrice · 1.25)` (`VendorMarkupPermille = 250`, `:31`) — ceiling division keeps the
markup alive on one unit (1 copper = 4g). Sells the whole `PricedPool`, Morning only. Shared
verbatim by the destitution floor's cheapest-path arithmetic (`DestitutionRecoverySystem.cs:58-66`).

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
(`OreMarketHandlers.cs:131-141`). Each Morning `FactionDriftSystem` steps every non-neutral
standing `DriftStep` toward 0, never past (`FactionDriftSystem.cs:29-65`, step math `:77-78`).

Voicing thresholds (`sim/GameSim/Factions/FactionStandingThresholds.cs:29,32`): favored ENTER at
cap/2, EXIT at cap·2/5 — a hysteresis deadband of cap/10; crossings are edge-triggered and emit
`FactionStandingShifted` (rise → Favored from the buy handler, fall → Cooled from drift). The
gossip generator suppresses a same-day contradictory pair for one faction.

Factions (`sim/GameSim/Factions/FactionRegistry.cs:37-42` + add-on files):

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
(`GuildAssessmentSystem.cs:54-61`). A THIRD settlement path (P2-LONG-18, §11.15, "the pledge"):
`PledgeDuesAction(item)` (`sim/GameSim/Contracts/Actions.cs:236-251`, handled by
`PledgeDuesHandlers`, free, every phase, `ApplyNow` — `ActionTiming.cs`) permanently removes one
player-crafted, unequipped, unsold, not-in-any-pack item from `GameState.Items` (and the shelf) in
exchange for covering the CURRENT cycle's dues, guarded (in order) on existence, `PlayerCrafted`,
not worn, not sold, not packed, `SuggestedPrice.For(item) ≥ assessment.DuesGold` (no change given),
and at most one pledge per cycle (`PledgeDuesHandlers.cs:43-113`). At the next Guild Assessment
tick, a live pledge for the current cycle (found by scanning the log back to the last
`GuildAssessmentPassed`/`GuildAssessmentMissed`/`DuesSettledByPledge`, `PledgeDuesHandlers.cs:115-145`)
is checked BEFORE the coin-payment branch and settles on the exact on-time track — ×1.5 escalation,
+100‰ confidence, `AssessmentsPassed` incremented, `DuesPaidGold` 0 — emitting `DuesSettledByPledge`
instead of `GuildAssessmentPassed` (`GuildAssessmentSystem.cs:114-133`). The pledge is deliberately
NOT a seventh decision: it is decision 1 ("sell the good one or hold it") at a third destination —
shelf, counter, or the guild wall — and the traded piece can never reach a hero, so it can never
earn a beat. It also moves the SHARED confidence meter
(`RentState.ConfidencePermille`) every Morning: −10‰ passive decay, then per yesterday's stamped
events +80‰ per `FloorRecordSet`, +50‰ per KillingBlow/LethalSave/BreakpointClear beat, −100‰ per
`HeroDied` (`GuildAssessmentSystem.cs:42-46`, `:88-105`); +100/−50‰ on pass/miss. Threshold
consequences (`GuildAssessmentSystem.cs:144-167`): below 400‰, +60‰ rival market share EVERY
Morning (edge-triggered `RivalExpansionTriggered` once per crossing); below 200‰, one
`HeroConsideringLeaving` naming the most discontented alive hero (a warning only — nothing ever
removes a hero, `GuildAssessmentSystem.cs:184-207`); at 0, `TownConfidenceCollapsed` fires once,
latched by `GuildAssessmentState.SoftFailed` — no era-reset mechanics exist (`World.cs:138-142`).

### 7.6 The rival shop

`RivalCatalog` (`sim/GameSim/Economy/RivalCatalog.cs:43-87` — entries at file lines 55-72):
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
emitted.

Supplies (`ForgeSupplyHandlers.cs:37-42`): coal 4g, flux 40g, flat, Morning-only; stock rides
`Player.Materials`; emits `MaterialPurchased`.

Masterwork attempt (`MasterworkAttemptHandlers.cs`): requires Forge Tier ≥ II
(`RequiredForgeTierIndex = 1`, `:35`), costs 3 coal (`CoalCost`, `:38`) + 1 flux (`FluxCost`, `:41`)
+ the recipe's normal materials (efficiency node honored) + 100g·(tierIndex+1)
(`GoldSurchargePerTier = 100`, `:46`). ZERO RNG: quality = Masterwork if
`materialGrade + mastery − tier ≥ 1`, else Superior (`:136-137`). All phases legal.

Legendary commission (`LegendaryCommissionHandlers.cs`): `MaxPerCampaign = 4` (`:32`), counted in
`Player.Materials["legendary-commissions-used"]`; costs `BaseGold = 3000` (`:35`) ·(tierIndex+1) +
`MaterialMultiplier = 2` (`:39`) materials with no efficiency discount (`:89-91`, `:100-101`);
mints a guaranteed Masterwork, no roll (`:113-134`). All phases legal, but bell-riding.

### 7.8 The destitution floor (no-softlock)

`DestitutionRecoverySystem` (`DestitutionRecoverySystem.cs`, `Process` at `:44-127`): fires only when ALL hold —
(1) gold below the cheapest guaranteed craft path (top the best-stocked pool material up to the
smallest selected-profession tier-1 recipe quantity at the vendor's own quote; cost 0 =
craftable now), (2) no stockable player craft exists (unshelved, unequipped, un-packed), (3) the
shelf is empty. Then gold tops up to `max(10, cheapestPathCost)`
(`DestitutionRecoverySystem.cs:38` floor) and `RecoveryStipendGranted` is emitted. Never fires on
a solvent trace.

---

## 8. Bounties

Posting (§2): gold escrows immediately; the floor must be 1..5 — Mine floors; bounties are
structurally Mine-scoped (`BountyHandlers.cs:23-25` guards, "the Mine IS the map" doc comment at
`ExpeditionSystem.cs:61-63`).

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
`AcceptanceThreshold`'s own doc comment (`BountyRules.cs:76-88`, §11.19 measurement 1, P2-HONEST-49)
corrects an earlier claim that a neutral level-1 hero posted at exactly `MinimumReward` clears the
bar: any hero above level 0 has nonzero reputation, which only ever subtracts, so a level-1 hero at
floor 1 scores `10×10 − 20/1 = 80` against a threshold of 100 and declines — measured in
production, 316 of 407 judgments (78%) declined a `MinimumReward` post with "is too thin." Posting
a reward that actually clears the bar for a real hero is `ForgeCounterPlayer.MinAcceptableReward`'s
job (§14), not this method's.

An accepted bounty overrides the whole party's target floor (§6.1) and exempts the ACCEPTOR
(only) from the competence retreat through the bounty's floor (`RetreatExemption`,
`ExpeditionSystem.cs:137-140`).

Payout (Evening, after the reveal — `BountySystems.cs:37-87` region): pays the acceptor
`RewardGold` when alive and `DeepestFloorReached ≥ TargetFloor` (emits `BountyPaid`, bounty
removed); refunds the player when the acceptor died (`BountySystems.cs:67`) or when
`Day − PostedOnDay ≥ 3` (`BountyRules.cs:14`, `BountySystems.cs:78`) — which also catches an
acceptor who lived but never reached the floor — and now (P2-MEMORY-15) emits a
`BountyRefunded(BountyId, RewardGold, BountyRefundReason, HeroId? AcceptedBy)` naming which of the
two (`AcceptorDied` / `Lapsed`, `sim/GameSim/Contracts/Enums.cs:80-84`) on both branches; a refund
is no longer silent. `Bounty.Paid` is never set true anywhere — paid AND refunded bounties are
both removed from the open list instead (§16).

---

## 9. Drama, gossip, legends, chronicle, memorial

### 9.1 What gets recorded, and when

Everything the town remembers is a `GameEvent` in `GameState.EventLog`, stamped by the kernel
with a sequential id and the day (§1.2). The reveal's fixed emission order is §6.6. Memorials
additionally live in `DramaState.Memorials` (hero, name, day, gear summary, honored flag, plus the
two wake fields `MarkerItem`/`Remembrance` — §9.10 — `sim/GameSim/Contracts/World.cs:16`); the
gear summary puts player-crafted pieces first, "(your make)" suffixed, or "nothing but courage"
(`ExpeditionRevealSystem.cs`). The Depths Progress board is `DramaState.DepthsBoard` (heroId →
deepest floor; survivors on strict improvement only). `GameState.LastNightExpeditions`
(`World.cs:241-242`) keeps last night's revealed `ExpeditionResult`s readable after the reveal
consumes the pending queue, specifically so the read models in §9.9/§9.11 can still narrate a
fight after the fact.

### 9.2 Gossip

Morning, about YESTERDAY only — event ids are stamped after a system returns, so same-day gossip
would have to invent ids (`sim/GameSim/Drama/GossipSystem.cs:7-15`; day slice via `DayLog.For`).
`GossipGenerator.Generate` (`GossipGenerator.cs:60-134` region): told kinds are `HeroDied`,
`AttributionBeatEvent` (every type except `ToolAssist`), `FloorRecordSet`, `RecruitArrived`,
`VenueGraduated`, the hero-less `FactionStandingShifted`, and — added since — `CounterSaleClosed`
(voiced by Fleeced/Pinned/plain), `CommissionFulfilled`, `CommissionExpired`, and the hero-less
`HeirloomReforged` (subject is the lineage string; `Describe`, `GossipGenerator.cs:364-399`).
Unstamped events are refused; contradictory same-faction direction pairs are suppressed. A new
dedup rule (P2-MEMORY-24) caps AT MOST ONE `KillingBlow` line per item per day (earliest by
EventId kept, `GossipGenerator.cs:120-137`) — one item's kills used to BE 31–87% of a seed's
gossip. Cap 3 lines/day (`MaxLinesPerDay = 3`, `GossipGenerator.cs:63`), ranked by a "what the
tavern would actually talk about" bucket FIRST (P2-MEMORY-24, ascending: death=0, lethal
save/potion life-save=1, floor record/venue graduation/breakpoint clear=2, decisive killing
blow/fleeced sale/commission expired/heirloom reforged/commission fulfilled=3, incidental killing
blow/plain-or-pinned sale=4 — `Rank`, `GossipGenerator.cs:89-114`), THEN — as the tie-break within
a bucket — involvement (how many of yesterday's tellable events name the subject) desc, then
relationship-affinity sum desc, then EventId desc (`GossipGenerator.cs:249-254`). "Decisive" reads
`AttributionBeatEvent.Decisive` live (§9.5, §9.11). Every line is a `GossipEmitted(sourceEventId,
line)` — a line can only cite a real logged event (R14). Prose renders through `FlavorEngine` with
the protagonist's seed-derived voice; the variant pick is
`Avalanche(Mix(campaignId, eventId, HashString(key))) % variants` — campaign identity is
`GameState.Rng.Inc`, and NO RNG is drawn (`sim/GameSim/Flavor/FlavorEngine.cs:69-70`, 4 frozen
voices at `sim/GameSim/Flavor/VoiceProfile.cs:127`).

### 9.3 The drama director

Morning, first system, exactly ONE `NextInt` per calendar day drawn UNCONDITIONALLY
(`sim/GameSim/Drama/DirectorSystem.cs:85`). Pacing (unchanged): tension 0–1000; each day tension
−40 decay + yesterday's deltas (+220 per death, +45 per floor record, +25 per party return);
BuildUp→Peak at ≥600 after ≥2 days dwell; Peak→Relax below 200 or after 4 days (min 1);
Relax→BuildUp after 2 days (`DirectorSystem.cs:126-144`, `DirectorPacing` at `:430-488`). An
incident fires at Peak if ≥3 days since the last fire, or is force-fired by 12 drought days;
firing releases 450 tension. The catalog now holds 11 incidents, not 5 (`Catalog`,
`DirectorSystem.cs:284-295`): the original 5 Mine incidents (weights 40/30/20/12/6, unchanged)
plus a Rumor and a Notable/Skirmish pair (weights 20/10) for each of Gloomwood, Sunken Crypt, and
Emberfall Foundry. `IsEligible` (`DirectorSystem.cs:206-209`) gates on THREE things, not two:
progression tier (deepest floor ever reached by anyone, dead included), survived-count, and — new
(P2-MEMORY-27) — `RaidedVenues` membership (`:212-234`): a non-Mine incident stays ineligible
until some party has ever mustered for that venue (read off logged `PartiesFormed.VenueId`); the
Mine is exempt so day 1's weight table is never empty. MAGNITUDE is still gated by survived-count,
NEVER by gold (the pinned wealth-spiral invariant). Weighted pick over the eligible subset.

Den escalation rides the same pass, RNG-free (`DirectorSystem.cs:326-344`): each live venue's
`InfectionPerMille` moves `DenDailyIncrement = 5`/day, `−DenClearRelief = 100` per party that
CLEARED its target last night (P2-HONEST-32 redefined "clear" to `ExpeditionHalt.TargetReached`
only — `ClearsLastNight`, `:407-419` — counting wipes/flights/gate-turns as clears had buried the
daily increment: threat never left tier 0 except on surge days), `+DenIncidentSurge = 55` when the
fired incident targets it, clamped [0,1000]; the tier steps at 250/500/750; `Closed` latches at
1000; `VenueState.DaysUntouched` resets to 0 on any clear, else +1 (`DenStep`, `:352-357`).
`DenThreatShifted` fires on any tier/lockdown change. **No routing or combat rule reads any of it
back** — den state is recorded drama only (`World.cs:86-89` doc; §16).

### 9.4 The demand board (read model)

`DemandBoard.Snapshot`: (a) `HeroPassedOnItem` reasons from the trailing `PassReasonWindowDays = 3`
days, grouped verbatim, count desc (`DemandBoard.cs:79`, `:104-130`); (b) open commissions of
ALIVE heroes only (`DemandBoard.cs:139-163`); (c) depth stalls — alive heroes ≥`StallThresholdDays
= 2` days without a new floor record and below the Mine's top floor, naming the first empty W/S/A
slot (a P2-HONEST-37 fix now scans the HERO-AWARE `RaidForecast.MissingItemSlots(hero)` overload,
not the gear-only one — the old scan named "missing Shield" for classes that can never hold one,
96% of Shield-blocking stalls were false) or, with full gear, the worst carried grade vs the next
floor's `CommissionSystem.FloorMinQuality` bar (`DemandBoard.cs:175-237`); (d) bounty price floors
(floor × 10) and open postings (`:265-291`).

### 9.5 Legends

`LegendQuery` (`sim/GameSim/Drama/LegendQuery.cs`): a hero is "famous dead"
(`IsFamousDead`, `:78-79`) with `LegendDeedCount ≥ FamousBeatThreshold` (3, `:27`) OR by dying
while wearing a Signed Work (`DiedBearingSignedWork`, `:57-74`). `LegendDeedCount` (`:54-55`)
counts only DEEDS (P2-MEMORY-23): a beat where `AttributionBeatEvent.Decisive` is true AND
`Beat != BeatType.KillingBlow` (`IsLegendDeed`, `:50`) — the raw `AttributionBeatCount` (every
beat, decisive or not, `:34-35`) is kept only as an honest "every beat logged" stat that fame no
longer reads. Rationale in the file's own doc comment: counting kills, any hero was famous by day
5 of a 20-seed×100-day measurement (~2,200 kills/campaign, 96% "decisive" by the counterfactual
test); excluding kills, first fame arrives day 16 (median), and famous-dead memorials dropped from
28% to 13%. `Decisive` is stamped at the Evening reveal by `Expedition.TellingQuery.IsDecisiveBeat`
(§9.11) — a `KillingBlow` is decisive iff the counterfactual replay shows the monster would have
survived the recorded kill without the item; every other listed beat type is always decisive.
Feeds the recruit kin-of-the-dead mood seed (§4.9) and the ending tallies (§9.7).

### 9.6 The chronicle

`ChronicleCodec` (`sim/GameSim/Chronicle/ChronicleCodec.cs`): an export of (seed, day, phase,
heroes, full event log) to JSON — written by the CLI batch farm, read by `tools/Analytics`. The
`ChronicleData` record now also carries a trailing `int? EndedOnDay = null`
(P2-HONEST-46) — the day the Ending (§9.7) actually fired for this campaign, read straight off
`state.EventLog.OfType<CampaignEnded>().FirstOrDefault()?.Day`, null if it had not fired within
the exported window. Still pure codec; no rule.

A separate, in-game "closing chapter" lives in `ChronicleComposer`
(`sim/GameSim/Chronicle/ChronicleComposer.cs:15`, P2-MEMORY-13) — link 5 said about the PLAYER
rather than a hero. Fifteen declared predicates (`Declared`, `:45`), each a plain-integer fact the
event log can prove (e.g. every counter sale this campaign was pinned, never a fleece); `Compose`
(`:80`) evaluates all fifteen, keeps every one that fired, ranks by `Specificity`
(`HeroAndNumber` > `NumberOnly` > `Neither`) then declaration order, takes the top 3, and appends
one fixed, never-scored `Closer` line. Sole production reader: `godot/scripts/panels/
LegendsWall.cs`, which renders it as the wall's own closing three-plus-closer lines — distinct
from `ChronicleCodec`'s JSON export, and drawing on the same `GameState` `ChronicleCodec` never
needs to touch.

### 9.7 The campaign arc

`ArcDirectorSystem` (Evening, `sim/GameSim/Arc/ArcDirectorSystem.cs:73-122`): Act I→II when any
hero has ever reached floor 3 (`ArcDirectorSystem.cs:46`); Act II→III when any hero's
`LadderRank` reaches the registry's top venue rank (2 today, derived, not pinned —
`ArcDirectorSystem.cs:52`); the Climax fires separately when a rank reaches 3 = the terminal
venue's own bottom floor fell (`ArcDirectorSystem.cs:57`, `:108-112`); the Ending fires
`EndingDelayDays = 5` days after the climax with derived tallies — memorials, honored count, total
beats (still the raw `AttributionBeatEvent` count), gossip count, legendary dead + living heroes
with `LegendQuery.LegendDeedCount ≥ 3` (§9.5 — corrected from a raw beat count,
`ArcDirectorSystem.cs:127-144`). Acts never regress (`Ended` short-circuits at the top of
`Process`); the world stays open after `Ended`. The 100-day horizon claim in §1.5 remains true:
neither `ChronicleData.EndedOnDay` nor anything else here introduces a fixed-length rule — the
Ending is still purely event-driven (Climax + a fixed delay that is itself driven by hero
progression, not by day count).

### 9.8 Presentation pacing (rules only)

`PresentationScheduler` (`sim/GameSim/Presentation/PresentationScheduler.cs`): transforms a
resolved expedition into paced beats — at most `MaxPullFocus = 1` and `MaxGlance = 6` beats per
raid (`:60`, `:65`), promoted by a stakes score (`BeatStakes`, `:87-93`: death 1000, proven save
450, killing blow 220, breakpoint 210, provisioned 150, near-miss 100 + 5·severity, +60 item
debut; `PullFocusStakesFloor = 400`, `:72-81`); an honest near miss is a hero who ended a round
alive at ≤`NearMissHpPercent = 15`% MaxHp (`:57`); floors schedule strictly ascending (no leak). A
day-1 carve-out (U8, `AllocateBudget`, `:199-207`/`:223-230`): on day 1 specifically, ANY
attribution-beat candidate — even a plain killing blow well under the stakes floor — is promoted
to pull-focus if nothing else already claimed the slot, since no player-crafted item can exist
before day 1 and this is necessarily the player's first-ever proof. The stakes ladder is exposed
as `StakesFor` (`:100-105`) specifically so `WakeQuery.DefaultRemembrance` (§9.10) can rank a
memorial's candidate events by the exact same ladder — one stakes judgment, never a second one
invented at the wake. All prose comes from the existing packs; variant picks are the same
StableHash contract, zero RNG. `ExpeditionNarrator` / `NarratorPack` / `TavernPack` / `RivalPack` /
`LedgerPack` / `FactionPack` / `TellingPack` (§9.11) are content, not rules, and are out of scope
beyond that contract.

### 9.9 Other read models over the event log

All pure, zero-RNG, zero-write projections over `GameState.EventLog` (+ current live state where
named) — a growing family alongside `DemandBoard` (§9.4) and `LegendQuery` (§9.5), each one fact a
UI or a prose pack renders rather than a new rule:

- **`EarmarkQuery`** (`sim/GameSim/Drama/EarmarkQuery.cs`): the three facts a shelf hold (§4.4/§4.7
  `EarmarkAction`) produces once the story moves past the hold itself — `ForDay` (a held piece sold
  to the hero it was held for), `WaitingTonight` (still held, unsold, since strictly before today —
  `EarmarkedSince(state, item) < day`), `ReleasedForDead` (a hold released because its hero died
  tonight, joined off the same-day `HeroDied`).
- **`ClosestCallQuery`** (`sim/GameSim/Drama/ClosestCallQuery.cs`): the closest a SURVIVING hero
  came to dying last night — the low-water HP moment where they first crossed the 25% flee line
  (`CombatMath.ShouldFlee`), with the floor, the monster, and whether the armor worn at that moment
  was player-marked. Returns null on a night nobody dipped that low (most nights) — silence is the
  honest, correct answer, not a synthetic zero-severity row.
- **`FallenQuery`** (`sim/GameSim/Drama/FallenQuery.cs`): the death card's four pure reads for a
  hero who did NOT survive — the pack line (what a dead hero still carried, since nothing ever
  writes a dead hero's pack), the last-blow line (`CombatEvent.KillingItem`), the margin line (how
  far the fatal blow missed or exceeded the hero's remaining HP, composed from three already-
  recorded numbers — monster roll, worn-gear stats, and an hp replay — reusing `TellingQuery`'s own
  replay rather than a second copy), and the absence line. Every method returns `string.Empty`
  rather than a generic fallback sentence when there is nothing true to say.
- **`GateHeldStreakQuery`** (`sim/GameSim/Drama/GateHeldStreakQuery.cs`): `ConsecutiveNights` — how
  many evenings running a venue's `DecisionExplained("expedition-halt:{venueId}")` has read
  `GateHeld`, read straight off the durable log (never re-deriving `ExpeditionResolver`'s own
  gate/power comparison). Feeds `ForgeCounterPlayer`'s bounty-diversion arm (§14) and the demand
  board's stall framing (§9.4). Key is venue-only (no floor, no party) — a known, currently
  unreachable imprecision if two different parties ever held at the same venue on the same day.
- **`ProvenanceQuery`** (`sim/GameSim/Drama/ProvenanceQuery.cs`): which of the four honest channels
  (shelf, counter, commission, vigil) actually carried an item to the hand that holds it, and when
  — derived from `CounterSaleClosed`/`ItemSold.FromPlayerShop`/`CommissionFulfilled`/
  `SupplyDelivered`, never a new event or a write into `Item.History`. `AllChannels` walks an
  item's whole sale history the same way.
- **`RivalAbsenceQuery`** (`sim/GameSim/Drama/RivalAbsenceQuery.cs`): did a hero die carrying
  NOTHING the player made (bare hands count) — gates the rival smith's one spoken "the absence of
  proof" line, once per death, read off `HeroDied.WornGear` cross-referenced against
  `GameState.Items` exactly like `LegendQuery`.
- **`RivalSaleQuery`** (`sim/GameSim/Drama/RivalSaleQuery.cs`): a hero bought rival iron tonight
  while a matching player piece sat on the shelf strictly BEFORE today (`ShelfEntry.StockedDay <
  sale day` — never same-day, since same-day tick order cannot prove which came first). Picks the
  one player piece a hero would most plausibly have weighed, ordered the way `ShoppingAi` itself
  judges: quality, then price, then lower `ItemId`.
- **`StoriedGear`** (`sim/GameSim/Drama/StoriedGear.cs`): says out loud what `ShoppingAi`'s
  storied-gear loyalty gate (§4.4) has been deciding silently — which worn pieces have crossed
  their bearer's own (trait-shifted) deed threshold, and how many deeds. Adds no rule; the
  threshold itself does not move here.
- **`XpSplitQuery`** (`sim/GameSim/Drama/XpSplitQuery.cs`): splits `HeroXp.ForExpedition`'s one
  number into its two real inputs — floor XP and beat XP — using the SAME KillingBlow/LethalSave
  filter the reveal's own XP step applies (§4.8); never widened to beat types that earn no XP.
- **`DepthCopy`** (`sim/GameSim/Drama/DepthCopy.cs`): the one place `Hero.DeepestFloorReached == 0`
  becomes "not yet" instead of the fabricated "floor 0", and the one place a depth-stall's standing
  clause is composed (never "stalled at not yet" for a hero who has simply never gone down).

### 9.10 The wake

Two Evening-legal verbs beyond `HonorMemorialAction` (§2, `FarewellHandlers`) let the player finish
a fallen hero's page: `PlaceGraveMarkerAction` sets `Memorial.MarkerItem` to a player-crafted piece
that is unworn, unshelved, and not already marking another grave (one marker ever); a
`ChooseRemembranceAction` sets `Memorial.Remembrance` to a logged `EventId` that truly names the
hero (`RemembranceQuery.NamesHero`, `sim/GameSim/Drama/RemembranceQuery.cs` — the set of events
whose payload carries the hero's id as subject or counterpart; one remembrance ever). `WakeQuery`
(`sim/GameSim/Drama/WakeQuery.cs`) is the pure read model a wake screen asks "what is still
choosable" from, never re-deriving legality itself: `MarkerCandidates`/`MarkerOpen` reuse
`ActionLegality.IsLegal` verbatim; `RemembranceChoices` reuses `RemembranceQuery.Naming`;
`DefaultRemembrance` pre-selects the highest-stakes naming event by `PresentationScheduler.StakesFor`
(§9.8); `HeirloomOpen` treats heirloom reforge (§3.11) as the wake's fourth, still-open question —
true while some piece the fallen wore at death has never been reforged. A hold placed for a hero
who then dies is released the same night ("a hold for the dead is released at the wake",
§4.4/§9.9's `EarmarkQuery.ReleasedForDead`), reusing the existing `ShelfEarmarked(item, null)`
shape rather than a new event.

### 9.11 The Telling

Covered fully in §6.5: `TellingQuery` restages the counterfactual attribution replay
(`AttributionEngine`, §6.5) into a full staged shape per beat kind, and `IsDecisiveBeat`/
`KillingBlowIsDecisive` decide the `Decisive` flag gossip (§9.2) and fame (§9.5) both read.
`TellingPack` (`sim/GameSim/Flavor/Packs/TellingPack.cs`) is the prose content rendered from that
staging — content, not rules, same contract as every other pack (§9.8). Sole production caller of
the query: `godot/scripts/panels/TellingPanel.cs`.

---

## 10. The advisor and the mirror

`ActionLegality` (`sim/GameSim/Advisor/ActionLegality.cs:50-82`): a deliberate second copy of
every handler's Apply-level guard chain, kept honest by a 100-day kernel-parity property test;
the switch THROWS on an unmirrored action type so silent drift is impossible
(`ActionLegality.cs:29-38`). `LegalActions` (`ActionLegality.cs:92+`) enumerates one canonical
legal instance per opportunity, including a `PledgeDuesAction` candidate when a piece appraises
high enough (`ActionLegality.cs:146`). `SuggestedPrice.For(item) = max(1, qualityFloor, (Atk+Def)·2,
healMagnitude·2)` with quality floors 4/8/14/22/34
(`sim/GameSim/Advisor/SuggestedPrice.cs:42-64`).

`ObjectiveAdvisor.Suggest` (`sim/GameSim/Advisor/ObjectiveAdvisor.cs:59+`) priority order — every
suggestion re-checked through `IsLegal` before being returned:

0. **The death-adjacent bridge**, narrowed to fire ONCE per memorial (P2-MEMORY-04): an un-honored
   memorial is suggested only on the FIRST Evening it is legal (the day after the death, since the
   memorial itself is raised during that Evening's own systems pass) and never again — reading
   `Memorial.Day` alone, no new state. The prior rule (every un-honored memorial, every Evening,
   forever) re-presented a permanent fact nightly as if it were news, measured at 1,287 fires in
   one campaign; once honored, the memorial drops out regardless.
1. **Demand-driven** (U8/U10): reads the SAME `DemandBoard` snapshot (§9.4) the CLI/Godot surfaces
   read. An open commission wins first (accepting locks in a guaranteed sale — the strongest signal
   the town gives); otherwise the TOP depth stall, whichever shape it is — an empty slot, or a
   filled-but-under-quality gate (`RequiredQuality > CarriedQuality`). A quality-gated stall is
   additionally scanned for and appended even when a commission already won (deduped against it):
   a Common+ commission never lifts a Fine+ floor-3 wall, so the two answer different-horizon goals
   and gating the second behind "nothing suggested yet" was masking it entirely in practice.
2. **Fulfillment guidance** (U11): a shelved or held player item that may already ANSWER the top
   commission/stall's need (right slot, quality at or above the bar) is appended UNCONDITIONALLY —
   information about existing inventory, not a competing directive, so it rides alongside whatever
   won above rather than only firing when nothing else did.
3. **The cheapest-productive-path fallback**, now NEWS-gated (P2-HONEST-36): re-suggested only when
   it has actually changed since it was last said (`IsFallbackNews`, a pure fact off `GameState.
   ActionLog` — the class holds no standing state of its own, so "changed since last said" can
   never be a remembered flag). The unconditional repeat used to be 37% of all advice lines (5,147
   of 13,745) — "you already have enough copper to craft 'buckler'" repeated night after night
   whether or not the player ever acted on it, the exact "permanent fact told as news" shape #0
   fixes for the memorial rite. Not-news means the suggestion list is simply left as it already
   was — no new copy needed, existing empty-list text still reads correctly.
4. **Stock any unshelved player craft** — always legal once one exists, appended after whatever won
   above, unchanged from the original rule.

`HeroForecast.ForShelfAsItStands` (`sim/GameSim/Advisor/HeroForecast.cs:34-45`) calls the shopping
system's own `EvaluateGearCandidates` (`Heroes/ShoppingAi.cs:155-156`), so the forecast can never
disagree with the next real pass.

---

## 11. RNG — every stream and every draw

One stream: PCG32 (`sim/GameSim/Kernel/Pcg32.cs:11` — multiplier 6364136223846793005;
Lemire-style debiased `NextInt`, `Pcg32.cs:33-55`), state `RngState(State, Inc)` seeded by
splitmix64 expansion (`sim/GameSim/Contracts/Rng.cs:21-33`). `Inc` is the campaign-constant
stream identity — flavor uses it as the campaign id and it must never change
(`GossipSystem.cs:17-20`, `DirectorSystem.cs:17-19`). The kernel snapshots the stream on every
Tick/ApplyNow; systems receive it in registration order — draw ORDER is the determinism contract
(`sim/GameSim/Contracts/Rng.cs:4-6`, `IPhaseSystem.cs:5-9`).

Complete census of draw sites in `sim/GameSim/` (grep `rng.NextInt|rng.Roll100|rng.NextUInt`):

| Site | Draws | When |
|---|---|---|
| `DirectorSystem.Process` (`DirectorSystem.cs:85`) | 1 × `NextInt(0, totalWeight)` | once per calendar Morning, unconditionally |
| `HeroRoster.CreateRecruit` (`HeroRoster.cs:69-71`) | 3 — name, class, gold 30+`NextInt(0,31)` | per recruit minted |
| `QualityRoller.Roll` (`QualityRoller.cs:78`) | 1 × `Roll100` | per passive craft (unreachable today — §16.9) |
| `QualityRoller.RollActive` (`QualityRoller.cs:154`) | 1 × `Roll100` | per craft / heirloom reforge |
| `QualityRoller.SimulateActiveForge` (`QualityRoller.cs:248`) | 1 × `Roll100` per strike thrown | minigame-only path |
| `ExpeditionResolver.FightMonster` (`ExpeditionResolver.cs:603`, `:613`) | 1–2 per combat round (hero roll; monster roll only if the monster lives) | per round |
| `ExpeditionResolver.ResolveFloors` ore (`ExpeditionResolver.cs:412`) | 1 × `NextInt(1,4)` per standing hero per cleared floor | after each clear |

Within one day the draw order is therefore: director poll → any recruit draws → (nothing else in
Morning) → per-party stage-1 combat/ore draws in formation order → stage-2 draws at the Deep
tick → nothing at Evening. Handlers draw only through the craft roll; every rejection precedes
any draw, so a refused action never advances the stream (`CraftingHandlers.cs:14-15`). This table
is unchanged in shape since the prior census: every module added since (`Progression/`,
`Economy/PledgeDuesHandlers.cs`, `Crafting/CraftCurve.cs`, every new `Drama/*Query.cs` read model,
every new `Harness/` policy) is either pure integer math or a pure log/state projection, and each
says so in its own doc comment; grepping the whole tree for `rng.NextInt|rng.Roll100|rng.NextUInt`
turns up no site outside the seven above.

Deterministic non-RNG "randomness": `StableHash` (FNV-1a 64 + splitmix avalanche,
`sim/GameSim/Flavor/StableHash.cs:20-95`) drives flavor variants, voices, trait derivation,
forge-path shapes, tanning patches, engineering schematics, and artifact names. None of these touch
the kernel stream. (`SmithSkill`, which used to mix `Day` and `NextItemId` into a blacksmith-only
hand grade, was deleted in #753, P2-HONEST-09 — `Harness/CraftHand.cs`, §14, is the hand model now.)

---

## 12. Contracts — every shared type

`sim/GameSim/Contracts/` is deny-listed; every change lands as an orchestrator micro-PR. Reader
notes name production readers (sim + CLI + godot), never tests.

**Ids.cs** — `HeroId`, `ItemId`, `BountyId`, `EventId`: deterministic integer ids allocated by
kernel counters (`Ids.cs:4-25`).

**Enums.cs** — `DayPhase` (§1.1); `ItemSlot` (Weapon/Shield/Armor/Consumable/Trinket — trinket
content exists via alchemy/engineering recipes; a trinket can only reach a hero through a
commission, never the ordinary gear pass, §4.1); `HaggleResponseKind`; `QualityGrade`; `BeatType`
(`ToolAssist` has no emitter, `Enums.cs:63`); `ConsumableKind` (Heal only, `Enums.cs:70-73`);
`BountyRefundReason` (`AcceptorDied`/`Lapsed`, `Enums.cs:79-84`, §8); `ExpeditionHalt`
(six values); `StandingShiftDirection`; `CampaignAct`.

**Rng.cs** — `IDeterministicRng`, `RngState` (§11).

**IPhaseSystem.cs** — `IPhaseSystem`, `IEventSink`, `IActionHandler` (`IPhaseSystem.cs:11-41`).
The kernel's private sink also implements `ITraceSink` (`sim/GameSim/Kernel/ITraceSink.cs:22-30`
region) — the only way a `DecisionTrace` reaches `TickResult.Traces`.

**ActionBudget.cs** — §1.5. The ten-action list is pinned by reflection in `ActionBudgetTests`
(`ActionBudget.cs:44-48` doc).

**Actions.cs** — the 29 `PlayerAction` types (§2) + `CraftPuzzleInput` (abstract; four derived
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
`ModifierHpDelta`; `Expedition.cs:20`), `FloorOutcome` (`Expedition.cs:43`), `AttributionBeat`
(`Expedition.cs:46`; the EVENT counterpart `AttributionBeatEvent`, Events.cs, additionally carries
`Decisive` — §9.5/§9.11), `OreLoot` (`Expedition.cs:49`), `GateReading(Floor, PartyPower,
GateRequired)` (`Expedition.cs:69` — set only on a `GateHeld` halt, read back by
`GateHeldStreakQuery` and the reference smith's bounty-diversion policy, §6.3/§14),
`HeroAtDeparture` (`Expedition.cs:89`), `ExpeditionResult` (+ trailing `VenueId`="mine",
`Halt`=TargetReached, plus init members `GateHeldAt` (nullable `GateReading`) and
`PartyAtDeparture` (the party's combat-relevant records as they marched, for read models that
outlive the live hero state); `Expedition.cs:108-133`), `InFlightExpedition` (+ `SupplySent`,
`Recalled`; carries NO RngState — the kernel stream is the single authority; `Dead` is always
empty under the v1 park invariant; `Expedition.cs:147-167`), `Bounty` (`Expedition.cs:169`;
`Paid` is constant false — §16).

**Player.cs** — `ShelfEntry` (`Player.cs:12` — plus `StockedDay` (P2-MEMORY-26, the sim day it was
stocked; stocking emits no event, so this stamp is the only durable "sat there before today"
record) and `EarmarkedFor` (P2-PEOPLE-28, §4.7/§2 `EarmarkAction`), both trailing with
old-save-safe defaults), `BatchEchoState` (`Player.cs:23`), `PlayerState` (gold, materials — which
also carries the two reserved counter keys `forge-tier-progress` and `legendary-commissions-used`
— per-profession talents, selected professions, shelf, nullable `Standing`, `BatchEcho`;
`Player.cs:44-51`).

**World.cs** — `Memorial` (`World.cs:16` — plus the wake's `MarkerItem`/`Remembrance`, both
trailing-nullable, §9), `DramaState` (`World.cs:19`), `LoggedBatch`
(`World.cs:31`), `CounterState` (`World.cs:57`), `VenueState` (`World.cs:99`; `DaysUntouched`
has zero readers — §16), `RentState` (`World.cs:114`), `GuildAssessmentState`
(`World.cs:148`), `ArcState` (+ `ClimaxDay`; `World.cs:180-195`), `GameState` (`World.cs:202` —
positional core + init members `InFlight`, `LastNightExpeditions` (last night's revealed
`ExpeditionResult`s, kept readable after the reveal empties the pending queue — feeds §9's
after-the-fact narration read models), `Venues`, `Counter`, `ActionSlotsRemaining`, `Rent`,
`RivalMarketSharePermille`, `Commissions`, `Director`, `Assessment`, `Arc`), `Commission`
(`World.cs:313` — plus `ProvenBy`/`ProvedFor`, trailing-nullable, §4.7), `DecisionTrace`
(`World.cs:352`), `TickResult` (+ `Traces` — drained by `sim/GameSim.Cli/BatchRunner.cs` into a
sibling `*.decisions.jsonl` per batch seed, §14/§16).

**Director.cs** — `DirectorPhase`, `IncidentCategory`, `IncidentMagnitude`, `DirectorState`
(`Director.cs:8-91`).

**Events.cs** — 56 event types (`Events.cs:11-66` registration; six added since the previous
census — `DuesPledged`/`DuesSettledByPledge` §7.5, `ShelfEarmarked` §4.7, `BountyRefunded` §8,
`GraveMarkerPlaced`/`RemembranceChosen` §9). Reason-carrying events: `HeroPassedOnItem`,
`HeroDecisionExplained`, `BountyJudged`, `BountyRefunded` (names `AcceptorDied`/`Lapsed`),
`CustomerWalked`, `HeroDied`, `AttributionBeatEvent`, `DecisionExplained` (the generic ad hoc
reason channel; first emitter is the reveal's expedition-halt line, `Events.cs:454`).
Conservation-reconciling records:
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
| influence-never-orders | `Kernel/HeroSovereigntyCensusTests.cs` + `Advisor/AdvisorNeverOrdersTests.cs` | the FIRST proves no player verb writes hero state outside an honest channel — forks a real campaign at every decision point; any verb whose `ApplyNow` moves serialized hero state must appear in a pinned 4-entry `HonestChannels` map — BuyOre, SendSupply, RecallParty, SendDeeper (`HeroSovereigntyCensusTests.cs:41-56`); channels pinned unexercised: {RecallParty, SendDeeper, SendSupply} (`:150-151`). "SendDeeper" names no action type — §16. The SECOND (P2-HONEST-24, added since the prior census) proves the law's other half — the advisor's own WORDS never order the player: it drives real campaigns and reads every distinct `ObjectiveAdvisor.Suggest` reason, checking each against the SHAPE of an imperative rather than a fixed banned-phrase list (the gap it closed: `ObjectiveAdvisor` used to emit "craft 'X' now" / "Raise the forge to Tier N" literally, unread by the first tripwire because nothing there touches a hero) |
| no-decision-timers | `Presentation/ClientAuthorityCensusTests.cs` | token census over godot scripts banning client clocks; 2 pinned exceptions, both save-file timestamps, each citing §11.7.8 (`ClientAuthorityCensusTests.cs:46-58`) |
| verbs-change-outcomes | `Balance/VerbConsequenceFloorTests.cs` | forks decision points with/without each legal verb and compares the durable-world fingerprint ticks later; asserts only the sound direction — no verb is inert every time it is offered (`VerbConsequenceFloorTests.cs:23-39`) |
| show-only-sim-decided | `Presentation/ClientAuthorityCensusTests.cs` + `Hygiene/PlayerVocabularyCensusTests.cs` | the FIRST bans client RNG (`new Random`, `GD.Rand*`) in the same census as no-decision-timers. The SECOND (P2-HONEST-06, added since) owns GENERATORS rather than a banned-word list — "player copy names only things the player can see": no raw enum member, registry id, CLI verb, surface id, plan-unit citation, permille, or bare formula may render where a display name exists or should — checked by reflecting over live declarations (`Contracts` enums, a registry's own `All` table, the CLI's dispatch arms) so a newly-added member bans itself without editing this file |
| sim-purity-determinism | `Kernel/SimPurityCensusTests.cs` + `Kernel/DeterminismTests.cs` | token census over every sim source banning DateTime/Stopwatch/Random/Guid/transcendental Math/float/double/decimal/Godot/network (`SimPurityCensusTests.cs:35-49`), pinned exception count **0** (`:63`); plus the golden replay |
| no-runtime-llm | `Kernel/SimPurityCensusTests.cs` | the network-token half of the same census |
| skipping-stays-legal | `Economy/NoSoftlockTests.cs` | the un-losability proof: the destitution floor fires at a true dead-end and only there (`NoSoftlockTests.cs:11-14`) — the mechanical half this file's tripwire actually enforces; the law's other half ("its cost is named in copy") gained its own pin since the prior census (P2-HONEST-23) but on the GODOT side, not here: `MarketShareSystem`'s idle-day charge names itself on the ticker the same evening (`godot/scripts/ui/AdventureTicker.cs`'s idle-direction `MarketShareShifted` case, pinned by `godot/tests/UnsilencedEventTests.cs`) |

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

`BaselinePlayer` is not the corpus, though, and this paragraph is not the authority on what the
corpus covers. Which of these the *whole* `Category=Balance` set actually submits along a real
trajectory — versus merely offers as a legal candidate that no policy ever takes — is executable in
`sim/GameSim.Tests/Hygiene/BalanceCorpusCoverageCensusTests.cs`, which reflects the live
`PlayerAction` hierarchy, denies by default, and fails by name. Read that test, not this paragraph,
before claiming corpus coverage of any verb.

**`CounterPlayer.ActionsFor`** (`sim/GameSim/Harness/CounterPlayer.cs:30`): Morning only —
`OpenCounterAction`; then per state: `PresentItemAction` (best role-fit shelf item, now
`BestRoleFitItem` — shared with `ForgeCounterPlayer`'s own counter opener below),
`HaggleResponseAction(Counter, band-center)` (never Accept, never HoldFirm),
`CloseCounterAction` when nothing to present or no customer. Nothing else, no other phase. Never
wired into `Category=Balance`/the CLI's default loop — exists so the determinism suite can drive a
full stepped Morning; `BaselinePlayer`'s own atomic-equivalence pin depends on it never opening
the counter.

**`MasterworkSeekingPlayer`**: `ActionsFor` (`sim/GameSim/Harness/MasterworkSeekingPlayer.cs:67-152`
region) is the ONLY policy that constructs `BuyForgeSupplyAction` (restock coal/flux to 12) or
`MasterworkAttemptAction` (greedy — a masterwork attempt over a hand-craft of the same recipe
whenever legal); falls back to `CraftAction`. Its own `UpgradeForgeAction` construction is no
longer exclusive — `BaselinePlayer` gained the same call (U-T1-9, checked first every Morning,
§14 above) — but the class's own doc comment (`MasterworkSeekingPlayer.cs:10-16`) still claims
sole ownership of all three; that stale claim is finding 14 (§16). Morning + Expedition only. Its
Expedition recipe loop is now gated by `BaselinePlayer.HasBuyer` (made `internal`, shared rather
than re-derived, P2-HONEST-44) — measured before the fix (§11.16 measurement 6, 20 campaigns): 66%
of all crafts were Shortswords, 42% sat unsold at the ending, no armor or shield was ever made, and
`LethalSave` fired 0 times against a baseline median of 8, because the loop kept Baseline's
tier-then-stat-sum ORDER but dropped its buyer gate. A separate entry point, `SweepActionsFor`
(P2-HONEST-40, `:41-64`), is what `--policy masterwork` actually drives: `ActionsFor` alone has no
acquisition half (no Evening ore buy, no shelf stocking) and crafted NOTHING across 8 seeds × 30
days pointed at a fresh campaign, so `SweepActionsFor` composes `BaselinePlayer` (the income) with
`ActionsFor` (the masterwork spend) outside the Expedition phase, leaving `ActionsFor` itself
byte-for-byte unchanged for the hand-fixture unit tests that already pin its numbers.

**`ForgeCounterPlayer`** (`sim/GameSim/Harness/ForgeCounterPlayer.cs`, P2-HONEST-30): composes
`BaselinePlayer`'s craft/stock/buy loop with `CounterPlayer`'s counter state machine and REAL
haggle responses — the first policy measured actually closing counter sales (`CounterPlayer` opens
2,000 sessions and closes zero, since it never crafts or stocks). Decision 2 ("price for the sale
or the relationship"), deterministic off recorded state, never RNG: once a customer has a standing
offer, a `RelationshipBand` (§4.6) of Regular-or-better PINS the price at the round's own ceiling
(inside `HaggleResolver`'s pin window on the only round this policy ever reaches, since it never
HoldFirms); a Stranger gets Accept — pricing for the relationship over the extra gold. `IsFleeceArm`
(P2-HONEST-35) takes roughly half of the Regular-or-better closes above the ceiling as a deliberate
FLEECE instead of a pin — deterministic off hero id + calendar day — because every harness before
this closed 551 counter sales with 270 pinned and ZERO fleeced, so `WillingnessModel.
FleeceMoodPenalty`, the fleeced-sale gossip line, and `NeedsSystem`'s boycott bias had never once
fired from any scripted policy. Also the first policy with an Expedition-phase armor priority
(P2-LONG-37): before its own baseline craft pick, it crafts the best armor recipe a MARCHING
light-class hero (mystic/occultist/skirmisher) whose armor slot holds nothing of the smith's can
legally wear — measured light-class heroes at 59% of deaths, 476 marches in bare rival gear, with
the light armor recipes (§3.2) crafted 0-1 times across three prior policies. Its own bounty-posting
arm (`AddBountyPost`, Morning) reads `GateHeldStreakQuery` (§9.9) for a venue stuck ≥
`DemandBoard.StallThresholdDays`; prices via `MinAcceptableReward` (the cheapest reward at which
some living, reach-eligible hero's own `BountyRules.DesireScore` actually clears
`AcceptanceThreshold` — a binary search over the real formula, never re-derived); and (P2-HONEST-50)
diverts the TARGET floor away from the one the party is already held at, naming instead the
deepest floor some OTHER living hero's own reach allows — measured before the fix (§11.19
measurement 2): 82 of 97 posted bounties named the same floor the party had just failed at 47% of
the time, pricing the party's own plan rather than aiming it anywhere.

**`ApprenticePlayer.ActionsFor`** (`sim/GameSim/Harness/ApprenticePlayer.cs`, P2-ONBOARD-03): plays
the onboarding tutorial ("The Warrant") roughly as its guided course describes it — the instrument a
seed search (P2-ONBOARD-04) runs against, not the pin itself. The one policy that branches on
`GameState.Day` (a deliberate calendar SCRIPT, not a day-agnostic policy like every other entry
here): buy copper and craft day 1; shelve every unsold player craft at a fair price EXCEPT one held
consumable, reserved for the camp runner; open the counter day 2 and close FAIR — Accept only,
never haggle; accept every open GEAR commission; buy every affordable ore offer each Evening; send
the one reserved consumable to a camped party (`SendSupplyAction`, §6.7) the moment one fires.
Deliberately narrow (one gear recipe, one consumable recipe, fixed rules) rather than adaptively
optimal, so the seed search it feeds has a stable target.

**`HandForgePlayer`** (`sim/GameSim/Harness/HandForgePlayer.cs`, 2026-09-03 owner ruling): closes a
structural blind spot — every OTHER policy auto-crafts (null `Puzzle`/`PerformanceGrade`), so the
100-day balance gate had NEVER exercised the hand-forge branch of `CraftingHandlers.ApplyCraft` at
all: not `ForgeScorer`, not `ForgeMoment`, not the batch echo. Composes over `BaselinePlayer`
unchanged, replacing only the single `CraftAction` Baseline's Expedition branch may emit with one
carrying a constructed `ForgeTraceInput` — a captured minigame trace scored against the real
`ForgePath` target with a constant per-mille tracking error at every sample and strike
(`AverageDeviationPermille`), not zero (a flawless trace) and not RNG (this harness draws none of
its own). `CraftHand` (`sim/GameSim/Harness/CraftHand.cs`) is the one shared "how good is the
scripted hand" rule every profession's policy applies to its own puzzle in its own terms —
`Indifferent` (ignores the puzzle; anchors to `CraftCurve.IndifferentAnchorPermille`, §3.7),
`Average` (roughly half the craft done right), `Skilled` (one mistake short of flawless) — replacing
the deleted `SmithSkill`'s single blacksmith-only scale (below) so a sweep can compare skill levels
across all four crafts without conflating three different, uncalibrated "average" hands (the
finding that motivated §3.7's shared curve in the first place).

**`LateMasteryPlayer`** (`sim/GameSim/Harness/LateMasteryPlayer.cs`, owner ruling 2026-09-03,
P2-OQ9): everything `BaselinePlayer` decides, including the hand-forge itself
(`HandForgePlayer.HandForgeOver`), except WHICH talent the one-node-per-day unlock loop reaches
for — defers both mastery talents to spend every other point first, the other end of the range from
Baseline's own greedy prereq-order pick (which made Masterwork the modal hand-forge grade, 51.7% of
1,522 items, under the pre-§3.5-retune subtractive forgiveness rule) — the control that made that
diagnosis trustworthy, re-run against every later curve revision.

**`ActiveProfessionPlayer`** (`sim/GameSim/Harness/ActiveProfessionPlayer.cs`, P2-OQ10): closes the
same blind spot `HandForgePlayer` closed for the blacksmith, for Alchemy/Tanning/Engineering —
grepped and confirmed before it was written: every existing policy crafts EXCLUSIVELY off
`RecipeTable.All` (blacksmith's own table), never `ProfessionRegistry.AllRecipes`, so the balance
corpus had ZERO items from these three professions, auto-craft or otherwise. NOT a composition over
`BaselinePlayer` (that assumption breaks here: `CraftingHandlers.ApplyUnlock` never checks
`PlayerState.IsSelected`, so composing over Baseline's Morning branch inside a non-blacksmith save
would silently spend a scarce action slot on blacksmith talents nobody uses). Instead: `Alchemy
PuzzlePlayer`/`TanningPuzzlePlayer`/`EngineeringPuzzlePlayer`, three thin wrappers each driving a
campaign that selected ITS OWN profession alone from day 1
(`GameComposition.NewCampaign(seed, profession)`) — one talent unlock/day, restocking off the
always-available Morning vendor (never the non-deterministic Evening ore offers), accepting every
open gear commission, shelving every unsold craft. A save can hold at most 2 of the 4 professions,
so no single sweep can exercise all three new scorer families side by side without confounding the
read — hence three separate single-profession campaigns, not one multi-profession one.

`SkilledSmithPlayer` and the `SmithSkill` scale it re-stamped `CraftAction`s with are gone from the
tree entirely (deleted with #753, P2-HONEST-09); `CraftHand` (above) is their replacement, and
`sim/GameSim/Harness/` holds no trace of either name.

**`ScenarioBuilder.BuildDay`** (`sim/GameSim/Harness/ScenarioBuilder.cs:43-62`): ticks a
fresh campaign under `BaselinePlayer` to the start of day N — the deterministic save-fixture
factory.

---

## 15. The CLI as a sim driver

`sim/GameSim.Cli/Program.cs` builds the composed kernel and a `NewCampaign(seed)`
(`Program.cs:272-273`), queues actions only when `kernel.Accepts` passes
(`Program.cs:1200-1213`), and advances exclusively through `kernel.Tick(current, batch)`
(`Program.cs:1218`) — the CLI never calls `ApplyNow` (grep: no hits under `sim/GameSim.Cli/`).
`batch` runs seed sweeps under `Policy.Baseline` by default (20 seeds × 100 days, `BatchRunner.cs:97-101`), writing
chronicles to `runs/`; `--policy` selects any of TEN scripted players — `baseline, counter,
apprentice, handforge, latemastery, alchemy, tanning, engineering, forgecounter, masterwork`
(`BatchRunner.cs:37`, `:200-213`) — and `--hand` selects the craft-minigame skill level for the
FIVE that play one — `handforge, latemastery, alchemy, tanning, engineering`
(`BatchRunner.HandAware`, `BatchRunner.cs:217-219`). Seven further CLI files
(`ArcStallSweep.cs`, `DenSweep.cs`, `EconTrajectory.cs`, `FeltWallSweep.cs`, `LongWallSweep.cs`,
`SeedSearch.cs`, `SlotSpendSweep.cs`) are one-off measurement sweeps, each self-documented as
"not a gate"; every one reuses `GameComposition`/`BatchRunner`'s existing policy axis and
`ActionLegality`/`BaselinePlayer`/`DemandBoard` machinery rather than defining a new verb or
drawing RNG outside the kernel stream — no sim rule lives in any of them. `ConsequenceProbe`
(`sim/GameSim.Cli/ConsequenceProbe.cs`) owns the whole-state
fingerprint the verbs-change-outcomes law test reuses. `Characterize`
(`sim/GameSim.Cli/Characterize.cs`) prints party-power/floor tables under `BaselinePlayer`.
`DecisionLogger`/`DecisionPlay` drive the same kernel for playtest telemetry.
`GameSim.Progression.ProgressionSpineSystem.Compute` (`Program.cs:444`, U-D4) is a pure, memoryless
read of the live `GameState` printing a five-axis "what to chase next" view (Forge/Depth/Roster/
Wealth/Chronicle — the first four finite and cross-feeding, Chronicle unbounded so there is always
a next rung) — despite the name it is NOT an `IPhaseSystem` (not registered in `GameComposition`,
§1.4); it is a CLI-only display, same footing as `Characterize`. None of these add rules.

---

## 16. Findings — orphans, dead fields, and rules with no surface

1. **`RecallPartyAction` is invisible to the balance corpus.** `SendSupplyAction` is now submitted
   by two scripted policies (`ForgeCounterPlayer`, `ApprenticePlayer` — §14, §6.7); `RecallParty`
   is not — grepped every file in `Harness/`, zero hits, and it is still pinned as unexercised by
   `HeroSovereigntyCensusTests.cs:150-151` (§13). Every balance number and every chronicle in
   `runs/` still describes a world where the recall bell never rings.
2. **`VenueState.DaysUntouched` is written and never read** — reset/incremented in
   `DirectorSystem.DenStep` (`DirectorSystem.cs:352-357`), read by no rule and no surface (grep:
   writers only). More broadly the whole den-escalation block is recorded drama by design:
   nothing reads `InfectionPerMille`/`ThreatTier`/`Closed` back into routing or combat
   (`World.cs:86-89`) — a lockdown-latched venue still hosts raids (§9.3).
3. **`CounterState.GoodwillPermille` feeds no sim rule** — decremented on a fleece
   (`HaggleResolver.cs:182`) and read only by godot `CounterPanel.cs` (`:400`, `:684`, `:698`) to
   diff before/after a response purely to detect a fleece for a flavor line, not the "standing"
   chip the panel actually displays. The contract doc's own claim — "the fleece memory that feeds
   Hero.MoodPermille and gossip" (`World.cs:39-40`) — describes a mechanism that does not exist;
   `godot/tests/BrynWrongOnPurposeTests.cs` guards against relying on it.
4. **Emberfall's floor 5 keeps the raw monster formula** — HP 62, Attack 35
   (`EmberfallFoundryVenue.cs:115-126`, `MonsterHp` at `:123`, uses `12 + 10·f` / `5 + 6·f` with no
   floor-5 override) — while the Mine and the Crypt dial floor 5 down to HP 50 / Attack 26 after
   measurement. The campaign's climax boss is the one boss still at the pre-retune deadliness,
   behind gate 73; the file's own comments now cite this as a known, un-fixed divergence
   (P2-HONEST-08) rather than a silent surprise (§5).
5. **`BeatType.ToolAssist` has no emitter** (`Enums.cs:63`) — extensively cross-referenced now
   (`GossipGenerator.cs`, `XpSplitQuery.cs`, `TellingQuery.cs`, `PresentationScheduler.cs`, several
   godot files all explicitly say "no emitter yet"), a tracked reservation rather than a hidden
   gap. `ConsumableKind` still has only `Heal`, so every "utility" consumable (Transmuter's Tonic,
   Field Repair Kit) mechanically heals.
6. **Doc-comment vs code on haggle patience**: `Actions.cs:103` says every haggle response
   consumes one patience round; only HoldFirm does (`HaggleResolver.cs:112-138`; Accept and
   Counter close the sale immediately, §7.2). Also: a Counter above the ceiling still SELLS — a
   fleece with a mood penalty, not a refusal; once a round is open the customer never walks over
   price, only over patience.
7. **"SendDeeper" is a pinned honest channel with no action type** — the sovereignty census maps
   four channels (`HeroSovereigntyCensusTests.cs:41-54`) and pins SendDeeper as never exercised
   (`:150-151`); "send them deeper" is mechanically the absence of both Camp verbs (§6.7).
8. **`InFlightExpedition.Dead` is always empty** under the v1 park invariant — the field's own doc
   comment says so explicitly (`Expedition.cs:155`); the field exists for a v2 that fights past
   deaths.
9. **The passive quality model is dead code on the live path** — all four professions are
   `ActiveCraft: true` (`ProfessionRegistry.cs:51`, §3.1), so `QualityRoller.Roll`'s ±8 threshold
   table (§3.4) is reachable only from tests and the two hard-coded auto-craft fallback call sites
   (`CraftingHandlers.cs:209`, `HeirloomHandlers.cs:138`) that would need a passive profession to
   ever actually take it.
10. **A dead hero's wealth evaporates**: expedition gold reaches survivors only
    (`ExpeditionRevealSystem.cs:162-173`), ore offers are minted for survivors only (`:357-365`),
    and a stale offer from a hero who died later is refused at purchase
    (`OreMarketHandlers.cs:69`).
11. **The bounty explains itself in-band; the quality roll's equivalent story does not.** The full
    D_q arithmetic is printed in the `BountyJudged` reason string (`BountyRules.cs`, `Judge`/
    `DesireScore`/`AcceptanceThreshold`) — a game event a hero's own gossip line can quote. The
    quality roll's shift/jitter/ceiling story is captured as a `DecisionTrace`
    (`QualityRoller.cs`, `HaggleResolver.cs`) that DOES now reach a reader —
    `sim/GameSim.Cli/BatchRunner.cs` forwards `TickResult.Traces` into a sibling
    `*.decisions.jsonl` per batch seed, read by `tools/Analytics` — but only as an out-of-band CLI
    telemetry file; nothing in `godot/scripts/` reads `.Traces` (grep), so no in-game surface ever
    tells the player why a craft or a haggle landed where it did.
12. **`PledgeDuesAction` has no `[JsonDerivedType]` registration.** `Contracts/Actions.cs`'s
    `[JsonPolymorphic]` attribute list (`:11-38`) names all 28 of the OTHER concrete `PlayerAction`
    types; `PledgeDuesAction` (`:251`) is the only one of 29 missing an entry. `PlayerAction` uses
    System.Text.Json's default (fail-closed) unknown-type handling, so a save taken after a
    `PledgeDuesAction` has been submitted and logged will throw at serialization — the exact live
    gap the `CraftPuzzleInput` registration warning (§12) already exists to prevent for a different
    type hierarchy.
13. **`Bounty.Paid` is constant false.** The only construction site sets `Paid: false`
    (`BountyHandlers.cs:49`); `BountyPayoutSystem` REMOVES a paid or refunded bounty from the open
    list instead of ever flipping the flag (`BountySystems.cs:58`). Harmless today — the one
    client-side gate that used to read the field (`godot/scripts/ui/TutorialFlow.cs`'s
    second-profession milestone) was fixed to scan for a `BountyPaid` event in the log instead
    (§17) — but the field itself remains permanently, silently false.
14. **`MasterworkSeekingPlayer`'s own class doc claims a fact `BaselinePlayer` now contradicts.**
    Its comment (`MasterworkSeekingPlayer.cs:10-16`) still calls it "the ONLY scripted policy...
    that ever constructs `UpgradeForgeAction`" — false since U-T1-11 gave `BaselinePlayer` the same
    construction, checked first every Morning (`BaselinePlayer.cs:42`); `BaselinePlayer.cs`'s own
    doc comment (`:54-59`) already flags the sibling file's claim as stale, but
    `MasterworkSeekingPlayer.cs` itself was never corrected — the same "stale doc comment the code
    has outgrown" shape that `ProfessionHandlers.cs`'s now-fixed class doc used to have.

---

## 17. Unverified — worth checking

Phrased as questions; none of these was mechanically verified.

- `FactionDriftSystem` has no held-Morning guard (`FactionDriftSystem.cs:29-65`, §1.4). During a
  multi-tick counter Morning, does standing drift once per tick on purpose, or should it carry the
  same guard as Rent/Gossip/Recruit? (BaselinePlayer never opens the counter, so no gated trace
  exercises it.)
- `MusterSystem.Process` re-runs `MusterPlan.Compute` and re-emits `PartiesFormed`
  (`MusterSystem.cs:102-106`) on every held-Morning tick with no guard against repeat emission —
  is the event-log duplication during stepped service intended?
- If a hero somehow held two accepted commissions, `TryFulfillFromShelf`
  (`CommissionHandlers.cs:84`) serves the first-in-list — is that reachable at all, given
  `CommissionSystem.cs:130`'s own doc comment says gaps are posted only for a hero with no live
  commission?
- In the CLI's Tick-only model, tonight's ore offers are purchasable only via actions applied at
  the NEXT Evening tick (`ExpeditionRevealSystem.cs:38-41` doc), while a Godot client using
  `ApplyNow` buys them the same Evening — is that client-model difference understood?
