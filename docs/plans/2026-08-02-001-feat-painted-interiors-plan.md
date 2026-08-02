---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
type: feat
title: Painted interiors — E at the Forge puts you INSIDE the forge
date: 2026-08-02
origin: owner escalation 2026-08-01 ("we want to see the INSIDES of the building… E on the forge only opens the shitty side menu"); chosen form = "the full painted room yes with clickable stations etc"
roadmap: docs/plans/2026-07-28-003-roadmap-post-skeleton.md
---

# Painted interiors — E at the Forge puts you INSIDE the forge

## Goal Capsule

The owner has asked for this repeatedly and the game has never delivered it: **pressing E at the
Forge opens a side drawer over the town instead of putting the player inside the building.** Given
the explicit choice between a static painted scene whose stations are buttons and a full painted
room you move around in, he chose the room: *"the full painted room yes with clickable stations
etc."* So the target is — E (or a click) at the Forge places the player-smith **inside a painted
pixel-art smithy**: anvil, furnace + bellows, quench trough, material shelf, finished-goods rack —
each a physical thing you walk up to and interact with, which opens the corresponding action. The
drawer never again appears as the *answer* to "I walked to my forge and pressed E."

**The single most important finding of this plan's investigation — why this keeps not happening:**

- `godot/scripts/town/InteriorStage.cs` (507 lines) is a complete per-venue staged-interior
  framework — backdrop art id, title, declarative hotspot table for all four venues, camera
  push-in, Esc handling, an embedded shop choreography — and it is **dead code**. It was the U22
  interact route in the pre-pivot world; the 2.5D pivot's MainUi cutover rewired
  `OnTownBuildingClicked` **straight to `OpenPanel`** (the drawer) and nothing was ever pointed
  back (`MainUi.cs:1697-1700`: *"nothing currently opens it… but it stays wired"*). The owner has
  been pressing E into that gap for a week of sessions. Its own doc even names its hotspot table
  as *"the carry-forward asset if walkable interiors happen later."* Later is now.
- Even revived, InteriorStage is the form the owner **declined** — a static backdrop with a button
  column on the right. It gets harvested (its declarative venue/hotspot vocabulary), not revived.
- The one committed interior painting, `godot/assets/art/shop-interior.png`, is SDXL-era art for
  that dead overlay; nothing draws it, and it does not match the pixel town. The three other
  interior ids (`forge-interior`/`tavern-interior`/`gate-interior`, `art/specs/town/
  TownSpecsExtra.cs`) are **specs only — no image was ever generated.**

**Goal:** one excellent walkable Forge interior in the live game, built entirely from the town2d
idioms the game already plays by (same avatar, same WASD + click-to-move, same E-interact and
highlight, same 16px-tile pixel discipline), with stations that open the existing, tested action
surfaces. Market/tavern/minegate rooms follow the same data-driven pattern in a later slice — this
plan ships the Forge and the framework, not four mediocre rooms.

Secondary (raised in the same playtest, kept deliberately short here): the new pixel building
**exteriors** read worse to the owner than the previous art ("only the interiors were supposed to
change"), and hero/NPC sprites "still look like booty." Both become rendered side-by-side
candidates he picks from — never our taste.

---

## Standing constraints (restated because every executing agent must obey them)

1. **Engine tests are SERIALIZED.** Two concurrent gdUnit runs silently truncate to a fake green.
   Parallel implementation is fine; engine-suite *runs and merges* are one at a time. CI floor is
   `ENGINE_MIN_PASSED=300` — "Failed: 0" alone is not a pass.
2. **Deny-list — never edit:** `Game.sln`, `godot/project.godot`, `.github/`,
   `sim/GameSim/Contracts/`, `CLAUDE.md`, `global.json`, `Directory.Build.props`, `.godot-version`.
3. **Sim purity (KTD2):** interiors are presentation only. Zero `sim/GameSim/` edits in this plan.
   Golden replay untouched.
4. **SubViewport hazard:** pumping frames while ANY SubViewport renders hangs gdUnit headless.
   This plan adds **no new SubViewport** (KTD-1) and every new engine test that pumps frames sets
   `Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled` first — the
   established pattern (`CameraFollowTests.cs:46` et al.).
5. **One unit = one branch (`feat/uN-slug`) = one small PR.** File lists below are disjoint except
   where a serial order is stated.
6. **Visible-difference receipts are mandatory** (`tools/receipt.ps1`, merged #323): every unit
   states what a human sees that proves it landed, produced from a rebuilt, sha-stamped frame with
   a measured nonzero diff. Receipts need a desktop GPU session — local gate, not CI.
7. **Every new art id joins the asset-resolution census** (#324) so "committed but invisible"
   fails a test, and every placeholder is loud (magenta border + id text), never a quiet box.

---

## Requirements

- **R1 — Room, not drawer.** E (or click) at the Forge places the player inside a painted forge
  interior in the same world viewport. The drawer is never the direct response to a Forge
  interact again.
- **R2 — Walkable clickable stations.** Stations are physical objects in the room — walk to them,
  they highlight, the HUD prompt reads "E · Anvil", and E/click opens the corresponding action.
- **R3 — The same game inside.** Same `PlayerController2D` avatar and movement (WASD + click-to-
  move), same interact/highlight idiom (`WorldInput2D`/`Building2D`), same pixel discipline (16px
  tiles, Nearest filter, integer canvas shrink), same Y-sorting. No new movement or input concepts.
- **R4 — Obvious, total exit.** A door zone at the room's threshold and Esc both return the player
  to the building's outside door anchor. No way to get stuck inside.
- **R5 — No verb regression.** Every gameplay verb reachable today (craft + all four minigames,
  talents, vendor buys, shelf/prices, bounties, tavern, bestiary, legends) stays reachable. The
  tested drawer panels remain the verb surfaces; the room replaces the *route to* them, not them.
- **R6 — Presentation only.** Zero sim reads beyond what the panels already do; zero sim edits.
- **R7 — Receipts + census.** Each unit lands with a receipt.ps1 receipt; each new art id gets a
  census row; placeholders are loud from day one so framework and art land independently.
- **R8 — Owner-picked visuals.** Building exteriors and hero-sprite direction are settled by his
  pick among rendered side-by-side candidates, not by agent taste.
- **R9 — Forge first.** Slice 1 = the Forge room only. Market/tavern/minegate keep today's drawer
  behavior until their rooms land (slice 2, out of this plan's scope). The venue gate is data
  presence, so slice 2 is table rows + art, not new code paths.

---

## Key Technical Decisions

- **KTD-1 — The interior is an "island" inside the existing world, not an overlay and not a new
  viewport.** The forge room is built at a far-offset region of Town2D's existing `World`
  (e.g. +2048px in X — off every town camera frame), inside the same 640×360 SubViewport. Entering
  = teleport the player body to the room's door tile and clamp the camera to the room rect;
  exiting = teleport back to the building's `DoorAnchorGlobal` and unclamp. Why: zero
  hide/show/reparent juggling (town keeps simulating off-frame exactly as it does off-camera
  today), zero new SubViewports (constraint 4), the camera/follow/shrink machinery is reused
  untouched, and the player never changes parent (Y-sort and feet-line invariants hold). This is
  the Stardew map-swap concept expressed in the cheapest way this codebase allows.
- **KTD-2 — Stations are `Building2D` instances driven by the existing interact loop.** A station
  is furniture-scale architecture: sprite + click/proximity `Area2D` + blocking footprint +
  nametag + highlight + `Picked` event — exactly what `Building2D.Configure` already builds.
  `WorldInput2D.Configure` is simply re-pointed at the room's station list on entry (and back to
  the town's buildings on exit). No new interaction component is invented (R3).
- **KTD-3 — The station table is declarative, harvested from `InteriorStage.Venues`.** A new
  `InteriorLayout2D` table maps venue key → room spec (shell art id, room rect, door tile,
  station list: id, label, hover line, tile position, sprite id, **action route**). Action strings
  keep the exact vocabulary MainUi already routes ("Forge"/"Shop"/"Tavern"/"Bounties"/"Bestiary"/
  "Legends" → `OpenPanel` or the Bestiary/Legends modals, `MainUi.OnInteriorHotspotActivated`'s
  switch). A new venue's room is a table row + art, never a new code path — InteriorStage's KTD10
  promise, finally kept by the walkable form.
- **KTD-4 — Drawer panels are kept and opened *by* stations; they are not replaced.** Recommend
  **wrap, not replace, not delete**: the panels carry every tested gameplay verb (ForgePanel alone
  hosts craft cards, talents, vendor rows, and all four minigame overlays). Deleting or re-hosting
  them is a large regression risk with no player-visible payoff; leaving them as the *direct*
  answer to E is the exact complaint. So: E enters the room; the anvil opens the Forge panel; the
  drawer slides over the room, which now reads as the world behind it. Slice-2+ may later shrink
  panels into station-scoped surfaces (e.g. vendor rows living at the material shelf) — deliberate
  follow-up, not this plan.
- **KTD-5 — Room art is authored-pixel composition, not one big SDXL painting.** One generated
  room **shell** PNG (floor + walls + baseboard shadow, ~24×14 tiles = 384×224px) plus **six
  separate station sprites**, all produced by `art/pipeline` scripts in the `gen-market.py` idiom:
  colours sampled from committed `town2d-*` siblings, byte-reproducible, `--check` drift guard, no
  GPU needed. Why not SDXL: the pipeline's own recorded finding ("at 20x36 a diffusion render
  downscales to mush") plus the pixel-town style target; the SDXL `shop-interior.png` is
  overlay-era art and stays unused. Stations must be separate sprites anyway for highlight,
  Y-sort, and per-station clicking. No normal maps — the `town2d-*` pixel set carries none.
  **Honest cost:** this is the largest authored-art task the pipeline has attempted (gen-market
  was one 64×64 building; this is a 384×224 shell + six 16–40px props). Budget 1–2 focused
  agent-days iterating against receipt renders, and expect the owner's taste pass to demand a
  revision round.
- **KTD-6 — Loud placeholders decouple framework from art.** U1 ships the walkable room with
  magenta-bordered, id-labelled placeholder stations (the #324 idiom), so the route, movement,
  stations, and verbs are provable on screen before any painting exists — and the painting (U2)
  then has its own unmistakable before/after receipt.

---

## Implementation Units

### U1 — Walk inside the Forge: interior island + stations (loud placeholders)

**Goal:** E or click at the Forge puts the player inside a walkable forge room with six
highlightable, clickable stations that open the existing surfaces; door zone and Esc walk you back
out. The room renders with loud placeholder art — the framework is complete and provable before
the painting lands.

**Files:**
- Create: `godot/scripts/town2d/InteriorRoom2D.cs` — builds one room at its island offset: shell
  sprite (non-sorted, below the Y-sort layer), perimeter `StaticBody2D` walls from the room rect,
  stations as `Building2D`s in a `YSortEnabled` container, an exit `Area2D` on the door tile, and
  a `RoomRect` the camera clamps to.
- Create: `godot/scripts/town2d/InteriorLayout2D.cs` — the declarative table (KTD-3). Slice 1
  content: the `forge` row only. Forge stations (sprite ids pinned here so U2 can run in
  parallel): `town2d-station-anvil` (Anvil → "Forge"), `town2d-station-furnace` (Furnace →
  "Forge"), `town2d-station-bellows` (flavor, see U3), `town2d-station-quench` (flavor, see U3),
  `town2d-station-shelf` (Material Shelf → "Forge"), `town2d-station-rack` (Finished Goods →
  "Shop"); shell id `town2d-forge-interior-shell`; door at the room's bottom edge.
- Modify: `godot/scripts/town2d/Town2D.cs` — `EnterInterior(venueKey)` / `ExitInterior()`:
  teleport the player, clamp/unclamp `FollowPlayer` to the room rect, expose
  `InteriorActive`/`InteriorVenueKey` (test + MainUi visibility), suppress `FocusOnMineGate`
  beats while inside.
- Modify: `godot/scripts/town2d/WorldInput2D.cs` — accept a re-`Configure` between the town's
  building list and a room's station list (it already takes `IReadOnlyList<Building2D>`; this may
  be zero-diff — verify).
- Modify: `godot/scripts/MainUi.cs` — `OnTownBuildingClicked("forge")` routes to
  `Town.EnterInterior("forge")` **when an InteriorLayout2D row exists** (data-gated per R9; other
  venues unchanged); station `Picked` actions route through the existing
  `OnInteriorHotspotActivated` vocabulary; exit/Esc calls `ExitInterior` (Esc priority: open
  drawer/modal closes first, then the room — extend the existing #320 Escape-topmost ladder);
  `QuickTravel("Forge")` enters the room (content parity: quick-travel opens nothing a walked
  arrival could not).
- Modify: `godot/tools/shot_harness.gd` — `SHOT_STATE=Forge` now captures the room (it drives the
  production `OnTownBuildingClicked` path, so this is automatic — verify settle frames); add
  `ForgePanel` state via the `OpenPanel` bridge so drawer receipts remain possible.
- Create: `godot/tests/InteriorRoomTests.cs` (room geometry: shell mounted, walls block, stations
  spawn from the table in declared order, exit zone present, loud placeholders on missing ids) and
  `godot/tests/InteriorEntryExitTests.cs` (E at forge enters; player at door tile; camera clamped;
  station E opens the routed surface; exit zone and Esc restore the outside door position;
  focus-beat suppressed while inside; other venues still open their drawers).
- Modify (enumerate by grep at execution — any test/tool asserting forge-interact → ForgePanel):
  expected `godot/tests/PlayerCanInteractTests.cs`, `godot/tests/TutorialFlowTests.cs` (tutorial
  copy for "open the forge" now completes at the anvil), `godot/scripts/tools/FullPlaytest.cs`
  (its building-click sweep must walk the room for forge, and its dead-panel rule must not read
  the room as a panel).

**Approach:** island placement per KTD-1 — no town hide/show, no reparenting, no new viewport.
Player body, camera, input, Y-sort all reused as-is. Room interior tint: `DayPhaseTint`'s dusk
modulate covers the whole `World`, so the room would read purple; slice-1 answer is a warm
constant modulate on the room subtree (self-lit smithy), flagged for the owner's eye at the
receipt. All engine tests pumping frames disable viewport rendering first (constraint 4).

**Test scenarios:** entry/exit round-trip preserves the outside position; station press opens
ForgePanel and the drawer renders *over* the room; Esc with drawer open closes drawer first, room
second; market/tavern/minegate behavior unchanged; census unaffected (placeholder ids join the
known-pending allowlist until U2).

**Verification:** full engine suite green locally (serialized), CI green on the PR.

**Visible-difference receipt:** before/after pair via `tools/receipt.ps1 -State Forge`: before =
drawer over the town; after = inside a room with six labelled placeholder stations and the player
standing at the door. Diff will be near-total. Second receipt: exit door → back outside the forge.

---

### U2 — The painted smithy: shell + six station sprites

**Goal:** the placeholder room becomes the painted forge interior — ember-lit stone-and-timber
smithy in the town's own pixel style.

**Files:**
- Create: `art/pipeline/gen-forge-interior.py` — authors `town2d-forge-interior-shell.png`
  (384×224: plank/flagstone floor, stone wall band, baseboard shadow, door gap) **and** the six
  station sprites (`town2d-station-anvil` ~24×20, `-furnace` ~32×40 with ember glow,
  `-bellows` ~20×14, `-quench` ~24×14, `-shelf` ~28×32, `-rack` ~28×32), palette sampled verbatim
  from committed `town2d-*` PNGs (the gen-market idiom: VOID outline, IRON body planes, BONE
  linework, EMBER lit openings, single ARCANE/COOLANT accents, 6–8 colours per sprite), with
  `--check` drift-guard mode.
- Create: the seven PNGs + `.import` files under `godot/assets/art/`.
- Modify: `godot/tests/AssetResolutionCensusTests.cs` — the seven ids move from the known-pending
  allowlist to enforced rows (red-then-green shown in the PR body).
- Modify: `art/pipeline/gen-manifest.ps1` / manifest rows if the manifest enumerates files (match
  whatever #324 established — verify at execution).

**Approach:** iterate against `receipt.ps1 -State Forge` renders, not against the PNG in an image
viewer — judge the art at the integer shrink the player actually sees. Composition brief for the
shell: readable open floor (stations must not crowd the walkable space), warm palette against the
town's dusk exterior, door gap centred on the room's bottom edge where U1 pinned it.

**Test scenarios:** `--check` green on a fresh render; census green with the seven rows enforced;
station sprite sizes match the footprints `InteriorLayout2D` declares (a size mismatch shifts
collision — assert texture dims in the census or a small art-contract test).

**Verification:** engine suite green (serialized); `--check` wired into the same local guard run
the other gen scripts use.

**Visible-difference receipt:** before (placeholder room, U1's receipt) / after (painted room)
pair with diff % — this is the plan's marquee image. **The owner's taste pass is the real gate**
(R8): expect a revision round; his notes become follow-up rows, not silent re-interpretation.

---

### U3 — Stations open the right verbs (and say so honestly)

**Goal:** stations differentiate: the anvil is crafting, the material shelf lands you on
materials, the finished-goods rack is stock-and-prices, and the two flavor stations (bellows,
quench) are honestly flavor — a hover line and a one-line response, never a dead click that
pretends to be a verb.

**Files:**
- Modify: `godot/scripts/panels/ForgePanel.cs` — `FocusSection(string)` scrolls/flashes a named
  section ("materials" → the vendor/material rows, "craft" → recipe cards); reuses the existing
  section containers (`_vendorRows`/`_recipeRows`), no verb changes.
- Modify: `godot/scripts/MainUi.cs` — station action routing gains an optional focus argument
  (e.g. action "Forge#materials" or a second field on the station record — implementer's pick,
  keep it in `InteriorLayout2D`'s data, not code).
- Modify: `godot/scripts/town2d/InteriorLayout2D.cs` — final action mapping: Anvil →
  Forge#craft; Furnace → Forge#craft (the minigames live behind the craft flow); Material Shelf →
  Forge#materials; Finished Goods Rack → "Shop"; Bellows/Quench → flavor (hover description + a
  `PlaytestLog`-noted one-liner via the narrator toast idiom, and their nametags render dimmer so
  they never promise a verb they don't have).
- Tests: extend `godot/tests/InteriorEntryExitTests.cs` (shelf press → ForgePanel open AND
  scrolled to vendor rows; rack press → ShopPanel; flavor press → no panel, one toast line);
  `godot/tests/ForgeCraftTests.cs` sibling coverage for `FocusSection` (no layout collapse).

**Approach:** strictly after U1 merges (shares `MainUi.cs`/`InteriorLayout2D.cs`). The rack →
"Shop" mapping is deliberate — stocking finished goods IS the shop verb — but it crosses venue
identity, so it is Open Question 3 for the owner; shipping default is as stated.

**Test scenarios:** as above, plus: a station whose action string is unknown fails loudly at build
of the room (table validation test), never a silent dead click — the game's whole recurring
failure class.

**Verification:** engine suite green (serialized).

**Visible-difference receipt:** receipt of the shelf-press frame — ForgePanel open over the room,
visibly scrolled to the material rows (different frame from the anvil-press receipt); one flavor
click showing the toast line.

---

### U4 — Retire InteriorStage (the dead 507-line overlay)

**Goal:** the superseded static-overlay framework leaves the codebase — no orphans — once its
declarative table has been harvested into `InteriorLayout2D` (U1) and the venues it nominally
served are confirmed unaffected (they route via drawer, and did before).

**Files:**
- Delete: `godot/scripts/town/InteriorStage.cs`, `godot/tests/InteriorStageTests.cs`.
- Modify: `godot/scripts/MainUi.cs` — remove the `Interior` mount, `OnInteriorHotspotActivated`
  stays (U1/U3 route stations through it) but its InteriorStage references go; sweep the Escape
  ladder and `SoundTheTick`'s `Interior.IsOpen` gate to the new `Town.InteriorActive`.
- Sweep by grep: remaining references (`TutorialFlowTests.cs`, `ShopStageTests.cs` — `ShopStage`
  the class is kept: the slice-2 market room wants its shelf choreography; only its InteriorStage
  host dies), `godot/tests/InteriorRoom3DTests.cs` if it survives only to reference this.

**Approach:** strictly after U3 (same `MainUi.cs`). Pure deletion + reference sweep; behavior of
every live route already covered by U1/U3 tests.

**Test scenarios:** suite passes with the two files gone; test count delta stated in the PR body
(the CI floor guards against silent suite shrinkage — state the expected new count).

**Verification:** engine suite green (serialized); CI floor still cleared.

**Visible-difference receipt (honest exception, per the make-it-visible plan's R8):** nothing on
screen changes — the receipt is a **0%-diff pair** proving the deletion changed no pixels
(receipt.ps1's diff mode inverted: identical frames are the *success* here, stated explicitly),
plus the test-count delta.

---

### U5 — Building exteriors: his pick among rendered options

**Goal:** resolve "the new pixel buildings look WORSE than the previous art" with evidence he can
judge in seconds — not by reverting on reflex and not by defending the pixel set on taste.

**Files (candidates phase — nothing ships until he picks):**
- Candidate stills to `runs/receipts/candidates/exteriors/` via receipt.ps1, identical state/seed:
  (a) current `town2d-*` pixel set; (b) the pre-#316 SDXL set (ids `forge`/`market`/`tavern`/
  `mine-gate`/`noticeboard` — still committed, so this candidate is free); (c) one graded pixel
  variant (brightness/contrast lift via the pipeline's existing quantize/dim stages) if cheap.
- Landing (after his pick): either `godot/scripts/town2d/TownLayout2D.cs` `Venues` sprite-id
  column (a five-line table swap — that is all #316 changed) or re-authored PNGs via the pipeline.

**Approach:** note on the sitting page (or its successor) that mixing sets is also legal (e.g.
SDXL buildings + pixel props). His verdict lands as its own one-commit PR with a before/after
receipt. **Needs his pick — no agent lands a default.**

**Test scenarios:** none beyond build for the candidates; the landing PR re-runs the census
(whichever set ships must fully resolve — the #316 lesson).

**Verification:** engine suite green on the landing PR (serialized).

**Visible-difference receipt:** the candidate contact-sheet row itself; then the landing
before/after town pair with diff % (for reference, #316 measured 47%).

---

### U6 — Hero/NPC sprite quality: candidate directions + his pick

**Goal:** turn "still look like booty" into a decidable choice. Kept brief: this unit manufactures
evidence and a verdict; a fresh gen batch, if chosen, is its own follow-up unit.

**Files (candidates phase):**
- Candidate stills to `runs/receipts/candidates/sprites/`: (a) current `town2d-hero-*`/townsfolk
  sprites at play scale; (b) an authored contrast/outline pass (pipeline: re-quantize with crisper
  1px VOID outline + stronger palette separation); (c) a mocked higher-detail direction (2×
  resolution redraw of ONE hero as proof-of-direction — remember `CharacterSpriteScale` is 0.5,
  so source art has 2× headroom the screen never shows; a 2× redraw shown at scale 0.25 keeps the
  same screen size with double the detail — flag that this changes the art contract for the whole
  cast before committing to it).
- Landing: per his pick — (b) is a pipeline re-run over the existing set; (c) spawns a sized
  follow-up gen unit (new T1 batch + census rows), not an extension of this one.

**Approach / Test scenarios / Verification:** candidates carry no behavior; the landing path
inherits the census + engine-suite gates. **Needs his pick.**

**Visible-difference receipt:** the zoomed side-by-side row; landing PR carries before/after
receipts of the town with the cast visible.

---

## Dependencies & parallelism

- **U1 is the spine — first.** One agent; it is the only unit allowed to make the routing change.
- **U2 runs in parallel with U1** (sprite ids and room/door geometry are pinned above, in this
  plan), but **merges after U1** — until U1's code references the ids, the census would call the
  PNGs orphans. Art iteration against receipts can start immediately.
- **U3 strictly after U1** (shares `MainUi.cs` + `InteriorLayout2D.cs`). **U4 strictly after U3**
  (same reason). U1 → U3 → U4 is the plan's serial spine.
- **U5 and U6 are independent** of everything above and of each other — candidates can be
  produced any time; their landing PRs wait on the owner's picks.
- Every unit that touches `godot/` runs the **full** engine suite locally (a filtered run cannot
  see other suites vanish) and merges serially per constraint 1. No unit runs balance gates
  (no sim edits anywhere in this plan).

## Verification contract

| Unit | Fast lane | Engine suite (serialized) | Receipt |
|---|---|---|---|
| U1 | — | required | drawer-over-town vs inside-the-room pair; exit round-trip |
| U2 | — | required (census red→green shown) | placeholder room vs painted room pair; owner taste pass |
| U3 | — | required | shelf-press frame scrolled to materials; flavor toast frame |
| U4 | — | required (test-count delta stated) | 0%-diff pair (deletion changed nothing) — honest exception |
| U5 | — | required (landing PR only) | candidate row; landing before/after town pair |
| U6 | — | required (landing PR only) | candidate row; landing before/after cast pair |

## Scope boundaries

- **Slice 1 is the Forge only (R9).** Market, tavern, and mine-gate rooms are slice 2: each is an
  `InteriorLayout2D` row + a shell/stations art unit on the U2 pattern. The market room inherits
  `ShopStage`'s customer choreography (the class survives U4 for exactly this). Not planned here.
- **Not re-hosting or splitting the drawer panels** (KTD-4). Station-scoped sub-surfaces (vendor
  rows living at the shelf, minigames launching directly from the anvil with a preselected recipe)
  are named follow-ups after the owner has played the room.
- **No sim changes, no Contracts edits, no balance/golden impact.**
- **No new music/SFX** — interior ambience (bellows loop, ember crackle) is a natural follow-up
  once `AudioDirector.SetScene` gets a "forge" scene; out of scope here.
- **Not the forge-minigame defect** — a separate diagnosis is running; this plan wires the station
  that opens the craft flow and assumes the minigame inside it gets fixed on its own track.
- **No CLAUDE.md / .github edits** (deny-listed). The stale plan-of-record pointer stays flagged
  in `docs/plans/README.md`.
- **Plans-index rule:** the commit landing this document must add its row to
  `docs/plans/README.md` (LIVE table) per that file's rule 2.

## Open questions

1. **Interior lighting:** warm self-lit constant (U1's default) vs keeping the dusk tint indoors
   vs a phase-aware warm ramp. Cheap either way — his eye at the U1/U2 receipts decides.
2. **Furnace station route:** slice 1 sends Furnace to the craft flow alongside the anvil. If the
   active-craft heat state ever surfaces as its own readout, the furnace is its natural home —
   name it as a follow-up when he asks, don't build it speculatively.
3. **Finished-goods rack → Shop panel** crosses venue identity (stocking happens at the market in
   the fiction). Shipping default stands (it is the real verb); flag for his verdict in the room.
4. **Exterior verdict option (c)** (graded pixel variant): produce only if the pipeline re-run is
   genuinely one-constant cheap; two honest candidates beat three where one is half-effort.
5. **Tutorial copy:** "walk to the forge" steps now complete inside the room (anvil press). U1
   adjusts assertions; whether the tutorial should *teach* the room (one extra line) is a
   one-liner his playtest will answer.
6. **Slice-2 order** (market vs tavern first) — owner's call after he has lived in the forge room.

## Definition of done

1. The owner walks to his forge, presses E, and is **inside a painted smithy** — walks to the
   anvil, presses E, and is crafting. He exits by the door and is back on the street where he
   left. No drawer ever answered the E press directly.
2. Every station either opens a real verb or honestly reads as flavor — zero dead clicks.
3. Every unit's PR carries its receipt (numbers + paths + in-frame sha); the room art ids are
   census-enforced; placeholders, where they ever render, are loud.
4. InteriorStage is gone; its venue/hotspot vocabulary lives on in `InteriorLayout2D`; the test
   count delta was stated, not discovered.
5. The exterior-buildings and hero-sprite complaints each have a recorded owner verdict with a
   landed (or explicitly deferred) consequence — not an open "someone should tune this."
6. Slice 2 (three more rooms) is a set of table rows + art units on a proven pattern.
