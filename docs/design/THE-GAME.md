# Maker's Mark

*What the game is, what it borrows, and how a person plays it.*

This document describes the game as it exists. It is not a plan and contains no proposals — where
something is absent, it says so as a fact about the game rather than as work owed. The planning
half lives separately, in `MAKERS-MARK.md` §8–§12; the owner's standing direction for where the
game goes next is §11.7 there.

---

## 1. In one page

You are the blacksmith. You never go into the mine.

Every fantasy RPG has a shop with a hammer and a person behind it who exists to sell you a sword.
This game is that person. The adventurers are the NPCs — six of them, autonomous, with names and
memories and a habit of dying. They decide what to buy, when to descend, how deep to push, and when
to run. You cannot order them. You can only make things, price things, and hand things across a
counter, and then watch what your work does to someone else's life.

**The whole game is one sentence: a specific person's fate provably turned on work your hands did,
and you were watching when it happened.**

Four links carry that. You make a thing, and it is stamped with your mark. The thing reaches a hero
through one of four honest channels. The hero carries it into the dark on their own judgment. The
game then proves, by re-running the fight without your item, that it mattered — and the proof
arrives on your screen that night with the hero's name in it.

*Emberbite turned the killing blow on floor 3. Torvald lives.*

That line is the product. Everything else is machinery for producing it.

**Influence, never orders.** You shape what heroes can do; you never choose what they do. The one
lever aimed at *where* they go is the bounty, and it is still only an offer: you escrow gold against
a floor, and every eligible hero weighs it for themselves against their greed, their level and the
depth. One of them may take it. Sometimes they take it and die on the floor you paid them to reach.
The mechanism, move by move, is §4.5.

**Narrow now, by choice.** Six hero classes, four professions, three live venues, a five-floor mine,
a campaign that resolves in about six weeks of in-game days. The narrowness is a focus decision, not
a ceiling: it exists so every system ships finished rather than sketched, and the architecture is
built to widen — content can sit inert in the code until a determinism-gated flip turns it on. Deep
before wide.

**The register is cozy, and the cozy is load-bearing.** This is a warm game about loss. Heroes die
permanently. Their gear comes back to your anvil as an heirloom you can reforge, carrying their
lineage forward into the next hand that holds it. The town remembers them on a wall you can honor.
Nothing about the tone is grim; the closest relative in feel is *Spiritfarer* — a game where
goodbye is content, not failure.

---

## 2. The spine

Five links from your hands to their story. Each is a real mechanism, not a metaphor.

### Link 1 — You make a thing, and it is provably yours

Every item you craft is stamped `MakersMark("You", day)` at the moment of minting. That stamp is
what the rest of the chain keys on: `Item.PlayerCrafted` is simply "does this carry a mark."
Rival-shop goods never have one. This is why a purchased masterwork still counts as yours — the
Foundry's guaranteed items mint through the same forge and carry the same stamp.

Quality comes from your hands. The forge minigame is two acts — bellows and strikes to shape, then
one plunge to quench — and the grade it produces sets the item's tier. Alchemy, tanning and
engineering each have their own act. The materials you choose set the ceiling; your timing sets
where in the band you land.

### Link 2 — The thing reaches a hero, through four honest channels

- **The shelf.** You stock and price; heroes shop each Morning on their own budgets and needs.
- **The counter.** You open it and serve a customer face to face — they speak their want first,
  you present, suggest, and haggle.
- **The commission.** A named hero asks for a specific slot at a minimum quality, five days to
  fill, a premium if you do.
- **The vigil runner.** A party camped in the mine can be sent one consumable from your hands, for
  a fee, while they wait in the dark.

No channel lets you push an item onto a hero. Every one of them ends with the hero deciding.

### Link 3 — The hero carries it into the dark, on their own judgment

Parties form themselves — three heroes, anchored by a Vanguard or Sentinel where one is available,
in roster order, with no player input at any point. They target exactly one floor past their
personal best, unless someone on the party accepted your bounty, in which case the bounty's floor
wins.

They fight, they take wounds, they quaff what they packed, they push or they turn back. Every one of
those decisions is theirs.

### Link 4 — The game proves your item mattered

This is the part no other shop game has.

When the expedition resolves, an attribution engine re-runs the recorded fight *counterfactually*:
it asks what would have happened with your item removed. It draws no new randomness — it replays
the rolls that already happened against different numbers. When the answer changes, it emits a beat:

- **KillingBlow** — your weapon landed the hit that ended it.
- **LethalSave** — your armour or shield absorbed what would have killed them.
- **BreakpointClear** — your gear took them over a threshold they could not otherwise cross.
- **Provisioned** / **PotionLifesave** — the consumable you sent kept them standing.

Beats are only ever emitted for player-crafted items. There is no participation credit.

### Link 5 — The outcome becomes the town's memory, with your name in it

The beat surfaces that night at the top of the ledger. It then persists: in the item's own history,
in the gossip heroes tell each other the next morning, on the legends wall, in the chronicle, and —
if the bearer dies wearing it — in the memorial that names the gear they fell in.

*The mechanism-level trace of this chain, function by function, is in `MAKERS-MARK.md` Appendix A §4.*

---

## 3. How to play

### 3.1 A day, start to finish

You wake at Dawn in a top-down pixel-art town. The HUD carries the day, your gold, the living
heroes, five slot pips — the day's action budget — and, once you have earned any, chips showing your
standing with the ore factions.

**Read the boards.** The muster board forecasts today's parties: who marches, to which floor, at
which venue, and who is going down with an empty gear slot. The forecast is not a guess — it
byte-matches what will actually happen. The commission board holds up to three standing asks from
named heroes: a slot, a minimum quality, five days, a premium for filling it.

**Work the room.** In the tavern, patrons with live business each carry a line and a thread. Pursue
one and the handshake section opens: shake on the commission, or turn it down, face to face. The
tavern runs two acts — the commission handshake in the morning, the ore handshake at night.

**Work the forge.** The two-act minigame: bellows and strikes to shape the piece, then a single
plunge to quench it. About ten seconds once you are good at it. When a recipe and material have a
proven trace, *forge another like it* repeats your grade in one click.

Beside the recipes sits the **Foundry** — the forge's own counter. It shows your forge tier, your
coal and flux, and the price of the next tier. Four things are bought here:

- **Raise the forge**, 400 → 1,600 → 6,400 → 25,600 gold, each also demanding 25 units of the ore
  from its matching mine floor. Gold alone never buys past what the mine has actually given up.
- **Coal and flux**, 4 and 40 gold, the consumables the premium work runs on.
- **A masterwork attempt** — from Tier II — 3 coal, 1 flux, materials, and a gold surcharge scaled
  to your tier. It draws no dice at all. It is a purchased guarantee of Superior, and Masterwork if
  your material outgrades the recipe.
- **A legendary commission** — 3,000 gold times your tier, double materials, a guaranteed
  Masterwork, four in an entire campaign. It rides the evening bell as a ceremony; a tray chip holds
  the promise until then.

**Serve the counter.** The customer speaks first — what they want and what they can spend. You
present an item, suggest a second, and haggle: accept, hold firm, or counter. The day holds at
Morning while a counter session is open, so the session is as long as you want it. Pinning a price
exactly earns goodwill that compounds into later commission premiums; over-asking is a fleece they
remember.

**Stock, price, buy, post.** Shelf work — stocking, repricing, unstocking — is free. Real work
spends one of five slots. You can change your professions here too, from the progression panel, if
the town's asks have drifted away from your bench; it takes effect at the bell.

**Ring the first bell — "Send them off."**

**The Quest span conducts itself.** The heroes file past your shop toward the mine, and the
departure slate names every marcher carrying your work — all of them, or, honestly, *"Nobody in
this party carries anything you forged."* The forge never closes; you can keep working through the
show, and *Hurry* skips the choreography without skipping the question that follows.

**The vigil stop.** If a party clears the first checkpoint cleanly, they camp — and the world stops.
There is no timer. The winch-house slate shows who is down there, their remaining HP, the heals they
carry (*of which yours: N*), and what still waits below. Three verbs end it:

- **Send the runner** — a fee, and one consumable from your inventory goes to the front of their
  pack.
- **Bring them home** — they bank what they have and surface without rolling the deep floors.
- **Send them deeper.**

You can craft the salve from inside the stop and hand it over in the same breath. The world waits
while you work.

**Deep Vigil is a held breath.** No verbs, deliberately. Nothing has been rolled yet that a verb
could honestly touch.

**Night — "Snuff the lanterns."** The reveal leads with your mark. The first card is the one
carrying an attribution beat, the item's own icon on the line:

> *Emberbite turned the killing blow on floor 3. Torvald lives.*

Then the rest of the night: who returned and who did not, memorials naming the gear the fallen wore,
loot, depth records, and the ore heroes hauled up — offered at the price you will actually pay, with
the faction's favour named when it moved the number. The ticker notes the rent paid, the guild's
dues assessed, a hero's promotion, a bounty collected.

Night has its own verbs: **buy ore** — which doubles as the only sanctioned gift in the game, since
you pay the hero directly and may pay well; **shake on ore** in the tavern's second act; **honor a
memorial**; **reforge** a fallen hero's recorded gear into an heirloom, choosing which recipe and
which metal its next life takes; and **post a bounty** with the day's lesson fresh.

Then the day turns. Slots refill, rent ticks closer, and it is Dawn.

### 3.2 The phases

The day is five kernel phases presented as two acts and a show. A phase is player-operated only when
it has verbs of its own; the rest conduct themselves.

| Phase | Called | The question it asks | Your verbs |
|---|---|---|---|
| Morning | Dawn / Prepare | *What do you make, and who do you make it for?* | ~20 of the 24 — craft, stock, price, counter, commissions, bounty, Foundry, professions, talents |
| Expedition | Quest | *Whose work goes with them?* | none — the send-off names the carriers |
| Camp | Vigil | *Do you reach into the dark, or let them decide?* | send supply, bring home, send deeper |
| ExpeditionDeep | Deep Vigil | — | none, by design |
| Evening | Night | *What did your work do?* | buy ore, honor, reforge, post bounty |

The middle of the day cannot be filled with intervention, and that is architectural rather than
incidental: the deep floors have not been rolled when you are looking at them, so any verb there
would either lie about a decided outcome or fake one that has not happened. What the middle can do
is stage stakes, and it does.

Skipping stays legal. You can ring straight through a day you do not care about.

### 3.3 Your first week

Day one you have one profession, a handful of copper, an empty shelf, and six strangers.

The tutorial runs three days as an apprenticeship rather than a tooltip tour: make one thing, sell
one thing, watch one raid resolve. By the end of it you have picked a second profession.

The first real lesson lands around day four, and it lands as a death. A hero you sold to goes one
floor past their competence and does not come back. Their memorial names the gear they died in — and
if that was your make, the sentence has your work in it. Nothing in the game punishes you for it.
The town keeps going, the roster refills, and you have learned what the numbers on a shelf actually
weigh.

By the end of the first week you know the shape of it: the roster's needs drift, veterans get
pickier, and the shelf you stocked on day two is the wrong shelf by day seven.

### 3.4 The shape of a campaign

Around day ten the heroes stop accepting Poor work. A veteran with a floor-3 record will not spend
on something that will get them killed at floor 4, and the commission board starts asking for
Superior before floor 5 is reachable at all. This is the point where the forge minigame stops being
a formality.

Relationship bands accumulate quietly underneath. Heroes remember who sold them the thing that saved
them and who fleeced them at the counter, and it shows in willingness and in which commissions come
your way.

By roughly day thirty a working shop has more gold than shelf space to spend it on, and the Foundry
is where a mature smith's money goes — tier by tier, then into guaranteed work no dice can take
away. The four legendary commissions in a campaign are the ceiling of that curve.

Act III turns when someone reaches floor 5. Five days later the game reads back everything your mark
touched, and the world stays open afterward.

### 3.5 The six dilemmas

The decisions the game is actually made of.

1. **Sell the good one, or hold it for the hero who needs it?** The shelf pays now; the commission
   pays more, later, to a named person — if they live that long.
2. **Price for the sale, or price for the relationship?** A pinned price earns goodwill that
   compounds. A fleece earns gold once.
3. **Fill the empty slot, or upgrade the full one?** The muster board tells you who is marching
   under-equipped. It does not tell you who will survive.
4. **Spend the slot, or bank it?** Five actions a day, and shelf work is free — the budget is a
   real constraint on ambition, not a formality.
5. **Buy the ore, or buy the goodwill?** Ore purchases are the one place you can transfer wealth
   directly to a hero, and paying above the ask is the only gift the game sanctions.
6. **Send the runner, or trust their judgment?** The full shape of this one is worth stating
   plainly: provisioning a camped party provably saves that party, and measurably endangers the run.
   A topped-up party dares one floor deeper, and the deep floors are where heroes die. Both halves
   are real, and the game does not resolve them for you.

---

## 4. The core systems

Ten systems, each with what it is, why it exists, and what would break without it.

### 4.1 Crafting and quality

Four professions — blacksmith, alchemist, tanner, engineer — each with a real interactive craft, a
recipe tree gated by talents, and materials that set the quality ceiling. Quality bands run Poor,
Common, Fine, Superior, Masterwork, and they scale an item's stats by 80% to 160%.

The forge's two acts are the archetype: shaping is kinetic, quenching is a single committed moment.
Alchemy and tanning are deliberately unhurried by contrast — the professions are meant to feel
different in the hands, not merely differently themed.

*Without it:* the game becomes a spreadsheet. The mark has to be earned by a hand, or link 1 is a
formality.

### 4.2 The heroes

Six classes across anchor and striker roles, each with traits, needs, relationships, XP and levels,
permadeath, memorials, and heirlooms.

Their autonomy is five legible arithmetic rules over true memories — what they can afford, what they
need, what they remember about you, how deep they have been, and what a bounty is worth. It is not a
utility engine and it is not an inner life. The reason it reads as personality anyway is link 4: the
proof layer makes five rules feel like a person, because the outcomes are specific and named.

Each hero card carries the day's decisions in plain language — which item they chose, which they
passed on, and by how much.

*Without it:* the customers become vending machine inputs and the deaths stop meaning anything.

### 4.3 The expedition

Parties of three, formed without you. One floor past their best, unless a bounty overrides. Stage one
resolves at departure; a clean checkpoint parks them in camp; the deep floors resolve after.

Wounds do not outlive the night. No hero ever quits the roster. The town cannot fail. The only
irreversible facts in the game are deaths and the calendar.

*Without it:* there is no dark for your work to go into.

### 4.4 The economy

Gold flows in from shelf sales, counter sales, commissions, and bounty payouts. It flows out through
rent on a ten-day cadence, guild dues on a seven-day one, materials, and the Foundry.

Three venues are live, each with its own ore ladder and its own banded routing — heroes go where
their power fits. A rival shop holds market share and restocks against you.

**Faction standing** sits underneath the ore trade. Buying a faction's ore raises your standing with
them; standing decays toward neutral every morning if you stop. In return their ore comes cheaper —
up to a ten percent discount, never a surcharge. It shows as a chip on the HUD, it is priced into
the night's ore offers with the faction named, and crossing a favour threshold is news the town
mentions.

*Without it:* nothing you make has a cost, and nothing you earn has a use.

### 4.5 Commissions and bounties

The two channels where the town asks *you* for something.

A commission is a named hero's specific request — slot, minimum quality, five-day window, premium
on delivery. Weapons, shields, armour, consumables and trinkets can all be asked for; trinkets only
by heroes who have been around long enough to want one.

A bounty is the inverse, and it is the game's only lever aimed at *where* heroes go. It runs in four
moves:

1. **You escrow.** Gold against a target floor — say 60 on floor 3. It leaves your purse when you
   post it, not when someone takes it.
2. **They weigh it.** On the next expedition tick every eligible hero judges the offer for
   themselves: greed against reward, against their own level and the floor's depth. The judgment is
   legible rather than hidden — each evaluation emits its reasoning.
3. **One of them commits.** The first hero to accept takes their whole party to that floor,
   overriding the depth the party would otherwise have chosen.
4. **It pays, or it doesn't.** They clear the floor and the taker collects. Or they die there. Or
   nobody wants it, the offer expires, and your gold comes back.

Your money is at risk and the decision is never yours. That is why this does not break
influence-never-orders: you can aim an offer, and any hero can refuse it.

*Without it:* the relationship is one-directional and the town never initiates.

### 4.6 Attribution and legends

The counterfactual engine of link 4, and the readers that carry its output: the night's ledger, the
ticker, gossip, the item's own provenance, the legends wall, and the chronicle.

*Without it:* the game is a shop sim. This is the system nothing else in the genre has.

### 4.7 Drama and tone

A director paces incidents. Gossip voices yesterday's real events — every line traces to something
that actually happened. A three-act arc turns on measured thresholds rather than a calendar.

The tone register is fixed and enforced in content: warm, dry, never grim, never cute. Deaths are
reported with dignity and without melodrama.

*Without it:* the same events happen and none of them land.

### 4.8 The advisor and legibility

A quiet objective tracker that answers "what could I usefully do right now" without ever telling you
what to do. It mirrors the same legality rules the kernel enforces, so it never suggests something
the game would refuse.

*Without it:* the first three days are opaque and the five-slot budget feels arbitrary.

### 4.9 Progression

Talents unlock along per-profession trees; they cost prerequisites, not points. Professions can be
changed mid-campaign — one or two at a time — so a shop can follow the town's drift. The forge's
own tier progression is the Foundry's spine.

*Without it:* day thirty plays exactly like day five.

### 4.10 Persistence

Full save and load with an end-of-day autosave and an honest Continue. A campaign survives closing
the window.

Determinism underpins all of it: the same seed and the same actions reproduce the same world,
byte for byte. This is what makes the counterfactual proof in link 4 trustworthy rather than
decorative — a golden-replay test enforces it, and breaking it fails the build.

---

## 5. Why it is built this way

**The sim is pure and the client is an adapter.** All game rules live in a .NET core with zero
engine references. The Godot layer renders state and submits actions; it decides nothing. This is
what makes the game testable at all, and it is why the proof chain can be trusted.

**No wall clock in the rules.** No decision the player makes is timed. The forge minigame has
rhythm, but the *decisions* — what to make, what to price, whether to reach into the dark — are
untimed on principle. The vigil stop waits indefinitely.

**Influence over command, everywhere.** Any mechanic that would let the player order a hero has been
refused, repeatedly and on purpose: outcome wagers, watch-to-buff, gate blessings, mid-delve
intervention verbs. The bounty is the single exception, and it survives because the hero still
decides.

**Every verb must change an outcome.** A control that occupies the hands without touching a hero's
fate is theatre, and the project cuts it. This is why the middle of the day stages stakes rather
than offering fake agency.

**Show only what the sim has decided.** Surfaces never display a number the rules have not produced,
and never hide one they have. The middle of the day is undrawn until its tick, so it shows stakes,
not events.

---

## 6. Inspirations

What was borrowed, and what was deliberately refused.

| Source | Taken | Refused |
|---|---|---|
| **Weapon Shop de Omasse** | The whole premise — you are the smith, the heroes adventure without you, and you follow their fate through a live feed. The conducted quest span, the departure manifest and the ticker are that feed. | Its rhythm-game-only craft and its purely comic register. |
| **Recettear / Moonlighter** | Face-to-face selling with the customer initiating, haggling as a real negotiation, and a shop that must clear its rent. The tavern's two handshake acts sit squarely in this lineage. | Dungeon-diving yourself. The player never leaves town. |
| **Majesty** | Heroes as autonomous agents who cannot be commanded, and the bounty as the one purchasable order. The reward-flag mechanic is borrowed close to verbatim. | Its RTS scale and its god-view framing. |
| **Against the Storm** | A pressure heartbeat that gives a settled economy a pulse — rent and guild assessments on their own cadences, now narrated as they land. | Roguelite runs and settlement resets. |
| **Erenshor** | Simulated players who feel like people: gossip generated from real events, veterans who get picky about quality, willingness bands built on memory. | Its MMO framing. Rivalry between heroes and typed causes of death are described in its design and absent here. |
| **Stardew Valley** | The daily loop, the top-down pixel register, the warmth, and the sense that a small town is worth knowing. | Its real-time clock. Nothing here is timed. |
| **Potion Craft / Dave the Diver** | Craft as a physical act with a skill expression, and profession minigames that feel different from one another. | Their pace. Two of the four crafts here are deliberately clockless. |
| **Football Manager, autobattlers, Blaseball** | The insight that watching a resolved simulation can carry real drama if it is revealed as highlights with stakes attached. The conductor and the beat-led night reveal are exactly this. | Competitive framing and leaderboards. |
| **Hades, Vagrant Story** | Weapons that accumulate history and identity — an item with a record is a character. | Their combat entirely. |
| **Crusader Kings, RimWorld** | Traits that give characters teeth rather than flavour text. Here traits drive shopping behaviour and bounty appetite; the raid resolver itself reads none of them. | Their complexity and their systems-first opacity. |
| **Graveyard Keeper** | An unglamorous profession taken seriously as a subject. | Its cynicism. |
| **Spiritfarer** | The emotional register: warmth about loss, and a game where saying goodbye is content rather than failure. | Its management-sim body entirely. |

---

## 7. What the game does not have

Stated as description. These are properties of the game as it stands, not gaps awaiting work.

- **The campaign's climax is a threshold and its ending is a chronicle.** When a hero reaches floor
  5 the act turns; five days later the game reads back everything your mark touched. There is no
  staged final scene — the tally is the ending.
- **The camp simulates nothing while a party sleeps.** It is a decision, not a place. Its entire
  weight is the one question and the three verbs.
- **The deep floors show the show, not the wager.** There is no slate down there restating what is
  at stake.
- **Three venues are live.** A fourth exists in the code, unrouted and unillustrated, and does not
  appear in the game.
- **The heroes' autonomy is five rules over true memories.** There is no goal system and no inner
  life. Their friendships and grudges are told in gossip; they are never acted on in the mine.
- **No wound outlives the night, no hero ever quits, and the town cannot fail.** The only
  irreversible facts are deaths and the calendar.
- **Demand leans toward the blacksmith.** Heroes ask for gear, consumables and trinkets, but nothing
  in the world specifically *needs* what only a tanner or an engineer can make.
- **Nothing is spoken aloud.** Four written narrator voices carry the prose and a director paces
  them, but the game is read, not heard. There is no narrator in the ear.
- **Most of what the town knows arrives as text on a board.** Two surfaces deliver information
  through a person — the customer at the counter states their want before you present anything, and
  the tavern patron carries their business face to face. Everything else is posted: the muster
  forecast, the commission board, the bounty board, the night's ledger.
- **Professions are picked, not accumulated.** A shop runs one or two at a time and may swap between
  them mid-campaign at the bell. There is no ladder that opens all four over a campaign, and no
  discipline beyond the four.
- **A run gets one camp, however deep it is going.** The checkpoint fires once, whether the party is
  bound for floor 2 or floor 5 — so the game's one reach-into-the-dark moment happens exactly as
  often on a shallow run as on the run where everything is at stake.
- **Talents are free.** They cost prerequisites, not points; there is no skill-point economy.
- **Provisioning saves the one and endangers the many.** A topped-up party dares one floor deeper,
  and the deep floors are where heroes die. Both effects are measured and real. The game states the
  tension and does not resolve it.

---

## 8. Glossary

| Term | Meaning |
|---|---|
| **Bell** | The player-rung transition that ends an act of the day. Two per day: send-off and lanterns. |
| **Slot** | One of five daily action allowances. Real work spends one; shelf work is free. |
| **The mark** | `MakersMark` — the stamp on everything you make. What the entire proof chain keys on. |
| **Beat** | An attribution event: proof that a specific item changed a specific outcome. |
| **Delve** | One expedition's descent. |
| **Vigil** | The camp stop — the untimed pause where a parked party waits on your decision. |
| **Park** | What a party does when it clears the checkpoint cleanly: it camps rather than pushing on. |
| **The Foundry** | The forge's endgame counter — tiers, coal and flux, masterwork attempts, legendary commissions. |
| **Standing / favour** | Your reputation with an ore faction. Rises when you buy their ore, decays toward neutral, and discounts their prices. |
| **The atomic pass** | The invisible morning shopping sweep where heroes buy from the shelf without a counter session. |
| **Commission** | A named hero's specific request, with a deadline and a premium. |
| **Bounty** | Escrowed gold against a target floor — the one lever aimed at where heroes go, and still an offer they can refuse. |
| **Heirloom** | An item reforged from a dead hero's recorded gear, carrying their lineage forward. |
| **Signed work** | An item whose maker chose to name it. |
| **Golden replay** | The determinism test: same seed and actions reproduce the same world byte for byte. |

---

*Mechanism-level ground truth — the complete action inventory, the phase machine, the expedition's
exact resolution order, and the numbers behind every system — is in `MAKERS-MARK.md` Appendix A,
derived from source and authoritative wherever this document and it disagree.*
