#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Owner playtest complaint (2026-08): "The workbench hall menu needs divided up - its confusing
/// to have the vendor on the same page as the crafting etc. Remember the entire point of having
/// different things to click on inside is to help sort this sort of menu lol." Before this unit,
/// every Forge-routed station (Workbench/Gear Rack in the engineering Workbench Hall, or the
/// default blacksmith's Anvil/Material Shelf) opened the exact same forge panel and only scrolled
/// to a different starting row — the vendor rows and the recipe/talent rows both stayed mounted
/// and reachable by scrolling no matter which station opened the panel, so two different stations
/// opened what was functionally one merged page.
///
/// <para>This file drives the REAL station-click path (<c>Building2D.RaisePick</c>, the same
/// production path <c>StationIdentityTests</c> already uses for the anvil/furnace collision) and
/// asserts the fix directly through <c>ForgePanel.MaterialsViewVisible</c>/<c>CraftViewVisible</c>
/// (test-inspection surfaces mirroring the existing <c>LastFocusedSection</c> idiom): a
/// materials-focused station must never show the craft controls, and a craft-focused station must
/// never show the vendor/foundry controls, in either direction, in the same session.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class StationSplitTests
{
    /// <summary>Switches the live campaign to the Engineering-only Workbench Hall — the exact room
    /// named in the owner's complaint (Workbench/Gear Rack/Parts Crate/Flywheel) — via the real
    /// <c>SetProfessionsAction</c> path (a bell-rider: queues now, resolves at the next day
    /// boundary, mirroring <c>InteriorEntryExitTests.SecondProfessionAddedMidRun...</c>), then
    /// enters the shared workshop room.</summary>
    private static InteriorRoom2D EnterWorkbenchHall(MainUi ui)
    {
        ui.Adapter.Queue(new SetProfessionsAction(ImmutableSortedSet.Create(EngineeringProfession.Id)));
        AdvanceDay(ui); // SetProfessionsAction resolves at the next day boundary, not immediately
        ui.Town.FindBuilding("forge").RaisePick(); // rebuilds the workshop room for the new selection
        return ui.Town.FindInteriorRoom("forge");
    }

    [TestCase]
    public void WorkbenchStation_OpensCraftOnly_NeverShowsTheVendorOrFoundry()
    {
        var ui = MountMainUi();
        try
        {
            var room = EnterWorkbenchHall(ui);
            room.Stations.First(s => s.Key == "bench").RaisePick();

            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Forge.LastFocusedSection).IsEqual("craft");
            AssertThat(ui.Forge.CraftViewVisible)
                .OverrideFailureMessage("The Workbench's own job is crafting — its craft view must be on screen.")
                .IsTrue();
            AssertThat(ui.Forge.MaterialsViewVisible)
                .OverrideFailureMessage("The Workbench opened the SAME page the Gear Rack does — the owner's exact complaint.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void GearRackStation_OpensMaterialsOnly_NeverShowsCraftingOrTalents()
    {
        var ui = MountMainUi();
        try
        {
            var room = EnterWorkbenchHall(ui);
            room.Stations.First(s => s.Key == "gear-rack").RaisePick();

            AssertThat(ui.Drawer.CurrentPanelId).IsEqual("Forge");
            AssertThat(ui.Forge.LastFocusedSection).IsEqual("materials");
            AssertThat(ui.Forge.MaterialsViewVisible)
                .OverrideFailureMessage("The Gear Rack's own job is buying materials — its materials view must be on screen.")
                .IsTrue();
            AssertThat(ui.Forge.CraftViewVisible)
                .OverrideFailureMessage("The Gear Rack exposed the crafting/talent controls — exactly the merged page the owner reported.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void SwitchingBetweenWorkbenchAndGearRack_NeverLeavesBothViewsOnScreenAtOnce()
    {
        var ui = MountMainUi();
        try
        {
            var room = EnterWorkbenchHall(ui);

            room.Stations.First(s => s.Key == "bench").RaisePick();
            AssertThat(ui.Forge.CraftViewVisible && ui.Forge.MaterialsViewVisible)
                .OverrideFailureMessage("Pressing the Workbench must not leave the vendor view mounted too.")
                .IsFalse();

            room.Stations.First(s => s.Key == "gear-rack").RaisePick();
            AssertThat(ui.Forge.CraftViewVisible && ui.Forge.MaterialsViewVisible)
                .OverrideFailureMessage("Pressing the Gear Rack must not leave the craft view mounted too.")
                .IsFalse();
            AssertThat(ui.Forge.MaterialsViewVisible).IsTrue();

            // Back to the Workbench in the SAME session — the split must not be a one-way ratchet
            // (the exact "pages merging back together later" failure the task calls out).
            room.Stations.First(s => s.Key == "bench").RaisePick();
            AssertThat(ui.Forge.CraftViewVisible).IsTrue();
            AssertThat(ui.Forge.MaterialsViewVisible).IsFalse();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void PartsCrateStation_StillOpensTheSeparateSellPanel_NeverTheForgePanel()
    {
        var ui = MountMainUi();
        try
        {
            var room = EnterWorkbenchHall(ui);
            room.Stations.First(s => s.Key == "parts-crate").RaisePick();

            AssertThat(ui.Drawer.CurrentPanelId)
                .OverrideFailureMessage("Parts Crate's own job is selling — it must open Shop, never the crafting/vendor Forge panel.")
                .IsEqual("Shop");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void FlywheelStation_IsHonestFlavor_NeverOpensAPanel()
    {
        var ui = MountMainUi();
        try
        {
            var room = EnterWorkbenchHall(ui);
            AssertThat(ui.Drawer.IsOpen).IsFalse(); // setup check: entering the room alone opens no drawer

            room.Stations.First(s => s.Key == "flywheel").RaisePick();

            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("The Flywheel has no verb (InteriorLayout2D: Action null) — it must never crack open a drawer.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>A bare (non-station) open — Camp's "Forge something for them" shortcut, or a
    /// playtest tool's direct <c>OpenPanel("Forge")</c> — must still show the FULL panel, even
    /// right after a station visit narrowed it. Without <c>ForgePanel.ResetFocus</c> (wired into
    /// <c>MainUi.OpenPanel</c>) this would silently inherit whichever half the last room visit left
    /// it on, breaking every existing test/tool that opens "Forge" directly and expects the whole
    /// panel (vendor rows AND recipe cards) to be there.
    ///
    /// <para><b>U-T7-1 (register #149, owner ruling 2026-08-18) rewrote what this asserts, and it is
    /// worth being explicit about what did and did not change.</b> This test used to require the bare
    /// open to show the FULL merged panel — every section at once — and that was correct against the
    /// bug it was written for. But the merged panel is the state in the owner's own
    /// <c>jank_menu.jpg</c>, and asked what a Forge opened from a button should show he answered "do
    /// the separate menus". So the bare open now lands on ONE section. The property this test exists
    /// to protect is untouched and is still the only thing it checks: a bare open never inherits a
    /// previous room visit's narrowing. It lands on the SAME section every time, whichever station
    /// was last pressed — which is a strictly stronger statement than "shows everything", since
    /// "everything" was reachable from a stale state by accident. <c>ForgeMenuSplitTests</c> owns
    /// which section that is.</para>
    /// </summary>
    [TestCase]
    public void BareOpenPanelForge_AfterAStationNarrowedIt_NeverInheritsTheStationsNarrowing()
    {
        var ui = MountMainUi();
        try
        {
            var room = EnterWorkbenchHall(ui);

            ui.OpenPanel("Forge"); // the non-station, direct-open path, from a clean slate
            var bareOpenSection = ui.Forge.LastFocusedSection;
            AssertThat(bareOpenSection)
                .OverrideFailureMessage("A bare open must land on a NAMED section, never the merged view.")
                .IsNotNull();

            room.Stations.First(s => s.Key == "gear-rack").RaisePick();
            AssertThat(ui.Forge.LastFocusedSection)
                .OverrideFailureMessage("Setup check: the Gear Rack must have narrowed the panel to its own job.")
                .IsEqual("materials");

            ui.OpenPanel("Forge"); // the same bare open again, now with a station's narrowing behind it

            AssertThat(ui.Forge.LastFocusedSection)
                .OverrideFailureMessage(
                    "A bare open must land on the same section every time. Inheriting whatever the last "
                    + "room visit narrowed the panel to is the bug this test was written for, and it is "
                    + "unchanged by the sections becoming separate menus.")
                .IsEqual(bareOpenSection);
        }
        finally { Unmount(ui); }
    }

    /// <summary>The fix is not Engineering-specific — every profession's forge room shares the same
    /// forge panel and <c>FocusSection</c> mechanism, so the default blacksmith start (Anvil vs
    /// Material Shelf) must show the identical split.</summary>
    [TestCase]
    public void BlacksmithDefault_ShelfStation_OpensMaterialsOnly_AnvilStation_OpensCraftOnly()
    {
        var ui = MountMainUi(); // default seed-2026 campaign — blacksmith
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");

            room.Stations.First(s => s.Key == "shelf").RaisePick();
            AssertThat(ui.Forge.MaterialsViewVisible).IsTrue();
            AssertThat(ui.Forge.CraftViewVisible).IsFalse();

            room.Stations.First(s => s.Key == "anvil").RaisePick();
            AssertThat(ui.Forge.CraftViewVisible).IsTrue();
            AssertThat(ui.Forge.MaterialsViewVisible).IsFalse();
        }
        finally { Unmount(ui); }
    }
}
#endif
