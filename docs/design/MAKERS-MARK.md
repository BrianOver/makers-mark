---
type: design
title: "Maker's Mark — the central document: what it is, how it plays, why it is built this way, and the plan"
updated: 2026-08-06
status: THE living central document of record — the one place to look; update it in the same PR as the work
origin: >
  owner brief 2026-08-06 — "pause and regroup about how the core game mechanic plays…
  document a full idea of how the player interacts with the game. The idea behind the game,
  everything… so that when we are developing there's a lot more focus on things."
  Consolidated 2026-08-06 from the two prior central documents plus three external model
  reviews the owner collected.
audience: the owner first; then anyone — human or agent — who needs to understand the game
  before touching it
rule: >
  every mechanical claim below was checked against the code at the commit this document
  landed on — through five laps. A first pass was written from the design docs and plans; a
  second, independent pass was written from source only, with docs/ deliberately unread
  (it survives intact as Appendix A and wins every factual dispute); the third was the
  collision of the two, and the source-only pass won every dispute the collision found. A
  fourth, hostile-review lap attacked the collided text — re-verified its contested claims
  against source, grounded the pitch language the earlier laps let stand, and added the plan
  of record (§11). A fifth lap (2026-08-06) evaluated three external model reviews
  recommendation-by-recommendation — adopt / adapt / reject, each verdict grounded in code
  or measurement — and rolled the survivors in (§12).
  Where an older design document and the code disagree, the code wins and the disagreement
  is named. Where something does NOT exist — or exists but cannot be reached from the
  screen — it is marked so, unmistakably.
---

# Maker's Mark

## Executive summary

**What it is.** Maker's Mark is an inverted MMO: you are the town blacksmith — the NPC — and
the heroes are the game's AI: six-or-fewer autonomous, permadeath adventurers who shop with
their own gold, pick their own targets, and live or die by what you made. No verb in the game
commands a hero ("influence, never orders"). The crown jewel is the attribution engine: a
deterministic counterfactual replay that *proves*, from the recorded dice, that your specific
item turned a specific fight. "My sword saved his life" is never flavor text here; it is a
theorem. The whole game is arranged around making that proof happen and making it felt:
**craft → carry → proof → witness**.

**Where it actually stands.** The sim is real and disciplined: five-phase deterministic
kernel, four playable professions with four interactive crafts, three live venues, permadeath
heroes with traits and memories, save/load, a CI balance gate, byte-identical replay. The
morning (cause) side works by machine measurement; the proof engine works by test. But: the
witnessing is half-staged — the send-off doesn't name your work and the Night reveal buries
its own headline; four of the 24 player actions — the entire endgame economy — are reachable
only in the console, not the shipped client; the camp phase, the fiction's centerpiece, runs
zero simulation systems, and its flagship verb (the supply run) measurably *raises* deaths at
campaign scale; the measured boredom wall is day 11–13; and nothing in CI proves a campaign
can reach its own ending.

**The single biggest risk.** No human being has ever played this game to completion — every
"player" in the entire evidence corpus is a scripted machine persona. The mechanisms are
verified; the *feeling* is not. Whether the first attribution line produces in a person what
it produces in a transcript is the most important unanswered question in this document, and
the plan treats it that way (P4).

**The immediate plan (§11).** Phase 0: six one-line rulings from the owner (R1–R6, one
sitting, no code). Then the critical path: **P1** the Night reveal leads with your mark →
**P2** the send-off names your work → **P3** CI proof the campaign can end → **P4 the owner's
one real evening at the keyboard** — the gate most remaining rulings wait on — then the vigil
branch (P5), the endgame surfaces (P6), and the day-11 demand program (P7).

**What the external reviews changed (§12).** Three outside model reviews were evaluated
recommendation-by-recommendation. Most recommendations confirmed what is already built or
already planned — useful independent signal, and it is recorded as such. Four things were
genuinely adopted: a new focus-filter law (**"hand-work must beat passive systems, or the
passive system must not exist"**), a recorded default for the provisioning ruling (damp the
risk-compensation push), the game's honest identity (**Spiritfarer-cozy: warmth about loss**
— not Stardew-cozy), and a commitment that the **Final Commission ending is owed** (new R6).
The full adopt/adapt/reject ledger, per review and per recommendation, is §12.

## Contents

| § | Section | What it holds |
|---|---------|---------------|
| [1](#1-what-this-game-is-in-one-page) | What this game is, in one page | The premise, honestly stated |
| [2](#2-the-spine--from-your-hands-to-their-story) | The spine | The causal chain, link by link, with the code trace |
| [3](#3-how-to-play) | How to play | A day, the first week, day 30, the six dilemmas |
| [4](#4-the-phases--what-each-one-is-for) | The phases | What each phase asks, and its honest state |
| [5](#5-the-core-systems) | The core systems | Each system: what, why, and the cost of cutting it |
| [6](#6-why-it-is-built-this-way) | Why it is built this way | Architecture as design decisions |
| [7](#7-inspirations--what-was-taken-what-was-refused) | Inspirations | What was taken, what was refused, and why |
| [8](#8-what-is-built-what-is-designed-what-is-wished-for) | Built / designed / wished-for | The status ledger; corrections; known defects |
| [9](#9-the-known-tensions-and-open-questions--for-the-owner-to-rule-on) | Open questions | §9.1–§9.10, each phrased for a one-line ruling |
| [10](#10-what-focus-means-from-here--the-filter) | The focus filter | The eight tests a piece of work must pass |
| [11](#11-the-plan-of-record--from-the-gold-outward) | The plan of record | Rulings R1–R6, path P1–P9, the cut list, the drift defense |
| [12](#12-the-external-reviews--what-came-from-outside-and-what-we-did-with-it) | **The external reviews** | Every outside recommendation: adopt / adapt / reject, with reasons |
| [13](#13-glossary) | Glossary | Terms of art |
| [14](#14-the-documents-behind-this-one) | The documents behind this one | Where the full arguments live |
| [A](#appendix-a--mechanics-ground-truth-derived-from-source-2026-08-06) | **Appendix A — mechanics ground truth** | The source-only control pass; the arbiter for any mechanical dispute |

---

This is the one central document. It says what the game is, walks the whole causal chain the
game exists to deliver, teaches a new player how to play it, explains what every phase and
every system is *for*, defends the architecture in design terms, names the inspirations and
what we took or refused from each, sorts everything into **built / designed / wished-for**,
lists the open questions that need the owner's ruling, gives a working definition of
"focus" — the test a piece of work must pass to count as moving the game forward rather than
sideways — carries the plan of record (§11), the ordered, cut-listed path from what exists
today to the game the rest of this document describes, and accounts for every external
recommendation the owner collected (§12).

It is long on purpose. It is written to be argued with: mark it up, strike sections, rule on
the questions in §9, and the marked-up version becomes the goal we develop against.

It carries its own arbiter: **Appendix A**, the same game described **from source only** —
written with `docs/` deliberately unread, every claim line-cited. It exists because this
project's documentation has repeatedly drifted from its code, and the owner keeps
discovering that "this exists" meant "this was planned." When a mechanical argument starts,
settle it there; the body of this document carries the design intent and the honest status
of every piece, and it defers to Appendix A on any fact of mechanism.

Because that confusion is the core complaint, every piece of the game named below carries
one of five status labels, and they are never blurred:
**BUILT** (on screen, playable in the shipped Godot client) · **BUILT, CLI-ONLY** (real in
the sim and console runner, no screen surface — *not shipped* under this project's own
"DEPLOYED means 3D" rule) · **BUILT-INERT** (complete and deliberately dormant behind a
content flip) · **DESIGNED** (agreed on paper, no code) · **WISHED-FOR** (no code, no plan
of record). The consolidated table in §8 is the at-a-glance ledger.

Terms of art (a **bell**, a **vigil**, a **mark**, a **delve**, a **slot**) are defined at
first use and collected in the glossary (§13). File references look like
`sim/GameSim/Kernel/GameKernel.cs:188`
and point at the real code so any claim can be verified in one jump.

---

## 1. What this game is, in one page

**Maker's Mark is an inverted MMO.** In an ordinary MMO you are the hero, and the blacksmith
in town is furniture — a vending machine with a beard. This game plays that world from the
other side of the counter. You are the blacksmith. The heroes are the game's AI: autonomous
adventurers who shop with their own gold, form their own parties, choose their own targets,
descend into the Mine on their own judgment, and live or die by what they carried — which is
to say, by what *you* made. That autonomy is real but narrow, and this page should not
oversell what §5.2 measures: it is five deterministic, legible rules over true memories, not
an inner life — "form their own parties" is a deterministic grouping, "choose their own
targets" is one-past-their-best arithmetic. What makes the inversion load-bearing is not
that the heroes are people; it is that **no verb exists by which you can command one**, and
that structural fact is what makes the game's proof layer (§2 link 4) possible at all.

You never fight. You never give an order. Nothing in the game lets you steer a hero — not a
command, not a nudge disguised as a command. What you have instead is a forge, a shop
counter, a price tag, a bounty board, and a reputation. Every lever you own is **influence,
never orders** (the design law the code calls PKD7): you shape what exists in the world, and
the heroes decide what it's worth.

Moment to moment, that means the game is about **work and witness**. In the morning you work:
hear what a customer wants, take a commission from a hero who asks you by name, forge to the
gap in tomorrow's muster, price the shelf, post a bounty toward the floor you want cleared.
Then the world moves — heroes march past your shop wearing your work — and you watch what
your morning did. Some days the watching is pride: the night's ledger opens with "Emberbite
turned the killing blow on floor 3 — Torvald lives," and Emberbite is yours; your mark is on
it; the game can *prove* it made the difference, mechanically, not narratively. Some days the
watching is grief: a hero dies wearing your armor, the death report names the gear, a
memorial stone goes up, and the blade comes back to your bench to be reforged into an
heirloom that carries the dead hero's name forward into someone else's hands.

That loop — **your craft becomes a specific person's outcome becomes a story that names you**
— is the whole game. The economy exists to feed it. The spectacle exists to stage it. The
project's north star, unchanged since the first plan: *your craft writes the legends*, and
narrative wins ties.

The feeling we are building toward is the feeling of mattering to people you cannot control:
the quiet pride of a craftsman whose work is out there doing things, the dread of the vigil
when a party you outfitted is camped above the deep floors, and the strange tenderness of a
town that remembers — every sword with a history, every death with a name, every legend with
a maker's mark on it. Call that feeling by its honest name, because "cozy" undersells the
graveyard: this is not Stardew-cozy but **Spiritfarer-cozy — warmth about loss** (§5.7; the
naming is adopted from the external-review lap, §12).

It is a small game on purpose — complete systems, modest content ("half vision": one town,
three live venues, ~39 recipes, and a cast you can actually know because it is never larger
than **six heroes alive at once**, refilled at most one recruit every two mornings
(`Drama/RecruitSystem.cs:22-27`)) — built as a hobby, kept shippable (CI-green,
`play.ps1`-launchable at every commit), and engineered so ruthlessly deterministically that
the same seed and the same choices produce the same world, byte for byte — a property CI
enforces with a build-failing replay test, not a slogan.

---

## 2. The spine — from your hands to their story

The pitch above is a slogan until you can trace it as a mechanism. Here is the chain, link by
link, through the real systems. Every link exists in code today; where a link is recorded as
data but weak as an *experience*, that is said too, because it is the central open problem
(§4, §9.6).

### Link 1 — You make a thing, and it is provably yours

A craft is a `CraftAction` (`sim/GameSim/Contracts/Actions.cs`). In the real client it
carries the trace of a two-act minigame — shape the billet at the anvil while working the
bellows, then a single timed **quench** plunge
(`godot/scripts/minigames/ForgeMinigame.cs`, `QuenchMinigame.cs`) — which the pure sim-side
scorer grades deterministically (`sim/GameSim/Crafting/ForgeScorer.cs`). The grade dominates
quality: the one RNG draw per craft is a small ±25‰ jitter
(`sim/GameSim/Crafting/QualityRoller.cs:107-156`), skill sets the band
(Poor/Common/Fine/Superior/Masterwork), the material's grade caps it, and auto-crafting
without the minigame hard-caps at Superior. Be precise about the top band, because the docs
have been sloppy here: a rolled Masterwork needs **both** a top-grade minigame trace **and**
over-tier material — an even-tier material caps at Superior no matter how perfect the hands
(`QualityRoller.cs:160-190`). Skill is necessary, never sufficient; the ore ladder is the
other half of the gate. (The two priced substitutes — the masterwork attempt and the
legendary commission — exist in the sim but are CLI-only today; see §8.)

The minted item (`sim/GameSim/Contracts/Items.cs:43`) carries a `MakersMark` — your name and
the day (`Items.cs:26`) — plus an append-only `History`, optional craft modifiers (quench
oil / rune / fitting, one per family, `Items.cs:13-23`), and hooks for a legend name
(`SignedName`) and an inheritance line (`HeirloomLineage`). Rival-vendor goods have no mark.
`Item.PlayerCrafted` (`Items.cs:106`) is the single predicate the whole attribution layer
keys on: the game only ever proves deeds for things you made.

### Link 2 — The thing reaches a hero, through one of four honest channels

1. **The shelf.** You `Stock` an item at a price; every Morning the shopping pass
   (`sim/GameSim/Heroes/HeroShoppingSystem.cs`) walks every living hero past every shelf.
   The verdict logic (`sim/GameSim/Heroes/ShoppingAi.cs`) is pure and legible: role fit,
   weight, affordability, is-it-an-upgrade, quality pickiness for floor-3+ veterans,
   sentiment for storied gear — every pass emits a typed reason you can read
   (`ShoppingAi.cs:15-24`). This "atomic pass" is the dominant income channel.
2. **The counter.** You can serve customers in person: `OpenCounter`, and the customer now
   *speaks first* — "Looking for a shield — about 45g on me" — a want line derived read-only
   from the hero's own gap query and purse (`godot/scripts/ui/CustomerVoice.cs:38`). Present,
   suggest, haggle inside a willingness model with a patience cap and a session-long memory
   of being fleeced (`sim/GameSim/Counter/WillingnessModel.cs`, `HaggleResolver.cs`). One
   honesty note, because the surface implies otherwise: **a counter-offer can never lose the
   sale.** Every `Counter(price)` closes the deal — a price above the round's ceiling is a
   *fleece* (sale plus mood −80 and a goodwill hit), not a walk-out; only patience exhaustion
   or an unwanted item walks (`HaggleResolver.cs:124-159`). The risk in haggling is to the
   relationship, never to the transaction. This is the game's face-to-face beat; it is also,
   measured, economically weaker than the atomic pass (§9.5).
3. **The commission.** A mustering hero with a real gap asks you *by name*: slot, minimum
   quality, deadline, premium (`sim/GameSim/Heroes/CommissionSystem.cs`). Premiums scale with
   target floor and relationship (`CommissionSystem.cs:219-220`); a promise you break costs
   mood (`ExpireMoodPenalty`, `CommissionSystem.cs:55`). Since the commission board learned
   to ask for consumables and (for regulars) trinkets (`CommissionSystem.cs:251-288`), every
   profession's work can be the subject of a named, deadlined ask — this repaired the worst
   hole the 07-27 audits found (§8, "corrections").
4. **The camp runner.** Mid-raid, if a party camps (Link 3), you can pay a runner to carry
   one held consumable down to a named hero (`sim/GameSim/Expedition/CampHandlers.cs`). The
   delivery front-inserts into the hero's working pack, and the resolver drinks front-first —
   *your delivery is consumed before anything they bought themselves*
   (`CampHandlers.cs:46-51`). The fee (9g at the floor-1 camp) is deliberately priced above
   the 8g salve sale so provisioning is a decision, not a reflex (`CampHandlers.cs:24-29`).
   The measured fine print on that decision is §9.9 and it is not small: sending supplies
   saves *individuals*, provably — and raises *aggregate* deaths.

### Link 3 — The hero carries it into the dark, on their own judgment

Parties form themselves (`sim/GameSim/Heroes/PartyFormation.cs`). A draw-free router sends
each party to a venue by power band and queue length
(`sim/GameSim/Venues/VenueRouter.cs`) — the Mine, Gloomwood, or the Sunken Crypt today
(`VenueRegistry.cs:62-66`). Target floor is their own ambition — one past their best — unless
a hero *chose* to accept your bounty, which commits the party to that floor
(`sim/GameSim/Expedition/ExpeditionSystem.cs:50-82`); acceptance itself is a legible
greed-vs-reputation score, Majesty-style, never a command
(`sim/GameSim/Bounties/BountyRules.cs:21-70`). Name the whole price of that sentence,
because Appendix A does and earlier drafts here did not: an accepted bounty is
**the one order money can buy**. It overrides the party's own target floor, routes them to
the Mine unconditionally, and — the part our own docs kept quiet — *suspends the acceptor's
competence-retreat rule* through the bounty floor: a hero who would normally peel off at
their personal ceiling fights past it because you paid them to
(`ExpeditionSystem.cs:78-82,122-125` — the code's own comment reads "accepting the bounty IS
the commitment"). The premise survives because the consent is real, legible, and refusable —
heroes reject bounties past their reach outright — but any argument about "influence, never
orders" should start from this exception rather than pretend there is none.

The raid resolves in **two stages**. Stage 1 — through floor 1, the checkpoint — resolves
at the moment of departure; a party bound deeper that clears it cleanly **parks** — camps — and the world
stops on the day's one real question: *supplies, home, or deeper?* Stage 2 resolves only
after that question is answered (`ExpeditionSystem.cs`, `ExpeditionDeepSystem.cs`). The
tuning comment on the checkpoint records why the camp matters: across the telemetry corpus,
**87.1% of hero deaths happen below the camp window** (`ExpeditionSystem.cs:20-26`). The
vigil is not a pause; it is the one moment tonight's survival is in your hands.

### Link 4 — The game proves your item mattered — this is the crown jewel

`sim/GameSim/Expedition/AttributionEngine.cs` re-runs the recorded combat math with your
item's stats removed — a counterfactual replay over the *recorded* rolls, drawing no new
randomness — and emits a beat only when the difference is provable: **KillingBlow**,
**LethalSave**, **BreakpointClear**, **Provisioned**, **PotionLifesave**
(`sim/GameSim/Contracts/Enums.cs:56-64`). Beats are only computed for marked items. This is
the mechanical heart of the thesis: "my sword made the difference" is never flavor text — it
is a theorem checked against the fight that actually happened.

### Link 5 — The outcome becomes the town's memory, with your name in it

The Evening reveal (`sim/GameSim/Drama/ExpeditionRevealSystem.cs`) applies the day: deaths
(permadeath — `Alive` flips once, never back), loot to survivors only, depth records, ore
offers from the floors your gear cleared, and attribution beats stamped onto the item's
history and the bearer's memory (`Contracts/Heroes.cs:34`). From there the memory fans out:
the ticker narrates; tavern gossip retells real beats in four fixed voices (never invented —
`sim/GameSim/Drama/GossipGenerator.cs`); the ledger and provenance cards read an item's
deeds back; a rare craft earns a legend name (`Crafting/ArtifactSigning.cs`); the famous dead
are derived, not curated (`Drama/LegendQuery.cs`); a death raises a memorial you can honor,
and the worn gear can be **reforged into an heirloom** that carries the dead hero's line
into a new blade (`Crafting/HeirloomHandlers.cs`). A three-act campaign arc watches the
depth record and fires a climax and an ending, after which the world stays open
(`sim/GameSim/Arc/ArcDirectorSystem.cs`). Two honesty notes on the arc: the ending is real
on screen — `CampaignEnded` carries the final tallies and `ChronicleScroll` renders them
(`godot/scripts/panels/ChronicleScroll.cs`) — but the **climax is a bare seam**: `ClimaxReached`
fires with no content behind it, and its own doc says so ("the Final Commission /
Warden-of-the-Heart content the plan describes is a later, orchestrator-wired hook — this
event is the seam it lands on", `Contracts/Events.cs:293-297`). See §9.7.

**Trace it yourself.** The whole spine, function by function, so no link is taken on faith —
this is the chain a debugger actually walks:
`ForgePanel` builds `CraftAction` (`godot/scripts/panels/ForgePanel.cs:535`; minigame path
`QuenchMinigame.cs:279`) → `SimAdapter.Queue` → `GameKernel.ApplyNow` →
`CraftingHandlers.ApplyCraft` (`Crafting/CraftingHandlers.cs:40`) → `ForgeScorer.Score` →
`QualityRoller.RollActive` → `ItemForge.Forge` (mints the `MakersMark`) → `StockAction`
(`Economy/ShopHandlers.cs:42`) → next Morning `HeroShoppingSystem.Process` →
`ShoppingAi.EvaluateItem` (`Heroes/ShoppingAi.cs:110`) → `MusterSystem` predicts →
`ExpeditionSystem.Process` → `PartyFormation.FormParties` → `VenueRouter.ChooseVenue` →
`ExpeditionResolver.ResolveStage1`, where `CombatMath.HeroAttack` reads the equipped weapon
(`Expedition/CombatMath.cs:51-52`) and every die lands in `CombatEvent.RecordedRolls` →
`AttributionEngine.ComputeBeats` replays those recorded rolls counterfactually
(`Expedition/AttributionEngine.cs:19-145`) → Evening `ExpeditionRevealSystem.Reveal` stamps
the beat onto the item's `History` and the bearer's `ItemMemory` → next Morning
`GossipSystem` retells it, citing the real `EventId`. Appendix A §4 carries
this same trace with every intermediate line number.

**The honest caveat, and the current battleground:** every link above is *recorded* end to
end as data. The 2026-08-03 loop analysis confirmed no link is missing
(`docs/plans/2026-08-03-001-feat-loop-structure-plan.md` §3). What has historically been
weak is the *staging* — cause and consequence rarely shared the screen: links 2–4 resolved
inside invisible instants and link 5 arrived as one Night text flood. That is the "feedback
shadow" the 07-25 audit named, the owner's repeated "the loop is not complete," and the
subject of the current work wave (§4).

---

## 3. How to play

Written for someone at the keyboard. The game's own words appear as the client uses them.

### 3.1 A day, start to finish

You wake at **Dawn** (the sim calls it Morning — the player-facing names live in
`godot/scripts/ui/PhaseVocab.cs`). What you are looking at — a fact this document never
stated before a reviewer asked — is a **top-down 2.5D pixel-art town**
(`godot/scripts/town2d/`). It is the surviving render layer of three: the layer has been
thrown away twice, most recently a full 3D town abandoned on 2026-07-27, without the sim
noticing either funeral (§6). The art is functional, not final — it
has never had the human tuning pass the pipeline docs keep promising — and one venue
(Emberfall) has no art at all (§9.1). The town square is yours to walk; buildings are rooms
you enter; stations inside them open the real controls.

**Dawn is where you act.** Your levers, roughly in the order a good morning uses them:

- **Read the boards.** The muster board forecasts today's parties, their target floors, and
  who marches with an empty gear slot (`sim/GameSim/Heroes/MusterSystem.cs`,
  `RaidForecast.cs` — the prediction byte-matches what the expedition will actually do). The
  commission board holds up to three open asks from named heroes
  (`CommissionSystem.cs:43`): slot, minimum quality, five-day deadline, premium.
- **Take or decline promises.** Accepting a commission is free; missing its deadline stings
  the hero's opinion of you. Declining honestly is a real move.
- **Work the forge.** Crafting is one of your five daily **slots** (see below). The forge is
  a two-act minigame, ~10 seconds when you're good: hold heat in the band with the bellows
  while striking shape on the anvil, then one timed quench plunge — higher-tier metal narrows
  the quench band (140/100/70‰ by tier, `QuenchMinigame.cs:61-63`). Your demonstrated
  accuracy shrinks the labor (21→18 required strikes) without ever widening the target. After
  a minigame craft, **"Forge another like it"** repeats the recipe at your proven grade for
  one click — skill is demonstrated once, not re-performed per unit.
- **Serve the counter.** Open it and customers queue; each states a want and their budget.
  Present the piece, or suggest something they didn't ask about, and haggle — accept, hold
  firm, or counter at a pin. Know the table's real rules: holding firm spends the customer's
  patience and can end in a walk-out, but a counter-offer *always* closes the sale —
  over-asking is a fleece the hero pays for and remembers (mood and goodwill down), never a
  refusal (`HaggleResolver.cs:124-159`). Pinning near their true willingness earns goodwill;
  the stakes of the haggle are the relationship, not the sale. While the counter is open the
  day *holds* — the bell will not move past you (`GameKernel.cs:190`).
- **Buy materials, stock the shelf, set prices** — shelf-arranging is free; vendor purchases
  cost a slot.
- **Post a bounty** — escrowed gold against a floor you want cleared. Heroes weigh it
  (greed × reward − reputation/distance) and take it or don't; unclaimed bounties refund
  after three days (`BountyRules.cs:13-19`).

**The slot budget** is the day's strategic constraint: five "real work" actions per calendar
day (`Contracts/ActionBudget.cs:18`) shared across crafting, buying, bounty-posting, heirloom
reforging, and the late-game gold sinks (the sinks themselves are CLI-only today — §8).
Everything conversational — commissions, the
counter, shelving, talents, rites — is free. (A footnote for whoever touches this next: nine
handlers actually decrement the budget, but the display predicate
`ActionBudget.ConsumesSlot` names only four action types and its test pins "exactly the
four" — so any surface that reads the predicate under-reports what spends a slot. Small,
real, worth one cleanup PR.) Playing well is triage: *whose* gap do you close today with the
slots you have. Actions resolve the instant you take them (your hands, your bench —
`sim/GameSim/Kernel/ActionTiming.cs`); only three ceremony verbs still ride the bell:
building a forge tier, declaring your profession, and commissioning a legendary work.

When you're done, you ring the first **bell**: **"Send them off."**

**The Quest span plays itself.** This is new and load-bearing: since the two-bell day
shipped (`godot/scripts/RaidConductor.cs`, PR #388), the middle phases — **Quest** (the
march), **Vigil** (the camp), **Deep Vigil** (the deep floors) — are a conducted show, not
bells you crank. Heroes file past the shop and into the dark. Stage 1 resolves. If nobody
parked (every first trip targets floor 1, which is unstaged — the vigil is deliberately the
*uncommon* case early), the day flows on through a beat or two of show. Your forge stays lit
the whole time — craft, stock, reprice freely mid-day; the phase-agnostic verbs never close.
A **Hurry** control skips ahead; it can never skip the one thing that matters:

**The vigil stop.** If a party cleared stage 1 clean, the world halts — indefinitely, no
timer — on the winch-house slate (`godot/scripts/panels/CampPanel.cs`): who is camped, their
HP, their heals ("of which yours: N"), the floors and monsters still below, read off the
same venue data the resolver will roll against. Three verbs, and only these end the stop:
**Send the runner** (9g, one consumable, front of their pack), **Bring them home** (bank the
shallow loot, forfeit the deep floors), or **Send them deeper**. You can open the forge from
inside the stop, craft the salve, and hand it to the runner — the world waits, because the
world is three people sitting in the dark under your checkpoint.

**Night.** The second bell — **"Snuff the lanterns"** — brings the reveal: the return
ceremony, the ledger, deaths and their memorials, loot, new depth records, and the beats
that name your work. Night verbs: buy ore off returning heroes (the offers on tonight's
slate were posted at yesterday's reveal — hauling it up takes a day), honor a memorial,
reforge a fallen hero's gear, post the evening bounty with the day's lesson fresh. Note the
quiet double duty of **BuyOre**: you pay the hero directly, so buying a broke regular's ore
is also how you fund tomorrow's sale to them — the game's one sanctioned gift channel.

Then the day turns, slots refill, rent ticks a day closer (30g every 10 days,
`Contracts/World.cs:112-115`), the Guild's seven-day assessment looms
(`World.cs:150-155`), and it's Dawn again.

### 3.2 Your first week

Day 1 is deliberately harsh: in every instrumented campaign so far, at least one of the
starting heroes died on floor 1 that first day, and the death report named whatever they
were wearing. You learn the loop under the
tutorial's pointing finger: craft a dagger, shelve it, watch someone buy it — or watch
everyone pass on it, each with a reason you can read. By day 2 or 3 you've hit the tier gate
("Recipe 'longsword' is tier 2; requires talent 'tier-2-smithing'"), unlocked your first free
talents, and discovered the two-day rhythm the slot budget forces on tier-2+ work: buy
materials today, craft tomorrow. Around day 4 or 5 your first commission lands, your first
haggle closes ("you read them perfectly"), and — if a party parks — your first vigil asks its
question. By day 7 the arc has usually flipped to Act II (someone reached floor 3,
`ArcDirectorSystem.cs:37`), the first memorial stands in the graveyard, and the ticker has
said your name: "Torvald — Longsword landed the killing blow." That first attribution line —
from "everyone rejects my gear" to "my sword is doing the killing" — is the strongest beat in
the instrumented playthroughs. And here this document owes its most uncomfortable
disclosure, because earlier drafts wrote "a new player reports" as if such a person existed:
**every "player" in this project's entire evidence corpus is a scripted machine persona.**
The feel-test has never been run; no human playthrough has been completed and reported
(§9.8). Everything in this section describing how the game *feels* is a machine-verified
mechanism wearing a human-unverified feeling. The mechanisms are real — the telemetry is
honest — but whether the first attribution line produces in a person the thing it produces
in a transcript is the single most important unanswered question in this document, and the
plan (§11) treats it that way.

### 3.3 By day 30

The campaign has shape. Veterans (floor 3+) refuse Poor work outright, and Discerning
customers demand a grade better still (`ShoppingAi.cs:68-81`), so quality is no longer
optional; the Superior bar in front of floor 5 forces you into the minigame's top grades on
above-tier material (both are required for Masterwork — §2 link 1; the priced shortcuts are
CLI-only).
Relationship bands matter: regulars ask for trinkets, patrons and sworn customers demand
your best and pay premiums for it (`CommissionSystem.cs:195-220`). A hero you've ignored for
six days quietly starts favoring the rival's shelf — telegraphed two days ahead, never a
hard lockout (`NeedsSystem.cs:49-53`). Three venues compete for parties; ore ladders from
each feed different recipes; the Guild's dues escalate.

But be honest about which wall arrives first, because this section used to blur two walls
into one. The **engagement wall is measured at day 11–13, not day 30**: two independent
instrumented personas hit "no reason to keep playing" there, and the five-pillars synthesis
diagnosed why — a *question shortage, not a content shortage*: the player answers "the
stalled hero needs a Weapon" roughly thirty times by day 11
(`docs/design/2026-07-25-improvement-research-agenda.md` finding 9;
`2026-07-27-five-pillars-design-synthesis.md`). The loop waves shipped since may have moved
that number; **no measurement since says so**, and until one does, day 11 is the number this
document carries. The **economy wall** arrives later, near day 30, when surplus gold has
nowhere to go on screen: the four late-game gold sinks (forge tiers, forge supplies,
masterwork attempts, legendary commissions) are built, tested, and reachable **only in the
console runner** (§8, §9.10). Fixing the second wall does not fix the first.

If your quality has kept pace with the depth bar, someone reaches floor 5 — Act III, the
climax — and five days later the ending plays: a chronicle summary over everything your mark
touched. The world stays open after. Two honesty notes on that sentence. First: if quality
has *not* kept pace, this is where the game stalls — floors 3–4 forever. That wall is
deliberate (it is the minigame's reason to exist), but whether it is *legible* enough is a
live question (§9.3). Second, and worse: **nothing currently proves a campaign gets there.**
The balance gate's floor-5 and Act-III assertions are one-sided trivialization ceilings — a
campaign that never reaches floor 5 and never ends passes CI green
(`BalanceSimTests.cs:86-87`; `ArcBalanceTests.cs:43-52` wraps its Act-III and Ending checks
in `if (> 0)`) — and the only full-length recording on the real client (90 days, 2026-07-27)
**never left Act II**. The harness comment says the corrected sim reaches floor 5 around day
12–13 under baseline play, but no assertion pins it and no client-side run since has
confirmed Act III on an actual screen. The finale is unprotected; §11 P3 closes this.

The **six recurring dilemmas** that make up actual play, distilled from fifteen instrumented
playthroughs (`docs/design/2026-07-27-how-you-play.md`):

1. **Slot triage** — five real actions against unbounded demands.
2. **Which hero, which gap** — the muster names many; you can close few.
3. **Commission vs shelf** — the premium is guaranteed; the named hero's solvency is not.
4. **Gold to yourself vs gold into a hero** — the BuyOre gift unsticks a broke buyer.
5. **A death: move on, or reforge** — the fork between a stat line and a legend.
6. **Recall vs press deeper** — the vigil's question; survival traded against depth, and
   recall is the single largest measured lever in the game. Quote the whole measurement,
   because earlier drafts quoted half of it: deaths 13→2 **and loot 3,013g→928g** (the
   07-25 A/B). Recall buys eleven lives at the price of two-thirds of the deep floors'
   yield — the omitted half is the very thing that makes this a dilemma instead of a
   dominant strategy.

---

## 4. The phases — what each one is for

The sim's day is five phases walked by the kernel: Morning → Expedition → Camp →
ExpeditionDeep → Evening (`GameKernel.cs:188-198`). The player sees six words — Dawn,
Prepare (Morning while the counter holds it), Quest, Vigil, Deep Vigil, Night
(`PhaseVocab.cs:27-46`). The presentation rule that now governs them, shipped with the
two-bell day:

> **A phase is player-operated iff the player has phase-specific verbs in it. Otherwise it
> is part of the show, and the show runs itself.**

This section is honest about what each phase asks — and about the owner's central complaint,
which two documents diagnosed in sequence and the second corrected the first. The
loop-structure plan (2026-08-03) read the empty middle and prescribed *removing the player's
obligation to crank it* — the two-bell day. The hero-facing-day ruling
(`docs/design/2026-08-04-hero-facing-day.md`, the loop authority of record) **formally
withdrew the claim that this completes the loop**: fewer bells is the floor, not the
destination. A raid that plays itself past a player with empty hands is still a phase the
player skips — now with less guilt. The middle is where the game's one unique thing lives
(*my craft decides their fate, and I watch it happen*), so the fix is to fill it with the
smith's own hands, not to optimize it away. Its design rule:

> **Every phase poses one hero-question the player can answer with their own hands, and
> every answer leaves a fingerprint the Night ledger can point back to.**

| Phase (player name) | The question it poses | Verbs it holds | Honest state today |
|---|---|---|---|
| Morning (**Dawn/Prepare**) | *Who marches today, and what of mine will they carry?* | Nearly everything: craft, buy, stock, price, the whole counter session, commissions, bounty, talents, sinks | **The game lives here, and — by machine measurement — it works.** Decision and feedback both. The customer speaks first now; the boards forecast; the commissions ask by name. The human verdict is §9.8's still-unrun feel-test. Risk is the inverse: Morning holds so much that the rest of the day can feel like an epilogue. |
| Expedition (**Quest**) | *What will I have in hand when they need me?* | No phase-specific verbs — by design (nothing has rolled yet). The forge family stays open; the send-off is the show | **Honest but thin.** The send-off plays; the conductor carries the span. Not yet built: the departure slate that names which marchers carry *your* items (hero-facing-day Q-1) — the antecedent that makes Night's beat land. Without it, this span is watchable but not yet *about you*. |
| Camp (**Vigil**) | *Supplies, home, or deeper?* — the day's one real decision | SendSupply / RecallParty / Send-them-deeper; the forge round-trip from inside the stop | **The centerpiece as presentation; the thinnest phase as mechanism.** The stop halts the world untimed (#388); the slate states the stakes in hero terms and counts "yours: N"; craft-and-send is a discoverable button (#392). But **no sim system runs in Camp — zero registered systems carry `Phase == Camp`** (`GameComposition.cs:57-77`); the phase exists purely as the action window for its two sim verbs — send-supply and recall; "send them deeper" is just letting the day resume. And those two verbs point in measured opposite directions: recall is the single largest life-saving lever in the game (deaths 13→2 — at a measured price the same audit names, loot 3,013g→928g), while the supply run *raises* aggregate deaths (§9.9). Fires only when a party parks — the empty vigil honestly ticks through. Remaining: V-3 chips, and the §9.9 ruling, which decides whether the flagship verb is a tragedy or a defect. |
| ExpeditionDeep (**Deep Vigil**) | *What's riding on the dark?* | None, and none should exist: stage 2 is undrawn during this span — any verb aimed at it would be a lie | **Deliberately a held breath.** Not yet built: the stakes slate (D-1) that restates the wager — your items in their hands, your salve front of pack, the live bounty — so the watcher knows what they're rooting for. |
| Evening (**Night**) | *What did my work do — and who pays for tomorrow?* | BuyOre, PostBounty, HonorMemorial, ReforgeHeirloom; the reveal | **The payoff, currently under-ordered.** The reveal applies everything correctly but still arrives as a flood; "the night leads with the mark" (loop-plan U5 — attribution beats as the opening card, sale-and-deed grouped by item) is designed, agreed, and **not built**. This is the single cheapest unshipped piece of the answer half. |

Two structural facts to hold onto when arguing about phases:

- **The middle cannot be filled with intervention.** Stage-1 resolves in one tick at the
  phase boundary; the parked camp record carries no evolving mid-delve state
  (`Contracts/Expedition.cs`); stage 2 is undrawn until its tick. Mid-fight verbs would
  fight both the kernel and the premise. The middle's content is the smith's own hands
  (craft toward the vigil), staging (whose gear marches), and the one camp question.
- **Skipping stays legal, and its cost must be named, not engineered.** Hurry exists in
  every span; no decision carries a timer (the "living clock" was tried, owner-rejected, and
  stays dead). The cost of hurrying is meeting the vigil empty-handed — and the game should
  say so in copy at the moment of skipping.

---

## 5. The core systems

For each: what it does (as the code has it), why it exists, and what cutting it would cost —
the Sirlin deletion test the project applies to everything ("does the game get *worse*, or
just smaller?").

### 5.1 Crafting and quality

Four professions — Blacksmith, Alchemy, Tanning, Engineering — as data on one pipeline
(`sim/GameSim/Professions/`), ~39 recipes, five quality grades, an 8-node talent tree each,
and a modifier layer (quench oils, runes, fittings — slot-exclusive, integer effects on
decision thresholds, never flat stat bumps, `Crafting/CraftModifiers.cs`). **All four
professions now have interactive crafts wired end-to-end**: the forge's two acts, alchemy's
reagent-order brew, tanning's scrape frame, engineering's socket assembly — all four puzzle
types accepted and scored sim-side (`Crafting/CraftingHandlers.cs:92-120`, all four
`ActiveCraft: true`). Depth is honestly unequal: the forge is a real execution test; the
brew puzzle still renders its own answer key (a scored form, not a puzzle — flagged as
deferred tuning in its own file); scrape and assembly sit between.

Quality policy is the game's skill covenant: performance dominates, RNG is a ±25‰ whisper,
material grade caps the band, and Masterwork is reachable only through demonstrated skill
*on over-tier material* (both required — §2 link 1) or its priced late-game substitutes
(MasterworkAttempt, CommissionLegendaryWork — both CLI-only today, §8). Repeat
crafts reuse your proven trace at one click.

**Why it exists:** the player's half of the thesis. Quality must be aimable — a die roll
here would make "your craft" a lie. **Cost to remove:** there is no game. Everything
downstream consumes crafted, marked, graded items.

### 5.2 The heroes and their autonomy

A hero (`Contracts/Heroes.cs:39`) is a named, classed, leveled, permadeath individual with
gold, gear, a pack (front-first quaff order is part of the determinism contract), memories
of specific items' deeds, a signed mood toward your shop, and XP that really feeds combat
strength. Around that record, derivation-only systems make them legible people: **traits**
(10, in 5 opposing axes — Thrifty/Spendthrift, Discerning/Unfussy, Sentimental/Practical,
Patient/Stubborn, Prepared/Reckless — a pure function of id+name, never rolled, so the cast
is stable across campaigns; `Heroes/TraitDefinition.cs`), **needs** (an unmet-demand streak;
six ignored days and they favor the rival, telegraphed at four, bias never block —
`NeedsSystem.cs`), **relationship bands** with you (Stranger → Regular → Patron → Sworn,
driving commission quality floors and premiums), and hero-to-hero edges that feed gossip.
Six classes are recruitable (`Classes/ClassRegistry.cs:84-89`). Mood is influence only —
it never touches expedition dice (PKD7, `Heroes.cs:63-66`).

**What the autonomy actually is — said plainly, because this is where our own docs have
oversold.** Hero "autonomy" is five deterministic, RNG-free integer rules: what to buy
(`ShoppingAi.cs:110-187`), what price to tolerate (`WillingnessModel.cs:135-147`), whether
to take a bounty (`BountyRules.cs:73-109`), how deep to push and when to flee, drink, or
retreat (`ExpeditionResolver.cs:459-577`), and whether to restock heals
(`HeroShoppingSystem.cs:252-300`). There is no goal system, no plan, no inner life. Traits
are a **hash of `(HeroId, Name)`** — recomputed on every read, never rolled, never stored
(`TraitDefinition.cs:57-141`) — and their teeth are shop-side plus bounty greed only;
nothing in the raid reads a trait. Hero-to-hero relationship edges (bonds, grudges, grief)
are re-derived from the event log on every read and **nothing in the sim acts on them** —
they exist to be narrated (`RelationshipSystem.cs:65-81`); the same is true of the legends
and famous-dead queries. Bands and needs *do* act, but only on shop math (commission
floors/premiums; a +40% price-read bias for a boycotting hero). Where the game earns the
word "individual" honestly is the record underneath: each hero's memories of specific
items' deeds are real data from the attribution engine — actual counterfactual math over
recorded dice, not narration — and the five rules read them back (sentimental attachment,
veteran pickiness). Five legible rules over a true memory beat a hundred opaque ones. But a
design conversation that says "the heroes will decide" should know exactly how much
deciding there is — and any pitch copy that says "living heroes" is describing the
*derivation layer's prose*, not behavior.

**Why they exist this way:** autonomy is the premise. The moment a hero takes an order, the
game becomes a manager sim and the fantasy dies. **Cost to remove** any of the legibility
layers: the heroes regress to RNG timers — the exact failure of the genre this game was
built against. **What would make the autonomy claim truer than it is today:** rules that
read the relationship data they currently only narrate — that is the parked Erenshor
rivalry work (M5) and the behavior-level half of Phase B, both DESIGNED, neither built (§8).

### 5.3 The expedition and its staging

Covered as spine links 3–4. The load-bearing choices: **resolve-at-departure** (a raid is a
pure function of party, gear, floor, seed — no wall clock, no live dice), **two-stage
resolution** with the camp checkpoint as the player's one window, **structural floor gates**
(floor 5's gate sits above any rival loadout — player craft is *required* by arithmetic, not
encouragement), and **banded venue routing** with no draw. Death statistics are tuned so the
vigil window sits in front of 87% of the danger.

**What resolve-at-departure means for stakes — say it without flinching.** Death is decided
at the Expedition and Deep ticks and only *revealed* at Evening: while you are still living
the afternoon, `PendingExpeditions` may already contain the corpse. There is no rescue
window, and any surface that implied one would be lying — which is why none does. Between
days there is no attrition: working HP initializes to `MaxHp` at resolve time
(`ExpeditionResolver.cs:33`), so no wound survives the night. Heroes never leave the
roster — `HeroConsideringLeaving` is "a legible warning, never an automatic departure"
(`Contracts/Events.cs:276-280`). And the town cannot fail: confidence collapse latches one
event and changes nothing else (no era reset exists — U-D5 is unbuilt by decision), rent
and dues cap out, destitution is rescued every Morning. **The only irreversible things in
the entire simulation are hero deaths and the arc's forward day-stamps.** Permadeath
carries all of the game's stakes, alone. That is a deliberate, defensible design — one
sharp stake, honestly staged, beats five fake ones — but every proposal to "add tension"
should start from the fact that today there is exactly one, and every piece of copy that
implies the town itself is at risk is writing a check the sim does not cash.

**Why:** determinism buys the attribution proof (§6); the camp stage buys the one honest
mid-day decision. **Cost to remove staging:** the middle of the day loses its only real
question; recall and supply — the two largest measured levers, which point in measured
*opposite* directions (§9.9) — vanish together.

### 5.4 The economy

Faucets: sales (shelf, counter, commissions with premiums), bounty-driven clears feeding
loot, ore resale margins. Sinks: materials, the camp runner, rent (30g base, 10-day cadence,
escalating; misses cost modeled Confidence), Guild dues (7-day cadence, own escalation,
shared Confidence gauge with a telegraphed soft-fail — the Against-the-Storm-style pressure
heartbeat), and the late-game gold sinks (forge tiers, forge supplies, masterwork attempts,
legendary commissions) — **which are all four CLI-only today: no screen in the shipped
client can spend a coin on any of them** (§8, §9.10). Of the whole late-game sink program,
only the dues bite on screen. A rival vendor restocks daily and gains edge on your idle days
(`Economy/MarketShareSystem.cs`) — and say what the rival actually is, because "competitor"
oversells it: a deliberately static pressure gauge, six fixed Common catalog lines re-minted
each morning, never reacting to what you stock, never improving, discount bounded at 40%
(`RivalCatalog.cs`, `RivalRestockSystem.cs:27-30`). It prices your idleness; it does not
compete. Destitution has a guaranteed no-softlock recovery floor.
Hero wealth is deliberately separate from yours — loot flows to *their* purses; your income
is only what they choose to buy — which is why the BuyOre gift channel matters.

**Why:** scarcity is what makes the five slots a decision; the calendar pressure (rent,
dues) is what makes idling cost something. **Cost to remove:** measured 5–6× gold growth
over 15 skilled days with nowhere to spend already produced "money with no meaning" findings
in early audits; the sinks and heartbeats exist because their absence was felt first. Note
the uncomfortable corollary: since the four big sinks never reached the screen, the shipped
client is still living in the "money with no meaning" world those audits described — we
fixed it in the sim and told ourselves it was fixed in the game. One more measured economy
fact to keep in view: the healing economy is, at campaign scale, a trap (§9.9).

### 5.5 Commissions and bounties — the two ask-channels

Commissions are demand with a name and a deadline (§5.2, §2 link 2). Bounties are your only
influence over *where the world goes*: escrowed gold judged by each hero's legible D_q score
(`BountyRules.cs`). Both were once theater — bounties had 0 acceptances in 435 evaluations
at naive rewards, commissions never narrated their outcomes — and both now have real math
and real surfaces; the bounty board renders floor minimums so a rational reward is
discoverable (`Drama/DemandBoard.cs`).

**Why two channels:** one aims *what you make* (pull), one aims *what they do* (push), and
neither is an order. **Cost to remove:** the player's agency collapses to shelf-stocking;
the "influence, never orders" thesis loses its positive half.

### 5.6 Attribution and legends

The AttributionEngine (§2 link 4) plus every surface that consumes its truth: item history,
hero item-memories, gossip, the ledger, provenance cards, signed works, the memorial wall,
heirloom reforging, the famous-dead query, the chronicle, the arc's final tally. The one
piece of the original spec that was **never built as specced** is the Legend *Engine* — the
`sim/GameSim/Legends/` sifter/composer/selector module from the July roadmap. Its
user-visible payload arrived piecemeal by cheaper routes, and the standing recommendation
(unruled — §9.2) is to retire the module and keep the promise, testing "does a legend read
as a story you can retell, or as a log?" against a human before building machinery.

**Why:** this is the moat. Any competitor can build a shop sim; the provable
craft-to-outcome chain is the thing this architecture uniquely affords. **Cost to remove:**
the game becomes Recettear with worse art.

### 5.7 Drama, tone, and the arc

Event-sourced chronicle; gossip generated only from real events, capped at three lines a
day; four frozen narrator voices (gruff/dramatic/wry/omen) with ~1,470 authored lines — a
budget, not an endowment: the 07-25 audits listed gossip-template reuse among the suspected
inputs to the day-11 sag, and that suspicion has never been resolved; a
pacing director (BuildUp → Peak → Relax); a three-act campaign arc that climaxes at the
deepest floor and ends five days later, world staying open, Hades-style. Tone law
(`docs/design/tone-register.md`): deaths and wipes never joke — warmth yes, punchlines no;
comedy is deadpan, never zany.

**Why:** the town has to feel like it remembers, and the register is what keeps a death in a
game about watching people die from reading as either grimdark or slapstick. **Cost to
remove:** the proof layer (§5.6) stays true but stops being *told*; the game reads as a
spreadsheet with a graveyard.

**Identity note — adopted 2026-08-06 (§12, review C).** What is actually built is not
Stardew-cozy; it is **Spiritfarer-cozy: warmth *about loss***. Permadeath is the game's only
stake (§5.3), Prepared heroes die ~63–66% (`ConsumableTraitMortalityBalanceTests.cs:159-206`),
and the memorial → heirloom → lineage loop is the emotional center. That is a stronger and
more distinctive identity than generic cozy — own it deliberately in tone now and in any
eventual store copy: players arriving for Stardew will bounce off the graveyard, and the
players who would love this game will never find it under a "cozy sim" label.

### 5.8 The advisor and legibility

A pure suggestion engine (`Advisor/ObjectiveAdvisor.cs`) over a legality mirror that now
covers all 24 action types and *throws* on any future unmirrored action rather than silently
lying (`Advisor/ActionLegality.cs:29-77` — the old `_ => false` blind spot that hid nine
verbs from every surface is structurally dead). Legibility is a design law here, not polish:
typed rejection reasons on every verb, pass reasons on every shopping verdict, forecasts
that byte-match the tick they predict.

**Why:** an autonomous world the player can't read is indistinguishable from randomness.
**Cost to remove:** measured directly — machine playtests where the advisor couldn't see
half the verbs produced drivers that never found the game's best moves. (Machine drivers,
not people — the distinction §3.2 now owns applies to every "playtest" in this document.)

### 5.9 Progression and unlocks

Free talent trees per profession (tier gates, quality nudges); forge tiers as gold sinks;
hero XP/levels; relationship bands; venue and class go-lives as deliberate,
determinism-gated content flips (`built-inert` until flipped — Emberfall is staged and
waiting, §9.1); the arc as the campaign's clock. **Deliberately absent:** demand-gated
profession unlocks (professions opening because the town starts needing them — the
five-pillars Wave-4 design) remain designed-not-built (§8).

### 5.10 Persistence

A byte-deterministic save codec, wired to the client with an envelope, autosave, and
degrade-to-no-save failure paths (`godot/scripts/CampaignSave.cs`). A campaign survives
closing the window. (The 07-29 state-of-the-game's "nothing survives a restart" is fixed
and stale.)

---

## 6. Why it is built this way

The architecture rules in `CLAUDE.md` read like engineering dogma. They are actually design
decisions, and the game's signature features are downstream of them.

**The pure, deterministic sim with an adapter-only client.** All rules live in
`sim/GameSim/` with zero engine references; Godot renders state and submits actions, nothing
more. Same seed + same actions = byte-identical world, enforced by a golden-replay test that
fails the build. No RNG outside the kernel's injected stream; no wall-clock reads; no
transcendental math (cross-OS float drift would silently fork worlds).

What this *buys the game*, in design terms:

1. **The attribution proof is only possible here.** The counterfactual replay (§5.6) re-runs
   recorded rolls with an item removed. In a nondeterministic engine that comparison is
   meaningless — "would they have died without your sword?" only has a true answer if the
   fight is a pure function. The moat is a corollary of the purity rule.
2. **Honest staging.** Because outcomes are computed at departure and *revealed* on a
   schedule, the presentation layer can stage tension (the vigil, the held breath of Deep
   Vigil, the Night reveal) without ever lying about what is decided when. The two-bell
   day's conductor is only trustworthy because ticking a phase early or late cannot change
   its result.
3. **Playtesting at machine scale.** Twenty-seed hundred-day telemetry farms, a balance gate
   in CI, instrumented personas playing the real decision surface, and every finding
   reproducible as seed + transcript. Nearly every design correction in this document's
   sources came from that instrument.
4. **Fearless iteration on the client.** The render layer was thrown away twice (3D → 2.5D
   pixel art) without touching a single game rule. That trade is only available while the
   sim stays pure.

**The prices paid, named honestly:** content go-lives are determinism events (appending to
the recruit pool or venue rotation shifts every downstream draw — hence "built-inert" as a
first-class status and re-baselines as deliberate ceremonies); the balance gate makes big
rebalances slow by design; and resolve-at-departure means the middle of the day can never be
filled with reactive intervention — the design must find its drama in preparation and
witness. That last price is exactly the current design battleground (§4), and the ruling on
record is that it is a price worth paying: heroes who can be steered mid-fight are units,
and this game is not an RTS.

**Influence, never orders (PKD7)** is the same commitment expressed as a game rule: mood
never touches resolution; bounties are weighed, not obeyed; nothing the player does commands
a hero. Every proposed feature gets tested against it, and its most common failure mode has
a name in the docs — *bounty theater*: verbs that occupy hands without touching an outcome.

**Sim-purity as a collaboration contract.** One more honest reason: this game is built by
many agents in parallel. A pure core with typed contracts and a build-failing replay gate is
what lets a swarm work on it without silently breaking each other — the architecture is also
the project's social contract.

---

## 7. Inspirations — what was taken, what was refused

| Source | What we took | What we did with it / refused |
|---|---|---|
| **Weapon Shop de Omasse** (3DS) | The whole genre seed: rhythm-forging for heroes who quest offscreen; the "Grindcast" live feed | The ticker + watch surfaces are our Grindcast (`docs/plans/2026-07-21-005-watch-surfaces.md`). Refused: its lease-don't-sell economy and its purely comedic register — our deaths are real and never jokes. |
| **Recettear / Moonlighter** | The two-mode day (shop / dungeon), the customer who initiates, haggling as the face-to-face beat | Customer-initiates shipped as CustomerVoice; the two-mode day validated the two-bell structure. Refused: playing the dungeon half yourself — inverting that is the entire premise. |
| **Majesty** | Indirect control via bounty flags heroes weigh by personality | Taken nearly verbatim as the D_q score (`BountyRules.cs:21-24`). |
| **Against the Storm** | Escalating institutional pressure decoupled from a fixed calendar (the Blightstorm) | The Guild Assessment + Confidence gauge couples pressure to *your* progress signals (`World.cs:125-133`). Refused: full roguelite settlement resets (a "prestige era" remains an explicit post-v1 deferral). |
| **Erenshor** (single-player MMO with SimPlayers) | The proof that AI "players" with memory and opinion carry a world; five specific borrows ruled on 2026-07-19: memory-anchored gossip, reputation-gated shopping, picky veterans, attribution-clear death causality, rivalry | Gossip-from-real-beats, the veteran quality gate, and haggle willingness bands are shipped mechanics in this space; rivalry (M5) is deliberately parked pending its own plan; death-cause typing (M4) remains designed-not-built. The docs honestly note the M1–M5 labels no longer map 1:1 onto what shipped. |
| **Stardew Valley** | The day as a time-allocation puzzle that never gates on reflexes; the 2.5D top-down town read | The pixel-art pivot (#244–#249) and the no-timers law. Refused: the real-time day clock — tried as the "living clock," rejected by the owner, retired permanently. |
| **Potion Craft / Dungeon Bodega Simulator** | Customer-states-want; the no-clock day ("the day advances when the player chooses"); DBS's post-mortem lesson that in a short game every major system needs a mandatory story touchpoint or it decays into decoration | The bell-not-clock model; the touchpoint lesson is effectively the Completeness Bar's "not-padding" test (`DB_GAMEPLAY_LOOP.md` §4). |
| **Football Manager / autobattlers / Blaseball** | That watching a resolved simulation can carry drama if revealed as highlights with stakes | The staged-reveal research behind the conductor and the Mirror's delve show. |
| **Hades / Vagrant Story** | Slot-exclusive build composition (one modifier per family); the ending that leaves the world open | `ModifierFamily` (`Items.cs:13`); `CampaignAct.Ended` keeps playing (`Enums.cs:113`). |
| **CK3 / RimWorld (negative lesson)** | Trait axes read by multiple systems; and RimWorld's wealth-spiral as the thing to avoid | Traits feed shop and raid teeth from one table. The drama director's escalation deliberately keys on progression tier, never shop wealth. |
| **Graveyard Keeper** | The deadpan register for a game about death and commerce | The tone law (§5.7). Refused: its cynicism — our town is warm. |

Refused outright, on principle, with the reasoning on record: **runtime LLMs in the sim**
(deterministic classic AI only — an LLM in the loop breaks replay, cost, and testability);
**mid-delve intervention of any kind**; **outcome wagers** at the stakes slate (manufactured
investment the causal chain should earn); **timers on decisions**; and the **full Zubek
five-need utility engine** for heroes (needs-lite shipped instead; the rejection is recorded
so it doesn't get re-planned by a fresh session).

---

## 8. What is built, what is designed, what is wished for

The project's own registries (`docs/registry/SYSTEMS.md`, `CONTENT.md`) carry the row-level
detail and their own drift warnings. This is the load-bearing summary, verified against code
on this commit and collided with the source-only control pass (now Appendix A). **A reader should trust these
buckets over any older document, including the registries where named.** The distinction
that keeps biting is BUILT vs **BUILT, CLI-ONLY**: repeatedly, "implemented and tested"
has been reported as "shipped" when no screen could reach it. Under the project's own rule —
DEPLOYED means the Godot client, not the CLI — CLI-only is *not shipped*, and this table
never blurs the two.

### The ledger, at a glance

| Piece | Status | Verify at |
|---|---|---|
| Five-phase deterministic kernel; golden replay + 100-day balance gates; save/load + autosave | **BUILT** | `GameKernel.cs:188-198`; `godot/scripts/CampaignSave.cs` |
| 24 player actions, typed rejections everywhere | **BUILT** — but only 20 reachable on screen | `Contracts/Actions.cs:10-35`; Appendix A §1 has the per-action table |
| The two-bell day: conducted middle, untimed vigil stop, camp verbs, craft-and-send round trip | **BUILT** (#388, #392) | `godot/scripts/RaidConductor.cs`, `panels/CampPanel.cs` |
| Camp-phase *systems* — anything the vigil actually simulates | **NOT BUILT** — zero registered systems run in Camp | `GameComposition.cs:57-77` |
| Four professions, four interactive crafts, all `ActiveCraft: true` | **BUILT** | `CraftingHandlers.cs:92-120` |
| Forge two acts, session skill, tier-narrowed quench, "forge another like it" | **BUILT** | `ForgeMinigame.cs`, `QuenchMinigame.cs:61-63` |
| Counter: speaking customer, willingness/haggle, goodwill memory | **BUILT** | `CustomerVoice.cs:38`, `HaggleResolver.cs` |
| Commissions across Weapon/Shield/Armor/Consumable/Trinket | **BUILT** | `CommissionSystem.cs:251-288` |
| Heroes: six classes, traits, needs boycotts, bands, XP/level flip, permadeath, memorials, heirlooms, signed works | **BUILT** | `ClassRegistry.cs:84-89`, `TraitDefinition.cs` |
| Hero *behavior* that reads relationships/rivalry (Phase B behavior level; Erenshor M5) | **DESIGNED** | §5.2 — today the edges narrate, never act |
| Attribution beats end to end, plus every reader: ticker, ledger, gossip, provenance, legends wall, chronicle | **BUILT** | `AttributionEngine.cs:19-145` |
| Three venues, banded draw-free routing, per-venue ore ladders (14 of 21 materials priced) | **BUILT** | `VenueRegistry.cs:62-66` |
| Emberfall Foundry | **BUILT-INERT** — mechanics done and banded; **no committed art at all**; flip blocked by an art-presence gate test | `VenueRegistry.cs:51-60`; draft PR #346 (§9.1) |
| Economy heartbeats: rent, Guild assessment + Confidence, rival share, destitution floor, bounty D_q + board minimums | **BUILT** | `RentSystem.cs`, `GuildAssessmentSystem.cs`, `BountyRules.cs` |
| The four endgame gold sinks: UpgradeForge, BuyForgeSupply, MasterworkAttempt, CommissionLegendaryWork | **BUILT — and SHIPPED 2026-08-07** (wave U3/U4, R2 ruled build). All four now have buttons; all 24 actions are reachable. *Corrected: this row previously said "3 of 4 have bell-tray strings waiting" — it was **2 of 4**. The third `PendingVerbVocab` entry is `SetProfessions`, not a sink, and the other two sinks resolve immediately so they never needed a tray entry — which is precisely why nothing flagged them.* | `godot/scripts/panels/ForgePanel.cs` (Foundry section); `godot/tests/ActionReachabilityCensusTests.cs` |
| Three-act arc: act flips, ending screen (world stays open) | **BUILT** — the ending renders *when it fires*; reachability is unasserted (defect below) and unconfirmed on a real screen | `ArcDirectorSystem.cs`; `panels/ChronicleScroll.cs` |
| The climax's *content* (Final Commission / Warden of the Heart) | **DESIGNED** — `ClimaxReached` fires as a bare seam, by its own admission | `Contracts/Events.cs:293-297`; §9.7 |
| Title/system menus, tutorial, audio pass one, machine playtest harness | **BUILT** | |
| Night leads with the mark (reveal ordering — beats first, sale-and-deed grouped) | **DESIGNED** (loop-plan U5/H3) — cheapest unshipped piece of the answer half | |
| Send-off slate (H4) | **SHIPPED 2026-08-07** (wave U2). *Correction: this row was stale — `MineWatch.RumoredLines` and `JourneyStream.DepartureLine` already rendered "X carries your Y" at departure, so the headline passed at HEAD with zero code. What was actually owed, and is now done: the manifest was capped at 2 lines (a party of three each carrying your work silently dropped one), it was buried in a scrolling strip rather than staged as a moment, and it had no honest empty state.* | `godot/scripts/panels/MineWatch.cs` |
| Deep-stakes slate (H5), vigil hero-chips (V-3) | **DESIGNED** (hero-facing-day) — H5 still behind P5/R1, V-3 still behind R1 | |
| Tavern's two acts (commission handshake AM / ore handshake PM) | **DESIGNED — in flight** as PR #393 at this writing | |
| Building-minigames wave (alchemy Draw, tanning Dip, engineering act split) | **DESIGNED** — sequenced after the loop work by its own doc | |
| Demand-hazard engine, demand-gated profession unlocks, Master Voss, Ledger-of-Legends screen | **DESIGNED** (five-pillars Waves 2/4/5) — not started | §9.4 |
| Erenshor M4 (death-cause typing), M5 (rivalry) | **DESIGNED — parked** | |
| Registry manifest enforcement (the ledgers' teeth) | **DESIGNED** — and the ledgers have drifted twice for its absence | |
| Prestige era / soft-fail reset (U-D5) | **WISHED-FOR** — soft-fail latches one event; nothing follows | `GuildAssessmentSystem.cs:25-27` |
| Casters/companions/healer; enchanter/food/husbandry; venue fatigue; monster variants; disasters; vanity economy; fan letters; scrolls; music generation | **WISHED-FOR** — no code, no plan of record; do not assume | |
| The full Legend Engine module (sifter/composer/selector) | **WISHED-FOR / UNRULED** — recommendation on record is retire-the-module, keep-the-promise | §9.2 |

### Corrections to our own documents — do not re-import these errors

Stale claims still sitting in older docs, each one now false in code:

- `docs/registry/SYSTEMS.md` says 2 of 4 professions are wired with `ActiveCraft: false` —
  **stale**: all four are wired and flagged true (`CraftingHandlers.cs:92-120`).
- The 07-27 audits' "consumables and trinkets can never be commissioned" root-cause finding —
  **obsolete**: `CommissionSystem.cs:251-288` commissions both (trinkets for Regulars and up).
- The 07-29 state-of-the-game headline table — **stale on three rows**: classes ("3 of 6" →
  six recruitable), venues ("2 of 4" → three live), and save/load ("nothing survives a
  restart" → full save/autosave shipped).
- Any doc describing the Phase-D economy as shipped — **wrong for the client**: sim-complete,
  CLI-only (§9.10).
- Any doc calling Emberfall "art complete" — **wrong**: no committed art at all
  (`VenueRegistry.cs:51-60`; PR #346 touches no art files).

### Fixed 2026-08-07 — the defect this document missed entirely

- **The Evening ledger quoted one price and the kernel charged another.** `LedgerModal.cs:211`
  printed the hero's base ask while `OreMarketHandlers` charged the standing-tariffed cost; the
  tariffed number existed client-side only inside the disabled-button affordability gate and never
  reached a label. Whenever faction standing was non-zero, the number on screen and the number
  charged differed. Benign only because standing is positive-only (KTD8) so the surprise was always
  a discount — but a reveal that lies is §10.5's interrupt class, and it sat on the flagship Night
  surface. It also meant **the game's only faction lever had been live and unplayable**: buying a
  faction's ore is the sole thing that raises standing, standing is the sole thing that moves the
  price, and the player could see neither. Fixed in wave U1 (line total, faction named) with U5
  surfacing the cause and the state. The aggregate-vs-per-unit rounding trap is pinned by test.

### Known defects and drift on this commit — recorded, deliberately not fixed in this PR

- **`ActionBudget.ConsumesSlot` is fiction.** The predicate names four slot-consuming action
  types and its test pins "exactly the four" (`Contracts/ActionBudget.cs:26-27`,
  `ActionBudgetTests.cs:18-31`) — but **nine** handlers actually decrement the counter
  (grep `ActionSlotsRemaining - 1`), and no runtime code calls the predicate at all
  (`MarketShareSystem` reads the counter directly). Any surface built on it will
  under-report what spends a slot. One cleanup PR; the test must be rewritten, not appeased.
- **A worn trinket can be sold twice.** The stock check rejects shelving gear a hero wears —
  but only checks Weapon/Shield/Armor and omits Trinket (`Economy/ShopHandlers.cs:59-66`),
  while trinket recipes exist and `GearSet` has the slot. A player-crafted trinket a hero is
  wearing can be re-shelved and sold to a second hero while the first still wears it.
  Compare `HeirloomHandlers.WoreItem`, which includes Trinket. Likely real defect.
- **The balance gate cannot see a missing finale.** Every floor-5/Act-III/Ending assertion
  is a one-sided trivialization ceiling: `FirstFloor5Day` initializes to `int.MaxValue` and
  is only asserted `>= 8` (`BalanceSimTests.cs:45,86-87`), and `ArcBalanceTests.cs:43-52`
  wraps its Act-III and Ending checks in `if (> 0)` — a campaign that never climaxes and
  never ends passes CI green. The one full-length client recording (90 days, 2026-07-27)
  never left Act II; nothing since has re-confirmed Act III on an actual screen. The
  campaign's finale — the arc's whole payoff — is protected by no test. (§11 P3.)
- **Stale comments that lie to readers**: `ProfessionHandlers.cs:16-18` claims the handler
  "is NOT yet wired" (it is — `GameComposition.cs:84`); `CraftingHandlers.cs:8-9` and
  `ShopHandlers.cs:10-11` say "ALL THREE phases" (the day has five); `Actions.cs:149` calls
  HonorMemorial "Evening/Night-legal" (no Night phase exists — it is Evening-only).
- **Vestigial**: `Bounty.Paid` is never set true (payout removes the bounty instead);
  `BeatType.ToolAssist` has no emitter (deliberate contract-ahead-of-content); talent
  points cost nothing ("talent-point economy deferred" — `CraftingHandlers.cs:332`).

Appendix A §7 carries the full dead-and-vestigial list with line cites.

---

## 9. The known tensions and open questions — for the owner to rule on

Each phrased so a one-line answer unblocks work. The first three are the ones sitting
longest without a ruling. The plan of record (§11) sequences the work these rulings gate,
states a default for each, and gathers the ones that touch its critical path into its
Phase 0 (R1–R6) — ruling there rules here. Four of these questions were also ruled on by
the external reviews; where a review's argument was adopted, the question below carries an
"amended" block naming it, and §12 carries the full verdict table.

### 9.1 The Emberfall flip (draft PR #346 — sitting as a draft since 08-02, four days)

Emberfall Foundry's *mechanics* are finished — built, banded, comparator-tested. Two things
block a one-line merge, and earlier discussion only ever named the first:

1. **The routing point.** Flipping it live at its original EntryPower 72 tie with Gloomwood
   **collapses Gloomwood from ~64% of all routed parties to ~19%**, handing Emberfall ~41%
   (20-seed × 100-day sweep; the PR body carries the full table — the "61%→18%" figures
   quoted in earlier discussion are the same measurement from a pre-rebase snapshot). Agents
   implemented option B on the branch — EntryPower 79 — which lands Gloomwood at 50.5% and
   Emberfall at 14.6%, and pulls deaths/trade volume back near pre-flip. The material ladder
   (Gloomwood mints grade 8–11 ore, Emberfall 12–16) says strictly-later was the design
   intent.
2. **The art does not exist.** Contrary to what earlier planning notes implied, Emberfall
   has **no committed art at all** — no backdrop, no monster portraits
   (`VenueRegistry.cs:51-60`), and PR #346 touches only sim, tests, and docs. The flip is
   deliberately gated by `VenueBackdropArt_Present_RendersRealArt_NotFallback`, which fails
   on any live venue that renders placeholder — the registry comment exists precisely
   because "finished-looking content that renders as placeholder" has burned this project
   before.

**Question: rule the EntryPower (79 is the implemented recommendation; the curve is measured
at 72/76/78/79/80) *and* either commission the art wave that unblocks the gate, or park #346
explicitly with a note — anything but letting a draft imply the venue is one click away.**

### 9.2 Is the Legend Engine still owed?

The specced `sim/GameSim/Legends/` module (sifter, 8 story shapes, composer, selector) was
never built; its payload shipped piecemeal (attribution + LegendQuery + memorials + signing
+ heirlooms). The recommendation on record since 2026-07-28: **retire the module, keep the
promise**, and let the human feel-test decide — if legends read as logs rather than stories,
the fix is a composer/selector pass over existing data, not the sifter. This is a ruling on
the thing the roadmap called the moat, so it wants an explicit yes/no rather than the
current silence. **Question: retire the specced module (recommended), or keep it on the
books?**

### 9.3 The forge difficulty question (the quench window and the beginner)

The two-act forge measured skilled ~9.7s — under target — but **beginner ~19.5s, virtually
unchanged from the meter it replaced, and the beginner is exactly who complains**. The
obvious knob (global tempo period) was tried and measurably made skilled play *worse* (an
aliasing interaction, documented in PR #381), so the remaining lever is a design choice, not
a constant: most likely a low-accuracy assist on shape-per-strike (bigger progress per
strike when demonstrated accuracy is low — the inverse of the current session-skill rule),
or accepting the beginner duration as the cost of a real skill gap. Related second knob:
whether the tier-narrowed quench band (140/100/70‰) is the right difficulty axis or should
narrow further at the top end. **Question: should a beginner's craft get faster (assist), or
is ~20 seconds an acceptable apprenticeship?**

**Amended 2026-08-06 (external-review lap, §12).** This question now carries a recorded
default: **the assist** — inverse session-skill (bigger shape-per-strike while demonstrated
accuracy is low), which preserves the covenant exactly as the current session-skill rule
does: labor shrinks, the accuracy target never widens. Review C's argument tipped a
long-open question into a default: ~19.5s with the beginner being exactly who complains is
a first-session killer, and first sessions decide whether anyone but the owner ever plays
this. P4 confirms the default rather than reopening the question; the build itself stays in
the cut list's tuning bucket until then.

### 9.4 The demand map — how much of the five-pillars program still stands?

The code has already absorbed the sharpest finding (consumable/trinket commissions exist;
veterans demand quality). What remains unbuilt is the program's keystone: **typed demand
beyond gear slots** — hazards that make heroes need what Tanners and Engineers make, and
professions that unlock when the world starts asking. Until then, measured demand still
skews heavily toward what the Blacksmith sells, and two professions have the newest craft
surfaces in the game with the least reason to use them. **Question: does the demand-hazard
engine (five-pillars Wave 2) stay the next big sim program after the loop wave, or is it
superseded by something in this document's margins?**

### 9.5 The counter vs the atomic pass

The counter is the game's best face-to-face moment and, measured, a worse income channel
than never opening it (the invisible atomic pass out-earned it ~10× in instrumented runs).
The customer speaking first fixed its legibility; its *economics* were deliberately left as
a post-measurement question. One input the ruling should know: within a round, a
counter-offer can never lose the sale (§3.1, `HaggleResolver.cs:124-159`) — the counter's
skill expression today is entirely about mood and goodwill margins, not closing risk, so
"make personal service the premium channel" and "make haggling actually risky" are separate
knobs. **Question: should serving the counter out-earn passive
shelving (making personal service the premium channel), or stay a flavor-equal alternative?
This decides whether we tune willingness/premiums or leave them.**

**Amended 2026-08-06 (external-review lap, §12).** The direction is now ruled by the newly
adopted filter law (§10, test 8 — hand-work must beat passive systems, or the passive system
must not exist): **personal service becomes the premium channel** — better pins, higher
willingness, goodwill compounding into commission premiums. Two consequences, kept separate:
(1) the *tuning wave* is sim-side (willingness/premium math, likely re-baseline) and is
sequenced by R5's queue — it does not displace P1–P6; (2) the *honesty gap* is booked as a
rider now (§11.4): the UI implies "push too hard and lose them" and the resolver says a
counter-offer cannot lose — either the copy tells the truth, or the tuning wave adds a real
walk risk. Staged tension the sim doesn't cash is this project's own named failure class.

### 9.6 The middle of the day — confirm the build order

The hero-facing-day document (§4) is the loop authority, its rule is agreed, and its first
waves are landing (#388, #392, #393). Its ordering asks for: vigil polish (V-3) → night
leads with the mark (U5/H3) → send-off slate (H4) → stakes slate (H5) → morning aim surfaces
(H6) → survivor's handshake (H7) — before any further minigame or content work. **Question:
bless this order (or reorder it), so the next sessions stop re-litigating it.** The plan
(§11) adopts this order with one argued amendment: V-3 moves *behind* the §9.9 ruling,
because #392 already built vigil presentation on the exact signal §9.9 questions, and
polishing that surface further before the ruling deepens the potential rework.

### 9.7 The ending, and what "done" looks like

A three-act arc with a climax and an ending exists and fires. Nobody has yet answered
whether it *lands* — that is a feel question. The old plans' richer climax (a Final
Commission forged for a chosen hero, watched at the Heart) exists only as prose. **Question:
after the feel-test, is the shipped ending the v1 ending, or is the Final Commission beat
owed before we call the campaign complete?**

**Amended 2026-08-06 (external-review lap, §12).** Default flipped: **the Final Commission
is owed** (new ruling R6, §11.3). Review C's argument, adopted: the game's thesis is "your
craft writes the legends," and the current climax is a threshold event with a tally
(`ArcDirectorSystem.cs:57-110`; `ClimaxReached` is a bare seam by its own admission,
`Contracts/Events.cs:293-297`) — the ending that pays off the premise is forging one named
piece for one named hero to carry to the Heart. Not before the loop wave; but the campaign
is not "complete" without it, and ruling it now stops later re-litigation. P4 can waive it
only with an unexpectedly strong verdict on the shipped tally-ending.

### 9.8 The human feel-test itself

Still the standing gate in front of several rulings above (9.2, 9.7 explicitly), still not
done: `play.ps1`, a real evening, answering five written questions — do four crafts feel
like four skills; can you name three heroes by personality; does a legend read as a story;
does the ending land; on which day does it get boring. Machine playtests have taken this as
far as they can. **This is not a question so much as the one item only the owner can clear.**

### 9.9 The provisioning irony — is the trap the point?

Both of these are true, both are measured, and both are pinned in the balance suite:

- **Individually, provisioning saves lives, provably.** The attribution engine proves
  `Provisioned` and `PotionLifesave` beats from recorded dice — a delivered salve's
  end-to-end save is demonstrated in a marquee test with zero attribution edits. When the
  ledger says your potion saved Torvald, that is a theorem.
- **In aggregate, provisioning kills.** Blanket salve-stocking raises total party mortality
  **~+35% unstaged / ~+59% staged** across an 11-seed sweep
  (`SalveProvisioningBalanceTests.cs:126-157`), and the camp send verb itself measured
  **+29 deaths and −12 target-reached** over 20 seeds × 100 days
  (`CampProvisioningBalanceTests.cs:23-37`). The mechanism is emergent risk compensation —
  a topped-up hero pushes one floor deeper than they would have dared, and deeper floors
  kill (87% of deaths are below the camp window). Both test files document this in-source
  as "emergent, not a bug."

Meanwhile PR #392 shipped the vigil as *forge the answer, save the hero* — the game's UI
now actively teaches the behavior the balance suite says costs lives at scale. The camp
test's own comment saw this coming: *"retune BEFORE U5 builds presentation on a signal that
currently costs lives at scale."* The presentation is now built; the retune never happened.

Three honest resolutions, each one line:
(a) **Intentional tragedy — keep it, and eventually tell the player.** A game about
influence without control could not ask for a truer theme: your kindness emboldens them,
and boldness is what kills. Gossip and the chronicle could surface it honestly ("the
well-supplied dare too much"). (b) **Balance defect — retune** the knob set (checkpoint
depth / send threshold / fee / the too-hurt quaff rule) until the verb points the way the
fiction says it does. (c) **Split the difference** — keep individual saves as-is, damp only
the risk-compensation push (e.g. the post-floor quaff no longer resets the too-hurt check).

**Question: is the irony intentional (and worth surfacing to the player), or a defect to
retune? One line decides whether the vigil's flagship verb is a tragedy or a bug.**

**Amended 2026-08-06 (external-review lap, §12).** The fork stays the owner's, but it now
carries a recorded default: **(c)**. The argument that tipped a refused-default into a
recommendation, from review C and adopted here: *a tragedy requires a tradeoff.* If
provisioning bought depth at the cost of blood, "your kindness emboldens them" would be a
beautiful theme — but the measurement says supply loses on **both** axes (+29 deaths *and*
−12 target-reached). That is not tragic irony; it is a dominated verb, and the project's own
theater law makes a verb that changes outcomes for the worse strictly worse than theater
(§10, test 8). Option (a) remains available, but it now has to argue against both the camp
test's own in-source comment and an independent reviewer who read the same numbers. Under
(c), individual saves stay provable, only the risk-compensation push is damped, and the
sweep re-runs until supply trades depth against survival — a real dilemma with two goods;
*then* "the well-supplied dare too much" can go into gossip honestly.

### 9.10 The unreachable endgame — do the four sinks get screens, or get cut from v1?

`UpgradeForgeAction`, `BuyForgeSupplyAction`, `MasterworkAttemptAction`, and
`CommissionLegendaryWorkAction` are implemented, tested, balance-integrated, and reachable
only from the console runner — no screen in the shipped client can construct any of them,
though three already have bell-tray strings registered and waiting
(`PendingVerbVocab.cs:31-43`). The plumbing shipped ahead of the buttons, and the buttons
never landed. Consequence: the entire late-game economy loop — the answer to "money with no
meaning" — does not exist where anyone plays, and §9.6's blessed build order does not
mention it. **Question: slot "Phase-D surfaces" into the §9.6 order (and where), or
explicitly defer the endgame economy past v1? Either is a fine answer; today it is silently
neither.**

---

## 10. What "focus" means from here — the filter

The owner's complaint, verbatim: *"very consistently we're doing bug fixes that are only
relevant to particular pieces and ends up almost straying further from the gold of the
game."* The gold is §2 — the chain from your hands to their story. So here is the test, in
the order to apply it, for any proposed unit of work — feature, fix, or content:

**1. The chain test.** Point to the link in §2 this work strengthens. Strengthening means
one of exactly three things:
   - it lets the player *cause* more (a new honest lever on a hero's outcome),
   - it makes an existing cause **visible where the player is standing** (legibility,
     staging, ordering — the current wave), or
   - it protects the substrate the proof depends on (determinism, purity, tests).
   If you cannot name the link, the work is sideways. Park it.

**2. The ledger-line test** (from the hero-facing-day doc, and the sharpest single tool we
have): *point to the line in the Night ledger that is different because the player did this.*
Verbs that occupy hands without touching an outcome are theater — the failure class that has
been caught and cut four separate times (bounty theater, watch-to-buff, gate blessings,
outcome wagers). New surfaces must answer it; new verbs must answer it before they are built.

**3. The premise test.** Does it order a hero, time a decision, or pay out in mood alone?
Then it is not a feature of this game, however fun it sounds. (PKD7; the no-timer law; the
mood-is-influence-only law.)

**4. The honesty test.** Does it show something the sim hasn't decided, or hide something it
has? The middle of the day is undrawn until its tick — surfaces there must stage *stakes*,
never fake *events*. Empty states tick through honestly; costs of skipping are named in
copy, not engineered as punishment.

**5. For bug fixes specifically — the triage rule.** A bug is core-lane work if it breaks a
§2 link (an attribution beat wrong, a craft mis-scored, a reveal that lies, a determinism
break — fix now, before features). Everything else queues behind the current wave and ships
*with* related work in its area, not as interrupt-driven singletons. Three playtest rounds
of scattered fixes did not move the owner's core complaint; the structural waves (#358,
#388) did. That is the empirical case for this rule.

**6. The measurement norm.** Tuning claims come with a sweep, not an adjective (the
EntryPower table, the strike-floor search, the 87.1% camp statistic are the house style).
And playtest findings get checked against the driver before they are believed — six of six
recent "game bugs" were harness bugs.

**7. The deployment clause.** Nothing counts as *done* until the ledger line is producible
in the Godot client by a player at a keyboard. Sim-complete, test-green, CLI-proven is
*ready*, not done — the four Phase-D sinks (§9.10) are the standing monument to what
happens when this clause is skipped: an entire endgame economy reported as built that no
player has ever touched.

**8. The dominance test** *(adopted 2026-08-06 from external review C — §12)*. For any
human verb that has a passive alternative: **hand-work must beat the passive system, or the
passive system must not exist.** This extends test 2 downward: a verb that occupies the
hands without changing an outcome is theater (banned); a verb that changes outcomes *for the
worse* is strictly worse than theater. The review's contribution was to name what our own
documents circled as two separate tuning questions — the counter out-earned ~10× by the
invisible atomic pass (§9.5) and the camp supply run measuring +29 deaths / −12
target-reached (§9.9) — as **one systemic defect: the game teaching players to skip its own
humanity.** Any new human verb answers this test at design time, against the passive
baseline, with a measurement.

### The filter, applied — three real pieces from the last two weeks

This is the calibration that keeps the filter from being a platitude. Applied to three
merged PRs, most recent work first:

- **PR #392 — the vigil states the stakes; craft-and-send made discoverable. PASSES.**
  Chain test: link 2 (the camp runner) made visible where the player stands; ledger line:
  the delivery and its provable `Provisioned`/`PotionLifesave` beat, with "of which yours:
  N" on the slate. It passes — *and* it is the case that shows the filter is not a rubber
  stamp: the balance suite had already measured the celebrated signal pointing the wrong
  way at campaign scale, and the camp test's comment explicitly asked for a retune before
  presentation was built on it (§9.9). The honesty test therefore attaches a condition:
  #392 is finished work that *raises* §9.9's priority rather than settling it. A filter-run
  before building it would have forced the §9.9 ruling first — which is exactly the kind of
  sequencing this document exists to buy.
- **PR #385 — the forge result ceremony ate clicks it never drew over. PASSES,
  interrupt-class.** A §2 link-1 break at the player's own hands: the craft cause existed
  and a surface defect hid the response. Rule 5 says a broken spine link is core-lane,
  fix-now work, and it was correctly treated as such.
- **PR #390 — retire Git LFS. REJECTED.** No hero whose outcome changes, no ledger line, no
  game invariant protected — repo plumbing. It was still worth doing (it removed a real
  operational papercut), and the filter does not forbid it; the filter *books* it. It
  counts as overhead, it may never displace a §2 unit from a work session, and a week whose
  merged list is mostly this class is a week the game did not move — whatever the commit
  count says. This is precisely the "bug fixes only relevant to particular pieces" drift
  the owner named, and the filter's job is to make that visible at the moment of choosing
  the work, not in retrospect.

A one-line summary suitable for pinning above the board:

> **If you can't name the hero whose outcome changes, the ledger line that proves it, or the
> invariant that protects it — it's not the game, it's around the game.**

---

## 11. The plan of record — from the gold outward

Everything above diagnoses; this section decides. It is the answer to the owner's actual
request — *"work this in the core goal so that when we are developing there's a lot more
focus on things"* — and it is written to be argued with: every item carries its reason, its
size, what blocks it, and its §10 filter line; every ordering tie is named with its
tiebreak; and the cut list is as much the point as the path. A session that wants to do
something not on this path must either amend this section (a visible diff) or book the work
as overhead. There is no third option.

**Sizing units**, the ones this project actually works in:
- **a ruling** — one line from the owner; costs nothing, unblocks the most.
- **a session** — one agent session, one branch, one small PR.
- **a wave** — several coordinated sessions/PRs under one design doc.
- **an evening** — the owner at the keyboard; cannot be delegated.
- **a re-baseline** — the golden re-record ceremony; expensive, serialized, never implicit.
  Anything touching `sim/GameSim/Contracts/` additionally requires an orchestrator-authored
  micro-PR (deny-list rule). Both flags are marked below wherever they apply.

### 11.1 The gold, named

**A specific person's fate provably turned on work your hands did — and you were watching
when it happened.** Craft → carry → proof → witness. That is the one thing this game is for;
it is the only thing on this game's shelf that no other game offers (§5.6: any competitor
can build a shop sim; the counterfactual proof chain is unique to this architecture).

Two candidates were considered and rejected as the gold, for the record. *The economy* is
the feeder, not the gold — the audits' "money with no meaning" era proved gold accumulates
happily while the game fails. *Hero autonomy* is the amplifier, not the gold — §5.2 is
honest that the autonomy is five arithmetic rules, and the game moves us anyway, because the
proof layer is what makes those five rules feel like a person. The spine (§2) stands as
written, with one sharpening: the five links are not equally weak. All five are built and
true as *mechanisms* (§2's trace). The game's entire current deficit sits in two places:
the **witnessing** — the staging of links 3–5, where the march is anonymous and the proof
arrives as a Night flood (§2's own caveat) — and the **campaign's far end** — measured
question-shortage at day 11, an unprotected finale, an unreachable endgame economy. The
plan orders by exactly that: first make the existing proof *land*, then verify it lands on
a *human*, then fix the far end.

### 11.2 Where the gold stands, in four sentences

The cause side (Morning) works, by machine measurement. The proof side (the attribution
engine) works, by test. The witnessing is half-staged — the send-off doesn't name your work,
and the Night reveal buries its own headline — and **no human being has ever reported
feeling any of it** (§3.2). Past day 11 the game runs out of questions, past day 30 gold
runs out of meaning on screen, and nothing in CI proves a campaign can end.

### 11.3 Phase 0 — the rulings (owner, one sitting, no code)

Six one-liners. Each names its default so silence has a meaning. R1 was originally a
genuine fork this plan refused to assume — it gates the vigil, the game's most-invested
surface, and each branch sends different work; the external-review lap (§12) argued it into
a recorded default of (c), but the fork below is preserved in full and the line is still
the owner's to write.

**R1 — the provisioning irony (§9.9). The fork, explicitly:**
- **(a) It is a tragedy — keep it and surface it.** Consequence: the vigil's flagship verb
  stays measured-harmful at scale *on purpose*; a session adds the honest telling (gossip
  and chronicle lines — "the well-supplied dare too much"); V-3's hero-chips ride along.
  No re-baseline. The game gains its truest theme: your kindness emboldens them, and
  boldness kills.
- **(b) It is a defect — retune.** Consequence: a wave over the knob set (checkpoint depth,
  send threshold, fee, the too-hurt quaff rule) until the verb points where the fiction
  says; **balance re-baseline**; the camp test's own comment asked for this *before* #392
  built presentation on the signal, so this branch is paying an already-incurred debt.
- **(c) Split — keep individual saves, damp only the risk-compensation push** (e.g. the
  post-floor quaff no longer resets the too-hurt check). Consequence: session-to-wave;
  **balance re-baseline**; the theme survives in muted form and the verb stops being a trap.

**Recorded default (2026-08-06, §12): (c)** — a tragedy requires a tradeoff, and a verb that
loses on both measured axes is a dominated verb, not a theme (§9.9's amendment carries the
full argument). Silence now means (c); a one-line veto restores (a) or (b).

Until R1 lands, **no further vigil work ships** (this is the plan's one amendment to
§9.6's blessed order — V-3 waits here, not first).

**R2 — the unreachable endgame (§9.10). RULED 2026-08-07: BUILD. ✅ DONE.** The owner's
instruction — *"get all current recommendations from the docs and items that CLI, but somehow not
in the actually game into the playable game"* — is this ruling, spoken. P6 shipped the same day as
the reachability wave (P6a the Foundry, P6b Masterwork + Legendary), godot-only, no re-baseline.
**All 24 player actions are now reachable from the Godot client**, enforced from here on by a
reflection census (`godot/tests/ActionReachabilityCensusTests.cs`) that fails by name on any
action without a surface or a reasoned exclusion. The alternative — defer past v1 — is no longer
live. Subordinate wave doc: `docs/plans/2026-08-07-001-feat-reachability-wave-plan.md`.

**R3 — Emberfall (§9.1).** Default: **park draft #346 explicitly, past v1.** Record
EntryPower 79 as the ruled routing point on the branch; note that the flip is art-blocked,
that no committed art exists, and that no art pipeline currently exists to produce it. A
parked venue is honest; a four-day-old draft implying one-click readiness is not.

**R4 — the Legend Engine (§9.2).** Default: **retire the module, keep the promise.** The
feel-test (P4) carries the promise as its question three: if legends read as logs, the fix
is a composer pass over existing data, never the specced sifter.

**R5 — bless this plan's order** (subsumes §9.6's question). One line makes this section
the sequencing authority and ends per-session re-litigation.

**R6 — the Final Commission (§9.7; added 2026-08-06 from §12).** Default: **it is owed.**
The campaign is not "complete" while its climax is a threshold event with a tally; the
ending that pays off "your craft writes the legends" is one named piece, forged for one
named hero, carried to the Heart. P9 stays sequenced after P4 — this ruling changes P9's
*status* (owed, not conditional), not its place in line. Ruled now so nobody re-litigates
it later; P4 can waive it only with an unexpectedly strong verdict on the shipped
tally-ending.

### 11.4 The critical path

| # | Item | Size | Blocked by | §10 filter line | Status |
|---|------|------|-----------|-----------------|--------|
| P1 | **Night leads with the mark** (loop U5 / H3): the reveal opens with the attribution beat; sale-and-deed grouped by item | session, godot-only | nothing | Hero: tonight's bearer of your marked item. Ledger line: *is* the item — the beat becomes the opening card | **DONE** 2026-08-07 (wave U1) |
| P2 | **The send-off names your work** (H4 / Q-1): the departure slate captions which marchers carry your items | session, godot-only | nothing (reads better after P1) | Hero: the named marchers. Ledger line: the antecedent Night points back to | **DONE** 2026-08-07 (wave U2) — and see the §8 correction: the naive version was already shipped; what was owed was the 2-line cap, the staging, and an honest empty state |
| P3 | **Protect the finale**: two-sided balance assertions (floor 5 *reached* by day ≤N on the main seed; ending *fires* within 100 days) + one scripted full-length client run confirming Act III on the real HUD | session, tests-only | nothing | Invariant: the campaign has an end. (Chain-test clause 3 — protect the substrate) | OPEN |
| P4 | **The human feel-test** (§9.8): `play.ps1`, one real evening, the five written questions — with the fifth (the boredom day) checked against the measured day-11 wall | an evening (owner) | P1+P2 merged — *with a deadline, not a dependency* (see ties) | Not a build item — the gate that rules 9.3, 9.5, 9.7, confirms R4/R6, and re-dates day-11 | OPEN — **put it on the calendar now** (§12, review C: the bottleneck is the owner, not the agents) |
| P5 | **The vigil branch**: (a) surface the irony, or (b) retune wave, or (c) damp compensation — V-3's hero-chips ride whichever branch wins | (a) session / (b) wave + **re-baseline** / (c) session-wave + **re-baseline** | **R1** | Hero: the camped party. Ledger line: the delivery's `Provisioned`/`PotionLifesave` beat — or the death delta, depending on the branch | BLOCKED (R1) |
| P6 | **Endgame surfaces**: buttons + bell-tray wiring for UpgradeForge, BuyForgeSupply, MasterworkAttempt, CommissionLegendaryWork | ~2 sessions, godot-only | R2 — **RULED: build** | Hero: whoever carries the guaranteed Masterwork. Ledger line: the attempt's cost and the resulting item's beats | **DONE** 2026-08-07 (wave U3/U4). Dominance measured before shipping the buttons: 17.0% of crafted value flows through purchased attempts at Tier II with a 5000g reserve — hand-work keeps the field. `BaselinePlayer` untouched, no re-baseline |
| P7 | **The day-11 program**: demand-hazard engine + demand-gated profession debuts (five-pillars Wave 2) | **wave + Contracts micro-PRs + re-baseline** — the expensive one; needs its own plan doc written against this section | P4 (re-confirm the question shortage before the biggest spend) | Hero: the one who needs what only a Tanner or Engineer makes this week. Ledger line: the typed demand fulfilled; `BeatType.ToolAssist` finally gets its emitter | BLOCKED (P4) |
| P8 | **Finish the hero-facing day**: H5 stakes slate → H6 morning aims → H7 survivor's handshake | 3 sessions, godot-side | P1/P2 (slate patterns) | Each carries the hero-facing-day doc's own per-item ledger lines | OPEN after P1/P2 |
| P9 | **The Final Commission climax** (§9.7) | wave; likely **Contracts + re-baseline** | P4 (sequencing only — **R6 rules it owed**; P4 can waive it only with an unexpectedly strong verdict on the tally-ending) | Hero: the chosen bearer at the Heart. Ledger line: the commission fulfilled at the climax | OWED (R6), after P4 |

**Ordering ties, named with tiebreaks:**
- **P1 vs P2** — either order works. Tiebreak: P1 first, because it pays off every marked
  item already in the field on the day it ships with zero setup, while P2's payoff is only
  *felt* once Night notices what the send-off staged.
- **P3 vs P1/P2** — parallel (different files, different skills); P3 must land before P7's
  re-baseline so the new assertions guard the retune. If P3's assertions *fail* — the finale
  is genuinely unreachable under baseline play — that is a broken §2 link-5, interrupt-class
  under §10.5, and the plan re-orders around fixing it before anything else ships.
- **P4 now vs P4 after P1/P2** — testing now measures staging debt already diagnosed;
  testing after P1/P2 measures what we do not know. One evening either way; spend it on the
  unknown. **Amendment (§12, review C):** this tiebreak is a deadline, not a dependency —
  P4 goes on the calendar *now*, and if P1/P2 have not merged by the scheduled evening, P4
  proceeds without them. The review's sharpest observation stands on our own record: five
  days and 2,100+ lines of design writing landed while 9.8 sat undone. Every remaining big
  ruling (legends, ending, boredom day) is gated on that one evening; it is worth more than
  the next three PRs, and no further document-writing lap may displace it.
- **P6 vs P7** — if R2 says build, P6 goes first: one-tenth the cost, no re-baseline, and
  P7 needs its own plan ceremony regardless.
- **P5 vs P8's H5** — P5 (the vigil branch) lands before the H5 stakes slate: H5 stages the
  camp signal that R1's branch may retune, and #392 already demonstrated what building
  presentation on an unruled signal costs (§9.9). (Adopted from §12, review C — its one
  insertion into the blessed order.)

**Riders** (small correctness work that never displaces a path item — it rides with the
first session touching its area): the worn-trinket double-sell fix (§8 defects — a §2
link-2 honesty break, sim-side, draw-free so no re-baseline expected); the
`ConsumesSlot` predicate rewrite with its test corrected rather than appeased; the stale
comment sweep (`ProfessionHandlers`, `CraftingHandlers`, `ShopHandlers`, `Actions.cs:149`);
the counter honesty-gap copy fix (§9.5's amendment — the surface stops implying a walk risk
the resolver does not roll, until/unless the 9.5 tuning wave adds a real one).

**Design notes adopted from §12** (bind the items they name, cost nothing today):
- **H5 / D-1 stakes slates state stakes qualitatively — never survival percentages.**
  "This looks dangerous," not "38% wipe risk." Two reasons: interpretation is the player's
  half of the watching (review B's genuinely new nuance), and resolve-at-departure means
  the sim already *knows* the outcome mid-day — a printed number could only lie or leak
  (§10 test 4 applied to arithmetic).
- **The building-minigames wave, when P4 unblocks it, must preserve the thinking-vs-reflex
  contrast across professions** (kinetic forge/engineering vs clockless alchemy/tanning) —
  if every craft becomes fast-twitch, the register warps into a chore simulator (review A's
  one adaptable contribution; the current four crafts already honor it).

### 11.5 The cut list

Focus is made of noes. Each entry names what it is and why it loses.

**Cut outright:**
- **The Legend Engine module** (sifter/composer/selector as specced) — R4 default. The
  payload shipped piecemeal by cheaper routes; the module is machinery in search of a
  problem the feel-test hasn't confirmed exists.
- **The already-refused class, restated so no fresh session re-pitches it:** outcome wagers,
  watch-to-buff, gate blessings, mid-delve intervention verbs, decision timers ("living
  clock"), runtime LLMs in the sim, the full five-need utility engine. All previously ruled;
  all stay dead.

**Deferred past v1, by name:**
- **Emberfall flip + its art wave** (R3 default) — mechanics stay built-inert; EntryPower 79
  recorded; art-blocked with no pipeline to unblock it. Parking is honest; the draft PR gets
  a note saying so.
- **Prestige era / soft-fail consequence (U-D5)** — the soft-fail latch stays a latch.
- **Erenshor M5 rivalry + Phase B behavior level** — hero behavior that *reads* the
  relationship edges it currently only narrates. This is the cut that hurts: it is the one
  item that would make §5.2's autonomy claim materially truer, and this document would
  personally like it built. It still loses, twice over: the filter ranks making the
  *existing* proof felt (P1–P4) above making autonomy deeper, and P7 beats it for the one
  big-sim slot on measured need (day-11 is a measured wall; shallow autonomy is not).
  **Convergence note (§12.4):** all three external reviews independently push deeper
  individual-hero attachment as the game's long-term engine — the strongest cross-review
  agreement on anything. That triple vote is recorded here as evidence, and it makes this
  cut the named **post-v1 queue leader** — but it still does not displace P7, for the same
  measured reason.
- **Erenshor M4 death-cause typing** — unless P4 flags death reports as illegible.
- **The building-minigames wave** (alchemy Draw, tanning Dip, engineering act split) —
  gated on P4's "do four crafts feel like four skills." One exception may promote: the brew
  puzzle renders its own answer key (§5.1), and if P4 confirms it reads as a form, fixing
  *that one* is a session, not a wave.
- **Counter economics (§9.5) and forge beginner assist (§9.3)** — tuning knobs, not builds;
  both wait for P4's human data. **Amended (§12):** both now carry recorded directions —
  premium-channel for the counter, inverse-assist for the beginner — so P4 confirms a
  default instead of opening a debate; the builds themselves stay deferred and never
  displace a path item.
- **Every §8 wished-for row** — casters/companions/healer, enchanter/food/husbandry, venue
  fatigue, monster variants, disasters, vanity economy, fan letters, scrolls, music
  generation. No code, no plan of record, and now formally: not v1.

**Capped, not cut:** the overhead class — registry manifest enforcement, CI speedups, repo
plumbing (#390's class). Bookable anytime, counted honestly, and never allowed to displace
a path item from a session. A week whose merged list is mostly this class is a week the
game did not move.

### 11.6 The drift defense

The failure mode this plan exists to end: a session picks up whatever is broken in front of
it, fixes it well, and the game moves sideways. Fifteen playtest findings and three rounds
of scattered fixes did not move the owner's complaint; the structural waves did (§10.5).
So, binding rules for every session that touches this repo:

1. **The queue rule.** A session takes the topmost OPEN item on the critical path it is
   capable of. If blocked, take the next unblocked — never an invented sideways item.
2. **The bug rule** (§10.5, made mandatory). A bug found en route: if it breaks a §2 link
   (attribution wrong, craft mis-scored, reveal lies, determinism break, finale
   unreachable), it is interrupt-class — fix now, name the link in the PR. Anything else
   goes to `.claude/tasks/BOARD.md` as a booked line with a one-sentence filter verdict,
   and ships later *with* the next wave in its area. Booking takes thirty seconds;
   drifting takes the week.
3. **The receipt rule.** Every PR description names the plan item it serves (`P1`,
   `P5(a)`, `rider: trinket double-sell`) or says `overhead — booked`. The merged list of
   any week is auditable against this table at a glance.
4. **The single-source rule.** This section is the only v1 plan. Status changes land in the
   same PR as the work; re-ordering happens as a visible diff *here*, argued in review —
   never as a fresh planning doc. (P7 gets a subordinate plan doc for its own wave, written
   against this section, when its turn comes.)
   **Amended 2026-08-07:** the same carve-out is granted, explicitly rather than assumed, to
   `docs/plans/2026-08-07-001-feat-reachability-wave-plan.md` for the reachability items it
   enumerates — P1, P2, P6a/b, and the legibility rows this section had not swept for. Granting it
   by name is the point: that doc leaned on the P7 parenthesis for two-thirds of its scope, which
   would have been a fig leaf. A wave doc is legitimate only when this section says so.
5. **The measurement rule.** When new data contradicts this plan — P4 moves the boredom
   day, P3's assertions fail, a sweep overturns a tuning claim — the plan amends in the
   same PR that lands the finding. A plan that cannot lose an argument with a measurement
   is a wish.

### 11.7 The bet

If everything above is done and only one thing worked, it must be this: **a human being at
a keyboard, on an ordinary evening, watches the Night ledger open with a beat that names an
item they hammered out that morning and the person it saved — and feels it.** P1 and P2
stage that moment; P3 guarantees the campaign it lives in can finish; P4 is the first time
anyone checks the feeling exists. Every system in this document is either upstream of that
moment or decoration around it. The telemetry already believes; the game succeeds or fails
on whether a person does.

---

## 12. The external reviews — what came from outside, and what we did with it

On 2026-08-06 the owner collected design reviews from three other AI models and asked for
them to be considered and rolled in. This section is the full accounting: every substantive
recommendation, a verdict — **ADOPT** (better than what we had; changed this document),
**ADAPT** (a real point, folded in with corrections), **CONFIRMS** (independently arrives at
something already built or already planned — genuinely useful signal, recorded as such), or
**REJECT** (does not survive contact with the code or with a measurement, which is cited) —
and the reason, grounded. Nothing was absorbed on authority; nothing was rejected on pride.

**Provenance.** The three raw texts are preserved verbatim in git history — the
consolidation's staging commit adds them under `docs/design/` and the consolidation commit
removes them, so `git log --follow docs/design/MAKERS-MARK.md` finds the commit and
`git show <staging-commit>:docs/design/<file>` retrieves any of them exactly as received:

| Review | File (in history) | What it had read |
|---|---|---|
| **A** | `gemini-code-1786070286251.txt` (Gemini) | Some of our design logs or summaries — it knows the quench band, the vigil, the boards — but not the code and not the measurements |
| **B** | `Makers_Mark_Recommendations.txt` (unattributed) | Neither the code nor the docs — it reasons from the premise alone |
| **C** | `makers-mark-recommendations-2026-08-06.txt` | Our actual documents, by its own header: the central doc, the ground truth, hero-facing-day, all-building-minigames, the audio forensics — and it argues with our measurements. Weighted accordingly |

**The two discounts applied throughout, per the evaluation brief.** First, *flattery*: all
three open with praise ("a brilliant, high-attraction premise"; "much closer to a genuinely
new game than it first appears"; "the concept is strong"). The praise was read and
discarded, and nothing the prior laps established was softened by it — the four hard facts
stand exactly as written: **no human has ever played this game to completion (§3.2, §9.8);
the boredom wall is measured at day 11–13, not day 30 (§3.3); nothing in CI proves a
campaign can end (§3.3, §8 defects); and the bounty is the one order money can buy (§2
link 3).** Second, *assumed features*: every recommendation was checked against Appendix A
before a verdict — reviews A and B each recommend against or on top of things that do not
exist as they imagine (noted in their tables), which is exactly why the ground-truth
appendix, not any review, stays the arbiter.

**The scoreboard:**

| Review | Substantive recs | Adopted | Adapted | Confirmations | Rejected |
|---|---|---|---|---|---|
| A (Gemini) | 9 | 0 | 1 | 6 | 2 |
| B (unattributed) | 15 | 1 | 2 | 8 | 4 |
| C (2026-08-06) | 13 | 7 | 0 | 6 | 0 |

### 12.1 Review A — Gemini

Fluent, warm, and almost entirely a mirror: it read enough of our material to hand our own
design laws back to us in its own words. That has real value — six independent confirmations
of choices we made for stated reasons — but it contributes no new mechanism, and its one
active *ordering* recommendation points backwards, because it never saw the balance suite.

| # | Recommendation | Verdict | Grounding |
|---|---|---|---|
| A1 | Don't fill the mid-day with superficial reflex minigames; treat Quest/Vigil as observation-and-preparation windows | **CONFIRMS** | Already law twice over: the theater ban (§10 test 2) and the hero-facing-day rule (§4). The middle's content is the smith's own hands, staging, and the one camp question. |
| A2 | Elevate the Vigil as the emotional centerpiece; halt the world; let the player forge from inside the stop ("the world waiting while I hammer out this potion") | **CONFIRMS** | Built, exactly as described: the untimed vigil stop (#388) and the craft-and-send round trip (#392, `CampPanel.cs`). But note the blind spot: the review celebrates a verb the balance suite measures as harmful at scale (§9.9) — it recommends the presentation without knowing the signal underneath points the wrong way. |
| A3 | Higher tiers should change hero *behavior* and survival thresholds, not grant flat stat bumps (a Masterwork shield should change defensive posture / retreat threshold) | **CONFIRMS** | Built, by explicit rule: no craft modifier is a stat bump — they move hero-AI decision thresholds (flee line ±8%/tier, Leech, Lodestone — `CraftModifiers.cs`; Appendix A §8 surprise 3). Quality's stat multipliers stay deliberately separate and legible. |
| A4 | Preserve the thinking-vs-reflex contrast across professions (kinetic forge/engineering vs clockless alchemy/tanning) so the game never warps into a chore simulator | **ADAPT** | The current four crafts already honor it; the review's contribution is making it *binding* — recorded as an acceptance criterion on the deferred building-minigames wave (§11.4 design notes). |
| A5 | Make hero personalities readable at a glance — visible traits, history, gear-gap indicators on the boards | **CONFIRMS** | Built: derivation-only traits, the muster board's empty-slot forecast, Phase B's card-level legibility (§5.2). The behavior-level half remains deliberately cut (§11.5). |
| A6 | Let the attribution engine shine — the Night ledger prominently celebrates your items' specific deeds | **CONFIRMS** | This is P1 ("night leads with the mark"), the plan's topmost item, arrived at independently. One of three independent votes for it (§12.4). |
| A7 | **"Prioritize the Vigil loop first"** (its #1 next-step priority) | **REJECT** | The firmest rejection in this section. The vigil's flagship verb measures **+29 deaths and −12 target-reached** over 20 seeds × 100 days (`CampProvisioningBalanceTests.cs:23-37`), and #392 already built presentation on that wrong-way signal — the camp test's own comment asked for the retune *first*. More vigil investment before R1 rules deepens the rework. The plan holds all further vigil work behind R1 (§11.3), and review C — the one that read the measurements — agrees with us against review A. |
| A8 | Keep the economy honest: no "theater verbs" that consume time without influencing outcomes | **CONFIRMS** | Our own named law, returned verbatim (§6, §10 test 2 — "bounty theater"). |
| A9 | Protect the "cozy-yet-stakes-driven" tone | **REJECT** (superseded) | "Cozy" is the wrong word for a game whose Prepared heroes die ~63–66%. Review C's identity ruling — Spiritfarer-cozy, warmth about loss — is sharper and was adopted instead (§5.7). |

### 12.2 Review B — the premise-only review

The most interesting review *as evidence*. It saw neither code nor docs, reasoned purely
from the premise — and independently reinvented five systems that are already built (traits,
sentimental attachment, gossip, item histories, the march-past) and one item already on the
plan (P2). When a model that has never seen the game derives its feature set from the
premise alone, that is real evidence the premise implies the design. Its one genuinely new
contribution was adopted. Its biggest ask is the cut that hurts, and stays cut.

| # | Recommendation | Verdict | Grounding |
|---|---|---|---|
| B1 | The real loop is Observe → Understand → Craft → Watch → Feel; the goal is not more mid-day tasks but "make the player unable to look away" | **ADAPT** | A good phrasing of the witnessing deficit §11.1 names, and it supports the H4/H5 staging program. Rejected as a *substitute* for verbs: the day-11 wall is a measured **question shortage** (§3.3), and watching, however gripping, adds no questions. The loop authority stands: hands *and* stakes, not stakes instead of hands. |
| B2 | Make relationships the primary long-term progression — "Oh thank God Sarah survived" | **REJECT for v1** | This is Phase-B-behavior / Erenshor-M5, already on the cut list as "the cut that hurts" (§11.5): P1–P4 (make the existing proof felt) and P7 (the measured day-11 wall) outrank unmeasured autonomy depth for the one big-sim slot. The triple-review convergence on this theme is recorded (§12.4) and makes it the named post-v1 queue leader — but a third vote is not a measurement. |
| B3 | Give heroes memorable quirks: refuses potions, always bargains, never bargains, loyal to your shop, only buys quality | **CONFIRMS** | Reinvented our trait table without seeing it: Reckless carries a heal-restock target of 0 (`TraitEffects.cs:121-129`), Thrifty/Spendthrift moves haggle and bounty greed, Discerning shifts the quality gate, and sentimental attachment keeps storied gear over small upgrades (`ShoppingAi.cs:164-175`). More axes stay post-v1. |
| B4 | Never erase mistakes — let failure create memorials, heirlooms, legends | **CONFIRMS** | Built end to end: permadeath → memorial naming the worn gear → heirloom reforging with lineage → legends and the famous dead (§2 link 5, §5.6). |
| B5 | …and scars, retirements, apprentices (a hero loses an arm and returns) | **REJECT** | Contradicts the code twice: no injury persists — working HP initializes to MaxHp at resolve (`ExpeditionResolver.cs:33`), and heroes never leave the roster — `HeroConsideringLeaving` is "a legible warning, never an automatic departure" (`Contracts/Events.cs:276-280`), both deliberate (§5.3). Injury persistence would be a determinism-and-balance program nothing on the path justifies. Item-side inheritance (the heirloom line) already carries the "apprentice inherits" beat. |
| B6 | Heroes should surprise players — sometimes choosing gear because it's lucky, family, or liked | **ADAPT** | The sentiment rule already does this *legibly* (storied gear resists displacement, with a typed reason). Anything further must stay legible: an autonomous world the player can't read is indistinguishable from randomness (§5.8), so illegible whimsy is rejected on the same ground the legibility law was built on. |
| B7 | Avoid reducing the game to Craft → Money → Upgrade | **CONFIRMS** | §11.1's own ruling: the economy is the feeder, not the gold. |
| B8 | **Preserve uncertainty — never expose survival percentages; present "this looks dangerous" and let the player interpret** | **ADOPT** | The one genuinely new nuance in this review, now binding on the H5/D-1 stakes slates (§11.4 design notes). It also closes an honesty trap the review couldn't have known: resolve-at-departure means the sim already knows the outcome mid-day, so a printed probability could only lie or leak. |
| B9 | The player enables success rather than controlling it; heroes deserve credit | **CONFIRMS** | PKD7, structurally enforced (§6). |
| B10 | Tiny personal letters from heroes ("Your shield held. Thought you'd like to know.") | **CONFIRMS** (wished-for) | Independently names an existing wished-for row (§8: "fan letters"). Stays wished-for — but the convergence is noted, and the cheapest honest version on the books is a gossip-voice variant reading real attribution beats in second person. No code, no plan, not v1. |
| B11 | Let the town react naturally — baker comments, children admire, tavern patrons discuss | **CONFIRMS** | Built: gossip retells only real stamped events, ≤3 lines/day, four frozen voices (§5.7). |
| B12 | Expand living item histories; histories become collectibles | **CONFIRMS** | Built: append-only `History`, provenance cards, signed works, heirloom lineage (§2 link 1, §5.6). |
| B13 | **The Window** — each morning heroes physically walk past your shop wearing what you made | **CONFIRMS** | The march-past exists (the conducted send-off, §3.1); the missing half — *naming* which marchers carry your work — is precisely P2/H4, already the plan's second item. The second independent external vote for P2 (§12.4). |
| B14 | Filter rule: "does this make me care more about a specific hero?" | **REJECT** as a replacement | Softer than the house filter and admits the exact failure the filter exists to catch: caring without an outcome is theater. Ours requires the hero *whose outcome changes* and the ledger line that proves it (§10). Kept as a friendly phrasing, not a test. |
| B15 | Vision statement: "a cozy simulation where you never become the hero…" | **REJECT** ("cozy") | Same measurement as A9 — 63–66% mortality among the *careful* heroes is not cozy. The north star stays "your craft writes the legends"; the identity ruling adopted is C12's. |

### 12.3 Review C — the one that read the documents

The clear money's-worth winner. It read the central document, the ground truth, and the
measurements; most of its rulings confirm the plan's own defaults (recorded below — six
independent confirmations from the only reviewer in a position to check them); and it moved
this document in seven places, including the single best idea any of the three produced.

| # | Recommendation | Verdict | Grounding |
|---|---|---|---|
| C1 | **The pattern the docs circle but never name: the game's two most human verbs (counter, camp supply) are both strictly dominated by doing nothing — one systemic defect, not two tuning questions. Principle: "hand-work must beat passive systems, or the passive system must not exist."** | **ADOPT** | The best single idea from the external lap. Our own documents carried both measurements (§9.5's ~10×, §9.9's +29/−12) as separate open questions; the review unified them and named the law. Now §10 test 8, extending the theater ban downward: a verb that changes outcomes for the worse is strictly worse than theater. |
| C2 | §9.9 is a **defect, not a tragedy** — "a tragedy requires a tradeoff," and supply loses on *both* axes; take option (c), and do it before more vigil presentation lands | **ADOPT** | The argument is correct against our own numbers (+29 deaths *and* −12 target-reached — no tradeoff, a dominated verb). R1 now carries recorded default (c); the fork is preserved and the line stays the owner's (§11.3). The "before more vigil presentation" sequencing was already the plan's one amendment to §9.6 — convergence noted. |
| C3 | §9.5: make the counter the **premium channel**, and fix the honesty gap (the UI implies a walk risk that a counter-offer never carries — add real risk or make the surface tell the truth) | **ADOPT** | The direction now follows from adopted test 8 (§9.5 amendment); the honesty gap is booked as a rider (§11.4) — "staged tension the sim doesn't cash" is our own named failure class, caught here by an outside reader. Tuning wave sequenced by R5's queue; it does not displace P1–P6. |
| C4 | §9.10: wire the Phase-D sinks in v1, after H3 — "a 5–6× gold pile with nowhere to go undermines every economic decision" | **CONFIRMS** | R2's default (build, P6) and its placement (after P1) as already written. |
| C5 | §9.3: assist the beginner via **inverse session-skill** — bigger shape-per-strike at low demonstrated accuracy; labor shrinks, the accuracy target never widens | **ADOPT** | The mechanism was already §9.3's "most likely" candidate; the review's first-session argument tipped it into a recorded default (§9.3 amendment). P4 confirms rather than reopens. |
| C6 | §9.1: rule EntryPower **79**; park #346 explicitly with a written note — "a draft that implies one-click-away when the venue has zero art is exactly the drift this project keeps getting burned by" | **CONFIRMS** | R3's default, verbatim, including the reasoning. |
| C7 | §9.2: retire the Legend Engine module, keep the promise; if legends read as logs, the fix is a composer pass over existing data | **CONFIRMS** | R4's default, verbatim. |
| C8 | §9.7: **the Final Commission is owed** — the thesis is "your craft writes the legends" and the current climax is a threshold event with a tally; rule it now so nobody re-litigates | **ADOPT** | New ruling R6; P9 flips from CONDITIONAL to OWED, sequencing unchanged (§9.7 amendment, §11.3). The one adopted item that *commits* future spend — flagged as such so the owner's Phase-0 sitting can veto it consciously. |
| C9 | §9.6: bless the build order as written, with one insertion — the 9.9 retune before H5, Phase-D surfaces after H3 | **CONFIRMS + ADOPT** | The order was already blessed-pending-R5; the P5-before-H5 pin is adopted into the ordering ties (§11.4) — H5 stages the exact signal R1's branch may retune. |
| C10 | §9.8: **schedule the feel-test — the bottleneck is now the owner, not the agents.** "2,100+ lines of design writing in five days while 9.8 sits undone is its own signal"; the evening is worth more than the next three PRs | **ADOPT** | Adopted as a deadline-not-dependency amendment on P4 (§11.4): calendar now; if P1/P2 haven't merged by the scheduled evening, P4 proceeds without them. The jab lands on this very lap — this consolidation is itself more design writing — which is precisely why the amendment bans further document laps from displacing the evening. |
| C11 | §9.4: the demand-hazard engine stays the next big sim program after the loop wave and the 9.9 retune — "two professions have the newest craft surfaces with the least reason to use them" | **CONFIRMS** | P7 as written, including its position behind P4. |
| C12 | **"Cozy" needs an honest modifier: this is Spiritfarer-cozy — warmth about loss — not Stardew-cozy; own it in tone and marketing** | **ADOPT** | Now in §1 and §5.7. The identity was latent in our tone law ("warmth yes, punchlines no") and the mortality numbers; the review named it, and the name changes how the game should eventually be sold. |
| C13 | Audio: hold the U9 acceptance bar from the composed-track forensics (raw LUFS ≈ −21.7, no positive TrimDb, ≥180s, no windowed RMS dropout near the noise floor) | **CONFIRMS** | The bar is the forensics doc's own (`docs/design/2026-08-02-composed-track-forensics.md`); recorded here so the "silent hole" failure class stays dead. |

### 12.4 Where the reviews converge — and what that is worth

Independent agreement is evidence; here is all of it, judged:

1. **The Night reveal is the payoff and must lead (A6, B1/B10's intimacy asks, C4's
   H3-anchored ordering — plus our own P1).** Three reviews and four prior laps point at the
   same top item. P1's position is now the most-corroborated sequencing fact in the project.
2. **The morning march-past wearing your work (A2's send-off framing, B13's Window — plus
   our P2).** Two external votes for the plan's second item, one from a review that had
   never seen the plan. P2 stands, order unchanged.
3. **Individual-hero attachment is the long-term engine (A5, B2/B3/B4, C's feel-test
   question "can you name three heroes by personality").** The strongest *cross-review*
   agreement — and the one place all three lean on something v1 defers. Verdict: recorded
   as evidence on the cut list (§11.5); the Phase-B-behavior/M5 program is the named
   post-v1 queue leader; it still does not displace P7, because day-11 is measured and the
   attachment wall is not. If P4's evening contradicts that — if the felt problem is "I
   don't care about anyone" rather than "there's nothing to decide" — the measurement rule
   (§11.6.5) reopens this in the same PR that lands the finding.
4. **No theater; the middle is preparation and witness, not busywork (A1, B1, C's whole
   frame).** Confirms the law already on record. Nobody outside proposed a mid-delve verb —
   the one anti-premise idea this project has had to kill repeatedly — which is mildly
   reassuring about the premise's legibility.
5. **What none of them found:** a factual error in the central document (C corrected
   nothing, it added rulings; A and B were not equipped to check), or a missed *system*.
   The two reviews that never read the balance suite both steered, with full confidence,
   toward the exact verb the suite measures as lethal at scale (A7, and B's camp-adjacent
   warmth) — the cleanest demonstration this project has of why generic design advice
   defers to Appendix A, and why the ground truth had to survive this consolidation intact.

### 12.5 The diffs — everything in this document that changed because of the reviews

For the reader who wants the outside contribution as a diff list: **§10 test 8** (the
dominance law — C1); **§9.9 + R1** recorded default (c) with the tragedy-requires-a-tradeoff
argument (C2); **§9.5** ruled direction (premium channel) plus the honesty-gap rider in
§11.4 (C3); **§9.3** recorded default (inverse assist — C5); **§9.7 + R6 + P9** the Final
Commission ruled owed (C8); **§11.4** P4 deadline-not-dependency and calendar-now (C10),
the P5-before-H5 ordering pin (C9), and the two design notes (no survival percentages —
B8; thinking-vs-reflex contrast binding on the minigame wave — A4); **§1 + §5.7** the
Spiritfarer-cozy identity (C12); **§11.5** the triple-convergence note making
attachment-depth the post-v1 queue leader (A5/B2/C). Everything else in this document
stands as the four prior laps wrote it.

### 12.6 The money's worth, plainly

**A** returned our own laws with a warmer voice and one backwards priority — its value is
six confirmations, at the cost of needing the balance suite to catch its headline advice.
**B** is the premise run through a model with no access, and the result reads like an
archaeology of our own decisions — five built systems and P2 reinvented from first
principles, one adopted nuance (B8), and its rejections were cheap to write because the
code answers them by line number. **C** did what a reviewer is for: read the record, argued
with the measurements, unified two open questions under one law, and forced four defaults
that had been sitting unruled. If only one of the three had been commissioned, C is the one
that was worth the asking.

---

## 13. Glossary

| Term | Meaning |
|---|---|
| **Bell** | The player's explicit act of advancing the day. Two remain: "Send them off" (ends Dawn), "Snuff the lanterns" (ends Night). The middle conducts itself. |
| **Slot** | One of five daily "real work" actions (craft, buys, bounty, reforge, and the late-game sinks). Conversations and shelf-work are free. |
| **Mark / maker's mark** | The crafter stamp on every player-made item; the predicate the whole attribution layer keys on. |
| **Delve / raid** | A party's trip into a venue; resolved as a pure function at departure (stage 1) and after the camp decision (stage 2). |
| **Vigil** | The player-facing name of the Camp phase; "the vigil stop" is the untimed modal when a party parks. |
| **Park / camp** | A deeper-bound party halting at the floor-1 checkpoint after a clean stage 1, awaiting the supplies/home/deeper decision. |
| **Beat** | A provable attribution fact (killing blow, lethal save, breakpoint clear, provisioned, potion lifesave) computed by counterfactual replay. |
| **Atomic pass** | The automatic Morning shopping sweep of every hero over the shelf — the counter's invisible competitor. |
| **Commission** | A named hero's public ask: slot, minimum quality, 5-day deadline, premium. Board holds three open asks. |
| **Bounty** | Escrowed gold posted against a floor; heroes weigh it by greed/reputation/distance and choose. |
| **Heirloom** | An item reforged from a fallen hero's worn gear, carrying their lineage line forward. |
| **Signed work** | A rare craft that earned a legend name; its history reads as a growing inscription. |
| **Built-inert** | Complete, registered, tested content deliberately not switched live (a determinism-gated flip away). Emberfall is the standing example. |
| **Golden replay** | The build-failing test asserting same seed + same actions = byte-identical world. |
| **Re-baseline** | The deliberate ceremony of re-recording goldens when a change legitimately moves the RNG stream (content flips, new draw sites). |
| **PKD7 / "influence, never orders"** | The design law: no player verb commands a hero or touches resolution through mood. |
| **Bounty theater** | The named failure class: a verb or surface that occupies the player without changing any outcome. Banned. |
| **BUILT / BUILT, CLI-ONLY / BUILT-INERT / DESIGNED / WISHED-FOR** | The five status labels (defined in the preamble, ledger in §8). CLI-only is *not shipped* — DEPLOYED means the Godot client. |
| **P1…P9 / the path** | Items of the plan of record (§11.4) — the sequencing authority for v1 work. R1…R6 are its Phase-0 rulings. |
| **The ground truth / Appendix A** | The source-only control pass at the end of this document — the same game described from code alone, every claim line-cited; the arbiter for any mechanical dispute. Absorbed from `docs/design/2026-08-06-mechanics-ground-truth.md` on 2026-08-06. |
| **The dominance test** | §10 test 8, adopted from the external-review lap: hand-work must beat passive systems, or the passive system must not exist. |
| **Review A / B / C** | The three external model reviews evaluated in §12; raw texts preserved in git history at the consolidation's staging commit. |

---

## 14. The documents behind this one

This document stands alone, but it compresses a real paper trail. Where you want the full
argument:

- **Mechanics arbiter:** **Appendix A** of this document (absorbed intact from
  `docs/design/2026-08-06-mechanics-ground-truth.md` on 2026-08-06) — the same game
  described from source only, `docs/` deliberately unread, every claim line-cited. Written
  as an independent control pass against this document and collided with it; the
  source-only pass won every factual dispute the collision found (this version carries
  the corrections). When a mechanical argument starts, settle it there first — it is the
  place a "the code does X" claim goes to live or die.
- **External reviews:** the three raw review texts (§12) are preserved verbatim in git
  history — the consolidation's staging commit adds them under `docs/design/`, the
  consolidation commit removes them; §12 carries every recommendation with its verdict.
- **Loop authority:** `docs/design/2026-08-04-hero-facing-day.md` (the phase-by-phase verb
  program and the formal withdrawal of "two bells completes the loop");
  `docs/plans/2026-08-03-001-feat-loop-structure-plan.md` (the two-bell diagnosis and units);
  `docs/design/2026-08-04-all-building-minigames.md` (the minigame layer, sequenced second).
- **Measurement:** `docs/design/2026-07-27-how-you-play.md` and
  `2026-07-27-gameplay-loop-analysis.md` (fifteen instrumented playthroughs);
  `docs/design/2026-07-25-core-interaction-audit.md` (+ EXECSUMMARY).
- **Strategy:** `docs/plans/2026-07-28-003-roadmap-post-skeleton.md` (sequencing authority);
  `docs/design/2026-07-27-five-pillars-design-synthesis.md` (the demand-map program);
  `docs/design/2026-07-21-operating-model.md` (the 3-tier work engine and Completeness Bar);
  `docs/design/tone-register.md` (the voice).
- **Ledgers:** `docs/registry/SYSTEMS.md` and `CONTENT.md` — with this document's §8
  corrections noted until those rows are refreshed.

*Everything in this document was verified against the repository on the day it was written —
verified again by collision with an independent source-only pass, which caught real errors
the first pass carried (an endgame reported as reachable, a venue reported as art-complete,
a haggle risk that does not exist), then attacked by a hostile-review lap, which caught
the errors the collision carried (player reports cited where no player exists, a boredom
wall dated day 30 when the measurement says day 11, a recall statistic quoted at half its
price, a bought order softened into a suggestion, a finale no test protects) and added the
plan of record (§11), and finally collided with three external model reviews (§12), which
were themselves checked against the code before anything was absorbed. When the code moves,
update this document or mark the section stale — an out-of-date central document is worse
than none, and this project has measured that twice. Appendix A is the cheap re-check: it
cites lines, not intentions.*

---

## Appendix A — Mechanics Ground Truth (derived from source, 2026-08-06)

> **Absorbed unchanged** from `docs/design/2026-08-06-mechanics-ground-truth.md` on
> 2026-08-06: headings demoted one level to fit this document, not a word otherwise touched.
> This appendix is the mechanical arbiter — it was written from source only, with `docs/`
> deliberately unread, and it wins any factual dispute with the body of this document.
> Section references inside it (§1–§8) refer to the appendix's own sections. Line numbers
> are pinned at commit `0dfe3a8`; when the code moves, re-verify here first.

**Method.** This document was written by reading the CODE ONLY — `sim/GameSim/`, `sim/GameSim.Tests/`, `sim/GameSim.Cli/`, and `godot/scripts/` — with `docs/` deliberately unread. It is the control arm against the documentation-derived design account: where the two disagree, the disagreement is the finding. Every claim carries a `path/file.cs:line` citation so it can be checked. Line numbers are as of commit `0dfe3a8` on this branch.

**One-paragraph summary of what the game actually is, per the code.** A deterministic, integer-only, five-phase-per-day simulation (`sim/GameSim/Kernel/GameKernel.cs:188-198`) in which the player runs a craft shop (24 action types, `sim/GameSim/Contracts/Actions.cs:10-35`), six-or-fewer autonomous heroes shop each Morning by a pure gear-score-per-gold rule (`sim/GameSim/Heroes/HeroShoppingSystem.cs`), form parties and raid one of three live venues each day (`sim/GameSim/Expedition/ExpeditionSystem.cs`), and the entire raid is resolved as a pure function at departure and merely *revealed* at Evening (`sim/GameSim/Drama/ExpeditionRevealSystem.cs`). The player's craft reaches the heroes only through prices and shelves — "influence, never orders" is enforced structurally, not aspirationally. A counterfactual attribution engine proves, from recorded dice, whether a specific player item saved a specific hero's life (`sim/GameSim/Expedition/AttributionEngine.cs`).

---

### 1. The complete action inventory

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

### 2. The phase machine, exactly

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

### 3. The expedition truth

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

### 4. The causal chain, verified end to end

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

### 5. What the heroes actually decide, and on what inputs

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

### 6. The numbers that matter

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

### 7. Dead and vestigial code — where drift has already happened

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

### 8. Surprises — both directions

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

### Appendix: file → subsystem index

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
