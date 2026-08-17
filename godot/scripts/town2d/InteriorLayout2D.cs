using System.Collections.Generic;
using System.Linq;
using GameSim.Professions;
using Godot;
using GodotClient.Ui;

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
/// which section to land on; Rack → <c>Shop</c>). <c>null</c> means "honest flavor" (Quench):
/// no verb exists, so pressing E must never silently do nothing — <see cref="StationSpec.HoverLine"/>
/// is shown instead of the usual "E · {Label}" prompt (never promising an interact it does not have),
/// and <see cref="StationSpec.FlavorLine"/> is the one-line toast <c>MainUi</c> shows on press. Both
/// must be set whenever <see cref="StationSpec.Action"/> is null — <c>InteriorRoomTests
/// .EveryStationAction_IsARecognizedMainUiRoute_NeverADeadClick</c> fails loudly on a flavor row
/// missing either, the same "never a dead click" contract the Action check enforces.</para>
///
/// <para><b>U5 (verify-by-playing plan, KTD-D) — station identity is DATA.</b> <see cref="Action"/>
/// alone used to BE the route, and two stations naming the same <c>(Action, Focus)</c> pair were
/// byte-identical clicks with nobody able to see it (anvil/furnace, the market's two shelves,
/// alchemy's cauldron/still). <see cref="StationSpec.Verb"/> is the station's own verb — the second
/// half of its route, alongside <see cref="Action"/> — so <c>(Action, Verb)</c> is the tuple
/// <c>StationIdentityTests</c>'s reflective guard asserts is unique across every station in every
/// building (and across every profession's own set, since up to two can be selected and unioned
/// into the shared workshop — <see cref="WorkshopVocab"/>). <see cref="Copy"/> is the station's own
/// on-screen line (<c>MainUi.OnStationActivated</c> toasts it on a real-verb press, the same seam
/// <see cref="FlavorLine"/> already used for flavor presses) — required whenever <see cref="Action"/>
/// is non-null, exactly mirroring the flavor pair's "required together" rule. <see
/// cref="CombinesWith"/> is the ONE sanctioned exception to the uniqueness guard: two stations that
/// name each other (a mutual pair, e.g. the forge's anvil+bellows) may share a route on purpose —
/// they resolve to one combined act, not two independent ones — and the guard checks the pairing is
/// exactly mutual, never a one-sided claim.</para>
/// </summary>
public static class InteriorLayout2D
{
    /// <summary>One physical station inside a room: its own stable id (nametag/lookup — NOT the
    /// click-key, since several stations can share one <paramref name="Action"/>), display label
    /// (the HUD "E · {Label}" prompt), sprite id (<see cref="TownAssets2D.ForStation"/>), the LOCAL
    /// tile position within the room's own grid, and the action string (U5: the station's
    /// "resolution surface") it opens on press — or <see langword="null"/> for an honest flavor
    /// station (see the class doc's U3 paragraph).
    /// <paramref name="Focus"/> is Forge-only (<c>ForgePanel.FocusSection</c>'s section key, e.g.
    /// "materials"/"craft"); <paramref name="HoverLine"/>/<paramref name="FlavorLine"/> are flavor-only
    /// (required together whenever <paramref name="Action"/> is null, forbidden when it is not).
    /// <paramref name="Verb"/>/<paramref name="Copy"/> (U5, KTD-D) are the real-station counterpart —
    /// required together whenever <paramref name="Action"/> is non-null: <paramref name="Verb"/> is
    /// this station's half of the <c>(Action, Verb)</c> route the reflective guard checks for
    /// uniqueness; <paramref name="Copy"/> is its own one-line toast. <paramref name="CombinesWith"/>
    /// (U5) names a MUTUAL partner station id this one is allowed to share a route with — the forge's
    /// anvil/bellows pair, wired for U7's paired minigame to build on; every other station leaves it
    /// <see langword="null"/>.</summary>
    public readonly record struct StationSpec(
        string Id,
        string Label,
        string SpriteId,
        Vector2I Tile,
        string? Action,
        string? Focus = null,
        string? HoverLine = null,
        string? FlavorLine = null,
        string? Verb = null,
        string? Copy = null,
        string? CombinesWith = null);

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
                // U7 (world-and-interiors plan, KTD-3): the STATIC default is blacksmith's own set —
                // read from WorkshopVocab (the single source of truth) rather than re-inlined here,
                // so this row and WorkshopRoomFor's blacksmith-only union can never drift apart
                // (the unit's own zero-regression pin: a blacksmith-only room must be byte-identical
                // to this pre-U7 row). A live session with a DIFFERENT/dual profession selection
                // never reads this static entry for the "forge" venue — see WorkshopRoomFor and
                // Town2D's own doc for how the actual composed room is resolved at build/entry time.
                //
                // U-T2-5 (Wave A substrate, §11.14.4, R14.5): MentorVoice.Station is appended here
                // TOO (not just in WorkshopRoomFor below) so this static row stays byte-identical to
                // WorkshopRoomFor's own blacksmith-only output — WorkshopVocabTests
                // .BlacksmithOnlyWorkshopRoom_IsByteIdenticalToThePreU7ForgeRow compares the two
                // directly. Bryn is not part of ANY profession's own WorkshopVocab set (she teaches
                // whichever craft the player actually picked, not blacksmithing specifically) — she
                // is appended once, here and in WorkshopRoomFor, never inside WorkshopVocab itself.
                WorkshopVocab.StationsFor(ProfessionRegistry.BlacksmithId).Append(MentorVoice.Station).ToArray()),
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
                    // U5 (verify-by-playing plan, KTD-D): the reported collision — "the market's two
                    // shelves are the same drawer with a different scroll anchor plus a 0.6s flash."
                    // ShopPanel has no FocusSection to differentiate on, so the fix is each station's
                    // own Verb/Copy/Label — three distinct on-screen identities, one route each.
                    new StationSpec("counter", "Sales Counter", "town2d-station-market-counter", new Vector2I(10, 6), "Shop",
                        Verb: "Haggle", Copy: "You step up to the sales counter."),
                    new StationSpec("shelf-a", "Wares Shelf", "town2d-station-market-shelf", new Vector2I(5, 3), "Shop",
                        Verb: "Browse Wares", Copy: "You browse the wares laid out on this shelf."),
                    new StationSpec("shelf-b", "Curio Shelf", "town2d-station-market-shelf", new Vector2I(14, 3), "Shop",
                        Verb: "Browse Curios", Copy: "You browse this shelf's odds and ends."),
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
                    // Hero-facing-day plan (2026-08-04) / minigames doc §3.6: the bar is where
                    // Act 2 (The Handshake) closes whatever thread the corner table surfaced in
                    // Act 1 (Work the Room) — CombinesWith names that cooperation as DATA, the
                    // same anvil+bellows precedent, even though (unlike the forge) the two halves
                    // already carry distinct Verbs and so needed no route-collision exception.
                    new StationSpec("bar", "The Bar", "town2d-station-tavern-bar", new Vector2I(4, 6), "Tavern",
                        Verb: "Order a Round", Copy: "You order a round at the bar.", CombinesWith: "table-b"),
                    new StationSpec("storywall", "Story Wall", "town2d-station-tavern-storywall", new Vector2I(18, 6), "Legends",
                        Verb: "Read the Wall", Copy: "You read the legends pinned to the story wall."),
                    // U6 (world-and-interiors plan, follow-up): these tiles double as patron seating
                    // anchors — kept as plain data here, no seating logic in this unit.
                    // U5 (verify-by-playing plan, KTD-D): both tables used to share bar's exact
                    // ("Tavern", null) route — a three-way collision the reflective guard now catches.
                    // Own Label/Verb/Copy per table fixes it without touching TavernPanel.
                    // table-a keeps its OWN Eavesdrop route (hero-facing-day plan, §3.6) — gossip
                    // only, deliberately NOT part of the Work the Room / Handshake pair below.
                    new StationSpec("table-a", "Fireside Table", "town2d-station-tavern-table", new Vector2I(8, 9), "Tavern",
                        Verb: "Eavesdrop", Copy: "You take the fireside table, catching the room's talk."),
                    new StationSpec("table-b", "Corner Table", "town2d-station-tavern-table", new Vector2I(14, 9), "Tavern",
                        Verb: "Swap Stories", Copy: "You take the corner table, trading stories with the regulars.",
                        CombinesWith: "bar"),
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
                    new StationSpec("overlook", "The Overlook", "town2d-station-gate-overlook", new Vector2I(9, 2), "Watch",
                        Verb: "Watch the Depths", Copy: "You lean into the overlook, watching the depths below."),
                    new StationSpec("muster", "Muster Board", "town2d-station-gate-muster", new Vector2I(5, 5), "Depths",
                        Verb: "Muster Heroes", Copy: "You check the muster board for who's ready to descend."),
                    new StationSpec("bountyledger", "Bounty Ledger", "town2d-station-gate-bounty", new Vector2I(12, 5), "Bounties",
                        Verb: "Post a Bounty", Copy: "You flip open the bounty ledger."),
                    new StationSpec("winch", "Gate Winch", "town2d-station-gate-winch", new Vector2I(9, 7), Action: null,
                        HoverLine: "Gate winch — raises the portcullis, nothing to manage from here",
                        FlavorLine: "The winch's chain hangs taut. It just raises the gate — try the muster board or the bounty ledger."),
                }),
        };

        return rooms.ToDictionary(r => r.VenueKey);
    }

    /// <summary>
    /// U7 (world-and-interiors plan, KTD-3): the workshop's ACTUAL composed room for a player's
    /// current profession selection — same shell/size/door as the static <see cref="Rooms"/>
    /// <c>"forge"</c> row (KTD-3: one shared shell, never per-profession buildings); <see
    /// cref="RoomSpec.Stations"/> is replaced with the UNION of every selected profession's own set
    /// (<see cref="WorkshopVocab"/>), deduplicated by profession id so a stale duplicate can never
    /// double-mount a station.
    ///
    /// <para><paramref name="orderedProfessions"/>'s first element is the PRIMARY profession — this
    /// method never reads that ordering itself (station placement is symmetric: every selected
    /// profession's full set appears, regardless of primary/secondary); only <see
    /// cref="WorkshopVocab.NametagFor"/>/<see cref="WorkshopVocab.SignboardSpriteIdFor"/>/<see
    /// cref="WorkshopVocab.StationNounFor"/> care which one leads. See <c>Town2D</c>'s own doc for
    /// how that ordering is derived from the sim's unordered <c>ImmutableSortedSet</c> state.</para>
    ///
    /// <para>Tile zones are disjoint by construction (each profession's stations sit on Y rows no
    /// other profession ever uses — see <see cref="WorkshopVocab"/>'s own doc), so any two selected
    /// professions' sets union into the shared shell with no tile collision, whatever the
    /// selection.</para>
    ///
    /// <para><b>U-T2-5 (Wave A substrate, §11.14.4, R14.5):</b> <see cref="MentorVoice.Station"/> is
    /// appended UNCONDITIONALLY, after the profession union — Bryn is not any one profession's own
    /// station, she is the apprenticeship's own teaching presence in whichever workshop the player
    /// actually built, so she appears regardless of which craft(s) are selected. Her own Y row (4)
    /// sits clear of every profession's own rows (<see cref="WorkshopVocab"/>'s row scheme: 2/3, 5/7/
    /// 10, 9, 11), so this can never collide with any selection, and the empty-selection defensive
    /// branch above already returns <see cref="Rooms"/>'s own "forge" row, which carries her too (see
    /// that row's own comment for why the two are kept byte-identical on purpose).</para>
    /// </summary>
    public static RoomSpec WorkshopRoomFor(IReadOnlyList<string> orderedProfessions)
    {
        var baseSpec = Rooms["forge"];
        if (orderedProfessions.Count == 0)
        {
            return baseSpec; // defensive: every real campaign always has >=1 selected profession
        }

        var stations = orderedProfessions
            .Distinct()
            .SelectMany(WorkshopVocab.StationsFor)
            .Append(MentorVoice.Station)
            .ToArray();

        return baseSpec with { Stations = stations };
    }
}
