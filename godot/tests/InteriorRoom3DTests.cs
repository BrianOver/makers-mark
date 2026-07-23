#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Town;
using GodotClient.Town3d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// 3D-interiors MVP: the real 3D venue rooms (<see cref="InteriorRoom3D"/>) that replaced
/// <see cref="InteriorStage"/>'s painted backdrop, and the MainUi seam that mounts them —
/// camera push-in on entry, see-through hotspot overlay preserved on top, full teardown +
/// door-anchor restore on exit. PROPERTY-ONLY by design (3D-render-hang rule): every node is
/// asserted synchronously after Build/mount, never a frame pump while a 3D SubViewport exists.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InteriorRoom3DTests
{
    // ── The room table itself — no live MainUi/world needed ──────────────────────────────────

    [TestCase]
    public void VenueKeys_MatchInteriorStageVenueTable_Exactly()
    {
        // The room table and the hotspot table must never drift: every venue InteriorStage can
        // open gets a 3D room, and no room exists for a venue the stage cannot open.
        var roomKeys = InteriorRoom3D.VenueKeys.OrderBy(k => k).ToArray();
        var stageKeys = InteriorStage.Venues.Keys.OrderBy(k => k).ToArray();
        AssertThat(string.Join(",", roomKeys)).IsEqual(string.Join(",", stageKeys));
    }

    [TestCase]
    public void Build_EachVenue_ConstructsShellLightFocus_AndVenueProps()
    {
        foreach (var key in InteriorRoom3D.VenueKeys)
        {
            var room = new InteriorRoom3D();
            try
            {
                room.Build(key);

                AssertThat(room.VenueKey).IsEqual(key);
                foreach (var shellPiece in new[] { "Floor", "WallBack", "WallLeft", "WallRight" })
                {
                    AssertThat(room.GetNodeOrNull<MeshInstance3D>(shellPiece))
                        .OverrideFailureMessage($"room '{key}' is missing shell piece '{shellPiece}'")
                        .IsNotNull();
                }

                AssertThat(room.GetNodeOrNull<OmniLight3D>("RoomLight")).IsNotNull();
                AssertThat(room.Focus).IsNotNull();
                AssertThat(room.GetNodeOrNull<Node3D>("Focus")).IsNotNull();

                // Venue-appropriate gen props, by name (Prop_<file>): present whether the GLB
                // resolved or degraded to the primitive fallback — art presence is not the pin.
                foreach (var file in InteriorRoom3D.PropFiles(key))
                {
                    var propName = "Prop_" + System.IO.Path.GetFileNameWithoutExtension(file);
                    AssertThat(room.GetNodeOrNull<Node3D>(propName))
                        .OverrideFailureMessage($"room '{key}' is missing gen prop '{propName}'")
                        .IsNotNull();
                }
            }
            finally
            {
                room.Free();
            }
        }
    }

    [TestCase]
    public void Build_UnknownVenueKey_Throws()
    {
        var room = new InteriorRoom3D();
        try
        {
            var threw = false;
            try
            {
                room.Build("not-a-real-venue");
            }
            catch (System.ArgumentOutOfRangeException)
            {
                threw = true;
            }

            AssertThat(threw).IsTrue();
        }
        finally
        {
            room.Free();
        }
    }

    // ── The MainUi seam: 3D room + camera push-in on entry, overlay preserved, teardown ──────

    [TestCase]
    public void VenueArrival_MountsRoomInTownWorld_PushesCamera_OverlayIsSeeThrough()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("forge"));
            ui.Town.WorldInputNode.TriggerInteract();

            // The 3D room replaced the painted backdrop...
            AssertThat(ui.InteriorRoom).IsNotNull();
            AssertThat(ui.InteriorRoom!.VenueKey).IsEqual("forge");
            AssertBool(ReferenceEquals(ui.InteriorRoom.GetParent(), ui.Town.World))
                .OverrideFailureMessage("the interior room must mount inside the live town world")
                .IsTrue();
            AssertThat(ui.Town.Camera.IsPushedIn).IsTrue();

            // ...while the hotspot overlay still opened on top, in see-through mode, with the
            // exact same carry-forward hotspot buttons.
            AssertThat(ui.Interior.IsOpen).IsTrue();
            AssertThat(ui.Interior.SeeThrough).IsTrue();
            AssertThat(ui.Interior.HotspotButtons.Count)
                .IsEqual(InteriorStage.Venues["forge"].Hotspots.Length);
            AssertThat(ui.Clock.Engaged).IsTrue(); // R7: interior open still engages the latch
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void HotspotPress_StillOpensItsDrawer_AndTearsThe3DRoomDown()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("forge"));
            ui.Town.WorldInputNode.TriggerInteract();
            AssertThat(ui.InteriorRoom).IsNotNull();

            Press(ui.Interior, "Hotspot_anvil");

            // Content parity: the forge hotspot routes to the SAME drawer id as ever...
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            // ...and the 3D room is fully gone with the camera handed back.
            AssertThat(ui.InteriorRoom).IsNull();
            AssertThat(ui.Town.Camera.IsPushedIn).IsFalse();
            AssertThat(ui.Town.World.GetNodeOrNull("InteriorRoom3D")).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Exit_TearsRoomDown_ReleasesCamera_RestoresAvatarToDoor()
    {
        var ui = MountMainUi();
        try
        {
            var doorPosition = ui.Town.DoorAnchor("tavern")!.Value;

            // Nudge the player off the door first, so the restore below is provably active
            // (mirrors InteriorStageTests' own door-restore proof).
            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("tavern"));
            ui.Town.WorldInputNode.TriggerInteract();
            AssertThat(ui.InteriorRoom!.VenueKey).IsEqual("tavern");
            ui.Town.Player.GlobalPosition += new Vector3(400, 0, 0);

            Press(ui.Interior, "InteriorExit");

            AssertThat(ui.Interior.IsOpen).IsFalse();
            AssertThat(ui.InteriorRoom).IsNull();
            AssertThat(ui.Town.Camera.IsPushedIn).IsFalse();
            AssertThat(ui.Town.World.GetNodeOrNull("InteriorRoom3D")).IsNull();
            AssertThat(ui.Town.Player.GlobalPosition).IsEqual(doorPosition);
            AssertThat(ui.Clock.Engaged).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ReEntry_AfterExit_MountsAFreshRoom_NoVenueStateLeaks()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("forge"));
            ui.Town.WorldInputNode.TriggerInteract();
            var firstRoom = ui.InteriorRoom;
            Press(ui.Interior, "InteriorExit");

            ui.Town.WorldInputNode.SetTarget(ui.Town.FindBuilding("minegate"));
            ui.Town.WorldInputNode.TriggerInteract();

            AssertThat(ui.InteriorRoom).IsNotNull();
            AssertThat(ui.InteriorRoom!.VenueKey).IsEqual("minegate");
            AssertBool(ReferenceEquals(ui.InteriorRoom, firstRoom)).IsFalse();
            // Exactly one room mounted — a re-entry can never stack rooms on the shelf.
            AssertThat(ui.Town.World.GetChildren().OfType<InteriorRoom3D>().Count()).IsEqual(1);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
