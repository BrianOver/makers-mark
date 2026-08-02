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
    };

    /// <summary>U3: the section keys <c>ForgePanel.FocusSection</c> actually knows how to
    /// scroll/flash — a station naming any other <c>Focus</c> is caught HERE (table-validation
    /// time), not discovered as "the shelf press opened the panel but never scrolled anywhere."</summary>
    private static readonly HashSet<string> KnownFocusValues = new() { "materials", "craft" };

    private static Town2D Mount()
    {
        var town = new Town2D { Name = "Town2D" };
        town.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(town);
        town.Build(new GodotClient.SimAdapter(seed: 2026));
        return town;
    }

    [TestCase]
    public void ForgeRoom_ExistsAtTownBuildTime_OffTheTownCameraFrame()
    {
        var town = Mount();
        try
        {
            var room = town.FindInteriorRoom("forge");

            // KTD-1: the island must sit clear of the town's own camera limits (0..GridWidth*16),
            // so the two are never both in frame at once regardless of camera position.
            AssertThat(room.RoomRect.Position.X)
                .OverrideFailureMessage(
                    "The forge room's island offset must clear the town's own width "
                    + $"({TownLayout2D.GridWidth * TownLayout2D.TileSize}px) — it is not off-frame.")
                .IsGreaterEqual(TownLayout2D.GridWidth * TownLayout2D.TileSize);
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
                    "U1 pins exactly these six forge station ids, in this order, so U2 can author "
                    + "art against them in parallel.")
                .IsEqual(expectedIds);
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void ForgeRoom_ExitZonePresent_WithACollisionShape()
    {
        var town = Mount();
        try
        {
            var room = town.FindInteriorRoom("forge");

            AssertThat(room.ExitZone).IsNotNull();
            AssertThat(room.ExitZone.GetChildren().OfType<CollisionShape2D>().Any(s => s.Shape is not null))
                .OverrideFailureMessage("The exit zone has no collision shape — walking onto the door tile could never trigger it.")
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
