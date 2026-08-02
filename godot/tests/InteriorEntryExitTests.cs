#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U1 (painted-interiors plan): the full E-at-the-Forge → walkable room → station → drawer-over-
/// the-room → exit round trip, driven through the REAL production path
/// (<c>Building2D.RaisePick</c> — exactly what a click or E-interact fires, mirroring <see
/// cref="Town2DSceneTests.Town2D_ForgeRaisePick_FiresBuildingClickedWithForgeKey"/> and <see
/// cref="FullPlaytest"/>'s own building-click sweep) rather than calling
/// <see cref="Town2D.EnterInterior"/> directly.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InteriorEntryExitTests
{
    [TestCase]
    public void InteractingWithForge_EntersTheRoom_NotTheDrawer()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();

            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("Pressing E/clicking the Forge must put the player INSIDE the room — R1's whole point.")
                .IsTrue();
            AssertThat(ui.Town.InteriorVenueKey).IsEqual("forge");
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("The drawer must never be the DIRECT response to a Forge interact.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void EnteringTheRoom_PlacesThePlayerInsideItAndClampsTheCameraToTheRoomRect()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");

            AssertThat(room.RoomRect.HasPoint(ui.Town.Player.GlobalPosition))
                .OverrideFailureMessage("The player must spawn inside the room's own rect on entry.")
                .IsTrue();

            AssertThat((float)ui.Town.Cam.LimitLeft).IsEqual(room.RoomRect.Position.X);
            AssertThat((float)ui.Town.Cam.LimitRight).IsEqual(room.RoomRect.Position.X + room.RoomRect.Size.X);
            AssertThat((float)ui.Town.Cam.LimitTop).IsEqual(room.RoomRect.Position.Y);
            AssertThat((float)ui.Town.Cam.LimitBottom).IsEqual(room.RoomRect.Position.Y + room.RoomRect.Size.Y);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void StationPress_OpensTheRoutedForgePanel_OverTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var anvil = ui.Town.FindInteriorRoom("forge").Stations[0]; // declared first in InteriorLayout2D
            AssertThat(anvil.Key).IsEqual("anvil");

            anvil.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage(
                    "Opening the Forge panel from a station must NOT exit the room — KTD-4: the "
                    + "drawer slides over the room, which reads as the world behind it, not instead of it.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void ExitZone_ReturnsThePlayerOutside_AndUnclampsTheCamera()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var outsideDoor = ui.Town.FindBuilding("forge").DoorAnchorGlobal;

            ui.Town.ExitInterior(); // the same call the exit zone's BodyEntered signal fires

            AssertThat(ui.Town.InteriorActive).IsFalse();
            AssertThat(ui.Town.InteriorVenueKey).IsNull();
            AssertThat(ui.Town.Player.GlobalPosition).IsEqual(outsideDoor);
            AssertThat((float)ui.Town.Cam.LimitRight)
                .IsEqual(TownLayout2D.GridWidth * TownLayout2D.TileSize);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public async System.Threading.Tasks.Task Escape_WithNoDrawerOpen_ExitsTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            AssertThat(ui.Town.InteriorActive).IsTrue();

            var player = new HumanPlayer(ui);
            player.Tap(Key.Escape);
            await player.Frames(3);

            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("Esc with no drawer/modal open must exit the room — the last rung of the #320 ladder.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public async System.Threading.Tasks.Task Escape_WithADrawerOpenOverTheRoom_ClosesTheDrawerFirst_NotTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            ui.OpenPanel("Forge");
            AssertThat(ui.Drawer.IsOpen).IsTrue();

            var player = new HumanPlayer(ui);
            player.Tap(Key.Escape);
            await player.Frames(3);

            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("Esc priority (#320 ladder): the drawer over the room must close FIRST.")
                .IsFalse();
            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("The SAME Esc press that closed the drawer must not ALSO exit the room.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void OtherVenues_StillOpenTheDrawerDirectly_NoRoomYet()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("market").RaisePick();

            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("R9: slice 1 is the Forge only — every other venue keeps today's drawer behavior.")
                .IsFalse();
            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Shop");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void FocusOnMineGate_IsSuppressed_WhileInsideTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var before = ui.Town.Cam.GlobalPosition;

            ui.Town.FocusOnMineGate(seconds: 5f);

            AssertThat(ui.Town.Cam.GlobalPosition)
                .OverrideFailureMessage("A departure focus beat must not fight the room's camera clamp.")
                .IsEqual(before);
        }
        finally { Unmount(ui); }
    }
}
#endif
