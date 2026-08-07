# Maker's Mark — Mechanics Ground Truth (derived from source, 2026-08-06)

**Method.** This document was written by reading the CODE ONLY — `sim/GameSim/`, `sim/GameSim.Tests/`, `sim/GameSim.Cli/`, and `godot/scripts/` — with `docs/` deliberately unread. It is the control arm against the documentation-derived design account: where the two disagree, the disagreement is the finding. Every claim carries a `path/file.cs:line` citation so it can be checked. Line numbers are as of commit `0dfe3a8` on this branch.

**One-paragraph summary of what the game actually is, per the code.** A deterministic, integer-only, five-phase-per-day simulation (`sim/GameSim/Kernel/GameKernel.cs:188-198`) in which the player runs a craft shop (24 action types, `sim/GameSim/Contracts/Actions.cs:10-35`), six-or-fewer autonomous heroes shop each Morning by a pure gear-score-per-gold rule (`sim/GameSim/Heroes/HeroShoppingSystem.cs`), form parties and raid one of three live venues each day (`sim/GameSim/Expedition/ExpeditionSystem.cs`), and the entire raid is resolved as a pure function at departure and merely *revealed* at Evening (`sim/GameSim/Drama/ExpeditionRevealSystem.cs`). The player's craft reaches the heroes only through prices and shelves — "influence, never orders" is enforced structurally, not aspirationally. A counterfactual attribution engine proves, from recorded dice, whether a specific player item saved a specific hero's life (`sim/GameSim/Expedition/AttributionEngine.cs`).

---

## 1. The complete action inventory

There are exactly 24 `PlayerAction` types (`sim/GameSim/Contracts/Actions.cs:10-35`). Phase legality is decided **only** by each handler's `CanHandle` (`sim/GameSim/Kernel/GameKernel.cs:30-31`); timing (instant vs. bell) is decided **only** by `ActionTiming.ResolvesImmediately` (`sim/GameSim/Kernel/ActionTiming.cs:75-129`); whether it costs one of the day's 5 action slots is decided by an `ActionSlotsRemaining` gate inside each handler (see §6). The Godot client submits everything through `SimAdapter.Queue`, which routes instant actions to `GameKernel.ApplyNow` and queues bell-riders for the next `Tick` (`godot/scripts/SimAdapter.cs:119-148`).

| # | Action | Effect | Legal phases (handler) | Timing | Slot? | Godot surface |
|---|--------|--------|------------------------|--------|-------|---------------|
| 1 | `CraftAction` | Consume materials, roll ONE `Roll100`, mint item (`Crafting/CraftingHandlers.cs:40-238`) | ALL phases — no phase filter (`Crafting/CraftingHandlers.cs:29-30`) | Now | Yes (`:130,228`) | ForgePanel button + 4 minigames (`godot/scripts/panels/ForgePanel.cs:535,596`; `minigames/QuenchMinigame.cs:279`, `AlchemyBrewPuzzle.cs:213`, `EngineeringBench.cs:371`, `TanningFrame.cs:256`) |
| 2 | `StockAction` | Shelve a player-crafted item at a price (`Economy/ShopHandlers.cs:42-98`) | ALL (`ShopHandlers.cs:25-26`) | Now | No | ShopPanel (`godot/scripts/panels/ShopPanel.cs:516`) |
| 3 | `SetPriceAction` | Reprice a shelf entry (`ShopHandlers.cs:100-121`) | ALL | Now | No | ShopPanel (`ShopPanel.cs:566`) |
| 4 | `UnstockAction` | Remove a shelf entry (`ShopHandlers.cs:123-141`) | ALL | Now | No | ShopPanel (`ShopPanel.cs:539`) |
| 5 | `BuyOreAction` | Buy from a returning hero's ore offer; hero gets base ask, player pays tariffed cost (`Economy/OreMarketHandlers.cs:38-165`) | Evening ONLY (`OreMarketHandlers.cs:35-36`) | Now | Yes (`:104,148`) | LedgerModal (`godot/scripts/panels/LedgerModal.cs:215`) |
| 6 | `BuyMaterialAction` | Buy any priced-pool material at +25% markup (`Economy/MaterialVendorHandlers.cs`) | Morning ONLY (`MaterialVendorHandlers.cs:45-46`) | Now | Yes (`:96`) | ForgePanel vendor row (`ForgePanel.cs:1029`) |
| 7 | `PostBountyAction` | Escrow gold on a target floor; heroes judge it next Expedition tick (`Bounties/BountyHandlers.cs:18-60`) | Morning or Evening (`BountyHandlers.cs:15-16`) | Now | Yes (`:42,58`) | BountyPanel (`godot/scripts/panels/BountyPanel.cs:212`) |
| 8 | `UnlockTalentAction` | Unlock a talent node — **zero cost**, prerequisites only (`CraftingHandlers.cs:304-338`, "talent-point economy deferred" `:332`) | ALL | Now | No | ForgePanel talent tree (`ForgePanel.cs:1018`) |
| 9 | `SetProfessionsAction` | Select 1–2 professions; gates craftable recipes (`Professions/ProfessionHandlers.cs:34-57`) | ALL (`ProfessionHandlers.cs:25`) | **Bell** (`ActionTiming.cs:126`) | No | MainUi tutorial second-profession picker only (`godot/scripts/MainUi.cs:2826`) |
| 10 | `SendSupplyAction` | Pay runner fee (6+3×floor g), front-insert one held consumable into a camped hero's pack (`Expedition/CampHandlers.cs:52-143`) | Camp ONLY (`CampHandlers.cs:34-35`) | Now | No | CampPanel (`godot/scripts/panels/CampPanel.cs:287`) |
| 11 | `RecallPartyAction` | Ring the bell: party banks stage-1 gains, surfaces without rolling stage 2 (`CampHandlers.cs:150-171`) | Camp ONLY | Now | No | CampPanel (`CampPanel.cs:263`) |
| 12 | `OpenCounterAction` | Flip Morning into stepped counter service (`Counter/CounterHandlers.cs`) | Morning ONLY (`CounterHandlers.cs:28-30`) | Now | No | CounterPanel (`godot/scripts/panels/CounterPanel.cs:78`) |
| 13 | `PresentItemAction` | Show a shelf item to the active customer; verdict resolves in the handler itself (`Counter/CounterQueueSystem.cs:52-93`) | Morning ONLY | Now | No | CounterPanel (`CounterPanel.cs:272`) |
| 14 | `SuggestItemAction` | Upsell; +80‰ Interest if it lands on a complementary empty slot (`Counter/HaggleResolver.cs:73-76`) | Morning ONLY | Now | No | CounterPanel (`CounterPanel.cs:390`) |
| 15 | `HaggleResponseAction` | Accept / HoldFirm / Counter against the standing offer (`HaggleResolver.cs:82-159`) | Morning ONLY | Now | No | CounterPanel (`CounterPanel.cs:307,429,478`) |
| 16 | `CloseCounterAction` | End the session; unserved heroes fall back to atomic shopping (`CounterHandlers.cs`) | Morning ONLY | Now | No | CounterPanel (`CounterPanel.cs:105`); MainUi force-close (`MainUi.cs:1874`) |
| 17 | `AcceptCommissionAction` | Lock a hero's gear request; fulfillment pays list + premium (`Heroes/CommissionHandlers.cs:30-41`) | Morning ONLY (`CommissionHandlers.cs:18-19`) | Now | No | CommissionBoard (`godot/scripts/panels/CommissionBoard.cs:96`) |
| 18 | `DeclineCommissionAction` | Remove an open commission, no penalty (`CommissionHandlers.cs:43-52`) | Morning ONLY | Now | No | CommissionBoard (`CommissionBoard.cs:101`) |
| 19 | `HonorMemorialAction` | Flip a memorial's `Honored` once; idempotent (`Drama/FarewellHandlers.cs`) | Evening ONLY (`FarewellHandlers.cs:20-21`) | Now | No | LegendsWall (`godot/scripts/panels/LegendsWall.cs:115`) |
| 20 | `ReforgeHeirloomAction` | Reforge a fallen hero's recorded worn gear into a lineage-carrying item; ordinary auto-craft roll (`Crafting/HeirloomHandlers.cs:41-154`) | ALL (`HeirloomHandlers.cs:39`) | Now | Yes (`:121,147`) | LegendsWall (`LegendsWall.cs:154`) |
| 21 | `UpgradeForgeAction` | Buy the next forge tier: 400/1600/6400/25600 g + 25 floor-ore (`Economy/ForgeTierHandlers.cs:46-54`) | Morning ONLY (`ForgeTierHandlers.cs:61-62`) | **Bell** (`ActionTiming.cs:125`) | Yes (`:99,113`) | **NONE — unreachable in Godot.** CLI only (`sim/GameSim.Cli/Program.cs:608`). Bell-tray vocab exists (`godot/scripts/ui/PendingVerbVocab.cs:31,41`); no button constructs it. |
| 22 | `BuyForgeSupplyAction` | Buy coal (4 g) / flux (40 g) (`Economy/ForgeSupplyHandlers.cs:37-42`) | Morning ONLY (`ForgeSupplyHandlers.cs:44-45`) | Now | Yes (`:91`) | **NONE — unreachable in Godot.** CLI only (`Program.cs:627`). |
| 23 | `MasterworkAttemptAction` | 3 coal + 1 flux + 100 g×tier + materials → guaranteed Superior (Masterwork if material outgrades recipe); requires Forge Tier II; ZERO RNG (`Economy/MasterworkAttemptHandlers.cs:30-158`) | ALL (`MasterworkAttemptHandlers.cs:47`) | Now | Yes (`:126,152`) | **NONE — unreachable in Godot; not even bell-tray vocab.** CLI only (`Program.cs:643`). |
| 24 | `CommissionLegendaryWorkAction` | 3000 g×tier + 2× materials → guaranteed Masterwork; capped 4/campaign (`Economy/LegendaryCommissionHandlers.cs:23-38`) | ALL (`LegendaryCommissionHandlers.cs:40`) | **Bell** (`ActionTiming.cs:127`) | Yes (`:127`) | **NONE — unreachable in Godot.** CLI only (`Program.cs:660`). |

**Reachability verdict.** 20 of 24 actions are reachable from the Godot client; the four Phase-D gold sinks (#21–24) are implemented, tested, CLI-reachable, and **have no on-screen affordance at all**. Three of the four even have their bell-tray display strings pre-registered (`godot/scripts/ui/PendingVerbVocab.cs:31-43`) — the plumbing shipped ahead of the buttons, and the buttons never landed. Under the project's own "DEPLOYED means 3D, not CLI" rule, the entire Phase-D economy endgame is not shipped.

No Godot panel is orphaned. Roughly half of the panels are pure read-only displays by design (HeroesPanel, TavernPanel, DepthsPanel, DemandPanel, HeroCards, ProgressionPanel, MineWatch, RaidForecastBoard, BestiaryPanel, ChronicleScroll, ScryingMirror, DelveStage, ProvenanceCard) — they submit nothing and exist to make the sim legible. The CLI (`sim/GameSim.Cli/Program.cs`) is a strict superset of the Godot client's action reach.

**Timing model.** 21 of 24 actions resolve instantly via `ApplyNow` (`GameKernel.cs:59-103`), which applies the one action, persists RNG + action log, and does NOT advance the phase or reset budgets. Exactly three ride the bell as deliberate ceremony: `UpgradeForgeAction`, `SetProfessionsAction`, `CommissionLegendaryWorkAction` (`ActionTiming.cs:121-128`). The list is deny-by-default: any future action type queues until someone opts it in (`ActionTiming.cs:60-62`).

---

## 2. The phase machine, exactly

One `Tick` = apply queued actions → run this phase's systems in registration order → stamp events → advance phase (`GameKernel.cs:105-173`). Day order is defined **only** by `Advance` (`GameKernel.cs:188-198`), never by enum value:

```
Morning → Expedition → Camp → ExpeditionDeep → Evening → (Day+1) Morning
```

Two state-aware exceptions (`GameKernel.cs:190-191`):
- An open, unfinished counter session **holds** the day at Morning indefinitely (`GameKernel.cs:190`; pinned by `sim/GameSim.Tests/Kernel/CounterPhaseHoldTests.cs:31-101`).
- With **zero living heroes**, Morning folds straight to Evening — Expedition/Camp/ExpeditionDeep are skipped entirely (`GameKernel.cs:191,216-217`; `NoRaidToHost` is true only when `PartyFormation.FormParties` returns empty, which requires no one alive). Pinned by `sim/GameSim.Tests/Kernel/PhaseCollapseTests.cs:44-95`.

Per-phase system order is the determinism contract (`sim/GameSim/GameComposition.cs:57-77`):

**Morning** (12 systems, in order): `DirectorSystem` (drama pacing — the day's ONE unconditional RNG draw) → `FactionDriftSystem` (standing decays toward 0) → `CounterQueueSystem` (stepped-counter resolution) → `RentSystem` → `GuildAssessmentSystem` → `DestitutionRecoverySystem` (no-softlock floor) → `RivalRestockSystem` (mints missing rival stock) → `RecruitSystem` (roster refill) → `GossipSystem` (voices *yesterday's* stamped events, max 3 lines — `Drama/GossipGenerator.cs:50`) → `HeroShoppingSystem` (every alive hero buys at most one gear item + one consumable) → `CommissionSystem` (expiry + posting) → `MusterSystem` (**must be last**: emits `PartiesFormed`, the roster/floor/venue prediction that must byte-match the Expedition tick — `Heroes/MusterSystem.cs:78-108`).

**Expedition** (2): `BountyJudgingSystem` (first-accept loop, visible `BountyJudged` events — `Bounties/BountySystems.cs:12-29`) → `ExpeditionSystem` (forms parties, routes venues, resolves stage 1 at departure — `Expedition/ExpeditionSystem.cs:37-112`).

**Camp** (0): **no registered system has `Phase == Camp`** (`GameComposition.cs:58-77`; the only kernel reference to Camp is the transition at `GameKernel.cs:194`). Camp exists purely as the action window for `SendSupply`/`RecallParty`. On a day when no party parked, the Camp tick — and the Deep tick after it — changes nothing the player can observe; the player rings the bell through two empty phases.

**ExpeditionDeep** (1): `ExpeditionDeepSystem` finalizes every parked `InFlight` party on the live RNG stream (`Expedition/ExpeditionDeepSystem.cs:27-47`).

**Evening** (4): `ExpeditionRevealSystem` (applies deaths/gold/records/beats/XP/ore — §4) → `BountyPayoutSystem` (pay, refund-on-death, refund-on-expiry — `BountySystems.cs:43-84`) → `ArcDirectorSystem` (act thresholds — `Arc/ArcDirectorSystem.cs:57-88`) → `MarketShareSystem` (**must be last**: reads whether any action slot was spent today before the kernel resets the budget — `Economy/MarketShareSystem.cs:33-48`).

Kernel bookkeeping at the phase boundary: the counter session is torn down the instant the day leaves Morning (`GameKernel.cs:163`), and `ActionSlotsRemaining` resets to 5 only when the Day number actually increments (`GameKernel.cs:169`).

**Held-Morning guard.** Because a counter session re-runs every Morning system per tick, every once-per-day Morning system (Director, Rent, Assessment, RivalRestock, Recruit, Gossip, Commission) skips while `Counter is { Closed: false }` (e.g. `Economy/RentSystem.cs:53-56`); `CounterQueueSystem` registers ahead of them so the closing tick fires them exactly once.

---

## 3. The expedition truth

**Party formation** (`Heroes/PartyFormation.cs:18-63`): alive heroes only, HeroId order. Parties of 3, each seeded with an anchor-class hero (Vanguard/Sentinel via `ClassDefinition.IsAnchor`) when available; leftovers form one smaller party, even solo. No player input touches this — ever.

**Target floor** (`Expedition/ExpeditionSystem.cs:136-152`): `clamp(max(party's DeepestFloorReached) + 1, 1, venue.FloorCount)` — heroes push exactly one floor past their best — **unless** a party member accepted a bounty, in which case the bounty's floor overrides (R18's one "order" the player can buy).

**Venue routing** (`Venues/VenueRouter.cs:58-125`): bounty parties always go to the Mine (bounties are structurally Mine-scoped — `ExpeditionSystem.cs:57-66`); bounty-free parties are routed draw-free by band (highest reached `EntryPower` wins), then queue length, then venue id. Live rotation = Mine, Gloomwood, Sunken Crypt (`Venues/VenueRegistry.cs:62-66`). Emberfall is built, tuned (EntryPower 72), and deliberately dormant — no art (`VenueRegistry.cs:51-60`).

**Staged resolution.** The checkpoint is `min(1, target−1)` (`ExpeditionSystem.cs:26-31`) — i.e. camp always sits below floor 1, and a floor-1 target is unstaged (whole run resolves at the Expedition tick).

A party **parks** (becomes `InFlightExpedition`, emits `PartyCampReport`) if and only if stage 1 (floors 1..checkpoint) ended with raw halt `TargetReached`: every stage-1 floor cleared, nobody dead, nobody too hurt (`Expedition/ExpeditionResolver.cs:99-128`). Any other stage-1 ending — gate held, floor lost, wipe, too-hurt — **finalizes immediately at the Expedition tick with no camp report**; the town still learns nothing until Evening (KTD5, `ExpeditionResolver.cs:104-106`). The parked record carries HP/packs/gold/floors/loot but deliberately **no RNG state** — stage-2 dice are provably undrawn while the party camps (`Contracts/Expedition.cs:78-82`).

**What the player can do below ground** (Camp phase, both draw-free):
- `SendSupplyAction`: one delivery per party per day, consumables only, must be the player's own unshelved craft, fee = 6 + 3×checkpoint = 9 g at the v1 checkpoint (`Expedition/CampHandlers.cs:28-32`) — priced deliberately above the pinned 8 g salve sale price so sending always costs more than selling (`CampHandlers.cs:24-27`). Front-inserted, so it is drunk first (`CampHandlers.cs:123-131`).
- `RecallPartyAction`: stage 2 never rolls; halt = `Recalled`; stage-1 clears/ore/gold bank (`ExpeditionResolver.cs:159-165`).

That is the complete list. The player cannot re-gear, heal, redirect, or reinforce a party underground.

**The floor loop** (`ExpeditionResolver.cs:247-397`), per floor:
1. Standing fighters (not dead, not retreated); none left → `PartyWiped`.
2. **Structural gate**: `PartyAveragePower < venue.Gate(floor)` → `GateHeld`, no roll (`ExpeditionResolver.cs:288-292`). Mine gates: 0/15/35/60/100 (`VenueRegistry.cs:100-109`). The rival catalog's best loadout sums to 54, so rival-only gear can never clear floor 5 (`Economy/RivalCatalog.cs:25-29`).
3. Each fighter fights the floor's monster 1-v-1 in HeroId order (`ExpeditionResolver.cs:297-313`). Per round (`FightMonster`, `:459-577`): flee check FIRST (below 25% MaxHp, no salve cancels it — the 2026-08-01 owner ruling, `:479-501`); then quaff if wounded (below 50% or one worst-case blow from death, `CombatMath.cs:37-38`, at most one heal per round, only if actually below MaxHp `:512-516`); then hero d6 attack, monster d6 counter if alive. Damage = `max(1, atk + roll − def)` (`Expedition/CombatMath.cs:70-75`). A hero death or flee leaves the floor uncleared (`:303-311`).
4. Cleared floor: post-floor "too hurt to continue" check at the 50% drink line — drink first, re-check; still under → banks the clear and the run ends `TooHurt` (`:341-354,379-384`).
5. Ore loot: every standing survivor draws 1–3 units of the floor's ore (+Lodestone bonus) (`:371-377`).
6. **Competence retreat** (TUNING-C): any hero for whom the NEXT floor exceeds `DeepestFloorReached + 1` peels off — banked, still a Survivor, fights no deeper (`:393,405-426`). A bounty acceptor is exempt through the bounty floor (`ExpeditionSystem.cs:122-125`).

**Halt precedence** (D4): `DeepestCleared == TargetFloor` is ALWAYS `TargetReached`, whatever ended the loop (`ExpeditionResolver.cs:194-195`; pinned `sim/GameSim.Tests/Expedition/StagedResolutionTests.cs:92-161`).

**Death is decided at departure.** All rolls happen at the Expedition/Deep ticks; Evening only reveals. There is no rescue window for a death that has already been computed — `PendingExpeditions` already contains the corpse while the player is still living the same day.

**Full HP every morning.** Working HP is initialized to `MaxHp` at resolve time (`ExpeditionResolver.cs:33,86`); no injury persists between days. The only persistent hero state: gold, gear, pack, memories, XP/Level, mood, depth record, Alive.

---

## 4. The causal chain, verified end to end

One concrete path from hammer to legend, every function named:

1. **Craft** — player clicks Forge (or finishes the two-act minigame): `ForgePanel` builds `CraftAction` (`godot/scripts/panels/ForgePanel.cs:535`; minigame path `QuenchMinigame.cs:279` → `ForgePanel.cs:727`) → `SimAdapter.Queue` → `GameKernel.ApplyNow` (`SimAdapter.cs:128`) → `CraftingHandlers.ApplyCraft` (`Crafting/CraftingHandlers.cs:40`) → grade resolution (`ForgeScorer.Score` for a hand trace `:145-147`; batch echo `:154-159`; puzzle scorers `:165-171`) → `QualityRoller.RollActive` (`Crafting/QualityRoller.cs:147-173`) → `ItemForge.Forge` (`Crafting/ItemForge.cs:34-57`, MakersMark "You") → optional modifier stamping (`CraftingHandlers.cs:263-302`) → optional signing (`Crafting/ArtifactSigning.cs:53-57`) → `ItemCrafted` event.
2. **Shelve** — `StockAction` (`Economy/ShopHandlers.cs:42-98`; player-crafted only `:54`).
3. **Sale** — next Morning `HeroShoppingSystem.Process` (`Heroes/HeroShoppingSystem.cs:39-75`): accepted-commission fulfillment first (`CommissionHandlers.TryFulfillFromShelf`, `Heroes/CommissionHandlers.cs:73-140`, guaranteed sale bypassing all verdict gates), else `EvaluateGearCandidates` → `ShoppingAi.EvaluateItem` (`Heroes/ShoppingAi.cs:110-187`) → `ApplyPurchase` equips the item, moves gold hero→player, emits `ItemSold` (`HeroShoppingSystem.cs:323-361`). Alternatively the player sells it by hand at the counter: `PresentItemAction` → `CounterQueueSystem.ResolvePresentedItem` (`Counter/CounterQueueSystem.cs:52-93`) → `HaggleResolver.OpenRound/ResolveHaggleResponse/CloseSale` (`Counter/HaggleResolver.cs:49-190`).
4. **March** — same Morning, `MusterSystem` predicts (`Heroes/MusterSystem.cs:93-108`); Expedition tick: `BountyJudgingSystem.Process` → `ExpeditionSystem.Process` → `PartyFormation.FormParties` → `VenueRouter.ChooseVenue` → `ExpeditionResolver.ResolveStage1`, where `CombatMath.HeroAttack` reads the equipped weapon's Attack (`Expedition/CombatMath.cs:51-52`) and every roll is recorded into `CombatEvent.RecordedRolls` (`ExpeditionResolver.cs:521-534`).
5. **Attribute** — at result-build time, `AttributionEngine.ComputeBeats` replays the recorded rolls counterfactually (`ExpeditionResolver.cs:218` → `Expedition/AttributionEngine.cs:19-145`): killing blow by a marked weapon (`:60-67`); lethal save = recompute the recorded hit without each marked defensive item, beat iff the hero lived and would have died (`:69-96`); breakpoint clear = party average would have missed the gate without the item (`:117-139`); Provisioned/PotionLifesave from recorded `ConsumableUse` data (`:156-229`). Beats exist ONLY for player-crafted items (`:231-232`) — rival gear can never earn a legend.
6. **Reveal** — Evening, `ExpeditionRevealSystem.Reveal` (`Drama/ExpeditionRevealSystem.cs:51-236`): `PartyReturned` → deaths flip `Alive`, raise a `Memorial` naming the worn gear (`:59-78`) → loot gold to survivors only (`:80-92`) → depth records (`:94-112`) → each beat becomes an `AttributionBeatEvent` AND a permanent `ItemHistoryEntry` ("kill"/"save") on the item AND an `ItemMemory` on the bearer (`:114-150`) → XP/rank/level (`:184-217`) → ore offers minted (`:219-233`).
7. **Echo** — next Morning `GossipSystem` voices yesterday's stamped events, ≤3 lines, each citing a real `EventId` (`Drama/GossipSystem.cs:33-60`, `GossipGenerator.cs:50`). `RelationshipBands`, `NeedsSystem`, `LegendQuery`, `RelationshipSystem` are all pure re-derivations over the same event log.

**The chain is complete in code**, end to end, with no stubs. The partially-wired part is the *surface*: attribution lands on ProvenanceCard/LegendsWall/ledger prose, but the Phase-D sinks that would spend the resulting wealth are CLI-only (§1), and the arc's Climax event is explicitly a bare seam — `ClimaxReached` fires with no content behind it ("the Final Commission / Warden-of-the-Heart content the plan describes is a later, orchestrator-wired hook — this event is the seam it lands on", `Contracts/Events.cs:293-297`).

---

## 5. What the heroes actually decide, and on what inputs

Hero autonomy is real but narrow: it is five deterministic, RNG-free decision rules over integers. No hero has goals, memory of intent, or plans — "personality" is derived arithmetic.

1. **What to buy** (`Heroes/ShoppingAi.cs:110-187`), fixed check order: role fit (shield-allowed, weight cap) → veteran quality gate (floor ≥3 refuses below Common, trait-shifted ±1 grade — `ShoppingAi.cs:68-81,137-142`) → affordability → sentimental attachment (worn gear with ≥3 recorded deeds isn't displaced by a gain <5 — `:164-175`) → strict gear-score improvement. Winner = best gain-per-gold by integer cross-multiplication (`:251-267`). Inputs: class definition, gold, worn gear, item stats/quality, own memories. A boycotting hero (6+ days without buying from the player — `Heroes/NeedsSystem.cs:53`) reads player prices +40% for ranking only (`HeroShoppingSystem.cs:169-182`) — bias, never a block.
2. **What price to tolerate** (`Counter/WillingnessModel.cs:135-147`): `list × (classFactor + Interest + mood + qualityBonus + traitBonus) / 1000`, capped at gold on hand. Class factors: Vanguard 1150 / Sentinel 1120 / Striker 1000 / Occultist 980 / Mystic 950 / Skirmisher 820 (`:36-45`). Quality: Poor −120‰ … Masterwork +220‰ (`:103-111`).
3. **Whether to take a bounty** (`Bounties/BountyRules.cs:73-109`): refuse anything past `DeepestFloorReached + 1`; else accept iff `greed × reward − 20×level / floor ≥ 100 × floor`. Greed = 10 (14 Spendthrift, 6 Thrifty). First accepting hero in HeroId order claims it (`:121-142`). Every judgment emits a reason that names the arithmetic verbatim.
4. **How deep to go / when to stop** (§3): target = own record + 1; flee below 25% HP; drink below 50% or when one worst-case hit from death; retreat at personal competence ceiling; "too hurt" ends the day. Craft modifiers shift the flee line ±8%/tier (`Crafting/CraftModifiers.cs:76-77`) — the one place the player's craft changes hero *behavior*, not just stats.
5. **Whether to restock heals** (`HeroShoppingSystem.cs:252-300`): buy the single cheapest Heal when pack is below the trait target — Prepared 2, neutral 1, Reckless 0 (`Heroes/TraitEffects.cs:121-129`).

**Traits are a hash, not a roll.** Each hero's 2 traits are recomputed on every read from `hash(HeroId, Name)` across 5 opposing axes — never stored, never drawn, campaign-invariant, so the starting six have the same traits in every campaign (`Heroes/TraitDefinition.cs:57-141`). Trait teeth are shop-side only plus bounty greed; nothing else in the raid reads them.

**Identity of the cast**: fixed starting six (Torvald/Brunhilde Vanguard, Kael/Sable Striker, Elowen/Moss Mystic — `Heroes/HeroRoster.cs:37-51`); recruits are 3 RNG draws (name from a 24-name append-only pool, class from a 6-class pool, gold 30–60 — `HeroRoster.cs:67-85`), at most one per 2 Mornings while roster <6 (`Drama/RecruitSystem.cs:22-27`).

**Mood is influence-only, structurally** (PKD7): `Hero.MoodPermille` is written by counter pins/fleeces (±60/−80 — `WillingnessModel.cs:82-88`), commission fulfillment/expiry (+100/−100), kin-of-the-dead seed (+60); it is read only by willingness math and gossip/bands. Party formation, floor choice, and expedition resolution never read it (`Contracts/Heroes.cs:61-67`).

**Relationships and needs are read models, not state**: hero↔hero edges (ComradeBond +20, Grief +35, Grudge −30, 40-day linear decay, 2 outbids → RivalrySeed) are re-derived from the event log on every read (`Heroes/RelationshipSystem.cs:65-81`); nothing in the sim acts on them — they are narration inputs only.

---

## 6. The numbers that matter

**Player start**: 100 g, 6 copper, blacksmith (or chosen profession) (`Kernel/GameFactory.cs:10-13`).

**Action budget**: 5 slots/day (`Contracts/ActionBudget.cs:18`), reset only on day increment (`GameKernel.cs:169`). Slot-spending handlers (9): Craft, BuyOre, BuyMaterial, PostBounty, ReforgeHeirloom, UpgradeForge, BuyForgeSupply, MasterworkAttempt, CommissionLegendaryWork (each handler's `ActionSlotsRemaining - 1` site; see §7 for the drifted `ConsumesSlot` predicate).

**Quality (passive roll)** (`Crafting/QualityRoller.cs:20-36,75-96`): `Roll100 + 8×(materialGrade + mastery − tier) + talent shifts`; ≤14 Poor / 15–64 Common / 65–89 Fine / 90–98 Superior / ≥99 Masterwork. Base odds at even material: 15/50/25/9/1%.

**Quality (active/blacksmith-alchemist-tanner-engineer)** (`QualityRoller.cs:147-190`): `clamp(grade,0,1000) + jitter(±25)`; <200 Poor / <550 Common / <780 Fine / <930 Superior / ≥930 Masterwork. Material ceiling: under-tier caps Fine, even caps Superior, over-tier uncapped. Auto-craft grade = 550, hard-capped Superior — **the minigame is the only road to a rolled Masterwork** (`:167-170`). Batch echo: one hand-forge seeds up to 4 same-day auto-crafts at grade decaying −80‰/copy, floored at 550 (`CraftingHandlers.cs:21-27`).

**Item stats**: quality multiplier 80/100/115/135/160% on Attack/Defense/heal magnitude; weight never scales (`Crafting/ItemForge.cs:20-28`).

**Recipes**: 39 total — blacksmith 16 incl. Field Salve Heal-6 (`Crafting/RecipeTable.cs:54-82`), tanning 7, alchemy 8, engineering 8 (all using the same mine ores as inputs — `Professions/*/`). Trinket-slot recipes exist only in engineering/alchemy.

**Materials** (`Materials/MaterialRegistry.cs:49-102`): priced pool of 14 (mine: copper 3 g/g1 … adamant 18 g/g5; gloomwood g8–11 at 36–54 g; sunken-crypt g1–5 at 3–18 g). Vendor markup +25% ceil (`Economy/MaterialVendorHandlers.cs:31,39-43`); hero ore offers sell at base — always cheaper than the vendor.

**Combat** (`Expedition/CombatMath.cs`, `Venues/VenueRegistry.cs:94-137`): d6 rolls (0–5); hero atk = classBase + 2×level + weapon; def = level + shield + armor. Class bases: Vanguard 29 HP/4 atk, Striker 24/6, Mystic 20/3 (weight cap 4) (`Classes/ClassRegistry.cs:26-56`). Mine floor f: HP 12+10f, atk 5+6f, def 2+2f, gold 5+3f, gates 0/15/35/60/100. Flee <25%, drink <50%.

**XP/Level** (`Heroes/HeroXp.cs:18-30,42-50,75-89`): 10 survive + 5/floor + 15/credited beat; ladder 0/50/150/300/500/800 = Novice→Legend = Level 1→6; Level feeds combat directly (U-C6 flip).

**Counter** (`Counter/WillingnessModel.cs`): patience 3 rounds (±1 by trait); band round 1 = 82–98% of willingness, +9%/round; pin window ±6% (mood +60); fleece = counter above ceiling (mood −80); Interest cap 300‰ (present +150 role-fit, suggest +80).

**Commissions** (`Heroes/CommissionSystem.cs:43-55,188-220`): ≤3 open, 5-day deadline, premium = 15 + 10×targetFloor + band bonus (Regular 10/Patron 25/Sworn 50); min quality = max(floor bar: <3 Common, 3–4 Fine, ≥5 Superior; band bar: Patron Fine, Sworn Superior). Fulfillment: +100 mood; accepted-then-expired: −100 mood; ignored posts expire silently.

**Bounties** (`Bounties/BountyRules.cs:13-19,80`): escrowed at post; expiry 3 days (refund); UI floor hint 10 g/floor; acceptance bar D_q ≥ 100×floor.

**Economy clocks**: Rent 30 g base / 10 days / +15% paid, +35% missed / cap 500 / confidence −150 missed, +40 paid (`Economy/RentSystem.cs:24-37`, `Contracts/World.cs:112-115`). Guild assessment 20 g base / 7 days / +50% paid, +75% missed / cap 800 (`Economy/GuildAssessmentSystem.cs:53-61`); confidence −10/day passive, +80 depth record, +50 beat, −100 death, +100 pass, −50 miss (`:42-51`); thresholds: <400 rival expands (+60‰ share/day), <200 hero considers leaving, 0 = one-shot soft-fail latch — **no era reset exists** (U-D5 unimplemented, `:25-27`). Destitution floor 10 g, fires only at a proven dead-end (`Economy/DestitutionRecoverySystem.cs:37-38`).

**Rival** (`Economy/RivalCatalog.cs:42-69`, `RivalRestockSystem.cs:27-30,85-90`, `MarketShareSystem.cs:23-27`): 6 fixed Common lines, price = 2×statSum, stat caps 20/16/18 (loadout 54 < gate 100); share +150‰ on a fully idle day, −100‰ on any working day; discount ≤40% at full share.

**Factions** (`Factions/FactionRegistry.cs:27-34`): only Deepvein is live (all 5 mine ores). Standing +5/buy, cap 100, −2/morning drift, max effect = 10% ore discount. Crownsguard/Ashguild/Wardens/Tidewrit are built but drive nothing (Deepvein is the only registered ore supplier the tariff path can find for live ores).

**Drama director** (`Drama/DirectorSystem.cs:118-137`): tension +220/death +45/record +25/return, −40/day, −450 on fire; fires at Peak with ≥3-day refire gap or 12-day drought pity; incident category gated by deepest floor, magnitude by survived count, **never by gold** (`:59-66`).

**Arc** (`Arc/ArcDirectorSystem.cs:36-46`): Act II at floor 3, Act III + Climax at floor 5, Ending 5 days later; world stays open after (`Contracts/Events.cs:299-303`).

**Signing** (`Crafting/ArtifactSigning.cs:36,43-45`): Masterwork + all three sub-scores ≥950; name from a frozen 12-name pool hashed on campaign identity.

**Craft modifiers** (`Crafting/CraftModifiers.cs:23-26,71-128`): Coward's/Braveheart oil ±8%/tier flee line; Leech rune 3 HP/tier on kill; Lodestone +1 ore/tier. Slots by grade 0/1/2/3/3; tier cap: mithril+ = 2, else 1; Masterwork grants +1 tier on the first modifier.

**Balance gate = executable design intent** (Category=Balance, all driven by `Harness/BaselinePlayer`): floor 3 by day ≤40 and floor 5 not before day 8; player gold never negative; 3–6 alive at day 100; ≥60 attribution beats in the last 60 days (`sim/GameSim.Tests/Balance/BalanceSimTests.cs:16-29,77-98`). Gold conservation: town total moves by exactly −rivalSales −Σtariff −vendorCost +stipend, asserted per tick (`sim/GameSim.Tests/Economy/GoldConservationTests.cs:171-174`).

---

## 7. Dead and vestigial code — where drift has already happened

1. **The four Phase-D gold sinks have no UI** (§1). Sim + CLI + tests complete; `PendingVerbVocab` strings pre-registered for three; zero Godot buttons. The economy's entire late-game spend loop is unreachable in the shipped client.
2. **`ActionBudget.ConsumesSlot` has drifted from reality.** The Contracts predicate names 4 action types (`Contracts/ActionBudget.cs:26-27`) and a test pins "exactly the four" (`sim/GameSim.Tests/Kernel/ActionBudgetTests.cs:18-31`) — but **nine** handlers actually decrement the slot counter (grep `ActionSlotsRemaining - 1`: BountyHandlers:58, ForgeTierHandlers:113, LegendaryCommissionHandlers:127, ForgeSupplyHandlers:91, MasterworkAttemptHandlers:152, OreMarketHandlers:148, CraftingHandlers:228, MaterialVendorHandlers:96, HeirloomHandlers:147). No runtime code calls `ConsumesSlot` at all — `MarketShareSystem` reads the counter directly (`MarketShareSystem.cs:35`). The predicate is documentation-only and its pinning test now pins a false claim.
3. **`ProfessionHandlers`'s class doc says it is not registered — it is.** "`this handler is NOT yet wired into GameComposition.BuildKernel`" (`Professions/ProfessionHandlers.cs:16-18`) vs. the actual registration (`GameComposition.cs:84`). Stale comment; the action is live.
4. **`ShopHandlers` bearer check misses the Trinket slot.** Stocking rejects an item equipped as Weapon/Shield/Armor (`Economy/ShopHandlers.cs:59-66`) but not a worn Trinket — and trinket recipes exist (engineering `EngineeringProfession.cs:83-84`, alchemy `AlchemyProfession.cs:89-90`). A player-crafted trinket a hero is wearing can be re-shelved and sold to a second hero while the first still wears it. Compare `HeirloomHandlers.WoreItem` (`Crafting/HeirloomHandlers.cs:156-157`) and `DestitutionRecoverySystem` (`:85`), which both include Trinket. Suspected duplication defect (not fixed here per task constraints).
5. **`BeatType.ToolAssist` has no emitter** — reserved for the Engineering add-on, declared and serialized but never produced (`Contracts/Enums.cs:63`). Deliberate contract-ahead-of-content.
6. **Dormant content behind registries** (deliberate, pinned by tests): Emberfall venue (built, banded, no art — `VenueRegistry.cs:51-60`); electrum/orichalcum + Emberfall ore ladder outside the priced pool (`MaterialRegistry.cs:91-102`); Crownsguard et al. factions registered but supplying no live ore; the `CraftPuzzleInput` seam's four scorers are live but `SimulateActiveForge`'s heat-band policy path (`QualityRoller.cs:218-244`) is reachable only from minigame plumbing, never auto-craft.
7. **Stale phase-count comments**: `CraftingHandlers.cs:8-9` and `ShopHandlers.cs:10-11` say "ALL THREE phases" — the day has had five phases since staged resolution landed; the handlers are in fact legal in all five (no phase filter). `Actions.cs:149` calls `HonorMemorialAction` "Evening/Night-legal"; no Night phase exists — the handler is Evening-only (`Drama/FarewellHandlers.cs:20-21`).
8. **`RentState.ConfidencePermille`'s doc says "deliberately NOT wired yet"** (`Contracts/World.cs:106-108`) — it has since been wired by `GuildAssessmentSystem` (which the same file's later record documents). Only the first comment is stale; harmless but misleading to a reader who stops early.
9. **`Bounty.Paid` field is never set true** — payout *removes* the bounty instead (`Contracts/Expedition.cs:106`; `BountySystems.cs:59`). Vestigial field, serialized forever.
10. **Talent points cost nothing** — "the talent-point economy is deliberately deferred" (`CraftingHandlers.cs:13-16,332`), so `UnlockTalentAction` is a free, monotone unlock; BaselinePlayer unlocks one per morning for free (`Harness/BaselinePlayer.cs:26-36`).

---

## 8. Surprises — both directions

**Richer than advertised:**

1. **Attribution is real counterfactual math, not flavor.** "Your shield saved his life" is only claimed when replaying the recorded monster roll *without the shield's Defense* would have dropped the hero to ≤0 while he actually lived (`AttributionEngine.cs:69-96`). Same rigor for gate breakpoints and potion lifesaves. Very few games compute this honestly; this one does, and pins it in tests.
2. **The determinism discipline is total.** One PCG32 stream, exactly four legitimate consumer sites (resolver, quality roller, recruit factory, drama director — `DirectorSystem.cs:9-10`); every other system is provably draw-free; traits/signing/gossip identity are hashes, not draws; 100-day byte-identical replay is a test (`BalanceSimTests.cs:100-105`). "Same seed + same actions = same world" is genuinely true.
3. **Craft modifiers change behavior, not numbers.** No modifier is a stat bump — by explicit rule they move hero-AI decision thresholds (flee line, heal-on-kill, ore greed) (`Contracts/Items.cs:16-22`, `CraftModifiers.cs:6-12`). The player literally forges personality adjustments.
4. **The balance suite encodes an emergent finding most designs never measure: provisioning kills.** Blanket salve-stocking *raises* party mortality ~+35–59% across seeds (risk compensation — topped-up heroes push one floor deeper and die there), and the camp send-supply verb measurably raises aggregate deaths too; both are pinned as documented behavior, not bugs (`sim/GameSim.Tests/Balance/SalveProvisioningBalanceTests.cs:126-157`, `Balance/CampProvisioningBalanceTests.cs:23-37`). Even after the flee-first fix, Prepared heroes die ~63–66% (only ≥5pp better than Reckless — `Balance/ConsumableTraitMortalityBalanceTests.cs:159-206`). **The game's healing economy is, by its own measurements, a trap the fiction sells as safety.**
5. **The advisor is a full parallel rules mirror.** `ActionLegality` deliberately re-implements every handler's guard chain and keeps itself honest with a 100-day kernel-parity property test plus a throw-on-unmirrored-action rule (`Advisor/ActionLegality.cs:13-38`) — added after the four Phase-D verbs sat "illegal, forever, silently" in the mirror.
6. **Four playable professions with four distinct minigames** (heat-trace forge, reagent brew, hide scrape, assembly bench), all scored in-sim as pure integer functions (`CraftingHandlers.cs:91-171`), all dual-wieldable (pick 2 — `ProfessionHandlers.cs:23`).

**Thinner than advertised:**

7. **"Autonomous heroes" are five arithmetic rules** (§5). There is no goal system, no needs simulation beyond a purchase-streak counter, no inter-hero behavior — relationships are narration-only derivations. The autonomy premise is honest at the *decision* level but there is no *life* behind it.
8. **The Camp phase — the vigil the game's fiction leans on — runs zero systems and exists on most days as two empty bell-rings** (§2). Its two verbs are also, per the balance suite, net-harmful to use (surprise #4). The dramatic centerpiece phase is mechanically the thinnest phase in the game.
9. **The campaign ending is a threshold and a tally.** Act II = someone reached floor 3; Act III + "Climax" = someone reached floor 5; Ending = 5 days later, fire one event with six counters on it (`ArcDirectorSystem.cs:57-110`). The climactic Final Commission the event doc gestures at does not exist (`Contracts/Events.cs:293-297`).
10. **Heroes never leave, and the town can't die.** `HeroConsideringLeaving` is explicitly "a legible warning, never an automatic departure" (`Contracts/Events.cs:276-280`); confidence collapse latches one event and changes nothing else; destitution is rescued every Morning; rent/dues cap out. The only irreversible things in the entire simulation are hero deaths and the arc's forward day-stamps.
11. **Counter haggling cannot lose a sale by countering.** Every `Counter(price)` closes the sale — above the ceiling is a *fleece* (sale + mood penalty), not a refusal (`HaggleResolver.cs:124-159`). Only patience exhaustion or an unwanted item walks. The risk the UI implies ("push too hard and lose them") does not exist within a round.
12. **Rival competition is a price knob.** The rival never reacts to the player's stock, never improves, never runs out; it re-mints 6 fixed Common items every morning and discounts them only by the idleness meter (`RivalRestockSystem.cs:36-76`).
13. **Quality's ceiling logic makes ore, not skill, the gate to the top band** — an even-tier material caps at Superior no matter how perfect the trace; over-tier ore uncaps it (`QualityRoller.cs:160-170,185-190`) — while auto-craft is capped at Superior no matter the material. Masterwork therefore requires BOTH the minigame AND over-graded ore (or the CLI-only masterwork/legendary sinks).

---

## Appendix: file → subsystem index

| Subsystem | Files |
|---|---|
| Kernel | `sim/GameSim/Kernel/{GameKernel,ActionTiming,GameFactory,Pcg32,IntegerCurves,SaveCodec}.cs`, composition root `sim/GameSim/GameComposition.cs` |
| Contracts (deny-listed) | `sim/GameSim/Contracts/*.cs` |
| Crafting | `sim/GameSim/Crafting/{CraftingHandlers,QualityRoller,ItemForge,RecipeTable,TalentTree,CraftModifiers,ForgeScorer,ForgePath,ForgeTraceInput,HeatBandForge,ArtifactSigning,HeirloomHandlers}.cs` |
| Heroes | `sim/GameSim/Heroes/{HeroRoster,HeroShoppingSystem,ShoppingAi,PartyFormation,MusterSystem,CommissionSystem,CommissionHandlers,TraitDefinition,TraitEffects,NeedsSystem,RelationshipBands,RelationshipSystem,HeroXp,HeroIdentity,HeroOps,RaidForecast}.cs` |
| Expedition | `sim/GameSim/Expedition/{ExpeditionSystem,ExpeditionDeepSystem,ExpeditionResolver,CombatMath,AttributionEngine,CampHandlers,MonsterTable}.cs`, `sim/GameSim/Venues/*` |
| Economy | `sim/GameSim/Economy/*.cs`, `sim/GameSim/Materials/*` |
| Drama | `sim/GameSim/Drama/*.cs`, `sim/GameSim/Arc/ArcDirectorSystem.cs`, `sim/GameSim/Factions/*` |
| Counter | `sim/GameSim/Counter/{CounterHandlers,CounterQueueSystem,HaggleResolver,WillingnessModel}.cs` |
| Advisor (read models) | `sim/GameSim/Advisor/*.cs`, `sim/GameSim/Drama/{DemandBoard,GoldLedger,LedgerQuery,LegendQuery,DayLog}.cs`, `sim/GameSim/Progression/*` |
| Surfaces | `godot/scripts/panels/*`, `godot/scripts/minigames/*`, `godot/scripts/town2d/*`, `godot/scripts/{MainUi,SimAdapter}.cs`; CLI `sim/GameSim.Cli/Program.cs` |
