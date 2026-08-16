---
type: playtest-register
title: "Owner playtest register — 2026-08-16"
date: 2026-08-16
status: open register — items close only by merged PR, and only the owner closes by re-testing
---

# Owner playtest register — 2026-08-16

## Why this file exists

The owner's standing complaint, verbatim:

> everyplay test i am giving heavy feedback but you jump on ONE thing then skip
> the rest. we need to HEAVY expand the amount of time spend thinking ->
> research -> planning -> working -> testing -> researching
>
> seriously, its annoying how little you need doing. the tutorial revamp is
> CLEARLY nowhere NEAR the scope i keep fucking telling you to do (full guided
> tutorial) and other feedback is getting randomly forgotten.

The forgetting has a mechanical cause and this file is the fix. The previous
session captured all of this feedback in an **in-session todo list**, which died
with the session. The handoff that survived said "already captured as tasks
#141–#162, do not re-triage" — but no such tasks existed anywhere on disk. The
list was reconstructed from the owner's raw notes and screenshots.

**Rule: owner feedback lands in this file, on `main`, in the same turn it is
received. Never in a session-local todo list.** A session-local list is not a
record; it is a promise that expires.

## How an item closes

An item closes when a merged PR on `main` addresses it AND the owner confirms it
in a later playtest. Three items on this register were previously marked fixed
and are reopened because the owner's screenshots disprove the fix (#155, #151/#152,
#153). Self-certification does not close an item.

Do not delete a closed item — strike it and keep the line. The register is the
audit trail that the forgetting stopped.

---

## The register

Numbering continues the handoff's `#141–#162` scheme. The exact original mapping
did not survive; five anchors named in the handoff are preserved (#151/#152 music,
#153 bellows, #154 bells, #155 anvil, #159 ledger). Items #163–#167 are additions:
#166/#167 are bugs found in the owner's screenshots that he did not report.

### Visuals — the town

| # | Item | Owner's words / evidence | Status |
|---|---|---|---|
| 141 | Character legs clip with the grass and read as sunk into it | "The character's legs 'clip' with the grass and look odd" | open |
| 142 | Hero-to-building scale is wrong. **Make the buildings bigger — do not shrink the characters** (explicit owner direction) | "heroes are too big compared to the buildings; make the buildings bigger" | open |
| 143 | The bounties building's art is poor | "bounties building isn't great, improve" | open |
| 144 | Too many lampposts | "too many lampposts" | open |
| 145 | Scatter props clip into each other and into actors — named example a carriage/cart | "The random 'extra' things are clipping like this carrage(?) thing" | open |
| 150 | Heroes and NPCs have no walking animation. Only townsfolk do | "WHERE ARE THE HERO/NPC walking animations? only the townsfolk have them" | open |
| 163 | The world is too small and must be expanded | "need to expand the size of the world" | open |

### Visuals — interiors and menus

| # | Item | Owner's words / evidence | Status |
|---|---|---|---|
| 146 | Building interiors need to look better | "Not sure how many fucking times we have to talk about the insides of buildings. 1) need them to look better" | open — **repeatedly raised** |
| 147 | Interior interactables must have distinct meaning and use. Named absurdity: the **furnace is where you buy resources** | "2) the items/things we click on need distincy meaning and use. why the fuck is the furance where we BUY resources" | open — **repeatedly raised** |
| 148 | The watch-heroes scene must reach cutscene quality (in-engine rendered) | "the watch heroes visuals still fucking suck dude. this should be similar to a cutscene in quality (obv ingame rendered etc)" | open |
| 149 | The legacy "jank" crafting menu is still reachable | "i somehow opened the legacy jank menu for crafting" — screenshot `jank_menu.jpg`: FORGE drawer with raw `(recipe default)` and three `(none)` dropdowns, a RECIPES list, and MORNING VENDOR (copper 4g / iron 7g / steel 10g, Buy buttons, qty spinners) grafted into the same scroll container | open |

### Audio

| # | Item | Owner's words / evidence | Status |
|---|---|---|---|
| 151 | Dawn bed is grainy. Measured: peak **+1.63 dBFS**, **11,133 clipped samples** — literal digital clipping, not a container problem | "Dawn music has some rough graininess" | **reopened** — MP3→OGG "fix" did not address it |
| 152 | Night bed is grainy static. Measured: hiss-heavy master, **34.7 % of energy in the high band** on a quiet ambient bed | "Night music is fucked, grainy static" | **reopened** — same failed fix |
| 153 | Bellows sound too loud | "bellows sound is too loud" | **reopened** — lowering Normalise 0.15→0.12 did not fix it |
| 154 | Bells/chimes too loud | "bells/chimes are too loud" | open |
| 165 | Structural: everything mixes on the default Master bus with **no limiter** (`AudioDirector` sets no `.Bus` anywhere) while Night stacks bed + narrator + death toll + UI cues. Likely the common cause of the whole too-loud family | derived, not owner-reported | open — fix as **one measured pass**, not four constant nudges |

### Gameplay — the forge

| # | Item | Owner's words / evidence | Status |
|---|---|---|---|
| 155 | **The anvil minigame cannot be completed.** Screenshot status line: `Strike 24/21 — Heat 1000 — pumping — the billet is yielding, keep going`. Strikes are past target, heat is pinned at max, it never ends | "ANVIL minigame is STILL fucking not working dude - HOW THE FUCK is it possible we cannot complte despite me telling you and you somehow playtesting???" | **reopened — highest priority in the project** |
| 156 | The anvil/forge drawer opens scrolled to the bottom; should open at the top | "Clicking on the anvil auto has the scroll bar at the bottom, should start at the top" | open |
| 157 | Too many recipes/options are unlocked from the start; they belong behind progression | "Why are there so many crafting recipes/things unlocked - should be part of the progression systems" | open |

### Gameplay — the tutorial

| # | Item | Owner's words / evidence | Status |
|---|---|---|---|
| 158 | **Full guided tutorial rework.** The single loudest, most-repeated ask. The recent revamp is "nowhere NEAR the scope" | "dude just FUCKING ACTUALLY LISTEN, we need a FULL rework on the tutorital" | open — **the headline item** |
| 161 | Tutorial ordering: should the player sell before the first send-off? Owner is asking, not asserting — he floats the alternative that sending unequipped is the lesson | "With the tutorial - shouldn't we sell before sending them the first time??? or are you sending without so we can learn that they need the things we craft???" | open — **owner decision needed** |
| 162 | Tutorial step 6 is weak | "Tutortial 6 sucks" | open |
| 160 | "Tomorrow at the Counter" is good but needs integrating: taught in the tutorial, then the player's own reference tool, and **openable while crafting** | "Tomorrow at the counter is good but needs integration into the game better - part of the tutortial then become the player's job to reference/utilize. Should be able to open this WHILE doing the crafting" | open — **the one screen he praised** |

### Gameplay — the night

| # | Item | Owner's words / evidence | Status |
|---|---|---|---|
| 159 | Evening ledger is tiny and unreadable, needs expanding — **and the narration is supposed to be used here for effect** | "Evening ledger sucks - needs expanded to be actually readable (its tiny) and the narriation is SUPPOSED to be used here for affect" | open |
| 166 | BUG: the ledger says `Brunhilde came back from floor 0`. **Floor 0 does not exist** | screenshot `Screenshot 2026-08-16 151211.jpg` | open — found, not reported |
| 167 | BUG: the same card reads `8g` in the sentence and `11g` on the reward chip | same screenshot | open — found, not reported |

### Substrate

| # | Item | Owner's words / evidence | Status |
|---|---|---|---|
| 164 | Logging must improve every pass. **Every action and the REASON behind it** must be logged so it can be checked later | "Make sure we are improving our logging each time. Ideally all actions and REASON behind them is logged so you can check later" | open — standing instruction, not a one-off |

---

## Owner process instructions attached to this playtest

These govern how the register is worked, and are not themselves register items:

1. Everything above goes into a global to-do list **first**. This file is that list.
2. Then run thinking → research → planning **multiple times**, with opus (or fable
   when requested) subagents, producing one overall **outline** that expands over
   multiple rounds into **multiple detailed plans**.
3. Only then fan out sonnet/haiku to implement.
4. Auto merge / deploy. Playtest as needed.
5. Caveman ultra, always.
6. Screenshots in `docs/playtests/BrianPlaytest/` are to be ingested and then
   **deleted** — individually, never with a recursive delete.

## Prior owner rulings that constrain this work

Do not re-litigate these:

- The first death belongs **inside** the tutorial (shipped).
- Venues are a **forward** ladder — veterans advance, never return.
- Heirloom reforge grows; it never rewards a death.
- Build the Legend Engine.
- Tavern scenery returns as separate props.
- Keep the well that matches the new town (done).
- **Buildings get bigger; characters do not get smaller.**
