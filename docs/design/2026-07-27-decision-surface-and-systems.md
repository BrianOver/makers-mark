# Maker's Mark — Decision Surface & Systems Documentation (2026-07-27)

*What the game actually offers the player, measured. Generated from `GameSim.Cli decisions --seeds 15 --days 100` (7500 decision points across 15 full playthroughs under the default `BaselinePlayer` policy) plus a systems map of the shipped sim. This is the documentation artifact for the Fable analysis pass. Raw data: `runs/decisions/decision-surface-summary.md` + per-seed `decisions-seed*.jsonl`.*

Measured on `main` @ dd8fd64 — the shipped game (2 live venues: Mine + Gloomwood; 3 recruit classes). NOT the draft 4-venue/6-class flip (PR #242).

---

## How to read this

At every phase of every day, a player faces a **decision point** with three parts, all captured here:
- **Legal options** — every verb `ActionLegality.LegalActions` says the player *could* do.
- **Advisor** — the ranked suggestions + reasons `ObjectiveAdvisor.Suggest` puts in front of them.
- **Choice** — what the default agent actually did (a policy-independent sample; the *option surface* itself is what matters).

The day is 5 phases: **Morning → Expedition → Camp → ExpeditionDeep → Evening.** The player is the blacksmith; heroes raid autonomously. The player never fights.

---

## The player's 25 verbs, grouped by system

| System | Verbs |
|---|---|
| **Crafting** | Craft, MasterworkAttempt, CommissionLegendaryWork, ReforgeHeirloom, UnlockTalent, SetProfessions |
| **Shop / pricing** | Stock, Unstock, SetPrice |
| **Counter service (haggle)** | OpenCounter, PresentItem, SuggestItem, CloseCounter, HaggleResponse |
| **Commissions** | AcceptCommission, DeclineCommission |
| **Economy / forge sinks** | BuyMaterial, BuyOre, BuyForgeSupply, UpgradeForge |
| **Expedition support** | SendSupply, RecallParty |
| **Bounties** | PostBounty |
| **Legends / memory** | HonorMemorial |

---

## FINDING 1 — ~40% of the game's verbs never once become legal

Across 7500 decision points, these defined player verbs were **NEVER legal**, not once:

| verb | system | why it never surfaces |
|---|---|---|
| `OpenCounter`→`PresentItem`/`SuggestItem`/`CloseCounter`/`HaggleResponse` | Counter service | `OpenCounter` *is* legal in Morning but the default policy never opens; the entire stepped-haggle minigame (present → suggest → negotiate) is gated behind an action nothing drives, so 4 of its 5 verbs are unreachable in normal play. |
| `UpgradeForge`, `BuyForgeSupply`, `MasterworkAttempt`, `CommissionLegendaryWork` | Forge / Phase-D sinks | All gold- and/or Forge-Tier-II-gated. The player is broke all 15 runs (Finding 4), so the tier/coal/gold thresholds never clear → the entire endgame-sink layer is dead under default play. |
| `SendSupply` | Expedition support | Requires a *held* (crafted-but-unshelved) consumable at the Camp tick; the default policy never stocks the pack that way, so the camp "send a salve to your dying hero" verb — a core drama beat — never lights up. |

**That is the counter/haggle system, the whole Phase-D sink economy, and the camp-supply drama beat — three headline systems — invisible to a default playthrough.** Whether a *human* reaches them is exactly what the owed Gate-B playtest must answer, but the machine never does.

## FINDING 2 — the played loop is 4 verbs wide

Of 25 verbs, the default policy ever *chose* only **Craft, Stock, BuyOre, UnlockTalent**. Everything else is either offered-and-ignored or never offered. The lived game is: buy ore in the Evening → craft it → shelve it → occasionally spend a talent point. Commissions, bounties, haggling, memorials, supply runs, reforging, forge upgrades — all sit outside the loop the game actually runs on rails.

## FINDING 3 — the advisor is a memorial nag loop

The single most-shown advisor line, by 3×, was **"Honor Sable's memorial — their Traveler's Sword still waits at the stone," shown 1287 times.** Memorial nags + verbatim-repeated commission lines ("Accept Torvald's commission — Weapon at Superior+ … due day 30/36/42/48…") dominate the advisor. This is the audit's "day-11 staleness / repetition" finding, now quantified: the thing the game says most often is a guilt-trip about a dead hero the player has no cheap way to action, repeated ~1300 times. `HonorMemorial` is legal 13,672 times and advised heavily — but **never chosen** (Finding 6), so the nag never clears.

## FINDING 4 — the player is broke in every single run

Final gold at day 100 across all 15 seeds: **0, 2, 2, 1, 1, 2, 2, 2, 0, 2, 2, 0, 2, 2, 1.** Total poverty, every seed. The default policy barely sells from its own shelf (prior audit: ~7 of 51 sales are the player's), so gold never accumulates — which is the root cause of Finding 1's dead sink layer. The economy's *sinks* were built (Phase D) but the economy's *source* (actually selling your work) doesn't flow under default play. Note this is the OPPOSITE of the batch-telemetry "gold-mint-spike" anomaly — that measured a non-selling policy hoarding; this measures a policy that never builds a surplus at all. Both point at the same hole: **the player's shop→gold pipeline is weak.**

## FINDING 5 — the campaign has no ending

All 15 runs ended **stuck in Act II.** Zero reached Act III; no `EndingDay` ever set. The 3-act arc + climax + chronicle + ending (Phase D, U-D3) **never fires within 100 days** under default play. The narrative spine that's supposed to give a run a shape — setup → climax → conclusion — never resolves. Either the act-advance thresholds are tuned past what a 100-day run reaches, or they depend on player actions the default loop never takes (likely both).

## FINDING 6 — the most-available verb is a ghost

`ReforgeHeirloom` is the **single most-legal verb in the game** — 17,628 legal offerings across the sweep (it's all-phase legal whenever any fallen hero's gear exists). Yet it is **never advised and never chosen.** A verb the game surfaces constantly, guides toward never, and nothing uses. Either it's genuinely valuable and the advisor should point at it (heirloom-passed is one of the 8 named legend shapes), or it's noise cluttering every phase's menu.

---

## Systems map (what's shipped, from the sim modules)

- **Crafting** (`sim/GameSim/Crafting/`) — recipes × material grade × quality roll; craft modifiers slice-1 (`CraftModifiers.cs`, 4 modifiers, 3 families); talent trees; `ArtifactSigning` (Signed Works — human-masterwork-only, so never fires in auto play). Reforge/heirloom, masterwork, legendary-commission sinks.
- **Heroes** (`sim/GameSim/Heroes/`) — traits, needs-lite, boycott (Phase B B0–B4); roster/recruit; muster + `RaidForecast`. 3 recruit classes live (6 registered).
- **Expedition** (`sim/GameSim/Expedition/`, `Venues/`) — 2 live venues (Mine, Gloomwood); floor gates; `VenueRouter` power/queue routing; camp checkpoint (send/recall).
- **Economy** (`sim/GameSim/Economy/`, `Materials/`) — ore pricing, tariffs, rent + confidence, guild dues; Phase-D gold sinks (forge upgrade, supply, masterwork, legendary).
- **Counter** (`sim/GameSim/Counter/`) — stepped service + `HaggleResolver` (open/present/suggest/close + accept/hold/counter).
- **Drama** (`sim/GameSim/Drama/`) — `DirectorSystem` (tension, but "recorded drama only — no sim rule reads it back"), `GossipGenerator`, `LegendQuery` (a tally, not a telling — the Legend Engine / Phase A is unbuilt), 3-act `ArcState`, chronicle, `GoldLedger`, `DemandBoard`.
- **Bounties** (`sim/GameSim/Bounties/`) — `D_q` acceptance scoring shipped but tuned so tight ~0 accept.
- **Advisor** (`sim/GameSim/Advisor/`) — `ObjectiveAdvisor.Suggest` (the guidance surface measured above).

---

## What this documents for the Fable pass

The sim is deep — 25 verbs, 8 systems — but a default 100-day playthrough exercises **4 verbs, reaches 0 endings, ends broke, and is guided mostly by a 1300× memorial nag.** The gap between the *offered* game and the *lived* game is the whole story. The open question a human playtest must close: how much of the dark surface (counter/haggle, sinks, camp-supply, the arc) a *real* player reaches — and whether the reason the default agent doesn't is "it's an agent" or "the funnels don't pull anyone there."
