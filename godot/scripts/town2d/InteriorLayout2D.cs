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
/// <para><b>U3 — honest differentiation.</b> A station's <see cref="StationSpec.Action"/> is now
/// nullable: non-null means "this station opens a real, tested surface" (Anvil/Furnace/Shelf →
/// <c>Forge</c>, optionally with <see cref="StationSpec.Focus"/> telling <c>ForgePanel.FocusSection</c>
/// which section to land on; Rack → <c>Shop</c>). <c>null</c> means "honest flavor" (Bellows/Quench):
/// no verb exists, so pressing E must never silently do nothing — <see cref="StationSpec.HoverLine"/>
/// is shown instead of the usual "E · {Label}" prompt (never promising an interact it does not have),
/// and <see cref="StationSpec.FlavorLine"/> is the one-line toast <c>MainUi</c> shows on press. Both
/// must be set whenever <see cref="StationSpec.Action"/> is null — <c>InteriorRoomTests
/// .EveryStationAction_IsARecognizedMainUiRoute_NeverADeadClick</c> fails loudly on a flavor row
/// missing either, the same "never a dead click" contract the Action check enforces.</para>
/// </summary>
public static class InteriorLayout2D
{
    /// <summary>One physical station inside a room: its own stable id (nametag/lookup — NOT the
    /// click-key, since several stations can share one <paramref name="Action"/>), display label
    /// (the HUD "E · {Label}" prompt), sprite id (<see cref="TownAssets2D.ForStation"/>), the LOCAL
    /// tile position within the room's own grid, and the action string it opens on press — or
    /// <see langword="null"/> for an honest flavor station (see the class doc's U3 paragraph).
    /// <paramref name="Focus"/> is Forge-only (<c>ForgePanel.FocusSection</c>'s section key, e.g.
    /// "materials"/"craft"); <paramref name="HoverLine"/>/<paramref name="FlavorLine"/> are flavor-only
    /// (required together whenever <paramref name="Action"/> is null, forbidden when it is not).</summary>
    public readonly record struct StationSpec(
        string Id,
        string Label,
        string SpriteId,
        Vector2I Tile,
        string? Action,
        string? Focus = null,
        string? HoverLine = null,
        string? FlavorLine = null);

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
                    // U3: Anvil/Furnace both open the craft flow (the minigames live behind it) —
                    // Focus "craft" lands ForgePanel on the recipe cards.
                    new StationSpec("anvil", "Anvil", "town2d-station-anvil", new Vector2I(12, 7), "Forge", Focus: "craft"),
                    new StationSpec("furnace", "Furnace", "town2d-station-furnace", new Vector2I(6, 5), "Forge", Focus: "craft"),
                    // U3: honest flavor — no verb, a hover line while proximate and a one-line
                    // toast on press, never a dead click pretending to be a station with a job.
                    new StationSpec("bellows", "Bellows", "town2d-station-bellows", new Vector2I(8, 5), Action: null,
                        HoverLine: "Old bellows — feeds the furnace, nothing to work here",
                        FlavorLine: "You give the bellows a pump. The furnace does the real work."),
                    new StationSpec("quench", "Quench Trough", "town2d-station-quench", new Vector2I(15, 7), Action: null,
                        HoverLine: "Quench trough — the anvil handles the real quenching",
                        FlavorLine: "The water ripples. Nothing to craft here — try the anvil."),
                    // U3: the shelf is the vendor/materials half of the Forge panel, not craft.
                    new StationSpec("shelf", "Material Shelf", "town2d-station-shelf", new Vector2I(4, 10), "Forge", Focus: "materials"),
                    new StationSpec("rack", "Finished Goods", "town2d-station-rack", new Vector2I(19, 10), "Shop"),
                }),
        };

        return rooms.ToDictionary(r => r.VenueKey);
    }
}
