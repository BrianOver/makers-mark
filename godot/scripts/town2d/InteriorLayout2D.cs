using System.Collections.Generic;
using System.Linq;
using Godot;

namespace GodotClient.Town2d;

/// <summary>
/// U1 (painted-interiors plan, KTD-3): the declarative venue → walkable-room table — the
/// "carry-forward asset" <c>town.InteriorStage.Venues</c>'s own doc named for exactly this moment,
/// harvested into the walkable form the owner actually asked for. A fresh venue's room is a table
/// row + art (<see cref="InteriorRoom2D"/> builds whatever this table declares), never a new code
/// path (KTD-3).
///
/// <para><b>U1, world-and-interiors plan (docs/plans/2026-08-02-004):</b> the forge-interior plan's
/// slice 1 shipped the <c>"forge"</c> row only. This unit adds three more — <c>"market"</c>,
/// <c>"tavern"</c>, <c>"minegate"</c> — as loud-placeholder rows on the exact same island pattern
/// (KTD-1: a new venue is a table row, not new code — no <c>Town2D</c> change was needed to prove
/// it). Only <c>"noticeboard"</c> keeps today's drawer-on-interact behavior (KTD-2: a plank board
/// has no inside).</para>
///
/// <para><b>Sprite ids are PINNED here on purpose</b> (plan text, U1): forge —
/// <c>town2d-station-anvil</c>/<c>-furnace</c>/<c>-bellows</c>/<c>-quench</c>/<c>-shelf</c>/
/// <c>-rack</c>, shell <c>town2d-forge-interior-shell</c>. World-and-interiors U1 pins twelve more
/// (four per new room) plus three shells — see each room's own inline comments below — so U2/U3/U4
/// (the real pixel art, one unit per room) can be authored in parallel against ids that never
/// change. Do not rename them.</para>
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

    /// <summary>U1 (world-and-interiors plan): +512Y from the forge lane — same X (2048, one lane),
    /// stacked vertically so every room's island clears every OTHER room's, not just the town
    /// (KTD-1's "distinct island offsets so no camera clamp can ever see two rooms").</summary>
    private static readonly Vector2 MarketRoomOffset = new(2048f, 512f);

    /// <summary>320×192px (plan text: 20×12 tiles) at <see cref="TownLayout2D.TileSize"/>=16.</summary>
    private static readonly Vector2I MarketRoomSizeTiles = new(20, 12);

    /// <summary>Bottom edge, horizontally centered (mirrors <see cref="ForgeDoorTile"/>'s convention).</summary>
    private static readonly Vector2I MarketDoorTile = new(10, 11);

    private static readonly Vector2 TavernRoomOffset = new(2048f, 1024f);

    /// <summary>352×208px (plan text: 22×13 tiles).</summary>
    private static readonly Vector2I TavernRoomSizeTiles = new(22, 13);

    private static readonly Vector2I TavernDoorTile = new(11, 12);

    private static readonly Vector2 GatehouseRoomOffset = new(2048f, 1536f);

    /// <summary>288×176px (plan text: 18×11 tiles).</summary>
    private static readonly Vector2I GatehouseRoomSizeTiles = new(18, 11);

    private static readonly Vector2I GatehouseDoorTile = new(9, 10);

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
            // U1 (world-and-interiors plan): the market room. ShopPanel has no FocusSection (unlike
            // ForgePanel) at the time this row was authored, so counter/shelf-a/shelf-b all open a
            // plain Shop with no Focus — the plan's "Focus stock if ShopPanel grows a section anchor
            // else plain Shop" resolves to the "else" branch today. The ledger's action would be
            // "Ledger" per the plan text IF that modal route existed in MainUi's action vocabulary;
            // it doesn't (Ledger is a HUD tray button, not an OnInteriorHotspotActivated case), so
            // the plan's own "else flavor" branch applies here too.
            new(
                "market",
                "town2d-market-interior-shell",
                MarketRoomSizeTiles,
                MarketRoomOffset,
                MarketDoorTile,
                new[]
                {
                    new StationSpec("counter", "Sales Counter", "town2d-station-market-counter", new Vector2I(10, 6), "Shop"),
                    new StationSpec("shelf-a", "Display Shelf", "town2d-station-market-shelf", new Vector2I(5, 3), "Shop"),
                    new StationSpec("shelf-b", "Display Shelf", "town2d-station-market-shelf", new Vector2I(14, 3), "Shop"),
                    new StationSpec("ledger", "Ledger Desk", "town2d-station-market-ledger", new Vector2I(3, 8), Action: null,
                        HoverLine: "Ledger desk — the books live in the day-end tally, not here",
                        FlavorLine: "You flip through the ledger. Nothing to buy or sell from these pages — try the counter."),
                    new StationSpec("crates", "Stock Crates", "town2d-station-market-crates", new Vector2I(16, 9), Action: null,
                        HoverLine: "Stock crates — whatever's for sale is already out on the shelf",
                        FlavorLine: "Crates of unsorted stock. Nothing here you can buy directly."),
                }),
            // U1 (world-and-interiors plan): the tavern room. "storywall" routes to the EXISTING
            // "Legends" action (MainUi.OnInteriorHotspotActivated already special-cases it for the
            // Legends Wall modal — no new plumbing needed).
            new(
                "tavern",
                "town2d-tavern-interior-shell",
                TavernRoomSizeTiles,
                TavernRoomOffset,
                TavernDoorTile,
                new[]
                {
                    new StationSpec("hearth", "Hearth", "town2d-station-tavern-hearth", new Vector2I(11, 2), Action: null,
                        HoverLine: "Hearth — keeps the room warm, nothing to work here",
                        FlavorLine: "The hearth crackles. Warm, but there's nothing to craft or buy from a fire."),
                    new StationSpec("bar", "The Bar", "town2d-station-tavern-bar", new Vector2I(4, 6), "Tavern"),
                    new StationSpec("storywall", "Story Wall", "town2d-station-tavern-storywall", new Vector2I(18, 6), "Legends"),
                    // U6 (world-and-interiors plan, follow-up): these tiles double as patron seating
                    // anchors — kept as plain data here, no seating logic in this unit.
                    new StationSpec("table-a", "Patron Table", "town2d-station-tavern-table", new Vector2I(8, 9), "Tavern"),
                    new StationSpec("table-b", "Patron Table", "town2d-station-tavern-table", new Vector2I(14, 9), "Tavern"),
                }),
            // U1 (world-and-interiors plan, KTD-2): the gatehouse — "everything about the mine
            // happens at the gate." "overlook" is the ONE new action string this unit adds:
            // "Watch" → MainUi.OnInteriorHotspotActivated routes it straight to Mirror.ShowMirror();
            // during non-live phases the Mirror already renders its own "nobody below" empty state,
            // so no extra plumbing is needed here for that case.
            new(
                "minegate",
                "town2d-gatehouse-interior-shell",
                GatehouseRoomSizeTiles,
                GatehouseRoomOffset,
                GatehouseDoorTile,
                new[]
                {
                    new StationSpec("overlook", "The Overlook", "town2d-station-gate-overlook", new Vector2I(9, 2), "Watch"),
                    new StationSpec("muster", "Muster Board", "town2d-station-gate-muster", new Vector2I(5, 5), "Depths"),
                    new StationSpec("bountyledger", "Bounty Ledger", "town2d-station-gate-bounty", new Vector2I(12, 5), "Bounties"),
                    new StationSpec("winch", "Gate Winch", "town2d-station-gate-winch", new Vector2I(9, 7), Action: null,
                        HoverLine: "Gate winch — raises the portcullis, nothing to manage from here",
                        FlavorLine: "The winch's chain hangs taut. It just raises the gate — try the muster board or the bounty ledger."),
                }),
        };

        return rooms.ToDictionary(r => r.VenueKey);
    }
}
