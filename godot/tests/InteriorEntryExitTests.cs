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

    // ── U3 (painted-interiors plan): stations differentiate — anvil/furnace land on the craft
    // cards, the shelf lands on the vendor rows, the rack opens Shop, and the two flavor stations
    // never open anything at all. ──────────────────────────────────────────────────────────────

    [TestCase]
    public void AnvilPress_OpensForgePanel_ScrolledToTheCraftSection()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var anvil = ui.Town.FindInteriorRoom("forge").Stations[0]; // declared first in InteriorLayout2D
            AssertThat(anvil.Key).IsEqual("anvil");

            anvil.RaisePick();

            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Forge.LastFocusedSection)
                .OverrideFailureMessage("The anvil opens the craft flow — ForgePanel.FocusSection must land on \"craft\".")
                .IsEqual("craft");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void ShelfPress_OpensForgePanel_ScrolledToTheMaterialsSection()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var shelf = ui.Town.FindInteriorRoom("forge").Stations[4]; // declared 5th in InteriorLayout2D
            AssertThat(shelf.Key).IsEqual("shelf");

            shelf.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Forge.LastFocusedSection)
                .OverrideFailureMessage(
                    "The Material Shelf must land ForgePanel on its materials section, not just open "
                    + "the panel at whatever scroll position it last had.")
                .IsEqual("materials");
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// The two tests above only prove INTENT (<c>LastFocusedSection</c>) — a real bug slipped past
    /// exactly that gap during this unit's own build: <c>ScrollContainer.EnsureControlVisible</c>,
    /// called immediately after the drawer opens, measured against the drawer's still-mid-slide,
    /// still-uncomputed layout and silently scrolled nowhere (a receipt.ps1 capture caught it — see
    /// <c>ForgePanel.DeferEnsureVisible</c>'s own doc). This test drives the SAME production path
    /// (a real station <c>RaisePick</c>) and observes with <see cref="HumanPlayer"/> — "only what a
    /// person could actually read on screen right now" — so a regression back to "scrolled nowhere"
    /// fails HERE, not just in a screenshot a human has to remember to look at.
    /// </summary>
    [TestCase]
    public async System.Threading.Tasks.Task AnvilThenShelfPress_ActuallyScrollToDifferentVisibleContent()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");
            var player = new HumanPlayer(ui);

            room.Stations[0].RaisePick(); // anvil -> craft
            await player.WaitForLayout(ui.Forge);
            AssertThat(player.Sees("Work the forge"))
                .OverrideFailureMessage(
                    "Anvil press must actually scroll the recipe cards into view — a recipe card's own "
                    + "\"Work the forge\" button must be readable on screen, not merely intended.")
                .IsTrue();
            AssertThat(player.Sees("Buy 1"))
                .OverrideFailureMessage(
                    "Anvil press landed on craft — the vendor's \"Buy 1\" buttons must have scrolled "
                    + "out of view, not still be sitting on screen from the panel's default open position.")
                .IsFalse();

            room.Stations[4].RaisePick(); // shelf -> materials (same open panel, re-focused)
            await player.WaitForLayout(ui.Forge);
            AssertThat(player.Sees("Buy 1"))
                .OverrideFailureMessage("Shelf press must actually scroll the vendor's \"Buy 1\" rows into view.")
                .IsTrue();
            AssertThat(player.Sees("Work the forge"))
                .OverrideFailureMessage("Shelf press landed on materials — the recipe cards must have scrolled back out of view.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void RackPress_OpensTheShopPanel_NeverForge()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var rack = ui.Town.FindInteriorRoom("forge").Stations[5]; // declared last in InteriorLayout2D
            AssertThat(rack.Key).IsEqual("rack");

            rack.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId)
                .OverrideFailureMessage("Finished Goods Rack is the stock-and-prices verb — it must open Shop, not Forge.")
                .IsEqual("Shop");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void FlavorStationPress_NeverOpensAPanel_ShowsOneToastLineInstead()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var bellows = ui.Town.FindInteriorRoom("forge").Stations[2]; // declared 3rd in InteriorLayout2D
            AssertThat(bellows.Key).IsEqual("bellows");

            bellows.RaisePick();

            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage(
                    "A flavor station (null Action) must never open a panel — that would be a fake "
                    + "verb dressed up as honesty, not honest flavor.")
                .IsFalse();
            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("A flavor click is a toast, not an exit — the room stays open.")
                .IsTrue();

            var toast = Find<Label>(ui, "RejectionToast");
            AssertThat(toast.Text)
                .OverrideFailureMessage("The flavor click must show its one-line response as a toast — never silently nothing.")
                .IsEqual("You give the bellows a pump. The furnace does the real work.");
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
    public void EnteringAndExitingTheRoom_EngagesAndReleasesTheClockLatch()
    {
        // U4: ModalOwnsTheScreen now reads Town.InteriorActive (replacing the deleted, always-false
        // InteriorStage.IsOpen) — the room genuinely covers the screen like a modal, so entering it
        // must engage PhaseClock.Engaged, and Town.InteriorExited (wired to MainUi.OnInteriorExited)
        // must release it again on the way out, mirroring every other modal open/close pair.
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Clock.Engaged).IsFalse();

            ui.Town.FindBuilding("forge").RaisePick();
            AssertThat(ui.Clock.Engaged)
                .OverrideFailureMessage(
                    "The walkable room covers the screen exactly like a modal — entering it must "
                    + "engage the clock latch the same way opening any other modal already does.")
                .IsTrue();

            ui.Town.ExitInterior(); // the same call the exit zone's BodyEntered signal fires
            AssertThat(ui.Clock.Engaged)
                .OverrideFailureMessage("Leaving the room must release the latch — it must not stay stuck engaged.")
                .IsFalse();
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
