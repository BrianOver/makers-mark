#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T9-6 (§11.14.13): the anchor kind built to point at controls inside panels could not reach any
/// surface the course's payoff happens on.
///
/// <para><b>The gap.</b> Ten panels are registered with the drawer. The <b>Ledger, Commissions,
/// Legends, Camp and Forecast are not</b> — they are modal siblings mounted straight onto
/// <c>MainUi</c> — and <c>TutorialOverlay</c> resolved a <c>PanelControl</c> anchor only through
/// <c>DrawerHost.PanelContent</c>. So the proof card, the rite, accept/decline and the vigil card
/// were all unreachable, and every T9 beat lives on one of them. The kind had exactly one caller and
/// zero registry rows, so nothing had ever noticed.</para>
///
/// <para><b>The second half.</b> A <c>PanelControl</c> target that is not on screen draws nothing —
/// <c>Tick</c> hides the outline for anything not visible in tree, which is correct and is also
/// silence. So a beat pointing inside a closed panel highlights nothing, which is the
/// station-behind-a-wall defect (U-T9-5) arriving one level further in. The anchor now points at the
/// way IN until the player is in.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PanelControlReachesModalsTests
{
    /// <summary>The five modal surfaces the course has to be able to point into.</summary>
    private static readonly string[] Modals = ["Ledger", "Commissions", "Legends", "Camp", "Forecast"];

    [TestCase]
    public void EveryModalSurface_ResolvesAsAnAnchorScope()
    {
        var ui = MountMainUi();
        try
        {
            foreach (var id in Modals)
            {
                AssertThat(ui.ModalContent(id))
                    .OverrideFailureMessage(
                        $"\"{id}\" hosts a beat the T9 course must point at, and it resolves to no "
                        + "anchor scope — so a PanelControl row naming it would throw.")
                    .IsNotNull();
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>The drawer's own panels must keep resolving through the drawer — the modal lookup is
    /// an addition, never a replacement, and an unknown id must still throw rather than point at
    /// nothing (the house rule this kind was built around).</summary>
    [TestCase]
    public void AnUnknownSurface_StillThrows_RatherThanPointingAtNothing()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.ModalContent("NotASurface")).IsNull();

            AssertThrown(() => ui.Overlay.RefreshAnchor(
                    TutorialAnchor.ForPanelControl("NotASurface", "AnyControl"), ui.Town, ui.Drawer, ui))
                .IsInstanceOf<System.InvalidOperationException>();
        }
        finally { Unmount(ui); }
    }

    /// <summary>A control inside a modal resolves and, once that modal is on screen, actually draws.
    /// Driven against the Legends wall's own always-present title, so the assertion is about the
    /// mechanism rather than about a row the sim may or may not have produced.</summary>
    [TestCase]
    public async System.Threading.Tasks.Task AControlInsideAModal_DrawsOnceThatModalIsOnScreen()
    {
        var ui = MountMainUi();
        try
        {
            ui.Legends.ShowWall(ui.Adapter.CurrentState);
            await SettleLayout(ui);

            ui.Overlay.RefreshAnchor(
                TutorialAnchor.ForPanelControl("Legends", "LegendsWallTitle"), ui.Town, ui.Drawer, ui);
            ui.Overlay.Tick(0.1);

            AssertThat(ui.Overlay.PulsingHudControlName)
                .OverrideFailureMessage(
                    "A control inside an open modal must be the outline's target — this is the whole "
                    + "point of the modal scope.")
                .IsEqual("LegendsWallTitle");
        }
        finally { Unmount(ui); }
    }

    /// <summary>The approach phase. While the surface is closed, the anchor aims at the way in — the
    /// venue building for a walkable surface, the tray button for a modal — rather than at a control
    /// nobody can see.</summary>
    [TestCase]
    public void WhileTheSurfaceIsClosed_TheAnchorAimsAtTheWayIn()
    {
        // Walkable surfaces approach via their own building.
        foreach (var (panelId, venue) in new[] { ("Forge", "forge"), ("Shop", "market"), ("Bounties", "noticeboard") })
        {
            var aimed = TutorialFlow.AimAnchor(TutorialAnchor.ForPanelControl(panelId, "AnyControl"), openPanelId: null);
            AssertThat(aimed.Kind)
                .OverrideFailureMessage($"{panelId} is reached by walking to {venue}, so that building is the way in.")
                .IsEqual(TutorialAnchorKind.Building);
            AssertThat(aimed.Key).IsEqual(venue);
        }

        // Tray-only surfaces approach via their Open{id} button — the convention
        // MainUi.RegisterGatedTrayButton already established.
        foreach (var panelId in new[] { "Ledger", "Commissions", "Legends", "Forecast" })
        {
            var aimed = TutorialFlow.AimAnchor(TutorialAnchor.ForPanelControl(panelId, "AnyControl"), openPanelId: null);
            AssertThat(aimed.Kind)
                .OverrideFailureMessage($"{panelId} has no venue, so the tray button is the only way in.")
                .IsEqual(TutorialAnchorKind.Hud);
            AssertThat(aimed.Key).IsEqual($"Open{panelId}");
        }
    }

    /// <summary>And once the player is on that surface, the anchor hands off to the control itself —
    /// otherwise a course would pulse a doorway at somebody already standing in the room.</summary>
    [TestCase]
    public void OnceTheSurfaceIsOpen_TheAnchorHandsOffToTheControl()
    {
        foreach (var panelId in new[] { "Forge", "Shop", "Ledger", "Legends", "Camp" })
        {
            var declared = TutorialAnchor.ForPanelControl(panelId, "AnyControl");
            var aimed = TutorialFlow.AimAnchor(declared, openPanelId: panelId);

            AssertThat(aimed)
                .OverrideFailureMessage(
                    $"With {panelId} on screen the declared control is the thing worth pointing at; "
                    + "pulsing the way in tells a player nothing they have not already done.")
                .IsEqual(declared);
        }
    }

    /// <summary>Every venue in the walk-in map round-trips, so the two directions cannot drift apart
    /// and leave a surface with no way in.</summary>
    [TestCase]
    public void TheVenueAndPanelMapsAreInverses()
    {
        foreach (var panelId in new[] { "Forge", "Shop", "Tavern", "Depths", "Bounties" })
        {
            var venue = TutorialFlow.VenueForPanel(panelId);
            AssertThat(venue).IsNotNull();
            AssertThat(TutorialFlow.PanelIdForVenue(venue!))
                .OverrideFailureMessage(
                    $"VenueForPanel(\"{panelId}\") gave \"{venue}\", which does not map back. A "
                    + "one-way mapping means some surface's approach phase points at the wrong door.")
                .IsEqual(panelId);
        }
    }
}
#endif
