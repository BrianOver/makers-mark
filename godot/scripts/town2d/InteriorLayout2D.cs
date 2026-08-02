using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U1 (painted-interiors plan, KTD-3): the declarative venue → walkable-room table — the
/// "carry-forward asset" <c>town.InteriorStage.Venues</c>'s own doc named for exactly this moment,
/// harvested into the walkable form the owner actually asked for. A fresh venue's room is a table
/// row + art (<see cref="InteriorRoom2D"/> builds whatever this table declares), never a new code
/// path (KTD-3). Slice 1 (this plan) ships the <c>"forge"</c> row only — R9: every other venue
/// keeps today's drawer-on-interact behavior until its own row lands here.
///
/// <para><b>Sprite ids are PINNED here on purpose</b> (plan text, U1): <c>town2d-station-anvil</c>/
/// <c>-furnace</c>/<c>-bellows</c>/<c>-quench</c>/<c>-shelf</c>/<c>-rack</c>, shell
/// <c>town2d-forge-interior-shell</c> — so U2 (the real pixel art) can be authored in parallel
/// against ids that never change. Do not rename them.</para>
///
/// <para><b>Action strings</b> (KTD-3) reuse the EXACT vocabulary <c>MainUi.OnInteriorHotspotActivated</c>
/// already routes ("Forge"/"Shop"/"Tavern"/"Bounties"/"Bestiary"/"Legends" → <c>OpenPanel</c> or a
/// code-built modal) — never a new routing concept. <c>InteriorRoomTests
/// .EveryStationAction_IsARecognizedMainUiRoute_NeverADeadClick</c> fails loudly if a row here ever
/// names an action nothing knows how to open (this repo's recurring "dead click" failure class).</para>
///
/// <para><b>Bellows/Quench route to "Forge" for now, deliberately.</b> The plan calls these two
/// "flavor, see U3" — U3 differentiates them into an honest hover-only response (never pretending to
/// be a verb). Until U3 lands, routing them to the SAME real, tested Forge panel the anvil/furnace
/// open is a genuine verb (never a dead click) rather than inventing a half-finished flavor action
/// this unit does not own building. U3 changes exactly these two rows.</para>
/// </summary>
public static class InteriorLayout2D
{
    /// <summary>One physical station inside a room: its own stable id (nametag/lookup — NOT the
    /// click-key, since several stations can share one <paramref name="Action"/>), display label
    /// (the HUD "E · {Label}" prompt), sprite id (<see cref="TownAssets2D.ForStation"/>), the LOCAL
    /// tile position within the room's own grid, and the action string it opens on press.</summary>
    public readonly record struct StationSpec(string Id, string Label, string SpriteId, Vector2I Tile, string Action);

    /// <summary>One venue's walkable room: which venue it answers for, the shell sprite id, the
    /// room's size in tiles, its island offset in WORLD pixels (KTD-1 — a far-off region of the same
    /// <c>Town2D.World</c>, off every town camera frame), the door tile (room-local, bottom edge —
    /// <see cref="InteriorRoom2D"/> spawns the player one tile north of it and gaps the perimeter
    /// wall there), and the station table.</summary>
    public readonly record struct RoomSpec(
        string VenueKey,
        string ShellSpriteId,
        Vector2I SizeTiles,
        Vector2 WorldOffset,
        Vector2I DoorTile,
        StationSpec[] Stations);

    /// <summary>Island offset (KTD-1: "e.g. +2048px in X — off every town camera frame"). The town
    /// grid is <see cref="TownLayout2D.GridWidth"/>×16 = 640px wide, so 2048px clears it with a wide
    /// margin — no camera clamp on either side can ever see both regions in the same frame.</summary>
    private static readonly Vector2 ForgeRoomOffset = new(2048f, 0f);

    /// <summary>384×224px (KTD-5's shell size) at <see cref="TownLayout2D.TileSize"/>=16.</summary>
    private static readonly Vector2I ForgeRoomSizeTiles = new(24, 14);

    /// <summary>Bottom edge, horizontally centered.</summary>
    private static readonly Vector2I ForgeDoorTile = new(12, 13);

    public static readonly IReadOnlyDictionary<string, RoomSpec> Rooms = BuildRoomTable();

    private static IReadOnlyDictionary<string, RoomSpec> BuildRoomTable()
    {
        RoomSpec[] rooms =
        {
            new(
                "forge",
                "town2d-forge-interior-shell",
                ForgeRoomSizeTiles,
                ForgeRoomOffset,
                ForgeDoorTile,
                new[]
                {
                    new StationSpec("anvil", "Anvil", "town2d-station-anvil", new Vector2I(12, 7), "Forge"),
                    new StationSpec("furnace", "Furnace", "town2d-station-furnace", new Vector2I(6, 5), "Forge"),
                    new StationSpec("bellows", "Bellows", "town2d-station-bellows", new Vector2I(8, 5), "Forge"),
                    new StationSpec("quench", "Quench Trough", "town2d-station-quench", new Vector2I(15, 7), "Forge"),
                    new StationSpec("shelf", "Material Shelf", "town2d-station-shelf", new Vector2I(4, 10), "Forge"),
                    new StationSpec("rack", "Finished Goods", "town2d-station-rack", new Vector2I(19, 10), "Shop"),
                }),
        };

        return rooms.ToDictionary(r => r.VenueKey);
    }
}
