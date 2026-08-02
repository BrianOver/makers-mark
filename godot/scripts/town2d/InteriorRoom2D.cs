using System.Collections.Generic;
using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U1 (painted-interiors plan, KTD-1/KTD-2): builds ONE walkable interior room at its island
/// offset — a far-off region of <c>Town2D</c>'s existing <c>World</c>/<c>SubViewport</c> (no new
/// SubViewport, no hide/show, no reparenting: the town keeps existing off-frame exactly as it does
/// off-camera today). Owns the shell sprite, the perimeter collision walls (gapped at the door
/// tile), and the exit trigger zone — all built in ABSOLUTE WORLD coordinates
/// (<see cref="InteriorLayout2D.RoomSpec.WorldOffset"/> + a local tile), the same convention
/// <c>Town2D</c> itself already uses for venues/props, so nothing here needs a second coordinate
/// space to reason about.
///
/// <para><b>Stations are built here but MOUNTED by <c>Town2D</c>, not by this node.</b> <see
/// cref="Stations"/> exposes the constructed <see cref="Building2D"/> list so <c>Town2D</c> can add
/// them as DIRECT children of its own <c>YSort</c> — the SAME flat Y-sort scope the player and every
/// town building already share. A nested <c>YSortEnabled</c> container here would sort the whole
/// room as ONE blob against the player (Godot sorts a Y-sort-enabled descendant as a single item, by
/// its own transform, not by its children's) instead of station-by-station, which would silently
/// break "walk behind the furnace" the moment real art gives that scene depth. This mirrors
/// <c>Town2D.BuildBuildings</c>'s own <c>BuildingsRoot</c> precedent: a plain (non-Y-sort) wrapper
/// for organization, with the actual sortable content living at the flat scope.</para>
/// </summary>
public partial class InteriorRoom2D : Node2D
{
    /// <summary>One tile (px) — matches <see cref="TownLayout2D.TileSize"/>; kept as a local literal
    /// so this file has no compile-time dependency on that class beyond <see
    /// cref="TownLayout2D.TileToWorld"/> itself.</summary>
    private const int TileSize = TownLayout2D.TileSize;

    /// <summary>Deep below every Y-sorted actor/station — set explicitly (not inferred from add
    /// order) because the room's screen-space never overlaps the town's, so there is nothing to
    /// out-draw except the room's OWN stations, which this must always lose to.</summary>
    private const int ShellZIndex = -100;

    /// <summary>How wide the walkable gap in the bottom wall is, centered on the door tile —
    /// two tiles, wide enough that a straight-line seek (<see cref="PlayerController2D.MoveToTile"/>)
    /// doesn't clip a wall corner on the way through.</summary>
    private const float DoorGapTiles = 2f;

    public Rect2 RoomRect { get; private set; }

    /// <summary>Where <c>Town2D.EnterInterior</c> teleports the player — ONE TILE NORTH of the door
    /// tile, deliberately not ON it: spawning exactly on the door tile would overlap <see
    /// cref="ExitZone"/> immediately and walk the player right back out the frame they just entered
    /// on (Godot's <c>BodyEntered</c> fires on physical overlap, which a fresh spawn already is).</summary>
    public Vector2 DoorAnchorGlobal { get; private set; }

    /// <summary>The walk-out trigger — <c>Town2D</c> wires its <c>BodyEntered</c> signal to <see
    /// cref="Town2D.ExitInterior"/>. Sits ON the door tile, at the same gap the perimeter wall
    /// leaves open, so walking down onto the threshold is what leaves (R4).</summary>
    public Area2D ExitZone { get; private set; } = null!;

    /// <summary>Every station this room built, in <see cref="InteriorLayout2D.RoomSpec.Stations"/>'s
    /// declared order (test visibility: "stations spawn from the table in declared order").</summary>
    public IReadOnlyList<Building2D> Stations => _stations;

    private readonly List<Building2D> _stations = new();

    /// <summary>Raised when a station is picked (E/click) — carries its <see
    /// cref="InteriorLayout2D.StationSpec.Action"/> string directly (already in the vocabulary
    /// <c>MainUi.OnInteriorHotspotActivated</c> routes), not the station's own id. <c>Town2D</c>
    /// re-emits this as its own <c>StationActivated</c> event, mirroring how <see
    /// cref="Building2D.Picked"/> → <c>Town2D.BuildingClicked</c> already works for town buildings.</summary>
    public event System.Action<string>? StationActivated;

    /// <summary>Builds every child fresh — call once per instance (mirrors every other
    /// code-built-node <c>Configure</c>/<c>Build</c> in this codebase).</summary>
    public void Build(InteriorLayout2D.RoomSpec spec)
    {
        Name = $"InteriorRoom_{spec.VenueKey}";

        var sizePx = new Vector2(spec.SizeTiles.X * TileSize, spec.SizeTiles.Y * TileSize);
        RoomRect = new Rect2(spec.WorldOffset, sizePx);

        BuildShell(spec, sizePx);
        BuildWalls(spec, sizePx);
        BuildExitZone(spec);
        BuildStations(spec);

        var entryTile = new Vector2I(spec.DoorTile.X, spec.DoorTile.Y - 1);
        DoorAnchorGlobal = spec.WorldOffset + TownLayout2D.TileToWorld(entryTile);
    }

    private void BuildShell(InteriorLayout2D.RoomSpec spec, Vector2 sizePx)
    {
        var shell = new Sprite2D
        {
            Name = "Shell",
            Texture = TownAssets2D.ForShell(spec.ShellSpriteId, sizePx),
            Centered = false,
            Position = spec.WorldOffset,
            ZAsRelative = false,
            ZIndex = ShellZIndex,
        };
        AddChild(shell);
    }

    /// <summary>Perimeter <see cref="StaticBody2D"/> walls around <paramref name="sizePx"/>, gapped
    /// at the door column (<see cref="DoorGapTiles"/> wide) so the room is fully enclosed except the
    /// one walkable exit — mirrors <see cref="Building2D"/>'s own footprint-collision idiom, just
    /// one rectangle per side instead of one per building.</summary>
    private void BuildWalls(InteriorLayout2D.RoomSpec spec, Vector2 sizePx)
    {
        var origin = spec.WorldOffset;
        var doorCenterX = (spec.WorldOffset + TownLayout2D.TileToWorld(spec.DoorTile)).X;
        var gapHalf = DoorGapTiles * TileSize / 2f;

        var rects = new[]
        {
            new Rect2(origin, new Vector2(sizePx.X, TileSize)), // top
            new Rect2(origin, new Vector2(TileSize, sizePx.Y)), // left
            new Rect2(new Vector2(origin.X + sizePx.X - TileSize, origin.Y), new Vector2(TileSize, sizePx.Y)), // right
            new Rect2(new Vector2(origin.X, origin.Y + sizePx.Y - TileSize), new Vector2(doorCenterX - gapHalf - origin.X, TileSize)), // bottom-left of the door gap
            new Rect2(new Vector2(doorCenterX + gapHalf, origin.Y + sizePx.Y - TileSize), new Vector2(origin.X + sizePx.X - (doorCenterX + gapHalf), TileSize)), // bottom-right of the door gap
        };

        foreach (var rect in rects)
        {
            AddChild(WallSegment(rect));
        }
    }

    private static StaticBody2D WallSegment(Rect2 worldRect)
    {
        var body = new StaticBody2D { Name = "Wall" };
        body.AddChild(new CollisionShape2D
        {
            Name = "WallShape",
            Shape = new RectangleShape2D { Size = worldRect.Size },
            Position = worldRect.Position + worldRect.Size / 2f,
        });
        return body;
    }

    private void BuildExitZone(InteriorLayout2D.RoomSpec spec)
    {
        var doorCenter = spec.WorldOffset + TownLayout2D.TileToWorld(spec.DoorTile);

        ExitZone = new Area2D { Name = "ExitZone", Monitoring = true, Position = doorCenter };
        ExitZone.AddChild(new CollisionShape2D
        {
            Name = "ExitShape",
            Shape = new RectangleShape2D { Size = new Vector2(DoorGapTiles * TileSize, TileSize) },
        });
        AddChild(ExitZone);
    }

    private void BuildStations(InteriorLayout2D.RoomSpec spec)
    {
        foreach (var stationSpec in spec.Stations)
        {
            var station = new Building2D();
            var sprite = TownAssets2D.ForStation(stationSpec.SpriteId);
            var worldPos = spec.WorldOffset + TownLayout2D.TileToWorld(stationSpec.Tile);
            station.Configure(stationSpec.Id, stationSpec.Label, sprite, worldPos);

            var action = stationSpec.Action; // captured per-iteration (C# foreach scoping) — safe
            station.Picked += _ => StationActivated?.Invoke(action);

            _stations.Add(station);
        }
    }
}
