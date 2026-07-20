# TownWorld scale reference (U14)

Published BEFORE U19/U20/U21/U22 (Wave 3+) start, per plan
`docs/plans/2026-07-19-002-feat-world-rework-plan.md` U14. This is the single source of truth
for world-space pixel constants in the promoted town — any add-on unit placing a sprite, an
Area2D, or a StaticBody2D in `godot/scripts/town/` reads its numbers here instead of guessing.

## World container

- **Design size:** `1600 x 700` px (`LitTownOverlay`'s `SubViewport.Size`). The
  `SubViewportContainer` stretches this 1:1 into whatever screen space the Town tab gets — the
  design space itself never changes with window size.
- **Ground line:** world **Y = 480**. This is the shared street baseline every facade's feet
  anchor sits on (KTD6) and the band heroes wander in front of.
- **Wander band:** heroes roam world X in `[300, 1300]`, Y in `[460, 600]` — the street in front
  of the four facades, never behind them.

## Y-sort

- `LitTownOverlay`'s `Ents` node (`Node2D`, `YSortEnabled = true`) is the ONE sort group: hero
  actors and building wrappers are its direct children, sorted by world Y at draw time (lower Y
  draws first — an actor above the ground line draws behind a facade, below it draws in front).
- Ground, the ambient tint, and the always-on-top `AmbientFxLayer` (window glow / forge coals /
  particles / fog) sit OUTSIDE `Ents` in fixed draw order (ground → Ents → Fx) — the fx layer is
  never Y-sorted against actors; it always reads on top, matching the pre-U14 look.

## Buildings (feet-anchored — KTD6)

Each entry's `Position` is the **ground contact point** (bottom-center of the facade), not its
visual center. `Sprite2D.Centered = false`; the facade renders from
`(-width/2, -height) + AssetCatalog.FeetAnchorOffset(litId)` relative to that point.

| Key | Lit asset id | Click routes to | World X | World Y (ground line) |
|---|---|---|---|---|
| `forge` | `town-forge` | `"Forge"` | 260 | 480 |
| `market` | `town-market` | `"Shop"` | 680 | 480 |
| `tavern` | `town-tavern` | `"Tavern"` | 1100 | 480 |
| `minegate` | `town-mine-gate` | *(none — no click routing, matches pre-U14)* | 1440 | 480 |

- **Target facade width:** ≈300 px (plan's 280–320 px band). Pitch between neighbors is 400–420 px
  — comfortably wider than the target width, so facades cannot overlap (the defect U3 partially
  patched at 230 px pitch / 220 px width is now structurally impossible at these numbers).
- **Colliders:** every building gets a `StaticBody2D` "Base" (a small `RectangleShape2D` hugging
  the ground line) regardless of art presence being required (graceful degrade: no art → no
  facade/collider/click-zone at all, same contract `LitTownOverlay` has always used).
- **Click zones:** `forge`/`market`/`tavern` each get an `Area2D` named `ClickZone_<key>` with a
  `RectangleShape2D` covering roughly the facade footprint, raising `BuildingClicked` with the
  capitalized routing string `MainUi.OnTownBuildingClicked` already switches on. `minegate` has no
  click zone (parity with the pre-U14 gate, which was never clickable either). G1 verdict (BOARD):
  headless Area2D physics picking does not fire in gdUnit4Net — tests drive these zones via
  `UiTestSupport.TryClickArea`, never `ClickWorld`. Real picking is a manual-smoke-only path
  (`UiTestSupport.ManualSmokeRecipe`) until a future gate reopens it.

## Heroes

- **Standing figure height:** ≈96 px (plan's headline number), width ≈68 px — `HeroSprite`'s
  `SpriteWidth`/`SpriteHeight`/`SpriteRise` constants. Feet-anchored the same way the pre-U14
  marker always was: `HeroSprite.Position` IS the ground-contact point; the figure renders
  upward from it (`SpriteRise` above the node's own origin).
- `HeroSprite` stays `Control`-based in U14 (U19 replaces it with a Node2D `HeroActor`) — Y-sort
  works on it anyway because `YSortEnabled`/`y_sort_enabled` lives on `CanvasItem`, the common
  base of `Node2D` and `Control`, so a `Control` child of a `YSortEnabled` `Node2D` still
  participates correctly.

## Props (`AmbientFxLayer`)

- **Target prop width:** ≈56 px (unchanged from LW4). Prop positions are re-plotted for the wider
  1600×700 canvas but the scale constant itself did not change.

## Camera

- `LitTownOverlay.Camera` (`Camera2D`, `AnchorMode = FixedTopLeft`, world-space `Position =
  Vector2.Zero`) is now **the** operative camera for the Town tab (previously it only scoped a
  decorative backdrop SubViewport). LW6 idle drift (`DriftOffsetFor`, ±4 px sine/cosine on
  `Camera2D.Offset`) is the only camera behavior in U14 — no follow target exists until U20 lands
  the player avatar.
- Mouse parallax (`LitTownOverlay.ApplyParallax`) offsets only the `AmbientFxLayer` node itself
  (single factor, 0.03) as an idle depth cue. It deliberately does NOT touch `Ents` — a
  building's `StaticBody2D`/`Area2D` must never drift out of sync with its own visual position.

## What U14 deleted

- The `Control`-based SVG scaffold: `TownScene.BuildGate`/`BuildBuilding` (invisible hit-rect
  `Building_*`/`TownGate` Controls, blinded in U3) and the `Control`-based tiled ground
  `TextureRect`. No `Building_*` `Control` node exists anywhere in the tree post-U14.
  `LitTownOverlay`'s decorative-only `HeroSpec`/`TryAddHero`/`HeroDecorLayer` are also gone — real
  `HeroSprite` actors in `Ents` fill that role now, which was the entire point of the promotion
  (KTD1: promote, don't rebuild a second decorative hero system alongside the real one).

## What's still deferred

- Walk-then-open building interaction (KTD12) — U14 keeps instant-open parity because no avatar
  exists yet to walk. U20 re-pins building clicks as click-to-move-then-open.
- `HeroActor` (Node2D, sim-bound, roster-true) — U19.
- Player avatar + interaction-zone prompts — U20.
