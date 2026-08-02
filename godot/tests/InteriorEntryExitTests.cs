#if GDUNIT_TESTS
using System.Linq;
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

    // ── U1 (world-and-interiors plan): the forge tests above predate market/tavern/minegate having
    // rows of their own. These parameterize the same entry/exit/camera-clamp round trip over all
    // four venue keys — the framework claim (KTD-1: a new venue is a table row, not new code) only
    // means something if it holds for every row, not just the one it was proven on first. ──────────

    [TestCase("forge")]
    [TestCase("market")]
    [TestCase("tavern")]
    [TestCase("minegate")]
    public void EnteringAnyVenue_PutsThePlayerInsideItsOwnRoom_NotTheDrawer(string venueKey)
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding(venueKey).RaisePick();

            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage($"Pressing E/clicking '{venueKey}' must put the player INSIDE its room — R1's whole point.")
                .IsTrue();
            AssertThat(ui.Town.InteriorVenueKey).IsEqual(venueKey);
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage($"The drawer must never be the DIRECT response to a '{venueKey}' interact.")
                .IsFalse();

            var room = ui.Town.FindInteriorRoom(venueKey);
            AssertThat(room.RoomRect.HasPoint(ui.Town.Player.GlobalPosition))
                .OverrideFailureMessage($"The player must spawn inside '{venueKey}''s own room rect on entry.")
                .IsTrue();
            AssertThat((float)ui.Town.Cam.LimitLeft).IsEqual(room.RoomRect.Position.X);
            AssertThat((float)ui.Town.Cam.LimitRight).IsEqual(room.RoomRect.Position.X + room.RoomRect.Size.X);
            AssertThat((float)ui.Town.Cam.LimitTop).IsEqual(room.RoomRect.Position.Y);
            AssertThat((float)ui.Town.Cam.LimitBottom).IsEqual(room.RoomRect.Position.Y + room.RoomRect.Size.Y);
        }
        finally { Unmount(ui); }
    }

    [TestCase("forge")]
    [TestCase("market")]
    [TestCase("tavern")]
    [TestCase("minegate")]
    public void ExitingAnyVenue_ReturnsThePlayerToItsOwnOutsideDoor_AndUnclampsTheCamera(string venueKey)
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding(venueKey).RaisePick();
            var outsideDoor = ui.Town.FindBuilding(venueKey).DoorAnchorGlobal;

            ui.Town.ExitInterior(); // the same call the exit zone's BodyEntered signal fires

            AssertThat(ui.Town.InteriorActive).IsFalse();
            AssertThat(ui.Town.InteriorVenueKey).IsNull();
            AssertThat(ui.Town.Player.GlobalPosition)
                .OverrideFailureMessage($"Exiting '{venueKey}' must return the player to ITS OWN outside door, not some other venue's.")
                .IsEqual(outsideDoor);
            AssertThat((float)ui.Town.Cam.LimitRight)
                .IsEqual(TownLayout2D.GridWidth * TownLayout2D.TileSize);
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

            // Wait on the CONDITION, not on layout stability: FocusSection's EnsureControlVisible is
            // deferred to the next idle frame and does not itself change any rect, so the layout can
            // read "settled" for three frames while the scroll is still pending. That is why this
            // passed locally and failed on every CI attempt — see HumanPlayer.WaitUntil's doc.
            room.Stations[0].RaisePick(); // anvil -> craft
            var sawCraft = await player.WaitUntilSees("Work the forge");
            await player.WaitForLayout(ui.Forge);
            AssertThat(sawCraft)
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
            var sawVendor = await player.WaitUntilSees("Buy 1");
            await player.WaitForLayout(ui.Forge);
            AssertThat(sawVendor)
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

    // ── U1 (world-and-interiors plan): each new room's stations, driven through the same
    // real-click path as the forge tests above — every real-verb station opens ITS OWN routed
    // surface (never the wrong one), and every flavor station is a toast, never a dead click. ──────

    [TestCase]
    public void MarketCounterPress_OpensTheShopPanel_OverTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("market").RaisePick();
            var counter = ui.Town.FindInteriorRoom("market").Stations.First(s => s.Key == "counter");

            counter.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Shop");
            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("Opening the Shop panel from a station must NOT exit the market room.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void MarketLedgerPress_IsHonestFlavor_NeverOpensAPanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("market").RaisePick();
            var ledger = ui.Town.FindInteriorRoom("market").Stations.First(s => s.Key == "ledger");

            ledger.RaisePick();

            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("The Ledger Desk has no routed action — MainUi has no 'Ledger' route — so pressing it must never open a panel.")
                .IsFalse();
            var toast = Find<Label>(ui, "RejectionToast");
            AssertThat(toast.Text).IsEqual("You flip through the ledger. Nothing to buy or sell from these pages — try the counter.");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void TavernBarPress_OpensTheTavernPanel_OverTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("tavern").RaisePick();
            var bar = ui.Town.FindInteriorRoom("tavern").Stations.First(s => s.Key == "bar");

            bar.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Tavern");
            AssertThat(ui.Town.InteriorActive).IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void TavernStorywallPress_OpensTheLegendsWall_NotADrawerPanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("tavern").RaisePick();
            var storywall = ui.Town.FindInteriorRoom("tavern").Stations.First(s => s.Key == "storywall");

            storywall.RaisePick();

            AssertThat(ui.Legends.Visible)
                .OverrideFailureMessage("The Story Wall must open the Legends Wall modal — the same route the Tavern's pre-existing 'Legends' action already uses.")
                .IsTrue();
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("Legends is a code-built modal, not a drawer panel.")
                .IsFalse();
            AssertThat(ui.Town.InteriorActive).IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void GatehouseMusterPress_OpensTheDepthsPanel_OverTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("minegate").RaisePick();
            var muster = ui.Town.FindInteriorRoom("minegate").Stations.First(s => s.Key == "muster");

            muster.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Depths");
            AssertThat(ui.Town.InteriorActive).IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void GatehouseBountyLedgerPress_OpensTheBountiesPanel_OverTheRoom()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("minegate").RaisePick();
            var bountyLedger = ui.Town.FindInteriorRoom("minegate").Stations.First(s => s.Key == "bountyledger");

            bountyLedger.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Bounties");
            AssertThat(ui.Town.InteriorActive).IsTrue();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// R4/KTD-2: the ONE new action string this unit adds. Unlike every other real-verb station,
    /// the Mirror is not a drawer panel — it's a code-built modal (same shape as Legends/Bestiary),
    /// so this pins that pressing "The Overlook" reaches <c>ScryingMirror.ShowMirror()</c> without
    /// ever touching the drawer.
    /// </summary>
    [TestCase]
    public void GatehouseOverlookPress_OpensTheMirror_NotADrawerPanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("minegate").RaisePick();
            var overlook = ui.Town.FindInteriorRoom("minegate").Stations.First(s => s.Key == "overlook");

            overlook.RaisePick();

            AssertThat(ui.Mirror.Visible)
                .OverrideFailureMessage("The Overlook's 'Watch' action must open the Mirror (Mirror.ShowMirror()).")
                .IsTrue();
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("The Mirror is a code-built modal, not a drawer panel.")
                .IsFalse();
            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("Opening the Mirror from the overlook must NOT exit the gatehouse room.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void GatehouseWinchPress_IsHonestFlavor_NeverOpensAPanel()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("minegate").RaisePick();
            var winch = ui.Town.FindInteriorRoom("minegate").Stations.First(s => s.Key == "winch");

            winch.RaisePick();

            AssertThat(ui.Drawer.IsOpen).IsFalse();
            AssertThat(ui.Mirror.Visible)
                .OverrideFailureMessage("The winch has no Action — it must never open the Mirror (or anything else).")
                .IsFalse();
            var toast = Find<Label>(ui, "RejectionToast");
            AssertThat(toast.Text).IsEqual("The winch's chain hangs taut. It just raises the gate — try the muster board or the bounty ledger.");
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

            // State the precondition rather than assuming it: if something (a tutorial card, a
            // narrator toast) is open over the room, an EARLIER rung of the #320 ladder correctly
            // eats the key and this test would be measuring the wrong rung.
            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage(
                    "This test needs the room bare — a drawer is open, so Esc is expected to close that "
                    + "first and the room-exit rung never runs. Fix the setup, not the ladder.")
                .IsFalse();

            var player = new HumanPlayer(ui);
            player.Tap(Key.Escape);

            // Poll the condition instead of hoping 3 frames is enough. Input dispatch, the Escape
            // ladder and ExitInterior's teleport/camera-unclamp span an unknown number of frames, and
            // CI (rendering disabled) does not spend them at the same rate a developer machine does.
            var exited = await player.WaitUntil(() => !ui.Town.InteriorActive);

            AssertThat(exited)
                .OverrideFailureMessage("Esc with no drawer/modal open must exit the room — the last rung of the #320 ladder.")
                .IsTrue();
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

    /// <summary>
    /// U1 (world-and-interiors plan, KTD-2): market/tavern/minegate all grew rooms this unit — the
    /// noticeboard is now the ONLY venue still answering E with the bare drawer, exactly as KTD-2
    /// says it should ("a plank board has no inside"). This test used to drive "market" (back when
    /// that venue had no room yet, pre-this-unit); it now targets the one venue that is SUPPOSED to
    /// keep this behavior forever, not one that is about to lose it.
    /// </summary>
    [TestCase]
    public void Noticeboard_StillOpensTheDrawerDirectly_NoRoomByDesign()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("noticeboard").RaisePick();

            AssertThat(ui.Town.InteriorActive)
                .OverrideFailureMessage("KTD-2: the noticeboard has no inside — a plank board has nothing to walk into.")
                .IsFalse();
            AssertThat(ui.Drawer.IsOpen).IsTrue();
            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Bounties");
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

    // U1 (world-and-interiors plan): parameterized over all four rooms — a departure focus beat
    // must not fight ANY room's camera clamp, not just the forge's.
    [TestCase("forge")]
    [TestCase("market")]
    [TestCase("tavern")]
    [TestCase("minegate")]
    public void FocusOnMineGate_IsSuppressed_WhileInsideAnyRoom(string venueKey)
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding(venueKey).RaisePick();
            var before = ui.Town.Cam.GlobalPosition;

            ui.Town.FocusOnMineGate(seconds: 5f);

            AssertThat(ui.Town.Cam.GlobalPosition)
                .OverrideFailureMessage($"A departure focus beat must not fight the '{venueKey}' room's camera clamp.")
                .IsEqual(before);
        }
        finally { Unmount(ui); }
    }
}
#endif
