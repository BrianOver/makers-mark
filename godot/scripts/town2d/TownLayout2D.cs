using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U1: static venue→grid table for the 2.5D town — the 2D twin of <c>town3d.Town3D
/// .BuildingLayout</c>'s <c>Vector3</c> placement, keyed to <b>tile</b> coordinates instead. Keys
/// reuse <c>Town3D.Building3D.Key</c>'s exact vocabulary ("forge"/"market"/"tavern"/"minegate"/
/// "noticeboard") so <c>Town2D.FindBuilding</c> stays a drop-in for every existing caller, and
/// <see cref="Building2D.Key"/>'s single flat key space (U3, confirmed in
/// <c>Building2DInteractionTests</c>) means the SAME string is what <c>Town2D.BuildingClicked</c>
/// re-emits — MainUi's <c>OnTownBuildingClicked</c> switch (currently keyed on Town3D's
/// capitalized <c>ClickKey</c> strings, e.g. "Forge"/"Shop") needs its case labels lower-cased to
/// this vocabulary at the U2 cutover; see <c>Town2D</c>'s own class doc for the flagged
/// reconciliation note.
/// </summary>
public static class TownLayout2D
{
    /// <summary>Ground tile edge length, px (pivot plan pixel-discipline: 16×16 tiles).</summary>
    public const int TileSize = 16;

    /// <summary>
    /// Uniform downscale applied to every CHARACTER sprite (player, heroes, townsfolk) — the one
    /// place the cast's world scale is decided.
    ///
    /// <para>Why 1.0 and not some fraction: the world-scale proportion this constant used to
    /// encode (a blacksmith standing 60% the height of his own forge was Brian's "buildings are too
    /// small and the player model is massive" verdict — the buildings were never the wrong dial)
    /// is now baked into the committed PNGs themselves by <c>tools/art/gen_town_sprites.py</c>,
    /// which ships every character sprite already at its on-screen pixel size. This constant stays
    /// as the one place to retune that proportion later (against a screenshot) without touching a
    /// single PNG.</para>
    ///
    /// <para><b>2026-08-12 (asymmetric-decimation fix).</b> This used to be 0.5, applied at RUNTIME
    /// to a double-size source PNG, on the theory that a Nearest-filtered GPU sampler with mipmaps
    /// off would do a "clean 2:1 decimation" at draw time. It didn't: Nearest keeps exactly one
    /// column/row out of every mirrored pair, chosen by pixel-grid alignment, not by the art — a
    /// bilaterally-symmetric silhouette came out visibly lopsided on screen (measured on the real
    /// committed sprites: every mirror-symmetric row broke symmetry, by up to half its width), and a
    /// single-pixel authored accent (visor slit, rune, coolant trace, shield boss) was a coin flip
    /// to survive at all. The fix moved the halving OFFLINE into the generator (see its own
    /// <c>rarity_downsample_2x</c> doc) — the committed PNG already IS the on-screen pixel grid, so
    /// this is 1.0 (a pure pass-through) and there is no runtime decimation left to get wrong. See
    /// <c>CastProportionTests.NoRuntimeDecimation_CharacterSpriteScaleStaysOne</c> for the
    /// regression pin — reintroducing any value other than 1.0 here without ALSO re-baking the
    /// source art at that new scale reproduces this bug.</para>
    ///
    /// <para>Applied via <see cref="CharacterArtRoot"/> — an intermediate node between the actor and
    /// its sprite — rather than by re-generating the PNGs on every retune, so this stays one number
    /// to tune against a screenshot. Deliberately NOT folded into the per-frame <c>Sprite.Scale</c>
    /// assignment: <c>SpriteMotion</c> owns that value for walk squash/breath, and the
    /// feet-compensation offset math in every actor's pose-apply inverts <c>Sprite.Scale</c> to keep
    /// the feet planted — an extra constant factor in there silently breaks that inversion (it made
    /// the player's sprite drift 11px off its own feet line). Keeping the constant on the parent
    /// leaves the pose math, and the invariant tests that check it, exactly as they were.</para>
    /// </summary>
    public const float CharacterSpriteScale = 1.0f;

    /// <summary>Builds the per-actor node that carries <see cref="CharacterSpriteScale"/>: parent the
    /// actor's <c>Sprite2D</c> to this instead of to the actor itself. Its origin coincides with the
    /// actor's, so the sprite's feet-at-origin convention (and therefore the Y-sort baseline) is
    /// unchanged; it only scales what hangs below it. Nothing outside the town2d actors depends on
    /// the sprite's node PATH, so this extra level is invisible to the rest of the client.</summary>
    public static Godot.Node2D CharacterArtRoot() => new()
    {
        Name = "Art",
        Scale = new Godot.Vector2(CharacterSpriteScale, CharacterSpriteScale),
    };

    /// <summary>Town grid extent in tiles (pivot plan: "town grid ≈40×28") — width deliberately
    /// equals the 640px viewport width exactly (no horizontal camera pan needed), height (28 ×
    /// 16 = 448px) exceeds the 360px viewport so the vertical <see cref="Camera2D"/> limit has
    /// somewhere to pan between the mine gate (north) and the town square (south).</summary>
    public const int GridWidth = 40;

    public const int GridHeight = 28;

    /// <summary>One venue's placement: the tile its Y-sort line (front-door row) sits on, plus the
    /// sprite id <see cref="TownAssets2D"/> resolves through the art manifest/fallback ladder.
    ///
    /// <para><b>U7 (world-and-interiors plan, KTD-3):</b> <see cref="Nametag"/> is a STATIC
    /// default only — the workshop ("forge") venue's REAL nametag follows the player's selected
    /// profession(s) via <see cref="WorkshopVocab.NametagFor"/>, resolved at build/rebuild time in
    /// <c>Town2D.BuildBuildings</c> (which has the live <c>GameState</c> this struct never does).
    /// This table has no GameState access, so its "forge" row keeps reading "Forge" — the
    /// blacksmith default any GameState-free consumer of <see cref="Venues"/> still sees. The
    /// venue KEY itself ("forge") never changes (KTD-3(b): it is load-bearing across <c>MainUi</c>
    /// routing, quick-travel, and the tutorial's <c>StepBuilding</c>).</para>
    /// </summary>
    public readonly record struct VenueLayout(string Key, string Nametag, string SpriteId, Vector2I Tile);

    /// <summary>One static prop's placement: sprite id (resolved via <see
    /// cref="TownAssets2D.ForProp"/>), the tile its feet-origin sits on (same <see
    /// cref="TileToWorld"/> convention buildings use), whether it needs Y-sorting against
    /// heroes/the player (true for anything tall enough to be walked in front of/behind — a well,
    /// lantern post, or tree; a flush ground decal would pass false and mount under <see
    /// cref="Town2D.Ground"/> instead, though this slice has none), and a uniform render <see
    /// cref="Scale"/> (default 1f — every entry above this comment ships pre-sized art and needs
    /// none).
    ///
    /// <para><b>U4 (asset-completion wave):</b> <see cref="Scale"/> exists because the eight
    /// <c>props-*</c> "warm-hub" entries below are raw ~800-1024px SDXL cutouts (<c>art/build/
    /// props-*.build.json</c> — background-removed and bbox-trimmed by <c>cutout.py</c>, never
    /// resized down), unlike every prop art committed before them (the 32px well, 8-24px lantern/
    /// tree/crate) which already ships at its on-screen pixel size. Mounting one of those at
    /// <see cref="Town2D.BuildProps"/>'s native <c>Sprite2D</c> size would draw a sprite several
    /// SCREENS wide on the 640×360 world viewport — exactly the "placed by coordinate arithmetic
    /// alone, shipped visibly wrong, property assertions passing the whole time" failure shape
    /// this repo has hit before. <see cref="Town2D.BuildProps"/> applies this multiplier to the
    /// node itself (after computing the feet-anchor <c>Offset</c> from the texture's raw,
    /// unscaled size), so the offset and the visible footprint scale down together and the feet
    /// stay planted on <see cref="Tile"/> regardless of the source art's native resolution.</para>
    /// </summary>
    public readonly record struct PropLayout(string SpriteId, Vector2I Tile, bool YSorted, float Scale = 1f);

    /// <summary>
    /// The five venues <c>Town3D.BuildingLayout</c> places (forge/market/tavern/minegate/
    /// noticeboard) — the "forge-station"/"counter-station" pair is deliberately OUT for this
    /// slice (scope boundaries: no diegetic in-world minigame stations tonight, see the pivot
    /// plan's "NOT tonight" list); the drawer panels already carry that gameplay verb via the
    /// venue interior route. Tightened into a cozy cluster around a central plaza (~tile 20,15,
    /// see <see cref="PathRects"/>'s plaza rect): forge NW, market NE, tavern SW, noticeboard SE,
    /// each one tile clear of the plaza/road cobble network via a short spur, with the mine gate
    /// north up the road (mirrors <c>Town3D</c>'s "gate departs north, square/market/tavern south"
    /// read, just pulled in tight instead of spread corner-to-corner). Tiles are hand-placed clear
    /// of each other's sprite footprint (see each <c>TownAssets2D.VenuePlaceholders</c> size, which
    /// matches the real generated-art pixel dimensions 1:1).
    ///
    /// <para><b>These draw the pre-pivot SDXL set</b> (2026-08-01 building-exterior receipt —
    /// see <c>runs/receipts/</c>, options A-D, and <c>art/pipeline/recolor-forge-roof.py</c>'s own
    /// doc for the full trace). #316 swapped every venue to the <c>town2d-*</c> pixel set to kill
    /// the Forge's magenta roof (#520051), which was real — but the owner's playtest verdict was
    /// "the buildings look WORSE, we only asked for interior changes": he never asked for an
    /// exterior swap and prefers this SDXL look. Measuring both sets confirms the pixel set's
    /// regression is real too (its shared structural palette runs 0.14-0.35 saturation vs this
    /// set's 0.16-0.41 — see <c>art/pipeline/boost-town2d-palette.py</c>'s own doc for the numbers),
    /// so #316's fix and the owner's complaint are BOTH correct about their own building set. This
    /// keeps the set he prefers and fixes the one thing that was actually broken in it: forge.png's
    /// roof is recoloured terracotta (sampled from tavern.png's own shingles) by
    /// <c>art/pipeline/recolor-forge-roof.py</c>, nothing else about the SDXL set changed.
    /// Footprints use the real PNG dimensions (forge 72×81, market 76×62, tavern 84×88, gate
    /// 48×48, noticeboard 44×50) at the SAME tile coordinates #316 used — verified clear: forge
    /// spans world-Y 119-200, tavern 208-296 (8px gap); market spans Y 138-200, noticeboard
    /// 246-296; the mine gate (Y8-56) shares no row with either.</para>
    ///
    /// <para><b>Switching options</b> (all four were rendered and compared, see the receipt):
    /// back to the <c>town2d-*</c> pixel set (options A/C) — edit the five
    /// <see cref="VenueLayout.SpriteId"/> strings below to their <c>town2d-*</c> equivalents AND mirror the same five keys in
    /// <see cref="TownAssets2D.VenuePlaceholders"/> (that committed set is already boosted —
    /// Option C, <c>art/pipeline/boost-town2d-palette.py</c> — not the original muddy one the
    /// owner rejected). Back to the unrecoloured magenta roof (option B) — overwrite
    /// <c>godot/assets/art/forge.png</c> with the frozen pre-fix copy at
    /// <c>art/pipeline/sources/forge-sdxl-magenta.png</c>.</para>
    /// </summary>
    public static readonly VenueLayout[] Venues =
    {
        new("forge", "Forge", "forge", new Vector2I(13, 12)),
        new("market", "Shop", "market", new Vector2I(26, 12)),
        new("tavern", "Tavern", "tavern", new Vector2I(13, 18)),
        // U2 (playtest-three plan): was "Gate" — bare enough to read as generic scenery next to a
        // "Bounties" building that actually opens a DIFFERENT panel (Depths). Nametag only; the
        // click-routing key ("minegate") and TownAssets2D's sprite id are untouched.
        new("minegate", "Mine Gate", "mine-gate", new Vector2I(20, 3)),
        new("noticeboard", "Bounties", "noticeboard", new Vector2I(26, 18)),
    };

    /// <summary>Where departing heroes rally (dwell as a cluster) before marching to the mine
    /// gate's own <see cref="Building2D.DoorAnchorGlobal"/> — sits ON the road (see <see
    /// cref="PathRects"/>) between the plaza and the gate, mirroring <c>Town3D.RallySpotFor</c>'s
    /// "near the gate, spread along one axis" read.</summary>
    public static readonly Vector2I RallyTile = new(20, 9);

    /// <summary>
    /// Cobble tile rects (<see cref="Town2D.BuildGround"/> paints every cell inside these over the
    /// grass base, using the atlas's existing Cobble coord) forming one connected network: a
    /// central plaza, a north road up to the mine gate, and a short spur from each building's
    /// door-front tile into the plaza/road — replaces the old "one path tile per venue door"
    /// decoration with an actual walkable-reading street layout. Rects deliberately overlap by a
    /// tile at each junction (redundant repaints, not a bug) so the network reads as continuous
    /// rather than as disjoint tile confetti.
    /// </summary>
    public static readonly Rect2I[] PathRects =
    {
        // Central plaza: x15-25, y13-17 (11×5 tiles), framing the well/lantern props below.
        new(15, 13, 11, 5),
        // North road: plaza's top edge (y13) up to the mine gate's door-front tile (20,4).
        new(19, 4, 2, 9),
        // Forge spur: its door-front tile (13,13) into the plaza's west edge (x15).
        new(13, 13, 3, 1),
        // Market spur: its door-front tile (26,13) into the plaza's east edge (x25).
        new(25, 13, 2, 1),
        // Tavern spur: plaza's south edge (y17) down to its door-front tile (13,19).
        new(13, 17, 1, 3),
        // Noticeboard spur: plaza's south edge (y17) down to its door-front tile (26,19).
        new(26, 17, 1, 3),
    };

    /// <summary>
    /// Static decoration: a well anchoring the plaza center, lanterns flanking the plaza corners
    /// and each building's door, trees framing the map's open edges/corners, and a couple of
    /// crates beside the market — all placed on open grass clear of every venue footprint and the
    /// cobble network (props carry no collision, so this is a legibility/framing choice, not a
    /// pathing constraint). <see cref="Town2D.BuildProps"/> instantiates each entry.
    /// </summary>
    public static readonly PropLayout[] Props =
    {
        new("town2d-well", new Vector2I(20, 15), true),

        // Lanterns: plaza's four corners, then flanking each building's door-front tile.
        new("town2d-prop-lantern", new Vector2I(16, 13), true),
        new("town2d-prop-lantern", new Vector2I(24, 13), true),
        new("town2d-prop-lantern", new Vector2I(16, 17), true),
        new("town2d-prop-lantern", new Vector2I(24, 17), true),
        new("town2d-prop-lantern", new Vector2I(12, 13), true),
        new("town2d-prop-lantern", new Vector2I(27, 13), true),
        new("town2d-prop-lantern", new Vector2I(12, 19), true),
        new("town2d-prop-lantern", new Vector2I(27, 19), true),

        // Trees: framing the map's open edges/corners, well clear of the central cluster.
        new("town2d-prop-tree", new Vector2I(2, 2), true),
        new("town2d-prop-tree", new Vector2I(10, 2), true),
        new("town2d-prop-tree", new Vector2I(30, 2), true),
        new("town2d-prop-tree", new Vector2I(37, 2), true),
        new("town2d-prop-tree", new Vector2I(2, 8), true),
        new("town2d-prop-tree", new Vector2I(37, 8), true),
        new("town2d-prop-tree", new Vector2I(2, 18), true),
        new("town2d-prop-tree", new Vector2I(37, 18), true),
        new("town2d-prop-tree", new Vector2I(2, 25), true),
        new("town2d-prop-tree", new Vector2I(10, 25), true),
        new("town2d-prop-tree", new Vector2I(30, 25), true),
        new("town2d-prop-tree", new Vector2I(37, 25), true),

        // Crates: a couple stacked just east of the market's footprint.
        new("town2d-prop-crate", new Vector2I(29, 11), true),
        new("town2d-prop-crate", new Vector2I(29, 13), true),

        // U4 (asset-completion wave, docs/design/ASSETS.md "warm-hub town props"): eight
        // committed, resolution-tested (ArtWiringCoverageTests.TownProps_ResolveWithNormal)
        // props that nothing ever drew until this table. Every tile below was checked clear of
        // every venue footprint (Venues above) and every walkable lane TownLayout2D.PathRects
        // carries INTO a building — the plaza square itself (PathRects[0]) is exempted from that
        // check, matching this file's own established precedent (the well sits dead center of
        // it, corner lanterns flank it): a wide-open square tolerates a decoration, a 1-2-tile
        // spur does not. Scale values bring each ~800-1024px source down to a footprint sized
        // relative to this file's existing prop ladder (8px lantern .. 32px well .. 88px
        // tavern) — tuned against a real rendered frame (tools/receipt.ps1), not guessed.

        // A market yard, north of its footprint and east of the mine-gate road — clear of the
        // market's own spur and the well/lantern cluster in the plaza.
        new("props-market-crates", new Vector2I(30, 9), true, 0.0298f),

        // A second, informal flyer board over by the market — NOT the same object as the
        // "noticeboard" VENUE key below (that key is the Bounties building at (26,18), a
        // different system entirely; see this class's own U4 doc note on the name collision).
        new("props-noticeboard", new Vector2I(22, 10), true, 0.0265f),

        // Festival garland over the top of the plaza, clear of the north road and every spur.
        new("props-string-lanterns", new Vector2I(17, 12), true, 0.0410f),

        // Ore cart parked in the yard behind the forge's west wall.
        new("props-ore-cart", new Vector2I(9, 11), true, 0.0396f),

        // The forge's own pet, curled by the coals — one column further west than the cart/
        // laundry line (not just a row apart): a first receipt at (9,13), sharing their column
        // with only 2 tiles of vertical gap, read as one cluttered blob on screen (the
        // salamander's small silhouette sat inside the laundry line's own footprint) — moving it
        // sideways instead of just further down gives it a horizontally clear read regardless of
        // the laundry line's height.
        new("props-forge-salamander", new Vector2I(7, 13), true, 0.0154f),

        // Laundry strung in the backyard gap between the forge and the tavern.
        new("props-laundry-line", new Vector2I(9, 15), true, 0.0393f),

        // Napping on the tavern's south side, clear of its door column (x=13).
        new("props-tavern-cat", new Vector2I(15, 19), true, 0.0130f),

        // A second, older well near the tavern/noticeboard row — duplicates the existing
        // "town2d-well" prop above in SUBJECT (this class's own U4 doc note flags this as a
        // genuine open question, not a deliberate two-wells design call).
        new("props-town-well", new Vector2I(17, 19), true, 0.0287f),
    };

    /// <summary>Tile coordinate → world-space pixel position of that tile's CENTER. Buildings are
    /// positioned by their Y-sort line (see <see cref="Building2D.Configure"/>'s remarks) at this
    /// same convention — one flat conversion used for every placement (venues, rally point, hero
    /// wander homes, props) so nothing drifts out of the tile grid by a stray pixel offset.</summary>
    public static Vector2 TileToWorld(Vector2I tile) =>
        new(tile.X * TileSize + TileSize / 2f, tile.Y * TileSize + TileSize / 2f);
}
