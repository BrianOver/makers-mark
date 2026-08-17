#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U1 (painted-interiors plan): the forge room's static geometry — built once at <see
/// cref="Town2D.Build"/> time, off-frame at its island offset (KTD-1), regardless of whether the
/// player has ever entered it. Property-only assertions everywhere except the one that needs a
/// physics step (walls blocking movement); that one disables SubViewport rendering FIRST, per the
/// documented gdUnit headless-hang hazard (standing constraint 4).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InteriorRoomTests
{
    /// <summary>The routable action strings <c>MainUi.OnInteriorHotspotActivated</c> actually
    /// knows how to open (the drawer ids <c>OpenPanel</c> accepts, plus the two code-built-modal
    /// special cases it checks first) — mirrors that method's own vocabulary so a station whose
    /// action falls outside it is caught HERE, at room-build time, rather than as a dead click a
    /// player discovers by pressing E and getting nothing (this repo's recurring failure class).</summary>
    private static readonly HashSet<string> KnownStationActions = new()
    {
        "Forge", "Shop", "Tavern", "Bounties", "Depths", "Bestiary", "Legends",
        // U1 (world-and-interiors plan): "Watch" is the ONE new action string this unit adds —
        // the gatehouse's "overlook" station, routed to MainUi.OnInteriorHotspotActivated's own
        // Mirror.ShowMirror() case (same shape as Bestiary/Legends above).
        "Watch",
    };

    /// <summary>U3: the section keys <c>ForgePanel.FocusSection</c> actually knows how to
    /// scroll/flash — a station naming any other <c>Focus</c> is caught HERE (table-validation
    /// time), not discovered as "the shelf press opened the panel but never scrolled anywhere."</summary>
    private static readonly HashSet<string> KnownFocusValues = new() { "materials", "foundry", "craft" };

    private static Town2D Mount()
    {
        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(seed: 2026));
        return town;
    }

    // U1 (world-and-interiors plan): parameterized over all four rooms — proving the off-frame
    // property forge alone used to pin now covers market/tavern/minegate's own island offsets too.
    [TestCase("forge")]
    [TestCase("market")]
    [TestCase("tavern")]
    [TestCase("minegate")]
    public void EveryRoom_ExistsAtTownBuildTime_OffTheTownCameraFrame(string venueKey)
    {
        var town = Mount();
        try
        {
            var room = town.FindInteriorRoom(venueKey);

            // KTD-1: the island must sit clear of the town's own camera limits (0..GridWidth*16),
            // so the two are never both in frame at once regardless of camera position.
            AssertThat(room.RoomRect.Position.X)
                .OverrideFailureMessage(
                    $"The '{venueKey}' room's island offset must clear the town's own width "
                    + $"({TownLayout2D.GridWidth * TownLayout2D.TileSize}px) — it is not off-frame.")
                .IsGreaterEqual(TownLayout2D.GridWidth * TownLayout2D.TileSize);
        }
        finally { town.Free(); }
    }

    /// <summary>
    /// U1 (world-and-interiors plan, KTD-1): "distinct island offsets so no camera clamp can ever
    /// see two rooms" is a claim about every PAIR of rooms, not just each room versus the town —
    /// this is the one place that pairwise property is actually checked, rather than trusted from
    /// the four rooms' Y-offsets simply looking spaced out in <see cref="InteriorLayout2D"/>.
    /// </summary>
    [TestCase]
    public void EveryPairOfRooms_HasNonOverlappingIslandRects()
    {
        var town = Mount();
        try
        {
            var rects = InteriorLayout2D.Rooms.Keys.Select(k => (Key: k, Rect: town.FindInteriorRoom(k).RoomRect)).ToArray();

            for (var i = 0; i < rects.Length; i++)
            {
                for (var j = i + 1; j < rects.Length; j++)
                {
                    AssertThat(rects[i].Rect.Intersects(rects[j].Rect))
                        .OverrideFailureMessage(
                            $"Rooms '{rects[i].Key}' and '{rects[j].Key}' have overlapping island "
                            + "rects — a camera clamped to one could see the other. KTD-1 requires "
                            + "every room's island offset to clear every OTHER room's, not just the town's.")
                        .IsFalse();
                }
            }
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void ForgeRoom_StationsSpawnFromTheTable_InDeclaredOrder()
    {
        var town = Mount();
        try
        {
            var spec = InteriorLayout2D.Rooms["forge"];
            var room = town.FindInteriorRoom("forge");

            var expectedIds = spec.Stations.Select(s => s.Id).ToArray();
            var actualIds = room.Stations.Select(s => s.Key).ToArray();

            AssertThat(actualIds)
                .OverrideFailureMessage(
                    "U1 pins these forge station ids, in this order (six blacksmith stations plus "
                    + "Bryn, U-T2-5), so U2 can author art against them in parallel.")
                .IsEqual(expectedIds);
        }
        finally { town.Free(); }
    }

    // U1 (world-and-interiors plan): parameterized over all four rooms — BuildExitZone is shared
    // code (InteriorRoom2D.Build runs it for every RoomSpec identically), so this proves it holds
    // for market/tavern/minegate's own rows too, not just the forge row it originally pinned.
    [TestCase("forge")]
    [TestCase("market")]
    [TestCase("tavern")]
    [TestCase("minegate")]
    public void EveryRoom_ExitZonePresent_WithACollisionShape(string venueKey)
    {
        var town = Mount();
        try
        {
            var room = town.FindInteriorRoom(venueKey);

            AssertThat(room.ExitZone).IsNotNull();
            AssertThat(room.ExitZone.GetChildren().OfType<CollisionShape2D>().Any(s => s.Shape is not null))
                .OverrideFailureMessage($"The '{venueKey}' room's exit zone has no collision shape — walking onto the door tile could never trigger it.")
                .IsTrue();
        }
        finally { town.Free(); }
    }

    /// <summary>
    /// Fixed post-review: U2 (a parallel unit) landed real committed art for all seven pinned ids
    /// before this PR merged, so none of <see cref="InteriorLayout2D"/>'s own ids are "missing"
    /// anymore — iterating the live room's stations no longer exercises the placeholder path at
    /// all (every corner pixel is real paint, not magenta). Forcing a deliberately bogus id through
    /// the SAME ladder (<see cref="TownAssets2D.ForStation"/>/<see cref="TownAssets2D.ForShell"/>)
    /// is what actually proves the loud-placeholder MECHANISM, independent of whether any
    /// particular id currently has art — it must keep failing loudly for as long as this game ships
    /// any unresolved id, real ones included.
    /// </summary>
    [TestCase]
    public void MissingStationAndShellArt_RendersLoudPlaceholders_NotSilentBoxes()
    {
        const string bogusStationId = "town2d-station-u1-test-does-not-exist";
        var stationImage = TownAssets2D.ForStation(bogusStationId).GetImage();
        AssertThat(stationImage.GetPixel(0, 0))
            .OverrideFailureMessage(
                $"TownAssets2D.ForStation('{bogusStationId}') did not render a loud "
                + "magenta-bordered placeholder for an unresolved id — corner pixel was not magenta.")
            .IsEqual(new Color(1f, 0f, 1f));

        const string bogusShellId = "town2d-forge-interior-shell-u1-test-does-not-exist";
        var shellImage = TownAssets2D.ForShell(bogusShellId, new Vector2(64, 64)).GetImage();
        AssertThat(shellImage.GetPixel(0, 0))
            .OverrideFailureMessage(
                $"TownAssets2D.ForShell('{bogusShellId}') did not render a loud magenta-bordered "
                + "placeholder for an unresolved id — corner pixel was not magenta.")
            .IsEqual(new Color(1f, 0f, 1f));
    }

    [TestCase]
    public void EveryStationAction_IsARecognizedMainUiRoute_NeverADeadClick()
    {
        foreach (var room in InteriorLayout2D.Rooms.Values)
        {
            foreach (var station in room.Stations)
            {
                if (station.Action is null)
                {
                    // U3: a null Action is only ever legitimate as HONEST FLAVOR (Bellows/Quench)
                    // — never a plain omission. Both lines are mandatory here, or pressing E is a
                    // silent dead click wearing a "this was deliberate" costume.
                    AssertThat(!string.IsNullOrWhiteSpace(station.HoverLine))
                        .OverrideFailureMessage(
                            $"station '{station.Id}' in room '{room.VenueKey}' has no Action (flavor-"
                            + "only) but no HoverLine either — WorldInput2D would fall back to "
                            + "promising 'E · {Label}' for a station with no verb. Never ship a dead click.")
                        .IsTrue();
                    AssertThat(!string.IsNullOrWhiteSpace(station.FlavorLine))
                        .OverrideFailureMessage(
                            $"station '{station.Id}' in room '{room.VenueKey}' has no Action (flavor-"
                            + "only) but no FlavorLine either — pressing E would silently do nothing. "
                            + "Never ship a dead click.")
                        .IsTrue();
                    continue;
                }

                AssertThat(KnownStationActions.Contains(station.Action))
                    .OverrideFailureMessage(
                        $"station '{station.Id}' in room '{room.VenueKey}' routes to action "
                        + $"'{station.Action}', which MainUi.OnInteriorHotspotActivated has no "
                        + "handler for — pressing this station would silently do nothing. Never "
                        + "ship a dead click.")
                    .IsTrue();

                if (station.Focus is not null)
                {
                    AssertThat(KnownFocusValues.Contains(station.Focus))
                        .OverrideFailureMessage(
                            $"station '{station.Id}' in room '{room.VenueKey}' names Focus "
                            + $"'{station.Focus}', which ForgePanel.FocusSection has no section for — "
                            + "the panel would open but never scroll anywhere.")
                        .IsTrue();
                }
            }
        }
    }

    // U12 (world-and-interiors plan, "stations you can read across the room"): a station's
    // Building2D.Tell must exist iff its InteriorLayout2D.StationSpec.Action is non-null (a real
    // verb) — the sight-level cue this unit adds on top of #349's dim-nametag/HoverLine
    // differentiation. Parameterized over all four rooms so a future room row inherits the
    // check for free, same as the dead-click guard above.
    [TestCase("forge")]
    [TestCase("market")]
    [TestCase("tavern")]
    [TestCase("minegate")]
    public void EveryRoom_VerbStationsCarryTheTell_FlavorStationsDoNot(string venueKey)
    {
        var town = Mount();
        try
        {
            var spec = InteriorLayout2D.Rooms[venueKey];
            var room = town.FindInteriorRoom(venueKey);

            AssertThat(room.Stations.Count)
                .OverrideFailureMessage($"room '{venueKey}': built station count doesn't match its table — the loop below would silently compare the wrong pairs.")
                .IsEqual(spec.Stations.Length);

            for (var i = 0; i < spec.Stations.Length; i++)
            {
                var stationSpec = spec.Stations[i];
                var building = room.Stations[i];

                AssertThat(building.Key)
                    .OverrideFailureMessage($"room '{venueKey}' station index {i}: table/build order mismatch (expected '{stationSpec.Id}').")
                    .IsEqual(stationSpec.Id);

                if (stationSpec.Action is null)
                {
                    AssertThat(building.Tell)
                        .OverrideFailureMessage(
                            $"flavor station '{stationSpec.Id}' in room '{venueKey}' has a Tell — "
                            + "only a real verb (non-null Action) may carry the sight-level pulse; "
                            + "a flavor station reading as interactive from across the room is worse than the dead-click problem this unit fixes.")
                        .IsNull();
                }
                else
                {
                    AssertThat(building.Tell)
                        .OverrideFailureMessage(
                            $"verb station '{stationSpec.Id}' in room '{venueKey}' (Action='{stationSpec.Action}') "
                            + "has no Tell — it will not read as interactive from across the room before hover/click.")
                        .IsNotNull();
                }
            }
        }
        finally { town.Free(); }
    }

    // The town's own outdoor buildings (Forge/Shop/Tavern/Gate/Noticeboard exteriors) must render
    // byte-for-byte unchanged — Building2D.Configure's showTell flag defaults to false, and
    // Town2D.BuildBuildings' one Configure call site (the only outdoor call site) never passes it.
    [TestCase("forge")]
    [TestCase("market")]
    [TestCase("tavern")]
    [TestCase("minegate")]
    [TestCase("noticeboard")]
    public void OutdoorTownBuildings_NeverCarryATell(string key)
    {
        var town = Mount();
        try
        {
            var building = town.FindBuilding(key);

            AssertThat(building.Tell)
                .OverrideFailureMessage($"outdoor building '{key}' has a Tell node — U12's tell is opt-in for interior stations only; town buildings must be unaffected.")
                .IsNull();
        }
        finally { town.Free(); }
    }

    /// <summary>
    /// The one physics test in this file: proves the perimeter wall actually blocks the player,
    /// rather than merely existing as an unenforced rect. Rendering is disabled BEFORE the first
    /// awaited frame (standing constraint 4) — pumping frames while any SubViewport renders is the
    /// documented gdUnit headless hang.
    ///
    /// <para><b>Fixed post-review:</b> the original version spawned the player at local X=8, which
    /// is INSIDE the left wall's own 0..16 (one tile) footprint, not beside it — the player started
    /// embedded in the wall's collision shape. <c>CharacterBody2D.MoveAndSlide</c> depenetrates an
    /// already-overlapping start position as part of its own recovery step, and for a body starting
    /// dead-center in a symmetric overlap that resolution direction is not something this test can
    /// rely on — combined with the "walk left" input, it reliably pushed the player OUT through the
    /// wall's outer face, which read as "the wall did not block them" for a reason that had nothing
    /// to do with whether the wall actually blocks a real approach. Spawning inside the walkable
    /// interior (tile (5,7): clear of every station and of the wall itself) and walking INTO the
    /// wall from there is the scenario that actually matters, and is what a real player does.</para>
    /// </summary>
    [TestCase]
    public async System.Threading.Tasks.Task ForgeRoom_PerimeterWalls_BlockThePlayer()
    {
        var town = Mount();
        try
        {
            town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var room = town.FindInteriorRoom("forge");
            var start = room.RoomRect.Position + TownLayout2D.TileToWorld(new Vector2I(5, 7));
            town.Player.SpawnAt(start);
            town.Player.SetDirectInput(Vector2.Left); // walk toward the left wall from clear ground

            var tree = (SceneTree)Engine.GetMainLoop();
            for (var i = 0; i < 90; i++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            }

            var finalX = town.Player.GlobalPosition.X;

            // The wall assertion below is meaningless if the player never actually walked toward
            // it — this is the "movement itself works" precondition CameraFollowTests' own doc
            // insists on checking before trusting a downstream position assertion.
            AssertThat(finalX)
                .OverrideFailureMessage(
                    $"The player never walked left at all (at {finalX:0.##}, started at "
                    + $"{start.X:0.##}) — the wall assertion below cannot mean anything until "
                    + "movement itself works.")
                .IsLess(start.X - 20f);

            AssertThat(finalX)
                .OverrideFailureMessage(
                    $"The forge room's left wall did not block the player — they reached "
                    + $"{finalX:0.##}, past the wall's inner face at "
                    + $"{room.RoomRect.Position.X + TownLayout2D.TileSize:0.##}.")
                .IsGreaterEqual(room.RoomRect.Position.X + TownLayout2D.TileSize);
        }
        finally
        {
            town.Player.SetDirectInput(null);
            town.Free();
        }
    }
}
#endif
