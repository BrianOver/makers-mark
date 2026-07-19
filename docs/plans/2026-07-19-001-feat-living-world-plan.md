---
title: "feat: living world — event choreography, atmosphere, presence (LW wave)"
date: 2026-07-19
artifact_contract: ce-unified-plan/v1
artifact_readiness: implementation-ready
origin: Brian directive 2026-07-19 ("player needs to FEEL the world and BE in it, not just click around") + 4-source research pass (Moonlight Peaks, Erenshor, shop-sim choreography corpus, Godot 4.6 technique catalog)
execution: 5 worker lanes (A–E), one unit = one branch = one PR, worktrees under .claude/worktrees/
related: docs/design/graphics-2.5d-direction.md (lit-2D committed direction), docs/plans/2026-07-17-003-feat-town-2p5d-migration-plan.md (V4b/V5b still gated on Godot 4.7.1 — THIS PLAN DOES NOT TOUCH THAT GATE), docs/design/lane-operating-model.md
---

## Goal Capsule

The sim already generates a living world — purchases, departures, returns, deaths, gossip, camps,
records — but the screen shows static figures and dashboards. This wave binds **existing sim
events to visible choreography** and layers **atmosphere** (two-temperature lighting, particles,
props, ambient motion) onto the shipped lit-2D overlay, entirely within the 4.6.3 additive-scene
rules. Zero sim changes. Zero contracts changes. The V4b full-migration gate (Godot 4.7.1) is
untouched — everything here is `Control`-town + `LitTownOverlay`-pattern work that survives the
later migration.

**Design thesis (from research):** presence = (1) never-static motion, (2) events witnessed on
screen instead of read in a ledger, (3) warm-vs-cool light contrast, (4) small ambient motion in
negative space. Erenshor's lesson: autonomous agents FEEL real when their decisions are visibly
enacted (rally → depart → return → absence). Moonlighter's lesson: one emote bubble at the moment
of decision beats a paragraph of log. Moonlight Peaks' lesson: lit windows against a cool night is
the cheapest "someone lives here" signal that exists.

## Hard constraints (every unit)

- **No sim/GameSim changes. No Contracts changes.** Presentation reads `Adapter.LastEvents` /
  `CurrentState` only. Choreography state lives godot-side.
- **Determinism:** all animation driven by accumulated frame delta or Tween — never wall clock,
  never engine RNG. (Existing pattern: `LitTownOverlay._Process` flicker, `HeroSprite.WanderOffset`.)
- **Headless-safe tests:** GPUParticles2D does NOT simulate headless (compute shaders) — use
  **CPUParticles2D only**, assert `Emitting`/`Amount`/config, never pixels. PointLight2D asserts
  properties only. Prefer emissive additive `Sprite2D` for window glow (testable via
  `Visible`/`Modulate`).
- **Light budget:** ≤15 concurrent PointLight2Ds affecting one sprite (engine cap). Window glows
  are emissive sprites, not lights, except one hero-landmark light (forge coals).
- **Test pins to preserve:** node names `Marker`/`Sprite`/`Building_*`/`LitBuilding_*`/`LitHero_*`/
  `HeroLayer`, 7-tab shell, `StatusLabel`, `AdvanceDay` loop-until-Morning helper. New nodes get
  new names; never rename existing.
- **Engine pin 4.6.3**; code-built nodes only (no editor-authored .tscn edits).
- Gates per unit: fast lane + engine tests green locally; PR auto-merge; screenshot self-verify
  (capture scene, read the PNG, judge it) before marking done.

## Unit map

| Unit | Branch | Worker | Files (exclusive) | Blocked on |
|---|---|---|---|---|
| LW1 town-life choreography | feat/lw1-town-life | A | godot/scripts/town/TownScene.cs, HeroSprite.cs | — |
| LW2 speech bubbles + barks | feat/lw2-speech-bubbles | A (serial after LW1) | godot/scripts/town/SpeechBubble.cs (new) + TownScene hook | LW1 |
| LW3 shop stage + coin pop | feat/lw3-shop-stage | C | godot/scripts/panels/ShopPanel.cs, ShopStage.cs (new), MainUi.cs StatusBar region | — |
| LW4 atmosphere layer | feat/lw4-atmosphere | B | godot/scripts/town/LitTownOverlay.cs, AmbientFxLayer.cs (new) | — |
| LW5 depths watch | feat/lw5-depths-watch | D | godot/scripts/panels/DepthsPanel.cs, MineWatch.cs (new) | — |
| LW6 camera drift + transitions | feat/lw6-camera-feel | B (serial after LW4) | LitTownOverlay camera region, TabFade.cs (new) | LW4 |
| LW-art asset parity | feat/lw-art-parity | E | art/specs/**, godot/assets/art/** (+manifest), art/build/** | — |

Workers A/B/C/D/E run parallel; serial within a lane. All PRs independent-mergeable; rebase on
staleness (`gh pr update-branch <n> --rebase`).

## Unit specs

### LW1 — town-life choreography (Worker A)

Bind party lifecycle to visible motion in the Control town (`HeroSprite` state machine already
has Wandering/WalkingOut/Away/WalkingIn, 260 px/s, deterministic lissajous wander — extend it,
don't replace):

1. **Never-static idle:** wandering heroes get breath micro-bob (`sin` position ±1-2px, freq ~1.2Hz
   via accumulated `_townTime`) + walk bob while stepping (freq ≈ 2×steps/s, amplitude 2-3px) +
   `FlipH` by direction + arrival squash (`Scale (1.2,0.8)` → Tween back, Trans.Back, 0.2s).
2. **Rally-and-depart:** on `PartyDeparted`, party members path to a rally point near the gate
   (`GateWalkTarget` exists), dwell ~1s together, then exit through gate in file. Add
   `TownState.Rallying`.
3. **Return parade:** on `PartyReturned`, survivors walk in from gate spaced in file (not
   simultaneous spawn); the dead simply never walk in — absence IS the signal (memorials already
   handled by `RebuildMemorials`).
4. **Recruit arrival:** on `RecruitArrived`, spawn off-screen left edge, walk to Home.
5. **Anchor vignettes:** idle-target bias — a wandering hero occasionally (deterministic from
   heroId + day) pauses at a landmark anchor (well/noticeboard/tavern door coordinates) for a few
   seconds. Pure `HeroSprite` state, no sim contact.
6. **Smooth phase tint:** replace tint snap with a Tween over ~1.5s between `TintFor` stops.

Events arrive via existing `MainUi.OnPhaseCompleted → Town.OnPhaseCompleted(state)` path — pass
`Adapter.LastEvents` through (additive param or property; keep old signature callable for tests).
Tests: extend TownSceneTests — rally state reachable, recruit walk-in creates sprite off-screen
first, unknown-phase no-op stays green, all existing pins untouched.

### LW2 — speech bubbles + barks (Worker A, after LW1)

Code-built bubble: `PanelContainer` → `MarginContainer` → `Label` (autowrap), `_Draw()` triangle
tail (`DrawColoredPolygon`, `QueueRedraw` on change), pop-in Tween (`modulate:a` 0→1 + scale
0.85→1, 0.15s), ~4s hold, fade out. Node2D-space child of HeroLayer (moves with scene).

Sources (all existing events, rendered at the moment they happen instead of ledger-only):
- `GossipEmitted` → bubble over the hero nearest the tavern (or random wanderer) with the line.
- Pair-banter: when two heroes idle near each other, render one gossip line as A-speaks-B-reacts
  (react = simple "…!" bubble) — Erenshor's "world runs without me."
- `ItemSold` (FromPlayerShop) → buyer barks a short satisfaction line (reuse flavor text already
  in events; no new prose generation).
- Cap: max 2 concurrent bubbles, per-hero cooldown ≥20s of town time, dedupe repeat lines same day
  (Erenshor anti-pattern: verbatim repetition kills it).

### LW3 — shop stage + coin pop (Worker C)

The Moonlighter core loop made visible. New `ShopStage.cs` — a slim lit strip (SubViewport
pattern cloned from `LitTownOverlay`, ~1024×220) mounted at the top of ShopPanel:

1. Backdrop: `shop-interior` art id if present else themed ColorRect gradient (graceful degrade
   like `IconRegistry.Lit`).
2. On Morning tick, read the day's `ItemSold`/`HeroPassedOnItem` events: for each (staggered by
   accumulated time), a customer figure (lit hero art or SVG sprite fallback, tinted by class)
   walks in → stops at a shelf slot → **shelf-slot highlight** on the judged item → **emote
   bubble**: bought-cheap → heart; bought-fair → smile; passed-unaffordable → frown+head-shake
   (X eyes glyph); passed-no-upgrade → shrug — glyphs drawn code-side (simple shapes), no art
   dependency → exits: bought = item icon bobbing above head; passed = slower slump walk.
3. **Coin flourish:** on each `ItemSold`, coin glyph arcs (parabolic hop tween) from stage to the
   StatusBar gold readout; gold label does a bounce-scale pop (1.0→1.25→1.0, Trans.Elastic 0.3s)
   instead of snapping. MainUi change confined to StatusBar region; `StatusLabel` name untouched.
4. End-of-day sales strip already exists in ledger — do NOT build a new tally screen (Bodega #6
   deferred; ledger owns that).

Tests: stage builds, N sold events → N customer runs queued, emote kind mapping table pinned,
gold-pop tween property assertions, ShopPanel existing asserts untouched.

### LW4 — atmosphere layer (Worker B)

All inside `LitTownOverlay` + new `AmbientFxLayer.cs` (child of LitWorld):

1. **Two-temperature palette:** retune phase stops — night/dusk cooler + desaturated (reference
   stops: dusk `(0.59,0.66,0.78)`, night `(0.30,0.32,0.55)`-ish — NOT below the crush point;
   Evening current `(0.45,0.45,0.70)` is close, push contrast) so warm pools pop. Keep TintFor
   table shape (tests read it).
2. **Window glow:** emissive additive `Sprite2D` overlays (`CanvasItemMaterial.BlendMode=Add`,
   soft radial texture generated code-side via `GradientTexture2D` — no art dependency) positioned
   per building, `Visible`/alpha ramped by phase (on at dusk/night, off Morning). Testable headless.
3. **Hero landmark:** forge coals get the one strongest extra PointLight2D + ember burst.
4. **CPUParticles2D set** (recipes with the researched values):
   chimney smoke per building (rise, grow, fade), forge sparks (orange ramp, gravity 200, burst
   feel), fireflies at dusk/night (turbulence, alpha-hump blink, additive), dust motes daytime
   (tiny, near-zero gravity, alpha ≤0.4). All CPUParticles2D (headless-simulatable), `Vector3`
   direction/gravity gotcha (Z=0).
5. **Props layer:** place committed props art — town-well, noticeboard, ore-cart, market-crates,
   string-lanterns, laundry-line, tavern-cat, forge-salamander — as lit sprites where art exists
   (null-tolerant). Lanterns + laundry get sway (`Rotation = sin(t)*0.05` at ~0.4Hz, per-prop phase
   offset).
6. **Fog wisp drift:** 2 large soft noise sprites panned by accumulated delta with modulo wrap,
   alpha ~0.12, dusk/night only.

Tests: overlay builds with fx layer, particle nodes exist + Emitting matches phase, window-glow
visibility flips by phase, prop sprites null-degrade, light count ≤ budget.

### LW5 — depths watch (Worker D)

`MineWatch.cs` — lit strip (SubViewport pattern, ~1024×260) at top of DepthsPanel, live during
Expedition/ExpeditionDeep/Camp phases (else collapsed):

1. `mine-backdrop` art scrolls slowly (accumulated-delta pan) while a party is underground.
2. Party figures (lit hero art, class-tinted) march in file with walk bob; torch PointLight2D
   circle around the leader; ambient dark-cool CanvasModulate.
3. Floor milestones: when `FloorRecordSet`/attribution beats arrive at phase tick, brief monster
   silhouette (committed monster art, dark-modulated) slides past + record bark label.
4. Camp phase: march halts, campfire (CPUParticles2D embers + warm light), tent-less huddle —
   reads `PartyCampReport` (hp fractions → slumped vs upright pose offsets).
5. Graceful degrade: no art → panel behaves exactly as today (strip hidden).

Tests: strip visibility per phase, march state machine, camp halt on camp report, degrade path.

### LW6 — camera drift + transitions (Worker B, after LW4)

1. **Idle drift:** `Camera2D` in LitViewport, `Offset = (sin(t*0.10), cos(t*0.13)) * 4px` —
   barely-conscious motion. `MakeCurrent` inside the SubViewport only.
2. **Mouse parallax:** lit-world layers offset by `(mouse - center) * 0.02–0.04`, lerped — do NOT
   move the camera for this; offset background/props layers at differing factors (depth).
3. **Tab fade:** 0.12s ColorRect alpha dip on tab switch (CanvasLayer 100). MainUiTests-safe:
   purely additive node, no tab structure change. (Skip click-zoom for now — Moonlight Peaks
   anti-pattern: forced zoom annoyed players; revisit post-V4b.)

### LW-art — asset parity (Worker E, GPU tiller)

Through the committed pipeline (specs → ComfyUI MCP gen → cutout.py → normalmap.py → import on
pinned engine → manifest + LFS):

1. **Lit hero figures ×3 missing classes:** occultist, sentinel, skirmisher (+`_n`) — neutral-base
   per the tint-in-engine rule, match existing hero-vanguard/striker/mystic style (seed/prompt
   provenance in build JSON).
2. **`shop-interior`** — 1024×220-ish shelf-wall strip for LW3's stage (warm wood, empty shelf
   slots readable).
3. Particle/glow textures are code-generated (GradientTexture2D / drawn) — NOT SDXL work; only gen
   the two categories above. Keep total new weight sane (LFS, ~1.5MiB/pair).

Spec-first (art/specs/town or heroes namespace as registry dictates), conformance tests, then gen.
If ComfyUI VRAM squeezed by Ollama, `clear_vram` first / sequence runs.

## Sequencing + integration

1. All five workers start parallel (LW1, LW3, LW4, LW5, LW-art). LW2 after LW1; LW6 after LW4.
2. Rebase discipline: whoever is stale rebases; no shared files across lanes by construction.
3. After each merge: orchestrator refreshes `C:\Code\Game\play` (fetch, detach to origin/main,
   build, import pre-pass, boot-verify) and captures per-tab screenshots; visual regressions bounce
   back to the owning lane.
4. LW3/LW5 consume LW-art ids when they land but MUST ship green with graceful degrade before art
   arrives (null-tolerant `Art()` lookups — same rule the overlay already follows).

## Risks

- **Node-pin breakage** — biggest CI risk; mitigations baked into unit specs (new names only).
- **Event delivery shape:** choreography reads `LastEvents` at phase completion; if an event class
  arrives only inside the Evening ledger flow, worker verifies against `SimAdapter.LastEvents`
  stamping (map: adapter stamps post-AdvancePhase — confirmed) before building on it.
- **Determinism:** cosmetic-only accumulated-delta animation is outside golden-replay scope (sim
  untouched); keep it that way — no presentation writes to sim, no engine RNG.
- **Perf:** CPUParticles2D counts modest (≤64/emitter); fog sprites 2; lights ≤8 town-wide.

## Out of scope (explicit)

- V4b Node2D full migration + V5b (Godot 4.7.1 gate — unchanged).
- Audio/music (parked per repo ruling), new sim mechanics (haggling, theft), new prose generation,
  runtime LLM anything, camera click-zoom.
