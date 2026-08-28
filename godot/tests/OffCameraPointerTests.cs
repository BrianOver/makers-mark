#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U15 (§11.14.14): a target off camera used to render literally nothing (<see
/// cref="TutorialOverlay"/>'s own class doc — a <see cref="Building2D"/> pulse lives inside <see
/// cref="Town2D"/>'s own <c>SubViewport</c>, so off screen it draws nothing at all) — at spawn the
/// camera shows only the forge, and the market/notice board/mine gate are each a screen or more
/// away with quick-travel still locked. This suite proves the two mechanisms this unit adds: an
/// edge marker that appears exactly when the target is off camera and points the right way (never
/// moving the camera itself — KTD7), and a damping pass that turns down every OTHER station's
/// ambient <c>Tell</c> glow while one of them carries the live pulse.
///
/// <para>Per this unit's own PR body: these tests prove GEOMETRY, not appearance. A human still has
/// to look at a rendered frame to confirm the marker actually reads as a marker (shape, contrast,
/// legibility) — see the PR body for exactly what to stand where and look at.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class OffCameraPointerTests
{
    [TestCase]
    public async Task ATargetOffCamera_ShowsAnEdgeMarker_TowardTheRealDirection()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui); // let the spawn-time camera snap settle before reading its transform

            var forge = ui.Town.FindBuilding("forge");
            var mineGate = ui.Town.FindBuilding("minegate");

            AssertThat(mineGate.GlobalPosition.Y < forge.GlobalPosition.Y)
                .OverrideFailureMessage(
                    "Fixture guard: TownLayout2D must place the mine gate north of the forge (smaller " +
                    "world Y) for this test's direction check to mean anything.")
                .IsTrue();

            ui.Overlay.RefreshAnchor(TutorialAnchor.ForBuilding("minegate"), ui.Town, ui.Drawer, ui);
            ui.Overlay.Tick(0.016);

            AssertThat(ui.Overlay.OffCameraMarkerVisible)
                .OverrideFailureMessage(
                    "Fixture guard: at spawn the camera sits on the forge and the mine gate is a screen " +
                    "or more away — if the marker is not showing, either the camera framing changed or " +
                    "the off-camera detection itself is broken, and this test cannot say anything further.")
                .IsTrue();

            var screenCenter = ui.Town.ViewportScreenRect.GetCenter();
            AssertThat(ui.Overlay.OffCameraMarkerCenter.Y)
                .OverrideFailureMessage(
                    $"The mine gate sits north of the forge, so the marker (at " +
                    $"{ui.Overlay.OffCameraMarkerCenter}) must land in the UPPER half of the screen " +
                    $"(center Y {screenCenter.Y}) — it did not, so the marker is pointing the wrong way.")
                .IsLess(screenCenter.Y);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public async Task WalkingTheCameraOntoTheTarget_ClearsTheMarker_AndNeverBringsItBack()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            var mineGate = ui.Town.FindBuilding("minegate");
            ui.Overlay.RefreshAnchor(TutorialAnchor.ForBuilding("minegate"), ui.Town, ui.Drawer, ui);
            ui.Overlay.Tick(0.016);

            AssertThat(ui.Overlay.OffCameraMarkerVisible)
                .OverrideFailureMessage("Fixture guard: the marker must start visible for 'arrival' to prove anything.")
                .IsTrue();

            // U15's own line, drawn directly rather than through a real WASD walk: CameraFollowTests
            // already proves the camera tracks the player, so this moves the CAMERA straight onto the
            // target the same way arriving there eventually would, and checks the ONE thing this unit
            // owns — that the marker reacts correctly once the target is on screen.
            ui.Town.Cam.GlobalPosition = mineGate.GlobalPosition;
            ui.Town.Cam.ResetSmoothing();
            await SettleLayout(ui); // let the canvas transform pick up the new camera position
            ui.Overlay.Tick(0.016);

            AssertThat(ui.Overlay.OffCameraMarkerVisible)
                .OverrideFailureMessage("The target is now centered on camera — the marker must clear, not keep pointing at it.")
                .IsFalse();
            AssertThat(mineGate.IsTutorialPulsing)
                .OverrideFailureMessage("The building's own on-screen pulse must still be running — the marker hands off to it, it does not replace it.")
                .IsTrue();

            // Never persists: a few more ticks must not bring it back on its own.
            for (var i = 0; i < 5; i++)
            {
                ui.Overlay.Tick(0.016);
            }

            AssertThat(ui.Overlay.OffCameraMarkerVisible)
                .OverrideFailureMessage("The marker reappeared on its own with nothing having changed — it must never persist/flicker back.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public async Task ClearingTheAnchor_HidesTheMarker_AndItStaysHidden()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            ui.Overlay.RefreshAnchor(TutorialAnchor.ForBuilding("minegate"), ui.Town, ui.Drawer, ui);
            ui.Overlay.Tick(0.016);
            AssertThat(ui.Overlay.OffCameraMarkerVisible)
                .OverrideFailureMessage("Fixture guard: the marker must start visible for this test to prove anything.")
                .IsTrue();

            ui.Overlay.RefreshAnchor(TutorialAnchor.None, ui.Town, ui.Drawer, ui);
            ui.Overlay.Tick(0.016);

            AssertThat(ui.Overlay.OffCameraMarkerVisible)
                .OverrideFailureMessage("Clearing the anchor entirely must hide the marker immediately.")
                .IsFalse();

            for (var i = 0; i < 5; i++)
            {
                ui.Overlay.Tick(0.016);
            }

            AssertThat(ui.Overlay.OffCameraMarkerVisible)
                .OverrideFailureMessage("With no anchor active, the marker must never reappear on its own.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void AWorldAnchor_DampsEverySiblingStationsTell_AndRestoresWhenItClears()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.EnterInterior("forge");

            var anvil = ui.Town.FindStation("forge", "anvil");
            var shelf = ui.Town.FindStation("forge", "shelf");

            AssertThat(anvil.Tell)
                .OverrideFailureMessage("Fixture guard: the anvil station must have a Tell glow (a real verb) for damping to mean anything.")
                .IsNotNull();
            AssertThat(shelf.Tell)
                .OverrideFailureMessage("Fixture guard: the material shelf station must have a Tell glow (a real verb) for damping to mean anything.")
                .IsNotNull();

            ui.Overlay.RefreshAnchor(TutorialAnchor.ForStation("forge", "anvil"), ui.Town, ui.Drawer, ui);

            AssertThat(shelf.IsTellDamped)
                .OverrideFailureMessage("A sibling station's Tell must dampen while another station carries the live world anchor.")
                .IsTrue();
            AssertThat(anvil.IsTellDamped)
                .OverrideFailureMessage("The station the anchor actually points at must NOT dampen its own Tell.")
                .IsFalse();

            ui.Overlay.RefreshAnchor(TutorialAnchor.None, ui.Town, ui.Drawer, ui);

            AssertThat(shelf.IsTellDamped)
                .OverrideFailureMessage("Once the world anchor clears, every station's Tell must restore to normal.")
                .IsFalse();
            AssertThat(anvil.IsTellDamped)
                .OverrideFailureMessage("Once the world anchor clears, every station's Tell must restore to normal (including the one that was pulsing).")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public async Task PointingAtAFarOffCameraTarget_NeverMovesTheCamera_WithoutAPlayerPress()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            var before = ui.Town.Cam.GlobalPosition;

            ui.Overlay.RefreshAnchor(TutorialAnchor.ForBuilding("minegate"), ui.Town, ui.Drawer, ui);
            for (var i = 0; i < 10; i++)
            {
                ui.Overlay.Tick(0.016);
            }

            await SettleLayout(ui);

            AssertThat(ui.Town.Cam.GlobalPosition.DistanceTo(before))
                .OverrideFailureMessage(
                    "KTD7: the off-camera marker must only ever say WHERE — pointing it at a far-away " +
                    "target must never itself drag the camera toward it. Only a player WASD press (or " +
                    "an existing, separately-triggered focus beat like a party departure) may move the " +
                    "camera.")
                .IsLess(1f);
        }
        finally { Unmount(ui); }
    }
}
#endif
