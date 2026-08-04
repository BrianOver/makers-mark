---
type: design
title: The hero-facing day — something to do in every phase, all of it about them
date: 2026-08-04
origin: >
  owner brief 2026-08-04 (third statement of "the base loop is not complete"): "One of the primary
  features that's unique about the game is seeing your actions impacting the heroes results.
  However, currently because there's nothing to do during the day, you have no reason to watch the
  heroes and just skip the phase. We need to set it up so the player has plenty to do each phase.
  All centered around interacting with the heroes etc"
revises: docs/plans/2026-08-03-001-feat-loop-structure-plan.md (branch feat/loop-structure-plan);
  docs/design/2026-08-04-all-building-minigames.md (branch docs/all-building-minigames)
in-flight: loop plan U1 is being built on feat/two-bell-day — ruling in §5
---

# The hero-facing day — something to do in every phase, all of it about them

## 0. Verdict on the reframing — in my own voice

**The owner's diagnosis holds up against the code, and it corrects mine.** The loop-structure
plan and I read the same evidence — three named phases with zero decisions and zero visible
events (`sim/GameSim/Advisor/ActionLegality.cs:50-77`; the instantaneous stage ticks in
`sim/GameSim/Kernel/GameKernel.cs:188-198`) — and I concluded the player should stop being asked
to crank them: automate the middle, leave two bells. The owner looked at the same dead middle and
said the opposite: the middle is where the game's one unique thing lives (*my craft decides their
fate, and I watch it happen*), so emptying it optimizes away the feature. He is right, and here
is the honest shape of it:

- **What I withdraw:** the claim that the two-bell day *completes* the loop. It completes the
  presentation of the loop — it removes the lie of buttons that decide nothing — but a raid that
  plays itself past a player with empty hands is still a phase the player skips, now with less
  guilt. "Fewer bells" was the floor and I sold it as the destination.
- **What I do not withdraw:** the evidence. There is genuinely nothing to *decide* mid-march —
  stage 1 resolves in one tick at the phase boundary, stage 2 in another, and the sim keeps no
  evolving mid-delve state a player could react to (`ExpeditionSystem.cs:87-108`,
  `ExpeditionDeepSystem.cs`; `InFlightExpedition` is a frozen record,
  `Contracts/Expedition.cs:84-103`). Any design that gives the player mid-fight verbs is fighting
  the kernel and the premise both. The heroes are autonomous. That constraint is the game.
- **The synthesis the code actually supports:** the player's mid-day verbs are not *commands to
  heroes* — they are *the smith's own hands, working while the world is in motion*. The kernel
  already allows exactly this: the forge never closes (`CraftingHandlers.CanHandle` — "all phases
  legal"), craft resolves the instant you swing (`ActionTiming.cs`), and the Camp window exists
  precisely to consume something you hold (`CampHandlers.cs`). The loop's missing half is not
  more automation and not more spectacle — it is surfacing the already-legal chain **see the
  question → forge the answer → hand it over → watch it drink**. Almost none of that requires a
  sim change. All of it requires the day to pose the question where the player is standing.

**And one criticism of the brief, owed honestly:** "plenty to do each phase" is the right demand
aimed at the wrong metric if it becomes verb-count. The failure mode this document polices
hardest is the busy-day trap — verbs that occupy hands without touching a hero's outcome (the
07-25 audit's "bounty theater" class). Every verb below passes one test: *point to the line in
the Night ledger that is different because you did it.* Verbs that fail the test are named as
failing it (§3.2 Q-3, §7 rejects) rather than shipped as filler.

---

## 1. The rulebook this design stands on (read from the code, not remembered)

**The phase machine.** Morning → Expedition → Camp → ExpeditionDeep → Evening
(`GameKernel.Advance`, `GameKernel.cs:188-198`). A phase's systems run at the tick that *ends*
it. Therefore the experienced spans are: **Quest span** = post-Morning-tick, nothing rolled yet;
**Vigil span** = post-Expedition-tick, stage 1 resolved, parked parties in `state.InFlight`;
**Deep Vigil span** = post-Camp-tick, stage 2 still *undrawn* (the parked record carries no RNG —
`Contracts/Expedition.cs:79-82`); **Night span** = post-Deep-tick, full results staged in
`PendingExpeditions`, revealed at the Evening tick (`Drama/ExpeditionRevealSystem.cs`).

The brief names seven phases (Morning, Expedition, Camp, ExpeditionDeep, Evening, Vigil, Night);
the sim has five — **Vigil** and **Night** are the player-facing names of Camp and Evening
(`godot/scripts/ui/PhaseVocab.cs:27-35`). This document covers the five sim phases under both
names.

**The 24 actions, sorted by who they face.** Thirteen of the 24 `PlayerAction` types are already
hero-facing: Present/Suggest/Haggle + the counter brackets (a hero at your counter),
Accept/DeclineCommission (a hero asking by name), BuyOre (a survivor selling you the floor they
cleared), SendSupply/RecallParty (a hero camped in the dark), HonorMemorial/ReforgeHeirloom (a
hero you lost), PostBounty (influence over where they go). The problem was never the verb list.
It is that **ten of those thirteen are locked to Morning or Evening, and the two mid-raid ones
are conditional on a park** (`ActionLegality.cs:56-71`; only ReforgeHeirloom roams free) — so the
middle of the day, where the watching happens, is where the hero-facing verbs aren't.

**What is legal mid-raid today.** During Expedition/Camp/ExpeditionDeep the phase-agnostic forge
family stays legal: Craft, Stock/SetPrice/Unstock, ReforgeHeirloom, MasterworkAttempt,
CommissionLegendaryWork, UnlockTalent (`ActionLegality.cs` — no phase gate on any of them), all
resolving immediately via `ApplyNow` (`ActionTiming.cs:77-97`). Camp adds SendSupply/RecallParty.
This is the entire mid-day toolbox, and it is enough for wave 1.

**The camp window is the day's one real lever, and the numbers say so.** The tuning comment on
`ExpeditionSystem.CampCheckpointDepth` (`ExpeditionSystem.cs:20-26`): deaths by floor over the
telemetry corpus = 59/182/191/25 — **87.1% of deaths happen in stage 2, after the camp window.**
The vigil is not a pause; it is the only moment the player's choice moves tonight's survival.
The supply fee (9g at the floor-1 camp) is deliberately priced above the 8g salve sale
(`CampHandlers.cs:24-29`) — the rationing tension is designed, and currently invisible.

**The proof mechanism already exists.** A delivered consumable front-inserts into the working
pack and the resolver quaffs front-first (`CampHandlers.ApplySend`; `Contracts/Heroes.cs:53-58`)
— *your delivery drinks before anything the hero bought*. The attribution engine then proves
`Provisioned` / `PotionLifesave` beats for that exact `ItemId` (`Contracts/Enums.cs:56-64`,
`AttributionEngine`), and the reveal stamps them onto the item's history and the hero's memory
(`ExpeditionRevealSystem.cs:114-150`). The causal chain the owner wants visible is *recorded end
to end*; no link is missing as data (the loop plan's §3 finding stands). The work is staging it.

**Hero individuality is derivable everywhere, free.** Traits (`TraitRegistry.TraitsFor` — pure
function of id+name, e.g. Prepared/Reckless literally describe heal-stocking), needs streaks and
boycotts (`NeedsSystem`), relationship edges (`RelationshipSystem`), gear gaps
(`RaidForecast.MissingItemSlots`), the muster prediction that byte-matches the real tick
(`MusterPlan.Compute`). Every surface below that "speaks hero" reads these — never a second rule
set (the `CustomerVoice`/ForgeMinigame read-only precedent).

---

## 2. The design rule

> **Every phase poses one hero-question the player can answer with their own hands, and every
> answer leaves a fingerprint the Night ledger can point back to.**

Three corollaries, enforced per verb below:

1. **Hands, never orders (PKD7).** No verb steers a hero mid-delve, re-rolls an outcome, or
   rewards reflexes with survival. The player gives *things* (gear, salves, gold, rites) and the
   heroes decide what those things are worth. Mood is influence-only and never touches
   resolution — so no verb below is paid in mood alone (that would be theater).
2. **Skipping stays legal and fast, but costs a named thing.** Hurry exists in every span; no
   decision ever carries a timer (the U15 living-clock rejection stands). The cost of hurrying is
   never "you missed a cutscene" — it is "you met the vigil question with nothing in hand" or
   "you learned at Night what you could have read at Dawn."
3. **Zero `Contracts/` edits in wave 1.** Every wave-1 verb commits an existing action. The two
   priced wave-2 candidates that would need new sim rules are in §7, justified or rejected there.

---

## 3. The day, phase by phase

Format per phase: the question → the verbs (what it feels like, which sim action, how your craft
shows up in a hero's outcome, why you'd rather do it than skip) → files → balance → the test.

### 3.1 Morning (Dawn / Prepare) — *"Who marches today, and what of mine will they carry?"*

Morning already holds the verbs; what it lacks is **aim**. Today the player crafts into a void
and the shopping pass silently decides. The fix is to make named heroes ask first, so every craft
is an answer to a person.

| # | Verb (player words) | Sim action | Craft→hero visibility | Why not skip |
|---|---|---|---|---|
| M-1 | **"Hear the want"** — the customer at your counter speaks first: "Looking for a shield — about 45g on me." You pull the piece, present it, haggle. Feels like serving a person, not guessing at a mannequin. | `PresentItemAction` / `SuggestItemAction` / `HaggleResponseAction` (existing; the want line is a read-only derivation from the hero's own gear gaps + purse via `ShoppingAi.EvaluateItem` / `RaidForecast.MissingItemSlots` — loop plan KTD-B, unchanged) | The thing you sell is the thing you watch on their body at the send-off two spans later; the ledger's sale-and-deed grouping closes the arc | Without it, Present is a guess with an instant walk-away penalty — the counter is a trap. With it, the counter is the aimed channel and the premium conversation |
| M-2 | **"Take the commission"** — a named hero asks for a slot, a quality floor, a deadline, a premium. You shake on it or you don't. | `AcceptCommissionAction` / `DeclineCommissionAction` (existing) | The commissioned piece is asked for *because of where they're going* (premium scales with target floor — `CommissionSystem.PremiumPerFloor`); deliver and you watch it march | Premium gold; an accepted-then-missed deadline stings mood (`ExpireMoodPenalty`) — a promise with teeth |
| M-3 | **"Read the muster, arm the gap"** — the forecast board names today's parties, target floors, the monsters between, and who marches with an empty slot. You forge the named gap and shelve it before the bell. | `CraftAction` + `StockAction` (existing, immediate); board is `RaidForecast.ForTomorrow` (existing pure projection) | The gap has a hero's name on it; the DemandPanel's depth-stall line even names the *blocking slot* — "Aldric cannot pass floor 3: no shield." Fill it, ring the bell, watch the shopping pass take it | The gap you ignore is the death you watch at Vigil. And `NeedsSystem` boycotts are real: ignore a hero's wants ~6 days and they take their gold to the rival — telegraphed 2 days ahead |
| M-4 | **"Steer the day"** — post a bounty toward the floor you want cleared, against the board's visible floor minimums. | `PostBountyAction` (existing, Morning-legal) | Acceptance is the hero's choice (influence, not orders); the muster board shows the override the moment a hero bites (`MusterSystem.StampTargetFloorDecision`) | Ore from that floor is your material supply; the bounty is how a smith votes |

**Files:** `godot/scripts/ui/CustomerVoice.cs` (new — this IS loop-plan U2 / minigames M2; build
once), `godot/scripts/panels/CounterPanel.cs`, `RaidForecastBoard.cs`, `CommissionBoard.cs`,
`DemandPanel.cs`, `BountyPanel.cs`. **Balance:** zero sim diff, no re-baseline. **Tests:** loop
plan U2's list stands (want line names a real gap and the hero's own gold; every
`ShoppingVerdictKind` renders a reply); forecast-board gap lines enumerate from
`MissingItemSlots`, never hand-listed.

### 3.2 Expedition (Quest) — *"What will I have in hand when they need me?"*

The honest sim fact first: during this span **nothing has been rolled**. Stage 1 resolves
entirely at the tick that ends the phase. There is no mid-march decision to offer, and under the
game's premise there never should be. The span's real content is two things: the send-off as the
*staging* of your consequences, and the forge as *preparation under uncertainty* for the one
question you know is coming.

| # | Verb | Sim action | Craft→hero visibility | Why not skip |
|---|---|---|---|---|
| Q-1 | **"See them off"** — the departure show (exists: send-off choreography, conductor's SendOff beat). Each hero files past the shop; their slate names the pieces of yours they carry — *your mark, walking into the dark*. | none (show + read-only slate; `Item.PlayerCrafted` + maker's mark are data the state already carries) | This is the chain's staging beat: sale → bearer, on screen, before consequence. Without it the Night beat ("Emberbite turned the killing blow") has no antecedent the player ever saw | It is where you learn *which* of today's marchers is yours to root for — the watch has stakes now |
| Q-2 | **"Bank the vigil answer"** — while they march, you forge. Specifically: the salve (or the commission piece) you'll want in hand when the camp question comes. The forge is lit, the town is quiet, the window is real. | `CraftAction` (existing — all-phases-legal, resolves Now, **costs a real day slot**: `ActionSlotsRemaining` is a per-day budget, so this competes with Morning's crafts — a genuine tradeoff, not free filler) | A vigil stop reached empty-handed offers only recall-or-pray. Reached with a fresh salve, it offers the game's best beat (§3.3 V-2). The Night beat that names your delivered item is this verb's receipt | 87.1% of deaths happen below the camp (§1). The vigil is the lever; this is its ammunition |
| Q-3 | **"Choose your watch"** — pick one marcher to follow; the show frames them, and beats involving your items on *that* hero caption first. | none (adapter-side lens only) | It aims the camera at your own causal chain | **Flagged honestly: this is the proposal closest to busywork.** It changes what you *see*, never what happens, and it must stay that way — the moment "watched" heroes perform differently it becomes an orders channel. Ship it only as attribution legibility; cut it without grief if the followed-hero captioning can ride Q-1's slate instead |

**Deliberately absent:** any blessing/cheer/token verb at the gate. `MoodPermille` never touches
resolution (PKD7), so a send-off gesture paid in mood is bounty theater reborn. The real
provisioning verb — handing a hero a consumable at the gate — needs a new sim rule and is priced
in §7, not smuggled here.

**Files:** conductor (feat/two-bell-day, in flight), `godot/scripts/town2d/Town2D.cs` departure
choreography (exists), slate captioning in the send-off surface; `ForgePanel.cs` unchanged
(already reachable — the forge never closes). **Balance:** zero sim diff. **Test:** the send-off
slate names exactly the `PlayerCrafted` items worn by the departing party (enumerated from
state); a craft submitted during the Quest span lands in `Player`-held items and is
`SendSupply`-eligible at the stop (sim-side, no Godot).

### 3.3 Camp (Vigil) — *"Supplies, home, or deeper?"* — the phase to build first

This is the day's centerpiece, and nearly all of it already exists as mechanism: the parked
party (`state.InFlight`), the slate facts (`PartyCampReport`: HP, heals-left, target floor), the
two verbs, the fee, the front-insert, the attribution proof. What it lacks is being *staged as
the day's one real question* — which is exactly the vigil stop the in-flight U1 conductor builds.
Keep that stop. Then make it speak hero and open the forge.

| # | Verb | Sim action | Craft→hero visibility | Why not skip |
|---|---|---|---|---|
| V-1 | **The stop itself** — the world halts, indefinitely, on one modal: who is camped, how hurt, how provisioned, how deep they mean to go. Three answers: **Send the runner** / **Bring them home** / **Send them deeper**. | `SendSupplyAction` / `RecallPartyAction` / the Camp tick (all existing; the third verb is loop-U1's modal addition — keep) | The slate's heals-left column counts consumables — and gains one word: "of which **yours**: N" (`Item.PlayerCrafted` over the working pack). Your Morning's provisioning, or its absence, IS this screen | This is the only moment tonight's survival is in your hands. Recall banks the stage-1 ore (tomorrow's `BuyOre` offers) against pressing for the record; the modal never times out (KTD-A) |
| V-2 | **"The lantern burns while you work"** — from inside the stop: *Forge something for them.* The modal yields to the forge, you craft the salve, you come back, you hand it to the runner. The world waited, because the world is a party of three sitting in the dark under your checkpoint. | `CraftAction` then `SendSupplyAction` (both existing, both immediate, both legal in Camp — verified: `CraftingHandlers.CanHandle` is all-phases, `ActionTiming` resolves both Now; a fresh craft is held/unshelved, exactly what `SendSupplyLegal` requires) | The tightest my-hands→their-heartbeat chain in the game: forged at the stop → front-of-pack → **drinks before anything they bought** → `PotionLifesave` beat at the reveal naming *that* `ItemId`. The player can watch the whole chain in one day | The fee (9g) plus the day-slot cost make it a real decision, not a reflex; and the alternative — recall — costs the deep floors. This beat is the game's thesis performed |
| V-3 | **The slate speaks hero** — trait chips and history on the camped cards: "Torvald — *Reckless*: came down light on heals, as usual." "Mira and Aldric — *Grief*: they buried Senna together." Recall reframes urgent at ≤40% HP (exists: `FleeThresholdPercent`). | none (read-only: `TraitRegistry.TraitsFor`, `RelationshipSystem.EdgeFor`, both derivation-only) | Traits explain the situation your craft must answer (Reckless is *why* the heals column is empty); the answer is V-1/V-2 | The stop stops being three buttons and becomes three people. This is the Living-Heroes work (Phase B) finally standing where the stakes are |

**The empty vigil stays empty.** When nobody parks, the conductor ticks through with a caption —
no fake question, no fabricated stop (loop plan open-Q1, resolved: tick through). The cure for an
empty phase is honesty, not theater.

**Files:** `godot/scripts/panels/CampPanel.cs` (third verb + forge affordance + "yours: N" +
trait/edge chips), `godot/scripts/MainUi.cs` (`SyncCampModal` at :821 becomes the conductor's
VigilStop — in flight; add the forge round-trip: the stop must survive the modal yielding to
`ForgePanel` and re-presenting), conductor state machine (feat/two-bell-day). **Balance:** zero
sim diff — but note honestly: a surfaced vigil will be *used* more than today's buried one, so
real-play survival shifts even though the balance gate (BaselinePlayer never uses it) is
untouched. That is the point, and it is not a re-baseline. **Tests:** sim-side — craft during
Camp via `ApplyNow`, send it, drive Deep+Evening, assert a `Provisioned`/`PotionLifesave` beat
names the delivered `ItemId` (fixture with a lethal-without-heal stage 2); engine-side — the stop
survives a forge round-trip (open forge from modal, craft, return, Send enabled with the new
item); "yours: N" counts exactly the `PlayerCrafted` effect-items in the working pack; Hurry
never skips the stop (U1's existing scenario, kept).

### 3.4 ExpeditionDeep (Deep Vigil) — *"What's riding on the dark?"*

The honest sim fact: stage 2 is **undrawn** during this span (`InFlightExpedition` carries no
RNG state; the rolls happen at the Deep tick). Anything "shown" of the deep before that tick
would be fiction, and any verb aimed at it would be a lie. This span's job is dread, and dread is
cheap when the stakes are yours — so its content is one read-only surface, and its length is
short (the conductor's held-breath beat, then the tick, then the stage-2 show plays from the
resolved result at the top of Night).

| # | Verb | Sim action | Craft→hero visibility | Why not skip |
|---|---|---|---|---|
| D-1 | **The stakes slate** — one card while the winch creaks: "Below right now: Torvald (your Emberbite in hand, your salve front of pack, 11 HP at last light), Mira (rival mail). Riding on it: the floor-3 bounty, 40g." | none (pure projection over `InFlight` + `Bounties` + `Items.PlayerCrafted` — adapter-side, unit-testable without Godot) | It is the causal chain, restated as a wager already placed. Every line is a thing the player did (or didn't do) this very day | A player who Hurries past it loses nothing mechanical — and that is correct. What it buys is the *reason to watch* the stage-2 show that follows: you know exactly what your hands have riding |
| D-2 | **Hurry** | none (conductor) | — | The fast day stays fast; the stop was the decision and it already happened |

**Deliberately absent:** wagers, "brace" buttons, watch-to-buff mechanics — every one would
either be theater (no sim effect) or an autonomy break (sim effect from attention). Named and
rejected in §7.

**Files:** a small `DeepStakes` projection (plain C#, `godot/scripts/` beside `DelveBeats.cs`)
rendered by the conductor's held-breath beat / `ScryingMirror.cs`. **Balance:** zero.
**Test:** the slate lists exactly the player-crafted items worn/packed by camped heroes and every
live bounty with an acceptor below — enumerated from state, never hand-listed.

### 3.5 Evening (Night) — *"What did my work do — and who pays for tomorrow?"*

The reveal fires on the Night bell (`ExpeditionRevealSystem`, emission order fixed), so the Night
span itself is the last quiet hour before the town learns. Two verb families belong here — meet
the living, answer the dead — plus the reveal's re-ordering (loop U5, kept: **the night leads
with the mark**).

| # | Verb | Sim action | Craft→hero visibility | Why not skip |
|---|---|---|---|---|
| N-1 | **The homecoming show + the ledger that leads with your mark** — survivors walk out of the dark; the first card of the reveal is your item and its deed ("Emberbite turned the killing blow on floor 3 — Torvald lives"), sale-and-deed grouped by item, then the day's other news. | none (loop-U5 ordering over `AttributionBeatEvent`s — unchanged, kept) | This is the answer half, finally read aloud in the right order | It is the payoff of every verb above; skipping it is skipping the game's applause |
| N-2 | **"Meet the survivor"** — a hero at your counter with ore from the floor they cleared. You buy or you don't; the quantity slider is the decision weight. Staged as a handshake over the bar (minigames §3.6 tavern act — the hero-facing half, pulled forward), not a ledger row. | `BuyOreAction` (existing, Evening-legal). Honesty note: offers on tonight's state were posted at *yesterday's* reveal (`ExpeditionRevealSystem` doc — a 1-day lag by design); the fiction covers it ("took a day to haul it up"), the surface must not pretend otherwise | The ore in their hands came up a shaft your gear cleared; buying it feeds tomorrow's forge — the loop's material half, closed in person | Hero ore is strictly cheaper than the vendor floor (the designed upside, `BuyMaterialAction` doc); faction standing moves with it |
| N-3 | **"Answer the fallen"** — the farewell rite at the memorial; the heirloom reforge that carries a dead hero's legend line into a new blade. | `HonorMemorialAction` / `ReforgeHeirloomAction` (existing; same 1-day-lag note for fresh deaths — memorials are stamped at the reveal this bell fires) | The epitaph already leads with your work ("Emberbite, *your make*" — `GearSummary`, `ExpeditionRevealSystem.cs:272-289`); the reforge makes grief into tomorrow's gear with the lineage attached | The 1287×-memorial-nag finding (07-27) dies here: the rite is a verb with a place, not a recurring scold |
| N-4 | **"Aim tomorrow"** — post the Evening bounty against the board's floor minimums, with tonight's reveal fresh in hand. | `PostBountyAction` (existing, Evening-legal) | Tonight's deaths tell you which floor needs paying for; the bounty is the answer | The day ends the way it began: aiming |

**Files:** `LedgerModal.cs` (U5 — in the loop plan, kept), `TavernPanel.cs` +
`InteriorLayout2D.cs` (ore handshake staging — minigames M3's hero-facing half), `MainUi.cs`
return-ritual caption (U5), `PanelGraveyard.cs`/memorial surface (rite affordance).
**Balance:** zero sim diff. **Tests:** U5's list stands (beat-day leads with the beat; every beat
line names a `PlayerCrafted` item); tavern handshake commits a `BuyOreAction` matching the spoken
offer (minigames §3.6 test, unchanged); rite from the graveyard surface marks the memorial
honored and is idempotent.

---

## 4. What this does to the skip decision — the design's own test

A player who wants a fast day still gets one: Morning bell → (Hurry) → vigil stop *only if
someone parked* → (Hurry) → Night bell. Two bells and one question — the two-bell day's floor is
preserved intact. What changes is what skipping *costs*, and that the cost is named to the
player's face: hurry past Dawn's boards and you armed nobody; meet the stop empty-handed and the
modal says so ("no salve in the house — the runner has nothing to carry"); hurry the stakes slate
and the stage-2 show plays to a spectator who doesn't know what they're rooting for. Skipping
becomes a loss the player chose, not a relief the game engineered. That is the brief, restated
as mechanics.

---

## 5. Ruling on the in-flight `feat/two-bell-day` (loop plan U1)

**Continue — most of it, unchanged:**
- The conductor (`RaidConductor.cs`), choreography-completion beats, pinned maxes, the
  `PhaseClock.AdvanceNow` determinism path. This is the substrate the hero-facing day *runs on* —
  without it there is no stage for §3.2/§3.4 at all.
- The indefinite, timer-free vigil stop, including "recall during the stop still enters
  ExpeditionDeep" and "Hurry never skips the stop."
- Retiring the lying bell labels ("Lower them into the mine", "Ring the return bell") and the
  bell-rider flush semantics.
- Hurry beat-by-beat (plan open-Q2 — resolved by this reframing: beat-by-beat, always; straight-
  to-Evening would skip the stakes the middle now carries).

**Change — the goal statement and two seams:**
1. **The north star is wrong, the mechanism is right.** U1's DoD headline is "a full day with
   exactly two bell presses." Keep it as a *floor*, not the definition of done — the destination
   is "the middle is the show of the player's consequences with one playable stop," and the
   two-bell count is merely what's left when the dead cranks go. Do not spend another hour
   minimizing clicks; spend it making the stop worth stopping for.
2. **The VigilStop must be re-enterable, not fire-once.** §3.3 V-2 requires the modal to yield to
   the forge and re-present with Send re-evaluated. If the conductor models VigilStop as a
   one-shot gate that auto-continues when the modal closes, V-2 is dead on arrival. The state
   machine needs `VigilStop` to hold until *answered* (one of the three verbs), not until
   *closed*. This is a small contract on the in-flight code, cheap now and expensive later.
3. **Don't compress the no-park middle below its content.** The plan celebrates "an empty
   Camp/Deep costs ~a second of show." For the *empty* case, correct. But the send-off and
   homecoming beats are content (Q-1, N-1), not overhead — pace them by their choreography, not
   by a minimization instinct.

**Stop:** nothing in U1's file list. What stops is the *framing* — and one sentence of the plan
is formally withdrawn: "the smallest change that completes the loop is to stop asking the player
to crank the middle." The smallest change that completes the loop is giving the middle's one
question ammunition and an audience. U1 builds the audience's seat; this document is the
ammunition.

## 6. Revision of the minigames doc (2026-08-04-all-building-minigames)

Its core ordering — "loop first, minigame layer second" — stands. Its blanket claim — "minigames
are all Morning-side work ... adds nothing to the answer half" — was too coarse, because three of
its units are not station minigames at all but hero-facing staging, and those move **forward into
this wave**: M2 (market — the customer speaks; identical to loop-U2/M-1, build once), the
hero-facing half of M3 (tavern ore handshake → §3.5 N-2), and M4's vigil-at-the-gate (the stop's
physical home → §3.3). The craft-side acts (M1 SessionSkill, M5/M6/M7 alchemy/tanning/
engineering, the Wave-2 Contracts micro-PR) remain second — they deepen the player's hands but
face no hero, and the owner's brief is explicit about which comes first.

## 7. The new-sim-rule register — priced, not smuggled

Wave 1 above needs **zero** `Contracts/` edits. Two candidates earn a price tag; three are
rejected by name.

| Candidate | What it would be | Cost | Ruling |
|---|---|---|---|
| `ProvisionHeroAction` (gate gift) | Hand a held consumable to a departing hero during the Quest span — front-inserts before stage 1, symmetrical with the camp runner | Contracts micro-PR (orchestrator-authored) + handler + `ActionLegality` mirror + `ActionTiming` entry + **balance re-baseline** (more heals in packs = survival up, the 100-day gate moves) | **Defer, measured.** The commerce channel already covers provisioning (sell heals at the counter — M-1 makes it aimed), and Provisioned/PotionLifesave beats already credit it. Build only if post-M-1 telemetry shows heroes systematically under-buying heals — then it is a real gap, not a redundant verb |
| `BuyRoundAction` (tavern) | Gold → patron mood, the "pour the round" act | Priced in the minigames doc §3.6 already: Contracts + handler + PKD7 review + balance gate | **Unchanged: priced, not recommended.** Mood is influence-only; a verb paid in mood alone is the theater class this document polices |
| Mid-delve intervention (any form) | React to a fight in progress | — | **Rejected on premise.** No mid-delve state exists to react to (§1), and creating it would convert heroes into units. This is the constraint that makes the game itself |
| Outcome wagers / predictions | Bet on tonight's result at the stakes slate | — | **Rejected as busywork.** It manufactures investment the causal chain should earn; if the player needs a side-bet to care whose pack their salve is in, the failure is upstream |
| Vigil timer / attention tax | Pressure the stop, reward the watcher | — | **Rejected.** Decisions never carry timers (U15 verdict stands); watching must stay optional in wall-clock terms |

## 8. Order of work — smallest first, one phase named

**Build the Vigil first (§3.3).** It is the one phase that, done first, most changes how the day
feels: it converts the middle from "the part that plays itself" into "the part the day was
building toward," and it does so almost entirely with shipped mechanism — the parked record, the
slate, the two verbs, the fee, the front-insert, the attribution proof, and the in-flight U1
stop. **Cost:** the U1 conductor lands (in flight; plus the §5 re-enterability change, small if
made now), one CampPanel/MainUi unit (third verb + forge round-trip + "yours: N" + trait chips),
one sim-side attribution test, engine-suite time. Zero contracts, zero re-baseline.

Then, in order of felt-change per cost:

| Wave | Unit | Contents | Depends on |
|---|---|---|---|
| 1a | **H1 — The vigil, playable** | §3.3 V-1/V-2/V-3 on top of U1's stop | feat/two-bell-day (+ §5 change 2) |
| 1a | **H2 — The customer speaks** | §3.1 M-1 (= loop U2 = minigames M2 — one build) | none (parallel) |
| 1b | **H3 — The night leads with the mark** | §3.5 N-1 (= loop U5) + N-3 rite surface | U1 merged (MainUi seam) |
| 1b | **H4 — The send-off carries your mark** | §3.2 Q-1 slate captioning (+ Q-3 only if free) | U1 |
| 1c | **H5 — The stakes slate** | §3.4 D-1 projection + render | U1 |
| 1c | **H6 — Morning aims** | §3.1 M-3 board surfacing (gap→forge affordance), M-4 board reference | none |
| 2 | **H7 — The survivor's handshake** | §3.5 N-2 tavern staging (minigames M3 hero-half) | H3 |
| 2+ | minigames Wave 1/2 proper | craft-side acts, SessionSkill, Contracts micro-PR | owner ruling |

Every unit: one branch, one PR, zero `sim/` diff (CI-checked), receipts per the make-it-visible
discipline, engine suite orchestrator-run and serialized, tests waiting on conditions never frame
counts.

## 9. Definition of done — for the brief, not for this document

1. In every phase the player can name the hero-question in front of them and the verb that
   answers it, without opening a menu to find either.
2. A player who forges a salve at the vigil stop and sends it can, that same Night, point to the
   beat that names it — the full chain witnessed in one day.
3. A player who Hurries every span still finishes the day in under a minute, meets the vigil
   question if it exists, and is told — in copy, at the moment of skipping — what they left on
   the table.
4. No verb shipped under this document orders a hero, times a decision, or pays out in mood
   alone.
5. Zero `Contracts/` edits and zero balance re-baselines in wave 1; the golden replay stands
   untouched.
