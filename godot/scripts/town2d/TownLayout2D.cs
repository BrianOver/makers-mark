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

    /// <summary>Town grid extent in tiles (pivot plan: "town grid ≈40×28") — width deliberately
    /// equals the 640px viewport width exactly (no horizontal camera pan needed), height (28 ×
    /// 16 = 448px) exceeds the 360px viewport so the vertical <see cref="Camera2D"/> limit has
    /// somewhere to pan between the mine gate (north) and the town square (south).</summary>
    public const int GridWidth = 40;

    public const int GridHeight = 28;

    /// <summary>One venue's placement: the tile its Y-sort line (front-door row) sits on, plus the
    /// sprite id <see cref="TownAssets2D"/> resolves through the art manifest/fallback ladder.</summary>
    public readonly record struct VenueLayout(string Key, string Nametag, string SpriteId, Vector2I Tile);

    /// <summary>One static prop's placement: sprite id (resolved via <see
    /// cref="TownAssets2D.ForProp"/>), the tile its feet-origin sits on (same <see
    /// cref="TileToWorld"/> convention buildings use), and whether it needs Y-sorting against
    /// heroes/the player (true for anything tall enough to be walked in front of/behind — a well,
    /// lantern post, or tree; a flush ground decal would pass false and mount under <see
    /// cref="Town2D.Ground"/> instead, though this slice has none).</summary>
    public readonly record struct PropLayout(string SpriteId, Vector2I Tile, bool YSorted);

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
    /// matches the real generated-art pixel dimensions 1:1) — verified by hand: forge's 4×5-tile
    /// footprint (x11-15,y7-12) and tavern's 3.5×4.5-tile footprint (x11.25-14.75,y13.5-18) share
    /// no row; market's 4×4 (x24-28,y8-12) and noticeboard's 2×3 (x25-27,y15-18) likewise don't.
    /// </summary>
    public static readonly VenueLayout[] Venues =
    {
        new("forge", "Forge", "forge", new Vector2I(13, 12)),
        new("market", "Shop", "market", new Vector2I(26, 12)),
        new("tavern", "Tavern", "tavern", new Vector2I(13, 18)),
        new("minegate", "Gate", "mine-gate", new Vector2I(20, 3)),
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
    };

    /// <summary>Tile coordinate → world-space pixel position of that tile's CENTER. Buildings are
    /// positioned by their Y-sort line (see <see cref="Building2D.Configure"/>'s remarks) at this
    /// same convention — one flat conversion used for every placement (venues, rally point, hero
    /// wander homes, props) so nothing drifts out of the tile grid by a stray pixel offset.</summary>
    public static Vector2 TileToWorld(Vector2I tile) =>
        new(tile.X * TileSize + TileSize / 2f, tile.Y * TileSize + TileSize / 2f);
}
