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

    [TestCase]
    public void MissingStationAndShellArt_RendersLoudPlaceholders_NotSilentBoxes()
    {
        var town = Mount();
        try
        {
            var room = town.FindInteriorRoom("forge");

            foreach (var station in room.Stations)
            {
                var image = station.Sprite.Texture!.GetImage();
                AssertThat(image.GetPixel(0, 0))
                    .OverrideFailureMessage(
                        $"station '{station.Key}' has no committed art yet (expected pre-U2) but "
                        + "must render TownAssets2D's loud magenta-bordered placeholder, never a "
                        + "silent flat box — corner pixel was not magenta.")
                    .IsEqual(new Color(1f, 0f, 1f));
            }
        }
        finally { town.Free(); }
    }

    [TestCase]
    public void EveryStationAction_IsARecognizedMainUiRoute_NeverADeadClick()
    {
        foreach (var room in InteriorLayout2D.Rooms.Values)
        {
            foreach (var station in room.Stations)
            {
                AssertThat(KnownStationActions.Contains(station.Action))
                    .OverrideFailureMessage(
                        $"station '{station.Id}' in room '{room.VenueKey}' routes to action "
                        + $"'{station.Action}', which MainUi.OnInteriorHotspotActivated has no "
                        + "handler for — pressing this station would silently do nothing. Never "
                        + "ship a dead click.")
                    .IsTrue();
            }
        }
    }

    /// <summary>
    /// The one physics test in this file: proves the perimeter wall actually blocks the player,
    /// rather than merely existing as an unenforced rect. Rendering is disabled BEFORE the first
    /// awaited frame (standing constraint 4) — pumping frames while any SubViewport renders is the
    /// documented gdUnit headless hang.
    /// </summary>
    [TestCase]
    public async System.Threading.Tasks.Task ForgeRoom_PerimeterWalls_BlockThePlayer()
    {
        var town = Mount();
        try
        {
            town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var room = town.FindInteriorRoom("forge");
            town.Player.SpawnAt(room.RoomRect.Position + new Vector2(8f, room.RoomRect.Size.Y / 2f));
            town.Player.SetDirectInput(Vector2.Left); // straight at the left wall

            var tree = (SceneTree)Engine.GetMainLoop();
            for (var i = 0; i < 60; i++)
            {
                await tree.ToSignal(tree, SceneTree.SignalName.PhysicsFrame);
            }

            AssertThat(town.Player.GlobalPosition.X)
                .OverrideFailureMessage("The forge room's left wall did not block the player — they walked clean through the perimeter.")
                .IsGreaterEqual(room.RoomRect.Position.X);
        }
        finally
        {
            town.Player.SetDirectInput(null);
            town.Free();
        }
    }
}
#endif
