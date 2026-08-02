---
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
execution: code
type: feat
title: World and interiors — every venue has an inside, the world matches your profession, night is night, and the watch shows the show
date: 2026-08-02
origin: owner playtest notes 2026-08-02 (fourth playtest; build = main incl. #335-#345, #348; #349 in flight)
roadmap: docs/plans/2026-07-28-003-roadmap-post-skeleton.md
---

# World and interiors — every venue has an inside, the world matches your profession, night is night, and the watch shows the show

## Goal Capsule

The owner's fourth playtest confirms the painted-interiors bet ("inside of the forge is a massive
improvement") and immediately asks for the rest of the world to catch up. His notes decompose into
six located defects, each verified against the code before this plan was written:

1. **Three venues still have no inside.** Shop, Tavern, and the Mine Gate answer E with a drawer.
   The forge room proved the pattern; `InteriorLayout2D.Rooms` was built so that a new venue is
   a table row + art, and it delivers on that promise — `MainUi.OnTownBuildingClicked` is already
   data-gated on row presence, and the census (`InteriorRoomSpriteIds_ResolveToCommittedArt`)
   already reads the table, so new rooms inherit enforcement with zero test edits.
2. **"All open the same menu" is half-fixed in flight.** PR #349 (open, not merged) lands honest
   station differentiation for the forge: anvil/furnace → craft focus, shelf → materials focus,
   rack → Shop, bellows/quench → flavor with dim nametags and hover lines, plus the
   never-a-dead-click table validation. His complaint predates that PR. What #349 does NOT solve:
   a station's purpose is legible only on approach (hover) or click — nothing tells you at a
   glance which objects carry verbs. This plan extends #349's discipline to the new rooms and adds
   sight-level legibility.
3. **The world ignores your profession.** Verified: all four professions (blacksmith, tanning,
   engineering, alchemy) craft through the same kernel action and `ForgePanel` already renders
   profession-correct recipes and minigames — but 9+ player-facing strings hardcode "Forge"
   (venue nametag, quick-travel, drawer id, tutorial `StepBuilding`, fallback copy "craft at the
   anvil"), and the only interior room is a smithy. An alchemist walks into a building called
   Forge, past an anvil, to brew potions. The fix is presentation-side: the workshop is one
   building whose name, stations, and dressing follow the professions you picked (KTD-3 says why
   shared-shell beats per-profession buildings).
4. **The watch surface hides its own show.** Verified: an animated, sprite-level delve renderer
   ALREADY EXISTS — `DelveStage` (749 lines: monster sprites, HP bars, hit-flash, knockback,
   death clouds, damage numbers, loot sparkle) hosted by `MineWatch` (lit SubViewport strip:
   backdrop, torch light, marching hero figures, monster silhouettes) — but it is mounted only
   inside `DepthsPanel`. The "👁 Watch" button and the PiP dock's "Mirror ⤢" both open
   `ScryingMirror`, which is 100% Labels and Buttons. The owner pressed Watch and got text
   because the animated surface is behind a different door. We re-host, not rebuild.
5. **Heroes visibly return right after being sent down.** Verified root cause: an unstaged
   (floor-1) expedition resolves synchronously inside the same `AdvancePhase` call
   (`ExpeditionSystem.cs:84-106`), and `Town2D.OnPhaseCompleted(Expedition)` calls
   `ReturnSurvivors()` immediately — so the bell press that "lowers them into the mine" is the
   same click that walks them home. The sim is honest; the presentation shows zero elapsed time
   below. Fix is show-timing, adapter-side only.
6. **"Night" is day.** Verified: the phase labelled "Night" is `DayPhase.Evening`, whose tint is
   (0.86, 0.80, 0.93) — near-white. Night→Dawn moves only 0.86→0.97. And the single
   `CanvasModulate` is the town's ONLY phase-driven visual: lamps glow at a fixed alpha all day
   (`AmbientLife2D` never reads `DayPhase`), no window light, no darkness. Both complaints
   ("night is day", "Night → Dawn no visual difference") fall out of two constants and one
   missing phase input.

Plus the standing item: **hero sprites, third round.** #341 landed six 26×44 hand-authored bodies
(13×22 on screen) and he still wants better. This plan judges the remaining dials honestly
(KTD-7) instead of commissioning a fourth same-size repaint.

**Goal:** he walks into every building in town and finds a room; the room's objects tell him
their purpose before he clicks; his workshop matches his craft; pressing Watch shows sprites
fighting, not paragraphs; heroes stay gone while the fiction says they're gone; night looks like
night; and the hero-sprite question gets a decidable set of candidates instead of another guess.

---

## Standing constraints (restated because every executing agent must obey them)

1. **Engine tests are SERIALIZED.** Two concurrent gdUnit runs silently truncate to a fake green.
   Implementing agents never run `dotnet test godot/tests`; the orchestrator runs the full suite
   once per branch, serially. CI floor is `ENGINE_MIN_PASSED=300` — "Failed: 0" alone is not a
   pass.
2. **Deny-list — never edit:** `Game.sln`, `godot/project.godot`, `.github/`,
   `sim/GameSim/Contracts/`, `CLAUDE.md`, `global.json`, `Directory.Build.props`,
   `.godot-version`.
3. **Sim purity (KTD2 of record):** everything in this plan is presentation. **Zero `sim/`
   edits, zero Contracts edits, zero golden/balance impact — by construction, in every unit.**
   If an implementing agent believes a unit needs a sim change, that is a CONTRACT-REQUEST
   escalation, not a commit.
4. **SubViewport hazard:** pumping frames while ANY SubViewport renders hangs gdUnit headless.
   Every new engine test that pumps frames disables viewport rendering first (the
   `CameraFollowTests.cs` pattern). U9 moves an existing SubViewport (`MineWatch`) — its tests
   inherit the discipline `MineWatch`'s existing tests already use. Tests wait on the CONDITION,
   never a frame count.
5. **Loud placeholders, never silent fallbacks.** Every new art id lands in
   `godot/tests/AssetResolutionCensusTests.cs` — via the table-driven
   `InteriorRoomSpriteIds_ResolveToCommittedArt` case where it applies, with the
   `KnownPendingIds` allowlist bridging framework-before-art, and red-then-green shown in the
   art PR body when ids leave the allowlist.
6. **Art is judged at play scale** via `tools/receipt.ps1` renders (rebuilt, sha-stamped,
   measured diff) — never in an image viewer. Nearest filter, no mipmaps: sub-1.0 scale is
   decimation. Palettes are sampled from committed sibling PNGs, never picked by eye.
7. **GPU hard limits** for any SDXL/ComfyUI work: ≥14 GB VRAM free per `nvidia-smi` (never the
   MCP stats snapshot), abort >14 GB used or >83 °C, one job at a time. **Every GPU-dependent
   item in this plan is deferrable without blocking anything** — the owner's own machine use can
   hold VRAM below the floor indefinitely (see U13 and Open Question 3).
8. **One unit = one branch (`feat/uN-slug`) = one small PR.** Conventional commits, no
   `git add .`. File lists below are disjoint except where a serial order is stated.
9. **Hard prerequisite: PR #349 merges first.** It rewrites `InteriorLayout2D.StationSpec`
   (nullable `Action`, `Focus`, `HoverLine`, `FlavorLine`), re-routes `MainUi` station handling,
   and retires `InteriorStage`. Every unit below is written against the post-#349 shape. If #349
   is rejected, this plan pauses at U1.

---

## Requirements (each traced to the owner's words)

- **R1 — Every venue has an inside.** *"shop needs an inside where the NPCs interact etc. Tavern
  and bounty/gate need an inside."* E at Shop, Tavern, or Mine Gate places the player in a
  walkable painted room in the forge room's exact island pattern. The drawer never again answers
  E at any venue directly.
- **R2 — Stations differ, and say so on sight.** *"items all open the same menu - whats the
  point of multiple things if they all do the same thing"* / *"make more clear what each
  item/interactable does."* Every station in every room opens a distinct surface (or is honest
  flavor per #349's grammar), and verb-bearing stations are visually distinguishable from flavor
  at a glance — before hover, before click.
- **R3 — The workshop follows the profession.** *"if you start with alchemist - shouldn't it be
  something other than the forge?"* / *"forge is still there despite being alchemist."* The
  workshop's nametag, quick-travel label, interior stations, and tutorial copy all reflect the
  selected profession(s). An alchemist never crafts at an anvil.
- **R4 — The watch shows sprites, not text.** *"Thw 'watch' / eyeball menu has no animations -
  all text WHERE ARE THE ANIMATIONS/VISUALS."* Opening the watch during a live expedition shows
  the animated delve (backdrop, hero figures, monster, HP, hits) with the text feed as support,
  not as the whole show.
- **R5 — The mirror dock reads.** *"the 'mirror' thing on the bottom doesn't appear correctly.
  too small and unsure what its even supposed to show."* The PiP dock states what it is and
  shows at-a-glance party state; its expand affordance names what it opens.
- **R6 — Gone means gone.** *"why did the heroes come back to the town visually?"* While the
  phase vocabulary says the party is below, no party member is visible in town; their return is
  a staged, legible beat, not an instant reappearance.
- **R7 — Night reads as night; dawn as dawn.** *"'night' phase is day"* / *"Night -> Dawn - no
  visual difference really."* The phase labelled Night is measurably dark, Dawn is measurably
  light, and at least one non-tint cue (lamps, windows) flips with the phase.
- **R8 — Hero visuals get a decidable next step.** *"still wanna improve the heroes models."*
  Rendered candidates along the dials that are actually still open (screen scale, motion,
  portraits) — his pick, never agent taste, and no same-size repaint (measured invisible at
  0.07% in #329).
- **R9 — Livelier town.** *"improve the general town - make more lively."* Shop customers
  visibly shop inside the market room; tavern patrons sit and emote inside the tavern; townsfolk
  run errands between buildings instead of drifting in place.
- **R10 — Presentation only.** Zero sim reads beyond what shipped surfaces already read; zero
  sim/Contracts edits; golden replay and balance gate untouched by construction.

---

## Key Technical Decisions

- **KTD-1 — New rooms are table rows on the proven island pattern; one framework unit, three art
  units.** `InteriorRoom2D` + `InteriorLayout2D` shipped exactly so a venue is data. U1 adds
  three `RoomSpec` rows (market at +2048 Y-offset lane spacing per row — distinct island offsets
  so no camera clamp can ever see two rooms), pins every sprite id, and ships loud placeholders;
  U2/U3/U4 paint one room each in the `gen-forge-interior.py` idiom (Python/PIL, palette sampled
  from committed siblings, `--check` drift guard, no GPU). Per-room art units because the forge
  set (1 shell + 6 sprites) was honestly logged as "the largest authored-art task the pipeline
  has attempted" — three rooms in one unit would be a guaranteed stall.
- **KTD-2 — The gatehouse is the mine-facing room; the noticeboard stays a board.** The owner
  said "bounty/gate need an inside." The mine gate gets a gatehouse interior hosting the three
  mine-facing verbs as stations: muster board → Depths, bounty ledger → Bounties, and an
  overlook window → the watch (U9's surface) — unifying "everything about the mine happens at
  the gate." The freestanding noticeboard keeps its drawer: a plank board has no inside, and
  pretending otherwise spends an art unit on a closet. Flagged as Open Question 1 because his
  phrasing ("bounty/gate") may bundle them differently than we read it.
- **KTD-3 — One workshop shell; stations, names, and dressing swap by profession. NOT four
  buildings.** Rationale, in order of force: (a) a player can hold TWO professions
  (`ProfessionHandlers.MaxSelected = 2`, second added mid-run) — two separate buildings per
  player breaks the town layout the moment a second craft is picked, while a shared room simply
  shows both station sets; (b) the venue key `"forge"` is load-bearing across `MainUi` routing,
  quick-travel, tutorial `StepBuilding`, and tests — swapping presentation over a stable key is
  a small diff, renaming the key is plumbing risk with zero player-visible payoff; (c) the sim
  routes nothing by workshop venue — a second building would be pure art surface; (d) art
  budget: three station sets (~4 sprites each, Python-authored) is provably affordable; three
  full building exteriors are SDXL work gated on GPU availability and his taste. The exterior
  gets the cheap honest move now — profession signboard overlay + nametag — and full
  per-profession exterior art is a named, owner-gated follow-up (Open Question 3).
- **KTD-4 — Watch = re-host the animated surface that already exists.** `ScryingMirror` becomes
  a composite: the `MineWatch` strip (backdrop, figures, torchlight, `DelveStage` beat overlay)
  mounted as its top band, journey feed and roll call below. `MineWatch` moves from
  "instantiated by DepthsPanel" to "instantiated by whichever host is open" — one live instance,
  ownership passed, never two SubViewports rendering at once (constraint 4). Rebuilding
  DelveStage's 749 lines of working FX as something new would be the exact re-invention this
  repo's memory warns against. Two adjacent fixes ride along: the strip's backdrop follows the
  party's actual venue (`MineWatch.cs:65` hardcodes `"mine"`; `InFlight.VenueId` /
  `PendingExpeditions` carry the truth, and gloomwood/sunkencrypt/emberfall backdrops are all
  committed at the same 160×160 footprint), and the PiP dock gets legible copy + party HP pips
  (Labels/ColorRects only — no viewport in the dock, ever).
- **KTD-5 — Hero return is a ceremony with a floor, not a teleport.** The sim resolves unstaged
  runs instantly; that is correct and untouched. The adapter already owns return presentation
  (`Town2D.ReturnSurvivors`), so it gains a show floor: survivors do not begin their walk-in
  until (a) the phase vocabulary has left "Quest" AND (b) a minimum on-screen delve interval has
  elapsed since departure (the PiP/strip had time to show the fight), then they emerge FROM the
  gate, staggered, under a brief gate-focus beat with a narrator line. Save/load and edge cases
  keep the existing `SnapRemainingHeroesHome` fallback — the floor only stretches presentation
  inside a live session, never state.
- **KTD-6 — Night is two constants and one missing input, not a lighting engine.** (a) Retune
  `DayPhaseTint` stops so the label matches the light: Evening/"Night" drops to genuinely dark
  (~0.45 luminance band, between today's Camp and ExpeditionDeep), Camp/Deep hold their dark
  values, Morning/"Dawn" stays pale — same purple family, one hue identity, per the class's own
  doc. A pure engine-free test pins minimum channel separation between Night and Dawn so this
  can never silently regress. (b) `AmbientLife2D` gains a `DayPhase` input: lamp glow alpha
  ramps up for Evening/Camp/Deep and nearly off for Morning; warm window-glow quads on the five
  venues follow the same curve. Because `CanvasModulate` multiplies the whole canvas, bright
  warm glows over a darkened town read as light sources — no Light2D system, no new render
  concepts. No sky/moon layer this slice (Scope boundaries).
- **KTD-7 — Hero sprites: the honest dials are screen scale and motion, not a fourth repaint.**
  Measured facts: a same-size repaint moved 0.07% of pixels (#329 — invisible); the canvas is
  capped by the proportion pin (hero < player 15×23 effective, `CastProportionTests`); 26×44 at
  clean 2:1 decimation (#341) is where pixel craft tops out at this scale. What remains: (a)
  **screen scale** — `CharacterSpriteScale` 0.5 → 0.65/0.75 renders the SAME art at 17×29 /
  19.5×33, more visible detail for one constant, at the cost of town proportion he previously
  tuned to 0.5 — his eye must re-judge; (b) **motion** — a 2-frame walk is the minimum alive;
  4-frame walk + idle frame sells character better than any static repaint; (c) **portraits** —
  the large `hero-{class}` portraits (Mirror roll call, MineWatch figures at 64px, tavern cards)
  are where SDXL is actually viable (documented dead end only at ≤44px sprite scale) — a
  regeneration pass is GPU-gated and deferrable. U13 renders (a) and (b) as receipt candidates
  and scopes (c); his pick lands as its own follow-up. No candidate lands by default.
- **KTD-8 — Shop choreography moves into the room; `ShopStage` retires after.** `ShopStage`
  (SubViewportContainer, 1024×220) was kept alive through #349 precisely for the market room —
  but its FORM (a flat viewport strip) cannot stand inside a walkable island. What transfers is
  its choreography vocabulary and data feed: customer figures entering, walking to shelf slots /
  counter, emoting (`ShopEmoteGlyph` is code-drawn and reusable as-is), coin-arc on sale —
  driven by the same Morning-tick events (`ItemSold{FromPlayerShop}`, `HeroPassedOnItem`,
  `CounterSaleClosed`, `CustomerWalked`). U5 builds `MarketLife2D` as world-space actors inside
  the market island (the `TownsfolkNpc2D` walker pattern), then retires `ShopStage` +
  `ShopStageTests` in the same unit — no orphans, stated test-count delta.

---

## Implementation Units

### U1 — Three rooms exist: market, tavern, gatehouse (rows + loud placeholders)

**Goal:** E at Shop, Tavern, or Mine Gate walks the player into a placeholder room with its
stations placed, labelled, and routed; door and Esc walk out. Framework provable before paint.

**Files:**
- Modify: `godot/scripts/town2d/InteriorLayout2D.cs` — three new `RoomSpec` rows (post-#349
  `StationSpec` shape). Island offsets: forge stays (2048, 0); market (2048, 512); tavern
  (2048, 1024); gatehouse (2048, 1536) — one lane, vertically separated beyond any camera clamp.
  Pinned content (art ids fixed here so U2–U4 run in parallel):
  - `market` (20×12 tiles, shell `town2d-market-interior-shell`): `counter` "Sales Counter" →
    `Shop`; `shelf-a`/`shelf-b` "Display Shelf" → `Shop` (Focus `stock` if ShopPanel grows a
    section anchor — else plain `Shop`); `ledger` "Ledger Desk" → `Ledger` if that modal route
    exists in the #349 action vocabulary, else flavor; `crates` flavor. Sprites
    `town2d-station-market-counter`, `-market-shelf`, `-market-ledger`, `-market-crates`.
  - `tavern` (22×13 tiles, shell `town2d-tavern-interior-shell`): `bar` "The Bar" → `Tavern`;
    `storywall` "Story Wall" → `Legends`; `table-a`/`table-b` "Patron Table" → `Tavern`
    (patron seating anchors for U6 — table tiles double as seat positions); `hearth` flavor.
    Sprites `town2d-station-tavern-bar`, `-tavern-table`, `-tavern-hearth`,
    `-tavern-storywall`.
  - `minegate` (18×11 tiles, shell `town2d-gatehouse-interior-shell`): `muster` "Muster Board"
    → `Depths`; `bountyledger` "Bounty Ledger" → `Bounties`; `overlook` "The Overlook" →
    `Watch` (NEW action string — routes to `Mirror.ShowMirror()`; during non-live phases it
    opens with the "nobody below" empty state the Mirror already renders); `winch` flavor.
    Sprites `town2d-station-gate-muster`, `-gate-bounty`, `-gate-overlook`, `-gate-winch`.
- Modify: `godot/scripts/MainUi.cs` — add `"Watch"` to the station action vocabulary
  (`Mirror.ShowMirror()`); verify quick-travel to Shop/Tavern/Gate funnels through the same
  data-gated `OnTownBuildingClicked` route the forge uses (content parity — expected zero-diff,
  verify); noticeboard behavior untouched.
- Modify: `godot/tests/AssetResolutionCensusTests.cs` — the 15 new ids (3 shells + 12 stations)
  enter `KnownPendingIds` (framework-before-art bridge; forge's hardcoded case untouched).
- Modify: `godot/tests/InteriorEntryExitTests.cs` — parameterize entry/exit round-trip over all
  four venue keys; per-venue: station press opens its routed surface; overlook opens the Mirror;
  Esc ladder (drawer closes before room); noticeboard still opens its drawer.
- Modify: `godot/tests/InteriorRoomTests.cs` — table validation covers new rows automatically
  (verify `Watch` joins the recognized-action set; flavor rows carry both lines).
- Modify: `godot/scripts/tools/FullPlaytest.cs` — building-click sweep walks all four rooms.

**Approach:** pure data + routing. No `Town2D` changes expected (`EnterInterior`/`ExitInterior`
are venue-generic). The `Watch` action is the only new vocabulary — one switch arm.

**Test scenarios:** round-trip preserves outside position per venue; every station action
recognized (dead-click guard); flavor stations toast, never open; census green with pending ids
listed; `FocusOnMineGate` beats suppressed while inside any room.

**Verification:** full engine suite green (orchestrator, serialized); CI green.

**Visible-difference receipt:** three before/after pairs via `receipt.ps1 -State Shop|Tavern|Gate`
— drawer-over-town vs inside-a-placeholder-room. Near-total diffs.

---

### U2 — The painted market: shell + four station sprites

**Goal:** the market room becomes a painted general store — plank floor, laden shelves, counter —
in the town's pixel discipline.

**Files:**
- Create: `art/pipeline/gen-market-interior.py` — authors `town2d-market-interior-shell.png`
  (320×192) + the four `town2d-station-market-*` sprites; palette sampled from committed
  `town2d-*` siblings; `--check` drift guard; the `gen-forge-interior.py` idiom exactly.
- Create: the five PNGs + `.import` under `godot/assets/art/`.
- Modify: `godot/tests/AssetResolutionCensusTests.cs` — the five ids leave `KnownPendingIds`
  (red-then-green shown in the PR body).

**Approach:** iterate against `receipt.ps1 -State Shop` renders at play scale. Composition
brief: open floor for U5's customer walkways between door, shelves, and counter — the
choreography's paths are part of the room's design, not an afterthought. Keep U1's pinned tile
footprints (a size mismatch shifts collision).

**Test scenarios:** `--check` green on fresh render; census green; sprite dims match the
footprints U1 declared.

**Verification:** engine suite green (orchestrator, serialized); CI green.

**Visible-difference receipt:** placeholder room vs painted room pair. Owner taste pass is the
real gate; his notes become follow-up rows.

### U3 — The painted tavern: shell + four station sprites

Same shape as U2. `art/pipeline/gen-tavern-interior.py`; shell `town2d-tavern-interior-shell`
(352×208) — hearth-lit, tables with clear seat tiles for U6's patrons, bar along one wall, story
wall with pinned scraps; four `town2d-station-tavern-*` sprites; census red→green; receipt via
`-State Tavern`. Warm-dark palette sampled from committed siblings (tavern reads dimmer than the
market — it is the room where the hearth carries the light).

### U4 — The painted gatehouse: shell + four station sprites

Same shape as U2. `art/pipeline/gen-gatehouse-interior.py`; shell
`town2d-gatehouse-interior-shell` (288×176) — stone, chain, tackle; the overlook window on the
north wall showing darkness below (it is the diegetic home of the watch); four
`town2d-station-gate-*` sprites; census red→green; receipt via `-State Gate`.

---

### U5 — The shop lives: customers shop inside the market room (and ShopStage retires)

**Goal:** the owner's "shop needs an inside where the NPCs interact" — hero customers visibly
enter the market room, walk to shelves, emote over goods, buy at the counter with a coin
flourish, and leave. The old flat `ShopStage` strip retires with its choreography absorbed.

**Files:**
- Create: `godot/scripts/town2d/MarketLife2D.cs` — world-space customer choreography inside the
  market island (KTD-8): customer actors (hero-class `town2d-hero-*` bodies, `ClassColors`
  tint, the `TownsfolkNpc2D` walker idiom), spawned from the same Morning-tick events ShopStage
  consumed (`ItemSold{FromPlayerShop:true}`, `HeroPassedOnItem`, `CounterSaleClosed`,
  `CustomerWalked`); walk-in from the door tile → shelf/counter tile → emote
  (`ShopEmoteGlyph`, reused as-is — it is a code-drawn Node2D) → walk out; coin-arc on a
  closed sale. Deterministic: accumulated delta only, replayable queue like `ShopStage.QueueDay`.
- Modify: `godot/scripts/town2d/Town2D.cs` — mount/refresh `MarketLife2D` with the market room;
  feed it the tick's events (same feed point the town's other event consumers use).
- Delete: `godot/scripts/panels/ShopStage.cs`, `godot/tests/ShopStageTests.cs` — the class was
  kept through #349 for exactly this moment; once the room carries the choreography it is an
  orphan (orphan policy: never leave one). Sweep references by grep; state the test-count delta
  in the PR body.
- Create: `godot/tests/MarketLifeTests.cs` — a queued sale produces a customer that reaches a
  shelf slot and emotes; a passed-on item produces the slump walk; determinism (same queue +
  same deltas = same positions); render-disabled frame pumping per constraint 4.

**Approach:** strictly after U1 (needs the market row; placeholder art is fine — choreography
and art land independently). Emote/coin vocabulary ports 1:1; walk speeds re-derived from
`TownsfolkNpc2D`'s scale rather than ShopStage's 150 px/s strip units. Customers render only
while the room exists; they are cosmetic actors, zero sim writes.

**Test scenarios:** as above, plus: no customers spawn when the day had no shop events; actors
never leave the room rect; Y-sort against the player holds (customers use the same
`CharacterArtRoot` scale).

**Verification:** engine suite green (orchestrator, serialized); CI green.

**Visible-difference receipt:** market-room frame mid-choreography (customer at shelf, emote
visible) vs empty room; plus the ShopStage deletion's stated test-count delta.

---

### U6 — The town runs errands; the tavern has patrons

**Goal:** "make more lively" gets its cheapest real wins: townsfolk walk actual routes between
buildings instead of drifting in place, and heroes present in town sit at the tavern's tables
with visible moods.

**Files:**
- Modify: `godot/scripts/town2d/TownsfolkNpc2D.cs` — errand mode: pick a venue door anchor
  (deterministic, id-seeded rotation), walk there via the existing step-frame walker, dwell,
  walk home; lissajous drift becomes the between-errands idle.
- Create: `godot/scripts/town2d/TavernLife2D.cs` — patron figures at the tavern room's table
  seat tiles (U1 pinned them): heroes currently in town (present, not Away), rendered with
  their class body + a mood emote (`ShopEmoteGlyph` reuse; mood from the same hero state
  `TavernPanel` already reads); refreshed on tick; capped at seat count.
- Modify: `godot/scripts/town2d/Town2D.cs` — mount `TavernLife2D` with the tavern room; extend
  `BuildTownsfolk` for errand targets. (Serial with U5 on `Town2D.cs`.)
- Create: `godot/tests/TownLifeTests.cs` — errand walk reaches a venue door and returns
  (condition-waited, render disabled); patrons appear only for present heroes; patron count ≤
  seats; Away heroes never seated (ties into U10's fiction).

**Approach:** after U1 (tavern row) and serialized after U5 (shared `Town2D.cs`). Patrons are
cosmetic duplicates of hero identity (portrait-tinted bodies), NOT the wandering `HeroActor2D`
instances — no state tangling with rally/march logic; the same hero may wander the square or
sit, decided by a simple deterministic pick, never both visible at once (guard: patron set
excludes heroes currently mid-rally/march).

**Test scenarios:** as above.

**Verification:** engine suite green (orchestrator, serialized); CI green.

**Visible-difference receipt:** town frame with a townsfolk mid-errand on the road; tavern-room
frame with two patrons seated and emoting.

---

### U7 — The workshop follows your profession (vocabulary + station sets)

**Goal:** an alchemist's E at their workshop enters a brewing room, the building is named for
their craft, quick-travel says so, and the tutorial points at the right furniture. Blacksmith
players see zero change.

**Files:**
- Create: `godot/scripts/town2d/WorkshopVocab.cs` — one static table, `PhaseVocab`'s idiom:
  professionId → workshop nametag ("Forge" / "Apothecary" / "Workbench Hall" / "Tannery" —
  defaults, Open Question 2), quick-travel label, drawer title, and station-set key. Primary
  profession (first selected) names the building; both selected professions contribute
  stations.
- Modify: `godot/scripts/town2d/InteriorLayout2D.cs` — the workshop room becomes
  profession-composed: the `forge` row's station table is built from per-profession station
  sets (blacksmith = today's six, unchanged); a room-spec resolver
  `WorkshopRoomFor(selectedProfessions)` unions the selected sets into the shared shell (KTD-3).
  Station sets for the other three professions (sprite ids pinned here so U8 runs in parallel):
  - alchemy: `cauldron` → craft focus, `still` → craft focus, `reagent-shelf` → materials
    focus, `potion-rack` → `Shop`, `herb-bundles` flavor
    (`town2d-station-alch-cauldron`, `-alch-still`, `-alch-shelf`, `-alch-rack`,
    `-alch-herbs`).
  - engineering: `bench` → craft, `gear-rack` → materials, `parts-crate` → `Shop`,
    `flywheel` flavor (`town2d-station-eng-bench`, `-eng-gears`, `-eng-crate`,
    `-eng-flywheel`).
  - tanning: `scrape-frame` → craft, `hide-rack` → materials, `goods-rack` → `Shop`,
    `vats` flavor (`town2d-station-tan-frame`, `-tan-hides`, `-tan-rack`, `-tan-vats`).
- Modify: `godot/scripts/town2d/TownLayout2D.cs` — venue nametag for `forge` resolves through
  `WorkshopVocab` at build time (key `"forge"` untouched — KTD-3(b)); exterior signboard
  overlay sprite id per profession (`town2d-sign-{professionId}`, four small sprites, pinned
  for U8).
- Modify: `godot/scripts/MainUi.cs` (serial after U1) — quick-travel label + drawer title
  resolve through `WorkshopVocab`; drawer id `"Forge"` stays internal.
- Modify: `godot/scripts/ui/TutorialFlow.cs` — the fallback copy "craft at the anvil" and any
  step text naming the Forge resolve through `WorkshopVocab` (station noun included: "at the
  anvil"/"at the cauldron"/...); `StepBuilding` keys untouched (they are routing, already
  profession-correct per #333/#338).
- Modify: `godot/scripts/NewGameSelect.cs` — the profession picker's blurb line gains "Your
  workshop: the {nametag}" so the pick's world consequence is stated at pick time (the
  "rethink the start picking" answer this slice can give: the pick now visibly matters).
- Modify: `godot/tests/AssetResolutionCensusTests.cs` — 17 new ids (13 stations + 4 signs) into
  `KnownPendingIds`.
- Create: `godot/tests/WorkshopVocabTests.cs` — every professionId has nametag/label/set;
  union-of-two-professions builds a valid room (no tile collisions between sets — sets are
  authored on disjoint tile zones of the shared shell); blacksmith-only room is byte-identical
  to today's forge row (zero-regression pin); every station action recognized (inherits the
  dead-click guard).
- Modify: `godot/tests/InteriorEntryExitTests.cs` — alchemist-start drive: enter workshop, see
  cauldron station, press → ForgePanel opens focused on craft with the "Brew" surface (the
  panel is already profession-correct; this pins the room now matching it).

**Approach:** strictly after U1 (shares `InteriorLayout2D.cs` + `MainUi.cs`). The forge row's
blacksmith path must be regression-free — the zero-regression pin above is the unit's contract.
Dual-profession layout: shell has two station zones (left/right); primary set takes the hearth
side. Second-profession-added-mid-run rebuilds the room on next entry (rooms are built per
entry already — verify; if rooms are built once at startup, rebuild-on-change is this unit's
one structural change).

**Test scenarios:** as above, plus signboard resolves per profession (loud placeholder until
U8).

**Verification:** engine suite green (orchestrator, serialized); CI green.

**Visible-difference receipt:** two pairs from an alchemist-start save: outside (nametag +
placeholder signboard vs "Forge") and inside (cauldron/still placeholder room vs anvil room).
Plus a blacksmith-start frame proving zero change.

---

### U8 — Profession station art: three sets + signboards

**Goal:** the placeholder alchemy/engineering/tanning stations and the four exterior signboards
become real pixels.

**Files:**
- Create: `art/pipeline/gen-profession-stations.py` — 13 station sprites + 4 signboards
  (~24×20 to 32×40 each), palette sampled from committed `town2d-*` siblings (cauldron gets
  the COOLANT/ARCANE accent, tanning the leather-brown band already present in the townsfolk
  tints, engineering the IRON/BONE machine planes); `--check` drift guard.
- Create: the 17 PNGs + `.import` under `godot/assets/art/`.
- Modify: `godot/tests/AssetResolutionCensusTests.cs` — 17 ids leave `KnownPendingIds`
  (red-then-green in the PR body).

**Approach:** parallel with U7's code (ids pinned there); merges after U7. Iterate against
`receipt.ps1` renders of an alchemist-start save. This is a similar total volume to the forge
interior set — budget accordingly (1–2 focused agent-days), and author ONE set first
(alchemy — it is the profession he actually started), receipt it, then batch the rest.

**Test scenarios:** `--check` green; census green; dims match U7's declared footprints.

**Verification:** engine suite green (orchestrator, serialized); CI green.

**Visible-difference receipt:** alchemist workshop placeholder vs painted pair — the plan's
second marquee image. Owner taste pass expected.

---

### U9 — The watch shows the show (Mirror hosts the animated delve)

**Goal:** pressing Watch (button, dock, or the gatehouse overlook) during a live expedition
shows the animated delve strip — backdrop, marching figures, monster, HP, hit flashes, beat
playback — with the journey feed and roll call beneath it. The dock states what it is.

**Files:**
- Modify: `godot/scripts/panels/ScryingMirror.cs` — top band hosts the `MineWatch` strip;
  feed/roll-call/attribution content moves below; the strip is requested from a shared owner
  (below) on open and released on close.
- Modify: `godot/scripts/panels/MineWatch.cs` — (a) single-instance ownership: extracted from
  "DepthsPanel constructs me" to a handle both hosts borrow (`MainUi` owns the instance;
  DepthsPanel and ScryingMirror mount/unmount it — one SubViewport alive, ever, constraint 4);
  (b) venue-true backdrop: `Build(AssetCatalog.VenueBackdropId(venueId))` where venueId comes
  from `InFlight[0].VenueId` / the departing party's target (falls back to `"mine"`) —
  gloomwood/sunkencrypt/emberfall backdrops are committed at the same 160×160 footprint;
  (c) the milestone-flash and phase gates unchanged.
- Modify: `godot/scripts/panels/DepthsPanel.cs` — borrows the shared strip instead of
  constructing its own.
- Modify: `godot/scripts/ui/PipDock.cs` — R5: title line "SCRYING MIRROR" (it finally names
  itself), expand button copy "Watch the delve ⤢", one row of party HP pips (ColorRects fed
  from the same `InFlight`/hp data `MineWatch` reads — text/rect only, no viewport in the
  dock); height 76 → ~96 to stop the truncation he saw ("doesn't appear correctly. too
  small").
- Modify: `godot/scripts/MainUi.cs` (serial after U7) — owns the shared `MineWatch` handle;
  Watch button copy unchanged; `Watch` station action (U1) already routes here.
- Tests: extend the #321/#335 spectate coverage (`SendOffOpensTheShowTests.cs` and siblings):
  Mirror open during a live phase → strip present, beats playing (condition-waited, rendering
  disabled per constraint 4); Depths and Mirror open sequentially → the single strip re-parents
  cleanly, never two viewports; venue-true backdrop id asserted per raided venue; dock shows
  pips during Camp.

**Approach:** re-host, don't rebuild (KTD-4). The known hazard is TWO live SubViewports — the
shared-instance handle is the design answer, and the sequential-host test is its pin. The strip
inside a modal renders at the Mirror's width (SubViewportContainer stretch already handles
container width — verify at his resolution; the fitted-card sizing pass from #335's U1 notes
applies).

**Test scenarios:** as above, plus: Mirror during Morning/Evening shows the honest empty state
("nobody below"); Escape ladder unchanged; PipDock suppression rules unchanged.

**Verification:** engine suite green (orchestrator, serialized); CI green.

**Visible-difference receipt:** `receipt.ps1 -State Mirror` before/after — text columns vs
animated strip with figures and HP bars mid-beat. Second receipt: the dock pair (old 76px
text-only vs titled, pipped, taller). This is the plan's loudest single receipt.

---

### U10 — Gone means gone: the return is a ceremony, not a teleport

**Goal:** while the HUD says Quest/Vigil, no party member is visible in town; when they return,
they file out of the gate under a brief focus beat with a narrator line. The "why did they come
back" moment cannot recur.

**Files:**
- Modify: `godot/scripts/town2d/Town2D.cs` — `ReturnSurvivors` gains the show floor (KTD-5):
  survivors' walk-in is queued, not immediate; it fires when (a) the completed-phase handler
  confirms the phase vocabulary has left Quest (Expedition completed) AND (b) at least
  `MinDelveShowSeconds` (~8 s, tunable constant with a doc comment) have elapsed since
  `PartyDeparted`'s march-out finished; emergence is FROM the gate door anchor, staggered by
  the existing `FileExitStaggerSeconds` idiom, under a short `FocusOnMineGate` borrow (reuse,
  including its drawer/modal deferral rules from #335); `SnapRemainingHeroesHome` keeps its
  save/load/edge role unchanged (the floor is session-presentation only and never survives a
  reload).
- Modify: `godot/scripts/MainUi.cs` or narrator feed point (whichever owns toast lines — locate
  at execution): one line on emergence ("The party returns from the {venue}...") through the
  existing narrator toast idiom.
- Create: `godot/tests/HeroReturnCeremonyTests.cs` — drive an unstaged floor-1 day: after the
  Expedition bell press, heroes remain invisible until both floor conditions pass
  (condition-waited); emergence walks from the gate anchor; reload mid-hold snaps heroes
  correctly (no stuck-invisible actor — the failure mode this unit must prove impossible);
  staged (Camp) runs unchanged.

**Approach:** serialized with U5/U6 on `Town2D.cs` (declared order: U5 → U6 → U10, or rebase
deliberately). The floor never delays STATE — ledger, drawer contents, and phase are already
correct the whole time; only body visibility and the camera beat are staged. The stuck-invisible
hazard is the unit's central test target, not an afterthought.

**Test scenarios:** as above, plus: heroes never render in town while `InFlight` holds them AND
the floor is active; the tavern's U6 patron guard reads the same visibility truth.

**Verification:** engine suite green (orchestrator, serialized); CI green.

**Visible-difference receipt:** three frames from one driven session: (1) Quest phase, town
empty of party; (2) emergence beat at the gate; (3) the narrator line on screen. Plus the
timeline (`PlaytestLog` timestamps) proving ≥ the floor elapsed between march-out and
emergence.

---

### U11 — Night is dark, dawn is dawn (tint retune + phase-lit lamps and windows)

**Goal:** the phase labelled Night is unmistakably night; Dawn reads pale and waking; lamps and
windows carry the difference even before the tint registers.

**Files:**
- Modify: `godot/scripts/town2d/DayPhaseTint.cs` — retune stops (KTD-6): `EveningTint`
  (0.86, 0.80, 0.93) → dark violet ~(0.42, 0.36, 0.58); `CampTint`/`ExpeditionDeepTint` hold;
  `MorningTint` stays pale (0.97) — the Night↔Dawn delta becomes the palette's largest. Ease
  rate unchanged. (Pure class — engine-free tests.)
- Modify: `godot/scripts/town2d/AmbientLife2D.cs` — gains a `DayPhase` input (set by `Town2D`
  each tick, mirroring how `DayPhaseTint` gets it): lamp glow alpha curve per phase (Morning
  ~0.06, Expedition ~0.25, Evening/Camp/Deep 0.7–0.85 with the existing flicker); new
  window-glow quads (warm `ColorRect`/sprite glows at each venue's window anchor — anchor
  table in this file, hand-placed against the five SDXL exteriors) on the same curve.
- Modify: `godot/scripts/town2d/Town2D.cs` — feed the phase to `AmbientLife2D` (serial after
  U10 on this file); interior warm-tint override unchanged.
- Modify: `godot/tests/DayPhaseTintTests.cs` (or create if the pure class has no suite yet) —
  pin: per-channel separation between `TintFor(Evening)` and `TintFor(Morning)` ≥ 0.35 (the
  "Night vs Dawn must differ" regression guard); monotonic darkness ordering
  Morning > Expedition > Evening ≥ Camp > ExpeditionDeep.
- Create: `godot/tests/PhaseLightTests.cs` — lamp/window alpha responds to phase (no
  frame-count waits; drive the phase input directly).

**Approach:** two constants and one input, no lighting engine (KTD-6). Judged at receipts: the
same town frame captured per phase (drive via the harness's phase path — `SendOff` state plus
bell advances, or a `SHOT_PHASE` hook if one is needed; keep the harness change minimal).
Candidate darkness levels for Evening rendered as a 3-option contact row for his eye
(Open Question 5) — shipping default is the ~0.42 band.

**Test scenarios:** as above; interior rooms unaffected (warm override); `MineWatch`'s own
SubViewport-scoped modulate untouched.

**Verification:** engine suite green (orchestrator, serialized); CI green.

**Visible-difference receipt:** a five-phase contact strip of the same town view — Dawn pale,
Quest lavender, Night dark with lit windows and lamps, Vigil darker, Deep Vigil darkest. The
Night frame vs today's Night frame is the marquee diff.

---

### U12 — Stations you can read across the room

**Goal:** verb-bearing stations visibly differ from flavor at a glance (R2's second half):
interactables carry a soft looping tell; flavor stays still and dim. Applies to the forge room
and every new room.

**Files:**
- Modify: `godot/scripts/town2d/Building2D.cs` — opt-in "tell" layer for stations: a small warm
  glow pulse (the `AmbientLife2D` lamp-sine idiom, ~0.15 alpha amplitude) anchored to the
  sprite, enabled iff the station's `Action` is non-null; flavor stations get nothing beyond
  #349's dim nametag. One flag on `Configure`, default off (town buildings unchanged).
- Modify: `godot/scripts/town2d/InteriorRoom2D.cs` — pass the flag from the spec (Action
  non-null).
- Modify: `godot/tests/InteriorRoomTests.cs` — verb stations have the tell node; flavor
  stations don't; town buildings unchanged.

**Approach:** after U1 lands (spec plumbed); independent of art units — the tell reads over
placeholders too. Deliberately code-only (no per-station animation frames this slice — a
2-frame furnace/cauldron idle is a named follow-up if the pulse isn't enough for his eye).

**Test scenarios:** as above.

**Verification:** engine suite green (orchestrator, serialized); CI green.

**Visible-difference receipt:** forge-room pair, pulse at peak vs off — plus one new-room frame
showing verb vs flavor side by side.

---

### U13 — Hero visuals, third round: the dials that are actually open (candidates + his pick)

**Goal:** turn "still wanna improve the heroes models" into a decidable choice along the dials
KTD-7 identifies — screen scale and motion now, portraits when GPU allows — with the dead ends
(same-size repaint, bigger-than-player canvas) explicitly closed.

**Files (candidates phase — nothing lands without his pick):**
- Candidate stills/strips to `runs/receipts/candidates/heroes-r3/`:
  - (a) scale bump: the SAME committed cast rendered at `CharacterSpriteScale` 0.5 / 0.65 /
    0.75 (one-constant builds, receipt each; note 0.65/0.75 breaks the clean 2:1 decimation —
    the render gets slightly softer sampling, which is exactly what his eye must judge);
  - (b) motion: ONE hero (striker) given a 4-frame walk + 1 idle frame in
    `tools/art/gen_town_sprites.py`'s grid idiom, rendered as a short receipt series
    (motion is judged from frames — receipt.ps1 stills at three walk phases);
  - (c) portraits (GPU-DEFERRED): scoped brief only — SDXL regeneration of the
    `hero-{classId}` portrait set used by Mirror roll call / MineWatch figures / tavern
    cards, in the Gloomwood/Crypt pipeline (`cutout.py`+`normalmap.py`); executes only when
    VRAM floor clears (constraint 7); its absence blocks nothing.
- Landing (after his pick): (a) is a one-constant PR + `CastProportionTests` still green
  (the pin is ratio-safe — scale applies to player and heroes alike); (b) is a sized follow-up
  unit (12 grids × more frames — budget stated before starting, `SpriteMotion`/walk-frame
  plumbing extended from 2-frame to N-frame); (c) is its own GPU unit.

**Approach:** honest framing in the candidate sheet: what #329 measured (same-size repaint =
0.07% = invisible), what #341 already spent (26×44 is the proportion ceiling), and that (a) is
free but changes town proportion he tuned, (b) is the biggest craft win per pixel, (c) improves
the surfaces U9 just made prominent. **Needs his pick — no default lands.**

**Test scenarios:** none for candidates; landing paths inherit census/proportion/engine gates.

**Verification:** landing PRs only.

**Visible-difference receipt:** the candidate contact sheet itself; landing receipts per pick.

---

## Dependencies & parallelism

- **PR #349 merges before anything starts** (constraint 9).
- **U1 is the spine — first.** Then three parallel tracks:
  - **Art track:** U2, U3, U4 in parallel (disjoint files; ids pinned by U1); each merges
    independently. U8 parallel after U7 pins its ids.
  - **MainUi chain (serial):** U1 → U7 → U9 (shared `MainUi.cs`).
  - **Town2D chain (serial):** U5 → U6 → U10 → U11 (shared `Town2D.cs`).
- U12 any time after U1. U13 candidates any time (no code); its landings wait on the owner.
- **Merges are serial across ALL units regardless of file disjointness** — every unit runs the
  full engine suite, and engine runs are one at a time (constraint 1).
- GPU appears only in U13(c) and Open Question 3 — both deferrable indefinitely; no unit waits
  on VRAM.

## Verification contract

| Unit | Engine suite (orchestrator, serialized) | Census | Receipt |
|---|---|---|---|
| U1 | required | 15 ids → pending | 3 drawer-vs-room pairs |
| U2–U4 | required | ids red→green (shown) | placeholder-vs-painted pair each |
| U5 | required | — | choreography frame + ShopStage test-count delta |
| U6 | required | — | errand frame + seated-patrons frame |
| U7 | required | 17 ids → pending | alchemist outside+inside pairs + blacksmith zero-change frame |
| U8 | required | ids red→green (shown) | alchemist workshop painted pair |
| U9 | required | — | Mirror text-vs-strip pair + dock pair |
| U10 | required | — | 3-frame return sequence + timeline log |
| U11 | required | — | five-phase contact strip; Night-vs-Night diff |
| U12 | required | — | pulse on/off pair |
| U13 | landing PRs only | landing-dependent | candidate contact sheets |

No unit runs the balance gate or touches the fast lane as a gate (zero sim edits anywhere).
Known flaky pre-step unchanged: engine suite reporting ~54 tests → kill stray Godot processes,
rebuild headless, re-run; `git restore -- '*.import'` before staging.

## Scope boundaries (deliberately deferred, named)

- **No per-profession building EXTERIOR art** — signboard + nametag only (KTD-3); full exterior
  variants are owner-gated SDXL follow-ups (Open Question 3).
- **No noticeboard interior** (KTD-2) — the board keeps its drawer unless Open Question 1 says
  otherwise.
- **No new lighting engine, no sky/moon/stars layer** — tint stops + phase-keyed glows only;
  a Light2D pass is a named follow-up if his eye asks for more after U11.
- **No Mirror/feed CONTENT redesign** beyond hosting the strip (U9) — the roll-call/attribution
  text he has now finally seen generates its own notes first.
- **No per-station animation frames** (U12 is code-pulse only; frame-animated stations are the
  follow-up if needed).
- **No per-venue interior music/ambience** — `AudioDirector.SetScene` has only `"depths"`
  today; interior scenes (hearth crackle, market murmur) are a natural follow-up batch, not
  this plan.
- **No profession-selection flow redesign** — "rethink the whole start picking" is answered
  this slice by making the pick visibly matter (U7's picker line + world response); a full
  onboarding redesign waits for his verdict on that.
- **No sim/Contracts/balance/golden/CI/deny-list changes anywhere.**
- **Plans-index rule:** the commit landing this document adds its row to
  `docs/plans/README.md` (LIVE table) per that file's rule 2.

## Open questions for the owner

1. **Gatehouse vs noticeboard (KTD-2):** shipping default folds the bounty verb into the
   gatehouse as a "Bounty Ledger" station and leaves the outdoor board's drawer as-is. If you
   meant the noticeboard itself should have an inside, say so and U4 grows a sibling.
2. **Workshop names (U7):** defaults are Forge / Apothecary / Workbench Hall / Tannery. One
   table row each — reword freely at the receipt.
3. **Per-profession exteriors:** is signboard + nametag enough for now, or do you want full
   per-profession building art later (SDXL, GPU-gated, four exteriors)? Nothing in this plan
   blocks on the answer.
4. **How dark is Night (U11):** three candidate darkness levels rendered side by side; default
   is the middle (~0.42 band).
5. **Return ceremony floor (U10):** ~8 s minimum between march-out and emergence on instant
   runs — feel free to retune with a number at the playtest.
6. **Hero direction (U13):** scale bump vs motion frames vs portrait regen — the candidate
   sheet is built so you can pick one, several, or none.
7. **Tavern patrons and the counter (U6/future):** patrons currently just sit and emote. If you
   want them to walk to the bar, order, and gossip visibly, that is a follow-up choreography
   unit on the U5 pattern — say the word.

## Definition of done

1. E at Shop, Tavern, and Mine Gate each walks into a painted room with differentiated,
   honestly-labelled stations; the forge pattern is now the town's pattern.
2. Customers shop visibly inside the market; patrons sit in the tavern; townsfolk run errands.
3. An alchemist start shows a workshop named, dressed, and furnished for alchemy — and a
   blacksmith start is pixel-identical to today.
4. Watch shows the animated delve everywhere it is offered — button, dock, overlook — and the
   dock names itself and shows party health.
5. While the game says the party is below, town shows no party member; their return is a staged
   beat with a narrator line.
6. Night is dark, Dawn is pale, lamps and windows agree with the clock, and a test pins the
   Night/Dawn separation forever.
7. The hero-sprite question has rendered candidates on the honest dials and a recorded owner
   verdict path — no taste guessing, no invisible repaints.
8. Every art id is census-enforced (red→green shown); every unit carried its receipt; every
   engine run was orchestrator-serial; `sim/` has zero diffs across the whole plan.
