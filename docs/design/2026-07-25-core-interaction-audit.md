---
title: Maker's Mark — Core Interaction Audit
date: 2026-07-25
kind: audit
provenance: 10-agent fleet (sonnet) + fable synthesis; target C:/Code/Game/play @ b4a1ada (main)
---

# Maker's Mark — Core Interaction Audit

**Build audited:** `C:/Code/Game/play` @ `b4a1ada` — "feat(godot): world overhaul — purple-dusk village, scale, textures, NPCs, enemies, dressing (#209)" (main). All file:line cites below are against this tree. This document records **what is** — no fixes, no proposals (those live in the companion Improvement-Research Agenda).

---

## 1. Executive summary

Maker's Mark is an inverted-MMO shop-and-forge sim: the player is a blacksmith NPC; autonomous heroes raid the Mine. The audited build presents a fixed 5-phase day, 20 player action types behind 11 handlers, two client surfaces (text CLI, 3D Godot town), and a deterministic pure-sim kernel.

Findings in one paragraph: the **crafting/selling spine is genuinely strong** — the Anvil Map minigame is a real skill test with continuous per-mille scoring, rejections across all 20 verbs are uniformly typed and self-diagnosing, and empirical A/B runs prove price, recall, and activity are real outcome levers. Around that spine sit three systemic weaknesses, all evidence-verified: (1) a **feedback shadow** — 20 of 36 event types never narrate in the CLI and 11 narrate nowhere at all, leaving whole subsystems (bounty lifecycle, rent consequences, tariffs, commissions, market share) running invisibly; (2) a **surface split** — the Wave-3/4c content layer (commissions, memorials, heirlooms, bestiary) exists only in Godot, the alchemy profession is undiscoverable in the CLI, and one Godot panel (Depths) is reachable by no production affordance at all; (3) an **advisor blind spot** — the legality mirror that powers `advice` and the objective chip hard-codes `false` for 9 of 20 action types, so half the game's verbs can never be suggested. Two live personas independently report the loop turning repetitive at day ~11-13. Nine of thirteen prior playtest P0/P1s are verified fixed; the bounty-lifecycle P1 is the single most durable unfixed defect (three rounds).

Composite interaction grades (Section 3.4): strongest — Anvil-Map craft 14.5/16, RecallParty 14, Stock/SetPrice/SendSupply 13; weakest — AcceptCommission 5, DeclineCommission 5.5, Brew-Puzzle craft 6.5, HonorMemorial 6.5, PostBounty and SuggestItem 8.

---

## 2. Method and provenance

### 2.1 The ten workstreams

| ID | Method | Key artifacts |
|---|---|---|
| T1 verb-legality | Static trace of all 20 `PlayerAction` types, 11 handlers, every rejection string | code cites only |
| T2 surface-parity | Full read of `Program.cs` (936 lines) vs Godot panels | code cites only |
| T3 naive playtest | **Live CLI**, seed 2026, 15 days, 27 incremental deterministic reruns | `%TEMP%/claude/mm-audit/task3-naive/session.txt`, `run1-27.txt` |
| T4 optimizer playtest | **Live CLI**, seed 7777, 21 days, 29 reruns, deliberate abuse/neglect probes | `%TEMP%/claude/mm-audit/task4-optimizer/session.txt`, `run_turn29.txt` (2,030 lines) |
| T5 Godot UI map | Static UI/input trace + 5 rendered screenshots (Town/Forge/Shop/Tavern/Gate) | `%TEMP%/claude/mm-audit/task5-uisurface/shot_*.png` |
| T6 craft depth | Static trace of both minigames into scorer/quality/mint chain; hand-computed integer math | code cites only |
| T7 feedback map | 36-event × 4-surface narration matrix | code cites only |
| T8 agency | **Empirical**: 10-seed × 60-day batch (88,616 events) + 6 seed-locked A/B lever pairs (seed 555, 20 days/arm) | `%TEMP%/claude/mm-audit/task8-agency/` scripts + chronicles |
| T9 core loop | Kernel/phase/budget/rent trace + 20-day zero-input CLI run (seed 2026) | `%TEMP%/claude/mm-audit/task9-coreloop/out.txt` |
| T10 friction+intent | Rejection-path audit + prior-findings disposition vs roadmap/registry docs | code + docs cites |

Determinism was sanity-checked in T8: two byte-identical scripts produced byte-identical end states (CLAUDE.md rule 5 held). All live runs used `--no-build` against the pinned build; nothing was written under `play/`.

### 2.2 Synthesis verification pass

Because workers occasionally contradicted each other, the synthesis re-read the load-bearing files directly: `Advisor/ActionLegality.cs` (full), `Professions/ProfessionHandlers.cs:1-40`, `GameComposition.cs:50-84`, `sim/GameSim.Cli/EventNarration.cs` (full), `Program.cs:494-533` + full verb-case list, `Crafting/QualityRoller.cs:100-191`, `Crafting/ItemForge.cs:14-58`, `godot/scripts/minigames/AlchemyBrewPuzzle.cs:215-234`, `sim/GameSim.Cli/BatchRunner.cs:105-124`, `godot/scripts/ui/TutorialFlow.cs` (milestone gate), `godot/scripts/MainUi.cs:1395-1434` + repo-wide greps for `"Depths"` and `OpenPanel(`, and the roadmap's phase headings. Every claim marked **[verified]** below was confirmed first-hand.

### 2.3 Adjudications (worker conflicts, ruled with evidence)

1. **Depths drawer reachability — T2 vs T5.** T2's view matrix claimed "Gate interior → Depths drawer"; T5 found no route. **Ruling: T5 correct, and stronger than T5 claimed.** [verified] The only call of `OpenPanel("Depths")` in the entire `godot/` tree is `godot/tests/MainUiTests.cs:76`. Production callers pass only "Heroes"/"Bounties"/"Forge"/"Shop"/"Town" (`MainUi.cs:1234,1252,1266,1273,1288`) plus interior-hotspot actions, none of which is "Depths" (Gate interior exposes only "Bounty Board" — T5 screenshot + `OnInteriorHotspotActivated`, `MainUi.cs:1404-1426`). The DepthsPanel/MineWatch drawer is registered (`MainUi.cs:1022`), tested, and **orphaned in production UI**.
2. **Camp `advice` omitting `send` — T4 ("incomplete enumeration", bad-message) vs T1/code.** **Ruling: T4's observation is real, its attribution wrong.** [verified] `ActionLegality.LegalActions` does build SendSupply candidates (`ActionLegality.cs:187-202`) but only for an existing eligible consumable; on T4's day-2 Camp no consumable existed yet (first elixir crafted day 5). The omission was state-correct. The residual legibility fact stands: the list cannot distinguish "no eligible item exists" from "verb not applicable here."
3. **Bounty posting as a lever — T8's lever-3 verdict ("LEVER: depth/deaths move") vs T8's own acceptance data.** **Ruling: causal lever NOT established.** 0 of 435 bounty evaluations accepted at 20g/day; T8's own caveat notes divergence can propagate via RNG-stream perturbation. The measured Δ (deaths 6 vs 9, depth 4 vs 3) cannot be attributed to the bounty mechanic. Classified **theater at tested rewards** (Section 7.3).
4. **Rent-miss severity — T4 (P1) vs T7/T9 (P2).** **Ruling: P1 as a class.** The individual missing rent line is P2; but rent is one member of an 11-event hard-silent cluster that includes a fully modeled, never-surfaced consequence system (Confidence −150‰, MissedPayments, DestitutionRecovery). The cluster is catalogued as one P1 (FR-3).
5. **Ore-window severity — T7 (P1) vs T2 (P2)/T8 (P3).** **Ruling: P2.** The original P0 trap was fixed with an explicit warning ("buyable at TOMORROW's Evening prompt…", `Program.cs:878-880`); the remaining friction is a silently lapsing one-day window.
6. **T1 internal slip.** T1's flag heading said "CommissionHandlers doc comment is stale" but cited `ProfessionHandlers.cs:16-18`. [verified] The stale "REGISTRATION: … NOT yet wired" comment is in **ProfessionHandlers.cs**; `GameComposition.cs:76` registers it. T1's body was right, heading wrong.
7. **ActionLegality drift risk — T10 ("real drift risk, guarded only by a property test") vs the class doc ("kernel-parity test is the standing tripwire … never silently").** **Ruling: both partially right, and the blind spot proves the hole.** [verified] The tripwire is real for the 11 mirrored types — but 9 action types fall to `_ => false` (`ActionLegality.cs:50`) while the kernel accepts them, and the parity test evidently does not exercise those types (otherwise it would fail today). The guard's own tripwire misses exactly the region that is broken.
8. **"Floor-3 hard wall" (T4) vs floor 4 reached (T3 forecast; T8 arms).** **Ruling: progression plateau, not a cap.** Depth stalls at floor 3-4 by day ~11-15 across all seeds; T4's verbatim-repeating "No passage beyond floor 3!" is one seed's expression of the known day-10+ flatline (07-19 finding #10).
9. **Objective-chip clipping — T5 screenshot vs "gate-b fixes landed" code comments.** **Ruling: screenshot wins; F1-class residue persists at 1152px.** Live confirmation owed (T5 was static).
10. **`quit` prints no farewell — T10 (P2).** **Ruling: downgraded to P3** (no lost intent, cosmetic).

---

## 3. Interaction inventory

### 3.1 The 20 actions — inventory summary

`sim/GameSim/Contracts/Actions.cs:11-141` defines exactly 20 `[JsonDerivedType]` PlayerActions. `ActionBudget.cs:14-28`: 5 slots/day; only **Craft, BuyOre, BuyMaterial, PostBounty** spend one; everything else is free. Handlers are walked in fixed order, first match wins (`GameComposition.cs:70-81`, `GameKernel.cs:29-56`).

| # | Action | CLI verb (`Program.cs`) | Godot surface | Phase legality | Slot |
|---|---|---|---|---|---|
| 1 | CraftAction | `craft <recipe> <mat> [grade N]` :137 | ForgePanel minigame/brew | ALL | 1 |
| 2 | UnlockTalentAction | `talent <nodeId>` :173 | ForgePanel tree | ALL | free |
| 3 | StockAction | `stock <id> <price>` :232 | ShopPanel | ALL | free |
| 4 | SetPriceAction | `price <id> <gold>` :254 | ShopPanel | ALL | free |
| 5 | UnstockAction | `unstock <id>` :276 | ShopPanel | ALL | free |
| 6 | BuyOreAction | `buyore <hero> <mat> <qty>` :294 | LedgerModal buy row | **Evening** | 1 |
| 7 | BuyMaterialAction | `buymat <mat> <qty>` :214 | ForgePanel vendor rows | **Morning** | 1 |
| 8 | PostBountyAction | `bounty <floor> <gold>` :316 | BountyPanel | **Morning/Evening** | 1 |
| 9 | SetProfessionsAction | `profession <id> [id2]` :196 | NewGameSelect; tutorial 2nd-slot affordance | ALL | free |
| 10 | SendSupplyAction | `send <hero> <item>` :338 | CampPanel | **Camp** | free |
| 11 | RecallPartyAction | `recall <hero>` :360 | CampPanel | **Camp** | free |
| 12-16 | OpenCounter / Present / Suggest / HaggleResponse / CloseCounter | `counter open/present/suggest/close`, `haggle accept\|hold\|counter <g>` :382-480 | CounterPanel | **Morning** | free |
| 17 | AcceptCommissionAction | **ABSENT** | CommissionBoard.cs:91 | **Morning** | free |
| 18 | DeclineCommissionAction | **ABSENT** | CommissionBoard.cs:96 | **Morning** | free |
| 19 | HonorMemorialAction | **ABSENT** | LegendsWall.cs:107 | **Evening** | free |
| 20 | ReforgeHeirloomAction | **ABSENT** | LegendsWall.cs:146 | ALL | 1 |

The four ABSENTs are verified by the complete CLI verb-case list [verified — `grep 'case "' Program.cs`]: quit/exit, export, help, craft, talent, profession, buymat, stock, price, unstock, buyore, bounty, send, recall, counter(open/present/suggest/close), haggle(accept/hold/counter), next, day, status, recipes, talents, mats, items, heroes, shelf, forecast/telegraph, board, gossip, advice. No commission/honor/reforge/bestiary/legends/bounty-status verb exists.

### 3.2 Phase-legality matrix (kernel-authoritative)

L = handler accepts (subject to preconditions); — = kernel rejects with `"No handler accepts {Type} during {Phase}."` (`GameKernel.cs:44`).

| Action | Morning | Expedition | Camp | ExpeditionDeep | Evening |
|---|---|---|---|---|---|
| Craft, UnlockTalent, Stock, SetPrice, Unstock, SetProfessions, ReforgeHeirloom | L | L | L | L | L |
| BuyMaterial | L | — | — | — | — |
| BuyOre | — | — | — | — | L |
| PostBounty | L | — | — | — | L |
| Send, Recall | — | — | L | — | — |
| Counter × 5 (open/present/suggest/haggle/close) | L | — | — | — | — |
| Accept/DeclineCommission | L | — | — | — | — |
| HonorMemorial | — | — | — | — | L |

Decision mass by phase: Morning 16 legal types, Evening 10, Camp 9, Expedition/Deep 7 (only the anytime verbs). Two of five phases have zero phase-specific verbs — pure spectation.

### 3.3 Surface-parity matrix

**Verbs:** zero CLI-only verbs; four Godot-only verbs (#17-20 above). **Same-verb expressiveness differences:**
- **Craft grade:** CLI takes a bare typed int 0-1000 (`Program.cs:143-157`); Godot derives grade from a real minigame trace/brew (`CraftAction.Puzzle`; `ForgePanel.cs:285-330`). The CLI never exercises the Puzzle scoring path — typed grade bypasses the skill layer entirely.
- **CloseCounter:** Godot's Skip/bell button auto-queues a close with a toast (`MainUi.cs:887-893`); CLI has no implicit close.
- **BuyOre:** identical rule (Evening-only, next day) but Godot presents a clickable ledger row (`LedgerModal.cs:129`) while CLI requires retyping the offer at tomorrow's Evening prompt (`Program.cs:874-881`).
- **SetProfessions:** CLI free anytime; Godot mid-game entry point exists only via the tutorial's earn-2nd-profession affordance (`TutorialFlow.cs:101`; `MainUi.cs:1344`), gated on `state.Bounties.Any(b => b.Paid)` [verified — `TutorialFlow.cs:322`]. No bounty payout was observed in any run (Section 7.3).

**Info views:**

| View | CLI | Godot |
|---|---|---|
| status/recipes/talents/mats/items/heroes/shelf/forecast/board/gossip/advice | yes | yes (HUD chips, panels, boards) |
| Evening ledger, camp slate | inline prints | LedgerModal (auto, Return Ritual), CampPanel (auto) |
| Bestiary, Commission board, Legends Wall, Provenance card | **no CLI command** | yes |
| Depths drawer (venue grid + MineWatch strip) | `board` covers the leaderboard | **registered but unreachable** [verified] — only caller is `MainUiTests.cs:76` |

Dead-code parity artifact [verified]: `EventNarration.cs:50-53` carries narration lines for `MemorialHonored` ("the town bids farewell…") and `HeirloomReforged` — but the CLI has no verb that can ever emit those actions, so both lines are unreachable in CLI play.

### 3.4 Eight-axis grades

Scale per axis: **0** absent/broken/illegible · **1** present but partial/shallow · **2** solid. Halves used where surfaces split (CLI/Godot averaged). Axes: A1 discoverability, A2 decision-quality, A3 depth/mastery, A4 feedback-legibility, A5 consequence-weight, A6 failure/recovery, A7 surface-parity, A8 fiction-fit.

| Interaction | A1 | A2 | A3 | A4 | A5 | A6 | A7 | A8 | **Σ/16** |
|---|---|---|---|---|---|---|---|---|---|
| Craft — Anvil Map (smith) | 2 | 2 | 2 | 1.5 | 2 | 2 | 1 | 2 | **14.5** |
| RecallParty | 1 | 2 | 1 | 2 | 2 | 2 | 2 | 2 | **14** |
| StockAction | 2 | 2 | 1 | 1 | 2 | 1 | 2 | 2 | **13** |
| SetPriceAction | 2 | 2 | 1 | 1 | 2 | 1 | 2 | 2 | **13** |
| SendSupply | 1 | 2 | 1 | 2 | 1 | 2 | 2 | 2 | **13** |
| UnlockTalent | 1 | 1 | 2 | 1 | 2 | 2 | 2 | 1 | **12** |
| HaggleResponse | 1 | 2 | 1 | 2 | 2 | 1 | 1 | 2 | **12** |
| BuyOre | 1 | 2 | 1 | 1.5 | 2 | 1 | 1 | 2 | **11.5** |
| ReforgeHeirloom | 0.5 | 2 | 1 | 2 | 2 | 2 | 0 | 2 | **11.5** |
| BuyMaterial | 2 | 1 | 1 | 0 | 1 | 2 | 2 | 2 | **11** |
| PresentItem | 1 | 2 | 1 | 1 | 2 | 1 | 1 | 2 | **11** |
| Unstock | 1 | 1 | 0 | 1 | 1 | 2 | 2 | 2 | **10** |
| OpenCounter | 1 | 1 | 1 | 2 | 1 | 1 | 1 | 2 | **10** |
| SetProfessions | 0.5 | 2 | 1 | 1 | 2 | 1 | 0.5 | 1 | **9** |
| CloseCounter | 1 | 1 | 0 | 0.5 | 1 | 2 | 1 | 2 | **8.5** |
| PostBounty | 2 | 1 | 0 | 0.5 | 1 | 0.5 | 1 | 2 | **8** |
| SuggestItem | 0.5 | 1 | 1 | 0.5 | 1 | 1 | 1 | 2 | **8** |
| Craft — Brew Puzzle (alchemy) | 0.5 | 0 | 0 | 2 | 1 | 2 | 0 | 1 | **6.5** |
| HonorMemorial | 0.5 | 1 | 0 | 1 | 1 | 1 | 0 | 2 | **6.5** |
| DeclineCommission | 0.5 | 1 | 0 | 0.5 | 0.5 | 1 | 0 | 2 | **5.5** |
| AcceptCommission | 0.5 | 1 | 0 | 0 | 1 | 0.5 | 0 | 2 | **5** |

### 3.5 Interaction cards (condensed; grades above, key evidence here)

**Craft (Anvil Map / smith).** 10 typed rejections, each naming value + rule (e.g. `"Recipe 'longsword' is tier 2; requires talent 'tier-2-smithing'."` — hit live, T3 day 8; `"Not enough iron: need 3, have 2."` — T3 day 9). Emits `ItemCrafted` (+`ItemSigned`); narrated `"⚒ forged Dagger [Superior]"` (T4 confirmed N1 fixed). CLI path skips the minigame (A7=1). A4 docked 0.5: the Godot grade preview runs the pure scorer without the material ceiling, so it can display a band the roll will clamp away (`ForgeMinigame.cs:378-381` vs `QualityRoller.cs:162-170`).

**Craft (Brew Puzzle / alchemy).** The ideal pour order is rendered unconditionally: `"Recipe notes — pour in order: …"` [verified — `AlchemyBrewPuzzle.cs:223-225`]; scoring is exact-2pt/misplaced-1pt multiset matching plus a flat talent bonus. A2/A3=0: optimal play is transcription. A1/A7=0-side: `recipes` never lists alchemy (hardcoded `RecipeTable.All` [verified — `Program.cs:499`], vs `talents` correctly iterating SelectedProfessions :509-522), and the id `alchemy` appears nowhere in CLI output (T4 guessed `alchemist`, got `"Unknown profession 'alchemist'."`, and found the real id only in source).

**UnlockTalent.** 4 clean rejections; free (no cost of any kind — `UnlockTalentLegal` checks only prereqs [verified — `ActionLegality.cs:361-380`]); no confirming event (verify via `talents`). Pull-only: nothing pushes the player toward the tree (T9's 20-day idle run never surfaced it).

**Stock / SetPrice / Unstock.** No events emitted (pure state writes — T7 verified no constructors in `ShopHandlers.cs`); only the `queued:` echo confirms. Stock has 6 typed rejections incl. the id-namespace trap hit live: `"Traveler's Sword (I2) is not player-crafted — only marked craft can be shelved."` (T3 guessed a sequential id; ids are global across the whole economy). Price accepts any positive int — `price I31 99999` accepted silently, producing `"~ Torvald passed on Dagger: can't afford at 99999g — has 122g"` from every hero every day thereafter (T4).

**BuyOre.** Evening-only, next-day window with explicit warning text (`Program.cs:878-880`). Success specially narrated `"⛏ bought Nx … from HX"` (`Program.cs:711-722`, the R1 fix, comment cites R1 by name). The faction tariff that moves the actual price (`TariffApplied`, the conservation-invariant delta per `Events.cs:94-99`) is silent everywhere.

**BuyMaterial.** The one action whose success narrates **nothing**: `MaterialPurchased` is emitted (`Events.cs:140`) but `EventNarration` has no case [verified] and no special-case block exists — the exact defect class patched for buyore, left open on the adjacent verb.

**PostBounty.** Escrow confirmed at queue time (`"queued: bounty — clear floor 3 for 10g (escrowed)"`, gold visibly drops). Afterward: nothing, anywhere, ever — no acceptance, payout, refund, or expiry line in CLI (`EventNarration` has zero Bounty* cases [verified]; `board` shows the unrelated depths leaderboard, `Program.cs:599-611`); Godot's BountyPanel renders `BountyJudged` only. Observed: T3 10g/floor-3 — floor cleared 6+ times, silence; T4 20g/floor-3 — 14 days silence, escrow never returned; T8 20g/day — **0 accepted of 435 evaluated**.

**SetProfessions.** Dual professions work mechanically (T4 ran alchemy+blacksmith; grade dominance applied to both). CLI id discovery is broken (above). Godot mid-game entry is tutorial-gated on first `BountyPaid` [verified — `TutorialFlow.cs:322`, class doc :46-50 states this deliberately] — see Section 7.3 for why that milestone was never observed.

**SendSupply.** The best-guarded verb in the game: 11 rejection strings, all specific (`"Dagger (I1) isn't a consumable — the runner carries consumables only."`, `"{item} is shelved — unstock it first."`, `"One runner per party per day — this party's delivery is spent."`). Delivery narrated with fee. One-per-party-per-day; fee = 6 + 3×checkpoint floor. Residual illegibility: a delivered consumable remains listed in `items` (T4 could not tell record from duplicate).

**RecallParty.** `"⤺ recall bell — [party] bank and surface"` plus in-fiction abandonment lines. The largest measured lever in the game (Section 7.2). Party-level only; no per-hero recall exists.

**Counter service (Open/Present/Suggest/Haggle/Close).** The stepped-session model: an open session holds the day at Morning across ticks (`GameKernel.cs:112`); customer responses/offers materialize between ticks via `CounterQueueSystem`, so a full round is present → `next` → respond → `next`. Nothing in `help` states this (T3 learned it via a rejection cascade; T8's same-batch script collapsed to a silent no-op byte-identical to never opening the counter). Haggle itself is the game's best moment when stepped correctly — `"★ Torvald buys Buckler for 25g — you read them perfectly"` (T3), and holding firm measurably improves offers (31g → 34g, T4). The only counter-offer ceiling is the buyer's purse: a 30g-priced dagger sold for 60g; the sole bound found was `"Countered price 5000g exceeds what the hero can afford (26g)."` (T4). Suggest on the wrong slot is a deliberate silent legal no-op (`CounterHandlers.cs:110-114`). Presenting a different item mid-round silently abandons the round (`CounterHandlers.cs:84-108`).

**Accept/DeclineCommission.** Godot-only. Commissions populate only when a hero has a gear-slot gap (never fired in T9's craft-nothing run — expected). Downstream `CommissionFulfilled`/`CommissionExpired` narrate nowhere; the premium gold is indistinguishable from an ordinary sale (T7).

**HonorMemorial.** Godot-only verb (LegendsWall); Evening-only; repeat-honor is a silent idempotent no-op by design. Its CLI narration line is dead code (Section 3.3). Consequence channel exists in the sim (`LegendQuery` seeds recruit opinion) but is unmeasured.

**ReforgeHeirloom.** Godot-only. 10-step guard chain mirroring craft plus provenance (`"{item} was never worn by a fallen hero."`, `"…has already been reforged."`). Narrated + rendered on LegendsWall; mints `HeirloomLineage`. The game's thesis ("craft writes legends") distilled — invisible to CLI play.

---

## 4. The core loop as played

**Kernel day** (`GameKernel.cs:110-119`): Morning → Expedition → Camp → ExpeditionDeep → Evening → next day. An open counter session holds Morning across ticks. Morning system order [verified — `GameComposition.cs:52-69`]: FactionDrift → CounterQueue → Rent → DestitutionRecovery → RivalRestock → Recruit → Gossip → HeroShopping → Commission → Muster; then BountyJudging → Expedition (stage 1 to checkpoint floor 1); Camp (player verbs only); ExpeditionDeep (stage 2); Evening: ExpeditionReveal → BountyPayout → MarketShare.

**Decision structure.** All decisions are opt-in. T9's 20-day zero-input run (only `status`/`day`) progressed fully: recruits arrived, parties raided, six heroes died, rent auto-paid twice — `"actions left today: 5/5"` on all 21 status prints. The 5-slot budget binds only under deliberate multi-craft bursts (T4 hit it once queuing 3 crafts + prior spends in one day). Camp's send/recall window passes with zero signal if unused. The only mandatory beat is rent: every 10 days, silently deducted (gold 100→70 across two status prints, T9; 58→28, T3; 33→3, T4), next ask escalating +150‰ on-time / +350‰ on miss, with a Confidence stat (−150‰ per miss) and a destitution floor (`DestitutionRecoverySystem`) guaranteeing no softlock — **none of which prints anywhere** (`EventNarration` has no Rent* case [verified]).

**Time cost.** Godot defaults to bell-not-clock: `AutoAdvance = false` (`PhaseClock.cs:45`), so a phase costs 0s until the player rings the bell; the opt-in legacy timer pins Morning 45s / Expedition 30s / Evening 45s with Camp/Deep borrowing 45s (deliberate fallback, `PhaseClock.cs:73-76`). CLI is fully keystroke-paced (`next` = one tick; `day` loops to next Morning).

**Player-economy shape** (T8 batch, 10 seeds × 60 days): hero loot income 122,418g (204g/day) flows to **hero wallets**, not the player; player income is sales only — 214 player sales (8,260g) vs 387 rival sales. Of 56,806 hero shopping passes: 50,709 "current gear is better", 3,204 "can't afford", 657 "too heavy for role", 102 "role doesn't use shields" — per-hero gear-score-per-gold evaluation is real, not decorative.

**Repetition onset.** Independently, the naive persona (seed 2026) and the optimizer (seed 7777) report days turning identical at **day 11-13**: same three floors (Cave Rat → Tunnel Spider → Deep Ghoul), verbatim-repeating depth-stall lines, static advice (`advice`'s top suggestion unchanged for 15+ days regardless of a 99999g dead listing, an unresolved bounty, or zero income), and tier-1 goods uniformly passed on ("current Soldier's Sword is better") for days before the player pivots. Depth progression stalls at floors 3-4 across all runs (adjudication #8).

**A 15-day naive arc exists and lands.** T3, unaided: day-1 craft+sell funnel via the `suggestion:` line → first death day 1 ("† Moss died on floor 1 — slain by a Cave Rat") → botched then mastered stepped haggle (day 2 → day 4) → tier-2 gate discovered by rejection, talent unlocked, iron banked (days 8-9) → `"$ Torvald bought Longsword for 40g from YOUR shop"` → `"★ Torvald — Longsword landed the killing blow"` across three consecutive floors. The attribution arc — from "everyone rejects my gear" to "my sword is doing the killing" — is the strongest story beat either persona found.

---

## 5. Crafting and mastery

**Pipeline** (both professions): input capture (Godot) → `CraftAction(Puzzle: trace, PerformanceGrade: null)` → sim scorer regenerates the same target deterministically (`ForgePath` via StableHash — no engine RNG) → `GradePermille` → `QualityRoller.RollActive` → band → `ItemForge` stat mint. **Exactly one RNG draw per craft** — the quality jitter (`Roll100()` → ±25 per-mille, `QualityRoller.cs:154-156`); scoring, path generation, and signing draw none.

**The quality math** [verified — `QualityRoller.cs:100-191`]: `effective = clamp(grade ?? 550, 0, 1000) + jitter(±25)`; bands Poor <200, Common <550, Fine <780, Superior <930, Masterwork ≥930. Material ceiling from `materialStep = materialGrade + mastery − recipe.Tier`: ≤−1 caps Fine; **0 (the recipe's own baseline ore) caps Superior**; ≥+1 uncapped. Auto-craft (null grade = 550) additionally hard-caps Superior — "the minigame is the only road to Masterwork" (code comment).

**Worked deltas** (dagger, copper, base Attack 8; `ItemForge.QualityPercent` = 80/100/115/135/160 [verified]):

| Input | Band | Attack |
|---|---|---|
| grade 0 | Poor (always) | 6 |
| null (CLI auto / no minigame) | Common or Fine, ~50/50 on the roll | 8 or 9 |
| grade 1000, baseline ore | Masterwork → **clamped Superior** | 10 |
| grade 1000, above-tier ore (counterfactual) | Masterwork | 12 |

So skill buys +4 Attack (6→10, +67%) on baseline material, and **everything above grade 930 is output-indistinguishable on baseline ore** — Masterwork is mathematically unreachable there regardless of a perfect trace. Nothing in the run UI reflects this: the Godot preview band comes from the pure scorer only (T6). Consumable potency also scales with the same percent (`ItemForge.cs:51-53`). Price is never stat-derived — the player names it; heroes act on `GearScore = Σ(Attack+Defense)` per gold.

**The two professions differ in kind** (T6): the Anvil Map is a continuous real-time execution test (two gauges sampled up to 256×, strike-tempo axis, 300/400/300 zone fold, three independent talent-forgiveness channels) with a fully visible but physically demanding target; the Brew Puzzle is discrete, un-clocked, and ships with its answer key permanently rendered — its own class doc flags hiding the notes as "deliberate later tuning" (`AlchemyBrewPuzzle.cs:30-33`). T6's depth scores: Anvil decision 1 / depth 2; Brew 0 / 0. Live corroboration: grade-in-hand dominance held at the extremes for both professions (grade 0 → Poor; 1000 → Superior; 999 elixir → Superior — T4 day 6).

**CLI bypass.** The CLI's `craft … grade N` submits `PerformanceGrade` directly and never constructs a Puzzle — on the text surface the entire skill layer is a typed integer.

---

## 6. Feedback and legibility

**Four surfaces:** (1) CLI immediate `queued:` echo (acceptance into the batch, not legality); (2) CLI tick resolution — `REJECTED: <Type> — <reason>` + `EventNarration.Line` per event; (3) Evening ledger (`LedgerQuery.ReturnCards`); (4) Godot (AdventureTicker narrates 6 event types; several panels read state directly).

**Narration coverage** [verified — `EventNarration.cs` full read]: **16 of 36 event types have CLI lines** (ItemCrafted, ItemSold-from-player-shop, HeroPassedOnItem, PartyDeparted, AttributionBeat, HeroDied, SupplyDelivered, PartyRecalled, RecruitArrived, GossipEmitted, CustomerApproached, CustomerCountered, CounterSaleClosed ×2 forms, CustomerWalked, MemorialHonored, HeirloomReforged). Of the 20 silent types, 4 are absorbed by the Evening ledger (PartyReturned, LootIncomeReceived, OreOffered, FloorRecordSet), 4-5 by Godot panels reading state (PartiesFormed → JourneyStream, BountyJudged → BountyPanel, CommissionPosted → CommissionBoard, PartyCampReport → camp slate), leaving a **hard-silent core of 11 with no surface anywhere**: `TariffApplied, RecoveryStipendGranted, FactionStandingShifted, RentPaid, RentMissed, MarketShareShifted, CommissionFulfilled, CommissionExpired, ItemSigned, BountyPosted, BountyPaid`.

**Can the player reconstruct why gold changed each day?** Partially. Legible: craft→sale (price+buyer narrated), counter sales, expedition loot (ledger), buyore (patched confirm), rent countdown chip (ambient). Not reconstructable from any output: rent actually being paid/missed that day; tariff surcharges/discounts on ore; destitution stipend top-ups (gold appears with no stated cause); commission premiums (look like ordinary sales); market-share drift. T4's day-20 missed rent produced **zero text** while the sim recorded a −150‰ Confidence hit and escalated the next ask 35%.

**Per-action feedback quality** (T7's 0-2 grades): 2 — Craft, OpenCounter, Haggle, BuyOre, Send, Recall, Honor, Reforge; 1 — Present, Suggest, Close, Stock, SetPrice, Unstock, PostBounty (echo only), UnlockTalent, SetProfessions, DeclineCommission; **0 — BuyMaterial, AcceptCommission** (success indistinguishable from silence).

**Advisor blind spot** [verified — `ActionLegality.cs:37-51`]: `IsLegal` mirrors 11 of 20 action types and returns `false` for the other 9 (all counter verbs, both commission verbs, HonorMemorial, ReforgeHeirloom). Since `LegalActions` feeds both the CLI `advice` listing and `ObjectiveAdvisor.Suggest` (which drives the Godot objective chip), **no surface can ever suggest half the game's verb families**, even in phases where the kernel accepts them. The class doc's drift tripwire (kernel-parity property test) demonstrably does not cover these types — it passes today with the blind spot in place. Consequences observed live: T3's six witnessed deaths never produced a death-adjacent suggestion (HonorMemorial exists but is advisor-invisible and CLI-absent); T4's advice repeated one suggestion verbatim for 15+ days.

**Onboarding surfaces that work:** the day-1 `suggestion:` funnel (both personas), the 5-step tutorial chain with an explicit anti-softlock edge (Craft advances on `ItemCrafted` directly because starter copper covers a day-1 craft — `TutorialFlow.cs:279-288`), three-tier CLI error separation (unknown verb / wrong shape / bad value with id-format help — the 07-19 #1/#2 fixes, confirmed in `CliParse.cs`), and phase-illegal pre-checks at input time naming the verb and phase (N3/R3 fixes, `Program.cs:685-698`).

---

## 7. Agency measurements (empirical)

### 7.1 Baseline
10 seeds × 60 days under `BaselinePlayer` (88,616 events). Hero purchasing is genuinely reactive to shelf/price (Section 4 pass-reason distribution). 13 anomaly hits (2 MEDIUM gold-mint-spike, 1 MEDIUM death-spike, 10 LOW tariff-saturation). The batch harness cannot exercise any other policy: `BatchRunner.cs:117` hardcodes `BaselinePlayer.ActionsFor` [verified]; the unit-tested `CounterPlayer` harness policy is unreachable from `batch`.

### 7.2 Seed-locked A/B levers (seed 555, 20 days/arm, one variable per pair)

| Lever | Δ deaths | Δ deepest | Δ final gold | Δ player sales | Δ hero loot | Verdict |
|---|---|---|---|---|---|---|
| L1 idle vs daily buymat+craft | 9 vs 5 | 3 vs 4 | 35g vs 0g | 0 vs 1 (50g) | 2,737 vs 3,568g | **LEVER** (activity moves world outcomes; note the naive active arm ended with *less* gold than idling) |
| L2 price 5g vs 500g | 5 vs 6 | 3 vs 3 | 28 vs 23g | 1 sold vs 0 unsold in 19 days | 3,059 vs 2,710g | **LEVER** (price directly controls sale/no-sale) |
| L3 bounty 20g/day vs none | 6 vs 9 | 4 vs 3 | 40 vs 35g | 0 vs 0 | 3,299 vs 2,737g | **NOT ESTABLISHED** — 0/435 acceptances; divergence attributable to RNG-stream perturbation (adjudication #3) |
| L4 recall daily vs never | **2 vs 13** | **1 vs 4** | 4 vs 4g | 0 vs 0 | **928 vs 3,013g** | **LEVER — largest measured effect**: survival traded for all depth/loot |
| L5 stepped counter vs atomic | identical | identical | identical | 0 vs 0 | identical | **INCONCLUSIVE** — same-batch scripting silently collapsed the session (Section 3.5) |
| L6 grade-1000 vs auto-craft (same 50g price) | 4 vs 5 | 3 vs 3 | 73 vs 73g | 1 vs 1 (both sold) | 2,647 vs 3,191g | **LEVER via combat stats** (atk 13 vs 10), indirect on divergence; grade→willingness-to-pay not isolated |

Causal-attribution caveats preserved: several divergences propagate through the deterministic RNG stream (different action counts shift downstream draws) — "outcome moved" ≠ "the intended mechanic moved it" except where the wire is direct (L2 price→sale; L4 recall→depth). `send` was never isolated (570 × `"The recall bell has rung — the runner won't chase them."` after same-batch recalls).

### 7.3 The bounty/second-profession coupling (synthesis finding)

Across all observation: T8 counted **0 accepted of 435 bounty evaluations** at 20g/day; T3 (10g) and T4 (20g) saw no acceptance, payout, refund, or expiry over 10 and 14 bounty-days respectively; T4's escrowed 20g never returned; the T8 baseline batch recorded bounties 0/0. Meanwhile the Godot tutorial's earn-2nd-profession affordance is deliberately gated on the first `BountyPaid` [verified — `TutorialFlow.cs:46-50, 322`], and Godot has no other mid-game profession entry point (T2). As shipped and at the reward levels players were observed choosing, the second-profession milestone did not occur in ~55 cumulative bounty-days. Whether higher rewards produce acceptances is undetermined (see Section 9.4).

---

## 8. Friction and failure catalog (de-duplicated, severity-ranked)

52 raw friction entries from ten logs merged to 29 unique findings. Format: severity — finding [contributors] evidence.

### P0
- **FR-0 (process) Gate-B rev.2 acceptance for the 3D town has never been run.** [T10] `docs/design/playtest-gate-b-3d.md` is the blank score-sheet; the redesign + open-questions docs exist, no filled findings doc does. Until run, "the 3D town hub is acceptance-gated" is an unverified claim, with F1 (menu off-screen) and F2 (profession doesn't land) still open per that doc's own disposition.

### P1
- **FR-1 The bounty lifecycle is invisible end-to-end.** [T3+T4+T7+T8+T10; most durable unfixed P1 — carried 07-19 #4 → 07-20 → gate-a-rerun → today] Escrow confirmed at queue; thereafter zero output on any surface (`EventNarration` has no Bounty* case [verified]; `board` shows depths, `Program.cs:599-611`; no bounty-status verb exists; Godot shows judgments only). Compounded by 7.3: 0/435 acceptance at tested rewards and the Godot 2nd-profession gate depends on a payout.
- **FR-2 The Wave-3/4c content layer does not exist on the CLI surface.** [T1+T2] No verb for Accept/DeclineCommission, HonorMemorial, ReforgeHeirloom (verified against the full case list); no read view for Bestiary, Commission board, Legends Wall, Provenance; the CLI narration lines for MemorialHonored/HeirloomReforged are dead code [verified]. A console player cannot learn these subsystems exist.
- **FR-3 The silent-economy cluster: 11 event types narrate nowhere.** [T4+T7+T9; class-P1 per adjudication #4] RentPaid/RentMissed (with modeled Confidence −150‰ and MissedPayments), TariffApplied (the conservation-invariant delta), RecoveryStipendGranted (gold appears uncaused), MarketShareShifted, CommissionFulfilled/Expired (premium indistinguishable from a sale), ItemSigned, BountyPosted/Paid — plus BuyMaterial success printing nothing (the patched-for-buyore N1 class, unfixed on the adjacent verb, `Events.cs:140` vs `EventNarration` [verified]). Net: day-over-day gold deltas from these systems must be reverse-engineered from the raw total.
- **FR-4 The advisor cannot see half the game.** [T1+T4; verified `ActionLegality.cs:37-51`] 9 of 20 action types fall through `_ => false`, so `advice` (CLI) and the objective chip (Godot) can never suggest counter service, commissions, memorials, or heirloom reforging; and the suggestion engine does not evolve with state (one suggestion verbatim for 15+ days, T4). The parity-test tripwire provably does not cover the blind region (adjudication #7).

### P2
- **FR-5 The Godot Depths drawer is orphaned.** [T5, synthesis-verified; T2's contrary row overruled] Registered and tested; only caller of `OpenPanel("Depths")` is `MainUiTests.cs:76`. No building, hotspot, HUD button, or objective binding routes to it. (Ranked P2 on player-harm — the leaderboard content is CLI-`board`/LegendsWall-visible — flagged for orphan-cleanup standards.)
- **FR-6 The counter's one-action-per-tick model is stated nowhere.** [T3+T4+T8] Interactive symptom: rejection cascade (`"No standing offer to respond to — present an item first."`) and a lost sale (T3 day 2). Scripted symptom: a same-batch open→present→haggle→close collapses to a silent no-op byte-identical to never opening the counter (T8 L5). `help` (`Program.cs:116-122`) never mentions the intervening-`next` requirement.
- **FR-7 `recipes` is hardcoded to blacksmith.** [T4; verified `Program.cs:499` vs the per-profession `talents` at :509-522] With alchemy selected, the player has zero in-game way to discover their own craftable recipe ids. Root of the F2 "profession doesn't land" finding at the code level.
- **FR-8 Profession ids are undiscoverable in the CLI.** [T4] `profession alchemist` → `"Unknown profession 'alchemist'."`; the valid id appears in no CLI output; no list form of the verb exists.
- **FR-9 Locked recipes are unmarked in `recipes`.** [T3] Tier-2+ entries print identically to craftable ones; the gate is discovered only via rejection.
- **FR-10 No price sanity bound.** [T4] `price I31 99999` accepted; the item becomes permanent dead shelf-space generating identical per-hero "can't afford" passes daily; no signal distinguishes a misprice from a slow market.
- **FR-11 Global item-id namespace trap.** [T3] Sequential-id guesses land on rival/NPC items (`stock I2` rejection); ownership must be looked up via `items` after failure.
- **FR-12 Skill above the material ceiling is silently discarded and the preview overstates.** [T6] Grades 930-1000 collapse to Superior on baseline ore; the Godot preview band ignores the ceiling entirely (`ForgeMinigame.cs:378-381` computes from the pure scorer).
- **FR-13 The brew puzzle ships its answer key.** [T6; verified `AlchemyBrewPuzzle.cs:223-225`; class doc flags as deferred tuning] The two professions' active-craft loops are radically unequal in depth (14.5 vs 6.5 composite).
- **FR-14 The ore-offer window silently lapses.** [T2+T3+T7+T8; adjudicated P2] One-Evening window with explicit warning text; T3 still missed a batch by advancing past it; nothing re-lists or reminds.
- **FR-15 Objective-chip clipping at 1152px.** [T5 screenshot; F1-class residue; adjudication #9] Tutorial step text visibly clipped by the right viewport edge in the bare-Town screenshot despite "gate-b fixed" comments in code. Live confirm owed.
- **FR-16 Recruit name reuse collides with permanent records.** [T3; prior-doc name-pool collisions T10] A new "Orin" arrived the day after hero "Orin" died; day-15 `board` lists "Orin: floor 3" twice with no disambiguator.
- **FR-17 State-illegal actions still queue-then-reject a phase later.** [T10; R2, documented deliberate scope] Phase-illegality rejects at input (fixed); bad material/floor/qty still `queued:` then `REJECTED:` at `next`.

### P3
- **FR-18 Phase order never enumerated** — the 5-phase cycle is learned only via phase-rejections [T4].
- **FR-19 `mats` empty-state misdirects** — "buy ore from returning heroes (Evening)" omits the Morning vendor that the very next suggestion line recommends [T3; verified `Program.cs:527-529`].
- **FR-20 Advice cannot distinguish "no eligible item" from "not applicable"** — the reclassified send-omission [adjudication #2].
- **FR-21 Delivered consumables remain listed in `items`** with no consumed/record distinction [T4].
- **FR-22 Shelf actions have no resolution confirmation** — Stock/SetPrice/Unstock/UnlockTalent/SetProfessions/DeclineCommission emit no events; silent acceptance and silent idempotent no-op look identical [T7].
- **FR-23 Heroes vs decorative townsfolk are visually ambiguous** — same mesh families; only a cooler tint + "· townsfolk" label; no hover/highlight differentiation for NPCs [T5].
- **FR-24 The 5-slot action budget is imperceptible** — never binds in normal play; a player can go 20 days without evidence it exists [T9; binds only under bulk-craft bursts, T4].
- **FR-25 The Camp window closes with no missed-opportunity signal** [T9].
- **FR-26 `quit`/`exit` print nothing** [T10; downgraded per adjudication #10; `Program.cs:85-86`].
- **FR-27 Stale registration doc-comment on ProfessionHandlers** claims it is unwired; `GameComposition.cs:76` registers it [T1; verified].
- **FR-28 No overproduction signal beyond per-hero pass-lines** — T3 crafted obsolete daggers for 3 days; the signal exists (accurate pass reasons) but nothing aggregates it [T3; genuine-design].
- **FR-29 Haggle counter-ceiling is purse-only** — no fairness/negotiation band; 2× list price accepted (also listed as a depth observation, Section 3.5) [T4].

### Tooling/process debts (not player-facing)
- `BatchRunner.cs:117` hardcodes `BaselinePlayer`; the tested `CounterPlayer` policy is unreachable from `batch` [T8; verified] — blocks at-scale measurement of counter service.
- `ActionLegality` is a hand-maintained mirror whose tripwire test misses the 9 uncovered types [T10 + adjudication #7].
- Send-vs-recall cannot be isolated in one script (correct mutual exclusion; scripting hazard) [T8].

### Confirmed fixed (baseline honesty)
07-19 #1/#2 id-format traps (CliParse, live-confirmed); #3 buyore timing hint; N1 silent craft ("⚒ forged" live both personas); N2 non-id errors; N3 phase-illegal silent queue (immediate named-verb rejections live); N4 death signaling ("† … slain by …" + roster "DIED day N", live T4); R1 buyore silent success (`Program.cs:711-722`, comment cites R1); R3 wrong-phase message names the verb; tutorial craft-skip softlock guard; destitution no-softlock floor (`NoSoftlockTests` pins the edge cases — a genuinely unrecoverable state does not exist in the current sim).

---

## 9. Intent vs reality

### 9.1 Roadmap frame
[verified — `docs/plans/2026-07-21-003-phased-roadmap.md`] Spine: "your craft writes the legends"; target: content-complete skeleton. Phases: **A — The Legend Engine**, **B — Living Heroes**, **C — The Hardening Window** (includes the active-craft modifier layer), **D — Completeness & Arc**; plus a per-system Completeness Bar (8 points) and minimum content counts. Several audited weaknesses are **scheduled work, not defects**: hero leveling/XP always-L1 (Phase B), traits/needs (B), talent costs and economy sinks (C/D), per-profession minigame differentiation (C — the Brew Puzzle's answer key is explicitly deferred tuning), day-10+ flatline/arc (C/D).

### 9.2 Prior-findings disposition (13 tracked P0-P2 classes)
Fixed and verified: 9 (see catalog above). Persisting as designed/scheduled: state-illegal queue-then-reject (R2, deliberate); talents free (Phase C/D); hero level static (Phase B); day-10 flatline (C/D). **Persisting as defect: the bounty lifecycle (FR-1), three rounds running.** Unverified this pass: 07-19 #7 empty-state consistency (no recurrence observed in ~56 live days, but not systematically re-tested); #9 name collisions re-observed live (FR-16); R4 retreat prose not re-read.

### 9.3 Ledger/reality mismatches
- `SYSTEMS.md` marks **Bounties/agency "partial ●●●○●○○●"** with Feedback (bar-point 3: "every consequence visible and attributable within one loop") counted as passing — contradicted by FR-1: the lifecycle has zero feedback surface. Clearest registry overstatement found.
- Shop/counter Feedback "●" is borderline given R2 silent-until-next resolution on counter-adjacent verbs.
- Town/3D "flight" status is consistent with not-yet-gated, but rests on the unrun Gate-B (FR-0).
- `ProfessionHandlers` self-documents as unwired; it is wired (FR-27).

### 9.4 What could not be determined
- Whether any bounty reward level produces acceptances/payouts (only 10-20g observed; the mechanic may function at rewards no player was observed choosing).
- Haggle patience-exhaustion behavior (T4's probe was mis-designed and honestly flagged); counter-open on an empty shelf in Morning.
- Whether stepped counter service outperforms atomic shopping at scale (harness cannot run `CounterPlayer`; the one A/B collapsed — FR-6/L5).
- `send`'s isolated causal effect (never separated from recall).
- Whether the DepthsPanel's action-submission capability (comment at `MainUi.cs:1061`) implies additional player verbs are unreachable with it.
- Godot feel: no live Godot playtest occurred in this audit (T5 was static + screenshots); the Anvil Map's human feel-test remains owed from Wave 5; F1 chip clipping needs a live window-size check.
- Empty-state consistency (07-19 #7) — no systematic re-test.
- Consequence channels of HonorMemorial (recruit-opinion seeding) and relationship effects of DeclineCommission — modeled, unmeasured.