# Interaction design research — making every craft, trade and watch surface physical

Research pass by a fable-model design agent, 2026-07-28, commissioned to answer one question:
**how should the player physically interact with every craft, activity and trade surface?** — with
findings grounded in published games and design writing rather than taste.

Execution schedule derived from this doc:
`docs/plans/2026-07-28-002-feat-interactive-professions-and-trade-plan.md`.

Verification note from the agent: sources marked ✓ were fetched and read during the session; the
remainder are canonical references cited from knowledge with their standard URLs (the session's
web-fetch budget capped out mid-research).

---

## A. Principles

**A1. An interaction is a skill atom or it's a chore.** Daniel Cook's model: a satisfying interaction
is a loop of *action → simulation → feedback → updated mental model*, and the pleasure is the
mental-model update — getting better. Once a skill is mastered with no downstream application,
repetition stops rewarding and becomes grind
([Cook, "The Chemistry of Game Design"](https://www.gamedeveloper.com/design/the-chemistry-of-game-design) ✓).
Corollary: every minigame needs a skill floor the player visibly climbs. This is precisely the failure
IGN identified in Recettear — "once the player has learned the habits of various characters, the price
haggling degenerates into a thoughtless, mechanical exercise"; only external market fluctuation kept
it alive ([Recettear](https://en.wikipedia.org/wiki/Recettear:_An_Item_Shop%27s_Tale) ✓).

**A2. Physicality = rules embodied in an object you manipulate, not a widget you operate.** Potion
Craft is the standard — grinding with a pestle, dragging a ladle, pumping a bellows; critics credited
the *tactile* detail ("the bubbling of the brew as you heat it up, the crunch of a root as you crush
it") for capturing invention in a way more "realistic" crafting systems don't
([Potion Craft](https://en.wikipedia.org/wiki/Potion_Craft) ✓). Papers, Please proves the same for
bureaucracy: dragging documents, stamping, cross-referencing — the deliberately clunky physical desk
is what makes rule-checking a job ([Papers, Please](https://en.wikipedia.org/wiki/Papers,_Please) ✓).
The test: **could you screenshot the surface and see the verb?** A cauldron with a tipping bottle
passes; a SpinBox labelled "price" fails.

**A3. Feedback carries most of the feel.** Hit-stop, screenshake, pitch-shifted audio, particles
scaled to input quality ([Jonasson & Purho, "Juice it or lose it"](https://www.youtube.com/watch?v=Fy0aCDmgnxg);
[Nijman, "The Art of Screenshake"](https://www.youtube.com/watch?v=AJdEqssNZ-U); Swink, *Game Feel*,
[game-feel.com](http://www.game-feel.com/)). Crucially: **feedback must grade the input**, because that
difference is the teaching signal in A1's loop.

**A4. Timing is a seasoning, never a wall.** The Game Accessibility Guidelines are explicit — avoid
repeated inputs / QTEs, and "do not make precise timing essential to gameplay – offer alternatives,
actions that can be carried out while paused, or a skip mechanism"
([full list](https://gameaccessibilityguidelines.com/full-list/) ✓). Celeste's Assist Mode is the
no-stigma model ([Celeste](https://en.wikipedia.org/wiki/Celeste_(video_game)) ✓); Dredge goes further
— clicking reels faster but waiting passively still lands the catch, so engagement with timing is
optional ([Dredge](https://en.wikipedia.org/wiki/Dredge_(video_game)) ✓).

**A5. Vary the skill tested, not just the skin.** Dredge shipped fishing variants after playtesters
found one minigame tedious, and reviews still split on repetitiveness ✓. Dave the Diver works because
its halves test genuinely different skills, with critics finding the many minigames "come together
unexpectedly well" ([Dave the Diver](https://en.wikipedia.org/wiki/Dave_the_Diver) ✓). Cult of the Lamb
alternates a fast half and a calm half ([Cult of the Lamb](https://en.wikipedia.org/wiki/Cult_of_the_Lamb) ✓).
Graveyard Keeper is the cautionary tale — many stations, all "walk here, press E, wait" — 69 Metacritic,
28% recommendation ([Graveyard Keeper](https://en.wikipedia.org/wiki/Graveyard_Keeper) ✓).

**A6. When NOT to add a minigame.** Only if ALL hold: (1) it's identity work in the player's fantasy;
(2) there's real skill and a real quality delta; (3) frequency × duration stays cozy (a few times a
day, ≤30s) — anything done ~10×/day gets *one meaningful choice* instead; (4) a no-timing path exists;
(5) it tests a skill no other surface tests. Disco Elysium's passive/active check split is the useful
frame ([Disco Elysium](https://en.wikipedia.org/wiki/Disco_Elysium)): auto-craft is the passive check,
the minigame is the active one you choose when you want to beat the baseline.

**A7. Loops need arcs above them.** What keeps loops alive over 100 days is the arc riding on them —
the legend a Masterwork writes, the hero who dies wearing your work
([Cook, "Loops and Arcs"](https://lostgarden.com/2012/04/30/loops-and-arcs/) ✓). Recettear's saving
grace was market news; ours is heroes, bounties and demand changing what's worth making.

---

## B. Per-surface designs (summary)

| Surface | Physical input | Skill | Reference |
|---|---|---|---|
| **Blacksmith forge** | aimed click on the billet; bellows as drag *strokes*; drag-to-quench; hit-stop + pitch on feedback | rhythm + resource pursuit + knowing when to stop | Stardew fishing pursuit ([wiki](https://stardewvalleywiki.com/Fishing)), Vlambeer juice |
| **Alchemy brew** | drag a bottle over the cauldron and tip; recipe notes fade after first brew (book re-shows, free) | recall + sequencing; later dosage | Potion Craft ✓ |
| **Tanning** (new active) | drag scrape strokes over a hide grid; 1–2 passes per cell, over-scraping wears through; flaw patches; drag hide off to finish | motor coverage + restraint, no clock | Papers, Please spatial scan ✓ |
| **Engineering** (new active) | drag-and-drop parts into schematic sockets; near-duplicate parts; order bonus; wind-the-crank finale | spatial reasoning + identification + planning | Opus Magnum ([wiki](https://en.wikipedia.org/wiki/Opus_Magnum)) |
| **Counter / haggle** | drag item onto the mat to present; stack coins to counter-offer; handshake to accept; customer posture/expression = mood, tapping foot = patience; walk-aways spoken | reading opponent state + risk appetite | Recettear ✓, Papers Please desk ✓, Moonlighter |
| **Restock / pricing** | drag goods onto shelf slots; click price tags to reprice | judgment (what to shelve, at what price) | Moonlighter |
| **Bounty posting** | click a stratum on a mine cross-section; coin-stack the reward; drag the poster onto the board | small decision, weighty commitment | Papers Please stamp ✓ |
| **Hero roster** | pin up to 3 (no command verbs — autonomy premise) | attention | Cult of the Lamb roster calm ✓ |
| **Mine watch** | one lens: click a hero to focus the feed | attention | Dave the Diver calm/active alternation ✓ |

---

## C. Input layer rule

> **Every gesture recognizer terminates in exactly one public seam method with integer/enum args.
> `_GuiInput`/`_Process` are thin translators; tests call the seam methods directly and never need a
> mouse.**

- **Aimed click:** hit-test in `_GuiInput`, then call the existing seam (`ForgeStrike()`); keyboard
  key calls the same seam unaimed.
- **Drag as quantized strokes:** accumulate `InputEventMouseMotion.Relative`, and every N px emit one
  discrete integer event (`PumpStroke()`, `ScrapeCell(id)`). No input float reaches a scorer.
- **Hold-and-release:** `ButtonDown/Up` → `Start()/Stop()`, duration measured only by the accumulated
  `Advance(delta)` clock — replayable exactly.
- **Drag-and-drop:** Godot `_GetDragData`/`_DropData`, with the drop handler a one-liner into
  `Place(socketId, partId)`; keyboard cursor calls the identical method.
- **Coin stack:** a Control wrapping `int Value` with `AddCoins/RemoveCoins`; the sim never learns
  coins existed.

Determinism checklist for every new surface: accumulated `Advance(delta)` only; integer/per-mille
state; path variants from `StableHash(recipeId, day)`; FX from fixed tables; `Finished` fires exactly
once with one action; preview via the same pure sim scorer, read-only; no `SubViewport`.

---

## D. Variety and pacing

A day touches ~2 crafts, 1 counter session, 1 restock, occasional bounty, one watch. If all nine
surfaces demanded a performance, a day would be nine performances — Graveyard Keeper with extra steps.
Hence the binding tier split (full minigame / light interaction / plain list) reproduced in the plan.

Anti-fatigue mechanisms that must ship alongside the minigames:
1. **Auto-craft stays first-class** (the passive check; also the accessibility alternative).
2. **Muscle memory:** once a recipe scores ≥ Fine, its emitted input can be re-submitted for repeat
   crafts at a small flat grade discount — perform while learning, delegate once mastered. This is the
   direct antidote to Recettear's degeneration.
3. **Assist data everywhere:** every new scorer consumes `MinigameAssist` (wider bands, more
   forgiveness) — talents as earned accessibility, plus a global toggle mapping to the same numbers.
4. **Only the forge has a clock.** Every other surface is pausable by construction.

---

## E. Priority (impact ÷ effort)

1. Counter desk physicality — very high impact, sim rules already shipped — **presentation-only**
2. Forge feel pass (aim, strokes, drag-quench, hit-stop) — **presentation-only**
3. Alchemy phase 1 (drag-to-pour, fading notes) — **presentation-only**
4. Restock drag-and-drop + price tags — **presentation-only**
5. Engineering assembly bench — **sim seam** (new scorer + `ActiveCraft`)
6. Tanning scrape frame — **sim seam**
7. Muscle-memory batch craft — mostly orchestration
8. Bounty poster — **presentation-only**
9. Hero pins + watch lens + mark-glint staging — **presentation-only**
10. Alchemy phase 2 (grind axis) — **sim seam**, only after 5/6 prove the pattern

Flagged, unscheduled: shelf position mattering (would be a `StockAction` slot-index change) — decide
only if playtests ask for it.

---

## Sources

Fetched and verified in-session: [Cook — Chemistry of Game Design](https://www.gamedeveloper.com/design/the-chemistry-of-game-design) ·
[Cook — Loops and Arcs](https://lostgarden.com/2012/04/30/loops-and-arcs/) ·
[Game Accessibility Guidelines](https://gameaccessibilityguidelines.com/full-list/) ·
[Potion Craft](https://en.wikipedia.org/wiki/Potion_Craft) ·
[Recettear](https://en.wikipedia.org/wiki/Recettear:_An_Item_Shop%27s_Tale) ·
[Papers, Please](https://en.wikipedia.org/wiki/Papers,_Please) ·
[Dredge](https://en.wikipedia.org/wiki/Dredge_(video_game)) ·
[Dave the Diver](https://en.wikipedia.org/wiki/Dave_the_Diver) ·
[Cult of the Lamb](https://en.wikipedia.org/wiki/Cult_of_the_Lamb) ·
[Celeste](https://en.wikipedia.org/wiki/Celeste_(video_game)) ·
[Graveyard Keeper](https://en.wikipedia.org/wiki/Graveyard_Keeper).

Cited from knowledge (canonical URLs; fetch budget capped): [Stardew fishing](https://stardewvalleywiki.com/Fishing) ·
[Moonlighter](https://en.wikipedia.org/wiki/Moonlighter_(video_game)) ·
[Juice it or lose it](https://www.youtube.com/watch?v=Fy0aCDmgnxg) ·
[The Art of Screenshake](https://www.youtube.com/watch?v=AJdEqssNZ-U) ·
Swink, *Game Feel* ([game-feel.com](http://www.game-feel.com/)) ·
[Opus Magnum](https://en.wikipedia.org/wiki/Opus_Magnum) ·
[Disco Elysium](https://en.wikipedia.org/wiki/Disco_Elysium) ·
My Little Blacksmith Shop.
