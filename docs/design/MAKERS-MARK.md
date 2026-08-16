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

## 1-7 — moved

**What the game is, what it borrows, and how a person plays it now lives in
[`THE-GAME.md`](THE-GAME.md).** It was moved rather than copied: this file is the working
document — the ledger, the open questions, the filter, the plan of record and the review
accounting — and a second copy of the description would be a second thing to keep true.

Sections below keep their original numbers so every existing citation still resolves.

---

## 8. What is built, what is designed, what is wished for

(`docs/registry/SYSTEMS.md` and `CONTENT.md` were cited here as the row-level ledgers. **That
directory does not exist** — removed 2026-08-12. The asset ledger is `docs/design/ASSETS.md`;
everything else is verified against code, not against a registry.) This is the load-bearing
summary, verified against code
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
| Emberfall Foundry | **BUILT AND LIVE** — in `LiveRotation` with committed art and a priced ore ladder (firebrick..heartcoal) | `VenueRegistry.cs:50-64`, `MaterialRegistry.cs:94-105`; shipped by #453 (rung live) + #462 (Foundry art) |
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

- ~~`docs/registry/SYSTEMS.md` says 2 of 4 professions are wired with `ActiveCraft: false`~~ —
  moot: that registry no longer exists. All four professions are wired and flagged true
  (`CraftingHandlers.cs:92-120`).
- The 07-27 audits' "consumables and trinkets can never be commissioned" root-cause finding —
  **obsolete**: `CommissionSystem.cs:251-288` commissions both (trinkets for Regulars and up).
- The 07-29 state-of-the-game headline table — **stale on three rows**: classes ("3 of 6" →
  six recruitable), venues ("2 of 4" → three live), and save/load ("nothing survives a
  restart" → full save/autosave shipped).
- Any doc describing the Phase-D economy as shipped — **wrong for the client**: sim-complete,
  CLI-only (§9.10).
- ~~Any doc calling Emberfall "art complete" — **wrong**: no committed art at all.~~ **Retracted
  2026-08-12: this correction is itself now wrong.** Emberfall is live with committed art. PR #346
  was CLOSED unmerged on 2026-08-07; the flip shipped via #453 (rung live) and #462 (Foundry art
  through the real SDXL chain). Verified against `VenueRegistry.cs:50-64` and
  `MaterialRegistry.cs:94-105`, not against this document.

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

### 9.1 The Emberfall flip — RESOLVED, shipped 2026-08-11 (PR #346 closed unmerged; #453 + #462 did it)

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

**Ruled 2026-08-07 (§11.7.7).** It stays, and it is promoted: the demand-hazard engine becomes a
prerequisite of fluid professions rather than a parallel program. Profession unlocks are
demand-gated story beats, so the engine is now the unlock trigger *and* the demand fix for the
non-blacksmith professions in one piece of work. Sequence it first, or alongside.

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

**Ruled 2026-08-07 (§11.7.3–11.7.5).** The order stands, and the standard it is built to changed:
every phase owes a verb that changes an outcome or a surface that reveals the player's stake, and
information arrives through a face rather than a board wherever it can. The middle of the day
gains no intervention verb — live mid-delve delivery is refused for the three reasons in §11.7.5 —
but it gains a second window: the camp speaks first, and deep-bound runs earn a second checkpoint.

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
| P3 | **Protect the finale**: two-sided balance assertions (floor 5 *reached* by day ≤N on the main seed; ending *fires* within 100 days) + one scripted full-length client run confirming Act III on the real HUD | session, tests-only | nothing | Invariant: the campaign has an end. (Chain-test clause 3 — protect the substrate) | **DONE** 2026-08-10 (forward-ladder L0-L7, closes draft #413). Venues are a forward ladder now; routing keys on `Hero.LadderRank`, not the power latch that stranded parties. Two-sided and green on the main seed (rung-0 clear day 18, Act III day 18, Climax day 26, Ending day 31) and on the 10-seed sweep (Ending ≤ day 36). See §11.8. The scripted full-length client-HUD run remains open |
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
3. **The receipt rule.** Every PR description carries one line beginning `Serves:` — the plan
   item it serves (`Serves: P1`, `Serves: P5(a)`), the spine link it defends
   (`Serves: link4`), `Serves: substrate`, or `Serves: overhead — booked`. The literal prefix
   is the point: the merged list of any week is auditable in one grep, which is how the
   hundred-reasonable-PRs erosion in CLAUDE.md rule 12 becomes visible at all. The receipt
   asserts presence, never truth — a false one is a rule-8 lie living in git.
4. **The single-source rule.** This section is the only v1 plan. Status changes land in the
   same PR as the work; re-ordering happens as a visible diff *here*, argued in review —
   never as a fresh planning doc. (P7 gets a subordinate plan doc for its own wave, written
   against this section, when its turn comes.)
   **Amended 2026-08-07:** the same carve-out is granted, explicitly rather than assumed, to
   `docs/plans/2026-08-07-001-feat-reachability-wave-plan.md` for the reachability items it
   enumerates — P1, P2, P6a/b, and the legibility rows this section had not swept for. Granting it
   by name is the point: that doc leaned on the P7 parenthesis for two-thirds of its scope, which
   would have been a fig leaf. A wave doc is legitimate only when this section says so.
   **Amended 2026-08-08:** granted, by name, to
   `docs/plans/2026-08-08-001-feat-proof-the-player-never-sees-plan.md`, which carries **P3** and
   a set of link-4/link-5 leaks three audits found the same night: the sim computes the proof and
   the client discards it. The worst of them is not a gap in the plan, it is a gap in the *game* —
   `CampNarration.Attribution` writes the causal sentence naming what the player did, and it lives
   in `GameSim.Cli`, a project the Godot client does not reference. That is §11.6 rule 2
   interrupt-class (a §2 link-4 break), which is why it leads a wave instead of being booked.
   The grant covers P3 and that leak set only; the art rows in it ride as capped overhead and may
   never displace a path item.
   **Amended 2026-08-09:** granted, by name, to
   `docs/plans/2026-08-09-001-feat-the-shell-around-the-game-plan.md` — the playtest harness, the
   export/shipping path, and the settings menu. This is the second and final live wave doc; the
   two-doc cap is now full and nothing further may be written until one of them dies on merge.
   The grant is **entirely substrate and capped overhead** — not one unit in it is a §2 link item,
   and none may displace a path item. It earns the slot on one argument: the harness was reporting
   completed runs it had not performed (§A), which makes every finding it has produced since
   2026-08-04 suspect, and a compromised instrument is a substrate defect rather than a chore.
   Its §C5 carries a genuine law question — a settings menu is where the seven laws can be broken
   by something shaped like a courtesy — and the tripwire it specifies is binding on any future
   settings work whether or not the rest of that wave ships.
   **Amended 2026-08-10:** the shell wave's units all landed (#417–#422, #430–#433), so its doc
   dies on merge per rule 7 and the freed slot is granted, by name, to
   `docs/plans/2026-08-10-001-feat-the-playtest-keeps-what-it-saw-plan.md` — the same harness, one
   layer deeper. The last wave made it stop lying about whether it played; this one makes it **keep
   what it saw**: per-turn frames, the backend log nothing has ever read, a coverage denominator so
   that "we tested everything" can be false, and personas so N runs measure N players instead of one
   player N times. Substrate again, and it earns the slot on its predecessor's argument plus one
   sharper fact: the scout judge's standing verdict — *"nothing in the log names the player's work —
   every outcome is read as generic"* — is a link-4/link-5 claim resting entirely on one model's
   prose, with no recorded evidence beside it. An instrument that cannot evidence a claim about §2
   links cannot be used to decide anything about them. Same constraint as before: capped overhead,
   and no unit here may displace a §11.4 path item.
   **Amended 2026-08-10 (second):** the keeps-what-it-saw wave's units all landed (#436, #438,
   plus the frames/backend/coverage/personas and sweep PRs), so its doc dies in the same PR as
   this amendment (rule 7, atomically — a plan only on a branch does not exist, so the deletion,
   the successor doc, and this grant travel together). The freed slot is granted, by name, to
   `docs/plans/2026-08-10-002-feat-the-playtest-becomes-a-player-plan.md` — five units: the eyes
   upgrade (llava:7b cannot read the game's own pixel fonts; qwen3-vl:8b measured reading a full
   tutorial line verbatim on this machine), mechanical fun metrics led by the product-sentence
   counter (a direct instrument on links 4–5, and the §A judge currently cannot answer its own
   day-11 question — its input is trimmed from the front at 6000 chars), a dead-verb detector
   probing law 3 with zero prose, driver-side temperament so quit reasons become findings, and
   scenario cards for owner goal 1. The plan was adversarially checked by a second fable pass and
   carries its seven named changes, including: the first sweep is an instrument SHAKEDOWN whose
   numbers are disposable until §11.8's Gloomwood fix lands — a baseline taken on a campaign that
   cannot finish is not a baseline. Substrate and capped overhead throughout; no unit may
   displace a §11.4 path item.
   **Amended 2026-08-10 (fourth):** the playtest-becomes-a-player wave's units all landed (#448),
   its doc died on merge per rule 7, and no successor was queued at the time — this section is
   catching up on that untracked death now. Separately, the critical-path slot the (now-deleted)
   "third" amendment above once described — `docs/plans/2026-08-10-003-feat-the-forward-ladder-plan.md`,
   the §11.8 resolution — also landed its last unit (L6/L7) and dies on merge in this same PR
   (§11.8 carries the resolution). Both slots are empty; nothing is queued.
   **Amended 2026-08-11:** one slot is granted, by name, to
   `docs/plans/2026-08-11-001-fix-the-eyes-learn-labels-plan.md` — the ten-rounds campaign's first
   forensics wave. The instrument's own volume run convicted it: models press visible labels while
   the harness accepts only node names (an A/B pair in the campaign's own logs proves the gap),
   judges were grading harness digest artifacts as if they were game copy, and the product-sentence
   headline read True in 33 of 34 runs on a regex hit the backend never corroborated. Four units
   are substrate (the instrument); two are game defects the campaign caught for real — commission
   and memorial buttons that never consult the legality mirror they were owed, and a quench
   minigame that ticks from boot and auto-plunges a phantom craft into every session. Capped
   overhead; no unit may displace a §11.4 path item.
   **Amended 2026-08-11 (second):** the eyes-learn-labels wave landed both halves (#457 the
   instrument, this PR the game defects), so its doc dies in this same PR per rule 7. Both
   slots are empty; nothing is queued.
   **Amended 2026-08-11 (third):** one slot is granted, by name, to
   `docs/plans/2026-08-11-002-feat-the-playtest-learns-to-finish-plan.md`. The ten-rounds
   campaign could not complete a single model-driven test (58 of 58 runs dead on patience by
   day 3; ~1,190 of ~1,260 refusals were the 8B model emitting empty commands — fable census),
   and the owner's finding is the grant's whole argument: testing that cannot finish tests
   nothing. Three units, all instrument-side: menu-choice acting (pick from the advisor's own
   legal set instead of composing freeform JSON), sweep-mode patience (the would-have-quit
   moment becomes a finding instead of a fatality), and an eyes/brain model split. Substrate,
   capped overhead; no unit may displace a §11.4 path item.
   **Amended 2026-08-11 (fourth):** U1-U3 of the playtest-learns-to-finish wave landed together
   in this PR — menu-choice acting, the eyes/brain split, and sweep-mode patience are all wired
   into the driver and covered by the pure-logic suites. Its own Definition of Done names this
   PR as where the doc dies (rule 7): U4 is a run, not a unit — the 10-run sweep proving the
   success criterion, executed by the orchestrator once the GPU frees, reported to the owner
   from `runs/`, never re-added here as a queued unit. The slot is empty; nothing is queued.
5. **The measurement rule.** When new data contradicts this plan — P4 moves the boredom
   day, P3's assertions fail, a sweep overturns a tuning claim — the plan amends in the
   same PR that lands the finding. A plan that cannot lose an argument with a measurement
   is a wish.

### 11.7 Owner direction — 2026-08-07: experience, phases, narrator, progression

The owner's rulings from the 2026-08-07 review of `THE-GAME.md`. These are decisions, not
suggestions: work proposed from here must trace to a ruling below or to the standing laws in
§11.7.8. Anything tracing to neither gets parked and raised. **Documentation only at the time
of this ruling — no sim changes, no re-baselines, no implementation sequencing yet.**

**11.7.1 The bounty explanation is rewritten, and the phrase is retired.** "A posted bounty is
an order you can buy" is dead. It contradicted influence-never-orders in six words, and that
collision *was* the confusion. Replacement framing: the bounty is the one lever aimed at
**where** they go, and they still choose to take it. The description now walks the four moves —
escrow, their judgment, one hero committing the party, then payout or death or refund
(`THE-GAME.md` §4.5). No mechanical change; copy and documentation only.

**11.7.2 "Small on purpose" is replaced by "narrow now, built to grow."** The game is not small
forever; it is narrow *now* as a focus decision. Every system ships finished rather than
sketched, and the architecture — built-inert content, determinism-gated flips, re-baseline
ceremonies — exists precisely so it can widen later. **Guard, mandatory:** "built to grow" is a
statement about post-v1 sequencing and never a licence for breadth work now. The Completeness
Bar and the §10 filter stand unchanged. A session reading this as permission to start new
content has read it wrong.

**11.7.3 No important information without a face.** Reading boards is boring. Sim information
must be *experienced* — dialogue, scenes, characters — not posted. Boards stay as reference
surfaces; dialogue becomes the delivery. The pattern is already proven twice in-project: the
customer speaks first (CustomerVoice, a read-only derivation from the hero's own state) and the
tavern handshake. Extend that pattern; do not invent a second mechanism.

**The constraint that survives this ruling:** the simulation is *not* reduced or scripted to
achieve it. The sim already computes everything the boards say, and the attribution proof, the
moat and the golden replay all depend on it staying pure. Presentation may stage, select, voice
and pace sim facts. It may never script outcomes or invent facts the sim did not produce.
Scripted moments are made by staging real data, never by faking it.

**11.7.4 Every phase owes the player something to do and a reason to watch.** "Something to do"
means a verb that changes an outcome, *or* a surface that reveals the player's own stake — verb
count for its own sake is the busy-day trap, already named and still banned. The per-phase gaps
adopted as the program: Dawn gets a spoken headline and a one-click path from a gap line to the
forge pre-loaded with the answering recipe; Quest gets item chips on the marchers carrying your
work and one line of conductor copy when a vigil is coming, so crafting during the march stops
being tribal knowledge; Vigil gets trait and relationship chips plus the per-hero survival math
the sim already computes; Deep Vigil gets the stakes slate and item flares on moments that will
become beats (honest, because stage 2 is already resolved when the show plays); Night deals the
reveal as cards with rhythm and previews an heirloom's lineage before committing. Across all of
them, one story threads the day — the same hero and item recurring from the morning headline to
the night card. Continuity of reference is the cheapest large experience win available.

**11.7.5 Reaching into the dark: more windows, not live delivery.** The instinct is right —
reaching into the dark is the game's best feeling. The mechanism of sending items live mid-delve
is refused, because it breaks three load-bearing things at once: raids must stay pure functions
or the attribution proof dies, decisions must stay untimed, and heroes must not become units you
tend. What is adopted instead: **the camp speaks first** — the vigil slate opens with the party's
own ask, derived read-only from their state the same way CustomerVoice is, turning a dashboard
the player evaluates into a request the player answers (zero sim change; this is staging). And
**checkpoints scale with depth** — deep-bound runs earn a second camp, so the best moment fires
twice on exactly the runs where the stakes are highest. Precedent is Darkest Dungeon, which
scales camps with dungeon length while never permitting intervention in a fight. The second
checkpoint is a priced sim unit with a re-baseline, sequenced later. Gate provisioning stays
deferred, built only if telemetry shows heroes systematically under-buying heals.

**11.7.6 The game gains a narrator.** Darkest-Dungeon-style, AI-generated voice, and plausibly
the highest feel-per-cost item on the table because the writing already exists: four frozen
voices, ~1,470 authored lines, a pacing director and a tone law. Rules it ships under:

- **Sparse and triggered, never continuous.** Darkest Dungeon's narrator works because he fires
  on kills, deaths and thresholds. Continuous narration worked once, in a short novelty, and
  would wear immediately here. Trigger set: attribution beats, deaths, depth records, act turns,
  and the vigil stop opening — daily-capped exactly as gossip already is.
- **Voice, not new writing.** The narrator speaks the existing frozen lines and curated additions
  in the same register. The tone law applies unchanged: warm, dry, never grim, never cute; deaths
  with dignity.
- **One primary voice for v1.** All four text voices stay; one gets the microphone first.
- **Pipeline, mandatory shape: generate → human-curate → freeze.** Lines are generated at content
  time, reviewed, and committed as static audio. Runtime *selection* stays deterministic from real
  events. This preserves the frozen-voices discipline, the golden replay, and testability.

**Shipping note (verified 2026-08).** Steam's AI disclosure policy separates pre-generated content
that ships with the game from content generated at runtime. Baked voice lines are pre-generated —
a disclosure checkbox and a brief description. Runtime generation requires documented guardrails
and carries platform risk. One more independent reason the standing refusal of runtime LLMs in the
sim is correct, and it is reaffirmed here.

**11.7.7 Progression becomes fluid, and unlocks are demand-gated.** Long-run "pick a profession"
does not hold. The player accumulates disciplines across a campaign — blacksmith, then alchemy
early, then later disciplines — giving the mid-game more to open. Refinements that ship with it:

- **Unlocks are story beats, not menu picks.** A profession opens when the world starts needing
  it: *three heroes came back poisoned from Gloomwood; the town needs an alchemist.* This promotes
  the five-pillars demand-gated design already on the books, and makes every unlock an event in
  the town's story, which is this game's grammar. The typical order can still follow the owner's
  ladder, since demand tracks venue progression anyway.
- **The demand-hazard engine is a prerequisite, not a nice-to-have.** Measured demand already
  skews blacksmith under pick-2; adding professions to a world that only asks for swords
  multiplies the "newest surfaces, least reason to use them" problem. Sequence demand first, or
  together — this rules §9.4.
- **Enchanting is probably a graduation, not a fifth pipeline.** The modifier layer — quench oils,
  runes, fittings, behaviour-shifting and slot-exclusive — is already proto-enchanting. Grow that
  layer into the discipline rather than building a new pipeline. Spellcrafting is genuinely
  net-new content; it parks post-v1 behind the same demand engine.
- **Identity guard.** Pick-2 created build identity. Accumulation must preserve who-you-are
  through mastery depth — talents, tiers, the mark's reputation — so "I do everything" does not
  flatten into "I am nothing in particular."
- **The governor already exists.** The five-slot day rations output, so more professions deepen
  the daily triage decision instead of multiplying busywork. Keep the budget; it is what makes
  breadth playable.
- **Determinism note.** Each unlock is a content flip — a determinism event with a re-baseline
  ceremony, same as a venue go-live. Plan them as such.

**11.7.8 The laws none of the above weakens.** Influence never orders; no timers on decisions;
every verb changes an outcome or reveals the player's stake; show only what the sim decided;
sim purity and determinism; no runtime LLMs in the sim; skipping stays legal and its cost is
named in copy, never engineered.

**11.7.9 What this changes about sequencing.** The demand-hazard engine rises — it is now both
the unlock trigger for 11.7.7 and the demand fix for the non-blacksmith professions, in one
program. The narrator is the standing highest feel-per-cost candidate. The second checkpoint is
a priced sim unit for a later wave. The 11.7.3 and 11.7.4 staging items are zero-sim and
art-light, and can interleave with anything.

**11.7.10 Open, for the owner.** Which of the four voices gets the microphone first, and does the
pacing director or the event type decide when text falls back to voice? Is the second checkpoint
at a fixed floor or derived from the target? Is enchanting-as-graduated-modifiers accepted, or is
a distinct pipeline owed for feel? Does spellcrafting make v1.x at all? And how many disciplines
should one campaign realistically open — all of them, or most-but-not-all, so campaigns differ?

### 11.8 The finale is unreachable — measured 2026-08-08, root cause found

P3's assertions were written and went red on their first run. This is not a test that needs
tuning. It is the finding P3 existed to produce, and under §11.6 rule 2 it is interrupt-class:
a §2 link-5 break, since a campaign with no end has no memory to end with.

**The measurement.** 100-day `BaselinePlayer` runs, 11 seeds, then 44 runs across four scripted
policies of increasing competence. Every single run: floor 3 by day 3-4, **floor 4 by day 3-5,
and floor 5 never**. Act II by day 4, **Act III never, Climax never, Ending never**. Not
borderline — unanimous, and identical across a baseline smith and a smith crafting Masterworks
every day, which is what rules out "the harness under-plays it."

**Root cause: the Gloomwood routing trap.** Gloomwood's `EntryPower` is 72
(`sim/GameSim/Venues/Gloomwood/GloomwoodVenue.cs:92`), which sits *between* the Mine's floor-4
gate (60) and its floor-5 gate of 100 (`sim/GameSim/Venues/VenueRegistry.cs:107`).
`VenueRouter.IsBetter` (`sim/GameSim/Venues/VenueRouter.cs:101-125`) permanently prefers the
highest band a party has reached — so the moment a party's power crosses 72, every future trip
routes to Gloomwood, a venue with **only four floors**. The party is now strong enough to be
taken out of the five-floor venues and can never be routed back, because the only return path
is `PostBountyAction` and no shipped scripted policy ever posts one.

A second mechanism compounds it: `ApplyCompetenceRetreat`
(`sim/GameSim/Expedition/ExpeditionResolver.cs:405-426`) caps each hero at +1 floor per trip, so
by the time a hero earns floor-5 eligibility her power has usually already tripped the reroute.

**Ruled out.** `ArcDirectorSystem` is not miscalibrated — it reads a legitimate venue-agnostic
signal at the intended threshold and faithfully reports a depth the expedition layer never
produces. And the old "BaselinePlayer refuses 90% of legal crafts" finding is **stale**: it was
fixed in #328 and measured at zero craft rejections across all 44 runs. Any doc or task still
asserting it should be corrected, not repeated.

**The fix is an owner decision, because every option is a balance lever and each implies a
re-baseline.** Three, with the tradeoff named:
1. **Raise Gloomwood's `EntryPower`** above the practical floor-5 power band — stops the four-floor
   venue stealing parties that should be finishing a five-floor one. Cleanest fix for the trap
   itself; changes which venue mid-power parties see, and §11.5's parked Emberfall decision
   already shows how sensitive venue share is to this number.
2. **Lower the floor-5 gate** on the Mine and the Sunken Crypt. Smallest diff, but it treats the
   symptom: the trap still exists, it just becomes survivable.
3. **Give the router a real return path** for veteran parties. Most faithful to the design, most
   work, and it needs a rule for when a party chooses to go back.

Two harness gaps should be fixed alongside whichever lever wins, because they are why the read
was murky: `BaselinePlayer` submits one craft a day against a five-slot budget, and never sets
`CraftAction.PerformanceGrade`, which caps every scripted craft below the Fine-plus breakpoint
(`sim/GameSim/Crafting/QualityRoller.cs:147-173`). Neither is the cause — fixing both still never
reached floor 5 — but both distort every balance number the repo currently records.

**A note on the instrument.** `tools/Analytics` reported 15 anomalies over the same runs, all LOW
or MEDIUM, and **none of them was "the campaign never ends."** An anomaly detector that cannot
see a missing ending is a detector reporting on the weather inside a burning building. Whatever
lever wins, the analytics pass owes an arc-completion check.

**Ruled 2026-08-10 — none of the three levers; the signal itself was wrong.** The owner:
*"Powerful party's should NOT go back? They should continue to the next dungeon which adds more
features/unlocks for the player."* And on the red gate's siblings: *"you are overcomplicating
the balance tests, just do what's more fun for the player."* Venues are a **forward ladder** —
graduation by beating a venue's bottom floor increments a monotonic `Hero.LadderRank`, routing
keys on rank instead of the power high-water latch, and oscillation becomes impossible by
construction rather than by tuning. Each rung unlocks blacksmith-facing features (ore tiers,
recipes, factions, news); the arc re-anchors so Act III is the last dungeon opening and the
Climax is its deepest floor; the Mine stays alive through the recruit trickle, the Forge's own
ore hunger, and the bounty's refusable back-steer — nothing new built there. The salve and
money-supply tests are re-framed to assert what a player feels. Lever 1 was already measured
dead before this ruling (Gloomwood `EntryPower` 90: reachability 0 of 4385 party-ticks, 7 tests
broken, not merged); the ruling supersedes the remaining two. The wave of record:
`docs/plans/2026-08-10-003-feat-the-forward-ladder-plan.md`, which also carries §11.5's parked
Emberfall decision (the flip is rung 2's go-live; the #92 share-collapse measurement becomes
obsolete when venue share is stage-keyed instead of threshold-keyed).

**Resolved 2026-08-10 (L0-L7).** The ladder shipped: rank-keyed graduation and routing (L0-L2),
all three rungs' gates characterized and set from measurement rather than guessed (L3-L4), the
arc re-anchored onto `Hero.LadderRank` (L5), and the finale gate itself re-pinned two-sided and
green (L6, closing draft #413). On the main seed a full `BaselinePlayer` campaign now clears rung
0 (floor 5) by day 18, opens Act III the same day, reaches its Climax by day 26, and Ends by day
31; across the main seed plus the 10-seed sweep, every campaign reaches its Ending by day 36 at
the latest — every seed lands inside the plan's own windows on the first measurement. The finale
P3 exists to protect is reachable. §11.4's P3 row is updated in this same PR; the plan doc itself
is deleted per rule 7 — git history is the archive.

### 11.9 The bet

If everything above is done and only one thing worked, it must be this: **a human being at
a keyboard, on an ordinary evening, watches the Night ledger open with a beat that names an
item they hammered out that morning and the person it saved — and feels it.** P1 and P2
stage that moment; P3 guarantees the campaign it lives in can finish; P4 is the first time
anyone checks the feeling exists. Every system in this document is either upstream of that
moment or decoration around it. The telemetry already believes; the game succeeds or fails
on whether a person does.

---

### 11.10 Finish the variation the owner asked for — 2026-08-14

**Status: capped overhead, substrate. Not a §2 link item, and no unit here may displace a
§11.4 path item.** It exists in this section, rather than as a third file in `docs/plans/`,
because rule 4's two-doc cap is full (`2026-08-13-002` and `2026-08-14-001` both hold live
units) and the owner chose an inline amendment over raising the cap. Written tight for that
reason: this is the plan, not a wave doc's worth of it.

**What was asked, 2026-08-14:** *"Heroes, NPCs, enemies, items we craft etc should all be a
little unique — obviously we cannot generate on the fly so we need a large collection of
assets that get randomly picked."* Two of four shipped in #503 — heroes and villagers, 128
town bodies behind `GodotClient.ArtVariants` (base id is variant 1; siblings are a contiguous
`-v2`, `-v3`, … run; the pick is FNV-1a over a stable sim id, never `GetHashCode`, which .NET
randomizes per process). **Items and enemies did not.** This finishes them.

**The one measurement that shapes everything below.** An unattended SDXL batch was run and
looked at: 42 candidates over 8 starter recipes, 13 passed every structural screen, and about
**two were the right object** — a cake stand and a lidded urn came back as bucklers, a full
armoured figure as a hauberk. And no automatic screen can rescue it: run any plausible
structural gate over the 48 *shipped* icons and it rejects 16, because `item-kite-shield`
legitimately splits 56/44 into two connected components and `item-engineering-clockwork-glaive`
splits 50/50 — a two-part item is numerically indistinguishable from a two-item concept sheet.
The bottleneck is art direction, not throughput, so U2 below is a **gate, not a step**.

#### Key technical decisions

**KTD-A. The Active master prompt was authored for buildings, and 48 item icons inherit it.**
It reads `"…single subject, one structure centered, 3/4 isometric view…"` and negates
`"multiple buildings"`. *Structure* and *buildings* are architecture words, which is why a
buckler becomes furniture. **The fix is not to edit the shared master prompt** — that would
silently change the meaning of every building, backdrop and venue asset already committed.
Instead `ComposePrompt` splices a **kind-aware clause**, exactly the way it already splices the
palette-family clause (`ArtTrackProfiles.cs:96-102`). `AssetKind.Item` gets an item clause and
item-specific negatives; every other kind composes byte-identically to today, and U1 pins that.

**KTD-B. Monsters get a real generator (owner ruling, 2026-08-14).** The five pixel mine minis
have no build script at all — `art/build/town2d-monster-*.build.json` records
`Status: "unreproducible-legacy"`, no seed, no model, and the repo does not record whether the
original pass was AI or hand-pixel. So there is nothing to vary *from*. Authoring a generator in
`gen_town_sprites.py`'s idiom (ASCII grid + palette ramp + `--check` drift guard) is the same
method that produced 128 exact, zero-curation town bodies. **Accepted cost: it re-authors five
sprites already on screen**, so those monsters change appearance. Pleasant side effect —
`ASSETS.md` §1 currently credits these minis to "Hand-pixel Python", which their own provenance
record contradicts (a rule-8 lie); U4 makes the claim *true* rather than merely correcting it.

**KTD-C. The encounter key is composed adapter-side, so `Contracts` is never touched.**
`DelveStage.ShowMonster` receives only a kind string, and `CombatEvent` carries
`MonsterKind` (a `string`) with no per-encounter id — so "vary each monster" has no key today.
Adding one to `Contracts` would mean a deny-listed micro-PR *and* a save-format risk. Not
needed: the beat already carries `Floor` (`DelveStage.cs:413`), so the key is
`$"{venueId}:{floor}:{kind}"` — deterministic, replay-safe, golden-safe, and it makes the cave
rat on floor 1 a different body from the one on floor 3 while keeping a given floor's monster
stable within a run.

**KTD-D. The Bestiary does not vary.** It is a reference catalogue — one canonical picture per
kind is the correct behaviour there. Variation applies to `DelveStage` and `MineWatch`, the
live-encounter surfaces. Stated because the opposite is an easy and wrong default.

#### Implementation units

**U1 — Make the prompt kind-aware, and prove nothing else moved.** `ComposePrompt` splices an
`AssetKind`-derived clause; `ComposeNegative` gains item-kind negatives (furniture, table,
vase, bowl, candlestick, plinth). Files: `art/GameArt/ArtTrackProfiles.cs`,
`art/GameArt.Tests/ArtTrackProfileTests.cs` (new). Test scenarios: an `Item` spec's prompt
contains the item clause and not `"one structure centered"`; a `Building`/scene spec's composed
prompt is **byte-identical to the string committed today** (pin the literal — this is the guard
against silently re-rolling ~300 existing assets); a kind with no clause of its own falls back
to today's master prompt unchanged. Proof: `dotnet test art/GameArt.Tests` green; no pixels change.

**U2 — The gate: four recipes, rendered, in front of the owner.** Re-render `item-buckler`,
`item-hauberk`, `item-longsword`, `item-round-shield` (the four that failed worst) at two
variants each through `dump-item-specs` → `gen-item-variants.py`. **Commits no pixels.** Proof:
the images themselves, shown to the owner beside the shipped base icon. **If the owner says no,
the wave stops here and U3 is not attempted** — that is the unit's whole purpose. GPU rules
apply (≥14GB free to start, abort >83 °C, one job, owner-granted window).

**U3 — Batch and commit the item pools** *(conditional on U2's yes)*. Two siblings per recipe
across the starter set first, then the rest; regenerate `art-manifest.json`; resample to draw
size offline (never a runtime `Scale` knob). Files: `godot/assets/art/item-*-v*.png`,
`godot/assets/art/art-manifest.json`. **Proof in the running game, not the diff:** a shot-harness
capture of the shop showing two items off the *same recipe* with *different* icons, plus a
`FullPlaytest` run reporting zero art-miss warnings (the logging landed in #497).

**U4 — Author the monster generator.** New monster section in `tools/art/gen_town_sprites.py`
(its own canvas — the existing rig is a fixed 40×64 humanoid and must not be bent), covering
all five mine kinds, with `--check` drift-guarding every committed PNG. Files:
`tools/art/gen_town_sprites.py`, `godot/assets/art/town2d-monster-*.png`,
`art/build/town2d-monster-*.build.json` (status flips off `unreproducible-legacy`),
`godot/tests/TownSpriteArtTests.cs`. Test scenarios: each mini is the pinned size; each has ≥N
distinct opaque colours (no flat placeholder); any two kinds have distinct silhouettes;
`--check` reports zero drift. Proof: a `DelveStage` render showing the new minis in combat.

**U5 — Monster variant pools + the per-encounter pick.** Add variants per kind, then key the
pick on KTD-C's `venue:floor:kind`. Files: `godot/scripts/AssetCatalog.cs`,
`godot/scripts/panels/DelveStage.cs`, `godot/scripts/panels/MineWatch.cs`,
`godot/tests/ArtVariantsTests.cs`. Test scenarios: the same kind on two different floors
resolves to two different body ids; the same kind on the same floor is stable across repeated
resolves *and* across a pool-cache drop (the save/load continuity case); `BestiaryPanel` still
resolves the base id (KTD-D); every committed monster variant has its whole frame set. Proof: a
two-floor `DelveStage` capture showing the same monster kind drawn two ways.

**U6 — Make the docs true.** `ASSETS.md` §1 monster row (now honestly "Hand-pixel Python"),
§1 image count, §4 pipeline-B asset count, and the §1 variation-pools paragraph extended to
items and monsters. Rule 8: this lands *in* the PR that makes it true, never after.

#### Sequencing, risks, and what is deliberately not here

U1 → U2 → U3 is a chain with a human gate in the middle. **U4 → U5 is independent of it** and
can run first or in parallel — nothing in the monster half depends on the item prompt. U6 lands
with whichever PR makes its claims true.

- **Risk: U4 changes art already on screen.** Accepted by the owner in advance. Mitigation:
  the `DelveStage` render in U4 is the check, and it happens before U5 adds any variants.
- **Risk: the U2 gate is skipped under time pressure.** That is the exact failure this plan
  exists to prevent; U3 has no other entry.
- **Risk: engine tests serialize globally.** U4/U5 both touch them — one run at a time, and
  always the full suite (a filtered run cannot see other suites vanish).
- **Not here:** any change to `ArtVariants` itself (it shipped and is proven), the town bodies
  (done), monster *behaviour* (this is presentation only), and `Contracts` (KTD-C avoids it).

#### The rest of the assets — amended 2026-08-14, after U1/U4/U5 landed

An audit of the whole manifest, once the town bodies and monsters had pools:

| family | base ids | pooled |
|---|---|---|
| item icons | 48 | no — U2/U3 above |
| town props / stations / signs / shells / tiles | 48 | no |
| venue art (backdrops, entrances, venue monsters) | 22 | no |
| UI chrome + glyphs | 19 | no |
| hero roster portraits | 6 | no |
| mine monster portraits (painterly) | 5 | no |
| player smith | 1 | no |
| town hero + civilian bodies, mine monster minis | 13 | **yes** |

Three findings from that audit shape the units below more than the counts do.

**1. Three shipped icons are visibly wrong right now.** `item-longsword` renders inside a
decorative card frame; `item-kite-shield` and `item-bulwark` are each *two shields side by side*.
The owner reported all three; they are still live. Same root cause as the cake-stand buckler —
KTD-A's building prefix — so U1's fix is also their fix.

**2. The variation work created an inconsistency.** A hero now has five bodies walking around town
and **one** portrait on their roster card. The person you watched leave for the mine is not the
person on the card.

**3. Everything left that is SDXL is downstream of one gate.** The broken icons, the item pools and
any portrait variants all render through the prompt U1 changed. **None of it runs before U2's
four-recipe sample is on the owner's screen** — that gate is not per-unit, it is per-programme.

**KTD-E. A hero's portrait must pick the SAME index as their body.** `ArtVariants.Pick` returns an
id from a pool, and two pools of different depth resolve one key to different indices — so a hero
could draw body 3 and portrait 1 and read as two different people across surfaces. Two ways out:
keep every per-hero pool at the same depth (5, matching the bodies), or expose the chosen index so
both surfaces share it. **Prefer exposing the index** (`ArtVariants.PickIndex`): pool depth is an
art-supply detail that will drift the first time a family gets a sixth variant, and a rule that
breaks silently on a future art drop is not a rule. Unit U8 owns this.

**KTD-F. Props vary by PLACEMENT, not by kind.** The town lays down 12 trees, 8 lanterns and 2
crates from three sprite ids (`TownLayout2D`), so keying on the id would change nothing — every
tree would still be the same tree. The key is the placement's index in the layout table, which is
fixed data, so a given corner of the map keeps its own tree across sessions.

**U7 — the three broken icons.** Re-render `item-longsword`, `item-kite-shield`, `item-bulwark` on
U1's fixed prompt; curate to one good result each. Depends on U2. Files:
`godot/assets/art/item-{longsword,kite-shield,bulwark}.png`, `art/build/*.build.json`,
`godot/assets/art/art-manifest.json`. Test scenarios: each id still resolves through
`AssetResolutionCensusTests`; the committed PNG is a single connected subject (the two-shield defect
is machine-detectable — a second blob at 44% of the opaque area is what `item-kite-shield` has
today); icon file size stays at draw size. Proof: a shop capture showing all three, beside the
current ones.

**U8 — hero roster portraits vary with the body.** Four extra portraits per class (24 renders),
picked by the same `HeroId` and the same index as the town body per KTD-E. Depends on U2 **and** an
explicit owner look — hero faces are the most curation-sensitive art in the game, and a bad one is
worse than a repeated one. Files: `godot/scripts/ArtVariants.cs` (add `PickIndex`),
`godot/scripts/AssetCatalog.cs`, `godot/tests/ArtVariantsTests.cs`, `art/specs/heroes/HeroSpecs.cs`,
`godot/assets/art/hero-*-v*.png`. Test scenarios: a hero's portrait index equals their body index
for every hero id 1-12 across all six classes; a class whose portrait pool is shallower than its
body pool still resolves (no index overrun); `HeroesPanel`, `TavernPanel` and `LedgerModal` all
resolve committed art. Proof: a roster capture beside a town capture of the same hero.

**U9 — prop and clutter pools.** Procedural, no GPU: author `prop-tree`, `prop-lantern`,
`prop-crate` variants in the `gen_town_sprites.py` idiom the monsters now use, then pick by
placement index (KTD-F). These three ids have **no generator today** — only `props-town-well` has a
build record — so this unit authors them the same way U4 authored the monsters, and inherits U4's
lesson: write full-width rows, never `mirror()` on a padded half. Files:
`tools/art/gen_town_sprites.py`, `godot/scripts/town2d/Town2D.cs`,
`godot/scripts/town2d/TownAssets2D.cs`, `godot/tests/TownSpriteArtTests.cs`,
`art/build/town2d-prop-*.build.json`. Test scenarios: 12 tree placements draw ≥3 distinct sprites;
the same placement index is stable across a pool-cache drop; every committed prop variant is a
single connected subject at the pinned size; `--check` reports zero drift. Proof: a town capture
where the tree line is no longer twelve copies.

**U10 — make the docs true.** `ASSETS.md` §1 counts and the variation-pools paragraph; the
`Mine monster portraits (painterly)` row is now a *fallback* path only (`DelveStage` reaches it
solely when pixel art is missing, which no longer happens) — say so rather than implying it is a
live surface. Lands in whichever PR makes its claims true.

**Sequencing.** U9 is independent — no GPU, no gate, can run first. U7 → U8 both wait on U2. U10
rides along. **Deliberately not here:** signs, building shells, ground tiles, backdrops and UI
chrome. Those are identity and chrome rather than cast; varying a signboard is noise that costs
legibility, and the owner's ask named heroes, NPCs, enemies and crafted items.

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
- **Asset ledger:** `docs/design/ASSETS.md` — every image, animation, sound and voice line, what
  draws or plays it, and what is orphaned or missing. (The former `docs/registry/SYSTEMS.md` /
  `CONTENT.md` ledgers cited here never existed in this tree; reference removed 2026-08-12.)

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

## Appendix A — Mechanics Ground Truth (derived from source, 2026-08-06; §1 reachability refreshed 2026-08-07)

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
| 21 | `UpgradeForgeAction` | Buy the next forge tier: 400/1600/6400/25600 g + 25 floor-ore (`Economy/ForgeTierHandlers.cs:46-54`) | Morning ONLY (`ForgeTierHandlers.cs:61-62`) | **Bell** (`ActionTiming.cs:125`) | Yes (`:99,113`) | ForgePanel Foundry section (`ForgePanel.cs:1253`); bell-rider, tray vocab `PendingVerbVocab.cs:31,41` |
| 22 | `BuyForgeSupplyAction` | Buy coal (4 g) / flux (40 g) (`Economy/ForgeSupplyHandlers.cs:37-42`) | Morning ONLY (`ForgeSupplyHandlers.cs:44-45`) | Now | Yes (`:91`) | ForgePanel Foundry rows (`ForgePanel.cs:1263`) |
| 23 | `MasterworkAttemptAction` | 3 coal + 1 flux + 100 g×tier + materials → guaranteed Superior (Masterwork if material outgrades recipe); requires Forge Tier II; ZERO RNG (`Economy/MasterworkAttemptHandlers.cs:30-158`) | ALL (`MasterworkAttemptHandlers.cs:47`) | Now | Yes (`:126,152`) | ForgePanel recipe card (`ForgePanel.cs:1277`); instant, so no tray entry needed |
| 24 | `CommissionLegendaryWorkAction` | 3000 g×tier + 2× materials → guaranteed Masterwork; capped 4/campaign (`Economy/LegendaryCommissionHandlers.cs:23-38`) | ALL (`LegendaryCommissionHandlers.cs:40`) | **Bell** (`ActionTiming.cs:127`) | Yes (`:127`) | ForgePanel recipe card (`ForgePanel.cs:1293`); bell-rider, tray vocab `PendingVerbVocab.cs:33,43` |

**Reachability verdict (updated 2026-08-07).** **All 24 actions are reachable from the Godot client.** The four Phase-D gold sinks (#21-24) were surfaced in the reachability wave; the table above records where each now lives. This is no longer checked by reading — `godot/tests/ActionReachabilityCensusTests.cs` reflection-enumerates every concrete `PlayerAction` and fails BY NAME on any that has neither a named surface nor a pinned exclusion with a reason. Its exclusions map is currently empty. Note the census is a *decision* census: it proves a surfacing decision was recorded for every action, not that any given button is clickable — that proof lives in the `PressEnabled` tests in `ForgeCraftTests`, `LegendsWallTests` and `LedgerModalTests`.

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

---

# §11.11 — P4 came back: the game never shows you the next beat

**Status: §11.4 path work, not overhead.** This is P4's return. The owner sat down, played, and
wrote notes — that is the human feel-test §11.4 has carried as OPEN since 2026-08-07, and §11.6
rule 5 (the measurement rule) says the plan amends in the same PR that lands the finding. Every
unit below traces to a note in his own words. Written as a §11 amendment in §11.10's shape
because the `docs/plans/` two-doc cap is full (`2026-08-13-002` and `2026-08-14-001` both hold
live units) and rule 4 does not permit a third.

---

## The one thing that is wrong

Read as a list, the notes look like eight separate complaints. Read against the code, they are
one.

**Nothing in this game is uncertain, and the client draws only the present tense.** The sim
already knows the next beat and can prove it. Which hero walks up to the counter is
`state.Heroes` ordered by relationship band then `HeroId`
(`sim/GameSim/Counter/CounterHandlers.cs:58-69`). What they ask for is
`RaidForecast.MissingItemSlots(hero.Gear)[0]` — a pure function of gear
(`godot/scripts/ui/CustomerVoice.cs:40-43`, `sim/GameSim/Heroes/RaidForecast.cs:85-104`). Which
three heroes march tomorrow is a pure function of the roster
(`sim/GameSim/Heroes/PartyFormation.cs:33-48`). Whether Camp will have a decision in it depends
on `CheckpointFor(targetFloor) = min(1, targetFloor - 1)`
(`sim/GameSim/Expedition/ExpeditionSystem.cs:26-31`) — knowable at the Morning bell. None of it
is hidden by design; all of it is simply undrawn until the tick that consumes it.

So the player is permanently answering a question they were never shown coming. A hero asks for
a shield the shelf cannot supply. A phase called Vigil ends 1.0 seconds after it begins with
nothing in it. A tutorial step names a verb whose precondition the player does not control. A
hero dies on a day the player had no reason to be watching that hero. Every one of those is the
same failure: *the game states its outcome and never staged its antecedent.*

This is what the owner meant by naming **Dungeon Bodega Simulator**. That game's whole strategy
layer is one day of lead time — read tomorrow's demand, then farm/brew/order against it
(`docs/design/DB_GAMEPLAY_LOOP.md` §1d, §5.2-5.3; the demand-rotation claim traces to the dev
site and itch page, *not* to the Steam store bullets, which say only "different demands each
day"). Its shopkeeping is not deeper than ours. It is *earlier*. Preparation is the verb, and
preparation is impossible without lead time.

Two things follow, and they set this wave's shape:

1. **Most of these are not missing mechanisms.** The pointing overlay exists
   (`godot/scripts/ui/TutorialOverlay.cs`). The demand data exists
   (`RaidForecast.ForTomorrow`, `DemandBoard.DepthStalls`). The vigil craft-and-send chain exists
   and has a button (`godot/scripts/panels/CampPanel.cs:371-375`). The narrator's loss-count fix
   already landed and is wired (`NarratorVoiceDirector.ChooseLine(..., losses)`,
   `godot/scripts/MainUi.cs:686`). The repo's named failure is rebuilding what the kernel already
   owns. This wave connects and stages; it builds new mechanism in exactly three places, each
   named.
2. **Law 4 ("show only what the sim decided") is a boundary here, not an obstacle.** Tomorrow's
   muster is *decided* — `RaidForecast.ForTomorrow` byte-matches what the Expedition tick forms
   (`sim/GameSim/Heroes/RaidForecast.cs:26-29`). Whether a party survives stage 1 to reach the
   camp is *not* decided — those rolls have not happened. So the telegraph tells the player the
   rule and the stake, never the outcome. That line is drawn explicitly in every unit below, and
   §11.4's own design note already binds it: stakes qualitatively, never survival percentages.

---

## Implementation units

Six units. Ordered so U1 and U2 can go in parallel (different files), U3 depends on U2's registry
work, and U5/U6 each carry a ruling that must land before their sim half ships.

---

### U1 — Tomorrow's asks, in front of tonight's shelf

**Goal.** The player can know, before the day they must act on it, which hero will walk up to the
counter and what they will ask for — and can act on it in one click. Closes *"Counter service
doesn't make sense — how does the player KNOW to make a shield?"*

**Serves: link2** — the counter is one of the four honest channels, and a channel that asks for
what the player provably could not have anticipated is not honest, it is a slot machine.

**Mechanism verdict: exists and is illegible, with one seam missing.**

The evidence, precisely:

- The counter's want is `MissingItemSlots(hero.Gear)[0]` with a fixed Weapon/Shield/Armor order
  (`godot/scripts/ui/CustomerVoice.cs:40-43`, `sim/GameSim/Heroes/RaidForecast.cs:85-104`).
- The *same* function already drives the day-end forecast board's gear-gap lines —
  "Torvald: no shield" — rendered at `godot/scripts/panels/RaidForecastBoard.cs:86-96` from
  `RaidForecast.ForTomorrow` (`sim/GameSim/Heroes/RaidForecast.cs:59-68`), and that board
  auto-opens chained off the Evening ledger (`godot/scripts/MainUi.cs:670`).
- So the answer to "how would I know?" is *already on screen the night before*, in a modal that
  closes and is never referred to again, on a board whose reopen control is a wordless tray icon
  (`godot/scripts/MainUi.cs:2285`).
- The advisor **will** say "craft a shield for Torvald" — but only once he is a `DepthStall`,
  which requires `state.Day - lastRecordDay >= StallThresholdDays` where
  `StallThresholdDays = 2` (`sim/GameSim/Drama/DemandBoard.cs:84,203-216`;
  `sim/GameSim/Advisor/ObjectiveAdvisor.cs:235-249`). On day 2, when the tutorial orders the
  player to open the counter, that predicate is false for everyone. The advisor is silent
  precisely when the counter first asks.
- The genuinely missing seam: nothing anywhere projects *the counter queue itself*. `ApplyOpen`
  computes the queue inside the handler and throws the projection away
  (`sim/GameSim/Counter/CounterHandlers.cs:58-70`).

**Files.**

- *create* `sim/GameSim/Drama/CounterForecast.cs` — pure projection: the ordered queue
  `ApplyOpen` would build, each entry carrying hero, the want slot `MissingItemSlots` reports (or
  the upgrade slot, mirroring `CustomerVoice.WantLine`'s second branch), and the hero's gold.
- *modify* `sim/GameSim/Counter/CounterHandlers.cs` — `ApplyOpen` calls `CounterForecast.Queue`
  instead of inlining the ordering, so the projection and the handler cannot drift. Behaviour
  identical; no new draw, no new state field.
- *modify* `godot/scripts/ui/CustomerVoice.cs` — `WantLine` reads the same projection.
- *modify* `godot/scripts/panels/RaidForecastBoard.cs` — a "TOMORROW AT THE COUNTER" section
  above the muster sections, and per-gap a **"Forge one"** button that closes the board and opens
  the forge preloaded with the answering recipe (§11.7.4's "one-click path from a gap line to the
  forge" — the direction is already ruled, this is its first instance).
- *modify* `godot/scripts/panels/ShopPanel.cs` — a persistent one-line header above the shelf
  while Morning: *"First at the counter: Torvald — wants a shield, 52g on him."* Read-only, no
  verb; the verb is the counter button already there.
- *test* `sim/GameSim.Tests/Counter/CounterForecastTests.cs`
- *test* `godot/tests/panels/RaidForecastBoardTests.cs` (extend)
- *test* `godot/tests/panels/ShopPanelTests.cs` (extend)

**Approach.** Extract, do not invent. `CounterForecast.Queue(GameState)` returns exactly the list
`ApplyOpen` builds today; `CounterForecast.Wants(hero, state)` returns exactly the slot
`CustomerVoice.WantLine` picks today. Both are pure, allocation-only, no RNG argument, no clock
— so the counter cannot ask for something the forecast did not name, by construction rather than
by two tests agreeing. The Godot side renders it in two places and adds one navigation verb.

**Determinism.** Sim change is a pure refactor: same enumeration order, same comparator, zero
RNG draws added or moved, no `Math.*`. `ApplyOpen` still emits `CustomerApproached` at the same
point. Golden replay unaffected; **no re-baseline**.

**Patterns to follow.**

- `sim/GameSim/Heroes/RaidForecast.cs` — the exemplar for "a pure projection that byte-matches
  what the tick will do", including its own doc's promise at lines 26-29. Copy that contract
  verbatim into `CounterForecast`'s class doc.
- `sim/GameSim/Drama/DemandBoard.cs` — the read-model shape and the `Snapshot` idiom.
- `godot/scripts/panels/DemandPanel.cs` — the read-only `SimPanel`/`Section`/`Card` idiom,
  including `AddWrappingRow` for chip rows.
- `godot/scripts/panels/CampPanel.cs:85,371-375` — the exact
  `event Action? OpenForgeRequested` → `MainUi` → `OpenPanel("Forge")` shape the "Forge one"
  button must reuse rather than reinvent.

**Test scenarios.**

1. `Queue_MatchesApplyOpensOwnOrdering_AcrossRelationshipBands` — build a state where band order
   and `HeroId` order disagree; assert the projection's head equals the `CustomerApproached` hero
   after a real `OpenCounterAction` tick.
2. `Wants_MatchesCustomerVoiceWantLine_ForEveryGearShape` — parameterised over all eight
   weapon/shield/armor null combinations; assert the projected slot appears in the rendered want
   line.
3. `Wants_FullLoadoutHero_NamesTheShelfUpgradeSlot_OrBrowsing` — covers `WantLine`'s second and
   third branches so the forecast never claims a want the sim would refuse.
4. `Queue_IsEmpty_WhenNoHeroIsAlive` — the `Active == null` case; the board renders an explicit
   line, never a blank section.
5. `ForecastBoard_RendersCounterSection_WithForgeOneButton_ForAGapThatARecipeCanFill`
6. `ForecastBoard_ForgeOneButton_IsAbsent_WhenNoSelectedProfessionHasARecipeForThatSlot` — never
   a dead click.
7. `ShopPanel_CounterHeader_NamesTheSameHeroTheCounterThenSeats`
8. `ShopPanel_CounterHeader_IsAbsentOutsideMorning` — mirrors
   `ActionLegality`'s Morning-only gate (`sim/GameSim/Advisor/ActionLegality.cs:67`) rather than
   asserting its own.

**Verification.** `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance`
green; `dotnet test godot/tests --settings .runsettings` green with `Failed: 0` and passed count
at or above the `ENGINE_MIN_PASSED=300` floor, quoted from the raw log. Golden-replay test green
with no re-record. Manual: day 1 evening, the board names tomorrow's first customer and their
slot; day 2 morning, that hero is the one seated.

---

### U2 — The tutorial points at the world, never asks for what the player cannot cause, and stays available

**Goal.** Close all four tutorial notes: *"Tutorials not great"* (highlights, not text cards),
*"Tutorial 6 doesn't make sense"*, *"Tutorial 7 makes no sense"*, *"Need guided/repeated
tutorial"*.

**Serves: link1** — a player who cannot reliably reach the anvil never makes the thing the whole
chain keys on.

**Mechanism verdict: the pointing exists and is starved; the two broken steps are a gating
defect, not a copy defect; the repeat is genuinely missing.**

#### Why "not great" is not a copy problem

The highlight the owner is asking for **already ships**. `TutorialOverlay`
(`godot/scripts/ui/TutorialOverlay.cs:36-134`) pulses the real `Building2D` sprite in world space
for a `Building` anchor and draws a pulsing screen-space outline for a `Hud` anchor, resolved by
name, throwing rather than pointing at nothing. That is the right mechanism.

What starves it is where the *teaching* lives. The objective card renders the step text
unclamped at a six-line budget (`godot/scripts/ui/ObjectiveTracker.cs:52-67`,
`TutorialMaxLines = 6`, ~127px), because each step's copy must carry walk-there instructions,
the live advisor reason, the control's exact label, and the gesture — e.g. Tutorial 8's
*"Evening. The **EVENING LEDGER** opens itself — press **Buy** under **ORE OFFERED**, then close
it and press **Snuff the lanterns** at the top of the screen."*
(`godot/scripts/ui/TutorialFlow.cs:581-583`). Meanwhile the `TeachNote` — the "what this
mechanism actually is" paragraph — renders inside a checklist whose scroll window is
`ChecklistMaxHeight = 32f` pixels, and that constant's own doc concedes it: *"a peek-and-scroll
sliver, not a several-row window, is what fits"* (`godot/scripts/ui/ObjectiveTracker.cs:78-93`).

So: the game highlights a *building*, and puts everything else in a 127px card above a 32px
sliver. That is the text-card complaint, exactly.

The fix is to move load off the card and onto the world. `Town2D` already re-emits
`StationActivated` carrying a whole `InteriorLayout2D.StationSpec` with `Action`, `Focus`,
`HoverLine` and `FlavorLine`, and `WorldInputNode.Configure(Player, room.Stations)` already scans
for the nearest in-range station to highlight (`godot/scripts/town2d/Town2D.cs:159-170,683`). The
anchor vocabulary is a three-value enum (`godot/scripts/ui/TutorialFlow.cs:54-59`). Adding a
fourth, `Station`, lets a step point at the anvil rather than at the building containing the
anvil — which is where "press **E** at a station" currently lives as a sentence.

#### Tutorial 6 — what it says, why it misreads

`TutorialStep.OpenCounter`, `DisplayIndex: 6`, `MinDay: 2`
(`godot/scripts/ui/TutorialFlow.cs:296-302`). Rendered copy
(`godot/scripts/ui/TutorialFlow.cs:570-573`):

> *"Tutorial 6/10: Walk to the **Shop** and press **E**, or click it — press **Open Counter** at
> the top of the Shop panel, then **Present** a shelved item and answer with **Accept**, **Hold
> Firm**, or **Counter**."*

Completion fact: `state.EventLog.OfType<CounterSaleClosed>().Any()`
(`godot/scripts/ui/TutorialFlow.cs:301`).

**Why it misreads.** Every clause is accurate and the step is still unfollowable, because it
demands an outcome the player cannot cause. The customer states their want *first*
(`CustomerVoice.WantLine`), and the want is `MissingItemSlots[0]`. On day 2 the player has
crafted one or two things from a starter kit against no signal at all. The owner's own case —
a hero asking for a shield ~50g while the shelf holds a Chain Vest and a Field Salve — is not
bad luck; it is the modal case. `ShoppingAi.EvaluateItem` returns Pass, `CustomerWalked` fires,
`CounterSaleClosed` never does, and the step sits on screen repeating the instruction. The step
is written as "perform this verb" when the sim's contract is "this verb *may* produce a sale."

**What replaces it.** Two changes, in this order:

1. Re-scope the step to what the player actually controls: **open the counter and answer the
   customer** — completion on `CustomerApproached` plus any one of
   `PresentItemAction`/`SuggestItemAction`/`HaggleResponseAction`/`CloseCounterAction` in
   `state.ActionLog`. A closed sale becomes a *bonus* the copy names, not the gate.
2. Copy that names the anticipation, now that U1 makes anticipation possible:
   > *"Tutorial 6/10: The **Shop** — press **Open Counter**. Whoever's first in line tells you
   > what they want before you show them anything; the Forecast board named them last night. Show
   > them something, or hear them out and close the counter. A hero who walks is a real answer,
   > not a mistake."*

That last sentence is load-bearing and is the honest register: it tells the player the failure
mode is legal. Depends on U1 for the "named them last night" clause to be true.

#### Tutorial 7 — what it says, why it misreads

`TutorialStep.Vigil`, `DisplayIndex: 7`, `MinDay: 2`, anchored to HUD control `CampCard`
(`godot/scripts/ui/TutorialFlow.cs:303-310`). Rendered copy
(`godot/scripts/ui/TutorialFlow.cs:578-580`):

> *"Tutorial 7/10: When they camp, a card fills the screen. Pick a supply and press **Send**, or
> press **Recall** to bring them home."*

Day-gated variant (`godot/scripts/ui/TutorialFlow.cs:732-733`):

> *"Tutorial 7/10: The vigil is a Day 2 lesson — nothing to do here yet; it opens once Day 2
> begins."*

Completion fact: `SupplyDelivered` or `PartyRecalled`
(`godot/scripts/ui/TutorialFlow.cs:309`).

**Why it misreads.** Three compounding defects, all worse than Tutorial 6's:

- **The precondition is a coin flip the player cannot influence.** A party camps only if
  `CheckpointFor(targetFloor) >= 1`, i.e. `targetFloor >= 2`
  (`sim/GameSim/Expedition/ExpeditionSystem.cs:26-31,88-108`), *and* they clear stage 1 cleanly.
  `RaidConductor`'s own class doc states the consequence: *"Beat.VigilStop is the UNCOMMON case,
  not the common one"* (`godot/scripts/RaidConductor.cs:28-32`).
- **Its two completion verbs are the two the design does not want reflexively taken.** `Send` is
  the verb R1 (§11.3) has frozen as measured-harmful at scale; `Recall` aborts the run. The third
  verb — "Send them deeper", the only one that *ends* the vigil
  (`godot/scripts/panels/CampPanel.cs:404-409`) — does not satisfy the step. The tutorial is
  teaching a habit the plan is actively unsure it wants.
- **The gating text lies by omission.** "It opens once Day 2 begins" states a day gate for
  something that is not day-gated. A day-2 party that wipes at floor 1, or targets floor 1 at
  all, gives the player a Day 2 that never opens it. The step then rides the `BackstopDay = 4`
  unconditional close (`godot/scripts/ui/TutorialFlow.cs:907-910,928`) — i.e. the player is told
  a lesson is coming, it silently never comes, and the chain moves on.

**What replaces it.** The step stops being *"do the vigil verb"* and becomes *"understand what
the stop is and that it waits."* Completion on **seeing the camp card at all**
(`CampPanel.ShowModal` notifies, same shape as `NotifyMirrorOpened` at
`godot/scripts/ui/TutorialFlow.cs:937-944`) **or** any of the three camp verbs including Send
Deeper. Gating note becomes conditional, not day-based:

- when no party is staged today: *"Nothing to teach here today — the party is only going one
  floor down, so they won't stop. It fires on a run that's aiming deeper."*
- when a party is staged: *"They'll stop below the checkpoint if they get there clean. When they
  do, the world waits — there is no clock on it."*

The first line is honest and is only writable because the sim already knows: `MusterPlan.Compute`
gives every party's `TargetFloor` at the Morning bell.

#### Repeatability

Genuinely missing. `Step` never regresses (`godot/scripts/ui/TutorialFlow.cs:354-356`),
`Complete()` and `Dismiss()` both set a permanent `user://` flag
(`godot/scripts/ui/TutorialFlow.cs:981-986,1011-1015`), `Checklist()` returns empty once
`Active` is false (`godot/scripts/ui/TutorialFlow.cs:818-823`), and `TeachNote` renders only on
the current row (`godot/scripts/ui/TutorialFlow.cs:842`). Ten authored teaching paragraphs, each
readable for a few minutes once per campaign, in a 32px window. Add a **Lessons** book: the
existing tray idiom, rendering all ten `Registry` rows' `ShortLabel` + `TeachNote` at full
height, readable forever, with the current row marked. Zero new copy — the paragraphs are
already written and already pinned non-empty by `TutorialRegistryConformanceTests`.

**Files.**

- *modify* `godot/scripts/ui/TutorialFlow.cs` — add `TutorialAnchorKind.Station`; re-point
  `BuyMaterial`/`Craft` at their stations; rewrite steps 6 and 7 (rows, `IsDone`, `StepText`,
  `WaitText`, `GatingNote`); add `NotifyCampCardShown`.
- *modify* `godot/scripts/ui/TutorialOverlay.cs` — resolve a `Station` anchor through
  `Town2D`'s room stations, same eager-resolve-or-throw contract as the `Hud` branch.
- *modify* `godot/scripts/town2d/Town2D.cs` — expose station lookup by id for the overlay
  (read-only; no change to `StationActivated`).
- *create* `godot/scripts/panels/LessonsPanel.cs` — the Lessons book.
- *modify* `godot/scripts/MainUi.cs` — register `"Lessons"`, add the tray button, wire
  `NotifyCampCardShown` from the existing `SyncCampModal` hook.
- *modify* `godot/scripts/ui/ObjectiveTracker.cs` — with load moved to stations and the Lessons
  book, drop `TutorialMaxLines` from 6 toward 3 and reclaim the height for the checklist.
- *test* `godot/tests/TutorialRegistryConformanceTests.cs` (extend — every `Station` anchor must
  resolve against a real room)
- *test* `godot/tests/TutorialCopyIsFollowableTests.cs` (extend)
- *test* `godot/tests/panels/LessonsPanelTests.cs`
- *test* `godot/tests/ui/TutorialOverlayTests.cs` (extend)

**Approach.** Registry-first, exactly as U5 of the loop-legibility plan established: every step
fact is a `TutorialStepDef` row, and the conformance suite resolves every anchor against the real
layout. The new `Station` kind must obey the same house rule the class doc already states — an
anchor that cannot resolve fails loudly, never points at nothing
(`godot/scripts/ui/TutorialFlow.cs:45-59`, `godot/scripts/ui/TutorialOverlay.cs:25-34`).

**Determinism / purity.** Godot-side only. `TutorialFlow` reads `GameState` and never mutates it;
`user://tutorial_flow.json` stays out of the sim save (KTD2). No sim diff, no re-baseline.

**Patterns to follow.**

- `godot/scripts/ui/TutorialFlow.cs:225-338` — the registry array; add rows, never a parallel
  structure.
- `godot/scripts/ui/TutorialOverlay.cs:114-133` — the `switch (anchor.Kind)` eager resolve.
- `godot/scripts/panels/DemandPanel.cs` — the read-only book panel idiom for `LessonsPanel`.
- `godot/scripts/MainUi.cs:2279-2319` — the tray-button registration shape.

**Test scenarios.**

1. `EveryStationAnchor_ResolvesAgainstARealRoomStation` — conformance, all rows.
2. `Step6_Completes_WhenTheCustomerWalks_WithoutASale` — the exact case that stalled the owner.
3. `Step6_AlsoCompletes_OnAClosedSale` — the happy path still works.
4. `Step7_Completes_OnSeeingTheCampCard_WithNoVerbPressed`
5. `Step7_Completes_OnSendDeeper` — the verb the plan is comfortable teaching.
6. `Step7_GatingNote_SaysNoPartyIsStaged_WhenEveryPartyTargetsFloor1`
7. `Step7_NeverClaimsADayGate_ForAConditionThatIsNotDayGated` — regression pin on the exact
   defect.
8. `LessonsPanel_RendersAllTenTeachNotes_AfterTheChainIsComplete`
9. `LessonsPanel_RendersAllTenTeachNotes_AfterDismiss` — a dismiss must not destroy the lessons.
10. `ObjectiveCard_HeightStaysWithinTheExisting260pxPin` — `HudBoundsTests`' pin is never
    relaxed.

**Verification.** Full engine suite, raw `Failed: N, Passed: N` quoted. Manual: start a new
campaign, confirm the anvil itself pulses on step 1; force a day-2 counter with a Pass verdict
and confirm step 6 advances; run a day where every party targets floor 1 and confirm step 7's
note says so.

---

### U3 — The town opens as you learn it

**Goal.** Content gates open as the player earns them rather than all at once on day 1. Closes
*"Features unlocked as you go."*

**Serves: link2** — the four channels arrive one at a time, so each is learned as a channel
rather than as one of seven wordless icons.

**Mechanism verdict: missing.** There is exactly one feature gate in the entire client:
`QuickTravelUnlocked => Completed` (`godot/scripts/ui/TutorialFlow.cs:376-377`). All seven tray
books — Ledger, Forecast, Commissions, Legends, Demand, Renown, Progress — are constructed and
visible on day 1 (`godot/scripts/MainUi.cs:2279-2319`), every one of them a wordless icon whose
name lives only in a tooltip, a fact the tutorial's own step-9 copy has to apologise for:
*"the tray's buttons carry no words, only icons and tooltips"*
(`godot/scripts/ui/TutorialFlow.cs:320-324`).

**Approach.** A single ordered unlock table in one place, keyed on durable sim facts the player
caused, in the same registry idiom as `TutorialFlow.Registry`. Recommended set, each gate being
the moment the surface first has anything true to say:

| Surface | Opens on | Why that moment |
|---|---|---|
| Ledger | first `PartyDeparted` | nothing came home yet |
| Forecast | first Evening reached | it forecasts *tomorrow* |
| Renown | first `ItemSold` to a hero | a stranger becomes a customer |
| Commissions | first commission posted by the sim | an empty board teaches nothing |
| Demand | first `HeroPassedOnItem` | the board's lead section *is* pass reasons |
| Legends | first `AttributionBeatEvent` | it is link 4's own reader |
| Progress | first `BountyPaid` | the existing second-profession milestone |

Each unlock fires a one-line town-voiced arrival toast in the existing register, not a modal.
`Progress` reuses `TutorialFlow.SecondProfessionMilestoneReached`
(`godot/scripts/ui/TutorialFlow.cs:1019`) rather than defining a second notion of the same
milestone.

**The constraint this unit must not break.** `godot/tests/ActionReachabilityCensusTests.cs` fails
by name on any `PlayerAction` without a recorded surface. A gated surface is still a *recorded*
surface — the census is a decision census, not a liveness proof (its own doc, lines 32-41) — so
each gated entry must carry its gate in the surface string, e.g.
`"HeroCards (opens on first sale to a hero)"`. See KTD-3.

**Files.**

- *create* `godot/scripts/ui/SurfaceUnlocks.cs` — the table plus a pure
  `IsOpen(GameState, string surfaceId)`.
- *modify* `godot/scripts/MainUi.cs` — tray buttons consult it every `RefreshHud`; `OpenPanel`
  refuses a closed surface with the arrival copy rather than a silent no-op.
- *modify* `godot/tests/ActionReachabilityCensusTests.cs` — surface strings carry their gate.
- *create* `godot/scripts/ui/SurfaceUnlockToast.cs` or reuse the existing toast path in
  `MainUi` (prefer reuse — check `OnStationActivated`'s flavor-toast route first).
- *test* `godot/tests/ui/SurfaceUnlocksTests.cs`
- *test* `godot/tests/MainUiTests.cs` (extend)

**Determinism.** Client-only. `IsOpen` reads `GameState.EventLog` and mutates nothing; no
`user://` persistence at all, so a reload re-derives every gate from the campaign itself.
**No sim diff, no re-baseline.**

**Test scenarios.**

1. `EverySurfaceInTheTray_HasAGateOrAnExplicitAlwaysOpen` — deny-by-default, the census idiom.
2. `Ledger_IsClosed_BeforeTheFirstDeparture_AndOpen_After`
3. `Legends_IsClosed_UntilTheFirstAttributionBeat`
4. `AGatedSurface_ReDerivesItsGate_AfterAReload` — no persistence to go stale.
5. `NoGate_EverClosesASurfaceItPreviouslyOpened` — monotonic, one-way, same ratchet discipline
   as `TutorialFlow.Step`.
6. `ReachabilityCensus_IsGreen_WithEveryGatedSurfaceNamingItsGate`
7. `EveryTutorialStepAnchor_PointsAtASurfaceThatIsOpenByThatStepsMinDay` — the trap this unit
   could create: a gate that hides a tutorial target. Pin it.

**Verification.** Engine suite green with the census passing. Manual: fresh campaign, confirm the
tray starts near-empty and each book arrives with its line.

---

### U4 — The vigil is a workbench, and the day says what happens next

**Goal.** Close *"What do we DO during vigil"* and *"Jumped straight from Vigil to Night."*

**Serves: link2** — the vigil runner is the fourth channel, and a channel the player cannot
stock is not a channel.

**Mechanism verdict: the chain exists and dead-ends; the phase jump is real and is a copy
absence, not a timing bug.**

**What the vigil already has.** Four verbs, not zero: Send a supply, Recall, Send them deeper,
and — since the hero-facing-day H1 work — **"Forge something for them"**, a real button that
closes the slate and opens the forge, with the hint *"Nothing to send yet? You can leave this
stop, work the forge, and come back — the vigil holds until you answer it"*
(`godot/scripts/panels/CampPanel.cs:363-375`). The slate also states floors and monsters still
ahead, per-hero hp, heals left, and "of which yours: N"
(`godot/scripts/panels/CampPanel.cs:177-241`). `CraftAction` carries no phase gate at all
(`sim/GameSim/Advisor/ActionLegality.cs:52`), so crafting mid-vigil is genuinely legal.

**Where it dead-ends.** `CraftLegal` requires both materials in hand and
`state.ActionSlotsRemaining > 0` (`sim/GameSim/Advisor/ActionLegality.cs:394-403`), and
`BuyMaterialAction` is Morning-only (`sim/GameSim/Advisor/ActionLegality.cs:57`). A player who
presses "Forge something for them" at the vigil having spent their day arrives at a forge that
can make nothing, with no on-screen explanation, and must walk back. The verb is discoverable
and frequently unusable — which reads as "there is nothing to do here" more convincingly than
having no button at all.

**Where the jump comes from.** `GameKernel` always runs Expedition → Camp → ExpeditionDeep →
Evening (`sim/GameSim/Kernel/GameKernel.cs:193-194`); Camp is never skipped. But
`RaidConductor.BeatFor` maps `DayPhase.Camp` to `Beat.DeepTick` whenever `state.InFlight` is
empty (`godot/scripts/RaidConductor.cs:180`), and an empty beat runs
`EmptyBeatSeconds = 1.0`, then the deep show `DeepShowSeconds = 3.0`
(`godot/scripts/RaidConductor.cs:71-78`). So on every day where nobody parks — the majority, per
that class's own doc at lines 28-32 — the clock reads *Vigil*, four seconds pass, and it reads
*Night*. The pacing is correct and intentional. **The absence is that nothing ever says a vigil
is or is not coming.** The player experiences a phase that appeared to be skipped.

**Approach.** Three changes, none of which touch a camp rule (see the ruling below).

1. **The vigil announces itself at the send-off.** §11.7.4 already asks for exactly this — *"one
   line of conductor copy when a vigil is coming, so crafting during the march stops being tribal
   knowledge."* At the Morning bell the sim knows every party's `TargetFloor`
   (`MusterPlan.Compute`), so the honest lines are:
   - staged: *"Nordri's three are aiming for floor 3. They'll stop at the checkpoint if they get
     there clean — that stop has no clock, and the forge stays open through it."*
   - unstaged: *"Everyone's going one floor down today. No checkpoint, no stop — they'll be back
     by dark."*
   This states the **rule**, never the outcome. Stage-1 rolls have not happened, and the copy
   must not imply they have.
2. **The vigil's forge trip cannot dead-end silently.** Before opening the forge, the camp slate
   reports which of the two blockers is live and what it means, using the same
   mirror-the-kernel discipline `GateButton` already uses at
   `godot/scripts/panels/CampPanel.cs:232-241` — e.g. *"No slots left today — the anvil's cold
   until dawn"* / *"Nothing on the rack to work with; the vendor opens at dawn."* A player who
   knows why can decide; a player who walks into a cold forge concludes the vigil is empty.
3. **The Camp phase never renders as an unexplained gap.** When `InFlight` is empty at Camp, the
   clock band shows one line — *"Nobody camped tonight — they're pushing straight through"* —
   instead of a phase label that changes silently. No timer change.

**What this unit deliberately does not do — and the ruling it needs.**

§11.4 states plainly: *"Until R1 lands, no further vigil work ships."* That freeze is about the
provisioning **balance** (P5, §9.9). This unit ships none of it: no change to the runner fee, the
checkpoint depth, the send threshold, the quaff rule, or the set of camp verbs. It is copy and
gating legibility over verbs that already exist.

But the obvious fourth fix — **letting the player buy materials during Camp** — *is* vigil work
in R1's sense: it materially raises how often a party gets provisioned, which is the exact axis
R1 is frozen on. **This is routed as an owner ruling, not taken.**

> **Ruling asked (R7): may `BuyMaterialAction` become legal during `DayPhase.Camp`?**
> Recommended default: **no**, for this wave. The vigil's scarcity is what makes the morning's
> "craft one extra salve" decision real, and unlocking mid-vigil supply would pre-empt R1's own
> question. Cost of "no": the vigil forge trip only works for a player who kept materials and a
> slot back — which U4 change 2 makes visible and therefore plannable, and which is arguably the
> better version of dilemma 4 ("spend the slot, or bank it"). If ruled **yes**, it is a sim
> change with a **balance re-baseline** and belongs in P5's wave, not here.

**Files.**

- *modify* `godot/scripts/RaidConductor.cs` — expose whether any party is staged this tick
  (derived, not stored).
- *modify* `godot/scripts/MainUi.cs` — the send-off conductor line; the empty-Camp band line.
- *modify* `godot/scripts/panels/CampPanel.cs` — the pre-forge blocker report on the
  `CampForge` button.
- *modify* `godot/scripts/ui/PhaseVocab.cs` (or wherever the Camp band label resolves — confirm
  first) for the empty-Camp copy.
- *test* `godot/tests/RaidConductorTests.cs` (extend)
- *test* `godot/tests/panels/CampPanelTests.cs` (extend — `VigilRoundTrip` already exists; add
  the blocked variants beside it)
- *test* `godot/tests/MainUiTests.cs` (extend)

**Determinism.** Client-only, zero `sim/` diff. **No re-baseline.** No timer is added, shortened,
or lengthened — law 2 ("no timers on decisions") is untouched, and the `VigilStop` hold at
`godot/scripts/RaidConductor.cs:206-208` stays exactly as it is.

**Patterns to follow.**

- `godot/scripts/panels/CampPanel.cs:232-241` — `GateButton(legal, whyNot)`: mirror the kernel's
  own guard and render the reason, never enforce a rule in the panel.
- `godot/scripts/panels/CampPanel.cs:179-187` — reading venue data for the stakes line rather
  than inventing a risk score. The send-off line must do the same with `MusterPlan`.

**Test scenarios.**

1. `SendOff_NamesAComingVigil_WhenAnyPartyTargetsFloor2OrDeeper`
2. `SendOff_SaysNoStopIsComing_WhenEveryPartyTargetsFloor1`
3. `SendOff_CopyNeverAssertsThePartyWillReachTheCheckpoint` — law-4 pin, asserted on the
   rendered string.
4. `EmptyCamp_RendersTheNobodyCampedLine_AndStillAdvancesInEmptyBeatSeconds` — the pacing is
   unchanged; only the explanation is added.
5. `CampForgeButton_ReportsNoSlotsLeft_WhenActionSlotsRemainingIsZero`
6. `CampForgeButton_ReportsNoMaterials_WhenNoSelectedProfessionRecipeIsCraftable`
7. `CampForgeButton_OpensTheForge_WhenACraftIsGenuinelyLegal` — the existing round trip, still
   working.
8. `VigilStop_StillHoldsIndefinitely_WithNoTimer` — regression pin on the law.

**Verification.** Engine suite green. Manual: a day where every party targets floor 1 — the
send-off says so and Night is no longer a surprise; a day with a staged party — the line lands
and the camp card follows.

---

### U5 — The party reads as people, not a rotation

**Goal.** Close *"Party seems in a loop."*

**Serves: link3** — the hero carries it into the dark *on their own judgment*, and a party that
reads as a fixture is a party whose judgment reads as a schedule.

**Mechanism verdict: BOTH — a real sim loop underneath a legibility failure, and they need
different fixes.**

**The real loop, with evidence.** `PartyFormation.FormParties` is a pure function of the roster:
alive heroes grouped by `LadderRank`, then anchors and fillers dequeued in `HeroId` order
(`sim/GameSim/Heroes/PartyFormation.cs:33-92`). With six alive heroes all at rank 0 — the state
of the game for its entire first stretch — that yields exactly two parties of three, the *same*
three heroes, every day, with no draw of randomness anywhere in it. Target floor is
deepest-cleared + 1, and `ApplyCompetenceRetreat` caps each hero at +1 floor per trip
(`sim/GameSim/Expedition/ExpeditionResolver.cs:405-426`, cited in §11.8). The Mine's floors 1-2
are Cave Rat and Tunnel Spider on every visit
(`sim/GameSim/Venues/VenueRegistry.cs:126-134`). So for the first several days the player watches
the same three names fight the same two monsters. It is a loop because it *is* a loop.

**The legibility failure on top.** Nothing on any surface names what changed since yesterday. The
muster board reports the same three names and the same target with no delta; the delve stage
renders Descend → Engage → Exchange → MonsterSlain → OreFound per floor with the collapse rule
capping exchanges at three per fight (`godot/scripts/DelveBeats.cs:8-14`). Two identical-looking
days can differ in gear, hp margin, or ore hauled, and none of it is said.

**Approach — legibility now, rotation as a ruling.**

*Ships now (client-only, free):* every muster line and every party card carries a
**since-yesterday delta** derived from durable facts already in `GameState.EventLog` — "Torvald
marches with the shield you sold him last night", "third trip to floor 2; the first two came back
clean", "Nordri is one clear from floor 3." Continuity of reference is §11.7.4's own named
cheapest-large-win, and this is its muster-side instance. It does not make the loop stop; it
makes each iteration legibly distinct, which is what the complaint is actually about on days
where the composition genuinely could not differ.

*Routed as a ruling:*

> **Ruling asked (R8): should party composition vary within a `LadderRank` cohort?**
> The current rule is anchor-preference then `HeroId` order — total, deterministic, identical
> every day. Options: (a) leave it, and rely on U5's legibility half plus rank divergence to
> break the sameness; (b) rotate the filler order by a deterministic function of `state.Day` so
> the same six heroes form different threes across days; (c) let heroes' own state (relationship
> edges, traits) influence who parties with whom — which is the Erenshor M5 work §11.5 already
> cut and named the post-v1 queue leader.
> Recommended default: **(b)**. It is a small, pure, seedless change (day is already in state),
> it directly answers the complaint, it costs a **balance re-baseline**, and it does not
> re-open the cut (c) represents. **(c) stays cut.**

Do not take (b) without the ruling — it changes party composition on every day of every seed,
which moves every balance number in the repo.

**Files.**

- *modify* `godot/scripts/panels/RaidForecastBoard.cs` — delta lines per marcher.
- *modify* `godot/scripts/panels/MineWatch.cs` and/or `godot/scripts/JourneyStream.cs` — confirm
  which owns the live party strip, then carry the same delta there (one owner, not both).
- *create* `sim/GameSim/Drama/MusterDelta.cs` — pure projection: for each mustering hero, what
  changed since their last march (new gear this player made, trips at this floor, distance to
  next record). Read-only over `EventLog`; no new state.
- *test* `sim/GameSim.Tests/Drama/MusterDeltaTests.cs`
- *test* `godot/tests/panels/RaidForecastBoardTests.cs` (extend)

**Determinism.** `MusterDelta` is a pure read over `EventLog` with no RNG parameter and no
`Math.*`. **No re-baseline** for the shipping half. The (b) half, if ruled, is a separate PR with
its own re-baseline ceremony — never bundled.

**Test scenarios.**

1. `Delta_NamesThePlayersOwnItem_WhenTheHeroMarchesCarryingIt` — keyed on
   `Item.PlayerCrafted`, the same gate attribution reads, so the muster and the night can never
   disagree about whose work it is.
2. `Delta_IsSilent_ForAHeroWithNothingNewSinceTheirLastMarch` — no filler prose; silence is a
   legitimate answer.
3. `Delta_CountsRepeatTripsToTheSameFloor`
4. `Delta_ReadsOnly_AndNeverMutatesState`
5. `ForecastBoard_RendersOneDeltaLinePerMarcher_AndNoneForAnEmptyDelta`

**Verification.** Fast lane + engine suite green. Manual: three consecutive days on the same
seed; the muster board should read as three different days, not one day printed three times.

---

### U6 — Loss is staged, not tallied

**Goal.** Close *"Heroes shouldn't die this early"* and the design half of *"Narrator said one
didn't come back but multiple did."*

**Serves: link5** — the outcome becomes the town's memory, with your name in it. A death the
player had no stake in produces a memory with nobody's name in it.

**Mechanism verdict: the narrator count bug is already fixed and wired — do not rebuild it. The
staging is missing. The pacing is a ruling.**

**What is already done.** `NarratorVoiceDirector.ChooseLine` takes a `losses` parameter and
filters out the count-committing epitaphs on a multi-loss night
(`sim/GameSim/Presentation/NarratorVoiceDirector.cs:181-213`); `MainUi` passes
`Math.Max(1, _pendingLedgerLosses)` (`godot/scripts/MainUi.cs:684-686`). The off-by-one reading
was itself wrong and the file says so at lines 184-193. **This unit adds nothing there.**

**What is missing — the *when* and *how*.** Loss is narrated exactly once, at the automatic
Ledger reveal after the Evening tick, as one slotless spoken line plus ledger text
(`godot/scripts/MainUi.cs:664-697`; `SelectForNight` returns `DeathEpitaph` on the first
`HeroDied` and stops, `sim/GameSim/Presentation/NarratorVoiceDirector.cs:147-170`). Everything
before that is self-censored on purpose: `DelveBeats` renders a fatal round as
`SwallowedByDark` with damage zeroed and the hero omitted from later HP snapshots
(`godot/scripts/DelveBeats.cs:65-74`); the camp slate's "ALREADY BACK TODAY" says only *"the full
story awaits tonight's Ledger"* (`godot/scripts/panels/CampPanel.cs:141-155`). That censorship is
right — it protects the reveal. But it means the *entire* emotional weight of a death lands in
one card, at the end of a day the player spent doing something else, about a hero they may never
have transacted with.

**What ships (client-only).** Three stakes-side changes, none of which leak a decided outcome:

1. **The vigil already knows who is fading and says so** — `FleeThresholdPercent = 40` drives an
   existing warning, *"⚠ Someone's fading — this is the moment to ring them home"*
   (`godot/scripts/panels/CampPanel.cs:60,247-259`). Extend it to name the hero and what of the
   player's work they carry. That is the stake, stated before the roll, which is exactly what
   law 4 permits and what "the deep floors show the show, not the wager" (`THE-GAME.md` §7)
   currently concedes is absent.
2. **The death card leads with the player's hand in it, or honestly says there is none.** The
   memorial already names the gear the fallen wore; the card should open on *"Torvald fell on
   floor 3, wearing the Iron Buckler you stamped on day 2"* — or, when nothing of the player's
   was on them, say so plainly. Same P1 discipline: lead with the mark, and an honest empty state
   rather than participation credit.
3. **Loss narration stays once per night; the *ledger* stops flattening it.** Keep
   `SelectForNight`'s "overflow is silence, never a queue" — it is right. What changes is the
   ledger's own ordering: on a multi-loss night, each death gets its own card in the reveal
   rhythm §11.7.4 asks for, rather than a run of lines under one header.

**What is routed as a ruling.**

There is **no day gate, grace period, or early-campaign protection on death anywhere in the
sim**. Death is `hp[hero] <= 0` after a combat round
(`sim/GameSim/Expedition/ExpeditionResolver.cs:569-572`); the only exemption parameter in the
resolver is the bounty-taker retreat exemption
(`sim/GameSim/Expedition/ExpeditionSystem.cs:86`, `RetreatExemption`), which is unrelated. The
only balance assertion about survival is `MinAliveAtEnd = 3` at day 100
(`sim/GameSim.Tests/Balance/BalanceSimTests.cs:34`). Nothing anywhere asserts *when the first
death may land*. `THE-GAME.md` §3.3 promises the first death "lands around day four" — that is a
description of an intention, not of a mechanism, and git outranks it.

> **Ruling asked (R9): should the first campaign death have a floor?**
> Options: (a) leave it — death is arithmetic from day 1, and the owner's note is answered
> entirely by U6's staging; (b) a hard day floor (no `HeroDied` before day N, N≈4-5) enforced in
> the resolver; (c) a *stake* floor rather than a day floor — a hero cannot die until the player
> has transacted with them at least once (sold, commissioned, or provisioned), so the first
> death is by construction a death the player has a hand in.
> Recommended default: **(c)**. It answers the actual complaint — deaths landed before the player
> had any stake — without lying about the arithmetic on a calendar, and it makes the first death
> *always* a link-4/link-5 moment rather than usually a statistic. It is a real sim change with
> a **balance re-baseline** and it needs a pinned law exception review, since a rule that reads a
> hero's transaction history inside the resolver is close to the line on "the raid resolver reads
> no traits" (`THE-GAME.md` §6, Crusader Kings row). **(b) is the cheap answer and (a) is the
> honest one; do not pick for the owner.**

> **R9 RULED, 2026-08-15 — none of (a)/(b)/(c). The grace is a TAUGHT MECHANIC, and it belongs
> to the tutorial rework (U2), not to the resolver as a silent rule.**
>
> Option (c) was built first, and building it is what produced the ruling. It works exactly as
> specified — and measured over the balance suite it inverts the game:
>
> | trait | deaths / raids | mortality |
> |---|---|---|
> | Prepared | 159 / 285 | **55.8%** |
> | Reckless | 59 / 276 | **21.4%** |
>
> Preparation became **2.6x deadlier than recklessness**, because under (c) buying a salve is a
> transaction, so a hero who restocks *forfeits the protection* while a hero who buys nothing stays
> shielded permanently. The player's entire job is to arm heroes, and the rule made arming one the
> thing that gets it killed. Two further consequences fell out of the same shape: a hero the player
> never trades with is immortal for the whole campaign (so it was never a rule about "early" at
> all), and 3 of 11 balance seeds stopped reaching an Ending inside 100 days.
>
> But the deeper objection is the one that decides it, and it is this section's own diagnosis
> turned on itself: **a hidden shield is exactly the failure §11.11 exists to fix.** The game would
> have staged an outcome — a hero walking away from a killing blow — and never staged its
> antecedent. The player would learn a rule they were never told, by noticing a pattern in deaths
> that did not happen. That is the same defect as a counter asking for a shield the shelf cannot
> supply, wearing different clothes.
>
> So the grace is **introduced deliberately, in the tutorial, as a mechanic the player is told
> about and watches work** — the apprenticeship's own promise, with a visible end. It moves out of
> the balance layer and into U2's teaching arc. Its trigger, its duration and its ending copy are
> U2's to design; what is fixed here is that it must be *taught*, must be *visible when it fires*,
> and must *end where the tutorial ends* — never a permanent property of an untraded hero, and
> never coupled to whether the player sold that hero anything.
>
> **Technical findings from the (c) build, kept so U2 does not rediscover them.** The mechanism is
> sound and reusable; only its trigger was wrong. The grace clamp reuses `CombatEvent`'s existing
> `ModifierHpDelta` ledger field (the same channel the Leech rune uses), so attribution's HP replay
> stays byte-consistent and **no `Contracts/` change is needed**. `DamageTaken` keeps recording the
> true lethal roll and the grace is the counteracting delta — which is what makes the near-death
> legible rather than an invisible cap. Survival ends at 1 HP and the existing `ShouldFlee` check
> sends the hero home on the next round, so no new retreat path is required. The exemption set is
> threaded from `ExpeditionSystem`/`ExpeditionDeepSystem` into the resolver as an opaque `HeroId`
> membership test, mirroring the pre-existing bounty `retreatExemptHeroes` parameter exactly — the
> resolver still decides every fight on combat math alone and reads no trait, which is why **no
> pinned law exception was required**. Recomputing at both the Expedition and ExpeditionDeep ticks
> (rather than carrying one set across Camp) matters, because a vigil resupply can land between them.
>
> Fixture note for whoever builds U2: `StagedResolutionTests`' "Naked" heroes have no transactions
> at all, so any stake-shaped predicate makes them structurally unkillable and those fixtures need
> deliberate updating rather than silent patching.

The staging half (1-3) ships regardless of which way R9 goes and does not depend on it.

**Files.**

- *modify* `godot/scripts/panels/CampPanel.cs` — name the fading hero and their carried marked
  work.
- *modify* `godot/scripts/panels/LedgerModal.cs` — death card leads with the mark; one card per
  loss.
- *test* `godot/tests/panels/CampPanelTests.cs` (extend)
- *test* `godot/tests/panels/LedgerModalTests.cs` (extend)
- *(R9 (b) or (c) only, in a separate PR)* `sim/GameSim/Expedition/ExpeditionResolver.cs`,
  `sim/GameSim.Tests/Balance/BalanceSimTests.cs`, plus a re-record ceremony.

**Determinism.** The shipping half is client-only; **no re-baseline**. Any R9 outcome other than
(a) is a sim change and a balance re-baseline, serialized, never implicit, never in the same PR
as the staging.

**Test scenarios.**

1. `FadingWarning_NamesTheHero_AndTheirCarriedPlayerCraftedGear`
2. `FadingWarning_IsSilent_WhenNoOneIsBelowTheFleeThreshold` — the existing threshold, unchanged.
3. `FadingWarning_NeverStatesASurvivalNumber` — §11.4's stakes-qualitatively note, pinned on the
   rendered string.
4. `DeathCard_LeadsWithThePlayersMarkedItem_WhenTheFallenWoreOne`
5. `DeathCard_SaysNothingOfYoursWasOnThem_WhenTrue` — honest empty state, no participation
   credit.
6. `MultiLossNight_RendersOneCardPerLoss`
7. `MultiLossNight_StillSpeaksExactlyOneNarratorLine` — regression pin on
   `SelectForNight`'s deliberate rule, so this unit cannot be mistaken for permission to queue
   narration.
8. `NarratorLine_OnAMultiLossNight_IsNeverACountCommittingLine` — pin the *already-landed* fix so
   a future session cannot regress it.

**Verification.** Engine suite green. Manual: play to a multi-loss night on a known seed; confirm
one spoken line, several cards, and the fading warning named the right person that afternoon.

---

## Key technical decisions

**KTD-1 — Forward-looking surfaces derive from the same function the tick consumes, never a
parallel one.** `CounterForecast.Queue` is called by `ApplyOpen` itself; `MusterDelta` reads
`EventLog`; the vigil send-off line reads `MusterPlan.Compute`. This repo has a named trap — a
spoken want that disagrees with what the sim accepts is worse than silence
(`godot/scripts/ui/CustomerVoice.cs:14-22`, which says exactly this and says there is precedent
for the trap). Extraction over duplication makes agreement structural rather than test-enforced.

**KTD-2 — The telegraph states rules and stakes; it never states an unrolled outcome.** Tomorrow's
muster is decided and may be named. Whether a party reaches the checkpoint is not, and may only
be described as a rule ("they'll stop if they get there clean"). Every unit that renders forward
copy carries a test asserting the rendered string contains no outcome claim and no survival
number. This is law 4 plus §11.4's own stakes-qualitatively note, made executable.

**KTD-3 — A gated surface is a *recorded* surface.** `ActionReachabilityCensusTests` is a decision
census; its entries are hand-written strings, and its own doc warns against reading a green run as
proof a button is live (lines 32-41). U3's gates therefore extend each surface string with its
gate rather than moving anything into `Exclusions` — an excluded action is one nobody surfaced,
and these are surfaced, just not yet.

**KTD-4 — Tutorial steps complete on what the player controls, never on what a hero decides.**
Both broken steps failed the same way: `CounterSaleClosed` requires `ShoppingAi` to return Buy;
`SupplyDelivered`/`PartyRecalled` requires a party to have parked. Influence never orders, so a
tutorial gated on a hero's decision is a tutorial that can be failed by the game. New house rule
for `TutorialStepDef.IsDone`: the fact must be caused by a `PlayerAction` in `ActionLog` or by a
UI navigation the player performed. Add a conformance test that asserts it for every row.

**KTD-5 — The unlock table is one-way and derived, never persisted.** `SurfaceUnlocks.IsOpen` is
pure over `GameState`, so a reload cannot resurrect a stale gate — the exact class of defect that
produced "the tutorial is missing" (`godot/scripts/ui/TutorialFlow.cs:1123-1143`: a `user://`
flag outliving the campaign that set it). No second `user://` file.

**KTD-6 — Sim changes with balance consequences are rulings, not defaults.** Three are named and
routed rather than taken: R7 (mid-vigil material purchase), R8 (party rotation), R9 (first-death
floor). Each names its default so silence has a meaning, per §11.3's own convention. Each, if
ruled, is a separate PR with its own re-baseline ceremony.

---

## Scope boundaries — what this wave does not do

- **No demand-hazard engine.** P7 is blocked on P4 and needs its own plan ceremony
  (§11.4). U1 projects demand the sim already computes; it generates none. `BeatType.ToolAssist`
  still has no emitter and this wave does not give it one.
- **No vigil balance work.** R1 (§11.3) freezes P5. U4 ships copy and gating legibility over
  verbs that exist; the runner fee, checkpoint depth, send threshold and quaff rule are untouched,
  and the one change that would breach the freeze (R7) is routed, not taken.
- **No second checkpoint.** §11.7.5's depth-scaled camps are a priced sim unit for a later wave.
- **No timers, anywhere.** `VigilStop`'s indefinite hold, `PhaseClock.Engaged`, and the
  show-held predicate are unchanged; U4 adds explanation to existing pacing, not pacing.
- **No new narrator lines and no queued narration.** The spoken library is frozen and append-only
  (`sim/GameSim/Presentation/NarratorVoiceDirector.cs:58-62` — an index is a filename). One
  spoken moment per ceremony stays the rule.
- **No hero-behaviour depth.** Erenshor M5 rivalry stays cut (§11.5); R8's recommended default is
  explicitly the version that does *not* reopen it.
- **No profession accumulation.** §11.7.7's fluid progression is P7-adjacent and demand-gated;
  U3 gates *surfaces*, not disciplines, and touches
  `ProfessionHandlers.MaxSelected` not at all.
- **No re-baseline in this wave.** Every unit as scoped is client-only or a pure sim projection.
  Any of R7/R8/R9 ruled affirmative becomes its own PR with its own ceremony.
- **No third plan doc.** This lives in §11 as an amendment; `docs/plans/` stays at two.

---

## Sequencing

| # | Unit | Size | Blocked by | Re-baseline |
|---|------|------|-----------|-------------|
| U1 | Tomorrow's asks | session | nothing | no |
| U2 | Tutorial points, gates honestly, repeats | session-wave | U1 for step 6's copy clause only | no |
| U3 | The town opens as you learn it | session | U2 (shares the registry idiom + the anchor/gate interaction test) | no |
| U4 | The vigil is a workbench | session | nothing | no |
| U5 | The party reads as people | session | nothing | no (shipping half) |
| U6 | Loss is staged | session | nothing | no (shipping half) |

U1, U4, U5 and U6 are mutually independent and touch disjoint files — four parallel lanes. U2
follows U1 by one clause. U3 follows U2. Engine tests serialize regardless
(`.claude` memory: never two gdUnit runs at once), so parallel branches, serial verification.

**Rulings owed before the wave completes:** R7 (mid-vigil materials — default no), R8 (party
rotation — default rotate by day, re-baseline), R9 (first-death floor — default stake-gated,
re-baseline). None blocks a shipping unit; each blocks a follow-on PR.

---

### 11.12 One building got a contract; nothing else did — 2026-08-15

> **Correction to this section's audio premise, measured after it was drafted — and then settled.**
> The audio unit that used to live below (since struck, along with the plan it carried) argued
> that `night-still` is "the one track the player has never heard wrap," citing
> `AudioDirector.cs`'s own comment that Camp is "a brief background beat." That comment is a claim,
> and the owner's session log is a measurement that contradicts it. In
> `runs/playtest/session-1786763902.jsonl`, Camp held `night-still` from t=325.2 to t=478.9 —
> **153.7 seconds, the longest single dwell of the day.** All four tracks were then probed directly:
> every one is 48kHz Lavf with an `Info` frame and NO LAME gapless tag, `quest-wait`/`night-still`/
> `town-dusk` are 60.02s and `day-first-light` is 134.04s. So the wrap counts in that session were
> `quest-wait` 0, `town-dusk` 1, `day-first-light` 1, and `night-still` **2** — night-still wrapped
> the most, not the least.
>
> That unit's mechanism survived this intact and was in fact strengthened: the missing gapless metadata is
> a property of all four files, so every composed bed replays encoder delay/padding on every wrap,
> and exposure is the only variable. What does NOT survive is using night-still's praise as evidence
> that it never wraps. The likelier reading of "vigil music good" is that the praised bed is the
> SYNTHESIZED `MusicBed.Underground()` theme — built so its end meets its beginning and de-clicked,
> and therefore structurally incapable of this defect — not the composed Camp MP3. Any unit that
> keys on "which track is clean" must re-derive it from the probe, never from a doc comment.

**Status: capped overhead, substrate, with four units defending §2 links. No unit here may
displace a §11.4 path item.** Written as a §11 amendment rather than a third file in
`docs/plans/` because rule 4's two-doc cap is full (`2026-08-13-002` and `2026-08-14-001` both
hold live units). Same shape and same discipline as §11.10.

**What was asked, 2026-08-15** — the owner played and wrote it down. Visual: *"shop looks
fantastic"*, *"rework other buildings to match"*, *"interiors super ugly and generic"*, *"main
character looks awful — the generic shopkeeper sprite was better"*, *"top menu needs a full
revamp"* (explanations + shortcuts), *"shop counters identical and redundant — condense them"*,
*"the Watch needs cutscene-quality visuals — render the heroes actually attacking"*, *"the hero
buying at the counter didn't match the heroes outside"*, *"heroes and NPCs need nameplates"* and
*"should stick together"*, *"the world feels TINY"*. Audio: *"bellows sound is too loud and
abrasive"*, *"vigil music is good"*, and a random static burst during Night, then again at Day 2
Dawn.

#### The common cause

The owner's split — one fantastic building, everything else generic — is not a taste gradient
across a shared art track. **There is no shared art track for the world.** Measured, from the
committed pixels:

| id | size | distinct opaque colours | bytes | what it actually is |
|---|---|---|---|---|
| `market` (drawn as **Shop**) | 76×62 | **2,732** | 9,803 | 3/4-isometric **volume**: two visible faces, porch columns, striped awning, lit glass, standing on its own baked ground pad; alpha stops at the object |
| `forge` | 72×81 | 3,645 | 10,902 | flat **front elevation** — one face, no ground, hard-cut at the bottom edge |
| `noticeboard` | 44×50 | 1,984 | 5,139 | flat front elevation |
| `tavern` | 84×88 | 4,067 | 12,780 | not a cutout at all — baked green hedges, a purple road and a cream path blob spilling outside the building's own footprint, matching no ground tile the town paints |
| `mine-gate` | 48×48 | **10** | **567** | a dark ellipse and a brown box. Programmer art, inside a set the manifest calls SDXL |

`market.png` is not better *rendered*. It is the only one drawn at the camera angle a top-down
2.5D town needs, with a self-contained base and a disciplined cutout. That is the whole
difference, and it was an accident: **not one of the five is in the art pipeline at all.**
`art/build/{market,tavern,mine-gate,noticeboard}.build.json` all record
`Status: "unreproducible-legacy"` with null seed/model/sampler; `forge` has **no build record**.
Meanwhile `art/specs/town/TownSpecs.cs:16-41` declares four `AssetKind.Building` specs —
`town-forge`, `town-tavern`, `town-market`, `town-mine-gate` — which have **no PNG, no
`art-manifest.json` entry and no draw site anywhere**. The buildings the player sees were never
specced, never rendered by the pipeline, and therefore could never be re-rendered, compared or
regressed. `TownLayout2D.cs:125-149` records the owner keeping this SDXL set on 2026-08-01 over a
pixel-art alternative; keeping it was right, and it also froze five orphans in place.

The interiors are the same failure with the polarity flipped. The four room shells
(`town2d-{forge,market,tavern,gatehouse}-interior-shell.png`, 384×224 / 320×192 / 352×208 /
288×176) carry **32–40 distinct opaque colours in 1.4–1.9 KB** — a wall band, a floor grid and a
door rectangle, four rooms differing only in hue. They are procedurally generated by
`art/pipeline/gen-*-interior.py` against a rule those scripts state in their own docstrings —
*"Six to eight colours per building, never more"* — sampled verbatim from the `town2d-*` pixel
building set **the owner rejected**. A palette law that is correct for a 20×36 sprite was applied
to a room the camera fills. And the repo already proved the alternative:
`art/build/shop-interior.build.json` is `Status: "locked"` — SDXL base 1.0, seed 497501345, 28
steps, dpmpp_2m/karras, a 1024×224 warm honey-amber shelf-wall crop with a Sobel normal map, two
rounds of curation documented in its own provenance. Its PNG is **not on disk**; it was deleted
with `ShopStage` and only `godot/.godot/imported/shop-interior.png-*.ctex` remains. The method
worked and the asset was thrown away.

Same shape in the cast, and in audio. `PlayerController2D.cs:30` resolves `player_smith`
directly through `IconRegistry.Art`, never through `ArtVariants` — it is the one figure that
commit `db18a90` (#503, 128 pooled town bodies) skipped. `CounterPanel.cs:125` and `:926` resolve
the customer through `IconRegistry.Sprite(classId)` → `res://assets/sprites/hero_vanguard.svg`, a
48×64 flat-grey primitive on the **retired** neutral-body-plus-runtime-tint contract that
`tools/art/gen_town_sprites.py:19-27` records as superseded on 2026-08-04 — so a hero changes
species walking from the plaza to the counter. And in audio, the one track the owner praised is
the one track the player has never heard loop. Every symptom is the same sentence: **a contract
was written once, satisfied once, and never made binding — so each new surface picked whichever
authoring method was nearest.** This wave writes the contracts down, pins them against the
manifest, and re-renders to them.

#### Key technical decisions

**KTD-A. The Shop's advantage is the camera and the cutout, not the renderer — so the fix is a
contract, not a re-roll.** Pin, in a test: a venue asset is one connected opaque subject at 3/4
isometric with a self-contained base, its alpha bounding box within a few percent of its own
footprint, and no baked ground extending past it. `tavern.png` fails that today and is
machine-detectably wrong (its opaque region runs to the image edge on three sides); `mine-gate`
fails the colour-depth floor by two orders of magnitude. Write the contract from `market.png`'s
own measurements, then re-render the other four to it.

**KTD-B. Do not edit the shared Active master prompt — splice a `Building` clause, exactly the way
§11.10 KTD-A spliced the `Item` one.** `ArtTrackProfiles.MasterPrompt` (`ArtTrackProfiles.cs:43-46`)
already reads *"single subject, one structure centered, 3/4 isometric view"* — the camera phrase is
in there and the buildings that violate it were made outside the pipeline that reads it. What is
missing is the *base and cutout* half. `KindClause`/`KindNegative` (`ArtTrackProfiles.cs:127,137`)
is the live seam §11.10 U1 built for `AssetKind.Item`; `AssetKind.Building`
(`art/GameArt/AssetSpec.cs:16`) gets its own clause the same way, and every other kind stays
byte-identical.

**KTD-C. The interiors get the `shop-interior` method, not a palette widening.** Re-running
`gen-*-interior.py` with more colours would still produce a flat elevation with a door rectangle
in it. What the rooms need is a painted backplate with depth and light, at the room's exact pixel
size, resampled offline — which is precisely the pipeline `shop-interior.build.json` records as
`locked`. Stations keep their pixel sprites and sit on top; the shell is the only thing that
changes, so `InteriorLayout2D`'s station table, collision and interact geometry are untouched.

**KTD-D. No runtime `Scale` knob, anywhere in this wave.** `TownLayout2D.CharacterSpriteScale` is
pinned at 1.0 by `CastProportionTests.NoRuntimeDecimation_CharacterSpriteScaleStaysOne` after the
asymmetric-decimation bug documented at `TownLayout2D.cs:34-46`; `TownLayout2D.PropLayout`'s doc
(`:99-108`) records the 25–30× runtime downscale that shimmered and held ~33 MB of VRAM for
thumbnails. Everything committed here is resampled offline to its draw size. Both prior bites are
named so no unit re-proposes it.

**KTD-E. Every guard added here iterates `art-manifest.json`. Never a literal id array.**
`ArtManifestTests.IdsInManifest()` (`godot/tests/ArtManifestTests.cs:69`) is the pattern to copy.
The counter-example is in this repo and is why this KTD is written down: `ArtWiringCoverageTests`
covers its families from hand-listed `string[]`s (`:34,43,48,50,55,61,72,79,85`) and
`AssetResolutionCensusTests` from five more (`:226,268,301,332,365`) — a guard iterating a literal
array stops covering a family the moment the family grows, silently, under a green suite.

**KTD-F. The static is the loop seam, and the track the owner praised is the proof.** Read
directly from the files' MPEG frame headers (`tools/audio/mp3-seam-probe.py` in U9 reproduces
this): all four composed tracks are **48 kHz stereo, encoded by Lavf**, carrying an `Info` (CBR)
Xing frame with **no LAME gapless sub-tag — encoder delay `none`, encoder padding `none`**.
Durations from their own frame counts: `town-dusk` / `night-still` / `quest-wait` = 2501 frames ×
1152 / 48000 = **60.02 s**; `day-first-light` = 5585 frames = **134.04 s**. `AudioDirector.cs:763`
forces `mp3.Loop = true` and every `.import` sets `loop_offset=0`, so each wrap replays the
encoder's tail padding and then its head delay with no metadata that would let the decoder strip
either — a hard step, not a fade. Now the owner's own report as evidence: **Night is
`AudioDirector.cs:146` → `town-dusk` (wraps at 60.02 s) and Dawn is `:145` → `day-first-light`
(wraps at 134.04 s) — the two phases a player sits in. Vigil is `:147` → `night-still`, and
`AudioDirector.cs:104-106` states in its own words that Camp "is a brief background beat."** The
praised track is the one that never reaches its wrap. *"Vigil music is good"* and *"static during
Night, then again at Day 2 Dawn"* are one fact stated twice. A prior pass diagnosed inter-sample
clipping and re-encoded exactly those two files (`AudioDirector.cs:117-128`); the owner still
hears it on exactly those two files, which is evidence against that diagnosis and for this one.
Secondary and worth measuring but not leading: `AudioDirector` sets no `.Bus` anywhere, so music
(-22 dB + trim), narrator (-14 dB), six pooled SFX voices and the bellows loop voice all sum on
the default **Master** bus with no limiter, and Night is the game's densest audio moment.

**KTD-G. The bellows is abrasive because of its rate, not its level.** `SfxLibrary.cs:300-314`
builds a 0.30 s buffer whose breath envelope is `sin(π · min(1, t/0.28))` — it reaches zero at
t=0.28, so the last 20 ms is silence — filled with `Synth.Noise` low-passed at 700 Hz and
normalised to peak 0.15, then unconditionally soft-clipped by `Synth.Normalise`'s `tanh`
(`Synth.cs:127`, applied even when the buffer is already under peak).
`AudioDirector.OnLoopVoiceFinished` (`:708-716`) retriggers the whole clip on `Finished`, so a
held pump is that swell **3.33 times per second with a 20 ms silent gap between each** — a pulsing
wash with tanh harmonics smeared across broadband noise. It already passes
`Bellows_IsNoLouderThanAVenueCue` (`AudioTests.cs:1326`) at the quietest level in the library,
which is exactly why "turn it down" was never going to fix it.

**KTD-H. The Watch is already animated; the missing thing is who landed the blow.**
`DelveStage.cs` has attack lunges, recoil/stagger, hit flash, drifting damage numbers, kill poofs,
HP pips and an `ImpactPulse` that punches `MineWatch`'s torch lights and world shake
(`MineWatch.cs:1180-1208`). The owner did not ask for animation; he asked for a *cutscene*. Two
real gaps: hero figures play `SpriteMotion`'s walk/breathe gait with no dedicated attack or
impact frames, and `DelveBeat` (`DelveBeats.cs:46-54`) drops the two fields that make a beat
*about the player* — `CombatEvent.MonsterKilled` and **`CombatEvent.KillingItem`**
(`sim/GameSim/Contracts/Expedition.cs:20-40`) — so the sim knows which of your items landed the
kill and the screen never says so. `DelveBeat` is a Godot-side record; threading those fields
touches **no** `Contracts` file.

**KTD-I. "The world feels TINY" is content density and framing, not zoom.** Measured: the grid is
`TownLayout2D.GridWidth/Height` 40×28 tiles at 16 px = 640×448 world px. `Town2D.CanvasShrink`
resolves to 3 at the 1152 px default window (`Town2D.cs:95-113`), so **384×216 world px = 24×13.5
tiles are on screen at once**. The five venues occupy tiles x13–26, y3–18 — the entire inhabited
town fits inside one screen with columns to spare, ringed by 12 copies of one tree at x=2 and
x=37 and nothing in between. Population is 4 townsfolk (`Town2D.cs:1535-1541`, four hardcoded
corners) plus 6 heroes plus the player. The camera has nowhere to go and nothing to find when it
gets there. Fixing that by lowering `CanvasShrink` would violate KTD-D in spirit and make every
sprite mushier; the fix is to occupy the grid and to give night real light pools instead of one
flat `CanvasModulate` wash (`Town2D.cs:484`) — note that `MineWatch` already uses `PointLight2D`
and the town does not.

#### Implementation units

---

**U1 — Write the building contract down and make it fail loudly.** *(no GPU)*

**Goal.** Turn what `market.png` accidentally is into a pinned, testable contract, put the five
drawn buildings into the pipeline that can render to it, and replace the hand-listed art guards
with manifest-iterating ones.

`Serves: substrate`

**Files.**
- modify `art/GameArt/ArtTrackProfiles.cs` — add `BuildingClause` / `BuildingNegative` to the
  existing `KindClause`/`KindNegative` switches (`:127,137`).
- modify `art/specs/town/TownSpecs.cs` — retarget the four orphan specs onto the ids actually
  drawn (`forge`, `market`, `tavern`, `mine-gate`) and add the missing `noticeboard` spec.
- create `godot/tests/VenueArtContractTests.cs` — the structural contract, iterating the manifest.
- modify `art/GameArt.Tests/ArtTrackProfileTests.cs` — byte-identity pins.
- modify `godot/tests/ArtWiringCoverageTests.cs` — replace `TownPropIds`, `VenuePropIds`,
  `MineMonsterKinds`, `HeroClassIds` literal arrays with manifest-derived enumeration.

**Approach.** The `Building` clause adds what the master prompt lacks: *"standing on its own small
ground base, complete object isolated on a plain background, nothing cropped at the frame edge"*,
with negatives for `landscape, background scenery, hedges, road, ground plane extending past the
building, flat front elevation, orthographic facade`. `TownSpecs.cs` is retargeted rather than
deleted-and-rewritten so the four already-authored `Subject` strings survive. The contract test
derives its thresholds from `market.png` itself, measured at test time, so the reference cannot
drift away from the standard silently.

**Patterns to follow.** `ArtTrackProfiles.cs:95-140` (the `Item` clause §11.10 U1 landed — same
seam, same doc discipline). `godot/tests/ArtManifestTests.cs:69` `IdsInManifest()` for the
manifest iteration. `godot/tests/ArtMissLoggingTests.cs` for the loud-degrade contract.

**Test scenarios.**
1. A `Building` spec's composed prompt contains the base/isolation clause.
2. A `Prop` / `Backdrop` / `Portrait` / `Monster` / `Sprite` / `ClassFigure` spec's composed prompt
   is **byte-identical to the string committed today** — the guard against re-rolling ~300 assets.
3. An `Item` spec still composes §11.10's item clause, unchanged.
4. Every id in `art-manifest.json` that `TownLayout2D.Venues` draws resolves to a real texture.
5. Every venue texture has ≥ *N* distinct opaque colours, where *N* is derived from the manifest's
   own venue set minus the outlier — `mine-gate` (10 colours) fails this today and must.
6. Every venue texture's opaque bounding box leaves transparent margin on at least three sides —
   `tavern.png` fails this today and must.
7. Every venue texture is a single connected opaque component above a small-blob threshold.
8. Every `AssetKind.Building` spec id in `AssetRegistry` appears in `art-manifest.json`, and every
   venue id `TownLayout2D` draws has a spec — closes the orphan gap in both directions.
9. `ArtWiringCoverageTests`' families are enumerated from the manifest; adding a `town2d-prop-*`
   PNG without wiring it fails the suite.

**Verification.** `dotnet test art/GameArt.Tests` green; `dotnet test godot/tests --settings
.runsettings` run **whole**, quoting the runner's own `Failed: N, Passed: N` line, with scenarios
5 and 6 red before the U3 pixels land and green after. Zero pixels change in this unit.

---

**U2 — The gate: five buildings and one room, rendered, in front of the owner.** *(GPU-gated)*

**Goal.** Put the U1 contract in front of the owner as images before anything is committed, and
stop the art half of this wave if he says no.

`Serves: substrate`

**Files.** None committed. Renders land in `runs/receipts/` only.

**Approach.** Render two candidates each for `forge`, `tavern`, `mine-gate`, `noticeboard` on the
U1 prompt, plus one `market` re-render as the control (if the contract cannot reproduce the
building the owner already likes, the contract is wrong and this unit has found that out for the
price of one render). Separately, render one interior backplate — the Forge room at 384×224 — by
re-running the `shop-interior` recipe recorded in `art/build/shop-interior.build.json`
(sd_xl_base_1.0, 28 steps, dpmpp_2m/karras, 1024 square then cropped and resampled offline to the
room's exact size). Present each beside the shipped asset it would replace.

**GPU gating.** Needs ≥13,900 MiB free VRAM to start; one job at a time; abort above 14 GB used or
83 °C; owner-granted window. **There is not enough free right now — this unit sits queued and
blocks only U3.** Every other unit in this wave runs while it waits.

**Verification.** The images themselves, beside the current assets. **If the owner says no, U3 is
not attempted** — that is this unit's entire purpose. A yes on buildings and a no on interiors (or
the reverse) splits U3 cleanly.

---

**U3 — Commit the rebuilt exteriors and the painted interiors.** *(GPU-gated; conditional on U2)*

**Goal.** Four buildings that match the Shop, and four rooms that stop looking like a diagram.

`Serves: substrate`

**Files.**
- modify `godot/assets/art/{forge,tavern,mine-gate,noticeboard}.png` (+ `_n.png` normals)
- create `godot/assets/art/town2d-{forge,market,tavern,gatehouse}-interior-shell.png` replacements
  (+ `_n.png`)
- modify `art/build/*.build.json` for each — `Status` flips off `unreproducible-legacy` to a real
  seed/model/sampler record; `forge` gains its first build record.
- modify `godot/assets/art/art-manifest.json` — `normal: true` for every rebuilt id.
- modify `godot/scripts/town2d/TownAssets2D.cs` — `VenuePlaceholders` sizes must track the new PNG
  header dimensions 1:1 (the file's own `:40-45` doc explains why).
- modify `art/pipeline/gen-{forge,market,tavern,gatehouse}-interior.py` — the shell half is
  removed from each script and its docstring records that the shell is now an SDXL backplate;
  the **station** half stays exactly as it is.

**Approach.** Resample offline to draw size (KTD-D); commit no runtime scale. Normals are shipped
because U6 adds `PointLight2D` and an unlit diffuse under a 2D light reads flat — `shop-interior`
already established the `normalmap.py` Sobel chain for exactly this surface class. `mine-gate` is
the largest visual delta (10 colours → a real building) and lands first inside this unit so it can
be reverted alone if it reads wrong.

**Patterns to follow.** `art/build/shop-interior.build.json` (the recipe and the honest provenance
prose). `art/pipeline/normalmap.py`. `art/pipeline/cutout.py` — and its documented skip condition
for full-bleed backplates, which the interior shells meet and the building cutouts do not.

**Test scenarios.**
1. U1's scenarios 4–8 now pass for every venue, including `mine-gate` and `tavern`.
2. Every committed PNG's dimensions equal the size `TownAssets2D.VenuePlaceholders` /
   `InteriorLayout2D` declares — a mismatch shifts collision and interact geometry.
3. Every rebuilt id has `normal: true` in the manifest **and** a `_n.png` on disk; the manifest and
   the filesystem agree in both directions.
4. Every rebuilt id's `build.json` has a non-null `Seed`, `Model` and `DiffuseSha256`, and
   `Status != "unreproducible-legacy"`.
5. `InteriorRoomTests` and `InteriorTraversalTests` still pass unchanged — the shell swap must not
   move a single station or door.
6. `--check` on each surviving `gen-*-interior.py` reports zero drift on the station sprites.

**Verification.** A `FullPlaytest` run reporting **zero** art-miss warnings (the `EngineDistress`
logging from #497), plus rendered captures: the five buildings in the plaza, and the player
standing inside each of the four rooms. Diff-side green is not the proof here; the frames are.

---

**U4 — Every person in the world is a specific person.** *(no GPU)*

**Goal.** The player smith stops being the drabbest figure on screen; a hero is the same human at
the counter as in the plaza; and you can read a name without clicking.

`Serves: link2` — you cannot *hold the good one for the hero who needs it* if you cannot tell
heroes apart on sight, and you cannot price for the relationship with someone whose face changes
when they walk indoors.

**Files.**
- modify `tools/art/gen_town_sprites.py` — the player's garment ramp.
- modify `godot/assets/art/player_smith{,_step,_walk2,_walk4}.png`
- create `art/build/player-smith.build.json`
- modify `godot/scripts/panels/CounterPanel.cs` (`:125`, `:926`)
- modify `godot/scripts/town2d/HeroActor2D.cs`, `godot/scripts/town2d/TownsfolkNpc2D.cs`,
  `godot/scripts/town2d/PlayerController2D.cs` — nameplates.
- modify `godot/tests/TownSpriteArtTests.cs`, `godot/tests/CastProportionTests.cs`,
  `godot/tests/panels/` counter coverage; create `godot/tests/NameplateTests.cs`.

**Approach, in three grounded parts.**

*(a) The player smith.* The regression is measurable and it is not size or generator — both come
off `gen_town_sprites.py`, and the player is deliberately taller (22×34 vs the cast's 20×32,
asserted at `gen_town_sprites.py:1436`). It is **colour area**: `player_smith.png`'s top tones are
the neutral steel-violet ramp — `#140f1f` 19.4 %, `#2a2438` 12.4 %, `#b8b0c6` 11.0 %, `#6e6880`
8.5 %, `#3d3242` 8.5 %, roughly 60 % neutral — with the warm `PLAYER_HUE (110,74,42)`
(`gen_town_sprites.py:229,1511`) confined to the apron bib, while `town2d-townsfolk-broad.png`
carries `#504027` 17.7 % + `#847050` 15.1 % + `#342919` 10.7 % + `#c4946e` 5.4 %, ≈49 % warm
garment across the whole torso. The player reads as a grey smudge standing next to warm people.
Fix at `gen_town_sprites.py:1455-1458`: the shirt takes the warm ramp, the neutral steel moves to
the apron and tools where it belongs, and the extra 2 px of height buys one more shading step than
the civilians get. **The player does not join a variant pool** — he is one person, and `ArtVariants`
on the player would be wrong.

*(b) The counter hero.* `CounterPanel.cs:125` and `:926` call `IconRegistry.Sprite(classId)`
(`IconRegistry.cs:168` → `res://assets/sprites/hero_{classId}.svg`, a 48×64 flat-grey primitive on
a contract `gen_town_sprites.py:19-27` records as retired on 2026-08-04), taking only the class id
so every Vanguard is identical, and bypassing the `UiKit.ArtRect(AssetCatalog.HeroPortraitId(...),
…, IconRegistry.Sprite(...))` fallback ladder every other panel uses (`HeroesPanel.cs:334`,
`LedgerModal.cs:270`, `TavernPanel.cs:220`). Route both call sites through
`TownAssets2D.ForHero(classId, heroId)` — the same `ArtVariants.Pick($"town2d-hero-{classId}",
"hero", heroId)` the plaza, market and tavern already use (`TownAssets2D.cs:157-165`,
`Town2D.cs:784`, `MarketLife2D.cs:353-354`, `TavernLife2D.cs:129`) — with `IconRegistry.Sprite` left
only as the ladder's last rung.

*(c) Nameplates.* None exist over any actor: `Label` appears in `godot/scripts/town2d/` only in
`Building2D.cs:102` and the interior station labels. Reuse `Building2D.BuildLabel`'s exact recipe
(`:400-421`, `FontSize=7`, `ShadowSize=2`, `ShadowOffset (0,1.5)`, offset `-size.Y - 10f`) so
nameplates and building nametags are visibly the same object class. Heroes get their name and
class tint; townsfolk get their name; the player gets none (the camera already says which one he
is). Nameplates live inside the world node so the integer canvas upscale magnifies them with
everything else — `Building2D.cs:404` already documents this.

**Test scenarios.**
1. `player_smith.png` is 22×34 and warm tones exceed 45 % of its opaque area — pins (a) as a
   measurement, not a taste claim.
2. The player's height still exceeds the cast's (`gen_town_sprites.py:1436` assertion holds) and
   `CharacterSpriteScale` is still exactly 1.0.
3. `gen_town_sprites.py --check` reports zero drift across every committed town sprite.
4. `CounterPanel`'s customer texture for hero id *H* of class *C* equals
   `TownAssets2D.ForHero(C, H)` — the same texture `Town2D.ReconcileHeroes` gives that hero.
5. Two heroes of the same class with different ids resolve to different counter textures whenever
   the class pool has depth > 1.
6. No production panel calls `IconRegistry.Sprite` outside an `ArtRect` fallback argument —
   reflective over `godot/scripts/panels/`, so a future panel cannot reintroduce it.
7. Every hero actor and every townsfolk actor in a built `Town2D` has a visible `Label` whose text
   is that actor's sim name.
8. A nameplate's world Y sits above its sprite's top edge for the tallest and shortest committed
   body, at both 22×34 and 20×32.
9. Nameplates do not enter Y-sort — an actor never sorts in front of another actor because of its
   label.

**Verification.** Rendered captures: the plaza with nameplates on, and the same hero at the
counter beside the same hero in the plaza. Full engine suite, run whole, raw `Failed:` line quoted.

---

**U5 — The cast stops scattering.** *(no GPU)*

**Goal.** Heroes and townsfolk read as a town's worth of people who know each other, not twelve
independent oscillators.

`Serves: link2`

**Files.** modify `godot/scripts/town2d/HeroActor2D.cs`,
`godot/scripts/town2d/TownsfolkNpc2D.cs`, `godot/scripts/town2d/Town2D.cs`
(`HomeFor` at `:1242-1243`, `TownsfolkHomeTiles` at `:1535-1541`); modify
`godot/tests/HeroActor2DTests.cs`, `godot/tests/TownsfolkNpc2DTests.cs`,
`godot/tests/TownLifeTests.cs`.

**Approach.** The scatter is arithmetic, and it is exact. `Town2D.HomeFor(heroValue) =
TileToWorld(6 + heroValue*3 % 28, 10 + heroValue*2 % 6)` gives heroes 0–5 world X = 104, 152, 200,
248, 296, 344 — a 240 px spread across a 384 px screen — and each then wanders a private lissajous
of amplitude 14×10 px around its own anchor (`HeroActor2D.cs:53,55,293-299`). Townsfolk sit at four
hardcoded corners 448 px apart in X (`Town2D.cs:1535-1541`) with a 9×5 px wander
(`TownsfolkNpc2D.cs:55,57,437-443`). There is no cohesion, flocking, leader-follow or
distance-to-companion term in any of the five ambient-life files.

Replace the per-id anchor with a small table of **named gathering spots** — the well, the tavern
door, the market front, the forge yard, the gate road — and give each actor a spot rather than a
coordinate. Two or three actors share a spot with a deterministic per-actor offset inside it, so
they stand in conversational clusters. Which spot an actor holds is a pure function of its sim id
and the day phase (heroes drift toward the gate before an expedition, toward the tavern in the
evening), so it stays deterministic, save-safe, and needs no new sim data. Keep the existing
lissajous as the *within-spot* idle so the walk animation and pose maths are untouched.

**Patterns to follow.** `HeroActor2D.RallyTo`/`MarchOutTo` (`:208-220`) — the one place the codebase
already moves heroes as a group, and `Town2D.RallySpacingPx = 14f`, which is the cluster spacing to
match. `SpriteMotion` stays the pose driver; no `Tween` (the repo has zero `CreateTween` calls in
`godot/scripts` and that is deliberate).

**Test scenarios.**
1. At any phase, no gathering spot holds more than *k* actors, and no actor is further than *r* px
   from its assigned spot.
2. The mean pairwise distance between living heroes at Morning is below a pinned ceiling derived
   from today's measured 240 px spread — the regression pin for "stick together."
3. Spot assignment is a pure function of `(heroId, phase)`: two `Town2D` instances built from the
   same state assign identically.
4. Assignments survive a save/load round-trip (`CampaignSaveTests` seam).
5. Actors sharing a spot never occupy the same pixel — the deterministic offsets are distinct.
6. Departure choreography (`RallyTo`/`MarchOutTo`) still wins over the spot anchor while it runs,
   and the anchor resumes after.
7. `AmbientLife2DTests`, `MarketLifeTests`, `TavernActsTests` unchanged and green — errand walks
   and tavern seating still override the anchor.

**Verification.** Rendered captures at Morning, Expedition and Evening showing clustered groups,
plus the full engine suite run whole.

---

**U6 — The town gains somewhere to walk and something to light.** *(no GPU; its optional skyline
art rides U3)*

**Goal.** Stop the world fitting on one screen.

`Serves: substrate`

**Files.** modify `godot/scripts/town2d/TownLayout2D.cs` (`Venues`, `PathRects`, `Props`),
`godot/scripts/town2d/Town2D.cs` (`BuildProps`, the lamp layer), `godot/scripts/town2d/Building2D.cs`
(door-anchor recheck); modify `godot/tests/Town2DSceneTests.cs`,
`godot/tests/CameraFollowTests.cs`, `godot/tests/PhaseLightTests.cs`,
`godot/tests/RealClickReachesBuildingTests.cs`.

**Approach, three parts, none of them a zoom change (KTD-I, KTD-D).**

*(a) Occupy the grid.* The venues currently sit inside x13–26 of a 40-wide grid with 24 columns
visible — the whole town is one screenful. Spread the five venues across the grid's real width and
lengthen the gate road so walking from the tavern to the mine gate is a journey with a camera pan
in it, keeping every `PathRects` spur connected and every door-front tile clear. The grid itself
stays 40×28: the fix is using it, not enlarging it. Redistribute the 12 identical trees and the
eight `props-*` warm-hub props (`TownLayout2D.cs:246-276`) into the newly opened middle ground so a
pan reveals objects rather than grass. Note the pre-existing duplicate the file flags in its own
doc (`:273-276`) is GONE: the owner ruled for `town2d-well` and `props-town-well` was
deleted (layout, spec, manifest, assets) on 2026-08-16, so the prop list is one shorter
than this paragraph originally counted.

*(b) Real light at night.* The town's only light model is one `CanvasModulate`
(`Town2D.cs:484`) tinting the whole viewport flat. `MineWatch` already builds `PointLight2D`s and
its own gradient (`MineWatch.cs:405-445`, `BuildLightGradient`). Give each `town2d-prop-lantern`
placement and each lit venue window a `PointLight2D` on the same recipe, energy driven by
`DayPhaseTint`, so dusk becomes pools of warm light in a cool wash instead of a purple filter. The
normals U3 ships are what make this read on the buildings; the effect degrades to today's flat
look if U3 has not landed, so this unit is not blocked by the GPU.

*(c) The edge stops being a wall of grass.* A parallax silhouette band at the grid's north and
south edges. Procedural in the `gen_town_sprites.py` idiom is the default and needs no GPU; if U2
came back yes, an SDXL skyline rides U3 instead. Either way it ships at draw size.

**Test scenarios.**
1. The venue set's bounding box exceeds the visible canvas in both axes — the camera always has
   somewhere to go. Derived from `Town2D.ShrinkFor(1152)`, not a literal.
2. Every venue's door-front tile is reachable from the plaza through connected `PathRects` cells —
   a flood fill, so a spread layout cannot strand a building.
3. No two venue sprite footprints overlap, using the real PNG dimensions (the check
   `TownLayout2D.cs:138-140` currently does by hand in a comment).
4. No prop is placed on a 1–2-tile spur (the plaza square stays exempt, per the file's own
   precedent).
5. `RealClickReachesBuildingTests` passes for all five venues at their new tiles — a real click at
   a real screen position, not a seam call.
6. Every lantern placement has exactly one `PointLight2D`; energy at Morning is below energy at
   Evening; energy is a pure function of `DayPhase`.
7. `CanvasShrink` is unchanged and `CameraZoom` is still 1 — the regression pin against fixing
   framing with magnification.
8. The parallax band never enters Y-sort and never receives a click.

**Verification.** Rendered captures at Morning and Evening from three camera positions, plus the
full engine suite run whole. The Evening capture is the one that proves (b).

---

**U7 — The top bar explains itself and shows its keys.** *(no GPU)*

**Goal.** Every HUD control says what it does and which key does it, and the keys that already
exist stop being secret.

`Serves: substrate`

**Files.** modify `godot/scripts/MainUi.cs` (`BuildUi` at `:1926`, `TrayButton` at `:2954`, the
verb cluster at `:2038-2236`, the books tray at `:2238-2304`); modify
`godot/scripts/ui/UiKit.cs` (a shortcut-badge helper beside `StatChip`);
create `godot/scripts/ui/ShortcutMap.cs` — the single registry of every binding;
modify `godot/scripts/ui/SettingsPanel.cs` to read from it; modify
`godot/tests/MainUiTests.cs`, `godot/tests/SettingsPanelTests.cs`; create
`godot/tests/ShortcutLegendTests.cs`.

**Approach.** The gap is precise: `TooltipText` is already used in 23 files, but every top-bar
tooltip is a **one-word restatement of the icon** — `"Ledger"`, `"Forecast"`, `"Commissions"`,
`"Legends"`, `"Demand"`, `"Renown"`, `"Progress"` (`MainUi.cs:2258-2304`) — and exactly one control
in the entire HUD names a key (`"Fullscreen (F11)"`, `:2217`). Meanwhile real bindings exist and are
undiscoverable: `F11` and the four-rung `Escape` ladder (`MainUi.cs:3265-3316`), quick-travel `1`–`4`
→ Forge/Shop/Tavern/Gate (`MainUi.cs:118-124`, `:3110-3122`) which are additionally **gated on
`Tutorial.QuickTravelUnlocked`** with nothing telling the player they exist or when they arrive,
`WASD`/arrows/`E` (`TownInput.cs:18-23`), the minigame verbs (`MinigameInput.cs:40-51`), and the
numeric widget keys on `PriceTag`/`CoinStack`. `project.godot` has **no `[input]` section at all** —
every binding is registered at runtime — and `SettingsPanel.RebindableActions` (`:74-110`) is the
only surface that ever shows a key to the player, covering movement and minigame verbs only.

`ShortcutMap` becomes the one place a binding is declared, with a human label and a one-sentence
description; `MainUi`, `SettingsPanel` and the tooltips all read it. Every top-bar control's tooltip
becomes *what it does* plus its key badge; controls with a key render the badge inline on the
button, not only on hover. Quick-travel keys render greyed with their unlock condition named rather
than being invisible. This is copy and wiring — **no new verb, no timer, no change to what any
button does** (law 3 and law 2 both untouched).

**Patterns to follow.** `UiKit.StatChip` / `StatChipCompact` (`:211,235`) for the badge shape;
`GameTheme` tokens only, never a literal colour; `UiKit.DrawerHeader` (`:700`) for the header
idiom; `SettingsPanel.ActionLabels`/`DefaultKeysByAction` for the human-label convention already
established.

**Test scenarios.**
1. Every top-bar button has a `TooltipText` longer than its own visible label — a one-word tooltip
   fails.
2. Every binding in `ShortcutMap` is actually registered (`InputMap` or a real `_Input` handler);
   every runtime binding appears in `ShortcutMap`. Both directions, reflectively — this is the
   guard that keeps the legend from going stale.
3. `SettingsPanel` renders every `ShortcutMap` entry, including `F11`, `Escape` and quick-travel,
   not just the rebindable subset.
4. A locked quick-travel key renders visibly disabled with its unlock condition in its tooltip, and
   pressing it still does nothing.
5. Tooltip and badge text fit their controls at the smallest supported window
   (`MenuSizingTests`/`HudBoundsTests` seams) — no clipping, no HUD overflow.
6. No control gained a verb: the set of actions reachable from the top bar is unchanged.

**Verification.** `UiRenderSmokeTests` plus a captured HUD frame with a tooltip open, and the full
engine suite run whole.

---

**U8 — One counter.** *(no GPU)*

**Goal.** The shop stops rendering the same shelf twice, one list above the other, in a single
scroll.

`Serves: link2`

**Files.** modify `godot/scripts/panels/ShopPanel.cs` (`BuildShelfSection` `:183-260`, the mount at
`:514-521`), `godot/scripts/panels/CounterPanel.cs` (`BuildShelfActions` `:368-425`); modify
`godot/tests/ShopPanelTests.cs`, `godot/tests/panels/` counter coverage,
`godot/tests/RealDragOntoShelfTests.cs`; delete the orphans
`godot/scripts/panels/ShopStage.cs.uid` and `godot/tests/ShopStageTests.cs.uid` (both have no `.cs`).

**Approach.** These are not two panels: `MainUi.PanelFor` (`:2918-2930`) registers only `"Shop"`,
and `CounterPanel` is mounted as a child of `ShopPanel` (`ShopPanel.cs:517`) directly **above** the
shelf sections. The redundancy is exact and it is one thing: **`state.Player.Shelf` is iterated
twice in one scroll** — once by `CounterPanel.BuildShelfActions` ("Present / Suggest") and once by
`ShopPanel.BuildShelfSection` ("Your Shelf") — same items, same `IconRegistry.Slot` icon, same
name/quality/price shape, different button sets.

Condense to **one shelf list** whose row carries every shelf verb, with the counter-only verbs
(Present, Suggest) appearing on the row when a customer is present and absent when one is not.
Everything else stays: `ShopPanel` keeps "Who Would Buy This" (`:135-161`), Unstock/Reprice/History
(`:225-250`), "Unshelved Crafts" with `SuggestedPrice` (`:290-392`), the Rival Shelf (`:394-438`)
and drag-to-stock; `CounterPanel` keeps the entire haggle session — Open/Close, want-line, the
Interest/Patience/Goodwill/Round meters, Accept/Hold Firm/Counter, the `CounterDesk` canvas and the
"no active customer" legibility state. **Nothing is deleted except the duplicate rendering.** The
three entry points into `QueuePresent` (`CounterPanel.cs:269-302` — button, desk drag, handshake)
stay: that is one seam with many affordances by design, and it stops reading as clutter once it is
not sitting on top of a duplicated list.

**Test scenarios.**
1. A shelved item appears in exactly one row in the built `ShopPanel` tree — the pin for the whole
   unit.
2. Every verb reachable before is reachable after: Stock, Unstock, Reprice, History, Present,
   Suggest, Accept, Hold Firm, Counter, Open Counter, Close Counter. Enumerated, not sampled.
3. With no active customer, Present/Suggest are absent (not disabled-and-present); with one, they
   are on the row.
4. The Rival Shelf, the forecast section and the unshelved-crafts section are unchanged.
5. `RealDragOntoShelfTests` passes — the drag gesture survives the layout change.
6. Presenting via the button, the desk drag and the handshake all queue the identical action.
7. No `.uid` file exists in `godot/scripts/` or `godot/tests/` without a matching `.cs` — a
   reflective orphan guard, so the next deletion cannot leave litter either.

**Verification.** A captured Shop frame with a customer present and one without, plus the full
engine suite run whole.

---

**U9 — The Watch shows the blow, and whose work landed it.** *(no GPU)*

**Goal.** Cutscene-quality means the heroes visibly attack **and** the screen names the item that
did it.

`Serves: link4` — the counterfactual proof is the game's fourth link, and the sim already computes
which of your items landed the killing blow while the renderer throws it away.

**Files.** modify `godot/scripts/DelveBeats.cs` (the `DelveBeat` record `:46-54`, `BuildBeats`
`:100-205`, `RenderFight` `:217-334`), `godot/scripts/panels/DelveStage.cs`,
`godot/scripts/panels/MineWatch.cs`; modify `tools/art/gen_town_sprites.py` and
`godot/assets/art/town2d-hero-*_{attack,impact}.png` (+ variants); modify
`godot/tests/DelveBeatsTests.cs`, `godot/tests/DelveStageTests.cs`,
`godot/tests/MineWatchTests.cs`, `godot/tests/TownSpriteArtTests.cs`.
**No `sim/GameSim/Contracts/` file is touched** (KTD-H).

**Approach, three parts.**

*(a) Carry the attribution.* `CombatEvent` (`sim/GameSim/Contracts/Expedition.cs:20-40`) already
has `MonsterKilled`, `KillingItem`, `Uses` and `ModifierHpDelta`; `DelveBeat` carries none of them.
Add `KillingItem` and `MonsterKilled` to the Godot-side `DelveBeat` record, thread them through
`RenderFight`, and let `DelveStage` stage the kill as a beat that names the item — the same
sentence `MineWatch` already barks for `AttributionBeatEvent` (`MineWatch.cs:1073`), now landing on
the frame where the monster dies instead of in a separate feed line.

*(b) Draw the swing.* Hero figures currently play `SpriteMotion`'s walk/breathe gait with combat
motion layered as position/rotation maths on top (`DelveStage.cs:777-841`). Author two frames per
class in `gen_town_sprites.py`'s existing rig — an attack extension and an impact recoil — and let
`BeginCombatPose` swap the frame at the curve's peak. Procedural, no GPU, `--check` drift-guarded,
and it inherits U4's lesson about writing full-width rows rather than mirroring a padded half.

*(c) Stage it.* On a killing blow, push `ImpactPulse` to its ceiling (the light-punch and world
shake at `MineWatch.cs:1180-1208` already read it), hold the beat longer in the playhead, and let
the camera settle on the pair. **No new timer on any decision** — this is the reveal, not a verb;
skipping stays legal and unchanged (law 7), and every beat still comes from
`DelveBeats.Build`, so nothing is invented that the sim did not decide (law 4).

**Test scenarios.**
1. A `CombatEvent` with a non-null `KillingItem` produces a `DelveBeat` carrying it; one without
   produces null. Both directions.
2. A killing blow by a **player-crafted** item renders a beat naming the item; a killing blow by a
   non-player item renders no attribution — there is no participation credit (link 4).
3. Every hero class has committed `_attack` and `_impact` frames at the class body's pinned size,
   enumerated from the manifest (KTD-E), not from a class-id array.
4. `gen_town_sprites.py --check` reports zero drift.
5. An `Exchange` beat with `DamageDealt > 0` swaps to the attack frame during the pose and returns
   to the gait after — asserted on the sprite's actual region, not on a flag.
6. Determinism: the same seed and the same beats produce the same frame sequence.
7. `MineWatch` still builds exactly **one** `SubViewport` (`:379-386`) — the historical two-viewport
   headless hang (`docs/debugging.md:93-100`) must not return.
8. Golden replay unaffected: `DelveBeat` is adapter-side and no sim state moves.

**Verification.** A captured `DelveStage` sequence across a full fight ending in a kill with the
item named, plus the **full** engine suite run whole (never a filtered run — a filtered run cannot
see other suites vanish), raw `Failed: N, Passed: N` quoted.

---

**U11 — Make the docs true.** *(no GPU)*

**Goal.** `ASSETS.md` stops describing a world that no longer exists.

`Serves: overhead — booked`

**Files.** modify `docs/design/ASSETS.md` — §1 image counts, the pipeline-B asset count, the
venue/building rows (they stop being `unreproducible-legacy` once U3 lands), the interior-shell
rows (procedural → SDXL backplate), the variation-pools paragraph (the player smith is
deliberately *not* pooled; hero attack frames are), and the audio section (composed tracks now
carry a seam pin). Also record, honestly, that `shop-interior` was generated, locked, and deleted
with `ShopStage`, and that its method is what U3's interiors are built on.

**Approach.** Rule 8: this lands **in** the PR that makes each claim true, never after. Every
count is regenerated from `art-manifest.json`, never hand-tallied.

**Verification.** No claim in `ASSETS.md` that `git` or the manifest contradicts.

#### Sequencing, risks, and what is deliberately not here

**U1 is the only prerequisite in the wave.** U1 → U2 → U3 is a chain with a human gate in the
middle and a GPU gate on top of it. **U4, U5, U6(a)(b), U7, U8, U9 are all independent of the
GPU and of each other** and can run in parallel from day one; only U6(c)'s optional skyline and
U11's interior/exterior claims wait on U3. U11 lands in whichever PR makes its claims true.

- **Risk: the GPU is unavailable and the wave stalls.** It cannot. U2/U3 are the only GPU units and
  eight of eleven units do not touch them. If the window never opens, this wave still ships the
  cast, the crowd, the town, the HUD, the counter, the Watch and the audio.
- **Risk: the U2 gate is skipped under time pressure.** That is the exact failure the gate exists
  to prevent; U3 has no other entry, and §11.10's measurement stands — 42 candidates over 8 recipes
  produced about two right objects, so the bottleneck is art direction, not throughput.
- **Risk: U3 changes art already on screen, including the building the owner likes.** `market.png`
  is re-rendered only as U2's *control*, and is replaced only if the owner picks the new one.
- **Risk: engine tests serialize globally.** U4, U5, U6, U7, U8 and U9 all touch
  `godot/tests`. One run at a time, always the full suite, and the raw `Failed: N, Passed: N` line
  quoted — never a wrapper's verdict (`tools/engine-test.ps1` has computed PASS from an exit code
  twice).
- **Risk: a new asset ships invisible.** Every unit that adds art also adds a manifest-iterating
  guard (KTD-E) and is verified by a `FullPlaytest` run reporting zero art-miss warnings, not by a
  green diff.
- **Risk: the audio format change alters how the game sounds.** Closed, not pending: the four beds
  are OGG/Vorbis now, re-measured within ~0.3 dB of the originals, so no `TrimDb` moved. The old
  U10 plan that lived here — re-master each file as MP3 with a matched fade, and *"do not touch
  `night-still.mp3`"* — was struck rather than corrected. Its premise was already false: the
  session log showed `night-still` wrapping **twice**, more than any other bed, so the one file it
  declared clean was the worst offender. A plan that has been overtaken is an instruction the next
  session obeys, which is why rule 8 says delete it.

**Not here, and deliberately:**
- **No `Scale` knob, no `CanvasShrink` change, no `CameraZoom` change** (KTD-D, KTD-I) — both prior
  bites are named so no unit re-proposes them.
- **No `sim/GameSim/Contracts/` change.** KTD-H routes around the only unit that might have wanted
  one. If a later finding needs one, it is a deny-listed micro-PR authored by the orchestrating
  session, not smuggled into a unit here.
- **No deny-listed file is edited.** `godot/project.godot` in particular: U7 adds no `[input]`
  section, because every binding in this game is registered at runtime and `ShortcutMap` keeps it
  that way. **If a later reviewer wants the bindings moved into the project's Input Map, that is an
  owner ruling, not a unit here.**
- **No new verb, no timer, no participation credit.** U7 is copy and wiring only; U9 stages a
  reveal, not a decision, and only player-crafted items earn an attributed beat; skipping stays
  legal everywhere and its cost stays named in copy.
- **No change to `ArtVariants` itself** — it shipped and is proven. The player smith stays out of a
  pool on purpose (he is one person); hero portraits are §11.10 U8's business, not this wave's.
- **No music generation.** The composed tracks are re-mastered at the seam; nothing new is written.
- **No re-baseline.** Every unit here is presentation; no sim rule, no RNG draw and no golden
  replay moves.

#### What needs an owner ruling before U3

1. **The U2 gate itself** — four buildings and one interior backplate, beside the assets they would
   replace. GPU window required.
2. **`mine-gate` is the largest single change** (10 colours → a real building). Worth a separate
   yes/no inside the gate.
3. **`tavern.png`'s baked scenery.** Removing it makes the tavern match the set and also removes
   the only hedges and roadway in the town. If the owner liked those, they should come back as
   *props*, not as pixels baked into a building.
4. ~~Whether `props-town-well` or `town2d-well` survives~~ — **RESOLVED 2026-08-16.** The
   owner ruled for the well matching the new town: `town2d-well` stays, `props-town-well`
   is deleted — layout entry, AssetSpec, manifest row, and all four PNG/import files.

---

# §11.13 — The tutorial revamp: the world teaches, the chain asks only what the player can cause, and the apprenticeship carries a warrant

**Status: §11.4 path work — the largest untouched item in the owner's playtest notes.** His words:
*"we 100% haven't worked on the tutorial revamp."* He is right. §11.11 U2/U3 designed this and no
PR has landed any of it: `sim/GameSim/Drama/CounterForecast.cs`, `godot/scripts/ui/SurfaceUnlocks.cs`
and `godot/scripts/panels/LessonsPanel.cs` do not exist on this commit, `TutorialAnchorKind` still
has three values (`godot/scripts/ui/TutorialFlow.cs:54-59`), and step 6 still gates on
`CounterSaleClosed` (`godot/scripts/ui/TutorialFlow.cs:301`). This amendment details §11.11 U2/U3
into shippable units, folds in the **R9 ruling** (§11.11, PR #512) which now belongs to this work,
and supersedes §11.11's U2/U3 sections where they disagree. Written in §11.10/§11.11's shape because
the `docs/plans/` two-doc cap stands.

Every unit traces to the owner's own words:
*"you need to be more specific and use in game highlights, hovers etc"* — U2.
*"Features should be unlocked as you go … things greyed out when not needed yet"* — U3.
*"Tutorial 6 doesn't make sense … do i press 'Snuff the lanterns?'"* — U1.
*"Tutorial 7 makes no sense. Why the fuck are we talking about camp when we were just selling something?"* — U1.
*"a guided / repeated tutorial for at least a few days with content unlocked as we go"* — U2 + U3.
*"Heroes should probably not die this early"* — U4 + U5 (R9's answer).

---

## What is actually wrong

Underneath all five complaints is one design error: **the tutorial teaches *about* the world in
prose instead of arranging the world to teach.** The game already owns a pointing mechanism —
`TutorialOverlay` pulses the real building sprite and outlines live HUD controls, resolving eagerly
or throwing rather than pointing at nothing (`godot/scripts/ui/TutorialOverlay.cs:114-133`) — and
then starves it: the pointing stops at the building's door while every gesture, label, and
mechanism explanation is crammed into a ~127px card (`TutorialMaxLines = 6`,
`godot/scripts/ui/ObjectiveTracker.cs:67,71`) sitting above a checklist whose scroll window is
`ChecklistMaxHeight = 32f` pixels — the constant's own doc concedes *"a peek-and-scroll sliver, not
a several-row window, is what fits"* (`godot/scripts/ui/ObjectiveTracker.cs:84-93`). The ten
`TeachNote` paragraphs render one at a time inside that sliver, once per campaign, then become
unreachable forever (`Checklist()` returns empty once `Active` is false,
`godot/scripts/ui/TutorialFlow.cs:826-831`). Meanwhile two of the ten steps are written as "perform
this verb" when the sim's contract is "this verb *may* produce that outcome": step 6 completes only
on a sale `ShoppingAi` decides, step 7's precondition is a stop `RaidConductor`'s own doc calls
*"the UNCOMMON case"* (`godot/scripts/RaidConductor.cs:30`) — both are unfollowable by
construction, which is what "doesn't make sense" means when a playtester says it. Nothing opens
gradually — the entire client has exactly one feature gate, `QuickTravelUnlocked => Completed`
(`godot/scripts/ui/TutorialFlow.cs:377`), while all seven wordless tray books mount on day 1
(`godot/scripts/MainUi.cs:2327-2381`). And the game's own pacing promise — *"The first real lesson
lands around day four, and it lands as a death"* (`docs/design/THE-GAME.md` §3.3) — has no
mechanism behind it at all: death is `hp <= 0` from day 1
(`sim/GameSim/Expedition/ExpeditionResolver.cs:569-572`), which is R9's origin and why "heroes
shouldn't die this early" is a tutorial complaint, not a balance complaint.

So the fix is four moves, each matched to whether the mechanism is missing or merely starved:
**re-gate the two dishonest steps** (mechanism exists, predicate wrong — U1), **move the teaching
onto the world and into a book** (mechanism exists, starved — U2), **build the unlock table**
(mechanism missing — U3), and **build the taught death-grace** (mechanism missing; its engineering
is already proven by the rejected R9(c) build — U4/U5).

---

## Implementation units

Five units. U1 and U2 are both `TutorialFlow`-centric and land in that order on the same lane;
U3 follows U2 (shares the registry idiom and the anchor/gate interaction test); U4 is the wave's
only sim change and is serialized behind its own re-baseline ceremony; U5 lands immediately after
U4 — a warrant that exists but is not yet taught is a hidden shield, the exact failure R9 names,
so U4 and U5 are one owner-visible deliverable in two PRs.

---

### U1 — Every step completes on something the player caused

**Goal.** Steps 6 and 7 stop demanding outcomes heroes decide. Closes *"Tutorial 6 doesn't make
sense"* and *"Tutorial 7 makes no sense."*

**Serves: link1** — a chain that stalls on a coin flip teaches the player the game is broken
before they ever learn what the counter and the vigil are for.

**Mechanism verdict: exists and is mis-gated.** The step machine, the registry, the overlay, the
persistence are all correct. The two `IsDone` predicates are wrong, and one gating note lies.

The defects, precisely:

- **Step 6 (`OpenCounter`)** completes on `state.EventLog.OfType<CounterSaleClosed>().Any()`
  (`godot/scripts/ui/TutorialFlow.cs:301`). The customer states their want first, the want is
  `MissingItemSlots(hero.Gear)[0]`, and on day 2 the shelf was stocked against no signal — the
  modal case is `CustomerWalked`, not `CounterSaleClosed`, and the step sits repeating
  *"press **Open Counter** at the top of the Shop panel, then **Present** a shelved item…"*
  (`godot/scripts/ui/TutorialFlow.cs:570-573`) at a player who has already done all of it.
- **Step 7 (`Vigil`)** completes on `SupplyDelivered` or `PartyRecalled`
  (`godot/scripts/ui/TutorialFlow.cs:309`), whose precondition is a party camping —
  `CheckpointFor(targetFloor) >= 1` requires `targetFloor >= 2` **and** a clean stage-1
  (`sim/GameSim/Expedition/ExpeditionSystem.cs:31,88`), the uncommon case. Its wait copy asserts a
  day gate for something that is not day-gated: *"The vigil is a Day 2 lesson — nothing to do here
  yet; it opens once Day 2 begins"* (`godot/scripts/ui/TutorialFlow.cs:740-741`). Day 2 arrives,
  nothing opens, and the owner reads a tutorial talking about camp while he is mid-sale — the
  exact note. Worse, its two completion verbs (Send/Recall) are the two the plan is actively
  unsure it wants reflexively taught (R1 froze provisioning balance), while "Send them deeper"
  (`godot/scripts/panels/CampPanel.cs:401-408`) — the only verb that always exists — does not count.

**What replaces them.**

*Step 6* re-scopes to what the player controls: **open the counter and answer the customer.**
`IsDone` becomes: `OpenCounterAction` in `state.ActionLog` AND any of
`PresentItemAction`/`SuggestItemAction`/`HaggleResponseAction`/`CloseCounterAction` after it —
all four are real `PlayerAction` records (`sim/GameSim/Contracts/Actions.cs:83-100`), and the
ActionLog-scan idiom already exists in this exact registry for the Commission step
(`godot/scripts/ui/TutorialFlow.cs:336`). Copy names the walk-away as legal:

> *"Tutorial 6/10: The **Shop** — press **Open Counter**. Whoever's first in line says what they
> want before you show anything. Show them something, or hear them out and close the counter.
> A hero who walks is a real answer, not a mistake — a closed sale is the bonus, not the lesson."*

(§11.11 U2's "the Forecast board named them last night" clause is deferred: it is only true once
§11.11 U1's `CounterForecast` ships, which has not happened. One-line copy amendment when it does.)

*Step 7* re-scopes to **understanding the stop**: completion on seeing the camp card at all — a
new `NotifyCampCardShown()` hook in the exact shape of `NotifyMirrorOpened`
(`godot/scripts/ui/TutorialFlow.cs:945-952`), called from `MainUi.SyncCampModal`
(`godot/scripts/MainUi.cs:989`), which already raises `CampPanel.ShowModal`
(`godot/scripts/panels/CampPanel.cs:100`) the moment a party parks — or on any of the three camp
verbs including Send Deeper. The lying day-gate copy is replaced by a **conditional** gating note
readable off `MusterPlan.Compute` (`sim/GameSim/Heroes/MusterSystem.cs:22`), which the Morning
bell already trusts:

- no party aiming past floor 1: *"No stop today — everyone's going one floor down. It fires on a
  run that's aiming deeper."*
- a party staged deeper: *"They'll stop below the checkpoint if they get there clean. When they
  do, the world waits — there is no clock on it."*

*The chain never strands on the coin flip.* `EveningClose`'s row gains
`AdvanceFrom: [Vigil, EveningClose]` — the same unconditional-sweep idiom `WatchDeparture`
already uses across day 1 (`godot/scripts/ui/TutorialFlow.cs:273-277`) — so a campaign where no
party ever camps moves past step 7 at day 3 instead of riding the silent `BackstopDay = 4` close
(`godot/scripts/ui/TutorialFlow.cs:915-918,936`). The checklist renders the skipped row honestly:
a new `Skipped` flag on `ChecklistRow` draws *"— didn't come up this time; it's in Lessons"*
instead of a false tick or an eternal ○.

**Files.**

- *modify* `godot/scripts/ui/TutorialFlow.cs` — steps 6/7 rows (`IsDone`, `StepText`, `WaitText`,
  `GatingNote`), `NotifyCampCardShown`, `EveningClose.AdvanceFrom`, `ChecklistRow.Skipped`.
- *modify* `godot/scripts/MainUi.cs` — one call in `SyncCampModal`.
- *modify* `godot/scripts/ui/ObjectiveTracker.cs` — render the `Skipped` row state.
- *test* `godot/tests/TutorialRegistryConformanceTests.cs` (extend)
- *test* `godot/tests/TutorialCopyIsFollowableTests.cs` (extend)

**Approach.** Registry rows only — no parallel structure (the registry replaced exactly that
class of defect, `godot/scripts/ui/TutorialFlow.cs:90-100`). The new conformance test is KTD-4
made executable: **`EveryStepsCompletionFact_IsReachableByPlayerActionAlone`** — for every row,
build a state whose EventLog/ActionLog contains only player-submitted actions and their immediate
consequences (no `ShoppingAi` buy, no parked party) and assert `IsDone` can flip true, with the
two UI-navigation rows (`LookIn`, `MeetHeroes`) and now step 7's card-shown hook exempted by
their declared shape (`IsDone: _ => false` + Notify), which the suite already recognizes.

**Patterns to follow.**

- `godot/scripts/ui/TutorialFlow.cs:336` — the ActionLog completion scan (Commission row).
- `godot/scripts/ui/TutorialFlow.cs:945-966` — the Notify-hook shape for UI-only facts.
- `godot/scripts/ui/TutorialFlow.cs:273-277` — the unconditional-sweep `AdvanceFrom` idiom.
- `godot/scripts/panels/CampPanel.cs:235` — `GateButton(legal, whyNot)`: mirror the kernel's
  guard, never enforce one.

**Test scenarios.**

1. `Step6_Completes_WhenTheCustomerWalks_WithoutASale` — the exact case that stalled the owner.
2. `Step6_AlsoCompletes_OnAClosedSale` — the happy path unchanged.
3. `Step6_DoesNotComplete_OnOpenAlone` — opening and abandoning is not answering.
4. `Step7_Completes_OnSeeingTheCampCard_WithNoVerbPressed`
5. `Step7_Completes_OnSendDeeper` — the verb the plan is comfortable teaching.
6. `Step7_GatingNote_SaysNoStopIsComing_WhenEveryPartyTargetsFloor1` — read from `MusterPlan`.
7. `Step7_NeverClaimsADayGate_ForAConditionThatIsNotDayGated` — regression pin on the lie.
8. `ChainReachesMeetHeroes_OnDay3_WhenNoPartyEverCamped` — the anti-stranding sweep.
9. `SkippedRow_RendersTheDidntComeUpState_NeverAFalseTick`
10. `EveryStepsCompletionFact_IsReachableByPlayerActionAlone` — KTD-4, all rows, pinned forever.

**Verification.** Fast lane green; full engine suite, raw `Failed: N, Passed: N` quoted against
the `ENGINE_MIN_PASSED` floor. Manual: day-2 counter with a Pass verdict — step 6 advances on the
walk; a floor-1-only day — step 7's note says so and day 3 still reaches step 9.

---

### U2 — The teaching moves onto the world; the card goes on a diet; the lessons stay

**Goal.** Close *"be more specific and use in game highlights, hovers etc"* and the repeatable
half of *"guided / repeated tutorial."*

**Serves: link1** — the player who cannot reliably find the anvil never makes the thing the whole
chain keys on.

**Mechanism verdict: the pointing exists and is starved; the repeat is genuinely missing.**
`TutorialOverlay` already pulses buildings in world space and outlines HUD controls by name,
throwing on a miss (`godot/scripts/ui/TutorialOverlay.cs:114-133`); `Building2D.SetTutorialPulsing`
exists (`godot/scripts/town2d/Building2D.cs:293`). What it cannot do is point at a *station* —
the anvil, the vendor, the counter — so "Inside, press **E** at a station" lives as a sentence in
the card instead of a pulse on the thing (`godot/scripts/ui/TutorialFlow.cs:541-546`). Every
interior already carries typed stations with `Action`/`Focus`/`HoverLine`/`FlavorLine`
(`godot/scripts/town2d/InteriorLayout2D.cs:80`), and `Town2D` already re-emits `StationActivated`
with the whole spec (`godot/scripts/town2d/Town2D.cs:159-165`). The seam is there; nothing points
through it.

**Three changes.**

1. **`TutorialAnchorKind.Station`.** A fourth anchor kind: `TutorialAnchor.ForStation(venueKey,
   stationId)` — outside the venue it behaves as the Building anchor (pulse the door); once the
   player is inside (`MainUi.CurrentLocationPanelId` already distinguishes this,
   `godot/scripts/ui/TutorialFlow.cs:497-501`), the overlay pulses the station sprite itself.
   `BuyMaterial`/`Craft` re-point at the vendor and the profession's crafting station; step 6 at
   the Shop's `counter` station (`godot/scripts/town2d/InteriorLayout2D.cs:181`). Same house rule:
   eager resolve or throw, and `TutorialRegistryConformanceTests` resolves every `Station` anchor
   against the real room at test time (mirroring
   `Registry_EveryBuildingAnchor_ResolvesAgainstTownLayout2D`,
   `godot/tests/TutorialRegistryConformanceTests.cs:109`).
2. **The Lessons book.** A read-only tray panel rendering all ten `ShortLabel` + `TeachNote`
   rows at full height, current row marked, readable forever — after completion, after dismissal.
   The ten paragraphs are already written and already pinned non-empty
   (`godot/tests/TutorialRegistryConformanceTests.cs:81`); today they render one at a time inside
   a 32px sliver and then never again. Zero new teaching copy; one new place to read it. This is
   the whole answer to "repeated": re-reading beats re-running, and nothing about the chain's
   never-regress design (`godot/scripts/ui/TutorialFlow.cs:354-356`) has to change.
3. **The card diet.** With gestures on the stations and mechanisms in the book, each step's card
   copy shrinks to pointer + verb (*"Tutorial 1/10: The Forge — buy copper at the vendor, then
   craft at the anvil"*). `TutorialMaxLines` drops 6 → 3 and the reclaimed ~65px goes to
   `ChecklistMaxHeight` (32f → ~90f), all inside the existing 260px
   `HudBoundsTests.ObjectiveChip_HeightTracksContent_NotFixedEmptyPanel` pin, which is never
   relaxed (`godot/scripts/ui/ObjectiveTracker.cs:84-93` documents the budget arithmetic — a
   3-line card frees the sliver into a real window). The followability suite's six-line budget
   test (`godot/tests/TutorialCopyIsFollowableTests.cs:175`) tightens to the new budget so verbose
   copy can never creep back.

**Files.**

- *modify* `godot/scripts/ui/TutorialFlow.cs` — `TutorialAnchorKind.Station`, re-pointed rows,
  shortened `StepText` copy.
- *modify* `godot/scripts/ui/TutorialOverlay.cs` — the `Station` resolve branch (pulse door
  outside, station inside).
- *modify* `godot/scripts/town2d/Town2D.cs` — read-only station lookup by `(venueKey, stationId)`
  for the overlay; no change to `StationActivated`.
- *create* `godot/scripts/panels/LessonsPanel.cs` — the book.
- *modify* `godot/scripts/MainUi.cs` — register `"Lessons"`, tray button via the existing
  `TrayButton` shape (`godot/scripts/MainUi.cs:3036`), tooltip a real sentence like
  `RenownTrayTooltip` (`godot/scripts/MainUi.cs:1510`).
- *modify* `godot/scripts/ui/ObjectiveTracker.cs` — `TutorialMaxLines`, `ChecklistMaxHeight`.
- *test* `godot/tests/TutorialRegistryConformanceTests.cs` (extend — Station anchors resolve)
- *test* `godot/tests/TutorialCopyIsFollowableTests.cs` (extend — tightened budget)
- *test* `godot/tests/panels/LessonsPanelTests.cs`

**Patterns to follow.**

- `godot/scripts/ui/TutorialOverlay.cs:114-133` — the `switch (anchor.Kind)` eager resolve; add a
  case, never a parallel path.
- `godot/scripts/panels/DemandPanel.cs` — the read-only book idiom for `LessonsPanel`.
- `godot/scripts/MainUi.cs:2362-2365` — the Demand tray button registration (the newest, cleanest
  instance of the shape).

**Test scenarios.**

1. `EveryStationAnchor_ResolvesAgainstARealRoomStation` — conformance, all rows, both halves
   (venue exists AND station id exists in that room).
2. `StationAnchor_PulsesTheDoorOutside_AndTheStationInside`
3. `LessonsPanel_RendersAllTenTeachNotes_AfterTheChainIsComplete`
4. `LessonsPanel_RendersAllTenTeachNotes_AfterDismiss` — a dismiss must not destroy the lessons.
5. `NoStepsCopy_OutgrowsTheNewThreeLineBudget` — the tightened followability pin.
6. `ObjectiveCard_HeightStaysWithinTheExisting260pxPin` — never relaxed.
7. `ChecklistWindow_ShowsAtLeastThreeRowsWithoutScrolling` — the sliver is dead; pin its death.

**Verification.** Full engine suite, raw counts quoted. Manual (render-and-look, per repo memory):
fresh campaign — the vendor itself pulses, then the anvil; open Lessons after dismissing — all ten
paragraphs there; checklist shows several rows at rest.

---

### U3 — The town opens as you learn it

**Goal.** Close *"Features should be unlocked as you go when tutorial is active and things greyed
out when not needed yet."*

**Serves: link2** — the four channels arrive one at a time, so each is learned as a channel
rather than as one of seven wordless icons.

**Mechanism verdict: missing.** One gate exists in the whole client
(`godot/scripts/ui/TutorialFlow.cs:377`); all seven tray books mount on day 1
(`godot/scripts/MainUi.cs:2327-2381`).

**Approach.** §11.11 U3's design stands — one ordered unlock table (`SurfaceUnlocks.IsOpen`,
pure over `GameState`, never persisted), gates being the moment each surface first has anything
true to say (Ledger on first `PartyDeparted`, Forecast on first Evening, Renown on first
`ItemSold` to a hero, Commissions on the first sim-posted commission, Demand on first
`HeroPassedOnItem`, Legends on first `AttributionBeatEvent`, Progress on first `BountyPaid` via
the existing `SecondProfessionMilestoneReached`, `godot/scripts/ui/TutorialFlow.cs:1027`). Two
details are settled here, per the owner's exact wording:

- **Greyed, not hidden.** A closed surface's tray button renders disabled with its gate as the
  tooltip (*"Opens when a party first returns — nothing to read yet"*), so the tray teaches its
  own shape instead of reflowing. Unlock fires the one-line arrival toast; no modal.
- **A gate never hides a tutorial target.** Pinned:
  `EveryTutorialStepAnchor_PointsAtASurfaceThatIsOpenByThatStepsMinDay` — the one trap this unit
  could create.

`ActionReachabilityCensusTests` stays green by extending each gated surface string with its gate
(KTD-3 of §11.11 — a gated surface is a *recorded* surface, never an exclusion).

**Files.**

- *create* `godot/scripts/ui/SurfaceUnlocks.cs` — the table + `IsOpen(GameState, surfaceId)` +
  per-surface arrival line.
- *modify* `godot/scripts/MainUi.cs` — tray buttons consult it every `RefreshHud`; disabled state
  + tooltip; arrival toast on the open transition.
- *modify* `godot/tests/ActionReachabilityCensusTests.cs` — surface strings carry gates.
- *test* `godot/tests/ui/SurfaceUnlocksTests.cs`
- *test* `godot/tests/MainUiTests.cs` (extend)

**Patterns to follow.** `godot/scripts/ui/TutorialFlow.cs:225-338` — the registry-row idiom;
`godot/scripts/panels/CampPanel.cs:235` — disabled-with-reason, never enforce; §11.11 KTD-5 —
derived, never persisted (`user://` outliving a campaign is exactly what produced "the tutorial
is missing", `godot/scripts/ui/TutorialFlow.cs:1139-1150`).

**Test scenarios.** §11.11 U3's seven stand unchanged (deny-by-default census; closed-before /
open-after per gate; re-derives after reload; monotonic one-way; census green; anchor/gate
interaction), plus:

8. `AClosedSurfacesTrayButton_IsDisabledWithItsGateAsTooltip_NeverAbsent` — "greyed out", the
   owner's own word, pinned.

**Verification.** Engine suite green including the census. Manual: fresh campaign — tray starts
grey except what day 1 needs; each book arrives with its line as its fact first lands.

---

### U4 — The apprenticeship warrant, in the sim

**Goal.** Build R9's ruled mechanic: no hero dies during the tutorial's three days — as a taught,
dated town rule, not a silent balance clamp. Closes the mechanism half of *"Heroes should
probably not die this early."*

**Serves: link5** — a day-1 death is a memory with nobody's name in it; the warrant guarantees
the first death lands after the player has hands, which is what makes it a link-5 moment.

**Mechanism verdict: missing, with the engineering already proven.** The rejected R9(c) build
(§11.11) established everything reusable: the grace clamp rides `CombatEvent.ModifierHpDelta` —
the Leech rune's existing ledger channel (`sim/GameSim/Contracts/Expedition.cs:39`, applied at
`sim/GameSim/Expedition/ExpeditionResolver.cs:539-561`) — so attribution's HP replay stays
byte-consistent and **no `Contracts/` change is needed**; `DamageTaken` keeps recording the true
lethal roll (`sim/GameSim/Contracts/Expedition.cs:26`) so the near-death is legible; survival ends
at 1 HP and the existing `CombatMath.ShouldFlee` check
(`sim/GameSim/Expedition/ExpeditionResolver.cs:503`) sends the hero home next round — no new
retreat path. Only the *trigger* was wrong: (c) keyed on transaction history, which made Prepared
heroes 2.6× deadlier than Reckless (55.8% vs 21.4%) because buying a salve forfeited protection.

**The trigger, redesigned.** The warrant is a **dated window: days 1 through 3**, the
apprenticeship's own three days — `ApprenticeWarrant.Covers(day) => day <= LastGraceDay` with
`LastGraceDay = 3`, a named sim constant. Threaded into the resolver as a boolean from
`ExpeditionSystem.Process` and `ExpeditionDeepSystem` (both read `state.Day`), mirroring exactly
how `RetreatExemption` already threads (`sim/GameSim/Expedition/ExpeditionSystem.cs:86,92,97` —
an opaque parameter; the resolver still decides every fight on combat math alone and reads no
trait, no transaction, no calendar of its own — so no pinned law exception is required, same as
(c)'s build found). Why a date and not a state:

- It cannot recreate (c)'s inversion: no player behavior feeds it, so arming a hero can never be
  what kills them, and no hero is ever permanently immortal.
- It is trivially deterministic (`state.Day` is already state) and trivially teachable: the copy
  can name the end — *"dawn of Day 4"* — which a stake-shaped rule never could.
- It satisfies every R9 fixed point: taught (U5), visible when it fires (the true roll +
  counteracting delta are both in the event stream), ends where the tutorial ends — the chain's
  own unconditional outer end is `BackstopDay = 4` (`godot/scripts/ui/TutorialFlow.cs:936`), i.e.
  the warrant expires at the same dawn that closes the chain, and a cross-layer test pins
  `TutorialFlow.BackstopDay == ApprenticeWarrant.LastGraceDay + 1` so neither constant can drift
  alone. It is not a property of an untraded hero and is not coupled to selling — the two
  prohibitions, met by construction.

**Legibility is a shared projection, not a client guess.** `ApprenticeWarrant` also exposes the
pure predicate the client and tests read — *did this combat event's survival come from the
warrant* — derived from the same fold attribution already uses to replay HP
(`sim/GameSim/Expedition/AttributionEngine.cs` is the pattern), so the resolver's clamp and the
screen's claim cannot disagree (§11.11 KTD-1: derive from the function the tick consumes).

**Files.**

- *create* `sim/GameSim/Expedition/ApprenticeWarrant.cs` — `LastGraceDay`, `Covers(day)`, the
  clamp helper the resolver calls, the fired-predicate the client reads.
- *modify* `sim/GameSim/Expedition/ExpeditionResolver.cs` — clamp at the death check
  (`hp <= 0` → held at 1, delta recorded), behind the threaded flag.
- *modify* `sim/GameSim/Expedition/ExpeditionSystem.cs`, `ExpeditionDeepSystem.cs` — thread
  `ApprenticeWarrant.Covers(state.Day)` at both ticks (a vigil resupply can land between them —
  the (c) build's own finding).
- *modify* `sim/GameSim.Tests/Balance/BalanceSimTests.cs` — re-baseline; add the trait-mortality
  flatness assertion (below).
- *modify* `docs/design/THE-GAME.md` §3.3 — the "lands around day four" sentence stops being an
  intention and describes the warrant (same PR; rule 8, git outranks the doc).
- *test* `sim/GameSim.Tests/Expedition/ApprenticeWarrantTests.cs`
- *(re-record)* the golden replay — same seed + actions now produce different early-day state.

**Fixture note (from the (c) build, kept on purpose).** `StagedResolutionTests`' `Naked` heroes
(`sim/GameSim.Tests/Expedition/StagedResolutionTests.cs:23`) call the resolver directly; the
warrant defaults **off** for direct calls (parameter, not ambient state), so those fixtures are
untouched — the day window only exists where the systems thread it. This is the second reason the
trigger lives in the systems, not the resolver.

**Determinism / balance.** Same seed + same actions stays identical — but different from the
*old* recording, so this is a **golden-replay re-record and a full balance re-baseline
(`Category=Balance`)**, serialized in its own PR, never bundled with client work. Verification
must census, not anecdote (repo memory: count shapes before claiming cause): a seed sweep
(`dotnet run --project sim/GameSim.Cli -- batch --seeds 20 --days 100`) before/after, asserting
(a) zero `HeroDied` on days 1–3 after, (b) Prepared-vs-Reckless mortality within noise of the
pre-warrant ratio — the (c) failure shape, pinned as a balance test, (c) `MinAliveAtEnd = 3`
(`sim/GameSim.Tests/Balance/BalanceSimTests.cs:34`) and Ending-reachability unchanged across the
suite's seeds.

**Test scenarios.**

1. `WarrantHolds_ALethalRollAtOneHp_OnDay3` — true roll in `DamageTaken`, counteracting
   `ModifierHpDelta`, survivor at 1 HP.
2. `WarrantExpires_AtDay4_SameRollKills` — the boundary, both sides.
3. `HeldHero_FleesNextRound_ViaTheExistingShouldFleeCheck` — no new retreat path.
4. `WarrantedFight_StillRecordsTheTrueLethalRoll_ForAttributionReplay` — HP replay byte-parity.
5. `DirectResolverCalls_AreUnaffected` — the fixture-protection pin.
6. `FiredPredicate_AgreesWithTheClampByConstruction` — same fold, one source.
7. `Balance: NoHeroDiedEvent_OnDays1Through3_AcrossAllSeeds`
8. `Balance: TraitMortalityRatio_IsNotInverted` — the (c) regression, pinned forever.

**Verification.** Fast lane green; balance suite green post-re-baseline with the re-record diff
in the same PR; the seed-sweep census quoted in the PR body (raw numbers, not a wrapper verdict).

---

### U5 — The warrant is taught, fires visibly, and ends on screen

**Goal.** The other three R9 fixed points: taught, visible when it fires, ends where the tutorial
ends. Ships immediately after U4 — **U4 without U5 is a hidden shield, the exact failure R9 names
(§11.11: "a hidden shield is exactly the failure §11.11 exists to fix"), so the wave is not
reportable between them.**

**Serves: link5** — same as U4; this is its legibility half.

**Mechanism verdict: missing (nothing teaches what U4 adds).**

**Three surfaces, all existing seams.**

1. **Taught at the first send-off.** `WatchDeparture`'s `TeachNote`
   (`godot/scripts/ui/TutorialFlow.cs:267-268`) gains the warrant:
   *"While the town's still teaching you — through Day 3 — the Mine doesn't keep anyone: a killing
   blow leaves them at death's door and they limp home. Dawn of Day 4 ends that."*
   Day 3's `MeetHeroes`/`Commission` copy carries the closing reminder (*"Tomorrow the warrant
   ends — what they carry down is what keeps them"*), so the end is named twice before it arrives.
2. **Visible when it fires.** The night's Ledger gets a warrant card, rendered when the
   fired-predicate reads true over the day's combats — leading with the true roll, the same
   honest-register shape as the death cards: *"The blow that landed on Torvald would have killed
   him. The apprenticeship's warrant held — he came home at death's door. Two dawns left on it."*
   One card per fired hero; no narrator line (the spoken library is frozen, §11.11 scope).
3. **Skipping stays legal and its cost is named, never engineered.** The tutorial's dismiss
   (`godot/scripts/ui/TutorialFlow.cs:990-994`, the ✕ at
   `godot/scripts/ui/ObjectiveTracker.cs:186-190`) does **not** end the warrant — a dated town
   rule keyed to a UI preference would need a new `PlayerAction` (a deny-listed `Contracts/`
   amendment) and would make dismissing a tutorial silently change mortality, a hidden cost the
   laws forbid engineering. Instead the dismiss toast names what stands: *"The lessons stay in
   Lessons, and the apprentice's warrant still runs through Day 3."* (Routed as R10 below in case
   the owner wants the coupled version.)

**Files.**

- *modify* `godot/scripts/ui/TutorialFlow.cs` — the two TeachNote/copy amendments; dismiss toast
  copy.
- *modify* `godot/scripts/panels/LedgerModal.cs` — the warrant card, driven by
  `ApprenticeWarrant`'s fired-predicate.
- *modify* `godot/scripts/MainUi.cs` — dismiss toast wiring (reuse the existing toast route —
  check `OnStationActivated`'s flavor-toast path first, per §11.11 U3's own note).
- *test* `godot/tests/panels/LedgerModalTests.cs` (extend)
- *test* `godot/tests/TutorialCopyIsFollowableTests.cs` (extend)
- *test* `godot/tests/TutorialRegistryConformanceTests.cs` (extend — the cross-layer constant pin)

**Patterns to follow.** §11.11 U6's death-card discipline — lead with the specific, honest empty
state, no participation credit; `godot/scripts/ui/TutorialFlow.cs:1007-1017` (`ConsumeLedgerTip`)
— the once-ever ledger-explainer shape, deliberately independent of `Active`.

**Test scenarios.**

1. `WarrantCard_RendersOnANightItFired_WithTheTrueRollNamed`
2. `WarrantCard_NeverRenders_AfterDay3` — and never before it fired.
3. `WarrantCard_CountsRemainingDawns_Correctly`
4. `TeachNote_NamesTheWarrantAndItsEndDate` — copy pin.
5. `DismissToast_NamesTheWarrantSurvivingTheDismiss` — skipping's cost/non-cost in copy, pinned.
6. `BackstopDay_EqualsWarrantLastGraceDayPlusOne` — the cross-layer drift pin.
7. `WarrantCopy_NeverStatesASurvivalNumber` — §11.4's stakes-qualitatively rule, on the rendered
   string.

**Verification.** Engine suite green, raw counts quoted. Manual: seeded campaign known to produce
a day-2 lethal roll — watch the ledger card land, then day 4 — same shape of roll kills, memorial
fires, and THE-GAME.md §3.3's sentence is now a description of a mechanism.

---

## Key technical decisions

**KTD-A — A tutorial step's completion fact is caused by the player, never decided by a hero.**
§11.11 KTD-4, adopted and made executable (U1 test 10). Both broken steps failed identically:
`CounterSaleClosed` needs `ShoppingAi` to say Buy; `SupplyDelivered` needs a party to have parked.
Influence never orders — so a tutorial gated on a hero's decision is a tutorial the game can fail
for you. The conformance test makes the next such step a red build, not a playtest note.

**KTD-B — Teaching lives on the world and in the book; the card carries only the pointer.** The
card is a 320px-wide dock with a hard 260px pin; six lines of prose over a 32px checklist sliver
is the *cause* of "not great," so any redesign that keeps loading that card has fixed nothing.
Gesture teaching → the station's own pulse and `HoverLine` (the seam already exists,
`InteriorLayout2D.StationSpec`); mechanism teaching → the Lessons book (already-written
`TeachNote`s at full height, forever); state teaching (gates) → the checklist's short notes. The
card: one sentence. Enforced from both sides — the tightened line-budget test and the untouched
260px pin.

**KTD-C — The warrant's trigger is a date, not a state.** The (c) build proved the mechanism and
disproved the trigger: any player-behavior trigger lets player behavior forfeit protection, which
inverted mortality 2.6×. A day window cannot invert anything, costs no `Contracts/` change, names
its own end in copy, and expires at the same dawn (`Day 4`) as the chain's own unconditional
backstop — pinned together by test so neither moves alone. The resolver still reads only combat
math plus an opaque flag threaded exactly like the bounty's `retreatExemptHeroes` — no law
exception needed.

**KTD-D — The warrant defaults off at the resolver's own seam.** Direct `ExpeditionResolver`
calls (all existing fixtures, all balance micro-tests) see no warrant; only the two system ticks
thread it from `state.Day`. This is what makes the (c) build's fixture trap (`Naked` heroes
becoming structurally unkillable) impossible here by construction rather than by fixture surgery.

**KTD-E — One legibility source.** The ledger card, the tests, and the clamp all read
`ApprenticeWarrant` — the same repo discipline as `RaidForecast` byte-matching the tick and the
counter's want matching `WantLine` (§11.11 KTD-1). A warrant card that could disagree with the
resolver about whether the warrant fired is worse than no card.

**KTD-F — Unlocks derive, never persist; grey, never hide.** §11.11 KTD-5 adopted (the
`user://`-flag-outliving-a-campaign defect is documented in this very file's history,
`godot/scripts/ui/TutorialFlow.cs:1139-1150`), plus the owner's own word: a closed book is
visible, disabled, and says what opens it.

---

## Rulings owed

- **R10 — should dismissing the tutorial also end the warrant?** Recommended default: **no** —
  the warrant runs its dated course and the dismiss toast says so (U5.3). Ending it on dismissal
  requires a new `PlayerAction` in `sim/GameSim/Contracts/` (deny-listed — an
  orchestrator-authored micro-PR) so the sim can see the dismissal deterministically, and it
  couples a UI preference to hero mortality, a cost the player would pay without being told at
  press time. If ruled **yes**, it is a Contracts amendment + a second balance touch, its own PR.
- **R11 — `LastGraceDay = 3` confirmed?** The tutorial's own three days, expiring at the dawn the
  chain's backstop closes. A longer warrant (e.g. through day 5, nearer THE-GAME §3.3's "around
  day four" prose) decouples it from "ends where the tutorial ends," so 3 is the recommended
  default; any other value is the owner's call, same re-baseline either way.
- *(Standing, from §11.11, unchanged by this amendment:)* R7 mid-vigil materials (default no) and
  R8 party rotation (default rotate-by-day, re-baseline) are not re-asked here; U1's step-7 copy
  deliberately teaches the stop without teaching Send-reflexively, staying clear of R1's freeze.

---

## Scope boundaries — what this wave does not do

- **No `CounterForecast` and no forecast-board work.** That is §11.11 U1, un-built and un-owned
  here; step 6's copy gains its "named them last night" clause in a one-line amendment when it
  lands.
- **No sim change outside U4.** U1/U2/U3/U5 are Godot-side only; `TutorialFlow` keeps reading
  `GameState` and mutating nothing; `user://tutorial_flow.json` stays out of the sim save (KTD2).
- **No new narrator lines.** The warrant card is ledger text; the spoken library stays frozen and
  append-only.
- **No timers, anywhere.** The warrant is a rule about outcomes, not a clock on a decision;
  `VigilStop`'s indefinite hold (`godot/scripts/RaidConductor.cs:206-208`) and every phase timer
  are untouched. No tutorial step acquires a countdown.
- **No forced verbs.** Every step remains advisory: the chain is dismissible, `Advance` only ever
  reads facts, and U1 *widens* what counts as done — nothing gates play on compliance. Skipping
  a step costs exactly what the copy says (the lesson waits in Lessons; the warrant runs
  regardless).
- **No changes to camp verbs, provisioning balance, party formation, or death staging** — R1's
  freeze and §11.11 U4/U5/U6 all stand as scoped there.
- **No difficulty/assist system.** The warrant is not a toggle, not an option, not extendable by
  the player; it is three days of town, then the game.
- **No third plan doc.** This is a §11 amendment; `docs/plans/` stays at two.

---

## Sequencing

| # | Unit | Size | Blocked by | Sim diff | Re-baseline |
|---|------|------|-----------|----------|-------------|
| U1 | Steps complete on player-caused facts | session | nothing | no | no |
| U2 | Teaching onto the world + Lessons + card diet | session | U1 (same file, serial) | no | no |
| U3 | The town opens as you learn it | session | U2 (registry idiom + anchor/gate test) | no | no |
| U4 | The apprenticeship warrant (sim) | session | nothing (parallel lane) | **yes** | **yes — golden re-record + Category=Balance, own PR** |
| U5 | Warrant taught, visible, ends on screen | session | U4 (must land immediately after) | no | no |

U1→U2→U3 is one lane (all touch `TutorialFlow`/`ObjectiveTracker`); U4→U5 is the other, and the
two lanes are file-disjoint until U5 touches `TutorialFlow` copy — U5 therefore rebases on
whichever of U2/U3 has landed. Engine tests serialize regardless (repo memory: never two gdUnit
runs at once). Every PR body carries `Serves:` per §11.6 rule 3; U4's quotes the seed-sweep
census raw.

---

# §11.13 amendment — the first death is part of the tutorial (U4/U5 replaced)

**Status: owner overrule, 2026-08-16.** Asked to pick a grace window, he answered:

> *"Again dude, we want the first death to be part of the tutorial"*

The "again" is the finding. The previous U4/U5 (`docs/design/MAKERS-MARK.md:3731-3889`) built a
warrant that protects heroes for three days and then **silently expires at dawn 4** — the tutorial
closes (`BackstopDay = 4`, `godot/scripts/ui/TutorialFlow.cs:1219`), and the first death lands
days later as an ordinary night, unframed, with the tutorial long gone. That is a balance window
wearing teaching copy. What he asked for — twice — is that the apprenticeship *delivers* the first
loss: the player is prepared for it, watches it happen, understands what it cost, and is shown
what the town does with it. This amendment replaces §11.13's U4 and U5 with U4a/U4/U5/U6 below.
Everything else in §11.13 (U1-U3, landed) stands.

---

## Framing — what "part of the tutorial" means mechanically

**The tutorial owns the death's preparation and its meaning. The sim alone owns its cause.**
Three commitments and one prohibition:

1. **No unframed death.** While the apprenticeship's warrant holds (days 1-3, or until the player
   walks out of it — below), no hero dies: a lethal roll is held at 1 HP through the proven
   `ModifierHpDelta` channel, the true roll stays in `DamageTaken`, and the survivor limps home via
   the existing flee check. Every near-death is shown as a forewarning: *"that blow would have
   killed him — the warrant held."* The player watches death rehearse before it performs.
2. **The warrant's end is a beat, not an expiry.** Its dawn is named at the first send-off, named
   again on day 3, and the dawn itself is staged once: *"From today the Mine keeps what it
   takes."* The old design's exact defect — the grace ending as a silent calendar fact — is the
   one thing this list exists to kill.
3. **The tutorial stays armed, quietly, until the sim produces the first death — then teaches
   it.** The taught chain still completes on day 3-4 exactly as today (quick-travel unchanged,
   `TutorialFlow.cs:461`); what remains is one dormant act that renders **nothing** until the
   campaign's first `HeroDied` lands, then wakes for that night and the day after: the ledger's
   fate card carries a once-ever first-loss teaching block, one pointed step walks the player to
   the wall and the rite, and the lesson joins the Lessons book forever. Whoever dies, wherever,
   whenever — the act frames whichever death the dice produced.

**The prohibition:** no code path may select, schedule, weight, hasten, or delay a death. The
warrant's clamp inside a told window is the design's entire sim-side footprint. The first death
is real, permanent, and unscripted — the tutorial authored the frame around the picture, never
the picture.

**How this stays compatible with permadeath-by-hero-judgment (link 3).** Heroes still form
parties without the player (`PartyFormation.FormParties`,
`sim/GameSim/Expedition/ExpeditionSystem.cs:39`), pick their own depth
(`TargetFloorFor`, `ExpeditionSystem.cs:140-156`), and die by arithmetic
(`hp <= 0`, `sim/GameSim/Expedition/ExpeditionResolver.cs:569-572`). The warrant converts
outcomes only inside a window the player was told about in advance — the shape R9 already ruled
legal as a *taught mechanic* (`MAKERS-MARK.md:2421-2451`). Past that window the game is
unmodified. A death the player was prepared for is still a death the hero chose.

**R10 falls out instead of being ruled.** If the death belongs to the tutorial, then the warrant
is part of the *taught version* of the game — so dismissing the tutorial is not muting a UI; it is
walking out of the apprenticeship, warrant included. The dismiss ✕
(`godot/scripts/ui/ObjectiveTracker.cs:198-202`) becomes a confirmed graduation: one press ends
the chain *and* submits the sim-visible action that ends the warrant, with the cost named in the
confirm at press time. No second rule, no hidden shield, no UI preference silently steering
mortality — the sim sees a logged, deterministic `PlayerAction`, same as every other decision.
(This is the one piece that needs a `Contracts/` amendment — routed as **R12** below, never
assumed.)

---

## The hard question, answered — how do you author a death without scripting it?

Ranked, with the losers named:

1. **CHOSEN — prepare, stage the end, then frame whichever death the sim produces first.** The
   dated warrant answers *"heroes should probably not die this early."* The dawn beat answers
   *"a guided tutorial with content unlocked as we go"* — the last unlock is mortality itself.
   The dormant loss act answers *"the first death is part of the tutorial"* without the sim ever
   choosing a victim. Each owner sentence maps to one mechanism; none requires the dice to lie.
2. **Narrate whichever death comes first, no grace at all.** Honest, zero sim change — but a
   day-1 death can land before the player has made anything, which is a memory with nobody's name
   in it (the old U4's own link-5 argument, `MAKERS-MARK.md:3737-3738`) and is the owner's other
   note verbatim. Rejected as incomplete, and folded in as option 1's third act.
3. **The warrant ends when the tutorial chain completes (not on a date).** Teachable ("it ends
   when you finish") — but it couples mortality to tutorial *progress*: slow-walking the
   Commission step becomes an immortality lever, which is the R9(c) failure class (player
   behavior moving the shield) with a new trigger. Capped by a dawn-4 backstop it degenerates
   into option 1 anyway. Rejected; option 1's only behavioral lever is one explicit, confirmed
   opt-out that names its cost.
4. **Dated grace that silently expires (the previous U4/U5).** The version the owner overruled:
   the death is real and warned-about, but it happens *outside* the tutorial. Kept only as the
   R12-declined fallback for the dismissal coupling (below), never for the staging.
5. **Script a death (force one on day 4).** Breaks link 3, law 4 ("show only what the sim
   decided"), and the game's thesis in one move. Not an option at any price.

---

## Implementation units

Four units. U4a is a deny-listed micro-PR gated on R12. U4 is the wave's only balance-affecting
sim change, serialized behind its own re-record ceremony. U4 without U5 is a hidden shield — the
exact failure R9 names — and U4+U5 without U6 is the overruled design again (a warrant whose end
leads nowhere), so **the wave is not reportable until U6 is merged.**

---

### U4a — The graduation action (Contracts micro-PR; blocked on R12)

**Goal.** Give the sim a deterministic way to see "the player walked out of the apprenticeship,"
so dismissal can end the warrant without the client whispering to the resolver.

**Serves: link3** — mortality must never be steered by a `user://` flag the sim cannot replay;
the ActionLog is the only honest channel.

**Files.**
- *modify* `sim/GameSim/Contracts/Actions.cs` — `ConcludeApprenticeshipAction` (no payload) +
  its `JsonDerivedType` row (**deny-listed — orchestrator-authored micro-PR, per the CLAUDE.md
  contract-amendment rule; merged before U4/U5**).
- *modify* `sim/GameSim/Kernel/ActionTiming.cs` — immediate lane (the pattern at
  `ActionTiming.cs:93` for `OpenCounterAction`).
- *modify* `sim/GameSim/Advisor/ActionLegality.cs` — legal in any phase, spends **no** action
  slot (it is a stance, not an economy verb), idempotent (a second submit is a no-op), and a
  no-op after day `LastGraceDay` (the warrant is already gone).
- *modify* `godot/tests/ActionReachabilityCensusTests.cs` — the new action's surfaced path is
  U5's dismiss confirm (a gated surface is a *recorded* surface, §11.11 KTD-3).
- *test* `sim/GameSim.Tests/Kernel/` — legality/timing/idempotence rows in the existing
  conformance suites (`ActionTimingConformanceTests`).

**Approach.** The action mutates nothing by itself — its entire meaning is its presence in
`GameState.ActionLog` (the same durable-fact idiom the tutorial's own Commission predicate reads,
`godot/scripts/ui/TutorialFlow.cs:410`). No new state field, no new event.

**Test scenarios.**
1. `ConcludeApprenticeship_SpendsNoActionSlot`
2. `ConcludeApprenticeship_IsIdempotent_SecondSubmitChangesNothing`
3. `ConcludeApprenticeship_AfterLastGraceDay_IsALegalNoOp`
4. Census: the action appears in the reachability census with its gate string.

**Verification.** Fast lane green. Golden replay untouched (no recorded campaign contains the
action). No re-baseline — this PR alone changes no outcome.

---

### U4 — The apprenticeship warrant, in the sim (trigger revised)

**Goal.** No hero dies while the apprenticeship holds — as a taught, dated town rule the player
can also walk out of. Closes the mechanism half of *"heroes should probably not die this early"*
without recreating the R9(c) inversion.

**Serves: link5** — a day-1 death is a memory with nobody's name in it; the warrant guarantees
the first death lands after the player has hands and after the tutorial has taught what a loss
means, which is what makes it a link-5 moment.

**Mechanism: proven, reused verbatim from the rejected R9(c) build** (`MAKERS-MARK.md:2453-2464`
— do not re-derive):
- The clamp rides `CombatEvent.ModifierHpDelta` — the Leech rune's existing ledger channel
  (`sim/GameSim/Contracts/Expedition.cs:32-39`, applied at
  `sim/GameSim/Expedition/ExpeditionResolver.cs:538-548`) — so attribution's HP replay stays
  byte-consistent and **no `Contracts/` change is needed for the clamp itself**.
- `DamageTaken` keeps recording the true lethal roll (`Contracts/Expedition.cs:26`), so the
  near-death is legible rather than an invisible cap.
- Survival ends at 1 HP; the existing `CombatMath.ShouldFlee` check
  (`ExpeditionResolver.cs:503`) sends the hero home next round — no new retreat path.
- Threaded exactly like the bounty `retreatExemptHeroes` parameter
  (`ExpeditionSystem.cs:86` → resolver calls at `ExpeditionSystem.cs:92,97-98`;
  `ExpeditionDeepSystem.cs:40-42`): an opaque boolean, recomputed at **both** ticks (a vigil
  resupply can land between them — the (c) build's own finding). The resolver still decides every
  fight on combat math alone and reads no trait, no transaction, no calendar of its own — **no
  pinned law exception required**, same as the (c) build found.

**The trigger.** `ApprenticeWarrant.Covers(state)` =
`state.Day <= LastGraceDay && !Concluded(state)`, with `LastGraceDay = 3` (a named sim constant)
and `Concluded(state)` a scan of `state.ActionLog` for `ConcludeApprenticeshipAction` — pure,
deterministic, monotonic (an append-only log can only ever turn it truer). Why this trigger and
not the alternatives:
- **It cannot recreate the (c) inversion.** The measured disaster (Prepared 55.8% vs Reckless
  21.4% mortality, 2.6× inverted, `MAKERS-MARK.md:2427-2437`) came from commerce feeding the
  predicate. Here no purchase, sale, provision, or tutorial progress moves the window — the only
  player behavior that does is one explicit, confirmed opt-out that names its cost at press time.
  No hero is ever permanently shielded (the date caps it) and arming a hero can never kill them.
- **It is teachable.** The copy names the end twice before it arrives (*"dawn of Day 4"*), which
  a stake-shaped rule never could.
- **It ends where the tutorial ends, both ways.** Dawn 4 is the chain's own unconditional close
  (`BackstopDay = 4`, `TutorialFlow.cs:1219`) — the cross-layer pin
  `TutorialFlow.BackstopDay == ApprenticeWarrant.LastGraceDay + 1` keeps them from drifting
  apart. And an early exit from the tutorial is an early exit from the warrant, by the same
  logged action — one principle, two layers, zero clauses.
- **Fixture protection by construction (old KTD-D, kept).** The warrant is a parameter defaulting
  **off** at the resolver's own seam (`Resolve`/`ResolveStage1`/`ResolveStage2` signatures at
  `ExpeditionResolver.cs:17,69,140`); only the two system ticks thread it from state. Direct
  resolver calls — all existing fixtures, all balance micro-tests, `StagedResolutionTests`' Naked
  heroes — see no warrant and need no surgery.

**Legibility is a shared projection (old KTD-E, kept).** `ApprenticeWarrant` also exposes the
pure fired-predicate — *did this combat's survival come from the warrant* — derived from the same
fold attribution uses to replay HP, so the resolver's clamp, the ledger card, and the tests can
never disagree.

**Files.**
- *create* `sim/GameSim/Expedition/ApprenticeWarrant.cs` — `LastGraceDay`, `Covers(GameState)`,
  `Concluded(GameState)`, the clamp helper the resolver calls, the fired-predicate the client
  reads.
- *modify* `sim/GameSim/Expedition/ExpeditionResolver.cs` — clamp at the death check
  (`hp <= 0` held at 1, delta recorded as `ModifierHpDelta`), behind the threaded flag.
- *modify* `sim/GameSim/Expedition/ExpeditionSystem.cs`, `ExpeditionDeepSystem.cs` — thread
  `ApprenticeWarrant.Covers(state)` at both ticks.
- *modify* `sim/GameSim.Tests/Balance/BalanceSimTests.cs` — re-baseline.
- *modify* `docs/design/THE-GAME.md` §3.3 — the amendment at the end of this doc, same PR
  (rule 8: the sentence stops being an intention the moment the mechanism exists).
- *test* `sim/GameSim.Tests/Expedition/ApprenticeWarrantTests.cs`
- *(re-record)* the golden replay — same seed + actions now produce different early-day state.

**Patterns to follow.**
- `ExpeditionSystem.cs:126-129` — `RetreatExemption`: the opaque-parameter threading shape.
- `ExpeditionResolver.cs:538-548` — the Phase C U-C1 `ModifierHpDelta` discipline (apply after
  the round's damage, record exactly what was applied, 0 means no behavior change).
- `ConsumableTraitMortalityBalanceTests.cs` — the seed-sweep census shape for balance claims.

**Test scenarios.**
1. `WarrantHolds_ALethalRollAtOneHp_OnDay3` — true roll in `DamageTaken`, counteracting
   `ModifierHpDelta`, survivor at 1 HP.
2. `WarrantExpires_AtDay4_SameRollKills` — the boundary, both sides.
3. `WarrantEnds_TheTickAfterConcludeApprenticeship` — conclude on day 2; the next resolution
   tick's lethal roll kills. (Both ticks read state fresh, so a mid-day conclude is honest:
   "starting with the next fight," which the confirm copy says.)
4. `HeldHero_FleesNextRound_ViaTheExistingShouldFleeCheck` — no new retreat path.
5. `WarrantedFight_StillRecordsTheTrueLethalRoll_ForAttributionReplay` — HP replay byte-parity.
6. `DirectResolverCalls_AreUnaffected` — the fixture-protection pin.
7. `FiredPredicate_AgreesWithTheClampByConstruction` — same fold, one source.
8. `Balance: NoHeroDiedEvent_OnDays1Through3_AcrossAllSeeds` — the harness policies
   (`sim/GameSim/Harness/`) never submit the conclude action, so this holds unconditionally.
9. **The not-inverted pin survives.**
   `SalvesStocked_PreparedHeroes_SurviveMeasurablyBetterThanReckless`
   (`sim/GameSim.Tests/Balance/ConsumableTraitMortalityBalanceTests.cs:157`) already pins trait
   mortality the right way up and MUST stay green through the re-baseline — plus the old U4's
   ratio-within-noise assertion against the pre-warrant baseline, so the (c) shape cannot return
   unnoticed by either door.
10. `MinAliveAtEnd = 3` (`BalanceSimTests.cs:34,102`) and Ending-reachability unchanged across
    the suite's seeds.

**Verification.** Fast lane green; balance suite green post-re-baseline with the re-record diff
in the same PR; the seed-sweep census quoted raw in the PR body
(`dotnet run --project sim/GameSim.Cli -- batch --seeds 20 --days 100`), including — **new,
because the beat's timing is now a design property** — the first-`HeroDied`-day distribution
(p50/p90 across seeds). Expected shape: the pre-warrant corpus measured 457 deaths over 20×100
days (`ExpeditionSystem.cs:21-25`), roughly one per 4-5 days, so the first post-warrant death
should typically land days 4-8. If the census shows a long tail (p90 past ~day 12), **report it
to the owner as a finding — never engineer a death to fix it** (the prohibition above).

---

### U5 — The warrant is taught, fires visibly, and its end is a dawn beat

**Goal.** The R9 fixed points (taught, visible when it fires, ends where the tutorial ends) plus
the piece the overrule adds: the end is *staged*, and walking out is a confirmed choice with its
cost named. Ships immediately after U4 — a warrant that exists but is not yet taught is a hidden
shield, so U4 and U5 are one owner-visible deliverable in two PRs.

**Serves: link5** — this is U4's legibility half.

**Four surfaces, all existing seams.**

1. **Taught at the first send-off.** `WatchDeparture`'s `TeachNote`
   (`godot/scripts/ui/TutorialFlow.cs:306-309`) gains the warrant:
   *"While the town's still teaching you — through Day 3 — the Mine doesn't keep anyone: a
   killing blow leaves them at death's door and they limp home. Dawn of Day 4 ends that, and
   you'll see it end."* Day 3's `MeetHeroes`/`Commission` copy carries the closing reminder
   (*"Tomorrow the warrant ends — what they carry down is what keeps them"*), so the end is
   named twice before it arrives.
2. **Visible when it fires.** The night's Ledger gets a warrant card, rendered when the
   fired-predicate reads true over the day's combats — leading with the true roll, the same
   honest-register shape as the death cards: *"The blow that landed on Torvald would have killed
   him. The apprenticeship's warrant held — he came home at death's door. Two dawns left on it."*
   One card per fired hero; no narrator line (the spoken library is frozen).
3. **The dawn beat.** A once-ever line on the first Morning after the warrant ends
   (`state.Day == LastGraceDay + 1`, and only if the player did not conclude early — an early
   graduate already heard it in the confirm): *"The apprenticeship's warrant ended at dawn. From
   today the Mine keeps what it takes."* Delivered through the `ConsumeLedgerTip` idiom
   (`TutorialFlow.cs:1329-1339` — once-ever, persisted, deliberately independent of `Active`),
   rendered by `MainUi` on the Morning tick. This is the single line that turns the old design's
   silent expiry into a beat.
4. **Dismissal is graduation, and says so.** The ✕ (`ObjectiveTracker.cs:198-202`) gains a
   confirm. While the warrant holds: *"End the apprenticeship? The lessons keep — they're in
   Lessons. The warrant doesn't: from your next send-off, the Mine keeps what it takes."*
   Confirming submits `ConcludeApprenticeshipAction` through the adapter **and** calls
   `TutorialFlow.Dismiss()` — one press, both layers, never one without the other. After the
   warrant has ended (dismissing a straggling chain, or U6's loss act), the confirm carries no
   mortality clause at all — there is nothing left to forfeit, and claiming otherwise would be
   copy stating a cost the sim would not charge.

**Files.**
- *modify* `godot/scripts/ui/TutorialFlow.cs` — TeachNote/copy amendments; `ConsumeWarrantEndBeat`
  (the `ConsumeLedgerTip` shape); `PersistedData` gains the beat flag (save-compat default, the
  `VigilCardSeen` precedent at `TutorialFlow.cs:1499-1504`).
- *modify* `godot/scripts/ui/ObjectiveTracker.cs` — the confirm dialog on ✕ (two copy variants,
  chosen by `ApprenticeWarrant.Covers(state)`).
- *modify* `godot/scripts/MainUi.cs` — dawn-beat rendering on the Morning tick (existing toast
  route); wiring confirm → adapter submit + `Dismiss()`.
- *modify* `godot/scripts/panels/LedgerModal.cs` — the warrant card, driven by
  `ApprenticeWarrant`'s fired-predicate over the night's combats.
- *test* `godot/tests/panels/LedgerModalTests.cs` (extend)
- *test* `godot/tests/TutorialCopyIsFollowableTests.cs` (extend)
- *test* `godot/tests/TutorialRegistryConformanceTests.cs` (extend — the cross-layer constant pin)

**Patterns to follow.** §11.11 U6's death-card discipline (lead with the specific, honest empty
state); `TutorialFlow.cs:1329-1339` (`ConsumeLedgerTip` — the once-ever persisted line);
`godot/scripts/panels/CampPanel.cs` `GateButton` — mirror the kernel's guard, never enforce one.

**Test scenarios.**
1. `TeachNote_NamesTheWarrantAndItsEndDawn` — copy pin.
2. `WarrantCard_RendersOnANightItFired_WithTheTrueRollNamed`
3. `WarrantCard_NeverRenders_BeforeItFired_OrAfterTheWarrantEnded`
4. `WarrantCard_CountsRemainingDawns_Correctly`
5. `DawnBeat_FiresOnceEver_OnTheFirstMorningAfterTheWarrant`
6. `DawnBeat_NeverFires_AfterAnEarlyGraduation` — the confirm already carried the news.
7. `DismissConfirm_NamesOrdinaryMortality_WhileTheWarrantHolds` — skipping's cost in copy,
   at press time, pinned.
8. `DismissConfirm_CarriesNoMortalityClause_AfterTheWarrantEnded` — the copy never states a cost
   the sim would not charge.
9. `ConfirmedDismiss_SubmitsConcludeAndDismisses_Atomically` — never one half without the other.
10. `BackstopDay_EqualsWarrantLastGraceDayPlusOne` — the cross-layer drift pin.
11. `WarrantCopy_NeverStatesASurvivalNumber` — §11.4's stakes-qualitatively rule, on the
    rendered string.

**Verification.** Engine suite green, raw `Failed: N, Passed: N` quoted against the
`ENGINE_MIN_PASSED` floor. Manual (render-and-look): seeded campaign with a day-2 lethal roll —
watch the warrant card land; ring into day 4 — the dawn beat fires once; dismiss on day 2 on a
second campaign — the confirm names the cost, and a day-3 lethal roll kills.

---

### U6 — The first loss is the tutorial's last lesson

**Goal.** The owner's sentence, made mechanical: when the sim produces the campaign's first
death, the tutorial wakes for one night and one day, teaches what just happened and what the town
does with it, and closes. This unit is what was missing from the overruled design.

**Serves: link5** — the outcome becomes the town's memory, and the first time that happens the
player is walked through every surface that memory lives on.

**Mechanism verdict: the aftermath already exists end-to-end; nothing frames its first
occurrence.** Deaths are applied at the Evening reveal — `Alive = false`, `DiedOnDay`, a
`Memorial` raised, `HeroDied` emitted (`sim/GameSim/Drama/ExpeditionRevealSystem.cs:65-84`);
the ledger's fate cards already exist (`sim/GameSim/Drama/LedgerQuery.cs:46-100`); the night's
loss count and narrator line are already captured (`godot/scripts/MainUi.cs:927-941`); the ticker
already speaks it (`godot/scripts/ui/AdventureTicker.cs:147`); the wall already renders memorials
with an Honor verb per un-honored `Memorial` and a Reforge row per still-reforgeable piece
(`godot/scripts/panels/LegendsWall.cs:15-27,104-149,164-169`), backed by the Evening-only,
idempotent `HonorMemorialAction` (`sim/GameSim/Contracts/Actions.cs:147-151`). U6 builds **no new
aftermath** — it points, once, at what is there.

**Three changes.**

1. **The wake.** A dormant loss act inside `TutorialFlow` (fields + a handful of methods — no new
   parallel class; the registry, persistence, and never-regress discipline already live there).
   Armed iff the chain was **not dismissed** (`Dismissed` is the opt-out — a graduate took
   ordinary mortality and its ordinary staging; §11.11 U6's death cards still serve them). It
   renders nothing — no card, no row, no override — until
   `state.EventLog.OfType<HeroDied>().Any()` first reads true. No arming date is needed: while
   the warrant holds, no `HeroDied` can exist (U4 test 8), and if the player graduated early the
   act is disarmed by the same flag, so the wake can only ever fire in the ordinary-mortality
   region the player was walked into.
2. **The first-loss block, on the night's own ledger.** Once ever, on the fate-card night: a
   short teaching block in `LedgerModal` under the death card — permadeath named plainly (gone
   for good; the roster refills), the rite named (*"Tonight the wall takes their name — the rite
   is yours if you want it"*), and the stake named honestly off the death card's own content:
   the card already leads with the player's marked item when the fallen wore one and says
   *nothing of yours was on them* when true (§11.11 U6's discipline) — the block adds no second
   claim, no participation credit, and **never a survival number**.
3. **One pointed step, then an honest retire.** A single checklist row wakes with the block:
   *"Take the night to the wall — honor them"*, Hud-anchored at the Legends tray button, exactly
   the `MeetHeroes`/`Commission` shape. Completion fact: `HonorMemorialAction` in
   `GameState.ActionLog` — player-caused, durable, KTD-A clean. The rite is Evening-only at the
   handler, and the death night *is* an Evening, so the modal case completes the same night; the
   gating note for a player who rings past says *"an Evening rite — the wall keeps"* (mirror the
   kernel's guard, never enforce). The row retires at the second dawn after the death via the
   established unconditional-sweep idiom (`TutorialFlow.cs:314-318,393`), rendering the `Skipped`
   state (*"— the rite keeps; it's at the wall whenever"*, `ChecklistRow.Skipped`,
   `TutorialFlow.cs:111-120`) — a sweep on the **pointer**, never on the **verb**: the rite stays
   legal forever and idempotent. This is the anti-nag line (the 1287×-memorial-nag finding is a
   repo memory): one night, one day, one row, then the Lessons book holds it permanently.

   Two supporting seams:
   - **The Legends gate widens.** `SurfaceUnlocks` opens Legends on first `AttributionBeatEvent`
     only (`godot/scripts/ui/SurfaceUnlocks.cs:82-83`) — an unattributed first death would leave
     the wall greyed on the exact night the tutorial points there. The gate becomes
     `AttributionBeatEvent.Any() || HeroDied.Any()` (still monotonic, still EventLog-derived),
     reason updated: *"Opens once your work has changed a fate — or the town has someone to
     remember."* Honest by the wall's own content: it renders memorials
     (`LegendsWall.cs:87,104-118`). The `MainUi.SurfaceEffectivelyOpen` OR
     (`SurfaceUnlocks.cs:29-40`) remains the backstop pin, not the fix.
   - **The Lessons book gains the loss lesson** — the block's copy, readable forever after, the
     same re-reading-beats-re-running answer U2 established.

**Files.**
- *modify* `godot/scripts/ui/TutorialFlow.cs` — the loss act (wake predicate, the row, the
  retire sweep, `PersistedData` extension for the once-ever block).
- *modify* `godot/scripts/panels/LedgerModal.cs` — the first-loss block.
- *modify* `godot/scripts/ui/SurfaceUnlocks.cs` — the widened Legends gate.
- *modify* `godot/scripts/panels/LessonsPanel.cs` — the loss lesson row.
- *modify* `godot/scripts/ui/ObjectiveTracker.cs` — render the woken row (existing row plumbing).
- *test* `godot/tests/TutorialRegistryConformanceTests.cs` (extend)
- *test* `godot/tests/panels/LedgerModalTests.cs` (extend)
- *test* `godot/tests/ui/SurfaceUnlocksTests.cs` (extend)

**Patterns to follow.** `TutorialFlow.cs:1241-1249` (`NotifyPanelOpened` — the Hud-anchor step
shape); `TutorialFlow.cs:314-318,393` (the unconditional-sweep retire);
`ChecklistRow.Skipped` (`TutorialFlow.cs:111-120` — the honest third state);
`SurfaceUnlocks.Gates` (`SurfaceUnlocks.cs:60-89` — monotonic, derived, never persisted).

**Test scenarios.**
1. `LossAct_RendersNothing_WhileArmed` — the quiet-gap pin: between the dawn beat and the first
   death, zero tutorial surface area.
2. `FirstLossBlock_RendersOnTheFirstDeathNight_OnceEver`
3. `LossAct_NeverWakes_WhenTheChainWasDismissed` — the opt-out pin; ordinary staging still
   renders (§11.11 U6 untouched).
4. `LossStep_CompletesOnHonorMemorialAction_APlayerCausedDurableFact` — KTD-A conformance,
   joins `EveryStepsCompletionFact_IsReachableByPlayerActionAlone`.
5. `LossStep_RetiresAtTheSecondDawn_AsSkipped_NeverAFalseTick`
6. `SecondDeath_RaisesNoTutorialSurface` — the tutorial owns the FIRST loss only, pinned.
7. `LegendsGate_OpensOnFirstHeroDied_EvenWithNoAttributionBeat`
8. `LossCopy_NeverStatesASurvivalNumber_AndClaimsNoCreditTheCardDidNot`
9. `LossLesson_IsReadableInLessons_AfterTheActCloses`

**Verification.** Engine suite green, raw counts quoted. Manual (render-and-look): a seeded
campaign known to produce a post-warrant death — the gap is silent, the block lands with the
fate card, the wall opens un-greyed, Honor completes the row, the second dawn retires it; a
dismissed campaign — the same death renders only §11.11 U6's ordinary staging; then confirm
`THE-GAME.md` §3.3 now describes what the screen just did.

---

## Key technical decisions

**KTD-C′ — The trigger is a date plus one confirmed opt-out; nothing else moves the window.**
The (c) build proved any behavior-fed predicate lets ordinary play forfeit protection (2.6×
inversion). A date cannot invert anything; the single opt-out is explicit, confirmed, cost-named,
and logged as a `PlayerAction` — it is a decision, not a side effect. No commerce, no tutorial
progress, no trait ever touches the predicate, and the pinned not-inverted assertion
(`ConsumableTraitMortalityBalanceTests.cs:157`) stands guard behind the argument.

**KTD-D — The warrant defaults off at the resolver's own seam** (kept verbatim from the old U4):
only the system ticks thread it from state; every direct resolver call is untouched by
construction.

**KTD-E — One legibility source** (kept): clamp, ledger card, dawn beat, confirm copy, and tests
all read `ApprenticeWarrant`. A card that could disagree with the resolver is worse than no card.

**KTD-G — The tutorial owns meaning, never fate.** No code path selects, schedules, weights,
hastens, or delays a death; no RNG site is added anywhere in the wave (the clamp draws nothing —
the `ModifierHpDelta` discipline, `ExpeditionResolver.cs:538-548`). The loss act is a pure reader
of `HeroDied`. If the first-death-day census shows an awkward tail, that is an owner finding, not
a knob.

**KTD-H — The loss act is once-ever and silent while armed.** Between the dawn beat and the
first death the tutorial has zero on-screen presence; the act fires for one night and one day and
retires honestly. The 1287×-memorial-nag playtest finding is the reason this is a KTD and not a
style preference.

**KTD-I — Dismissal is graduation: one press, both layers.** The sim half rides `ActionLog`
(deterministic, replayable, per-campaign — a new campaign resets it with the sim save, so no
cross-campaign leak of the `user://tutorial_flow.json` class, `TutorialFlow.cs:1455-1482`); the
UI half is only framing. The two cannot drift because the confirm submits and dismisses
atomically, and U5 test 9 pins it.

---

## Where this brushes the laws — said plainly

- **Link 3 / law 4 ("show only what the sim decided").** The warrant converts a decided outcome
  (death) into a different decided outcome (near-death) inside a told window. This is exactly the
  shape R9 already ruled legal as a taught mechanic (`MAKERS-MARK.md:2446-2451`), and the
  resolver-side construction (opaque flag, no trait, no RNG) is the one the (c) build already
  established needs **no pinned law exception**. The loss act adds zero sim influence — it is
  pure framing of an event stream. No new ruling needed here.
- **Law 7 ("skipping stays legal and its cost is named in copy, never engineered").** Dismissal
  ending the warrant is the one genuine brush in this design. The defense: ordinary mortality is
  the game's baseline, not a penalty bolted onto skipping — the warrant is part of the taught
  version, and declining the teaching declines its bubble; the cost is named at press time, in
  the confirm, before the choice is made. But it is honestly arguable the other way (a mortality
  change riding a tutorial ✕), it requires a deny-listed `Contracts/` amendment regardless, and
  §11.11 KTD-6 says balance-consequential sim changes are rulings — so it is **routed as R12
  below, default yes**, with the fallback pre-written: if declined, the warrant runs its dated
  course regardless of dismissal and the dismiss toast names the surviving warrant (the old
  U5.3 shape), while U4/U5/U6's staging all ship unchanged.
- **"No timers on decisions."** The warrant's dated end is a rule about the world, announced as
  a beat — not a clock on any decision. U6's retire is a sweep on a *pointer*; the rite itself
  stays legal forever (idempotent by the handler, `Actions.cs:147-151`). No tutorial step
  acquires a countdown.
- **"Influence never orders."** The loss step points and explains; `HonorMemorialAction` remains
  optional, and declining it costs exactly what the copy says (nothing — the wall keeps).

## Rulings owed

- **R12 — the graduation action (`ConcludeApprenticeshipAction`, a `Contracts/` amendment):
  should dismissing the tutorial also end the warrant?** Recommended default: **yes** — it is the
  owner's own principle ("the death is part of the tutorial") applied to its contrapositive, it
  removes the last hidden shield (a dismissed-but-warranted campaign would be protected by a rule
  nobody on screen is left to explain), and the cost is named at press time. If **no**: U4a is
  dropped, U4's trigger is date-only, and the dismiss toast names the surviving warrant. Either
  way U4 re-records and re-baselines once.
- **R11 (restated, unchanged) — `LastGraceDay = 3` confirmed?** Three days is the
  apprenticeship's own span, and dawn 4 is the chain's existing backstop — the pinned equality
  makes any other value a two-constant decision. Longer values push the first death later; the
  census's first-death-day distribution (U4 verification) is the evidence to re-ask against if
  day 4-8 feels wrong in play.

## Scope boundaries — what this wave does not do

- **No scripted, scheduled, weighted, or forced deaths — ever.** The census reports the first
  death's timing; nothing steers it.
- **No new RNG site, anywhere.** Determinism changes are confined to U4's clamp, re-recorded once.
- **No difficulty/assist system.** The warrant is not a toggle and not extendable; the one exit
  is one-way and cost-named.
- **No changes to §11.13 U1-U3** (landed), to camp verbs, provisioning balance (R1's freeze),
  party formation, or §11.11 U6's death staging — U6 here *points at* that staging once, and the
  first-loss block is additive copy on one night.
- **No new narrator lines.** The dawn beat, warrant cards, and loss block are text surfaces; the
  spoken library stays frozen.
- **No new tray surface and no LessonsPanel redesign** — one lesson row, one widened gate
  predicate.
- **No timers on any decision**, and no tutorial step that completes on a hero's cooperation
  (KTD-A stands; U6's step completes on the player's own rite).
- **No third plan doc.** This is a §11 amendment; `docs/plans/` stays at two.

## Sequencing

| # | Unit | Size | Blocked by | Sim diff | Re-baseline |
|---|------|------|-----------|----------|-------------|
| U4a | Graduation action (Contracts micro-PR) | small | **R12 ruling**; orchestrator-authored | yes (additive) | no |
| U4 | The warrant, in the sim | session | U4a (or R12-declined fallback) | **yes** | **yes — golden re-record + Category=Balance, own PR** |
| U5 | Taught, visible, dawn beat, dismiss-as-graduation | session | U4 (must land immediately after) | no | no |
| U6 | The first loss is the last lesson | session | U4 (needs real deaths past day 3), U5 (copy seams in the same files) | no | no |

U4a→U4→U5→U6 is one lane (U5/U6 share `TutorialFlow`/`LedgerModal`, serial). Engine tests
serialize regardless. Every PR body carries `Serves:` per §11.6 rule 3; U4's quotes the
seed-sweep census raw, including the first-death-day distribution. **The wave is reportable only
at U6's merge** — U4+U5 alone is the overruled design with better copy.

---

## THE-GAME.md §3.3 amendment (lands in U4's PR)

Replace the second and third paragraphs of §3.3 (`docs/design/THE-GAME.md:224-231`) with:

> The tutorial runs three days as an apprenticeship rather than a tooltip tour: make one thing,
> sell one thing, watch one raid resolve. By the end of it you have picked a second profession.
> The apprenticeship carries a warrant, and you are told so at the first send-off: through day
> three the Mine keeps no one. A killing blow leaves a hero at death's door and they limp home,
> and that night's ledger shows you the roll that should have killed them. You are told when the
> warrant ends — twice — and the dawn of day four ends it as a beat, not a footnote: from today
> the Mine keeps what it takes. Walk out of the apprenticeship early and you walk out of its
> warrant too; the game names that price at the moment you choose it.
>
> The first real lesson lands after that dawn, and it lands as a death — whose, where, and when
> is the dice's answer, never a script's, and usually it comes within the first week. That night
> is the tutorial's last act: the ledger names the blow, the wall takes the fallen's name, and
> the rite is yours to perform or to leave. If they wore your make, the sentence has your work in
> it; if they did not, the game says so honestly. Nothing in the game punishes you for any of it.
> The town keeps going, the roster refills, and you have learned what the numbers on a shelf
> actually weigh.

(The "around day four" promise stops being an intention: the earliest possible death IS day 4 by
mechanism, and the tutorial is standing there when it lands, whichever day the dice pick.)

---

# §11.14 — The owner's 2026-08-16 playtest, planned end to end

**Status: §11.4 path work. This section is the plan of record for all 27 items of the owner's
2026-08-16 playtest.** The register itself lives at
`docs/playtests/2026-08-16-owner-playtest-register.md` (landed in PR #527) and is not duplicated
here; this section is what gets built, in what order, and why.

## Why this section exists in this shape

The owner's complaint is not that any one thing is broken. It is that feedback goes missing:

> everyplay test i am giving heavy feedback but you jump on ONE thing then skip the rest… its
> annoying how little you need doing. the tutorial revamp is CLEARLY nowhere NEAR the scope i
> keep fucking telling you to do (full guided tutorial) and other feedback is getting randomly
> forgotten.

The forgetting had a mechanical cause, now fixed: the previous session held all 27 items in an
in-session todo list, hit its subagent cap, and died with the list. The handoff that survived
asserted the items were "already captured as tasks #141–#162" and nothing by that name existed
anywhere on disk.

So this program has two products. One is the fixes. The other is that **a fix can no longer be
claimed without a unit, and a unit can no longer be claimed without a test** — the coverage
census in §11.14.9 is the executable form of that, and every theme's plan ends with a table that
has no blanks.

Seven parallel research passes read the code before any unit was written. **Four of the five
previously-attempted fixes on this list had been aimed at the wrong cause** — the audio clipping
that does not exist, the bellows volume that was never the mechanism, the loop seam that was not
the artefact, the tutorial "revamp" that taught button presses. That is why this section leads
with root causes and not with tasks.

## §11.14.1 Owner rulings, 2026-08-16

Made this session, in response to costed options. Do not re-litigate.

| # | Ruling |
|---|---|
| R14.1 | **Strike implies release.** A hammer strike arriving mid-pump stops the pump and lands. Rejected: auto-release at max heat (leaves the trap live at heat 999) and a copy-only prompt (that is the shape of the fix that already failed). |
| R14.2 | **The furnace opens the Foundry** — coal, flux, forge-tier upgrade. The Material Shelf stays the ore vendor, which the tutorial, `WorkshopVocab` and this document already agreed it was. |
| R14.3 | **Recipes gate on Forge Tier plus an action slot.** Rejected: a talent-point economy (a new system, deliberately deferred) and a calendar gate (gates on the clock rather than on anything the player's hands did). |
| R14.4 | **The tutorial chain numbers within acts**, not as one global countdown. |
| R14.5 | **A named journeyman delivers the lessons no hero can honestly speak.** Ships as a station-table row plus a pure `MentorVoice`, on an existing townsfolk body. She never orders, and no step's completion depends on speaking to her. |
| R14.6 | **The pointed chain runs through day 7**, not day 3–4. This is a deliberate widening: Acts I–III get room rather than being compressed. |
| R14.7 | **The tutorial names the six dilemmas out loud** — one sentence each, both sides, no recommendation, pinned by corpus tests. |
| R14.8 | **Buildings grow role-ranked, 3.5×–5.5× the character body.** Not a uniform multiplier: a uniform 2× would leave the market and the Bounties hall still undersized, i.e. both complaints still open. |
| R14.9 | **The world grows to 64×44 tiles** (1024×704 px, 3.87 screens of area, from 1.55). Larger was rejected on content grounds — the cap is what can credibly fill it, not what the grid allows. |
| R14.10 | **`market.png` is not re-rendered.** The owner named it as art he likes and there is no fallback copy on disk. Accepted consequence: at 1.94× it becomes the smallest building in a town whose tavern is 5.5×. A candidate render may be produced for side-by-side review only; it merges on his word or not at all. |
| R14.11 | **All three causes of the legs-in-grass defect are fixed, including the 244-PNG silhouette pass** over the AI cast approved on 2026-08-15. |

Standing rulings from §11.7 and earlier that constrain this program: venues are a forward ladder;
heirloom reforge grows and never rewards a death; the first death belongs inside the tutorial;
buildings get bigger rather than characters smaller; no important information without a face, and
dialogue is the delivery (R3); the narrator is sparse and triggered, one voice for v1 (R6).

## §11.14.2 The six themes and the ship order

```
T6  reasons              ──┐
                           ├──► T1  the forge works ──► T2  the full guided tutorial
                         ──┘
T4  one mix pass         ──── independent
T3  the town reads right ──── independent (T3 is internally ordered: guard → grid → art)
T5  the night lands      ──── independent
```

**T6 first** because reason-logs are how the *next* playtest gets diagnosed instead of guessed at.
Four of the five failed fixes on this register failed for want of a recorded reason.

**T1 before T2** because the tutorial teaches the forge, and teaching a minigame that cannot be
completed is worse than not teaching it.

**T3 is internally strict**: the placement guard must exist before the grid moves, and the grid
must move before the buildings grow. This is arithmetic, not preference — a minimum town at
R14.8's ratios needs 640 px of vertical space and the world has 448. The 16 measured sprite
overlaps are the same fact from the other side: the layout already fails at today's sizes.

## §11.14.3 T1 — The forge works

Closes #147, #149, #155, #156, #157.

**#155 is the highest-priority defect in the project.** Root cause, three lines: a Shift tap under
`BellowsTapMaxHoldSeconds` latches the bellows on permanently
(`godot/scripts/minigames/ForgeMinigame.cs:649-656`); `ForgeStrike()` early-returns while pumping
(`:465-470`); and the pumping branch drains banked shape at 8‰/s while heat is clamped (`:441-445`).
Every strike after the latch is discarded and the readout says "keep going."

It survived two fixes and a playtest because **the latch had never executed in CI**: every
winnability harness drives the overlay with `SetProcess(false)`, and `:622` disables the whole
gesture machine when not processing. The one test that does exercise it,
`AgentPlaytestBridgeTests.cs:868-968`, **asserts the softlock as correct** — a 420-turn pilot probe
landed zero strikes and it was filed as a measurement.

| Unit | Closes | What |
|---|---|---|
| U-T1-1 | #155 | The rule (R14.1). Drops `IsPumping` from the strike gate and from the button's disabled state; stops the drain at the heat clamp. Amends the test that pins the bug and the pilot policy that depends on it. |
| U-T1-2 | #155 | The readout gains a pumping branch. The assist line may not render while pumping. |
| U-T1-3 | #155 | One bellows, one state — and `ForgeGestureTests`, the first real-clock input suite in the repo, which leaves `_Process` on and waits on conditions rather than frame counts. |
| U-T1-4 | #155 | `HudBoundsTests` gains its first minigame-overlay case: hammer, bellows and cancel are on screen and clickable at the minimum window. |
| U-T1-5 | #156 | `EnsureControlVisible` aimed at a section taller than its viewport scrolls to that section's *bottom*. Replaced with an explicit top-edge scroll; the old guard is strengthened so it can no longer pass on a bottom-scrolled panel. |
| U-T1-6 | #149 | The three modifier selects get family labels derived from the enum; the blank feedback row stops reserving space. |
| U-T1-7 | #147 | The quench trough stops denying the quench it performs. |
| U-T1-8 | #147 | The furnace opens the Foundry (R14.2). `FocusSection` gains a third section; `InteriorRoomTests`' known-focus table is the guard that keeps a silent no-op from shipping. |
| U-T1-9 | #157 | Unlocking a talent costs an action slot and requires a Forge Tier (R14.3). **Golden re-record and a balance re-baseline in the same PR** — `BaselinePlayer` unlocks one talent per morning. |
| U-T1-10 | #157, #149 | The panel mirrors the kernel's gate. Locked recipes render as one compact row with the named requirement, greyed and never hidden. Day 1 goes from 22 five-button cards to 7 cards and 15 rows — which is also the density half of #149. |

Deliberately not done: reverting the two-scroll split (it is the documented fix for a burial bug);
un-pairing the bellows from the anvil (deliberate, pinned twice); removing the `IsProcessing()`
gate (it protects the tempo measurements — the answer is a second harness, not deleting the first
one's protection); deleting the tap-to-toggle latch (a real accessibility feature — the inert
hammer under it was the bug).

## §11.14.4 T2 — The full guided tutorial

Closes #158, #160, #161, #162.

**The finding that sets the scope:** the current tutorial teaches you to operate a shop, and this
game is not a shop. Ten steps teach ten button presses; a player can complete all ten without ever
watching a hero decide, seeing the mark on anything, or seeing a counterfactual beat. The census:
**25 player actions exist, 7 are taught, 4 are named but never required, 11 are wholly untaught**,
all five craft minigames are untaught, and of the six dilemmas the game is made of, **pricing and
the slot budget are never taught at all**.

The rework is four acts on the five-link spine — The Mark, The Hand-Off, The Dark, The Memory —
plus a **first-touch** tier so the long tail teaches itself once, the moment it becomes reachable,
and then lives in the Lessons book. That two-tier split is what makes 25 actions tractable without
a forty-step chore.

31 units in six waves:

- **Wave A, substrate (7):** act-scoped numbering (R14.4) · splitting the chain's backstop from the
  warrant constant, which today are the same number · the Docket, one new `CanvasLayer` that draws
  above the drawer veil and is deliberately absent from `OverlaySurfaces()` · Tomorrow at the
  Counter moved into it (#160) · `MentorVoice` and the journeyman (R14.5) · a `PanelControl` anchor
  kind · the first-touch engine with its once-ever anti-nag pin.
- **Wave B, Act I (4):** the forge's two acts taught inside the forge · the other three crafts, and
  a stale "ships DORMANT" doc deleted · material sets the ceiling and your hands set the band · the
  mark, read.
- **Wave C, Act II (6):** **pricing as a decision** (dilemma #2, untaught today) · **day 1 gets a
  link-2 beat** (#161) · the counter step completes on actually answering · it points at the
  counter station · its copy splits and its gates stop lying · hold-or-sell (dilemma #1).
- **Wave D, Act III (5):** the slot budget named before it bites (dilemma #4) · the muster speaks
  (dilemma #3) · the ore gift named as a gift (dilemma #5) · **the proof taught the first time it
  lands** (link 4) · the forecast board taught.
- **Wave E, the long tail (5):** talents and the second profession · the Foundry's four verbs at
  affordability · reforge · the read-only surfaces · the HUD chips, including quick travel which
  unlocks silently today.
- **Wave F, the guard (3):** `TeachingCoverageCensusTests` — deny-by-default over every action,
  minigame and panel, where a refusal must carry a written reason · "show me that lesson again" ·
  census hygiene.

**#161 answered.** The honest state today is *neither* branch: a sale can fire silently on the
send-off tick because `HeroShoppingSystem` runs before `MusterSystem`, and the game never says so.
The fix is not to add a sale — it is to make **the moment a hero decided visible**: the send-off
names what happened, buyer and price, or says honestly that nobody bought it. That gives link 2 its
missing day-1 beat, and it collapses the either/or, because the two readings were only exclusive
while the game was silent.

**#160 solved structurally.** It is impossible today, not merely unwired: the drawer's veil is
added after the tray and eats the click, and hiding `ForgePanel` force-cancels every running craft.
The Docket fixes both by living on its own layer and never touching `DrawerHost`.

## §11.14.5 T3 — The town reads right

Closes #141, #142, #143, #144, #145, #146, #150, #163.

**#150 is one constant.** `SpriteMotion.WalkSpeedThreshold = 20f`; a wandering hero's lissajous
velocity peaks at 15.03 px/s. **No wandering hero can ever cross the threshold** — all six are
frozen on frame 0 all day. The art is not missing (61 `_step` + 61 `_walk2` + 61 `_walk4` on disk)
and the wiring is not missing. Townsfolk differ solely because their errand speed is 60. Lowering
the threshold is the wrong fix: at 6 px/s a hero would play one stride every 13.5 seconds while
drifting in place. The fix is the errand model the townsfolk already have.

**#145 has an exact cause.** `HomeFor(id)` is a formula that puts hero 1's permanent home one tile
below the ore cart, and the nameplate draws at `ZIndex 20` inside the cart. All six hero homes
collide with something; hero 6 spawns inside the market and Y-sorts behind it.

**#146 is measured, not felt.** The interior shells carry 0.020–0.033 bytes/px against 1.688 for
the forge exterior — 19–84× less pixel information per unit area than any art the owner has
approved. Cause: a "six to eight colours per building" rule, correct for a 20×36 sprite, applied to
a 384×224 room the camera fills, with the palette sampled from the pixel set the owner rejected.

11 units: the placement table and its census guard (which reconstructs rects the way the client
does, covers the two placement sources that were unreachable from any test, and pins today's 16
overlaps as an exact exception set) → the grid to 64×44 and every table re-laid (the exception set
goes to zero, which is the unit's proof) → occupancy, so the bigger world reads fuller not emptier
→ the venue art contract that #514 shipped without → the venue re-render to R14.8's sizes → the
pixel-snap that stops resampling the cast every frame → the contact shadow → the silhouette pass
(R14.11) → heroes get a real errand → the march pace, as its own revertable PR → painted interior
backplates → an orphan sweep.

**No prop needs shrinking.** The well at 2.1× a person and the lamppost at 1.4× are both defensible;
they only read as absurd against undersized buildings. The lamppost's real defect is count — 22
light sources in a four-building village — which the re-lay fixes.

## §11.14.6 T4 — One mix pass

Closes #151, #152, #153, #154, #165.

**The correction that reframes the whole theme: nothing clips.** Zero clipped samples in any
shipped bed, in OGG or the MP3 predecessors. The prior "+1.63 dBFS, 11,133 clipped samples" is an
artefact of an L+R mono sum, which saturates all four beds to exactly 0.0 dB; Godot plays them
stereo and that sum never happens. Two rounds of gain cuts chased it.

The real defects are content and structure:

- **#151** — the Dawn bed fires 356 hard onsets in 134 s, with 91.1% of its energy below 150 Hz and
  under 0.001% above 6 kHz. It is an impulse train with no midrange. No gain change can fix content.
- **#152** — the Night bed has seven seconds of flat noise floor at its tail and loops back into
  content 34.9 dB louder. The MP3→OGG fix made the *sample* continuous and left the *content*
  discontinuity. The Evening bed is worse and unreported: 53 of 60 seconds of bare noise floor with
  isolated full-bandwidth clicks, one of them 62 dB out of silence.
- **#153** — the previous nudge measured −1.92 dB, at or below noticeable. Level was never the
  mechanism: the bellows is the only looping cue in the game, retriggered continuously at 0 dB,
  sitting 8.5 dB above the bed it plays over. Every constant and test in the codebase measures
  **peak**; the complaint is about **sustained loudness**.
- **#154/#165** — there are no audio buses at all, no limiter, no ducking, and a 47 dB spread from
  loudest cue to quietest bed. The narrator sits below nine UI cues.

Two further findings ride along: the craft grade sting bypasses `AudioDirector` entirely and ignores
the SFX fader, the master fader **and mute** — so muted playtests were never silent; and the
composed-track fingerprint test pins all four beds by SHA-256, which **actively locks the defective
generations in place** and must die in the same PR as any re-master.

Unit list lands with the T4 plan.

## §11.14.7 T5 — The night lands

Closes #148, #159, #166, #167, and the unreported "Returned safely" defect.

**#166 root cause:** `SurvivorFloor` returns 0 when a survivor set no record, earned no beat and
looted no ore — exactly what happens when a partymate flees floor 1 while gold is still banked per
kill. `PartyDeparted.TargetFloor` is in the same day's log and unread. A second vector is already
armed for the forward ladder: `OreFloor` is hardcoded to the Mine. Fix: clamp to the provable
minimum of 1 for any hero the log proves departed, and scan the venue registry for ore. Reading
`TargetFloor` as the floor is rejected — it would overstate, which is fabrication in the direction
the law cares about most.

**#167 root cause:** the sentence states the day's loot income and the chip states the hero's entire
purse. Both correct, the chip unlabelled. Fix: label both. `ReturnCard.GoldEarned` gains its first
client reader.

**#159** is two halves. Geometry: the modal is a hardcoded 640×420 inside a `CenterContainer`, which
is simultaneously a floor and a ceiling — 33%×39% of a maximized window, 1.4 of 6 cards visible,
~5× overflow behind an unthemed default scrollbar, and the title is 16px body text because it calls
`AddLabel` rather than `AddHeader`. The fitted-modal helper that fixes exactly this already exists
and two other panels already use it. Narration: `SpeakNarrator` **returns** the line's text and all
three call sites discard it, in direct contradiction of its own contract, and the retelling
collapses to a single line on any night without an attribution beat.

**#148** splits into a cheap honest tier and the real work. Cheap: pin the viewport to nearest
filtering (the watch is the only 2D path in the game not pinned, so it is literally the only blurry
surface), use the hero art variants that already ship, re-author the backdrops at draw size instead
of stretching 160×160 to 1024×260 anisotropically, and make a rout look different from a triumph.
Real: replace the fabricated HP bar with the sim's own number, flare the link-4 beats as they
happen, wire `PresentationScheduler` — fully built, fully tested, called by nothing — as the pacing
source, and add the camera its `CameraHint` field was written for.

**A law breach is named here.** The monster HP bar depletes by a client-authored fixed ⅓ per beat.
That is a drawn quantity no sim rule produced — a breach of *show only what the sim decided*, live
under a green build, because the tripwire scans only for RNG and clock tokens. Its stated
justification is false: `VenueDefinition.MonsterHp(floor)` is public, is the resolver's own seed, and
is already rendered by the Bestiary. The fix needs no contract amendment.

## §11.14.8 T6 — Every decision leaves a reason

Closes #164.

The census: across 21 outcome-changing decisions, **3 emit a reason, 11 compute one and discard it,
5 never compute one, and 2 emit no event at all.** This is a discard problem, not a computation
problem. The sim already calculates the willingness number the whole counter minigame is played
against, the quality roll's shift and band, and the counterfactual margins that *are* the
attribution beats — then returns a bare enum.

The client half is worse: `PlaytestLog.Decision` is the general reason channel, its own doc quotes
the owner's directive verbatim, and it has **one call site in the repo**. `Action.why` is never
passed. The owner's 2026-08-16 session recorded 8 ticks, 3 actions and 98 audio rows with **zero
reasons**.

**Two tiers, chosen by one test — would the player ever want to read this?** Player-facing reasons
become a persisted `DecisionExplained` event; diagnostic internals become a non-persisted
`TickResult` trace. The split is forced by a real cost: the golden test hashes the entire serialized
state and `EventLog` is inside that hash, so a persisted event moves the SHA and — because event ids
seed the prose variant picker — re-rolls rendered flavour text campaign-wide. Traces cost nothing.
**Two deliberate re-baselines across the whole theme**, each with the RNG-position pin asserted
unchanged to prove no draw was added.

12 units. Two are bug fixes rather than instrumentation:

- **The boycott reason lies**, and it is three defects: the emitted prose has no boycott knowledge,
  so a hero refusing over mood reports "better gear score per gold"; the `HeroDecisionExplained`
  reason is equally blind; and **the reported margin is computed from raw prices while the ranking
  used boycott-inflated ones** — a number that is invisibly wrong and survives every existing test.
  Downstream, Analytics buckets that false reason as a gear-quality problem.
- **The reveal deletes its own evidence.** Every recorded roll and the typed halt are destroyed the
  same tick they are narrated. After Evening, nothing in the state says why a party stopped.

## §11.14.9 Coverage census

Every register item, and the unit that closes it. A blank here is a failure of this program.

| # | Item | Units |
|---|---|---|
| 141 | legs clip with the grass | U-T3-5, U-T3-6, U-T3-7 |
| 142 | heroes too big vs buildings | U-T3-4a, U-T3-4b |
| 143 | the Bounties building | U-T3-4a, U-T3-4b |
| 144 | too many lampposts | U-T3-1, U-T3-2 |
| 145 | props clip into actors | U-T3-1, U-T3-2 |
| 146 | interiors look bad | U-T3-10 |
| 147 | interactables lack distinct meaning; furnace sells ore | U-T1-7, U-T1-8 |
| 148 | the watch must reach cutscene quality | U-T5-8, -9, -10, -11, -12a, -12b |
| 149 | the legacy jank crafting menu | U-T1-6, U-T1-10 |
| 150 | no hero/NPC walk animation | U-T3-8, U-T3-9 |
| 151 | Dawn graininess | T4 |
| 152 | Night grainy static | T4 |
| 153 | bellows too loud | T4 |
| 154 | bells/chimes too loud | T4 |
| 155 | the anvil minigame cannot be completed | U-T1-1, -2, -3, -4 |
| 156 | drawer opens scrolled to the bottom | U-T1-5 |
| 157 | everything unlocked from the start | U-T1-9, U-T1-10 |
| 158 | full guided tutorial rework | all of T2 |
| 159 | evening ledger tiny; narration unused | U-T5-5, U-T5-6, U-T5-7 |
| 160 | Tomorrow at the Counter: taught, and openable while crafting | U-T2-3, U-T2-4, U-T2-23 |
| 161 | sell before the first send-off? | U-T2-13 |
| 162 | tutorial step 6 | U-T2-14, -15, -16 |
| 163 | expand the world | U-T3-2, U-T3-3 |
| 164 | log every action and its reason | all of T6 |
| 165 | no audio bus, no limiter | T4 |
| 166 | "came back from floor 0" | U-T5-1, U-T5-2 |
| 167 | 8g in the sentence, 11g on the chip | U-T5-3 |
| — | "Returned safely" after a rout (found, unreported) | U-T5-4 |

## §11.14.10 Process notes

- **Both `docs/plans/` slots are occupied**, so under §11.6 rule 4 this program lands here as a §11
  amendment rather than as a third wave doc — the shape §11.12 and §11.13 both used.
- **Golden re-records / balance re-baselines: three in total** — U-T1-9 (an action slot changes the
  baseline trace) and two in T6. Each is a loud, reasoned entry appended to the existing ledger with
  the RNG-position pin asserted unchanged. Everything else in this program is golden-neutral.
- **No `sim/GameSim/Contracts/` change is required by T1, T3 or T5.** T6 needs exactly one contract
  micro-PR, orchestrator-authored, merged before its dependents.
- **The engine suite is the gate and only the orchestrator runs it.** Healthy pass count on
  `023c960` is **1245**. Compare the count, never the verdict — two concurrent gdUnit runs each
  report "Failed: 0" while silently losing about 400 tests.
- **Several stale figures in §11.12 are corrected by this program's PRs** per rule 8: its venue
  colour table predates the 2026-08-15 re-render, and its screen-size arithmetic assumes a canvas
  shrink of 3 where the code resolves 2.
