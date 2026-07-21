#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// PA8 (spec DB4/PKD8) "town host" wiring: <c>MainUi.OnTownBuildingClicked</c>'s new
/// "ForgeStation"/"CounterStation" cases push the <c>CameraRig</c> in on the station and open the
/// station's focus surface (the existing <c>ForgePanel</c>/<c>ShopPanel</c> drawer — PA6's
/// minigame already lives inside <c>ForgePanel</c>; PA7's dedicated counter panel isn't merged
/// yet, so "Shop" is the correct entry per this unit's brief), then release the camera when the
/// drawer fully closes (<c>DrawerHost.Closed</c>). Station arrival is raised through the SAME
/// production path <c>TownStationTests</c> exercises directly against <c>Town3D</c> — here it is
/// driven through the real, mounted <c>MainUi</c> so the panel-open/camera-release side of the
/// wiring is proven end-to-end. No physics-frame pumping needed (arrival is raised via the
/// deterministic <c>WorldInput3D.SetTarget</c>/<c>TriggerInteract</c> test seam — see
/// <c>Building3DInteractionTests</c> precedent — so there is no headless-hang risk here).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TownStationWiringTests
{
    [TestCase]
    public void ForgeStationArrival_PushesCameraIn_AndOpensForgePanel()
    {
        var ui = MountMainUi();
        try
        {
            var station = ui.Town.FindBuilding("forge-station");
            ui.Town.WorldInputNode.SetTarget(station);
            ui.Town.WorldInputNode.TriggerInteract();

            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Town.Camera.IsPushedIn).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CounterStationArrival_PushesCameraIn_AndOpensShopPanel()
    {
        var ui = MountMainUi();
        try
        {
            var station = ui.Town.FindBuilding("counter-station");
            ui.Town.WorldInputNode.SetTarget(station);
            ui.Town.WorldInputNode.TriggerInteract();

            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Shop");
            AssertThat(ui.Town.Camera.IsPushedIn).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Wiring order: overlay (drawer) close → camera released.</summary>
    [TestCase]
    public void ClosingDrawerAfterStationArrival_ReleasesCamera()
    {
        var ui = MountMainUi();
        try
        {
            var station = ui.Town.FindBuilding("forge-station");
            ui.Town.WorldInputNode.SetTarget(station);
            ui.Town.WorldInputNode.TriggerInteract();
            AssertThat(ui.Town.Camera.IsPushedIn)
                .OverrideFailureMessage("test setup invalid — camera never pushed in").IsTrue();

            ui.OpenPanel("Town"); // closes the drawer, same as Esc/click-out

            AssertThat(ui.Drawer.IsOpen).IsFalse();
            AssertThat(ui.Town.Camera.IsPushedIn)
                .OverrideFailureMessage("camera was not released on drawer close").IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>A drawer-to-drawer switch (Forge → Heroes, say) never closes the drawer at all
    /// (<c>DrawerHost.Open</c> replaces in place) — Release only fires on a full close, so the
    /// camera should still read as pushed in immediately after the switch.</summary>
    [TestCase]
    public void SwitchingToAnotherPanel_WithoutClosingDrawer_KeepsCameraPushedIn()
    {
        var ui = MountMainUi();
        try
        {
            var station = ui.Town.FindBuilding("forge-station");
            ui.Town.WorldInputNode.SetTarget(station);
            ui.Town.WorldInputNode.TriggerInteract();

            ui.OpenPanel("Heroes"); // replaces the drawer content, never closes it

            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Heroes");
            AssertThat(ui.Town.Camera.IsPushedIn).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
