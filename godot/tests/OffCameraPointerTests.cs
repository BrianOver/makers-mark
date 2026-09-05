#if GDUNIT_TESTS
using System;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Tools;
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
    /// <summary>
    /// P2-SCREEN-10: replaces U42's own test, which asked the SAME single node
    /// (<c>ui.Objective.GetGlobalRect()</c>) production consulted for its avoidance rule —
    /// tautologically green, and blind to the Tutorial dock <c>MainUi</c> stacks 16px below
    /// Objective in the same top-right column. This exercises BOTH cards visible at once — a case
    /// the old test could not even express — and reads the marker against EACH card's own live
    /// rect via the arbiter's free-region query, never a single named obstacle.
    ///
    /// <para>Before this unit's fix, this exact test (asserting <c>ui.Tutorial.GetGlobalRect()</c>
    /// clear) failed against production: U42's <c>KeepClearOf</c> named only <c>Objective</c>, so
    /// its slide-clear-of-the-card fallback had no idea a second card existed 16px below and could
    /// land the marker on it. After the fix it passes — see this unit's own PR body for the raw
    /// before/after run.</para>
    /// </summary>
    public async Task AnEasternTarget_PutsTheMarkerClearOfBothTopRightDocks()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            var forge = ui.Town.FindBuilding("forge");
            var market = ui.Town.FindBuilding("market");

            AssertThat(market.GlobalPosition.X > forge.GlobalPosition.X)
                .OverrideFailureMessage(
                    "Fixture guard: the market must sit east of the forge (larger world X) for this test " +
                    "to be about the edge both docks occupy.")
                .IsTrue();

            AssertThat(ui.Objective.IsVisibleInTree())
                .OverrideFailureMessage("Fixture guard: the objective card must be on screen — it is one of the two obstacles under test.")
                .IsTrue();

            // The Tutorial dock never naturally shows on day 1 (both its rows gate on later
            // eligibility this fixture has no reason to earn) — forced here, directly, the same way
            // this suite already forces scenarios production would not naturally reach (see
            // AWorldAnchor_DampsEverySiblingStationsTell entering an interior directly below).
            // Docked exactly where MainUi.UpdateObjectiveDock docks it in real play: immediately
            // below Objective's own live bottom edge, with a real non-zero height, so it is a
            // genuine second obstacle — never a sliver a Grow() would swallow by accident.
            var objectiveRect = ui.Objective.GetGlobalRect();
            ui.Tutorial.Size = new Vector2(objectiveRect.Size.X, 80f);
            ui.Tutorial.GlobalPosition = new Vector2(objectiveRect.Position.X, objectiveRect.End.Y + 16f);
            ui.Tutorial.Visible = true;
            await SettleLayout(ui);

            AssertThat(ui.Tutorial.IsVisibleInTree())
                .OverrideFailureMessage("Fixture guard: the Tutorial dock must be on screen — it is the SECOND obstacle U42's own fix could not see.")
                .IsTrue();

            ui.Overlay.RefreshAnchor(TutorialAnchor.ForBuilding("market"), ui.Town, ui.Drawer, ui);
            ui.Overlay.Tick(0.016);

            AssertThat(ui.Overlay.OffCameraMarkerVisible)
                .OverrideFailureMessage(
                    "Fixture guard: the market is a screen away from the forge spawn, so the marker must " +
                    "be showing for this test to say anything.")
                .IsTrue();

            var card = ui.Objective.GetGlobalRect();
            var tutorialDock = ui.Tutorial.GetGlobalRect();
            var marker = ui.Overlay.OffCameraMarkerCenter;

            AssertThat(card.HasPoint(marker))
                .OverrideFailureMessage(
                    $"The marker ({marker}) is inside the objective card ({card}). It reads as one of the " +
                    "card's own buttons instead of a direction to walk.")
                .IsFalse();

            AssertThat(tutorialDock.HasPoint(marker))
                .OverrideFailureMessage(
                    $"The marker ({marker}) is inside the Tutorial dock ({tutorialDock}). This is exactly the " +
                    "defect P2-SCREEN-10 exists to fix: U42's own KeepClearOf named ONLY the objective card, so " +
                    "clearing it could still land the marker on the second card stacked below — a " +
                    "named-obstacle rule is a hand-listed fixture with n=1, and the old test (asking the same " +
                    "single node production consulted) could never catch it.")
                .IsFalse();

            AssertThat(marker.X)
                .OverrideFailureMessage(
                    $"Clearing both cards must move the marker ALONG the edge, never inward: an eastern " +
                    $"target's marker ({marker}) still has to sit in the right half of the screen, or it " +
                    "has stopped encoding a direction.")
                .IsGreater(ui.Town.ViewportScreenRect.GetCenter().X);
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    /// <summary>
    /// P2-SCREEN-10: the third option U42's own fallback comment missed ("overlapping is worse than
    /// wrong, but wrong is worse than gone") — when EVERY point on the boundary is claimed, the
    /// marker must not draw a wrong position, and the suppression must be LOUD, never silent (this
    /// repo's own scar: a full playtest once reported "clean" over a real placeholder bug because a
    /// degradation logged nowhere).
    /// </summary>
    public void AFullyClaimedBoundary_SuppressesTheMarker_AndLogsRatherThanFailingSilently()
    {
        var ui = MountMainUi();
        try
        {
            EngineDistress.ResetForTests();

            // Blankets the WHOLE inset boundary with one obstacle — the objective card is already a
            // live SurfaceArbiter HudDock claim (MainUi.BuildUi's Overlay.ClaimHudColumn), so
            // growing IT to cover the whole viewport is enough to prove the "nothing free anywhere"
            // path without needing every real HUD dock in the game to cooperate.
            var full = ui.Town.ViewportScreenRect.Grow(64f);
            ui.Objective.Size = full.Size;
            ui.Objective.GlobalPosition = full.Position;

            ui.Overlay.RefreshAnchor(TutorialAnchor.ForBuilding("minegate"), ui.Town, ui.Drawer, ui);
            ui.Overlay.Tick(0.016);

            AssertThat(ui.Overlay.OffCameraMarkerVisible)
                .OverrideFailureMessage(
                    "A boundary claimed EVERYWHERE must suppress the marker entirely — drawing it " +
                    "anywhere would be a confidently wrong direction, which U42's own comment already " +
                    "conceded is worse than overlapping.")
                .IsFalse();

            AssertThat(EngineDistress.Messages.Any(m => m.Contains("off-camera marker suppressed", StringComparison.Ordinal)))
                .OverrideFailureMessage(
                    "The suppression must log through EngineDistress AT THE DRAW SITE, never silently — " +
                    "this repo has already shipped the silent-degrade defect once (a full playtest reported " +
                    "'clean' over a real placeholder bug because nothing recorded the fallback). Recorded " +
                    "messages: " + string.Join(" | ", EngineDistress.Messages))
                .IsTrue();
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

            // Diagnosed 2026-09-04: a fixed 3-frame SettleLayout pump here flaked in CI twice in one
            // day (once on a docs-only PR). Town2D.WorldToScreen bakes in
            // WorldViewport.GetCanvasTransform(), which only catches up to Cam's new GlobalPosition
            // once the engine actually PROCESSES a frame carrying it — a guessed frame count is not
            // that condition. Poll the real production signal instead: Overlay.Tick recomputes
            // OffCameraMarkerVisible from the CURRENT canvas transform every call (see
            // TutorialOverlay.UpdateOffCameraMarker's own inset.HasPoint(targetScreen) check), so
            // ticking once per pumped frame and watching for the marker to clear IS watching the
            // transform propagate, not guessing when it "probably" has. Budget 30 frames — half a
            // second at 60fps, ~30x the steady-state cost (this passes on frame 1 in every local run
            // observed) — generous enough to absorb CI scheduling jitter without ever being the
            // bottleneck when the marker is behaving correctly.
            await SettleUntil(
                ui,
                () => { ui.Overlay.Tick(0.016); return !ui.Overlay.OffCameraMarkerVisible; },
                frameBudget: 30,
                conditionDescription: "Overlay.OffCameraMarkerVisible to clear once the camera's " +
                    "canvas transform catches up to Cam's new GlobalPosition");

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

    [TestCase]
    /// <summary>
    /// Diagnosed 2026-09-04: proves <see cref="UiTestSupport.SettleUntil"/> itself is a real guard,
    /// not a pump that can never fail. A deliberately-impossible predicate (<c>() => false</c>) with
    /// a tiny budget must throw, and the thrown message must NAME the condition that never held — a
    /// bare "timed out" was explicitly rejected for this fix (it teaches nothing about what to look
    /// at next), so this pins that the caller-supplied description actually reaches the failure.
    /// </summary>
    public async Task SettleUntil_FailsByName_WhenItsConditionNeverHolds()
    {
        var ui = MountMainUi();
        try
        {
            const string condition = "a condition that can never hold (guard test)";
            InvalidOperationException? caught = null;
            try
            {
                await SettleUntil(ui, () => false, frameBudget: 2, conditionDescription: condition);
            }
            catch (InvalidOperationException ex)
            {
                caught = ex;
            }

            AssertThat(caught)
                .OverrideFailureMessage(
                    "SettleUntil must throw once its frame budget is exhausted without its predicate " +
                    "ever holding — a guard that cannot fail is not a guard.")
                .IsNotNull();

            AssertThat(caught!.Message.Contains(condition, StringComparison.Ordinal))
                .OverrideFailureMessage(
                    $"SettleUntil's failure message must NAME the condition that never held, not just " +
                    $"say 'timed out'. Got: \"{caught.Message}\"")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }
}
#endif
