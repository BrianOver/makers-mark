#if GDUNIT_TESTS
using System;
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
    public void PanelControl_HidesTheOutline_WhileItsOwnPanelIsClosed()
    {
        var ui = MountMainUi();
        try
        {
            ui.Overlay.RefreshAnchor(TutorialAnchor.ForPanelControl(TargetPanelId, TargetControlName), ui.Town, ui.Drawer, ui);
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

    [TestCase]
    public void PanelControl_ShowsTheOutline_OnceItsOwnPanelOpens()
    {
        var ui = MountMainUi();
        try
        {
            ui.Overlay.RefreshAnchor(TutorialAnchor.ForPanelControl(TargetPanelId, TargetControlName), ui.Town, ui.Drawer, ui);
            ui.OpenPanel(TargetPanelId);
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

    [TestCase]
    public void PanelControl_HidesTheOutline_WhileADifferentPanelIsOpen()
    {
        var ui = MountMainUi();
        try
        {
            ui.Overlay.RefreshAnchor(TutorialAnchor.ForPanelControl(TargetPanelId, TargetControlName), ui.Town, ui.Drawer, ui);
            ui.OpenPanel("Shop"); // a DIFFERENT panel than the anchor names
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
