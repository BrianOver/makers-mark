# Maker's Mark — Gameplay-Loop Analysis (dense, for Claude/Fable)

*The analysis-grade companion to the human-facing "how you play" artifact. Compiled from FIVE intelligent, turn-by-turn playthroughs driven through the `decisions play` harness — merchant(seed 2026), legend-chaser(2026), hero-patron(2026, 18d), verb-catalog/explore(2026, 18d), merchant(seed 2027) — plus source grep of `ActionLegality.cs`, `ActionBudget.cs`, the handler files, and `CounterHandlers.cs`. (A 6th run, advisor-follower, was in flight at compile time; fold when available.) This doc is for reasoning/critique with Fable, not for humans — it is deliberately exhaustive and flags every mechanical caveat.*

Measured against `main`-based sim (Phase C/D live). Harness = deterministic index-replay; caveat that the harness enumerates legal actions from `ActionLegality.LegalActions`, which does NOT expose every implemented verb — see §5.

---

## 1. The loop, formally

**Core cycle:** ore → (Craft to a hero's gear gap) → Stock → hero buys (needs gold + matching slot/class) → hero clears a deeper floor → returns loot (gold + ore) → funds next craft + posts new (harder) commissions → some heroes die wearing your gear → ReforgeHeirloom the drop → legend/chronicle credits the smith by name → repeat.

**Governing constraint:** `ActionBudget.SlotsPerDay = 5`. Slot-consuming (5 of the "production" verbs): **Craft, BuyMaterial, BuyOre, PostBounty, ReforgeHeirloom** (+ the 4 never-enumerated Phase-D sinks). Free/unlimited (15): Stock, SetPrice, Unstock, UnlockTalent, SetProfessions, AcceptCommission, DeclineCommission, HonorMemorial, SendSupply(costs ~9g gold, no slot), RecallParty, OpenCounter, PresentItem, SuggestItem, HaggleResponse, CloseCounter. Slots reset only on the Evening→Morning day rollover.

**Day = 5 phases:** Morning (commissions post, shop pass, vendor, counter session) → Expedition (upper floors) → Camp (checkpoint: send/recall) → ExpeditionDeep (deep floors) → Evening (sales resolve, ore market w/ 1-day lag, memorial rite, ledger/rent).

**The two income channels (critical distinction, from the 2027 run):**
- **Atomic shopping pass** (automatic, commission-aware): every tick, matches shelved items to whichever alive hero needs+affords them. This is the dominant income engine — single-tick jumps of +91/+124/+158g once the shelf is broad. Requires only: accept commissions + stock broadly.
- **Manual counter/haggle session** (OpenCounter→Present→Haggle→Close): pitches shelf-item-#1 to a serve-queue. **Underperforms badly** (2027: ~2 sales / 18 presentations) because candidate-gen only ever offers the *first* shelf item regardless of the queued hero's need. Net verdict: **the counter is currently worse than not opening it**, except as a targeted lever to unstick one specific poor-buyer sale.

## 2. Per-run outcomes (all seed 2026 unless noted)

| Run | Intent | End gold | Deepest floor | Arc | Signature finding |
|---|---|---|---|---|---|
| Merchant 2026 | max gold | 100→513 (5.13×) | ~3–4 | II | counter/haggle unsticks a poor-buyer sale; sink layer unreachable despite 513g |
| Merchant 2027 | max gold (variance) | 100→578 (5.78×) | ~3–4 | II | **don't open the counter** — atomic pass beats it 10×; 5–6× ceiling is systemic |
| Legend-chaser | craft→legend | 100→37 | ~3–4 | II | **the reforge-heirloom legend loop is real** — smith credited by name in memorials |
| Hero-patron | push one hero deep | 100→132 | **4 (×5 heroes)** | II | **BuyOre = gold-gift to a hero** breaks the affordability logjam; floor-4 beatable by skill |
| Verb-catalog | exercise every verb | (n/a) | ~4 | II | precise mechanics of all 24 verbs; confirmed candidate-gen gaps by grep |

**Convergent across ALL runs:** day-1 double-death of 2 starting heroes (Striker+Mystic, both seeds); accept-every-commission is the free spine; the advisor names the exact stalled hero + gear-slot + quality-tier; Act flips I→II when heroes clear floor 3; everyone stalls at floor 4 (Superior+ gear bar for floor 5); Mystic class trends toward extinction, Striker hardiest.

## 3. Verb matrix (24 canonical `PlayerAction` types)

Legend: **slot** = consumes 1 of 5/day · **free** = unlimited · status: CORE / SITUATIONAL / INERT-HERE (offered but candidate-gen makes it a no-op) / UNREACHABLE-HERE (never enumerated).

| Verb | Cost | Legal when | Mechanics | Status |
|---|---|---|---|---|
| Craft | slot | ~any phase | material→item; quality capped by material-grade vs recipe-tier (Fine below / Superior at / uncapped above — Masterwork only via 3D minigame); item NOT auto-shelved | CORE |
| Stock | free | any, item unshelved | shelf it at a price → buyable | CORE |
| BuyMaterial | slot | Morning | +1 unit base material for gold; 1/action, no bulk discount | CORE |
| BuyOre | slot | Evening, 1-day lag | buy a returning hero's ore; **pays the hero full ask, charges you tariffed cost = a gold-gift lever**; cheap common / luxury-priced exotic | CORE (patron's key) |
| AcceptCommission | free | hero has open commission | lock named gear/quality/premium; silent (no event, count drops) | CORE |
| UnlockTalent | free | prereqs met | tier ceilings + recipes; all free | CORE |
| HonorMemorial | free | Evening AFTER the death | farewell rite; no economic effect; clears the advisor nag; unlocks legend legibility | SITUATIONAL |
| ReforgeHeirloom | **slot** | any; source = gear worn at a hero's death | consume heirloom+material → new item (quality independent, landed Fine); **the legend loop** | SITUATIONAL / thesis |
| DeclineCommission | free | same window as accept | refuse; dead heroes' commissions do NOT auto-clear — must decline | SITUATIONAL |
| SetPrice | free | item shelved | **INERT-HERE**: enumerator only builds the item's CURRENT price → repricing impossible via this surface | INERT-HERE |
| PostBounty | slot | Morning/Evening | escrow gold vs a floor-clear; silent lapse-refund; **enumerator only offers 1g escrow** → useless as offered | INERT-HERE |
| Unstock | free | item shelved | pull back to held; round-trips cleanly; used to make an item the sole shelf entry for Present | SITUATIONAL |
| RecallParty | free | Camp, party out | bank progress, pull home; forfeits deeper floors | SITUATIONAL |
| SendSupply | ~9g, no slot | Camp, party out, held item | deliver a held (unshelved) item to a camped hero; likely prevents death | SITUATIONAL |
| OpenCounter | free | Morning, 1/day | build serve-queue (band then id); promote first customer | SITUATIONAL (see counter verdict) |
| PresentItem | free | counter open, active customer, item shelved | show item; resolves same tick → walk or open a haggle round; **only shelf-item-#1 ever offered** | SITUATIONAL |
| SuggestItem | free | counter open | upsell if it fits an empty slot the hero wears; legal no-op otherwise | SITUATIONAL |
| HaggleResponse | free | a round is open (separate tick from Present) | accept / hold (offer drifts your way next round) / counter (**only the standing offer value offered = accept**) | SITUATIONAL |
| CloseCounter | free | session open | end service; unserved fall to atomic pass; **auto-closes when queue empties; forgetting holds the day at Morning** | SITUATIONAL |
| SetProfessions | free | Morning | pick 1–2 professions; re-select = no-op | one-time |
| UpgradeForge | slot | — | Phase-D gold sink (400/1600/6400/25600g ladder) | **UNREACHABLE-HERE** |
| BuyForgeSupply | slot | — | coal/flux sink | **UNREACHABLE-HERE** |
| MasterworkAttempt | slot | — | guaranteed Superior+ craft (would beat the floor-5 quality wall) | **UNREACHABLE-HERE** |
| CommissionLegendaryWork | slot | — | guaranteed Masterwork | **UNREACHABLE-HERE** |

## 4. Corrected conclusions (intelligent play overturned dumb-baseline findings)

- ❌ "Economy anemic / player broke" → ✅ **5.13× and 5.78× gold** over 15 days. Faucet works; dumb policy just never sold.
- ❌ "Dead verbs (reforge, etc.)" → ✅ **ReforgeHeirloom is the signature legend verb**; the "dead" list was a policy artifact.
- ❌ "Arc structurally stuck at floor 4" → ✅ **floor 4 beatable by skill** (5 heroes there, same seed) via BuyOre gold-gift + advisor-led gear crafting. **Floor 5 / Act III is UNTESTED** (needs Superior+ gear → the ActiveCraft minigame / Masterwork, neither reachable via the text surface), not proven-unreachable.

## 5. Mechanical gaps / bugs (all source-verified in `ActionLegality.cs`)

1. **Phase-D sinks never enumerated** — `UpgradeForge/BuyForgeSupply/MasterworkAttempt/CommissionLegendaryWork` have zero candidate-construction; implemented + working handlers, but unreachable via the guided surface AND un-suggestable by the advisor (which reads the same list). A rich player's surplus has nowhere sanctioned to go. (Pre-flagged as repo task "Advisor legality mirror for new sink + bounty actions" — never done.)
2. **SetPrice inert** — only the current price is offered → cannot reprice via the guided surface (`ActionLegality.cs:104`).
3. **PostBounty inert** — only 1g escrow offered regardless of floor → the bounty lever is toyless.
4. **PresentItem tunnel** — `break` after the first legal candidate → only shelf-item-#1 presentable; must Unstock others to aim the pitch. Root cause of the counter's underperformance.
5. **HaggleResponse.counter inert** — only the standing-offer value offered → identical to accept; no player-chosen counter-price.
6. **Superior+ / quality is unaimable via text surface** — `CraftAction` supports a `PerformanceGrade` and higher-grade material substitution, but the enumerator only proposes default-material/default-grade. So the auto-craft path caps at Superior-by-RNG and Act III's Superior+ bar can't be targeted here — the minigame is the only aim.
7. **Counter can soft-hang the day** — Morning stays Morning while a counter session is open; forgetting CloseCounter wastes ticks (recoverable, but a trap).

These are candidate-generation gaps (the guided surface + advisor can't reach real mechanics), NOT necessarily 3D-client truths — the Godot panels may bind some action types directly (the 3D forge minigame reaches Masterwork; the Shop has an OpenCounter button). Cross-check against `2026-07-27-3d-client-surface-recording.md` when acting.

## 6. Strategy divergence (the branch structure of the loop)

| Decision point | Merchant | Legend-chaser | Hero-patron | Systemic lesson |
|---|---|---|---|---|
| what to craft | sells + commissions | future heirlooms | advisor-named gear gap | steers gold vs legend vs depth |
| stuck poor-buyer sale | (2026) counter / (2027) **just stock, atomic pass** | self-resolve | **BuyOre gold-gift** | 3 different unlocks; atomic pass is the reliable one |
| a hero dies in your gear | next sale | **ReforgeHeirloom** → legend | keep pushing survivors | fork: shopkeeper vs legend-smith |
| surplus gold | piles up (nowhere to sink) | fund commissions | gift into heroes' wallets | spending into heroes > hoarding |

**Convergent spine (irreducible loop):** accept every commission (free) → take every free talent → craft to a named gap → stock broadly → let the atomic pass sell → ration 5 slots.

## 7. The ~6 recurring dilemmas (the game's actual decision content)

1. **Slot triage** — 5 production actions/day vs unbounded needs; a tier-3 gear-up eats a day.
2. **Which hero, which gap** — advisor names one; you close few.
3. **Craft-for-commission vs craft-for-shelf** — premium guaranteed, but you can't force the named hero to *afford* it.
4. **Gold to yourself vs into a hero** (BuyOre gift) — only helps when the hero's *wallet*, not gear, is the bottleneck.
5. **A death: move on vs reforge** — stat-line vs legend.
6. **Reforge vs restock under scarcity** — same material + same slot serve both.

## 8. Open questions for Fable

1. **The counter is a trap** — is it worth fixing (present-any-item candidate-gen; real haggle-counter values) so it beats the atomic pass, or should it be cut/reframed? It currently punishes the player who engages with it.
2. **The sink layer is unreachable + surplus has nowhere to go** — fix candidate-gen so the sinks + real repricing surface, or is the surplus itself the problem (money with no meaning)?
3. **Quality is unaimable outside the minigame** — is the floor-5/Act-III Superior+ wall *intended* to gate the ending behind the 3D active-craft minigame (skill expression), or is it an accidental dead-end for a text/auto player? This determines whether "the game can't reach its ending" is a bug or a design.
4. **Day-1 double-death is systemic** (both seeds) — intended "harsh open, prove yourself" or a bad first impression that should be softened?
5. **Class extinction (Mystic always dies, Striker hardiest)** — systemic tuning gap or intended class-fragility flavor?
6. **The two best moves are hidden** (BuyOre-as-gift, when-NOT-to-counter) — the advisor teaches the core loop well but never these; is discovering them the fun, or a legibility failure?
7. **The reforge-heirloom legend loop is the north star working** but it's a happy accident a legend-chaser stumbles into — should it be a first-class, guided arc?
8. **What decision points are MISSING?** — the loop has ~6 dilemmas but the mid-game (days 6–15) becomes a stable "accept/craft/stock" rhythm; where would a new tension most improve it?

---

## 9. Professions — the four crafts + the demand-map finding

*Added after 5 more runs: one each of Alchemy / Tanning / Engineering (seed 2026, ~14d), a Blacksmith advisor-follower (18d), and a dual Blacksmith+Alchemy run (14d). All via `decisions play --profession`.*

**Professions are pure data on one pipeline** (`ProfessionDefinition` → `CraftingHandlers`/`QualityRoller`). All four share the day, the 5-slot budget, the verb set, the talent-tree *shape*, the quality table, and the ore materials. Only three things vary: the recipe line, the hero need it serves, the crafting mechanic.

| Profession | Makes | Commissionable? | Unique mechanic | Result (100g start, ~14d) | Depth |
|---|---|---|---|---|---|
| **Blacksmith** | weapons/shields/armor (16) | yes (all 3 gear slots) | forge hammer minigame (`PerformanceGrade`) | 5.1–5.8× | DEEP |
| **Engineering** | bolt-throwers/vests/**trinkets** (8) | yes (gear) + only Trinket source | none (generic roll) | **6.2×** | THIN, rich |
| **Alchemy** | draughts/elixirs/robe/charm (8) | **only the robe** (Armor); consumables+trinket NOT commissionable | reagent-brew puzzle (**sim-scored**, but harness emits `Puzzle=null` → invisible) + live `send` at Camp | modest; two-business model | its own thing |
| **Tanning** | hide armor/shields/poultice (7) | Armor/Shield only, **no weapon** | none (generic roll) | **weakest: 100→~28, failed a Guild Assessment** | THIN |

**THE unifying finding — demand is weapon-first, at two levels:**
1. `CommissionSystem.FindGapSlot` scans **only Weapon/Shield/Armor** → consumables (alchemy) and trinkets (engineering/alchemy) can NEVER be commissioned; and ~75% of commissions ask Weapon.
2. Every advisor "hero stalled at floor N missing X" depth-gate prompt observed was **Weapon or Shield** — never even Armor. So weapons drive *both* premium gold *and* floor progression.

Consequence: **weapon-making professions (Blacksmith, Engineering) carry the world; non-weapon professions (Tanning, Alchemy) are structurally under-served** — viable as slower/support playstyles but strictly weaker income + locked out of depth-gating. Honest framing: **two professions have a *craft* (Smith's forge, Alchemist's brew), two have a *customer* (Tanner's weight-limited classes, Engineer's trinket monopoly).** The Game-Feel Plan's own "profession distinctness" section prescribes the fix (a distinct core verb + input per profession); the current build hasn't done it for Tanning/Engineering.

**Dual-profession (Blacksmith+Alchemy):** composes as *flavor* (both draw copper → a real "weapons or potions today?" daily dilemma; alchemy's `send` saves weaker heroes — 0 deaths after the heal-send loop began vs 4 before) but **not as balance**: all 18 commissions were Weapon/Shield, every depth-gate was Weapon/Shield, so blacksmith carried all money + all progression (dual ended 1.94× vs mono 5–6×). A player never spent a free alchemy talent because none visibly unblocks a depth-gate. **Verdict: dual = one dominant + one support act.** Also: **picking a 2nd profession is unreachable via the guided surface** — `ActionLegality` only emits a reaffirm-current `SetProfessionsAction`, never an "add a profession" candidate (the dual worker had to add a literal-token escape hatch). Another candidate-gen gap (§5 family).

**Alchemy's puzzle correction:** fable hoped the sim-scored alchemy puzzle could probe the Superior+/Act-III quality wall the blacksmith runs couldn't reach. It can't *through this harness* — `ActionLegality` never emits a puzzle input, so alchemy crafts fall back to the same auto-craft RNG. Act III reachability remains untested via any text surface.

## 10. Additional real bugs found (profession runs)

8. **Blindly following the advisor FAILS** (advisor-follower, 18d): the advisor ranks `AcceptCommission` (Fine+ premium) as #1 while `BuyMaterial` — the precondition to craft it — sits at #2 and is never promoted, so a trusting player accepts commissions they can't fulfill → **≥9 CommissionExpired**, net +7 gold over 18 days. The tutorial/intended path soft-fails. Fix: advisor must promote Buy/Craft to #1 when holding unfulfillable accepted commissions.
9. **`Stock` candidate-gen stops** after ~day 5 in the advisor run — finished items got no `stock` legal action, stranding 3 items as permanent dead inventory. Needs repro but looks like a real candidate-gen gap.
10. **Same-name recruits** ("Magnus" ×2 live) make advisor commission references ambiguous to resolve against `legal`.
11. **Quality→price disconnect:** a Masterwork consumable still auto-prices at 1g (quality tier doesn't raise the suggested price), compounding the SetPrice-inert gap (§5.2).
12. **Guild Assessment can be FAILED** (`GuildAssessmentMissed`, tanner run) — the slow-selling professions can miss the quota; first observed failure, worth confirming the consequence.

*Evidence: TEN worker playthrough logs (5 blacksmith incl. advisor-follower + 4 professions + 1 dual; deterministic replays, seeds 2026/2027). Harness: `sim/GameSim.Cli/DecisionPlay.cs` (+`--profession`). Companion human artifact: the "How the Game Plays" visual page. Source cross-refs: `ActionLegality.cs`, `ActionBudget.cs`, `CounterHandlers.cs`, `Professions/ProfessionRegistry.cs`, `Heroes/CommissionSystem.cs` (FindGapSlot = the demand-map root), `Professions/Alchemy/AlchemyPuzzleScorer.cs`.*
