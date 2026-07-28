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

    /// <summary>
    /// The five venues <c>Town3D.BuildingLayout</c> places (forge/market/tavern/minegate/
    /// noticeboard) — the "forge-station"/"counter-station" pair is deliberately OUT for this
    /// slice (scope boundaries: no diegetic in-world minigame stations tonight, see the pivot
    /// plan's "NOT tonight" list); the drawer panels already carry that gameplay verb via the
    /// venue interior route. Tiles are hand-placed clear of each other's ~4-tile interact
    /// footprint, inside the grid, with the mine gate north (low Y, toward the "wilds") and the
    /// rest clustered around a southern town square (mirrors <c>Town3D</c>'s "gate departs north,
    /// square/market/tavern south" read).
    /// </summary>
    public static readonly VenueLayout[] Venues =
    {
        new("forge", "Forge", "forge", new Vector2I(9, 15)),
        new("market", "Shop", "market", new Vector2I(27, 15)),
        new("tavern", "Tavern", "tavern", new Vector2I(9, 21)),
        new("minegate", "Gate", "mine-gate", new Vector2I(19, 4)),
        new("noticeboard", "Bounties", "noticeboard", new Vector2I(29, 21)),
    };

    /// <summary>Where departing heroes rally (dwell as a cluster) before marching to the mine
    /// gate's own <see cref="Building2D.DoorAnchorGlobal"/> — the open town-square tile roughly
    /// between the venues and the gate, mirroring <c>Town3D.RallySpotFor</c>'s "near the gate,
    /// spread along one axis" read.</summary>
    public static readonly Vector2I RallyTile = new(19, 10);

    /// <summary>Tile coordinate → world-space pixel position of that tile's CENTER. Buildings are
    /// positioned by their Y-sort line (see <see cref="Building2D.Configure"/>'s remarks) at this
    /// same convention — one flat conversion used for every placement (venues, rally point, hero
    /// wander homes) so nothing drifts out of the tile grid by a stray pixel offset.</summary>
    public static Vector2 TileToWorld(Vector2I tile) =>
        new(tile.X * TileSize + TileSize / 2f, tile.Y * TileSize + TileSize / 2f);
}
