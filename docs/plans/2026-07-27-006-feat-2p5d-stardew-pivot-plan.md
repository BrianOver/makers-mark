# 2.5D Stardew Pivot — Implementation Plan

> **STATUS: NEARLY COMPLETE — one unit outstanding (stamped 2026-07-28).** U1-U7 shipped (#244-#249); the game now boots `Town2D` (`godot/scripts/MainUi.cs:960`). U8, the teardown of the old 3D layer, was never run: all 16 files in `godot/scripts/town3d/` are dead code that roughly 17 test files still exercise on every CI run. Note that `godot/scripts/panels/MonsterView3D.cs` is NOT dead — it still renders gen monsters inside BestiaryPanel and MineWatch. Close this plan by finishing U8.

---

*Date: 2026-07-27. Branch: `feat/2.5d-stardew` (off main @ #239). Abandon the non-working 3D gen render layer; rebuild `godot/` as 2.5D Stardew-style pixel art (top-down 3/4, tile-based, Y-sorted). The pure C# sim (`sim/GameSim/`) and `SimAdapter.cs` stay 100% untouched. Source: read-only scout + Fable architecture pass. Scope tonight: vertical slice — walkable pixel town + heroes + clickable buildings that open the existing drawer panels = the full craft→sell→raid loop.*

## Goal Capsule

Replace `godot/scripts/town3d/` (14 files) with `godot/scripts/town2d/` exposing the **same public surface** `Town3D` exposes, then flip one constructor at `MainUi.cs:853`. The Control-based HUD/panel layer, `SimAdapter`, and the whole sim survive byte-identical. The drawer panels (Forge/Shop/Tavern/Board/…) already carry every gameplay verb via `Adapter.Queue`/`AdvancePhase` — the town just has to be a legible, clickable, alive 2D stage.

## Hard rules (from CLAUDE.md — enforce)

- Sim purity KTD2: `sim/GameSim/` gets ZERO edits. This pivot is `godot/`-only.
- Determinism KTD5: golden replay safe by construction (no sim touch). Keep all tween/animation timing in `_Process`, off the sim path.
- `project.godot` is deny-listed / orchestrator-only AND has no [rendering]/[display]/[input] sections — set ALL pixel-perfect + input config at runtime in code (the pattern `TownInput.cs` already uses for InputMap). Never edit project.godot.
- Godot 4.6.3 pin. Conventional commits, no `git add .`, one unit = one small change.
- Fast lane green before done: `dotnet test sim/GameSim.Tests/GameSim.Tests.csproj --filter Category!=Balance`.

## THE Town2D CONTRACT (mirror Town3D verbatim — MainUi depends on all of it)

`MainUi.cs:853-859` and elsewhere call these on the town. `Town2D` MUST expose the full surface or MainUi won't compile:

```csharp
namespace GodotClient.Town2d;
public partial class Town2D : Control {
    public event System.Action<string>? BuildingClicked;   // click-key vocabulary (forge/shop/tavern/board/mine/...)
    public event System.Action<int>? HeroClicked;          // hero id
    public void Build(GodotClient.SimAdapter adapter);     // MainUi.cs:856 — paint ground, place buildings, spawn player, Bind, ReconcileHeroes
    public void Bind(GodotClient.SimAdapter adapter);
    public void Refresh();                                  // => ReconcileHeroes()
    public GodotClient.PhaseClock? Clock { set; }           // MainUi.cs:857 — no-op setter OK (Town3D's is)
    public void OnPhaseCompleted(DayPhase completedPhase);  // moves heroes Rallying->WalkingOut etc.
    public void SetWorldInputEnabled(bool enabled);
    public void ForgeGlow(int heatPermille); public void ForgeGlowReset();
    public void ForgeSparkBurst(); public void ForgeSteamPlume();
    public Building2D FindBuilding(string key);
    public int HeroActorCount(); public HeroActor2D FirstHeroActor();
    public void ReconcileHeroes();
}
```
FX methods may be working-or-stub (modulate/particle, or empty body) so MainUi compiles — but they must exist. **Before touching MainUi, grep it for every `Town.` call and ensure the contract covers each.**

## Node architecture

Root `Town2D : Control` (FullRect, added first so it draws under the HUD). Inside: `SubViewportContainer (TextureFilter=Nearest) → SubViewport (640×360, Snap2DTransformsToPixel) → World : Node2D`. Integer upscale by the container (×2/×3) — NEVER fractional-zoom the Camera2D (kills pixel art). A **2D** SubViewport does NOT trip the 3D-headless-render-hang KTD.

```
Town2D : Control (FullRect)
└── SubViewportContainer (stretch, TextureFilter=Nearest)
    └── SubViewport (640×360, Snap2DTransformsToPixel=true, Snap2DVerticesToPixel=true, CanvasItemDefaultTextureFilter=Nearest)
        └── World : Node2D
            ├── Ground     : TileMapLayer (16px tiles; grass/path/cobble; no Y-sort)
            ├── YSort      : Node2D { YSortEnabled = true }   ← the 2.5D trick
            │   ├── Buildings : Building2D per venue
            │   ├── Player    : PlayerController2D
            │   └── Heroes    : HeroActor2D per hero
            ├── Fx         : Node2D (forge glow modulate target, spark bursts)
            ├── CanvasModulate (dusky purple ~#b9a3d0 — carries the purple-dusk mood)
            └── Cam        : Camera2D (PositionSmoothing ~8, Limit* = town rect)
```

**Pixel/camera discipline:** 16×16 tiles, town grid ≈40×28. Camera2D `Zoom=One` (container upscales). All filter/snap set at runtime in `Town2D._Ready()` (project.godot is off-limits). The "3/4 look" is an ART convention (buildings drawn with visible front faces + roof tops over straight-top-down ground), not a camera transform.

**Y-sort = the whole trick:** one YSortEnabled Node2D holds buildings+player+heroes; every sprite's origin sits at its **feet/base line** via `Sprite2D.Offset`. Buildings sort by their **front-door row**, not sprite-center. Get `Building2D.Configure` right once → heroes correctly walk behind/in-front. A Y-sort assertion guards it (U5 test) + a human eyeball before "done".

**Venue→grid:** `TownLayout2D` static table maps venue-key → tile coord + sprite id (replaces Town3D's Vector3 placement). Keys reuse Town3D's `FindBuilding` vocabulary so MainUi's `OnTownBuildingClicked` switch is unchanged. DoorAnchor = tile in front of door; heroes rally there.

## Class-by-class disposition of `godot/scripts/town3d/`

| File | Verdict | 2D equivalent |
|---|---|---|
| Town3D.cs | REWRITE | `Town2D.cs` (~400L, full contract; drops navbake/WorldEnvironment/gen-props) |
| CameraRig.cs | DROP | inline `Camera2D` in Town2D |
| PlayerController.cs | PORT | `PlayerController2D : CharacterBody2D` (WASD via TownInput, MoveAndSlide, straight-line click-move, rect CollisionShape2D on buildings) |
| Building3D.cs | PORT | `Building2D : Node2D` (Sprite2D + Area2D pick + `RaisePick()` seam + Label nametag + `Marker2D DoorAnchor` + `SetHighlighted`; keep Key/ClickKey/Configure w/ Vector2) |
| BuildingKit.cs | DROP | sprite lookup in TownAssets2D |
| TownAssets.cs | REWRITE(small) | `TownAssets2D.cs` (venue-key→Texture2D via IconRegistry/art-manifest, SVG fallback per key) |
| HeroActor3D.cs | PORT(mechanical) | `HeroActor2D : Node2D` (state machine Wandering/Rallying/WalkingOut/Away/WalkingIn back to Vector2; keep HeroIdValue, RaisePick, class-color Sprite2D) |
| TownsfolkNpcs.cs | DROP(slice) | later |
| WorldDressing.cs | DROP(slice) | 2-3 static Sprite2Ds placed by Town2D |
| AmbientLife.cs | DROP(slice) | later `AmbientLife2D` CPUParticles2D |
| MineZone/MineApproach.cs | DROP(slice) | Mine = one cave-mouth Building2D at north edge; heroes WalkingOut path to it, despawn |
| InteriorRoom3D.cs | DROP | resurrect `town/InteriorStage.cs` (already Control 2D) later |
| WorldInput3D.cs | REWRITE(small) | `WorldInput2D.cs` (E-interact on Area2D overlap, Esc-close; Area2D handles clicks natively) |
| TownInput.cs | KEEP AS-IS | move into town2d/ (runtime InputMap trick is load-bearing) |

Namespace `GodotClient.Town2d`. Delete `town3d/` only in the final teardown unit, after 2D CI is green.

## Vertical-slice asset manifest (programmer-art FIRST, gen swaps in)

PNGs → `godot/assets/art/`, ids in `art-manifest.json` via `art/pipeline/gen-manifest.ps1`, loaded via `IconRegistry.Art(name)`. New tiles under `godot/assets/tilesets/` (TileSet built in code, no .tres churn). Every build unit codes against manifest ids with a wired **SVG/flat programmer-art fallback**, so NO unit blocks on gen.

| Asset | Size | Count | Fallback (ships the slice) |
|---|---|---|---|
| Ground tileset (grass/path/cobble, autotile) | 16×16 | ~24 (1 sheet) | flat 2-tone tiles |
| Smith forge/shop (hero building) | 64×80 | 1 (+night) | rasterized `assets/sprites/forge.svg` |
| Tavern / commission board / well | 48×64,32×32,32×32 | 3 | colored-rect + roof-triangle + Label nametag |
| Cave mouth (mine) | 48×48 | 1 | dark arch |
| Player smith | 16×24, single 3/4 facing (walk later) | 1-8 | static facing sprite |
| Heroes vanguard/striker/mystic | 16×24 | 3 | tinted body (class color = modulate) |
| Props lantern/tree/crate | var | 3 | skip; lanterns = PointLight2D |

Gen = ComfyUI, orchestrator-run, **ONE job at a time**, GPU hard limits (≥14GB free, abort >14GB/>83°C). Character sheets are highest junk-risk — hand-pixel the player walk if gen sheets are inconsistent. Gen output = drop-in file replacement, zero code dependency.

## Playtest harness

Survives untouched: `UiTestSupport.cs`, both recorders (Control-tree level), all panel/HUD tests. **Never push synthetic input into the SubViewport** (recorded dead-end) — tests call `building.RaisePick()`/`heroActor.RaisePick()` and assert `BuildingClicked`/`HeroClicked`. Town2D test-shape mirrors `Town3DSceneTests.cs`:
```csharp
var town = new Town2D(); AddNode(town);
town.Build(new SimAdapter(seed: 42));
AssertThat(town.FindBuilding("forge")).IsNotNull();
AssertThat(town.HeroActorCount()).IsEqual(expectedFromState);
```
- **2D rewrites (7):** Town2DSceneTests, HeroActor2DTests (state machine — port 1:1), Building2DInteractionTests, PlayerController2DTests, phase-transition/reconcile, forge-FX no-crash, ClickKey-vocabulary (Town2D emits keys MainUi's switch expects).
- **Delete with subjects (13):** CameraRigTests, MineZone*Tests, InteriorRoom3DTests, MonsterView3DTests, ShopStageTests(3D), gen-asset/GLB tests, navmesh — deleted in the SAME teardown PR as `town3d/`.
- **Smoke test (slice proof):** mount Town2D+Bind → ground TileMapLayer has cells + ≥4 buildings → advance phases until a hero enters WalkingOut → RaisePick forge → assert BuildingClicked("forge"). Renders trivially headless (retires the 3D hang).

## Build units (subclaude fleet — disjoint files, one worktree `feat/2.5d-stardew`)

| # | Unit | Owns exclusively | Deps | Verify |
|---|---|---|---|---|
| U1 | Town2D skeleton + contract | `godot/scripts/town2d/Town2D.cs`, `TownAssets2D.cs`, `TownLayout2D.cs` | — | Town2DSceneTests builds town, 4+ buildings, events wired |
| U2 | MainUi swap (orchestrator, serial) | `godot/scripts/MainUi.cs` (line 853 region) | U1 | game boots to town; fast lane green |
| U3 | Building2D + WorldInput2D | `town2d/Building2D.cs`, `WorldInput2D.cs`, `tests/Building2DInteractionTests.cs` | U1 contract | RaisePick→BuildingClicked green |
| U4 | HeroActor2D port | `town2d/HeroActor2D.cs`, `tests/HeroActor2DTests.cs` | U1 contract | state-machine tests green; hero walks out |
| U5 | PlayerController2D + TownInput | `town2d/PlayerController2D.cs`, `town2d/TownInput.cs`, `tests/PlayerController2DTests.cs` | U1 contract | WASD moves; Y-sort behind/in-front assert |
| U6 | Programmer-art fallback pack | `godot/assets/sprites/2d/*`, `art-manifest.json` additions | — (parallel w/ U1) | IconRegistry resolves every Town2D id |
| U7 | Gen-art batch (orchestrator, GPU rules) | `godot/assets/art/` new PNGs only | U6 ids | visual pass; no code change |
| U8 | Teardown (orchestrator, serial) | delete `town3d/*`, coupled tests; stub 3 panel SubViewport strips to portraits | U2-U5 | full fast lane + engine tests green; zero `Town3d` refs |

**Sequencing:** U1+U6 start immediately, parallel. U3/U4/U5 build against U1's contract (given the signatures above they can run in the same batch — orchestrator resolves any signature mismatch at integration). U2 is the integration gate (orchestrator, after U1+U3+U4+U5 compile). U7 orchestrator-serial after U6. U8 last, atomic with test deletions.

**Fallback ladder:** U1+U2+U6 alone = a walkable programmer-art town with clickable buildings + the full loop via panels. That already beats the broken 3D pipeline and is playtestable.

## Risks

1. **Y-sort feet-origin** (heroes floating over roofs) — one Configure convention (sort=door row) + U5 assert + human eyeball.
2. **Pixel drift/blur** — all filter/snap centralized in `Town2D._Ready()`; integer container scale only; never touch project.godot.
3. **Gen junk** — de-risked by U6-before-U7 ordering; slice never art-blocked.
4. **20 test rewrites gating CI** — Town3D + its tests stay green until U8 deletes both sides atomically; new 2D tests are additive.
5. **MainUi hidden couplings** — U1 contract includes every `Town.` method as working-or-stub; U2 owner greps first.
6. **Determinism** — no sim touch; safe by construction.

## Scope boundaries (NOT tonight)

MineZone/MineApproach, InteriorRoom3D replacements, TownsfolkNpcs, AmbientLife particles, ground decals, the MonsterView3D/ShopStage/MineWatch viewport strips (keep running against 3D until U8 stubs to portraits — must not gate the slice), 4-dir walk animation, navmesh pathing.
