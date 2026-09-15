#if GDUNIT_TESTS
using System;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-22: a design capture found "E · Forge" rendered on the TAVERN's roof, right beside
/// the Tavern nametag, while the player stood at the forge door — the chip was anchored
/// CenterBottom (<c>MainUi.BuildUi</c>'s old <c>LayoutPreset.CenterBottom</c>), a FIXED screen
/// point that named one building while sitting wherever the camera happened to frame the bottom
/// of the screen. The same audit found it also survives a Send-Off/mine-gate camera pan, since
/// <see cref="WorldInput2D"/> keeps scanning (and the player keeps walking) throughout one of
/// those — see <see cref="Town2D.FocusOn"/>'s own doc.
///
/// <para><see cref="InteractPromptTests"/> already pins the chip's TEXT against <see
/// cref="WorldInput2D.PromptText"/>; this suite is the POSITION half — <see
/// cref="MainUi.UpdateInteractPrompt"/> now floats the chip over <see
/// cref="WorldInput2D.ActiveTarget"/>'s own nametag (<see cref="Building2D.NameLabel"/>) via <see
/// cref="Town2D.WorldToScreen"/>, the exact world-to-screen mechanism <see
/// cref="Ui.TutorialOverlay"/>'s off-camera marker already established for this codebase, rather
/// than a second, independently-typed anchoring scheme.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class InteractPromptAnchorTests
{
    /// <summary>
    /// Iterates every venue <see cref="TownLayout2D.Venues"/> actually places — not a single
    /// hand-typed "forge" — and checks the chip against a property, not a re-derivation of
    /// <c>UpdateInteractPrompt</c>'s own formula (a test that recomputed the exact same nametag-
    /// plus-gap offset would only prove the code agrees with itself). The property CenterBottom
    /// could never satisfy for every venue at once: the five venues span both the north (forge,
    /// market) and south (tavern, noticeboard) building rows, roughly 28 tiles apart, so a chip
    /// anchored to a single fixed screen point cannot land near EVERY one of their own on-screen
    /// positions — only whichever venue happens to sit near that fixed point at spawn.
    /// </summary>
    [TestCase]
    public async Task EveryTownVenue_PromptFloatsAboveItsOwnBuilding_NeverAFixedScreenPoint()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            foreach (var venue in TownLayout2D.Venues)
            {
                var building = ui.Town.FindBuilding(venue.Key);
                ui.Town.Player.GlobalPosition = building.DoorAnchorGlobal;
                await PumpWorldFrames(ui, 4);
                // The chip is anchored to a WORLD position projected through the camera, so its
                // screen position is only meaningful once the camera has finished travelling —
                // see SettleCamera. Teleporting the player is instant; the camera is not.
                await SettleCamera(ui);
                await SettleLayout(ui);

                AssertThat(ui.Town.WorldInputNode.ActiveTarget?.Key)
                    .OverrideFailureMessage(
                        $"Setup check: standing at '{venue.Key}'s door anchor never made it the " +
                        "active target — this iteration would prove nothing about its own prompt position.")
                    .IsEqual(venue.Key);
                AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                    .OverrideFailureMessage($"Setup check: the chip is not visible at '{venue.Key}'.")
                    .IsTrue();

                var buildingScreen = ui.Town.WorldToScreen(building.GlobalPosition);
                var chipRect = ui.InteractPromptLabel.GetGlobalRect();

                AssertThat(chipRect.Position.Y)
                    .OverrideFailureMessage(
                        $"[{venue.Key}] chip ({chipRect}) does not float above its own building " +
                        $"(screen anchor {buildingScreen}) — it must sit above the thing it names.")
                    .IsLess(buildingScreen.Y);

                // Generous on purpose: covers the widest venue footprint (tavern, 84px) times the
                // largest CanvasShrink a small test viewport can report, plus the nameplate's own
                // half-width — this is a "same building" bound, not a pixel-exact one.
                const float horizontalSlackPx = 250f;
                AssertThat(Mathf.Abs(chipRect.GetCenter().X - buildingScreen.X))
                    .OverrideFailureMessage(
                        $"[{venue.Key}] chip center X {chipRect.GetCenter().X} is nowhere near its " +
                        $"own building's screen X {buildingScreen.X} (screen anchor {buildingScreen}) " +
                        "— it reads as anchored to something other than the venue it names.")
                    .IsLess(horizontalSlackPx);
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// P2-SCREEN-30: the check above only proves the chip floats ABOVE its own building's screen
    /// row — a chip clamped to window y=0 still satisfies "less than buildingScreen.Y" whenever
    /// the building itself sits mid-viewport, which is exactly how this bug shipped and stayed
    /// green (the design doc's own capture: "E · Forge" at window y=12, over the HUD header band).
    /// This pins the actual bound: the chip's rect must be fully inside <see
    /// cref="Town2D.ViewportScreenRect"/> — the world SubViewportContainer's OWN screen rect,
    /// which starts BELOW the HUD header (see <c>HudBoundsTests.WorldRegion_NeverIntersects_
    /// TheHudHeader</c>) — for every registered venue, never the whole window.
    ///
    /// <para>A property over every venue, not the one hand-picked Forge case: a negative control
    /// proves at least one of them really does put its own nametag above the visible world band
    /// (the Forge is 170 world-px tall in a ~197-px world viewport per the design doc), so the
    /// enclosure assertion above it is not a property no iteration ever exercises.</para>
    /// </summary>
    [TestCase]
    public async Task EveryTownVenue_PromptStaysInsideTheWorldViewport_NeverOverTheHudHeader()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            var worldRect = ui.Town.ViewportScreenRect;
            var anyNameplateAboveWorldView = false;

            foreach (var venue in TownLayout2D.Venues)
            {
                var building = ui.Town.FindBuilding(venue.Key);
                ui.Town.Player.GlobalPosition = building.DoorAnchorGlobal;
                await PumpWorldFrames(ui, 4);
                // Same camera-glide reasoning as EveryTownVenue_PromptFloatsAboveItsOwnBuilding
                // above — every screen position read here is projected through the camera.
                await SettleCamera(ui);
                await SettleLayout(ui);

                AssertThat(ui.Town.WorldInputNode.ActiveTarget?.Key)
                    .OverrideFailureMessage(
                        $"Setup check: standing at '{venue.Key}'s door anchor never made it the " +
                        "active target — this iteration would prove nothing about its own prompt position.")
                    .IsEqual(venue.Key);

                var nameplateScreen = ui.Town.WorldToScreen(building.NameLabel.GlobalPosition);
                if (nameplateScreen.Y < worldRect.Position.Y)
                {
                    anyNameplateAboveWorldView = true;
                }

                var chipRect = ui.InteractPromptLabel.GetGlobalRect();

                // Grown by a hair to absorb sub-pixel float rounding in the world->canvas->screen
                // chain — the same slack HudBoundsTests' own world-region check already uses,
                // never enough to hide a real overflow into the HUD band.
                AssertThat(worldRect.Grow(1.5f).Encloses(chipRect))
                    .OverrideFailureMessage(
                        $"[{venue.Key}] chip rect {chipRect} is not fully inside the world " +
                        $"viewport {worldRect} — it is floating outside Town2D's own region (the " +
                        "HUD header band, most likely — P2-SCREEN-30).")
                    .IsTrue();
            }

            AssertThat(anyNameplateAboveWorldView)
                .OverrideFailureMessage(
                    "Setup check: every venue's own nametag stayed inside the world viewport at " +
                    "its own door anchor — this suite never exercised the P2-SCREEN-30 case (a " +
                    "target tall enough that its own nametag sits above the visible world band), " +
                    "so the enclosure assertion above proved nothing about that case.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Negative control (unit's own test list, bullet 2): mirrors <see
    /// cref="InteractPromptTests.WalkingAway_ClearsThePrompt"/>'s off-grid teleport, but reads the
    /// PanelContainer's own global rect rather than just <c>IsVisibleInTree</c> — a hidden control
    /// still reports a stale <c>GlobalPosition</c> from wherever it last floated, so this proves
    /// there is really nothing left claiming a screen position, not just that the flag flipped.
    /// </summary>
    [TestCase]
    public async Task NoTargetInRange_ShowsNoPrompt_AtAnyPosition()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            await PumpWorldFrames(ui, 4);
            await SettleLayout(ui);

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage("Setup check: nothing active at spawn — this test would prove nothing about clearing an existing prompt.")
                .IsNotNull();

            // Same off-grid teleport WorldInput2DNoTargetInteractTests/InteractPromptTests use:
            // TownLayout2D's whole grid is 40x28 tiles at 16px, so this is nowhere near any
            // Interact zone by construction, not by tuning a magic distance.
            ui.Town.Player.GlobalPosition = new Vector2(-2000f, -2000f);
            await PumpWorldFrames(ui, 4);
            await SettleLayout(ui);

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage("WorldInput2D still has an ActiveTarget 2000px off the town grid.")
                .IsNull();
            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("The interact-prompt chip is still visible with no target in range.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// The MineGateFocus case the audit found: <see cref="Town2D.FocusOnMineGate"/> borrows the
    /// camera for a Send-Off departure pan without touching player input or <see
    /// cref="WorldInput2D"/> at all (see its own doc — "the player keeps walking underneath"), so
    /// a target can stay active the entire time the visible frame shows the mine gate instead of
    /// wherever the player actually is. The chip must disappear for exactly that stretch, since a
    /// target-relative prompt cannot honestly point at anything the screen isn't currently showing.
    /// </summary>
    [TestCase]
    public async Task CameraFocusedElsewhere_HidesThePrompt_EvenWithATargetStillActive()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            await PumpWorldFrames(ui, 4);
            await SettleLayout(ui);

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage("Setup check: nothing active at spawn — this test would prove nothing about hiding a live prompt during a focus beat.")
                .IsNotNull();
            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("Setup check: the chip is not showing before the focus beat — nothing to hide.")
                .IsTrue();

            ui.Town.FocusOnMineGate();
            AssertThat(ui.Town.IsCameraOnPlayer)
                .OverrideFailureMessage("Setup check: FocusOnMineGate did not actually start a focus beat (IsCameraOnPlayer still true) — this test would prove nothing.")
                .IsFalse();

            await SettleLayout(ui);

            AssertThat(ui.Town.WorldInputNode.ActiveTarget)
                .OverrideFailureMessage(
                    "Setup check: the target cleared on its own during the focus beat — WorldInput2D " +
                    "keeps scanning while the player keeps walking (FocusOn's own doc), so this test's " +
                    "premise (a target still active while the camera looks elsewhere) no longer holds.")
                .IsNotNull();
            AssertThat(ui.InteractPromptLabel.IsVisibleInTree())
                .OverrideFailureMessage("The 'E · Forge' chip is still visible while the camera is off the player during a Send-Off/mine-gate focus beat — P2-SCREEN-22 regressed.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>Consecutive frames the drawn camera centre must not move before it counts as
    /// arrived. One is not enough: the glide is exponential, so a single sub-epsilon step could in
    /// principle be a very short frame rather than the end of the journey.</summary>
    private const int CameraStillFrames = 3;

    /// <summary>Squared world-px the drawn centre may move per frame and still count as stopped —
    /// (0.01px)². The glide never lands exactly on its target (measured rest for the mine gate:
    /// drawn (519.9808, 152.044) against target (520, 152)), so "equals the target" is not a
    /// reachable condition and this is a movement test, not a distance-to-target one.</summary>
    private const float CameraStillEpsilonSq = 0.0001f;

    /// <summary>Frames before giving up. The worst measured journey here (tavern door → mine-gate
    /// door, 512 world px) converged in 57; 240 is ~4x that, so exhausting it means the camera
    /// genuinely never arrived rather than that this number is too tight.</summary>
    private const int CameraSettleFrameBudget = 240;

    /// <summary>
    /// Waits until <see cref="Town2D.Cam"/>'s DRAWN centre stops moving.
    ///
    /// <para><b>Why this exists (2026-09-14, CI-only red on this suite's first case).</b> CI failed
    /// with <c>[minegate] chip ((640.1, 12), (101, 23)) does not float above its own building
    /// (screen anchor (684.52, -1.854))</c> — a screen anchor 1.85px ABOVE the top of the window,
    /// which no on-screen chip can sit above, so the assertion read as unsatisfiable. It is not.
    /// The anchor is right and the invariant is right; the MEASUREMENT was taken while the camera
    /// was still travelling.</para>
    ///
    /// <para><c>Town2D.FollowPlayer</c> assigns <c>Cam.GlobalPosition</c> instantly, but the
    /// camera is built with <c>PositionSmoothingEnabled = true, PositionSmoothingSpeed = 8</c>, so
    /// the DRAWN centre — the one <see cref="Viewport.GetCanvasTransform"/>, and therefore <see
    /// cref="Town2D.WorldToScreen"/>, actually uses — eases toward it over many frames. This suite
    /// walks the venue list in <see cref="TownLayout2D.Venues"/> order, and tavern (tile 18,40) →
    /// minegate (tile 32,8) is a 512-world-px teleport. Measured locally at the exact instant the
    /// old <c>PumpWorldFrames(ui, 4) + SettleLayout(ui)</c> pump handed control back: camera target
    /// (520, 152), camera DRAWN (437.75, 340.02) — 188px short, only ~63% of the way there, which
    /// puts the gate's own origin at screen y 10.95 with the clamped chip at 12 and fails by
    /// 1.05px. Pump to convergence instead and the same frame reads screen anchor (576.04, 386.91)
    /// against a chip at y 81.9: it passes by 305px, and the horizontal check lands within 0.006px
    /// of dead centre against its own 250px slack. Every venue behaves the same way — the worst
    /// settled horizontal error across all five is 0.014px.</para>
    ///
    /// <para>So this is <c>frame count is not a duration</c> again, the same defect #741 fixed in
    /// <c>RealClickReachesBuildingTests</c>: 7 process frames is a guess, and Godot's idle
    /// smoothing consumes the PROCESS delta, which differs between a local run and a headless CI
    /// runner with rendering disabled. That is the whole CI-vs-local split — nothing here is
    /// font-derived. <c>buildingScreen</c> is <see cref="Town2D.WorldToScreen"/> of a layout
    /// constant, and the failing chip Y is the viewport clamp's own floor; the only font-sensitive
    /// term in the chip's position is <c>NameLabel.Size.X</c>, which feeds the horizontal check
    /// that did not fail (and cannot be clamped for these five: their requested widths are 76-125px
    /// against font minimums of 17-35px).</para>
    ///
    /// <para>Deliberately NOT <see cref="UiTestSupport.SettleUntil"/> with "drawn equals target":
    /// <c>Cam</c>'s <c>Limit*</c> rect clamps the drawn centre, so at a map edge the two never
    /// converge — the tavern rests at y 605.5 (704px map minus the 98.5px half-viewport) while its
    /// target stays 664. Movement between frames is the honest condition at an edge and in open
    /// ground alike.</para>
    /// </summary>
    private static async Task SettleCamera(MainUi ui)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var last = ui.Town.Cam.GetScreenCenterPosition();
        var still = 0;

        for (var frame = 0; frame < CameraSettleFrameBudget; frame++)
        {
            await ui.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            var now = ui.Town.Cam.GetScreenCenterPosition();
            still = now.DistanceSquaredTo(last) < CameraStillEpsilonSq ? still + 1 : 0;
            last = now;

            if (still >= CameraStillFrames)
            {
                return;
            }
        }

        throw new InvalidOperationException(
            $"The camera never stopped moving within {CameraSettleFrameBudget} frames: drawn centre " +
            $"{last} is still travelling toward {ui.Town.Cam.GlobalPosition}. Every screen position " +
            "read in this suite is projected through that camera, so asserting now would measure a " +
            "glide rather than a layout.");
    }
}
#endif
