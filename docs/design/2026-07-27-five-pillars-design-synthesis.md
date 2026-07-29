# Maker's Mark — Five-Pillars Design Synthesis

*Date: 2026-07-27. Source: five independent Fable deep-dives (advisor, hero-attribution, balance, content, professions), each grounded in the shipped code, following a 10-playthrough measurement pass (`docs/design/2026-07-27-how-you-play.md`, `docs/design/2026-07-27-gameplay-loop-analysis.md`). This doc combines them into one buildable program. It is the handoff for the next work wave.*

---

## 0. The convergence (read this first)

Five designers worked five different concerns in isolation. They independently reached the **same root cause** and the **same build order**. That agreement is the strongest signal in this document.

> **Root cause — the demand map is weapon-first at two levels.** Commissions only ask Weapon/Shield/Armor (~75% Weapon). *Every* depth-stall gate ("hero needs X to go deeper") is Weapon/Shield too. Consequence: weapon-makers (Blacksmith, Engineer) drive both the gold **and** progression; Alchemist/Tanner have supply verbs with no matching demand. **Two professions have a craft, two have only a customer.**

Four of the owner's five concerns are downstream of this one fact:

| Owner concern | How it traces to the demand map |
|---|---|
| Advisor misleads | It can only ever say "craft a weapon for the stalled hero" — one question, so the accept-before-buy bug is the only interesting failure left. Keying its gate on *recipe-matches-demand* makes it diversification-ready. |
| Heroes feel flat | Heroes can't behave distinctively when the demand system only lets them want three things. Attribution is the grammar; **diverse demand is the vocabulary**. Most "natural behavior" falls out of demand diversity *for free*. |
| Day-11 repetition | The sag is a **question shortage, not a content shortage**. A one-question game with more content is a *longer* one-question game. |
| Balance sweet spot | The 4th skill surface (allocation under scarcity) only exists once 4 crafts compete for 5 slots. Today "what do I make?" has one answer, so the advisor can trivially give it and the game is brainless. |
| Profession trees | Grind-gated unlocks would unlock professions **into a demand vacuum**. Demand-gated unlocks fill the vacuum by construction. |

**The keystone: demand diversification.** Every other pillar consumes it. It is the single highest-leverage change in the program. The advisor should be built *ready* for it; heroes, balance, content, and professions all *depend* on it.

A second, quieter convergence: **the same two subsystems appear in two pillars.** The content pillar's `NeedsEngine`/`DemandLine` and the profession pillar's `HazardDefinition` are one build — hazards create typed demand (content) *and* trigger profession unlocks (professions). Build the demand+hazard engine once; two pillars light up.

Three pillars independently proposed the **same difficulty model** (advisor verbosity: Apprentice / Journeyman / Master on one tuned world). Three pillars independently kill the **1287× memorial nag** (advisor nag-decay + content HeirloomRefit-fold + a balance invariant that caps any event at 20×/run). Convergence = confidence.

---

## Pillar 1 — Advisor v2: from ranked suggestions to derived plans

*Full spec grounded in `sim/GameSim/Advisor/ObjectiveAdvisor.cs`, `Drama/DemandBoard.cs`, `Contracts/Actions.cs`.*

**The shift.** A suggestion advisor answers "what's one legal thing now." A plan advisor answers "what *sequence* closes an identified demand — and is it actually completable." Stays pure/memoryless: plans are **re-derived every call and recognized structurally** (an accepted-unfulfilled commission *is* an open plan; materials on hand *are* step-1-done). UI shows "step 2 of 3" with zero advisor memory.

**Fulfillability projection** (before recommending any goal): recipe path (cheapest selected-profession recipe, baseline quality only — never counts modifier/minigame upside) → tier-gate (prepend free `UnlockTalent` if needed) → material buy (gated on gold now, **never on projected income**) → slot budget (phase-walked; buys are Morning-only) → sale path (target hero can afford, or splice the broke-hero opener).

**Priority stack (replaces current U8 ordering):**
1. Memorial — on decay cadence only.
2. **Open-plan next step** — accepted commissions nearest deadline first. *A demand you already promised beats any new one.* (Biggest behavioral fix — today the advisor forgets a commission the instant it's accepted.)
3. Fulfillable new demand (gated commission → quality stall → slot stall).
4. Opportunistic (broke-hero gift, stale-listing haggle, fulfillment match, stock).
5. Cheapest-productive fallback.

**Fulfillability gate on AcceptCommission** — the fix for the measured 9+ expiries. Unfulfillable → actively suggest **Decline** with the reason code in plain words (`NO_RECIPE` / `LOCKED_TIER` / `CANT_AFFORD_MATERIAL` / `NO_TIME`): *"Decline Grimma's commission — a promise that expires hurts more than a polite no."* Never silently skip.

**Teach the two hidden openers** (the answer to the measured #1 frustration, "gear made, hero won't/can't buy"):
- **Broke-hero → BuyOre gift**: reuse U11's null purse-mismatch dead-end as the trigger. Copy teaches the *pattern*: *"...buy 6 of her copper ore (14g). You bank ore you'd buy anyway, and the shield comes within her reach."*
- **Stale listing → haggle**: age from `EventLog` stock event + a price-shaped pass reason → two-step `OpenCounter`→`Present` plan. Advisor never emits the haggle *number* — that's the player's.

**Nag decay** (deterministic, memoryless): `age = state.Day - memorial.Day - 1; show = age >= 0 && age % 3 == 0`. Multiple memorials → oldest un-honored FIFO, rest folded into copy.

**The brainless line, exact:** *"If two skilled players could legitimately choose differently, the advisor describes; if every informed player would do the same thing, it prescribes."* It plans the craft-sell-fund loop; it **never** picks which hero to invest in, the forge modifier, the price, or the talent branch-fork.

**Difficulty tiers**: Full plans / Signals / Off, labeled "Guidance," orthogonal to (economic) difficulty. Ship, default new saves to Full.

**Diversification-ready**: key the gate on *recipe-matches-demand*, **not** `Slot ∈ {Weapon,Shield,Armor}`. Then diversification is a data change, not a rewrite. New "repeat-demand" plan shape for consumables ("craft 2 antidotes — they burn per raid").

**No contract micro-PR** — `Suggestion` lives in `Advisor/`; the record extension (`PlanKey`, `ImmutableList<PlanStep> Plan`) is source-compatible for existing callers.

**Tests**: golden scenario suite asserting the *entire* suggestion list incl. reason strings verbatim (all interpolation is integer/enum → stable cross-OS); memorial cadence table; property test over the 20-seed sweep (every emitted action legal; no PlanKey recurs after its premise dies; same state → byte-identical); an `AdvisorFollower` Balance policy asserting accepted commissions fulfill before deadline ≥95%.

---

## Pillar 2 — Hero Behavioral Attribution: the felt world

*Full spec grounded in `Contracts/Heroes.cs` (`MoodPermille`, `ItemMemory`), PKD7 "influence never orders."*

**Principle.** Legibility (you can *read* the hero) shipped in Phase B. Attribution (the hero's *choices* visibly trace to you) is the missing half. It must live **in-market** — the Morning shopping pass — not only the postmortem chronicle. **Design law: every relationship state changes a market-visible behavior, one hop from a named player action.**

**One new bounded record — `Bond`** (follows the `MoodPermille` precedent: immutable record, integers, old saves default to `Bond.None`):
```csharp
public sealed record Bond {
    public int LoyaltyPermille   { get; init; }  // 0..+1000
    public int GriefPermille     { get; init; }  // 0..+1000, decays 50/evening
    public int GratitudePermille { get; init; }  // 0..+600,  decays 30/evening
    public ImmutableList<BondMark> Marks { get; init; }  // 3-entry provenance ring
}
```
Channels are separate (a hero can be loyal to you *and* grieving a friend who died in your armor). They feed the **existing PA4 willingness math** (additive, clamped) — no new pricing pipeline. O(heroes), never O(history).

- **Survival loyalty** — hero who cleared a floor in your marked gear: `+120‰` (floor-first) / `+60‰` (save). Shops *first* (≥300‰), commissions *you* by name (≥500‰, *"make the shield's brother"*), premium. **Dies → loyalty converts** to a `ReforgeHeirloom` tier-waiver, not deleted (*"He never bought from anyone else"*). The item accrues a deed epithet from existing `ItemMemory` — the legend is a named object with a market consequence.
- **Death aversion** — hero dies in your gear → **witnesses only** (not campaign-wide) grieve `+350‰`, decaying `−50‰`/evening (7-day shadow). While ≥150‰ they demand **tier+1 or nothing** in that slot — a *want*-upgrade, always answerable at the forge, never a boycott. Surfaced as a shop beat: *"Tomas died in a hauberk like that. Show me better."*
- **Gift memory** — `BuyOre` overpay → `Gratitude` bucketed, rate-limited (per-day one-bucket-max, per-hero clamp, decay) so it's a relationship you maintain, not a stat you buy. Trait-flavored voice (Proud: *"I repay debts."*; Timid: *"You didn't have to."*). The *solvency* effect stays; only the relationship credit is capped.

**PKD7 stays constitutional.** Bond touches the *market only*, never expedition dice. A guard test runs identical seeds with Bond zeroed vs maxed and asserts **byte-identical floor/kill/death outcomes**.

**Felt moment (Godot, adapter-only)**: hero-card bond glyph (hammer/black-band/open-hand) → tap renders the top mark as a sentence; Morning shopping-pass *staging* (loyal first, griever pauses at the rack, gifted heads for your stock); chronicle lines that name the item and recur verbatim when the loyalty commission arrives.

**Gossip/reputation/rivalry** (approved Erenshor borrows) = amplifiers on Bond marks, ~3 integers total. Rivalry = one comparison + a line table (two heroes competing to be your favorite).

**Sequencing note**: most "alive" sensation spikes when the demand map ships — don't over-tune Bond magnitudes against today's weapon-monotone baseline, because that baseline is the thing being deleted.

---

## Pillar 3 — Balance: the curve is right, legibility is broken

*Full spec grounded in the 100-day `Category=Balance` harness + measured runs.*

**Diagnosis.** 5.13× skilled vs floor-4-stall dumb (same seed) = a **healthy skill curve** — a good staircase with one invisible step and two flat landings. **Do not retune the numbers.**

**The quality wall is a legibility problem.** Floor 5 needs Superior+; auto-craft reaches Superior only by RNG; the game never says the minigame/Masterwork is the intended lever, so players grind auto-crafts. Fix = information (free, reversible, golden-safe), NOT a rate buff (which would make the *grind* strategy more viable — tuning toward the failure mode):
- **Quality forecast on the craft screen** (core fix): deterministic band from recipe+materials+method — *"auto-craft iron → Common–Fine (Superior rare)"* vs *"forge it (Great) → Fine–Superior."* The band, not the roll, teaches. Show slot-cost alongside so the quality tax stays legible.
- Depth-gate callout at the wall (fire once, per the nag lesson) + advisor escalation copy at the grind point.

**Soften the two cliffs, keep the fall.** Destitution keeps a *visible exit* (one always-on low-pay income path; 2–3 painful recovery days; never loans/gifts). Commission expiry fixed at the source by Pillar 1's fulfillability gate + a one-time deadline-minus-1 ping. Stakes that MUST remain: money can wall for days; promises break with lasting cost; advisor warns but never auto-acts.

**Difficulty = advisor verbosity, not modes.** Apprentice (full plans) / Journeyman (warnings+labels, default) / Master (cliff warnings only). Diegetic, zero sim change, one balance sim. Plus progression-scaled *stakes* (Act III consequences heavier) via copy, not hidden multipliers.

**Keep 5 slots.** Five is correct *because* it's one short of comfortable for a Tier-3 item (the buy-today-craft-tomorrow rhythm). 4 = slog; 6 = tension evaporates. Add a 6th slot **only** as a late forge-upgrade — after demand diversifies (so it relieves the *new* 4-craft pressure, and gives surplus gold a sink). One upgrade, not a ladder.

**Demand diversity is the 4th skill surface**: specialization (deep on one craft, win by hero-portfolio) vs generalization (serve all hazards, win by 5-slot allocation across 4 demand streams). Make hazard-typed gates *hard* gates (no Superior antidote → poison floor stalls, full stop) or everyone stays a weapon specialist and nothing rebalances.

**10 balance-sim invariants** (three canonical policies — Skilled / Naive / Greedy — over ≥20 seeds). Non-negotiables:
- **#2** NaivePolicy *never* reaches floor 5 by day 100 (protects the minigame's reason to exist — if it passes, quality RNG regressed).
- **#7** Zero advisor-surfaced commissions expire under a policy that accepts everything surfaced (encodes the 9-expiry finding).
- **#10** No event class fires >20×/100-day run (the 1287× nag becomes a permanent build failure).
- Plus: skilled reaches floor 5 by day 30 (≥90%); naive reaches floor 3 by day 20 but not before day-15 destitution; skilled day-15 gold ∈ [3.5×, 7×]; once diversified, no craft >55% of gold and each ≥10%.

---

## Pillar 4 — Content Depth: demand is the content engine

*Full spec grounded in `Drama/DemandBoard.cs`, `Heroes/CommissionSystem.cs` (`MaxOpenCommissions=3`).*

**Thesis.** The day-11 sag is a **question shortage**. The player answers "the stalled hero needs a Weapon" ~30× by day 11. Fix = a demand engine that asks many *typed* questions, an escalation engine (hazards) that changes which questions get asked over time, and an ending that makes the sequence a finite arc. Near-zero assets.

**`NeedsEngine`** (new Morning phase-system, before `CommissionSystem`; pure projection like `MusterPlan`): reads each hero's class/traits/target-floor/inventory/recent-events → emits typed `DemandLine`s. `Commission` generalizes to:
```csharp
public sealed record DemandLine(
    DemandShape Shape, CraftDiscipline Craft, ItemSlot? Slot, QualityGrade MinQuality,
    int Quantity, int PremiumGold, int DeadlineDay, HeroId? Hero, HazardTag? Counter);
```
**Eight demand shapes**, each a distinct question: GearGap (today's), ConsumableRestock (pricing test, recurring), HazardCounter (prep-ahead), PartyOutfit (batch/queue), RushOrder (2× premium, half deadline), BiddingWar (two heroes, one masterwork → feeds rivalry), VanityPiece (craft for legend not depth), HeirloomRefit (routes the memorial system into an *offer* instead of the 1287× nag). **Traits become demand-mix parameters** (Cautious Ranger over-orders antidotes; Vain Duelist posts vanity pieces) — cheaper than behavior trees, directly visible.

Folds in the two old audit wounds: broke-state gets an income floor (always-on low-value restock); memorial nag → HeirloomRefit that fires once and expires.

**Mine hazards** (one system, three jobs — typed demand + revive 3 dead professions + pace mid-game). 5 axes as data rows: Venom (Alchemist/Tanner), Arcane (Engineer), Structural (Engineer/Alchemist), Frost-Heat (Tanner/Alchemist), Martial (Blacksmith, existing). Each floor carries 1–2 tags; an uncountered tag applies an integer expedition penalty; a matching item converts it to a **chronicle-visible save**: *"Kara's filter draught held through the gas pocket — maker's mark: you."* North star made mechanical.

**Escalation curve** (Act structure): Floors 1–2 Martial only (clean core loop); Floor 3 (~day 8–10) first non-martial tag, telegraphed 2 days early, **Alchemist unlock lands here**; Floor 4 Arcane/Structural, Engineer demand; Floor 5 stacked dual-tag + masterwork gates; **Floor 6 "the Heart" (Act III)** = all tags, apex threat, full-party multi-craft masterwork outfit.

**Gating↔repetition tension resolved by a law:** *gate = hazard debut = profession debut, as one beat.* Never spawn a hazard whose counter-craft is locked; never gate a craft without spawning its demand. A dead verb must never be visible-but-unrouted again.

**The ending** (recommendation: hard authored beat + open scored epilogue, not endless, not NG+ machinery): heroes clear the Heart wearing *your* masterwork covering all its hazards — you don't fight, you *watch whether your work holds* (the whole game in one scene). Epilogue = **"The Ledger of Legends"** (every hero you outfitted, each piece's fate) + a **Legend Count** cross-run score. Failing forward is on-theme — the ending stays available; legends include the fallen. Bounds content to ~25 good days = the strongest anti-treadmill move.

**Rival crafter "Master Voss"** (1 portrait + 2 integers): expired/declined `DemandLine`s route to *him* → every miss becomes story (*"Voss shod Kara's party"*), not a silent mood hit. Reputation = a demand-mix dial (high weapon-rep attracts VanityPieces/BiddingWars; low alchemy-rep means restock lines start Voss-side, win them back).

**The Generator Test** (permanent CI/review guard): *"This addition produces N different player-facing situations from ≤1 page of data. If N=1, or N>1 only by palette-swap, reject or redesign as a system."* Corollaries: every new noun must be *routable* (some demand shape can ask for it); every new system must write to the *chronicle*; every generator ships with a Balance-category distribution test. Total hand-authored assets for the whole content spec ≈ 1 rival portrait + ~5 hazard rows + ~8 counter-item rows + ~30 chronicle templates. Everything else is combinatorics.

---

## Pillar 5 — Demand-Gated Profession Progression

*Full spec grounded in the action vocabulary (`SetProfessionsAction`, `UnlockTalentAction`, `MasterworkAttemptAction`).*

**Verdict on the owner's tree idea: instinct right, axis wrong. Ship the re-axised version.** Start-narrow-widen-over-time is exactly right for the sag. The *unlock currency* is wrong: a grind-gated tree unlocks professions **into a demand vacuum**, and the owner's specific trees put the craft-less professions at the *deep* end where the player expects the biggest payoff and gets the smallest.

**Kept** from the owner's idea: the pacing shape; the branching fantasy (floor-3 unlock is a genuine Alchemist-vs-Tanner fork = the physical-vs-mystic split in miniature); the tree *structure*, relocated **inside** each profession as the talent layer (§talent). **Cut** (honestly, a real loss of breadth): Enchanter/Healer/Spellcrafter as *professions*. Mitigation: `ProfessionRegistry` stays data-driven and the unlock mechanism is generic — a later floor-7 hazard (curses→Healer, magic-null→Spellcrafter) slots the seven-profession fantasy in with zero rework. **Park it as Phase 2, contingent on the 4-profession version proving the demand-pull loop.**

**Demand-gated unlock mechanism** — *a profession unlocks the moment the world first asks for it, and only then.* Trigger (deterministic, end-of-day): **≥2 distinct heroes** have hit the new hazard's stall gate, **AND** a readiness milestone (5 Fine+ items lifetime OR any hero cleared floor 3 in your gear — an OR floor, not a wall). Fires `ProfessionUnlockOffered(Branch:[Alchemist,Tanner], TriggeringHazard:Venom)`.

**The unlock moment is not a menu — a hero asks.** The first stalled hero walks in: *"Venom-beasts on floor 3. Steel doesn't help — I need an antidote, or hide the fangs can't pierce. Nobody in town makes either."* Then a branch screen: **Study Alchemy** vs **Study Tanning**. New `UnlockProfessionAction(string Profession)`; legality restricted to professions in a live offer. The un-chosen branch is **not lost** — it re-offers +4 days at half urgency (a *sequencing* choice, not a permanent cut).

**Profession/hazard mapping**: Floor 3 Venom → Alchemist (antidote, consumable/repeat) **or** Tanner (hide, durable) — the starved pair gets the *first* new hazard, teaching the two revenue shapes. Floor 5 armored burrowers (immune to edged/blunt weapons) → **Engineer, exclusively** — the floor-5 stall gate is unsatisfiable by any Blacksmith item, so `needs PiercingBolt` is Engineer-typed only. Engineer's disease isn't starvation, it's *redundancy* with Blacksmith; the cure is an exclusive deep-mine niche. ~25% of floor-5+ weapon commissions re-type to Engineer, permanently denting the 75% skew.

**Fixes, not hides, the demand map — by construction:** the unlock trigger *is* the demand existing (≥2 real stall events). On day one of any profession's life there are already named heroes stalled on commissions only it can fill. There is no reachable state where a profession is unlocked and customer-less — **an invariant, tested.**

**Single-start (Blacksmith-mandatory).** Only profession with both a real craft-feel (forge minigame) and day-1 demand; the floor-3 beat ("your steel stops working") only lands if you've been selling steel. **Risk flagged: reads as railroading to brew-first players.** Mitigations: fast lane to identity (brewing by ~day 9–11); **replay alt-start** (`AltStartUnlocked` flag → Alchemist-start variant, data-authored hazard tables, ship same phase or first fast-follow).

**Talent layer** — the owner's trees live *inside* professions via existing `UnlockTalentAction`. Blacksmith gets a 3-branch tree: Anvil (quality), Furnace (tier, unlocks `MasterworkAttempt`), **Runeforge/"Enchanter"** (affixes on weapons that *already have buyers* — a soft cross-craft bridge to the venom gate, gates `CommissionLegendaryWork`). Enchanter earns its keep as talents modifying items with customers, not a fifth shop tab with none. Alchemist mirrors it (Spellsealing, Healer-as-field-tonics branch).

**Pacing math** (depth-triggered, not day-triggered — stays honest across seeds/skill): Blacksmith d1 → venom activates when heroes reach floor 3 (~d7–8) → 2nd-stall unlock offer + branch choice **lands in the measured day-9–11 sag** → mirror offer ~d13–15 → Engineer ~d20–24. **Gating-to-the-sag dissolves the tension**: at day 11, players holding 4 flat professions are *sagging* (4 crafts ÷ weapon-only demand = repetition); moving the 2nd profession *to* day 11 converts dead inventory into a timed novelty injection at the exact low point. Residual risk: a player who stalls pre-floor-3 gets the beat late → hard fallback: hazard force-activates day 14.

**Migration** (serial re-baseline, per Phase-C precedent): reuse all 4 `ProfessionDefinition`s + handlers + minigame + talent plumbing. New: `HazardDefinition`+spawn, `HazardStallEvent`, `ProfessionUnlockOffered`, `UnlockProfessionAction`+legality, per-hazard commission-typing weights. `SetProfessionsAction` — keep the action, restrict legality to day-0 (+ alt-start); old goldens replay under a `LegacyFlatProfessions` ruleset flag. Dual-classing as a day-1 pick disappears (it's now the natural post-unlock state); revisit the 1.94× dual penalty *after* — demand re-typing may fix most of it for free. Sequence: (1) hazards+typed-demand behind a flag, flat model intact — proves the demand math alone; (2) unlock state machine + re-baseline; (3) client moments; (4) alt-start.

---

## Unified build sequence

The five pillars agree on this order. Each wave is independently shippable and green-gated.

**Wave 1 — Advisor v2 (Pillar 1).** One file (`ObjectiveAdvisor.cs`), has tests, *stops actively harming new players now*. Ship the fulfillability gate + decline copy + teach-the-openers + nag-decay + the "recipe-matches-demand" keying (so it's diversification-ready) + the Guidance tiers. Independent of everything else. **Do first.**

**Wave 2 — ⭐ Demand + Hazard engine (Pillars 4 + 5, shared build). The keystone.** `NeedsEngine` + `DemandLine` + the 8 demand shapes + `HazardDefinition` + the 5 hazard axes, **behind a flag with the flat profession model intact** — this proves the demand math alone before touching unlocks. This is the single highest-leverage change; every remaining pillar consumes it. Lock the current curve with the Wave-3 invariants *before* flipping the flag.

**Wave 3 — Balance legibility + invariants (Pillar 3).** Quality forecast on the craft screen; the 10 balance-sim invariants (pin the curve *before* diversification tuning can drift it); destitution floor. Invariants #2/#7/#10 are the non-negotiables.

**Wave 4 — Profession restructure (Pillar 5).** Flip demand-gated unlocks on: unlock state machine + `UnlockProfessionAction` + legality + the hero-asks moment + branch screen + serial re-baseline. Rides Wave 2's hazards. Alt-start as the fast-follow.

**Wave 5 — Hero attribution (Pillar 2) + the ending (Pillar 4) + rival (Pillar 4).** The `Bond` record + three echoes (rides Wave 2's diverse demand signals — build now, but expect the "alive" spike here) + the Ledger-of-Legends ending + Master Voss. The payoff layer.

**The one-line program:** fix the advisor so it stops lying (Wave 1), then diversify demand so the game asks more than one question (Wave 2 — the keystone), then let professions unlock *onto* that demand (Wave 4), heroes *react* to it (Wave 5), and the whole thing stays legible and bounded (Waves 3+5).

---

## Cross-pillar risks to watch

1. **Sequencing risk (biggest):** demand diversity *raises* effective difficulty (same 5 slots, 4× demand shapes). Ship Wave 2 *with* Wave 1's fulfillability gate and Wave 3's warnings pre-landed, or measured expiry/destitution rates spike.
2. **Blacksmith-mandatory start reads as railroading** to brew-first players → alt-start fast-follow is not optional.
3. **Don't over-tune Bond magnitudes** (Wave 5) against the pre-diversification weapon-monotone — that baseline is being deleted.
4. **The day-14 hazard force-activation fallback** needs playtest tuning so slow players get the beat without it feeling arbitrary.
5. **Serial re-baseline discipline** (Wave 4): land sim rules + new goldens + `BaselinePlayer` migration in one PR; legacy goldens pinned under the ruleset flag.

---

*Method: five Fable model deep-dives (2026-07-27), each independently grounded in the shipped code, synthesized by Opus. Predecessor docs: `docs/design/2026-07-27-how-you-play.md` (human field guide), `docs/design/2026-07-27-gameplay-loop-analysis.md` (measurement + verb matrix). Next step: turn Wave 1 + Wave 2 into a `ce-plan` implementation plan.*
