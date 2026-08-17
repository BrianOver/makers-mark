#if GDUNIT_TESTS
using System;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T2-6 (Wave A substrate, §11.14.4): <see cref="TutorialAnchorKind.PanelControl"/> — the
/// mechanism §11.14.4 names as missing ("the tutorial can point at panels and at world positions
/// but not at an individual control inside a panel"). No <see cref="TutorialFlow.Registry"/> row
/// uses this kind yet (Wave C/E's own units are its first real callers) — these tests drive <see
/// cref="TutorialOverlay.RefreshAnchor"/> directly against a real, always-mounted panel control
/// (<c>ForgePanel</c>'s "MaterialSelect", built once at boot and never conditionally omitted), the
/// same standalone-mechanism-proof shape <c>WorkshopVocabTests</c> uses for <c>WorkshopRoomFor</c>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PanelControlAnchorTests
{
    private const string TargetPanelId = "Forge";
    private const string TargetControlName = "MaterialSelect"; // ForgePanel's own material dropdown — built once in BuildUi, never conditionally omitted.

    [TestCase]
    public void ForPanelControl_Factory_SetsKindKeyAndControlName()
    {
        var anchor = TutorialAnchor.ForPanelControl(TargetPanelId, TargetControlName);

        AssertThat(anchor.Kind).IsEqual(TutorialAnchorKind.PanelControl);
        AssertThat(anchor.Key).IsEqual(TargetPanelId);
        AssertThat(anchor.ControlName).IsEqual(TargetControlName);
    }

    [TestCase]
    public void PanelControl_Resolves_EvenWhileItsOwnPanelIsClosed()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Drawer.IsOpen).IsFalse();

            ui.Overlay.RefreshAnchor(TutorialAnchor.ForPanelControl(TargetPanelId, TargetControlName), ui.Town, ui.Drawer, ui);

            AssertThat(ui.Overlay.PulsingHudControlName)
                .OverrideFailureMessage("A PanelControl anchor must resolve its target control REGARDLESS of whether the panel is currently open — every registered panel stays permanently mounted.")
                .IsEqual(TargetControlName);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task PanelControl_HidesTheOutline_WhileItsOwnPanelIsClosed()
    {
        var ui = MountMainUi();
        try
        {
            ui.Overlay.RefreshAnchor(TutorialAnchor.ForPanelControl(TargetPanelId, TargetControlName), ui.Town, ui.Drawer, ui);
            await SettleLayout(ui);
            ui.Overlay.Tick(0.016);

            AssertThat(Find<ColorRect>(ui, "TutorialOverlayTop").Visible)
                .OverrideFailureMessage("The outline is drawn while the target panel is not even open — a PanelControl anchor must never point at a control the player cannot currently see.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Real Godot engine timing, not a synchronous fiction: <see cref="Ui.DrawerHost.Open"/>'s own
    /// visibility flip cascades through <see cref="Control.IsVisibleInTree"/> via the engine's
    /// notification pipeline, and a nested <see cref="Container"/> tree's own deferred
    /// <c>queue_sort</c> can still be mid-cascade the instant this method returns (the exact
    /// documented hazard <see cref="SettleLayout"/> exists for). This test therefore awaits a real
    /// settle — a bounded number of ACTUAL engine frames, never a stand-in for wall-clock duration —
    /// before reading <see cref="TutorialOverlay"/>'s output, the same discipline
    /// <c>ForgeRoom_PerimeterWalls_BlockThePlayer</c> already applies to physics.
    ///
    /// <para><b>Ordering, not timing, was the original defect here.</b> <c>MainUi.OpenPanel</c> is
    /// itself a real production call path: it ends by calling <c>RefreshObjectiveLine</c>, which
    /// re-derives the tutorial's own anchor (<c>Tutorial.Active ? Tutorial.CurrentAnchor : ...</c>)
    /// and re-invokes <see cref="TutorialOverlay.RefreshAnchor"/> with THAT value. On a fresh
    /// campaign the live chain's own current step is active and its anchor is NOT this test's
    /// PanelControl anchor (no <see cref="TutorialFlow.Registry"/> row uses this kind yet — class
    /// doc), so calling <c>OpenPanel</c> AFTER manually setting the anchor silently overwrote it
    /// with the chain's own unrelated anchor before a single frame was ever pumped — a real ordering
    /// bug in the test, not a timing one, and not a defect in <see cref="TutorialOverlay"/> itself
    /// (in real gameplay a PanelControl-anchored step's own <c>OpenPanel</c> re-refresh is a genuine
    /// no-op: <c>Tutorial.CurrentAnchor</c> returns the SAME anchor before and after, and
    /// <c>RefreshAnchor</c>'s own "same anchor is already active" guard skips the redundant work).
    /// Opening the panel FIRST, then setting the anchor this test actually means to prove, is what
    /// makes this test exercise its own claim rather than a value <c>OpenPanel</c> already
    /// clobbered.</para>
    /// </summary>
    [TestCase]
    public async Task PanelControl_ShowsTheOutline_OnceItsOwnPanelOpens()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel(TargetPanelId);
            ui.Overlay.RefreshAnchor(TutorialAnchor.ForPanelControl(TargetPanelId, TargetControlName), ui.Town, ui.Drawer, ui);
            await SettleLayout(ui);
            ui.Overlay.Tick(0.016);

            AssertThat(Find<ColorRect>(ui, "TutorialOverlayTop").Visible)
                .OverrideFailureMessage("The outline did not appear once the PanelControl anchor's own panel opened.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Same ordering fix as <see cref="PanelControl_ShowsTheOutline_OnceItsOwnPanelOpens"/>'s
    /// own doc: open the (different) panel FIRST, so <c>OpenPanel</c>'s own internal anchor refresh
    /// cannot clobber the anchor this test sets afterward — without this fix the test happened to
    /// still pass, but for the wrong reason (the clobbered chain-anchor also resolves to no
    /// outline), which is exactly the "green test hiding what it claims to prove" trap.</summary>
    [TestCase]
    public async Task PanelControl_HidesTheOutline_WhileADifferentPanelIsOpen()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Shop"); // a DIFFERENT panel than the anchor names
            ui.Overlay.RefreshAnchor(TutorialAnchor.ForPanelControl(TargetPanelId, TargetControlName), ui.Town, ui.Drawer, ui);
            await SettleLayout(ui);
            ui.Overlay.Tick(0.016);

            AssertThat(Find<ColorRect>(ui, "TutorialOverlayTop").Visible)
                .OverrideFailureMessage("The outline is showing while a DIFFERENT panel than the one named is open — a PanelControl anchor must be scoped to its own panel.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PanelControl_UnknownPanelId_ThrowsRatherThanPointingAtNothing()
    {
        var ui = MountMainUi();
        try
        {
            AssertThrown(() => ui.Overlay.RefreshAnchor(
                    TutorialAnchor.ForPanelControl("NotARealDrawerPanel", TargetControlName), ui.Town, ui.Drawer, ui))
                .IsInstanceOf<InvalidOperationException>();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PanelControl_UnknownControlName_ThrowsRatherThanPointingAtNothing()
    {
        var ui = MountMainUi();
        try
        {
            AssertThrown(() => ui.Overlay.RefreshAnchor(
                    TutorialAnchor.ForPanelControl(TargetPanelId, "NotARealControlOnThisPanel"), ui.Town, ui.Drawer, ui))
                .IsInstanceOf<InvalidOperationException>();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
