# State of the game — 2026-07-29

*Assessment written after a day of audits (art, animation, 3D residue, passive/ambient/misc), ten merged PRs, and six iterations of a five-run automated playtest against the shipped client. Facts below were read out of `origin/main` @ `d4b577f` or measured in play, not recalled.*

Gates at time of writing: sim **1428/1428**, engine **492/492**, build clean.

---

## The finding

**This project does not have a missing-systems problem. It has a reachability problem.**

Maker's Mark builds systems to real depth and then wires them to the player late, partially, or not at all. That pattern held in every direction the audits looked, and it is the single most useful lens for deciding what to do next — because it means the highest-value work is mostly *connecting* rather than *building*.

The evidence, four independent instances:

| Built | Reachable by a player? |
|---|---|
| **6 hero classes** (`ClassRegistry.All`) | **3.** `RecruitPool` is `[vanguard, striker, mystic]`. Sentinel/Skirmisher/Occultist are registered, defined, art-complete — and can never be drawn. |
| **4 venues** (mine, gloomwood, sunken-crypt, emberfall) | **2.** `LiveRotation` is `[mine, gloomwood]`. |
| **4 professions**, 39 recipes | **All craftable — but 3 of 4 have no demand.** Measured: 27 commissions across 5 campaigns were Weapon 11 / Shield 11 / Armor 5 / **Consumable 0 / Trinket 0**. |
| **`SaveCodec`** — full JSON codec, referenced by 44 test files | **No.** Zero callers in `godot/scripts/`. Nothing survives closing the window. |

Before today the same pattern also covered: ten sim event types computed and then dropped by the ticker's allow-list, the hero needs/boycott model computing player-warning flags nothing read, per-hero relationship edges with no consumer, and `CampaignEnded` — whose own contract says it carries tallies "so a credits scroll can render straight off this one event" — having no reader at all. Those four are now fixed (#277, #278). The four in the table are not.

## Why the class and venue gates are harder than they look

This is the part worth internalising before planning around it: **they are not flag flips.**

`ClassRegistry.RecruitPool`'s own doc explains why — recruit draws reproduce an older numeric role draw byte-for-byte, so the array's order *is* the determinism contract. Reordering or extending it breaks every golden replay. Same shape of problem for the venue rotation, which is why draft PR **#242** ("flip Sunken Crypt + Emberfall + 3 hero classes live") is parked on *"router tuning needed"* rather than merged.

So unlocking half the content requires a deliberate determinism re-baseline, not a one-line change. That cost is real and should be budgeted, not discovered.

## The demand map is the most consequential open problem

Three of four professions produce into slots the game never asks for. That is not a tuning nit — it is the reason two professions read as decorative.

The independently-authored `2026-07-27-five-pillars-design-synthesis.md` (five separate deep-dives, recovered from an uncommitted worktree on 2026-07-29 — see `docs/plans/README.md`) reached this conclusion from the code and named it: *"two professions have a craft and two have only a customer."* The playtest measured it the same day without reference to that document. Two independent methods, same answer.

Its sharpest edge: this landed **immediately after** U3b (#275) gave tanning and engineering full interactive craft overlays with talent remaps. Those two professions now have the newest, most tactile craft surfaces in the game and nothing to sell into. The build order was inverted — supply was deepened before demand existed.

## What is genuinely strong

Worth stating, because the list above is all deficits:

- **The sim is disciplined.** 24 modules, zero Godot references, determinism gated by golden replay, a 100-day balance sim in CI. 1428 tests. The purity rule has held.
- **The world is alive, measurably.** Frame-to-frame pixel deltas: town 1.5% at rest, 2.1% walking, raid 9.5%, forge 3.0%. All procedural — no `AnimationPlayer`, no `Tween`, no sprite sheets anywhere, by deliberate decision. Buildings, trees, lamps, smoke, fireflies and a day-phase tint all move (#280).
- **All four crafts are interactive and complete end-to-end**, verified in the shipped client: forge (heat curve + tempo), alchemy (reagent pour), tanning (40-cell scrape), engineering (socket seating + crank finale).
- **Narrative infrastructure is real.** Attribution beats, gossip, memorials, signed works, a 3-act arc with an ending 5 days past climax, ~1260 authored flavour lines.

## Ranked, if I were choosing

1. **Wire save/load.** A game whose premise is legends accumulating across a long campaign, where nothing survives a restart, has a hole under its thesis. The codec exists and is tested; this is client wiring plus autosave-semantics decisions.
2. **Rule on the demand map** — then fix it before building more supply. Everything else in the profession lane is downstream.
3. **Decide the #242 re-baseline.** Half the content is finished and unreachable; either pay the determinism cost or explicitly shelve it, but stop carrying it as a draft.
4. **The human feel-test** (`play.ps1`). Still never done. Every question that matters now — do four crafts feel like four skills, can you name three heroes by personality, does a legend read as a story — is unanswerable by a test suite, and the last week of work was specifically about feel.
5. **Audio.** Zero `.ogg`/`.wav`/`.mp3` files exist; two procedurally synthesized tones on the forge panel are the entire soundscape. A silent game reads as a prototype no matter how good the pixels are. This is a new asset track, not a fix.

## Two process notes worth keeping

**Automated playtest findings are usually the driver, not the game.** Five for five today: "forge grades low" (driver ignored the target heat curve), "tanning gate broken" (driver picked a tier-3 recipe), "economy broken" (driver never stocked the shelf), "calendar frozen" (driver held Morning open), "shapeX=0" (driver counted strike calls, not effects). One of these had already been recorded as a real game finding by an earlier session and carried forward as fact. Read the sim-side scorer before filing.

**Verify the tree before deleting anything.** The worktree cleanup that closed today's task list was one command away from destroying six authored design documents that existed nowhere in git — including the synthesis this assessment leans on twice.
