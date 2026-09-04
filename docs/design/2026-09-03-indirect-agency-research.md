# Indirect agency — what shipped games do, and what of it we should steal

*Research pass, 2026-09-03. Read-only: no code was written for this, and nothing here is a plan
item until §11 adopts it. Every recommendation below is checked against the seven laws and against
`§10`'s filter before it is ranked.*

---

## 0. The question, and the honest answer

**The question.** The player of Maker's Mark is structurally a spectator: they prepare, then watch
someone else act. How do shipped games make that feel like authorship instead of a screensaver?

**The answer the evidence gives, in one paragraph.** Nobody solves it with a verb during the watch.
Every game that succeeds at this solves it in four other places: *before* the watch (the player
enters already knowing what should happen, so the watch is confirmation-or-surprise rather than
information delivery), *inside* the watch (the player controls the **pacing**, not the events),
*after* the watch (per-actor attribution — "which of my decisions did the work" — not just win/lose),
and *around* the whole loop (a specific named other party whose fate the player has pre-committed
to). The games that fail at it fail in exactly one identifiable way, and it is not "not enough
verbs" — it is **the player cannot tell why the outcome happened**. Loop Hero's sharpest complaint
is *"Far too many times it felt like I lost because there was nothing I could do"*
([Game-Wisdom](https://game-wisdom.com/analysis/loop-hero)); Majesty's is *"The player lacks any
real substantial agency, and therefore both victory and failure can feel meaningless"*
([GameFAQs](https://gamefaqs.gamespot.com/iphone/639934-majesty-the-fantasy-kingdom-sim/reviews/173674)).
Both are legibility failures wearing an agency costume.

That is good news for this project, because legibility is the half we are already spending on
(P1, P2, The Telling, P2-MEMORY), and it is the half that costs no law.

**Method and confidence.** Twenty-five-odd games across three parallel research passes, sources cited inline.
This session exhausted its WebSearch budget partway through, so several sources are search-engine
summaries rather than fetched primary pages; every one of those is flagged **unverified** at the
point it is used, and §6 collects them. Where a game's mechanism could not be confirmed, this doc
says "could not verify" rather than asserting.

---

## 1. The five, ranked

Ranked by (value to the five links) ÷ (cost). Each is checked against the two laws that bite here —
**influence never orders** (pinned mechanically by `HeroSovereigntyCensusTests`: no player verb may
write hero state at apply time outside the four named honest channels) and **show only what the sim
decided**.

### M1 — The Patron. *(Blaseball's Idol.)* **Cost: small. Value: high.**

**The mechanism.** The player may name **one living hero as their patron**. Naming is free but
**sticky: it can be changed once every seven days**, so the choice is made before the week's muster
is known and cannot be re-picked after seeing who is marching. What it does, all of it presentation
over recorded state:

- The Night reveal's first card is the patron's night — beat or no beat, survival or death — *before*
  the beat-led card, or merged with it when they coincide.
- The muster forecast sorts the patron's party first and captions their gap.
- The chronicle records who you backed, from which day to which, and what happened to them. A patron
  who dies while patron is a line in the book.
- Nothing else. **No hero state is written, ever.**

**Why it works, in the one game that proves it.** Blaseball was a text simulation of a sport nobody
could play, and it generated real attachment through two mechanisms: pick a team, and pick an Idol —
*"Fans may choose to idolize a player on that player's page"*, one at a time, 200 coins to switch
([Blaseball Wiki](https://www.blaseball.wiki/w/Idols)). The Idol is a **pre-commitment**: it converts
a simulation tick into news about someone the fan chose, before the tick happened. That is precisely
the missing beat in our loop — the player currently has no way to say *"this is the one I'm watching"*
until the game tells them who mattered, after the fact.

**Links served.** Link 3 (the hero's judgment becomes *someone's* judgment) and link 5 (the town's
memory acquires the player's own allegiance as a recorded fact). It also composes with, rather than
duplicating, `P2-PEOPLE`: that program *authors* six people worth caring about; this one lets the
player **choose** which one, which is the half authoring cannot supply.

**Law check.** Passes. It writes no hero state, so it never approaches
`HeroSovereigntyCensusTests`' pinned four channels. It shows only recorded facts. It is untimed. It
is legal to ignore entirely. Under law 3 it qualifies as *"a surface that reveals the player's own
stake"* (§11.7.4's own wording), not as a verb that must move an outcome.

**The line that makes it legal, stated so no later session crosses it:** *the patron changes what the
player sees and nothing about what the hero does.* The moment a patron buys more readily, marches
differently, or survives better, it is an order with extra steps — see T7.

**Cost and blast radius.** One nullable `HeroId` plus a `PatronSinceDay` on `PlayerState` — a
Contracts micro-PR and a golden re-record, on the trailing-init-member save-compat precedent the
expedition contracts already use repeatedly — one action, client sort and card ordering in the
ledger and the muster board, one chronicle projection. No balance re-baseline: nothing the sim
decides moves.

---

### M2 — The item's own record, and the moment it crosses a threshold. *(Dwarf Fortress's weapon kill lists; Mechabellum's post-round stats, in our register.)* **Cost: small–medium. Value: high.**

**Two halves. The second is the better one and it is nearly free.**

**M2a — the facts.** After a delve, every player-crafted item that marched gets **its own recorded
facts** appended to its history and shown on its provenance card: the floors it went to, the rounds
it was carried, the damage it dealt or the damage its bearer took while wearing it, the monsters it
landed the last blow on. Recorded facts only — the numbers the resolver already wrote down. No
percentages of party contribution, no ranking against other items, no totals across your work, and
**never in the Night reveal's headline**, which belongs to beats alone.

**M2b — the promotion, which the sim already computes and no screen shows.** In Dwarf Fortress,
every weapon carries its own kill list, and a weapon whose list gets long enough is **automatically
promoted to "semi-legendary" and appears on the Artifacts screen, in blue** ([DF wiki:
Weapon](https://dwarffortresswiki.org/index.php/Weapon), [Kill
list](https://dwarffortresswiki.org/index.php/DF2014:Kill_list)). No ceremony, no announcement — an
ordinary object silently becomes a listed one because of what it did. **We already have this rule
and it is invisible.** `ShoppingAi.SentimentalDeedThreshold = 3`: once a worn item's Kills + Saves
from the bearer's `ItemMemory` reach three, the hero **will not trade it away** for a marginal
upgrade (`ShoppingAi.cs:83-90,158,190`). The sim has been promoting the player's old work to
heirloom status for months, changing a hero's actual behaviour, and telling nobody.

The build is: say it. The item's card reads *storied*. The legends wall lists objects as well as
people. And — the part that makes it a link-2 mechanism rather than a trophy — **the hero's refusal
to part with it is a shopping verdict the counter can voice**: *"Not this one. It's been down there
with me."*

**Why M2b outranks M2a.** The async-contribution research is unambiguous that the strongest
feedback units **name a party and fire reliably**, and the weakest are anonymous aggregates that fire
on a threshold nobody can see. M2a is a stat panel; M2b is the sim already having decided something
about your work and finally admitting it.

**Why.** Mechabellum's single most-cited readability tool is the post-round per-unit damage panel
that turns *"did I win"* into *"which of my decisions did the work"*
([Steam Community](https://steamcommunity.com/app/669330/discussions/0/600780667249278169/)). Our
beats answer that question **only when the answer is dramatic**. On the nights when nothing crossed
a counterfactual threshold — the majority of nights — the player's work vanishes from the record
entirely, and that silence is what a player reads as "it didn't matter." Weapon Shop de Omasse is
the warning: its craft→outcome link was real and reviewers concluded it was *"largely random"*
because nothing let them trace it.

**The data already exists, for both halves.** `CombatEvent` records `DamageDealt`, `DamageTaken`,
`MonsterKilled` and `KillingItem` per exchange; `ConsumableUse` records `HpBefore`/`HpAfter` per
quaff; `ItemHistoryEntry` already appends "kill"/"save" rows at reveal; `ItemMemory(Item, Kills,
Saves)` already accumulates the per-bearer deed count that `SentimentalDeedThreshold` reads. This is
an aggregation and a card, not a new measurement — and M2b is a *rendering* of a rule that already
runs.

**Links served.** Link 1 (the mark accumulates a verifiable record, the Dwarf-Fortress-artifact
property) and link 4 (attribution gains a *floor* beneath the beat, so an ordinary night still
reads as evidence).

**Law check.** Passes, *conditionally*, and the condition is the whole design: it must read as
**recorded facts, not credit**. The register is the one `P2-PROOF` already blessed for the memorial
— *"The blow read 15. Her shield drank 2."* If it acquires a total, a ratio, a medal or a
congratulation, it has become participation credit and it violates §2's "no participation credit"
in spirit while technically showing sim-decided numbers. **Ship it on the item's card, in the past
tense, with no adjectives.**

**Cost and blast radius.** M2b is the cheap half: a read-only query over `Hero.Memories` and a label,
plus one counter voice line — no sim change at all, no re-baseline, and `SentimentalDeedThreshold`
stays exactly where it is (moving it *would* be a balance change; rendering it is not). M2a is
sim-side: one query over `ExpeditionResult.Floors` (the record now survives the night as of #679)
plus history rows, and the existing `ProvenanceCard` client-side. Medium overall only because the
copy discipline in M2a needs a tripwire test of its own in the `P2-HONEST` family. **If only one
half ships, ship M2b.**

---

### M3 — The forecast gets a face. *(Mechabellum's pre-fight reveal, delivered by §11.7.3's rule.)* **Cost: medium. Value: high.**

**The mechanism.** The marcher tells you where they are going and what they are short of, in their
own voice, in the Morning — the same read-only derivation from hero state that `CustomerVoice`
already is. Not a new fact: `RaidForecast`/`MusterSystem` already compute it byte-exactly. A new
*channel* for it.

> *"Four, tomorrow. I've never been past three. Ask Halvar how that went."*

**Why.** Two findings converge. First, pre-resolution reveal is a top-three readability mechanism
across the whole sample — Mechabellum clears fog at round start, TFT and Underlords let you scout
during the prep phase — because it lets the player **enter the watch already knowing what should
happen**, which is what converts watching into confirmation-or-surprise rather than passive
information delivery. Second, §11.7.3 is a standing owner ruling that *"reading boards is boring"*
and sim information must arrive through a face. We currently have the strongest pre-watch reveal in
the sample and we deliver it through the weakest channel in our own rulebook.

**Links served.** Link 3 (the hero's judgment becomes audible *before* it is exercised, which is
what makes it read as judgment rather than as a dice roll) and link 2 (the shelf decision becomes an
informed one, which is what makes dilemma 3 — fill the empty slot or upgrade the full one — a real
dilemma rather than a guess).

**Law check.** Passes. Read-only derivation, zero sim change, no ordering, no timer. The honesty
constraint is already recorded and binds here: **stakes qualitatively, never survival percentages**
(§11.4's design note). *"I've never been past three"* is a recorded fact; *"38% wipe risk"* would
be a number the sim did not produce for the player.

**Cost and blast radius.** Godot-only plus one read-only query, but it is a *scene*, not a label, so
it costs more than M1 or M5. It is also the one item here that overlaps existing plan rows —
`P8`/H6 "morning aims" and `P2-PEOPLE`'s scene engine — so it should be built **as a client of the
scene engine**, not as its own mechanism.

---

### M4 — Legible defeat. *(The failure mode every prepare-then-watch game shares.)* **Cost: small–medium. Value: high.**

**The mechanism.** Every death names its **margin**, not just its cause. Today `DeathReport`
produces `"slain by a {MonsterKind}"` on floor N, and the memorial names the gear
(`ExpeditionRevealSystem.cs:303-324`). What the record contains and no surface shows is *how close it
was*: the blow's recorded damage, the hero's HP before it, what their armour absorbed, whether an
unquaffed potion was in the pack. One extra sentence on the memorial and the death card:

> *Floor 4. The blow read 19. She stood at 14, and her plate drank 3.*

**Why this is the highest-evidence item in the document.** The single most damaging complaint found
about any prepare-then-watch game is Loop Hero's *"Far too many times it felt like I lost because
there was nothing I could do"* ([Game-Wisdom](https://game-wisdom.com/analysis/loop-hero)), and its
twin is Majesty's *"both victory and failure can feel meaningless"*. Both are **legibility
failures**, not agency failures — and the autobattlers' floor-level defence against them is exactly
this: TFT and Underlords make the loss arithmetic proportional and transparent so the headline
number is always legible even without a combat log. A permadeath game whose deaths do not say how
close they were is asking the player to accept the harshest event in the game on faith.

**Links served.** Link 5 (the town's memory of the fallen becomes specific) and link 3 (a death that
states its margin is a death the player can reason backwards from into next week's craft — which is
what makes the hero's judgment feel like judgment).

**Law check.** Passes, and it is the *honest* substitute the plan already chose over the rival's
mirror: `P2-PROOF` explicitly rejected "a Fine shield would have held" in favour of *"the memorial
states the recorded facts of the fatal blow — 'The blow read 15. Her shield drank 2.' — and the
player's own head does the counterfactual."* **This recommendation is that ruling, built.** It
invents nothing; it stops discarding what the resolver already recorded.

**Cost and blast radius.** The margin facts live in the last `CombatEvent` for that hero, which the
result already carries and which now survives the night. Small sim query, small client change, one
tone-pack line per voice. The one real cost is prose: deaths never joke (tone register), so these
lines need the same human curate-and-freeze pass as the rest.

---

### M5 — The day's thread survives being read late. *(Weapon Shop de Omasse's missed Grindcast.)* **Cost: small. Value: medium–high.**

**The mechanism.** Two halves, both client-side:

1. **The departure slate stays readable for the whole Quest span**, not only during the walk-past.
   The forge stays open through the march by owner ruling (§11.7.4) and that ruling is right; this
   is the repair the ruling needs. A player who spent the march at the anvil must still be able to
   read *who went down carrying their work* when they look up.
2. **The Night's beat card names its own antecedent.** *"You watched Emberbite leave this morning."*
   §11.7.4 already adopts one story threading the day and calls continuity of reference "the
   cheapest large experience win available" — this is the smallest possible instance of it, at the
   one joint where the payoff lands.

**Why.** The closest precedent to this game shipped a live feed of the heroes' quest running
concurrently with the player's craft, and its reviewers report the obvious consequence: *"unless you
don't do anything, you won't get much of an opportunity to read what's going on"* (PixlBit). We have
shipped the same structure. A feed that competes with the player's hands loses, and the fix is not to
close the forge — it is to make the feed's claim **re-readable at the moment it pays off**.

**Links served.** Link 4's delivery and link 5's. It strengthens nothing in the sim; it stops an
already-built proof from being missed.

**Law check.** Passes trivially — presentation of recorded facts, no verb, no timer, skippable.

**Cost and blast radius.** Godot-only, one session. It belongs to `P2-SCREEN`'s arbiter work (the
slate needs a region claim that outlives the choreography) and should ride with it rather than
landing alone.

---

**Why these five and not the others.** M1 and M5 are cheap and attack the *emotional* half — who am
I watching, and did I see it. M2 and M4 are cheap-ish and attack the *legibility* half, which is
where every game in the sample that failed actually failed. M3 is the expensive one and earns its
place because it is the only item that improves the moment *before* the watch, which the research
ranks as more valuable than anything during it.

**If only one thing gets built, build M2b.** It is a label and a query over a rule the sim has been
enforcing invisibly for months, it needs no Contracts change and no re-baseline, and it converts a
hidden behaviour change into the exact shape of feedback this research ranks highest: a specific
object, named, because of a specific thing it did.

---

## 2. The next tier

Real, adoptable, and beaten on value/cost by the five above. Listed so they are on the record, not
so they get built next.

**M6. The null result gets a voice, as a general rule.** *(The Sims' "yes, and".)* `P2-PROOF`
already ships the instance — Provisioned's *"It would have run the same without it — this time."*
The recommendation is to promote it from an instance to a rule: **whenever a hero's autonomy
produces nothing for the player, the game says so in one sentence rather than dropping the thread.**
The hero who passed on your Fine sword and why. The commission that expired uncollected. The item
that marched and did nothing. Maxis' formulation is *"the game should always try to maintain the
consistency of the player's story"*, and the honest version of that here is not flattering the
player — it is refusing to leave their thread unresolved. Cost: small per instance, and largely
already covered by `P2-HONEST`'s silences. **This is the antidote to T3: the null case gets a
sentence, not a counter.**

**M7. The bounty's judgment stays loud.** Majesty's whole failure is that its reward flag *"is an
expensive and unreliable process"* whose reasoning is invisible. We already emit `BountyJudged` per
eligible hero with its reasoning. The recommendation is defensive: **treat those events as a
first-class surface, not debug output**, because they are the only place the game shows that link
3's sovereignty is arithmetic rather than caprice. Cost: small, godot-only. Would be M-tier if the
surfacing is thinner than the sim's emission — worth a five-minute check before anyone plans it.

**M8. Recall states its price.** Loop Hero's retreat is tense because it is priced in public: 100%
of resources on a full loop, 60% on retreat, 30% on death. Our `RecallPartyAction` has a real price
— the forgone deep floors, the depth record, the bounty — and the button says *"Recall"*. Naming
the price in the button's own copy costs nothing and turns a safety valve into a decision. Cost:
trivial, copy-only. Not in the top five only because the vigil's arm reportedly never fires at all
right now (`P2-LONG-24`), so this would be polish on a surface with no traffic.

**M9. The counter's optimum should not be solvable.** Recettear's negotiation *"degenerates into a
thoughtless, mechanical exercise"* and its community's optimal play is to **skip haggling entirely**
by pricing at 105–110%. Our `HaggleResolver` deserves the same audit: if a fixed multiplier
dominates, the counter is decoration and §10 test 8 applies. Cost: a measurement first, a tuning
wave only if the measurement convicts. **Do not build anything here until someone runs the sweep.**

**M10. Hero-stated wants as an intake channel.** Holy Potatoes has heroes **bid** on weapons matching
their stat preferences and legendary heroes issue stat-gated requests — the hero-initiated ask is the
genre's other honest channel, and we have it (commissions). Recorded only to note that the fifth
channel does not need inventing; what the sample suggests is that our four are already one more than
anyone else ships.

---

## 3. Do not build

Twelve mechanisms that look right for this game and are not. Six of them this project already
refused; they are restated with the *external* evidence, so the next session that re-pitches one
has to argue with a shipped game as well as with `§11.5`.

**T1. A verb inside the delve.** The perennial re-pitch. The closest shipped analogue is Football
Manager's **touchline shouts** — small in-match nudges to morale, explicitly *"simple commands, not
complex adjustments to your plans"*
([FM-Arena](https://fm-arena.com/thread/628-touchline-shouts-in-fm/)) — and the FM community's
standing suspicion about them is the whole argument: a Sports Interactive forum thread is literally
titled *"I am convinced that the match engine does different things depending on whether you're
watching or not"*
([community.sports-interactive.com](https://community.sports-interactive.com/forums/topic/530498-i-am-convinced-that-the-match-engine-does-different-things-depending-on-whether-youre-watching-or-not/)).
A nudge whose effect the player cannot see is indistinguishable from theater, and it *breeds
conspiracy theories about the sim*. Here it would also be dishonest twice over: stage 2 is undrawn
while the player is looking at it, and any verb writing hero state fails
`HeroSovereigntyCensusTests` by name. **Already refused (§11.5, §11.7.5). Stays dead.**

**T2. Outcome wagers / betting on the delve.** Blaseball had betting and it worked — but it worked
as a *live, shared, social* event with same-hour payouts and tens of thousands of people watching
the same game. Single-player, a wager pays out in mood and changes no ledger line, which is exactly
§10 test 2's definition of theater. **Already cut (§11.5). Blaseball is not a counter-example; it is
a multiplayer one.**

**T3. A participation counter — "your gear was carried 47 times."** Death Stranding's *likes* are
the shipped version of this and are the standing complaint about that system: an unattributed
counter is a number, not a proof. This game's entire moat is that a beat is *earned by a
counterfactual*. A parallel tally that pays out for showing up would train the player to read the
number instead of the sentence. **Reject the counter. §2's "no participation credit" is not
negotiable — see M6 for the honest form of the same instinct.**

**T4. The rival's mirror — "a Fine shield would have held."** Already ruled out inside P2-PROOF and
correctly: removing an object that existed is a fact about the player's work; inserting one that
never existed elevates one member of an unbounded hypothetical to chronicle truth. It also converts
Spiritfarer grief into Darkest Dungeon guilt, which is a soft order aimed at the player. **Do not
re-seek it.**

**T5. A scrubbable slow-motion replay of the fight.** This is the single strongest *readability*
tool found anywhere in the research — Mechabellum saves a full replay specifically so a player can
work out *"exactly what is shooting what at any given moment"* after a loss
([Steam Community](https://steamcommunity.com/app/669330/discussions/0/4848778928895297029/)) — and
it is still wrong here. Our fight has no spatial or simultaneous structure to scrub: heroes fight
the floor's monster alone, sequentially, in HeroId order (`ExpeditionResolver`; P2-PROOF's finding
4 already established that an initiative strip would fabricate structure the sim does not have). A
scrub bar would render a fight that did not happen. **Take Mechabellum's post-round stat half (M2);
leave its replay half.**

**T6. Medals, MVPs, or a damage leaderboard.** Mechabellum tags top-damage units with medals; TFT
ranks. The register is competitive and this game's is not (`§6`: Football Manager and autobattlers
were borrowed *from* explicitly minus "competitive framing and leaderboards"). A medal also
re-introduces T3 through the side door. **Reject the ranked form; M6 is the unranked one.**

**T7. A "favourite hero" that changes hero behaviour.** Blaseball's Idol is safe because it is a
*lens*: it changes what the fan sees and is paid, never what the player does on the field. The
moment idolising Torvald makes Torvald more likely to buy from you, it is an order with extra steps
and it writes hero state at apply time — `HeroSovereigntyCensusTests` fails it by name and the
pinned channel count (4) makes widening it a visible diff. **M1 is the lens version. The
behaviour-changing version is banned.**

**T8. Filling the middle of the day with more verbs.** §11.7.4 already bans verb count for its own
sake, and the idle-game literature is the negative control that explains *why* it would not work
anyway: what makes a loop read as hollow is removing effortful **preparation**, not removing
watch-phase input. Loop Hero and the autobattlers keep the prepare step expensive and add nothing to
the watch — that is precisely why they escape the "idle game" label despite an unwatched combat
resolution identical in kind to Cookie Clicker's. **The middle needs a changing question (P2-LONG),
not more buttons.**

**T9. A second meta-currency or upgrade grind to fill days 8–18.** Every substantive Loop Hero
complaint found clusters at hours 15–20 and is about the **camp-building resource grind**, not the
watching: *"Game is boring after 20 hrs [and] forces grind"*
([Steam](https://steamcommunity.com/app/1282730/discussions/0/3104642254788614097/)); TechRadar's
piece is titled *"I love Loop Hero, but it doesn't respect my time at all"* and locates the problem
at hour 3–4 when *"the resource grind becomes the game"* (**unverified** — the article 404s on
fetch; headline and thesis corroborated by
[gamebrowsing.com](https://gamebrowsing.com/2021/03/28/i-love-loop-hero-but-it-doesnt-respect-my-time-at-all/)).
The day-8–18 wall has the same shape. Adding a grind to fill it is the documented way to make it
worse.

**T10. Deeper hero simulation as the fix for attachment.** Majesty's heroes have rich class
preferences — paladins chase danger, rogues chase reward — and reviewers *still* wrote *"The player
lacks any real substantial agency, and therefore both victory and failure can feel meaningless"*
([GameFAQs](https://gamefaqs.gamespot.com/iphone/639934-majesty-the-fantasy-kingdom-sim/reviews/173674)).
Simulation depth did not buy attribution there and will not here. The five-need utility engine is
already cut (§11.5) and this is the external case for the cut. **What buys attribution is
attribution.**

**T11. Fan letters.** The wished-for row closest to this research (`ItemMemory(Item, Kills, Saves)`
already exists on the contract; the tone doc calls it *"highest attribution-thesis payoff"*). It is
cut from v1 (§11.5) and should stay cut, because **P2-PEOPLE's commendation delivers the same
payload cheaper and with a face** — thanks spoken aloud in the tavern, three real beats quoted from
the log — and §11.7.3 says a face beats a letter. Re-pitching letters after the commendation ships
would be two mechanisms for one job.

**T12. A legend generator that composes prose from templates instead of from events.** The
best-documented failure in this entire research pass, and the one most likely to be built here by
accident. Crusader Kings 3's *Legends of the Dead* (2024, $19.99) lets a ruler commission a
chronicler, pick a protagonist and a legend type, and spread the resulting story across the map. Its
Steam rating is **"Mostly Negative" — 31% positive of 1,642 reviews**
([Steam](https://store.steampowered.com/appreviews/2671060)). Wargamer (7/10) diagnosed it exactly:
the generated text is *"awkwardly stitched together generic paragraphs"* unrelated to what actually
happened, major victories are not incorporated, and the achievements feel **"inconsequential and
ephemeral"**
([Wargamer](https://www.wargamer.com/crusader-kings-3/legends-of-the-dead-dlc-review)). Player
reviews: *"after two years I still have no clue how it's supposed to work"*; *"feels like a tacked on
gamey system, not a real choice."* The failure is not "legends are a bad idea" — it is **a legend
whose prose does not name the events that produced it.** `P2-MEMORY` is directly exposed to this,
and its existing R14 rule (gossip must cite the record) is the correct defence. Two hard lines fall
out of the CK3 case and both are already this project's practice, so this trap is about **keeping**
them: every generated line names a real recorded event, and a missing fact renders nothing rather
than a generic line. *(Note the disambiguation: the feature literally called "Chronicle" is CK2's
auto-log, not CK3's DLC. CK2's own complaint is different and milder — "The chronicles are too
short. They delete past entries" — which is an argument for `P2-MEMORY`'s full-retention book.)*

**T13. History that runs backwards.** Caves of Qud's celebrated Sultan histories are generated once
at world-seed time and never reflect anything the player does; the developers' own paper describes
the generator picking an event first and inventing a justifying cause afterward — *"the event's
effect precedes its cause"*
([Freehold Games](https://www.freeholdgames.com/papers/Generation_of_mythic_biographies_in_Cavesofqud.pdf)).
It reads beautifully and it proves nothing. Recorded here because Qud is the game most likely to be
cited in a future session as "roguelike world-history shows your actions mattered," and for its
headline system that is verifiably false. **Our chain runs the other way — cause first, recorded,
then told — and that direction is the whole product.**

**T14. Making the skip feel bad.** Football Manager ships **Instant Result** as a first-class
citizen and a meaningful fraction of its players use it without shame; one write-up frames it as
itself a decision — *"instant results let you dictate how you'd like that match to go while you're
absent"*
([FRVR](https://frvr.com/blog/news/football-manager-26-instant-results-ranked-why-your-instant-results-choices-actually-matter/)).
Mechabellum ships a 2× speed toggle, an explicit admission that a determined outcome has dead time.
*Hurry* is correct, it should never be gated behind a confirm, and skipping's cost stays named in
copy and never engineered (law 7).

---

## 4. What the evidence says about work already planned or shipped

Research that only proposes new work is half a pass. Six things this project already decided are
confirmed by shipped games, and the confirmations are worth recording so nobody re-litigates them.

| Ours | Confirmed by | The evidence |
|---|---|---|
| **The counterfactual proof (link 4, The Telling, #687)** | The genre's hole | Ten shop games, zero proofs of causal impact. Weapon Shop de Omasse shipped our exact premise *without* it, scored 63, and every reviewer who examined its loop mechanically called it hollow. This is the moat, and it is the only thing in the design that no competitor has attempted. |
| **The muster forecast, byte-exact** | Mechabellum's fog-of-war reveal; TFT/Underlords scouting | Pre-fight reveal ranks in the top three readability mechanisms found. It moves the "did my prep matter" moment *earlier*, converting the watch from information delivery into confirmation-or-surprise. We have the strongest version of this in the sample and deliver it as a board (M3). |
| **Hurry, and skipping stays legal (law 7)** | FM's Instant Result; Mechabellum's 2× toggle | Both are first-class, both are used without shame, and Mechabellum's toggle is an explicit admission that a determined outcome has dead time. Never gate *Hurry*. |
| **The commendation (P2-PEOPLE), thanks with three reasons** | The whole async-contribution cluster | Naming the specific deed is what separates a thank-you from a counter — see §5.7. Three beats quoted from the log is the right dosage; a tally would be T3. |
| **"One story threads the day" (§11.7.4)** | Weapon Shop de Omasse's missed Grindcast | The precedent proves a feed that competes with the player's hands loses. Continuity of reference is the cheapest repair, and §11.7.4 already calls it "the cheapest large experience win available." |
| **Cutting the five-need utility engine (§11.5)** | Majesty | Rich hero preferences did not buy attribution there. Attribution buys attribution. |

Two places where the evidence pushes *against* a current default, stated plainly rather than
buried:

- **Rent and dues demoting to "one worded line each with no new verb"** (P2-SCREEN applied default).
  Recettear's escalating dated debt with a real game-over and Winkeltje's 4–5 day debt clock are, in
  both cases, *the* anti-passivity mechanism those games have — they are what makes preparation
  urgent. Our town cannot fail by design, which is a good decision the research does not challenge;
  but the research does say that demoting the pressure heartbeat to a line of prose spends the one
  thing the genre uses to make preparation feel consequential. `P2-LONG-17` giving failure a face is
  the right shape. **Flagging, not recommending: this is an owner call, not a research finding.**
- **Deaths fell ~65% in six weeks** (§11.3's R1 retirement, measured 2026-09-02: sweep deaths 768 →
  266, camped-hero minimum HP now 60% so the camp send verb's arm never fires at all). Every game in
  this sample derives its watch-phase tension from consequence: Loop Hero prices retreat at 60% and
  death at 30%; Backpack Battles' complaint is that a *correct* decision can still be punished.
  **A watch phase with no downside is the definition of a screensaver.** This is already booked
  (`P2-LONG-23`/`-24`) and this research says it outranks every new mechanism below it. If the mine
  does not kill, none of M1–M5 will land.

---

## 5. Per-game notes

Only the load-bearing findings. Full source lists are inline.

### 5.1 The shopkeeper cluster — and the genre-sized hole in it

**The headline finding: across ten shop/craft games checked, ZERO contain a mechanic that proves a
specific player-made item caused a specific NPC's specific outcome and shows the player that
proof.** Recettear, Moonlighter, Winkeltje, Merchant of the Skies, Shoppe Keep, Travellers Rest and
Holy Potatoes make no attempt at it. Potion Craft has recurring-NPC callbacks that motivate a *new*
request but never confirm the last potion worked. Bear and Breakfast's satisfaction signal was
proven gameable by a reviewer who filled a room with *"cardboard boxes and wall-to-wall possum
clocks"* and got five stars
([TheSixthAxis](https://www.thesixthaxis.com/2022/09/15/bear-and-breakfast-review/)). Strange
Horticulture is the only one with genuine proof-of-consequence — a branching epilogue with eight
endings gated on specific plant choices — and it is confined to ~4 named characters and delivered as
retrospective end-text, with GameCritics finding *"no evidence of player choice consequences"*
on-screen for the broader cast
([GameCritics](https://gamecritics.com/damiano-gerli/strange-horticulture-review/)).

**Recettear does not solve the spectator problem; it abolishes it.** The player *directly controls*
the adventurer in real-time action combat — Wikipedia, the store page, the wiki and the control
guides all agree. You can even lend an adventurer your stock before a run, and then fight wearing
the bonuses yourself. So the preparer/user split collapses entirely inside the dungeon. **Nothing
about Recettear's answer to "why isn't watching boring" is available to us, because Recettear never
watches.** What *is* borrowable is the pressure structure we already borrowed: a dated, escalating
debt with a real game-over (5 payments, 10,000 → 500,000 pix over 36 days), and per-customer
reputation that gates how many haggle rounds you get.

Recettear's failure mode is worth recording anyway, because it is ours in a different costume: PC
Gamer (76) wrote that haggling *"degenerates into a thoughtless, mechanical exercise"* once the
habits are learned, and the community's optimal play is to price at 105–110% and **avoid haggling
entirely** to chain Just Price combos
([Steam guide](https://steamcommunity.com/sharedfiles/filedetails/?id=336404171)). A negotiation
with a solvable optimum stops being a negotiation. Worth a glance at our counter's `HaggleResolver`
some day; not a recommendation here.

**The most common complaint in this whole genre is customer repetition at hour 3–4.** Potion Craft:
*"you slowly start noticing the unmistakable flavour of repetition. Everybody wants a healing
potion"*; *"at the 3-4 hour mark, you get to a point where you are just making the same potions for
the same clients all over again"*
([Steam negative reviews](https://steamcommunity.com/app/1210320/negativereviews/?l=english)).
Travellers Rest: *"I'm just playing chore simulator."* Moonlighter's shop half: *"It feels less like
gameplay and more like an idle game giving you mostly-pointless tasks to keep you engaged while your
gold ticks upward"* ([chamomilehasa.blog](https://chamomilehasa.blog/2023/02/08/moonlighter-doesnt-deliver-on-its-premise/)).
**That is the day-8–18 wall, in four other games, at the same wall-clock hour.** It is the genre's
central defect, and `P2-LONG`'s "the player's question stops changing" diagnosis matches it exactly.

### 5.2 Weapon Shop de Omasse — the closest precedent, and it is a cautionary tale

Guild01, 2012/2014, Level-5 anthology (**correction to the brief:** Guild01, not Guild02; creator
Yoshiyuki Hirai, not Matsuno). Forge by rhythm minigame; heroes **rent** the weapon and you are paid
only on a successful return; the **Grindcast** — *"a Twitter-like magical apparatus"* — narrates
their quest **in real time on the top screen while you keep forging on the bottom screen**
([Wikipedia](https://en.wikipedia.org/wiki/Weapon_Shop_de_Omasse),
[Destructoid](https://www.destructoid.com/reviews/review-weapon-shop-de-omasse/)).

Four findings, each of which lands on us directly:

1. **The concurrent feed gets missed.** PixlBit: *"unless you don't do anything, you won't get much
   of an opportunity to read what's going on."* This is the exact shape of our Quest span — the
   forge stays open through the march by owner ruling (§11.7.4), which is right, and it means the
   send-off's naming must survive being read late. See M5.
2. **The craft→outcome link was real but illegible, and reviewers concluded it was fake.** PixlBit
   (1/5): *"Just because you master the rhythm game doesn't mean your weapons will actually turn out
   better... I have nailed every beat, kept perfect heat, and gotten huge chains only to be told my
   weapon was rated 'Dull'... it was clear that the results are largely random."* RPG Site
   corroborates that hero-level/weapon-tier matching mattered more than craft quality. **A causal
   chain the player cannot trace is, for review purposes, a chain that does not exist.**
3. **No reviewer, across ~14 outlets, quotes a line like "the sword you forged saved Hero X."** The
   Grindcast names the hero and narrates banter; success is legible as a payout, never as a cause.
   The premise of indirect consequence shipped without the proof.
4. **Reviewers named our exact standing risk, out loud.** Digitally Downloaded (3/5) built its
   review on it: *"the protagonist gets no role in defeating this evil. He just sits there and
   watches"* — invoking *Waiting for Godot* — and concluded *"it makes it painfully clear how dull
   life is in a narrative when you're not actually the focus of it."* Hardcore Gamer liked the game
   (4/5) and still called *"simply picking a weapon with the best numbers and watching someone else
   use it"* **"uninvolving."** Nintendo Insider (7/10): *"it soon reveals itself to be all style and
   no substance."* Metacritic 63 (5 positive / 21 mixed / 3 negative). No sequel; the concept was
   never reused.

The two most positive reviews (Nintendo Life 90, Destructoid 85) never engage with the passivity
criticism at all. **That is the sharpest single data point in this research: the game that shipped
our premise without link 4 got a 63, and the reviewers who scrutinised its loop mechanically all
converged on "hollow, propped up by writing."** Our counterfactual replay is the thing those
reviewers went looking for and did not find. It is not a nice-to-have; it is the difference between
this game and the one that already tried this and stopped.

### 5.3 Loop Hero and the autobattlers — the purest form

**Loop Hero.** The player places tiles adjacent to the loop *live, while the hero walks*; every
tile is simultaneously a reward and a threat. RPS's framing, quoted secondhand and therefore
**unverified against the primary**: *"There's an uneasy dissonance between your goal (help the hero
win) and the only real action you can take (create a world that's actively trying to murder them)."*
The retreat decision is **priced in a way ours is not**: finishing the loop banks 100% of resources,
retreating mid-loop banks 60%, dying banks 30%
([PCGamesN](https://www.pcgamesn.com/loop-hero/beginners-guide-tips-tricks),
[Slythergames](https://www.slythergames.com/2021/03/12/loop-hero-when-to-retreat-guide/)).
Complaints cluster at hours 15–20 and are about the camp resource grind, not the watching — see T9 —
except for the one that matters most: *"Far too many times it felt like I lost because there was
nothing I could do"* ([Game-Wisdom](https://game-wisdom.com/analysis/loop-hero)).

**The autobattlers.** TFT's plan phase is a flat **30 seconds**; during combat the player may buy,
lock the shop and equip items, but positioning is locked
([League wiki](https://wiki.leagueoflegends.com/en-us/Teamfight_Tactics_(game))). Super Auto Pets and
Backpack Battles allow literally nothing during combat, and reviewers treat that as correct and
deliberate — one explicitly compares the appeal to Slay the Spire's: *"the constant stream of
important out-of-combat decisions makes the experience always engaging"*
([Esteemed Steam Games](https://www.esteemedsteamgames.com/posts/super-auto-pets-in-depth-review-steams-cutest-auto-battler)).
Backpack Battles supplies the crispest variance complaint found anywhere: *"A player can make all
the right choices during the buying phase, but then still be punished in battle. Their weapon may
have a 95% chance of hitting, but still miss"*
([indiegames.substack.com](https://indiegames.substack.com/p/packing-a-punch-backpack-battles)).

**Mechabellum is the answer key on readability**, and its toolkit is worth listing in full because
four of its five tools are things we either have or are building:

| Mechabellum tool | Ours |
|---|---|
| Fog of war clears at round start — you see the enemy's board *before* the fight | The muster forecast (byte-exact) — **we have it, delivered as a board** (M3) |
| 2× speed toggle | *Hurry* — we have it |
| Post-round per-unit damage stats, medals on top performers | **We do not have the stats half** (M2); the medals half is banned (T6) |
| Full replay, scrubbable in slow motion | The Telling (#687) — ours is *stronger*, because the sim answers the counterfactual instead of making the player guess |
| Sandbox re-creation of a lost round to test alternatives (**unverified**) | Refused: it would be a what-if the sim never rolled |

**Slay the Spire is the clean negative case.** It is *not* prepare-then-watch — every turn is a fresh
decision with the enemy's next intent telegraphed
([Fandom](https://slay-the-spire.fandom.com/wiki/Combat_Mechanics)). The nearest genre neighbour
deliberately kept per-turn agency. Worth knowing when someone argues "the deck is the preparation."

**Football Manager** ships **Instant Result** as a first-class alternative to watching, and the
argument about it is genuinely unresolved — see T12. Its in-match complaint set is a legibility
complaint, not an agency one: *"match highlights become boring quickly, with 90% of highlights
boiling down to shot blocks and crosses"* (**unverified** — search-index wording). *If the simulated
thing does not look like the thing, watching stops teaching you anything about your preparation.*

**Idle games are the negative control.** Cookie Clicker's appeal is named by critics as *"mostly
just watching numbers go up"* ([Vice](https://www.vice.com/en/article/cookie-clicker-wasnt-meant-to-be-fun-why-is-it-so-popular-8-years-later/)),
and Progress Quest was literally written as a parody of the phenomenon. What separates Loop Hero and
the autobattlers from them is not watch-phase input — they have none either — it is that the
**preparation stayed expensive**. That is the single most useful structural fact in this document and
it is why T8 is a trap.

**Majesty is the cautionary tale on link 3.** Heroes cannot be commanded at all; the only lever is a
reward flag, *"even then their cooperation is not guaranteed"*
([Wikipedia](https://en.wikipedia.org/wiki/Majesty:_The_Fantasy_Kingdom_Sim)). Contemporary reviews
were warm — Computer Gaming World called it *"a quick-paced, hands-off formula that defied our
expectations and won our hearts"* — but the dissent is exactly our risk: *"The player lacks any real
substantial agency, and therefore both victory and failure can feel meaningless"*, with the concrete
grievance being that the lever is unreliable: *"It's an expensive and unreliable process to persuade
them to destroy that bear den 50 metres down the road."*
([GameFAQs](https://gamefaqs.gamespot.com/iphone/639934-majesty-the-fantasy-kingdom-sim/reviews/173674)).
**Note what the complaint is not about.** Nobody says "I wish I could command them." They say the
influence lever did not reliably produce the outcome they aimed at. Our bounty is that lever, and
its four moves (escrow → their judgment → one commits → payout/death/refund) already emit the
judgment as visible `BountyJudged` events. **Keeping those legible is a link-3 defence, not
polish.**

### 5.4 Blaseball — a text simulation with no gameplay that made thousands of people care

Worth its own section because it is the extreme case: fans watched a baseball sim they could not
play, rendered as text descriptions of at-bats, and formed genuine attachments. Two mechanisms did
the work, and only one of them is available to a single-player game.

- **Pick a team, then pick an Idol.** *"Fans may choose to idolize a player on that player's page"*
  — one at a time, 200 coins to switch, and the fan earns payouts tied to that specific player's
  performance. The Idol Leaderboard *"stands as a monument to the 20 most idolized players"*
  ([Blaseball Wiki](https://www.blaseball.wiki/w/Idols)). The pre-commitment is what turns a
  simulation tick into news about someone you chose.
- **Weekly elections where fans voted on rules.** Community-scale; not portable.

The transferable half is M1. The betting half is T2.

### 5.5 The Sims — the one design principle worth stealing whole

Maxis' autonomy rule, per GMTK's write-up: Sims will relieve their own bladder but *"won't
autonomously quit their job"*; the studio took *"inspiration from 'yes, and', from improvisational
comedy"* so that *"the Sims try to build on the player's actions, and try not to negate them"*; and
the stated goal is that *"the game should always try to maintain the consistency of the player's
story"* ([GMTK](https://gmtk.substack.com/p/the-genius-ai-behind-the-sims); the article attributes
these to Maxis collectively rather than a named designer).

For us this is not a licence to make heroes prefer player-crafted goods — that would write hero
state and fail the sovereignty census. It is a rule about **narration**: when a hero's autonomy
produces a null result for the player, the game should say so in the player's own story rather than
silently dropping the thread. `P2-PROOF` already got this exactly right with the Provisioned
no-drama line — *"It would have run the same without it — this time."* **The principle deserves to
be written down as a general rule, because it is the thing that keeps hero autonomy from reading as
the game ignoring you.**

---

### 5.6 Emergent story from autonomous actors — what gets recorded, and where it is read back

**Dwarf Fortress.** Every historical figure carries family, titles and *"heroic feats (e.g. kill
lists)"* ([DF wiki](https://dwarffortresswiki.org/index.php/DF2014:Historical_figure)); artifacts
record creation circumstance and every ownership transfer by combat, diplomacy or theft; and — the
finding that produced M2b — **weapons carry their own kill lists**, and a long enough list
auto-promotes an ordinary weapon to semi-legendary, where it appears on the Artifacts screen in blue.
Dwarves also spontaneously name weapons they are attached to; the exact rule is undocumented, but
*"slaying an important historical figure… will often be enough."*

The recurring complaint is **readback, not recording**: *"the switch between Fortress & Legends mode
should be much more streamlined"*; *"I lack the tools to navigate and filter easily to get the
information that I want"*
([Steam](https://steamcommunity.com/app/975370/discussions/0/5792223132457051068/)). Legends mode
requires retiring the fort and exiting to the main menu. **`P2-MEMORY`'s "queryable by actor and
day" success criterion is the right one and this is the evidence for it** — the richest world-history
system ever shipped is bottlenecked at its index, not its data. (I could not retrieve an exact
in-game string of the form "your axe killed X"; the *mechanism* is confirmed, the surfaced copy is
**unverified**.)

**RimWorld.** Two mechanisms, and the cheap one is better. Sculptures get randomly generated
descriptions drawn from a pool of real logged events — verbatim: *"bears an image of Taylor 'Pixie'
Jenkins searching for water with cracked lips… This depiction tells the story of Pixie getting
heatstroke on the 8th of Jugust"* ([Steam
thread](https://steamcommunity.com/app/294100/discussions/0/3057364785114310711/)) — but the same
generator also emits noise with no event behind it (*"depicts several peppers. There is a snake in
the upper part of the image"*). The reliable one is the **mood thought**: *"My friend, [Name], died"*
(−10, 20 days, stacks to 5), *"My rival, [Name], died"* (+10)
([RimWorld wiki](https://mail.rimworldwiki.com/wiki/Thoughts)). It names a real colonist, it fires
every time, and it fires on the same tick. **Across every game in this research, that is the
single most effective small feedback unit found** — see the table at the end of this section. The
end-game credits listing the colony's dead (v1.1) is the same instinct at campaign scale, and it is
what our chronicle already does.

**Crusader Kings.** See T12 — the most instructive failure in the pass. Also a disambiguation worth
recording so nobody mis-cites it: the feature called **"Chronicle" is CK2's** auto-log of births,
deaths, battles and title changes, whose player complaint is *"The chronicles are too short. They
delete past entries sometime so you can't have a full chronicle"*
([Steam](https://steamcommunity.com/app/203770/discussions/0/3288067088092981183/)) — an argument
*for* `P2-MEMORY`'s full retention. CK3's *Legends of the Dead* is the template-generator that scored
31%.

**Wildermyth** closes the loop more completely than anything else in Cluster A. Permanent Aspect
tags (`missingLeftArm`, `deadLover|HERO|TIER`) are **queryable by later story text via parameter
substitution**, so a dialogue card mid-campaign can reference one specific earlier choice by name
([Wildermyth wiki](https://wildermyth.com/wiki/Aspect)). RPS's example ran across **three separate
campaigns** before paying off: Jilly Poole *"eventually becom[ing] the recognized leader who defeated
the Drauven"* ([RPS](https://www.rockpapershotgun.com/wildermyth-review)). Its documented failure is
directly ours: **legacy items do not fully persist** — *"the current system… is to simply convert any
item into a generic leveled down item with in some cases no hope of ever reclaiming the item with a
unique effect"* ([Steam](https://steamcommunity.com/app/763890/discussions/0/2266942917233947453/)).
Our heirloom reforge is the analogous seam, and it is where an item's accumulated record is most at
risk of being silently flattened. Worth a look at whether `ReforgeHeirloomAction` carries the parent
item's history forward or starts a new one — **I did not verify which**, and it is a five-minute
check for whoever picks up M2.

**Caves of Qud** is the negative case — see T13.

### 5.7 Asynchronous contribution to a stranger's run — and the gap nobody has closed

**The central correction to a claim this program might otherwise have made: "You were praised" does
not exist.** Extensive searching across Fextralife, Steam, GameFAQs and general web search found **no
popup or notification text of that shape in any Souls game or Elden Ring** — recorded as
**verified-absent**, not merely unverified. What actually happens when someone rates your message:

| Game | What the author gets |
|---|---|
| Dark Souls 1 | **Nothing.** *"Getting your own messages rated doesnt do anything at all"* ([Fextralife](https://darksouls.wiki.fextralife.com/Online)) |
| Dark Souls 2 | A live HP heal *"equal to the effect of a Stone of Healing"* |
| Dark Souls 3 | *"If other players rate your messages, you'll regain a little HP"* — no popup described |
| Elden Ring | *"1 Flask of Crimson Tears' worth of health at no cost… your character takes on a crimson glow"* ([Fextralife](https://eldenring.wiki.fextralife.com/Messages)) |

It is a **wordless mechanical tick**, delivered only while the author happens to be online, carrying
zero information about who rated it or why. And bloodstains — the mechanism that *does* carry a name
and a moment — give the dead player **no notification at all** that anyone ever watched theirs. Its
failure modes are the famous ones: *"Try Jumping"* at cliff edges, *"Amazing Chest Ahead"*, and
Elden Ring's false illusory-wall spam, where *"players became frustrated with wall-smacking spam"*
([KnowYourMeme](https://knowyourmeme.com/memes/dark-souls-player-messages)). One player reported
hundreds of hours before a first rating.

**Death Stranding likes are an aggregate, never an attribution.** No source confirms any "player X's
ladder saved me" notification; structures ride popularity waves and accumulate an impersonal count,
and the player complaint is exactly what T3 predicts — *"some noting that others receive thousands of
likes while their structures receive minimal recognition"*, some reporting *"0 likes on 99% of their
structures"*, and likes that *"appear to do nothing or only increase player level."* (Player-level
sentiment is well attested; **no critic pull-quote could be sourced this session** — see §6.)

**Journey** withholds identity entirely during play and reveals it exactly once, post-credits, on
the *Companions Met Along the Way* screen with real platform usernames
([ScreenRant](https://screenrant.com/journey-ps4-game-companion-real-other-player-ai/)). The design
rationale is explicit: names and chat *"would allow players' biases and preconceptions to come
between them and the other player."* Its failure is worth knowing because it is a naming failure:
Escapist — *"After sitting through the credits and seeing four separate names pop up under people I
met along the way, not only am I not sure which one was my traveling companion, but I don't even know
if I was with the same person the entire time."* **A name without a deed attached does not land.**

**Spiritfarer** — the register this game named as its closest relative — pairs its farewells with a
**permanent keepsake**: each departed spirit leaves a spirit flower. Giovanni's line, verbatim:
*"I've never deserved you anyway... But I've loved you, and that won't stop even if I'm not around
anymore. The ones who really love you never really leave you."* Whether farewell text varies with
player actions rather than only timing is **unverified**. The keepsake is the mechanism worth noting:
a small, permanent, inventory-visible object standing for one relationship at the moment it ended —
which is the heirloom and the grave-marker, and confirms `P2-PEOPLE`'s wake verbs are aimed correctly.

**Animal Crossing** is the cautionary tale on autonomous-NPC warmth at scale: *"to call the things
villagers say 'repetitive' would be unnecessarily kind"*, and *"each villager 'type' (of which there
are only eight) has literally only got one line of dialogue that they repeat every single time"*
([Trekking with Dennis](https://trekkingwithdennis.com/tag/animal-crossing-new-horizons-villager-dialogue)).
Eight archetypes, one line each, is a shipped Nintendo game's failure and it is the exact shape of
our six starters deriving from a pinned `(Id, Name)` hash — which `P2-PEOPLE` already caught
(Brunhilde and Moss share a trait pair) and answers in prose.

**Super Mario Maker** has the best-documented push mechanic in the set: a **default-on notification
when someone plays your course**, escalating when someone beats your world record. Aggregate
otherwise; and its structural failure is total — Nintendo Network shut down 2024-04-08 and the
feedback apparatus ended. The genuinely useful side-example is Super Mario 3D World's **Mii Ghosts**:
a stranger's recorded play becomes an object in your world *with a reward embedded in it*, which is a
more literal "someone else's work materially helped you" than any like counter.

**The finding that matters most.** Across both AAA precedents, **the mechanisms that carry a name and
a moment give the contributor nothing, and the mechanisms that loop back to the contributor are
anonymous aggregates.** Nobody has shipped both halves at once. Ranked by (names a party) × (fires
reliably):

| Rank | Unit | Names a party? | Fires reliably? |
|---|---|---|---|
| 1 | RimWorld's *"My friend, [Name], died"* mood thought | Yes | Yes, every time, instantly |
| 2 | Journey's post-credits companion name | Yes (real username) | Once, guaranteed |
| 3 | Spiritfarer's farewell line + spirit flower | Yes (fixed per character) | Once, guaranteed |
| 4 | Mario Maker's world-record notification | Yes | Superlative case only |
| 5 | Dwarf Fortress's blue artifact-list entry | Implicitly | Threshold only |
| 6 | Souls/Elden Ring rating tick | **No** — anonymous | Only while author is online |
| 7 | Death Stranding like counter | **No** — anonymous aggregate | Automatic, unattributed |

*Emberbite turned the killing blow on floor 3. Torvald lives.* names a party, names a deed, names a
place, and fires on the night it happened. **That sentence sits above row 1 of this table, and no
shipped game in this research produces its equivalent.** The five recommendations above are all in
service of making sure a player is present, oriented and reading when it arrives.

---

## 6. Unverified claims register

Everything below reached this document through a search-engine summary rather than a fetched primary
page, or could not be confirmed at all. **None of it is load-bearing for M1–M5.** It is listed so a
later session does not launder any of it into fact.

| Claim | Status |
|---|---|
| RPS's *"uneasy dissonance between your goal... and the only real action you can take"* (Loop Hero) | **Second-hand.** Quoted via a PCGamesN summary; the RPS original was not fetched. |
| TechRadar's *"the resource grind becomes the game"* and the hour-3–4 timing | **Unverified.** The article 404s on direct fetch; headline and thesis corroborated by a secondary blog. |
| Loop Hero developer's stated intent (*"control the adventure, not the hero"*) | **Second-hand.** Windows Central interview inaccessible; from search index only. |
| PC Gamer's Loop Hero review wording (*"pleasantly tense battles of attrition"*) | **Lower confidence.** Paywalled; from search index. |
| The community claim that TFT's agency *"is very bad at communicating"* itself | **Unverified.** Reddit was unfetchable this session; aggregated sentiment only. |
| Mechabellum's sandbox re-creation of a lost round | **Unverified.** Only a search-engine synthesis; no primary confirmation. Nothing in this doc depends on it (it is rejected under T5 regardless). |
| FM's *"90% of highlights boiling down to shot blocks and crosses"* | **Second-hand.** Aggregated Steam/SI-forum sentiment, exact wording unconfirmed. |
| Dota Underlords' decline being caused by watch-phase agency complaints | **Not established.** No evidence found either way; do not repeat this as a cause. |
| Frame-level combat legibility in TFT and Super Auto Pets (damage popups, hit-flash, cast bars) | **Unresearched.** Text sources do not describe presentation at this level. If this detail ever becomes load-bearing for a build, it needs a footage pass, not another text search. |
| Recettear's exact haggling UI (slider vs. typed price) | **Unverified.** Mechanics confirmed; the interface was not. |
| Recettear as spectator-of-an-autonomous-Louie | **Contested and rejected.** One fan blog describes it that way; the store page, Wikipedia, the wiki, control guides and every review agree the player drives the adventurer directly. This doc uses the majority reading. |
| A Weapon Shop de Omasse Grindcast line naming a specific weapon's causal effect | **Searched for and not found**, across ~14 outlets. Recorded as an absence, not as a confirmed non-existence. |
| Blaseball Idol payout details beyond "coins tied to the idol's performance" | **Partially verified.** The Pendant/Snack specifics come from the wiki summary; the switch cost (200 coins) and the leaderboard wording are quoted. |
| The Sims autonomy quotes | **Attributed to Maxis collectively** by GMTK's article, not to a named designer. Do not attribute them to Will Wright. |
| *"Agency is a sensation of authorship"* framing | **Dropped.** It surfaced in a search summary of a Medium post and could not be traced to a citable primary; this doc does not rely on it. |
| A Souls/Elden Ring popup reading *"You were praised"* | **VERIFIED ABSENT.** Searched across Fextralife, Steam, GameFAQs and general search; no such notification exists in any of the four games. The reward is a wordless HP tick. This doc previously risked asserting the opposite; recorded here so nobody re-imports it. |
| Whether a Souls rating reward queues for an offline author | **Unverified.** No source addresses it. |
| Death Stranding's exact like-notification wording and timing (real-time vs. next boot) | **Unverified.** Fandom/Reddit/GameFAQs blocked this session. The *aggregate, unattributed* character of the mechanic is well attested; the copy is not. |
| Death Stranding "likes are meaningless" at critic level | **Player-level only.** Well attested in Steam discussion; no reviewer pull-quote sourced. |
| Dwarf Fortress's exact in-game wording for a weapon's kill list / semi-legendary promotion | **Unverified.** The mechanism is confirmed by two wiki pages; the surfaced copy is not. M2b depends on the mechanism, not the copy. |
| RimWorld mood/mental-break complaints (disproportionate debuffs) | **Paraphrased** from search-summarised Steam threads, not individually re-fetched. Moderate confidence. |
| Whether Spiritfarer's farewell text varies with player actions rather than only timing | **Unverified.** Reviews describe the player-controlled variable as *when*, not *what*. |
| Whether `ReforgeHeirloomAction` carries the parent item's `History` forward | **Not checked in this pass.** Flagged because Wildermyth's documented legacy-item flattening is the analogous failure. Five-minute check for whoever picks up M2. |
| Animal Crossing photo-gift bell threshold (750 vs 1,000) | **Unresolved.** Sources disagree; likely a version or friendship-tier difference. Nothing here depends on it. |

**One methodological flag about this pass itself.** The session's WebSearch budget (200 calls) was
exhausted partway through, shared across the parallel research agents. The back half of the research
leans on WebFetch against known URLs, which biases the evidence toward sources whose pages render
without a paywall — Steam, Wikipedia, wikis, smaller outlets — and away from PC Gamer, TechRadar,
Eurogamer and RPS. The negative-signal hunt is therefore **weighted toward player complaints over
professional criticism**. That is not the worst bias for this question, but it is a bias, and any
follow-up pass should start where this one thinned out.

---

## 7. Shelf life

This is a research artifact, not a plan. It has no status to keep current and it asserts nothing
about what is built — `git log` does that (rule 8). Its job is done the moment §11 either adopts one
of M1–M5 by name or declines to, and the "do not build" list has been read once by whoever next
proposes a mechanism from this space.

**Delete this file when either of those happens**, and in the same PR that records the adoption. If
it is still here fourteen days after the last PR that referenced it, it is abandoned twice over at
this repo's cadence and rule 7 applies without further discussion.
