---
type: playtest-register
title: "Owner playtest register — 2026-08-16"
date: 2026-08-16
status: this file records what was asked, never what is done — see "Where status lives"
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

## Where status lives

**This file carries no status column, and never will again.** It records what was
asked and the evidence for it. Whether an item has shipped lives in `git log` and
in the PR list — CLAUDE.md rule 8, because git outranks every doc.

This is not a stylistic preference. It was earned on 2026-08-17: this register
still said `open` against three items that had merged to `main` the day before,
a worker lane was dispatched against those rows, and it spent its budget proving
the work already existed. **A stale doc is not clutter; it is an instruction the
next session obeys.** A status column on a file that is edited less often than the
code it describes will always eventually lie, so the column is gone rather than
corrected.

To find whether an item shipped, ask git:

```bash
git log origin/main --oneline --grep="register #147"
gh pr list --repo BrianOver/makers-mark --state merged --search "register #147" --limit 20
```

Every PR body carries a `Serves:` line and register-item PRs name the item number,
so one grep answers it against the only record that cannot drift.

## How an item closes

An item closes when a merged PR on `main` addresses it **and the owner confirms it
in a later playtest**. Self-certification does not close an item — this register has
already seen items marked fixed that the owner's next screenshots disproved
(#151/#152 music, #153 bellows, #155 anvil). A merged PR is evidence that work
happened, not evidence that the complaint is answered.

Do not delete an item once addressed. The register is the audit trail that the
forgetting stopped, and an item the owner rejects a second time needs its history
visible.

---

## The register

Numbering continues the handoff's `#141–#162` scheme. The exact original mapping
did not survive; five anchors named in the handoff are preserved (#151/#152 music,
#153 bellows, #154 bells, #155 anvil, #159 ledger). Items #163–#167 are additions:
#166/#167 are bugs found in the owner's screenshots that he did not report.

### Visuals — the town

| # | Item | Owner's words / evidence |
|---|---|---|
| 141 | Character legs clip with the grass and read as sunk into it | "The character's legs 'clip' with the grass and look odd" |
| 142 | Hero-to-building scale is wrong. **Make the buildings bigger — do not shrink the characters** (explicit owner direction) | "heroes are too big compared to the buildings; make the buildings bigger" |
| 143 | The bounties building's art is poor | "bounties building isn't great, improve" |
| 144 | Too many lampposts | "too many lampposts" |
| 145 | Scatter props clip into each other and into actors — named example a carriage/cart | "The random 'extra' things are clipping like this carrage(?) thing" |
| 150 | Heroes and NPCs have no walking animation. Only townsfolk do | "WHERE ARE THE HERO/NPC walking animations? only the townsfolk have them" |
| 163 | The world is too small and must be expanded | "need to expand the size of the world" |

### Visuals — interiors and menus

| # | Item | Owner's words / evidence |
|---|---|---|
| 146 | Building interiors need to look better — **repeatedly raised** | "Not sure how many fucking times we have to talk about the insides of buildings. 1) need them to look better" |
| 147 | Interior interactables must have distinct meaning and use — **repeatedly raised**. Named absurdity: the **furnace is where you buy resources** | "2) the items/things we click on need distincy meaning and use. why the fuck is the furance where we BUY resources" |
| 148 | The watch-heroes scene must reach cutscene quality (in-engine rendered) | "the watch heroes visuals still fucking suck dude. this should be similar to a cutscene in quality (obv ingame rendered etc)" |
| 149 | The legacy "jank" crafting menu is still reachable | "i somehow opened the legacy jank menu for crafting" — screenshot `jank_menu.jpg`: FORGE drawer with raw `(recipe default)` and three `(none)` dropdowns, a RECIPES list, and MORNING VENDOR (copper 4g / iron 7g / steel 10g, Buy buttons, qty spinners) grafted into the same scroll container |

### Audio

| # | Item | Owner's words / evidence |
|---|---|---|
| 151 | Dawn bed is grainy. Measured: peak **+1.63 dBFS**, **11,133 clipped samples** — literal digital clipping, not a container problem. An MP3→OGG re-container did not address it | "Dawn music has some rough graininess" |
| 152 | Night bed is grainy static. Measured: hiss-heavy master, **34.7 % of energy in the high band** on a quiet ambient bed. Same failed re-container fix | "Night music is fucked, grainy static" |
| 153 | Bellows sound too loud. Lowering Normalise 0.15→0.12 did not fix it | "bellows sound is too loud" |
| 154 | Bells/chimes too loud | "bells/chimes are too loud" |
| 165 | Structural: everything mixes on the default Master bus with **no limiter** (`AudioDirector` sets no `.Bus` anywhere) while Night stacks bed + narrator + death toll + UI cues. Likely the common cause of the whole too-loud family. Fix as **one measured pass**, not four constant nudges | derived, not owner-reported |

### Gameplay — the forge

| # | Item | Owner's words / evidence |
|---|---|---|
| 155 | **The anvil minigame cannot be completed.** Screenshot status line: `Strike 24/21 — Heat 1000 — pumping — the billet is yielding, keep going`. Strikes are past target, heat is pinned at max, it never ends | "ANVIL minigame is STILL fucking not working dude - HOW THE FUCK is it possible we cannot complte despite me telling you and you somehow playtesting???" |
| 156 | The anvil/forge drawer opens scrolled to the bottom; should open at the top | "Clicking on the anvil auto has the scroll bar at the bottom, should start at the top" |
| 157 | Too many recipes/options are unlocked from the start; they belong behind progression | "Why are there so many crafting recipes/things unlocked - should be part of the progression systems" |

### Gameplay — the tutorial

| # | Item | Owner's words / evidence |
|---|---|---|
| 158 | **Full guided tutorial rework** — the single loudest, most-repeated ask. The revamp before this register is "nowhere NEAR the scope" | "dude just FUCKING ACTUALLY LISTEN, we need a FULL rework on the tutorital" |
| 161 | Tutorial ordering: should the player sell before the first send-off? The owner is asking, not asserting — he floats the alternative that sending unequipped is the lesson. **Owner decision needed** | "With the tutorial - shouldn't we sell before sending them the first time??? or are you sending without so we can learn that they need the things we craft???" |
| 162 | Tutorial step 6 is weak | "Tutortial 6 sucks" |
| 160 | "Tomorrow at the Counter" — **the one screen he praised** — needs integrating: taught in the tutorial, then the player's own reference tool, and **openable while crafting** | "Tomorrow at the counter is good but needs integration into the game better - part of the tutortial then become the player's job to reference/utilize. Should be able to open this WHILE doing the crafting" |

### Gameplay — the night

| # | Item | Owner's words / evidence |
|---|---|---|
| 159 | Evening ledger is tiny and unreadable, needs expanding — **and the narration is supposed to be used here for effect** | "Evening ledger sucks - needs expanded to be actually readable (its tiny) and the narriation is SUPPOSED to be used here for affect" |
| 166 | BUG (found by us, not reported): the ledger says `Brunhilde came back from floor 0`. **Floor 0 does not exist** | screenshot `Screenshot 2026-08-16 151211.jpg` |
| 167 | BUG (found by us, not reported): the same card reads `8g` in the sentence and `11g` on the reward chip | same screenshot |

### Substrate

| # | Item | Owner's words / evidence |
|---|---|---|
| 164 | Logging must improve every pass. **Every action and the REASON behind it** must be logged so it can be checked later. A standing instruction, not a one-off | "Make sure we are improving our logging each time. Ideally all actions and REASON behind them is logged so you can check later" |

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

---

## Found while fixing the above — not owner-reported

These were discovered by the work on this register rather than reported by the owner.
They go here for the same reason everything else does: a finding that lives only in a
session transcript is a finding that is already lost.

| # | Item | Evidence |
|---|---|---|
| 168 | The AI pilot's Act 2 "wait" turn may be inert for the same reason Act 1's was. `tools/agent-playtest/pilot.ps1:761-765` sends `forge_strike` as a deliberate no-op to wait out a timer; `forge_strike` and `plunge` **share physical Space** (`MinigameInput.cs:45,47`), so the trick that worked in Act 1 by accident may be pressing a live key in Act 2. Act 1's version was corrected during #155 by switching to `confirm` (Enter), which shares no key with either. | found while fixing #155 |
| 169 | `HeroReturnCeremonyTests.StagedReturn_AlreadyPastTheShowFloor_EmergesWithNoExtraDelay` waits on a **frame budget** (`HumanPlayer.WaitUntil(condition, maxFrames)`) for an animation timed in **seconds**. CI runs faster per frame with rendering disabled, so a slow runner exhausts the budget before the wall-clock stagger completes and the test fails without a regression. Observed failing on a docs-only PR, passing elsewhere on the same base. Waits must be on the condition, never on a frame count. | found while diagnosing the merge queue |
| 170 | **One of the six dilemmas has no mechanism behind it.** Dilemma #5 is "buy the ore or buy the goodwill," and `THE-GAME.md` describes it as "you pay the hero directly and may pay well." `OreMarketHandlers` does not do that: the hero always receives the base ask, and when the player pays MORE the surplus is a **faction sink**, not a payment to the hero. The surcharge branch is commented as unreachable/dormant in the current discount-only core. So paying generously buys the player nothing from the hero, and the goodwill half of the dilemma does not exist. Found while building Wave D's teaching for it — and correctly **not** taught, because a lesson explaining a mechanic that does not exist makes the game lie to the player. Needs a real unit: either implement the goodwill payment, or amend `THE-GAME.md` and the six-dilemma list to match what the game is. This is a design decision, so it is **the owner's call** which way it resolves. | traced through `OreMarketHandlers` while building §11.14.4 Wave D |
| 171 | `MineWatch.BarkFor` discarded `AttributionBeatEvent.Detail` — the sim's own already-composed sentence, e.g. "Emberbite landed the killing blow on the cave-rat" — and rebuilt a phrase naming the hero and the action but never the ITEM. On the one screen where the player watches the fight, the proof could not name what earned it. Recorded here because it is the same one-reader-field shape as `KillingItem` and `Hero.Pack`, and because a bark that outranks it (a new depth record) can still win the slot. | found while building §11.14.4 Wave D |

## Three structural facts this session established

None is a defect; all three change how the next session should plan.

**Subagents cannot see the client.** The engine suite serializes globally, so workers are
banned from running it. Any *new* engine test a worker writes is therefore first executed
by CI, not by its author. Three PRs in this program failed on exactly that and all three
were real. This is the expected tail on any UI wave, not a sign of a bad worker — but it
means a wave's schedule must budget for one orchestrator-side repair pass per UI unit,
and worker prompts should say so plainly so the report distinguishes "verified" from
"could not verify here."

**The merge queue does not drain itself.** Auto-merge armed on every PR is not sufficient:
the ruleset requires branch-up-to-date and GitHub does not auto-update, so a batch of
green PRs sits BLOCKED indefinitely until someone pushes branch updates. On a night with
ten PRs this is a material throughput tax, and it is what the CI sharding change (owner-
authored, `.github/` is deny-listed) would relieve.

**A worker lane inherits whatever the doc says, including the parts that are wrong.**
The status column removed above sent a lane after three items that had already merged.
The lane did the right thing — it checked git before building, found the work, and
reported instead of reimplementing — but the budget was spent. Prompts should name the
symbol the work builds on and require the worker to grep for it first, which is already
CLAUDE.md rule 9; the deeper fix is not to let the doc make the claim at all.
