#if GDUNIT_TESTS
using System;
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U9 (§11.14.14): <see cref="TutorialSurfaceRegistry"/> is the one roster that replaced two
/// hardcoded lists which quietly disagreed with the live game — <c>MainUi.PanelFor</c>'s own
/// ten-arm switch (a duplicate of <see cref="DrawerHost"/>'s real registrations) and
/// <c>MainUi.ModalContent</c>'s five-arm switch, which MISSED five real MainUi-mounted surfaces
/// (the Scrying Mirror, the Bestiary, the Chronicle, the PiP dock, the Companion Docket). This suite
/// pins the roster's own contract: every surface resolves its content root; every surface either
/// names a reachable way in or is on the class doc's own named "no live way in yet" list; an id the
/// roster does not know throws with its own name in the message; and the Mirror's non-conforming way
/// in (named for watching, never "OpenMirror") resolves correctly.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialSurfaceRegistryTests
{
    /// <summary>The class doc's own named list of surfaces that are reachable once open but have no
    /// live door in today's build — see <see cref="TutorialSurfaceRegistry"/>'s class doc for why
    /// each one is honestly null rather than a manufactured anchor. A surface landing here that is
    /// NOT in this array (or vice versa) is exactly the drift this test exists to catch.</summary>
    private static readonly string[] SurfacesWithNoWayInYet = ["Heroes", "Bestiary", "Chronicle", "Pip"];

    [TestCase]
    public void EverySurface_ResolvesItsContentRoot()
    {
        var ui = MountMainUi();
        try
        {
            foreach (var id in TutorialSurfaceRegistry.Ids)
            {
                var root = TutorialSurfaceRegistry.ContentRootFor(id, ui.Drawer, ui);
                AssertThat(root)
                    .OverrideFailureMessage($"\"{id}\"'s ContentRoot did not resolve to a live Control.")
                    .IsNotNull();
            }
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void ExactlyTheDocumentedSurfaces_DeclareNoWayInYet()
    {
        var actual = TutorialSurfaceRegistry.Surfaces
            .Where(s => s.WayIn is null)
            .Select(s => s.Id)
            .OrderBy(s => s, StringComparer.Ordinal)
            .ToList();
        var expected = SurfacesWithNoWayInYet.OrderBy(s => s, StringComparer.Ordinal).ToList();

        AssertThat(actual)
            .OverrideFailureMessage(
                "A surface's WayIn is null but this test's own allowlist does not name it (or the "
                + "allowlist names a surface that now HAS a way in) — update whichever one drifted: "
                + $"actual=[{string.Join(", ", actual)}] expected=[{string.Join(", ", expected)}]")
            .IsEqual(expected);
    }

    [TestCase]
    public void EverySurfaceWithADeclaredWayIn_ResolvesToARealOnScreenTarget()
    {
        var ui = MountMainUi();
        try
        {
            foreach (var def in TutorialSurfaceRegistry.Surfaces)
            {
                if (def.WayIn is not { } anchor)
                {
                    continue; // covered by ExactlyTheDocumentedSurfaces_DeclareNoWayInYet
                }

                switch (anchor.Kind)
                {
                    case TutorialAnchorKind.Building:
                        Building2D? building = null;
                        Exception? thrown = null;
                        try { building = ui.Town.FindBuilding(anchor.Key!); }
                        catch (Exception ex) { thrown = ex; }

                        AssertThat(thrown)
                            .OverrideFailureMessage(
                                $"{def.Id}'s WayIn building \"{anchor.Key}\" does not resolve: {thrown}")
                            .IsNull();
                        AssertThat(building).IsNotNull();
                        break;

                    case TutorialAnchorKind.Hud:
                        var control = ui.FindChild(anchor.Key!, recursive: true, owned: false) as Control;
                        AssertThat(control)
                            .OverrideFailureMessage(
                                $"{def.Id}'s WayIn Hud control \"{anchor.Key}\" does not resolve to a live Control.")
                            .IsNotNull();
                        break;

                    case TutorialAnchorKind.PanelControl:
                        var scope = TutorialSurfaceRegistry.ContentRootFor(anchor.Key!, ui.Drawer, ui);
                        AssertThat(scope)
                            .OverrideFailureMessage(
                                $"{def.Id}'s WayIn panel scope \"{anchor.Key}\" does not resolve.")
                            .IsNotNull();
                        var inner = scope?.FindChild(anchor.ControlName!, recursive: true, owned: false) as Control;
                        AssertThat(inner)
                            .OverrideFailureMessage(
                                $"{def.Id}'s WayIn control \"{anchor.Key}/{anchor.ControlName}\" does not "
                                + "resolve inside its own panel.")
                            .IsNotNull();
                        break;

                    default:
                        throw new NotSupportedException(
                            $"{def.Id}: WayIn kind {anchor.Kind} has no resolution case in this test — " +
                            "add one rather than skipping it silently.");
                }
            }
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void MirrorsWayIn_IsTheWatchButton_NotTheOpenMirrorConventionGuess()
    {
        var def = TutorialSurfaceRegistry.Surfaces.Single(s => s.Id == "Mirror");
        AssertThat(def.WayIn).IsNotNull();
        AssertThat(def.WayIn!.Value.Kind).IsEqual(TutorialAnchorKind.Hud);
        AssertThat(def.WayIn!.Value.Key)
            .OverrideFailureMessage("Mirror's WayIn should be the WatchButton, never a guessed \"OpenMirror\".")
            .IsEqual("WatchButton");

        var ui = MountMainUi();
        try
        {
            var control = ui.FindChild("WatchButton", recursive: true, owned: false) as Control;
            AssertThat(control)
                .OverrideFailureMessage("Mirror's declared WayIn (\"WatchButton\") does not resolve to a live Control.")
                .IsNotNull();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void ASurfaceAbsentFromTheRoster_ThrowsWithItsOwnNameInTheMessage()
    {
        Exception? thrown = null;
        try { TutorialSurfaceRegistry.WayInFor("NotASurface"); }
        catch (Exception ex) { thrown = ex; }

        AssertThat(thrown)
            .OverrideFailureMessage("WayInFor(\"NotASurface\") did not throw for an unregistered surface id.")
            .IsNotNull();
        AssertThat(thrown!.Message).Contains("NotASurface");

        var ui = MountMainUi();
        try
        {
            AssertThat(TutorialSurfaceRegistry.ContentRootFor("NotASurface", ui.Drawer, ui))
                .OverrideFailureMessage("ContentRootFor should answer null for an id nothing registers, not throw.")
                .IsNull();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void AimAnchor_PointsMirrorAtWatchButton_WhileClosed()
    {
        var aimed = TutorialFlow.AimAnchor(TutorialAnchor.ForPanelControl("Mirror", "AnyControl"), openPanelId: null);
        AssertThat(aimed.Kind).IsEqual(TutorialAnchorKind.Hud);
        AssertThat(aimed.Key)
            .OverrideFailureMessage("AimAnchor still guessed \"OpenMirror\" instead of reading the declared WayIn.")
            .IsEqual("WatchButton");
    }

    [TestCase]
    public void AimAnchor_StillAimsTheFiveOriginalWalkAndTraySurfaces_Identically()
    {
        // U9 changed WHERE AimAnchor reads the way in from (a computed convention -> a declared
        // table), not what it resolves to for the surfaces the convention already got right.
        foreach (var (panelId, venue) in new[] { ("Forge", "forge"), ("Shop", "market"), ("Tavern", "tavern"),
                     ("Depths", "minegate"), ("Bounties", "noticeboard") })
        {
            var aimed = TutorialFlow.AimAnchor(TutorialAnchor.ForPanelControl(panelId, "AnyControl"), openPanelId: null);
            AssertThat(aimed.Kind).IsEqual(TutorialAnchorKind.Building);
            AssertThat(aimed.Key).IsEqual(venue);
        }

        foreach (var panelId in new[] { "Ledger", "Commissions", "Legends", "Forecast" })
        {
            var aimed = TutorialFlow.AimAnchor(TutorialAnchor.ForPanelControl(panelId, "AnyControl"), openPanelId: null);
            AssertThat(aimed.Kind).IsEqual(TutorialAnchorKind.Hud);
            AssertThat(aimed.Key).IsEqual($"Open{panelId}");
        }
    }

    [TestCase]
    public void AimAnchor_ThrowsForASurfaceWithNoDeclaredWayIn_WhileItIsClosed()
    {
        foreach (var surfaceWithNoWayIn in SurfacesWithNoWayInYet)
        {
            AssertThrown(() =>
                    TutorialFlow.AimAnchor(TutorialAnchor.ForPanelControl(surfaceWithNoWayIn, "AnyControl"), openPanelId: null))
                .IsInstanceOf<InvalidOperationException>();
        }
    }

    [TestCase]
    public void DocketsWayIn_ResolvesInsideForgesOwnContentRoot()
    {
        var def = TutorialSurfaceRegistry.Surfaces.Single(s => s.Id == "Docket");
        AssertThat(def.WayIn).IsNotNull();
        AssertThat(def.WayIn!.Value.Kind).IsEqual(TutorialAnchorKind.PanelControl);
        AssertThat(def.WayIn!.Value.Key).IsEqual("Forge");
        AssertThat(def.WayIn!.Value.ControlName).IsEqual("OpenDocketFromForge");

        var ui = MountMainUi();
        try
        {
            ui.Overlay.RefreshAnchor(def.WayIn!.Value, ui.Town, ui.Drawer, ui);
            AssertThat(ui.Overlay.PulsingHudControlName)
                .OverrideFailureMessage("Docket's WayIn did not resolve to the button nested inside Forge's own panel.")
                .IsEqual("OpenDocketFromForge");
        }
        finally { Unmount(ui); }
    }
}
#endif
