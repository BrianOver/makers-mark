# Design — 3D town hub + interaction vertical slice

**Date:** 2026-07-20
**Status:** approved-in-principle (brainstorm); pending written-spec review
**Origin:** Brian's gate(b) Godot playtest. Screenshots (`play/playTest_Images/Screenshot 2026-07-20 1602*.jpg`)
show the town as painted sprites floating on a bare debug grid, a placeholder rect player, a stretched
interior painting, and broken menu sizing. Directive: stop the custom-painted-image approach; build a
**real, grounded environment** (Stardew / Moonlight Peaks feel) with **working world interaction**;
**generic models only** for now; **pause custom image generation**.

## Decisions locked (brainstorm)

1. **Render approach:** true **3D low-poly** (Node3D world), not 2D grounded sprites.
2. **Assets:** free **Kenney CC0** low-poly kit(s) — buildings/props + characters. New asset dependency,
   license-clean (CC0, no attribution obligation; safe for Fornida's compliance posture), committed via
   Git LFS with a provenance/CREDITS note. No custom-generated art; the art pipeline is paused for this work.
3. **Movement:** WASD (camera-relative) **+ click-to-move** (walk-then-open, as today).
4. **First slice:** town hub + interaction + menu-sizing fix in one PR. Interiors, gate walk-out
   spectating, and per-venue theming deferred to PR2+.

## Goal

Replace the 2D floating-sprite town hub with a grounded 3D low-poly town where the player avatar walks
(WASD + click), buildings read as real structures on real ground, it is **obvious what is interactable
and how**, and interacting opens the existing action panels. Fix the menu-sizing defects the playtest
surfaced. All presentation — **zero sim change** (KTD2 sim purity holds).

Non-goal (this slice): 3D interiors, hero-walk-out-the-gate cinematics, ambient particle richness,
final art. Interiors keep the existing `InteriorStage` 2D overlay + panels.

## Architecture

### Boundary (unchanged, reused verbatim)
- `SimAdapter` — plain C#, zero Godot types: `CurrentState`, `Queue`, `LastEvents`, `LastRejections`,
  `AdvancePhase`, `StateChanged`. The 3D town is a pure consumer.
- `PhaseClock` — plain C# living clock; unchanged.
- `DrawerHost` + `scenes/panels/*.tscn` + `MainUi.OpenPanel(id)` — the panel system. Opened by string id
  (`Forge|Shop|Heroes|Tavern|Depths|Bounties|Town`), no town coupling. **Reused unchanged.**
- `InteriorStage` — 2D `Control` overlay; still staged on venue entry this slice (no 3D interior yet).

### Scene host (minimal MainUi change)
`main_ui.tscn` root is a `Control`; `MainUi.BuildUi()` adds a base `Town` child, then 2D HUD/drawer
siblings draw over it. Today `Town` = `town_scene.tscn` (2D). **Change:** `Town` becomes a
`SubViewportContainer` (FullRect, stretch) → `SubViewport` hosting the **3D** world + `Camera3D`. This
mirrors the existing `LitTownOverlay` SubViewport pattern, so the 2D HUD/drawer overlay stack is
untouched — only the base child's contents change from Node2D to Node3D.

- **`SubViewport.PhysicsObjectPicking = true`** is mandatory (U25 lesson: the 2D town shipped with this
  off and building clicks only worked in tests). The 3D slice must set it, and an engine test must assert it.
- SubViewport is `HandleInputLocally = true`; input routes to the world for WASD/click, HUD stays on top.

### 3D scene tree (new)
```
Town3D (SubViewportContainer, FullRect, stretch)
└── SubViewport (PhysicsObjectPicking = true, HandleInputLocally = true)
    └── World (Node3D)
        ├── Ground (MeshInstance3D: PlaneMesh + tiled cobble/dirt material; StaticBody3D floor collider)
        ├── DirectionalLight3D + WorldEnvironment (simple ambient; phase-tinted later)
        ├── Camera3D (fixed pitch ~50°, perspective, smoothed follow rig on the player)
        ├── Buildings (Node3D)
        │   └── Building_{key} (Node3D)               # forge / market / tavern / minegate
        │       ├── MeshInstance3D (Kenney building mesh, feet on ground)
        │       ├── StaticBody3D + CollisionShape3D    # footprint only — walk behind roof, bump walls
        │       ├── Area3D "Interact_{key}" + CollisionShape3D  # proximity zone
        │       ├── Label3D (billboard: "FORGE")       # readable without art
        │       └── Highlight (material emission / outline mesh, toggled on proximity)
        ├── PlayerAvatar (CharacterBody3D)
        │   ├── MeshInstance3D (Kenney character mesh)
        │   ├── CollisionShape3D (capsule at feet)
        │   └── blob shadow (decal or flattened mesh)
        └── Heroes (Node3D)
            └── HeroActor3D (Node3D per alive hero: character mesh + Area3D pick zone + name Label3D)
```

### Reuse / replace / adapt
- **Reuse unchanged:** `SimAdapter`, `PhaseClock`, `DrawerHost`, `panels/*.tscn` + panel scripts,
  `MainUi.OpenPanel` routing, `InteriorStage`.
- **Replace:** `LitTownOverlay`, `HeroActor`, `PlayerAvatar`, `AmbientFxLayer`, the SubViewport-2D +
  Camera2D + Y-sort rendering. (3D gives real depth — Y-sort is gone.)
- **Adapt:** `TownScene` keeps its sim-reconciliation (`ReconcileSprites`, memorial plot, phase
  choreography `OnPhaseCompleted`) and its `BuildingClicked` / `HeroClicked` events; only the scene-graph
  calls swap from Node2D to Node3D. The string vocabulary (`Forge|Shop|Tavern|Gate` → `OnTownBuildingClicked`)
  is preserved so `MainUi` routing is untouched.

## Interaction model (the priority)

A player must always know **what** is interactable and **how**.

1. **Input** (runtime-registered, as today via `WorldInput` — no `project.godot [input]` edit): `move_*`
   (WASD/arrows), `interact` (E), `cancel` (Esc), quick-travel 1–4.
2. **Proximity affordance:** when the avatar enters a building's `Area3D`, that building **highlights**
   (emission/outline) and a **prompt** shows: `E · Forge`. Leaving clears both. Only one active target
   at a time (nearest wins), mirroring the current `InteractionZone` single-target rule.
3. **Two triggers, same result** (preserve today's vocabulary):
   - **Press E** in a zone → raise `BuildingClicked("Forge")`.
   - **Click** a building (Area3D picked by camera ray) → `RequestMoveTo(door)`, store `_pendingOpenKey`,
     fire on arrival (`PathCompleted`) — the existing walk-then-open (KTD12).
4. `BuildingClicked` → `TownScene` relays → `MainUi.OnTownBuildingClicked` → `OpenInterior`/`OpenPanel`
   (unchanged). Hero click → `OpenPanel("Heroes")` (unchanged).
5. **Prompt rendering:** a small screen-space label anchored near the avatar (or a billboard `Label3D`
   above the building). Chosen for legibility over the 3D scene; container-safe so it never clips.

## Menu-sizing fixes (2D UI — independent of the 3D work)

Root causes identified in the current code; each fix is container/theme-level, no absolute offsets.

1. **OBJECTIVE panel collapses to ~1 char wide** — `ObjectiveTracker` (PanelContainer) width comes only
   from a `CustomMinimumSize=(320,0)` set on `MainUi`, but it is docked with `LayoutPresetMode.Minsize`,
   which snapshots the (near-zero, autowrap) content width at call time. **Fix:** give the tracker a real
   established width — set `CustomMinimumSize` on the tracker itself (not just the host), stop using the
   `Minsize` preset snapshot (anchor top-right with fixed left/right offsets from a known 320 width), and
   give the autowrap `Reason` label a min width so it reports a sane minimum.
2. **Phase timeline reads as run-on text** (`Morning Expedition Camp Deep Evening`) — the `DayTimeline`
   HBox has no `separation` and no segment styling. **Fix:** add container separation + per-phase segment
   styling (chip/pill with the active phase emphasized), via `GameTheme`.
3. **HUD right controls + "Skip" clip off-screen** — the header row has two `ExpandFill` siblings
   (StatChips, Timeline) plus a fixed six-control cluster with no width budget, and there is no
   `[display]` window size, so at the actual window width the right controls overflow. **Fix:** budget the
   header (cap/limit the ExpandFill regions, give the control cluster a min width and clamp the right-anchored
   overlays to the viewport; allow the control cluster to wrap or shrink gracefully). Prefer a code/layout
   fix over editing the deny-listed `project.godot`; if a window default size genuinely helps, that is a
   flagged `project.godot` change (deny-listed) called out separately, not bundled silently.

## Assets — Kenney CC0

- Packs (GLB/GLTF, Godot-native import): a low-poly **town/medieval building** kit + a **character** kit.
  Exact packs chosen at implementation time from kenney.nl (all CC0). Placed under
  `godot/assets/models/kenney/<pack>/`, `.glb` LFS-tracked, with a `CREDITS.md` recording pack name,
  URL, CC0.
- **Dependency flag:** this is a new asset dependency (external download, repo size via LFS). Approved in
  brainstorm. The download step is an outward network fetch — called out in the plan, not silent.
- If a suitable building shape is missing, fall back to a primitive (box/wedge) mesh + label — never a
  custom-painted texture (pipeline paused).

## Determinism & purity

- 3D town draws no RNG and reads no wall clock; motion uses accumulated `delta` (as the current
  `HeroActor`/`PlayerAvatar` do). Sim stays the single source of truth; the town only renders
  `SimAdapter.CurrentState` and re-emits action strings.
- Engine pin: Godot **4.6.3-stable .NET** only. `godot/GodotClient.csproj` keeps `net10.0`.

## Testing

- **gdUnit4Net engine tests** (`godot/tests`): SubViewport `PhysicsObjectPicking` is on; a building
  Area3D raises the correct `BuildingClicked` key; proximity highlight toggles on enter/exit; the ported
  hero/avatar motion state machine is deterministic (same inputs → same transitions); menu-sizing:
  ObjectiveTracker resolves to its intended width and the timeline has non-zero separation.
- **Fast lane** (`sim` xUnit) stays green — no sim change.
- **Manual verify:** build + run Godot 4.6.3 headless import, launch, screenshot the town + an open panel;
  confirm nothing floats, interaction prompt shows, menus sized. Attach before/after.

## Scope boundary (PR1)

**In:** 3D grounded town (ground, lit, camera), Kenney buildings + player + heroes, proximity highlight +
prompt, E + click-to-move interaction opening existing panels, menu-sizing fixes, engine tests.
**Out (PR2+):** 3D interiors (keep `InteriorStage` overlay), gate walk-out spectating in 3D, ambient
particles/weather, phase-tinted lighting polish, per-venue 3D theming, final art pass.

## Risks

- **3D-in-Control embedding**: SubViewportContainer sizing/stretch + input routing can be fiddly; verify
  picking + HUD-overlay input priority early.
- **Kenney kit fit**: medieval/fantasy building shapes may be sparse; primitive fallback keeps the slice
  unblocked.
- **Deny-list**: `project.godot` is deny-listed; keep input runtime-registered and fix clipping in layout
  to avoid touching it. Any unavoidable edit is a flagged, separate change.
- **Deprecating the 2D town**: `LitTownOverlay`/`AmbientFxLayer`/2D actors become dead code — remove them
  in this PR (no orphans, per repo rules) rather than leaving both.
