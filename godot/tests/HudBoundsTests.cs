#if GDUNIT_TESTS
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Town2d;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// The 2026-07-21 unit (playtest F1, "menus off-screen") plus the 2026-08-02 shell-and-audio
/// plan's U2 (R1/KTD-C, "the world is never under the HUD") — the latter's test cases are
/// labeled "U2 (shell-and-audio plan)" below to avoid confusion with the former's own U2 label;
/// they are two different plans that happened to both number a HUD-geometry unit "U2".
///
/// <para><b>2026-07-21 postmortem (original cases):</b> the missing assertion the postmortem
/// called out. <c>MenuSizingTests</c> only checked the objective chip's WIDTH resolved to
/// <see cref="GodotClient.Ui.ObjectiveTracker.DockWidth"/> — never that the chip, or the HUD's
/// own Skip/Auto/Pause/Ledger controls, actually sit INSIDE the viewport. Two concrete defects this
/// hunts: (a) once the Gold/Heroes stat chips mount after the first tick the HUD header row's total
/// width can exceed the window, pushing the rightmost controls off-screen; (b) the objective chip
/// is a fixed-height overlay docked over the drawer/modal region, so it can visually sit on top of
/// the very panel buttons (or the Ledger) the tutorial points the player at.</para>
///
/// <para><b>2026-08-02 shell-and-audio plan's U2 (new cases below):</b> the owner's words — "the
/// mine is off the screen at the top because the top menu blocks it" — traced to <c>MainUi</c>
/// mounting <see cref="Town2D"/> full-rect BEHIND an opaque <c>HudHeader</c>, with
/// <c>Town2D.TopObstructionPx</c> only half-compensating the camera for the hidden band (and
/// earlier attempts at this same area either left the mine off-screen (finding PT8) or
/// overcorrected into "world is a little... too zoomed in now" (finding PT19)). KTD-C's fix is
/// structural, not a third compensation pass: the header now sits in LAYOUT FLOW and Town2D only
/// ever occupies the region below it — occlusion is impossible by construction, so the strong
/// test is a RECT NON-INTERSECTION check, not "the mine happens to be visible right now."</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class HudBoundsTests
{
    [TestCase]
    public async Task AfterFirstTick_CoreHudControls_StayInsideViewport()
    {
        var ui = MountMainUi();
        try
        {
            // Mount is day 1 Morning (2 stat chips: Day/Phase). Advance once so Gold/Heroes
            // chips mount too (RefreshStatus rebuilds all 4 — see MainUi.RefreshStatus) — the
            // exact "once the stat chips mount" trigger the finding names.
            PressEnabled(ui, "AdvancePhase");
            await SettleLayout(ui);

            var viewport = ui.GetViewportRect().Size;
            AssertThat(viewport.X).IsGreater(0f);
            AssertThat(viewport.Y).IsGreater(0f);

            foreach (var name in new[] { "AdvancePhase", "AutoAdvance", "PlayPause", "Speed", "OpenLedger" })
            {
                var control = Find<Control>(ui, name);
                if (!control.Visible)
                {
                    continue; // PlayPause/Speed are Auto sub-controls (U2), hidden while gated
                }

                var rect = control.GetGlobalRect();
                AssertThat(rect.Position.X)
                    .OverrideFailureMessage($"{name} left edge {rect.Position.X} < 0 (off-screen)")
                    .IsGreaterEqual(0f);
                AssertThat(rect.Position.Y)
                    .OverrideFailureMessage($"{name} top edge {rect.Position.Y} < 0 (off-screen)")
                    .IsGreaterEqual(0f);
                AssertThat(rect.End.X)
                    .OverrideFailureMessage($"{name} right edge {rect.End.X} > viewport width {viewport.X}")
                    .IsLessEqual(viewport.X);
                AssertThat(rect.End.Y)
                    .OverrideFailureMessage($"{name} bottom edge {rect.End.Y} > viewport height {viewport.Y}")
                    .IsLessEqual(viewport.Y);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task DrawerOpen_ObjectiveChip_NeverCoversDrawerButtons()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge"); // Morning phase — the vendor's "Buy 1" rows are live
            await SettleLayout(ui);

            var buyCopper = Find<Control>(ui.Forge, "BuyMat_copper");
            AssertThat(buyCopper.Visible).IsTrue();
            var buyRect = buyCopper.GetGlobalRect();

            AssertThat(!ui.Objective.Visible || !ui.Objective.GetGlobalRect().Intersects(buyRect))
                .OverrideFailureMessage(
                    "Objective chip overlaps the Forge drawer's Buy-copper button — "
                    + $"chip={ui.Objective.GetGlobalRect()} button={buyRect}")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task AfterMount_ObjectiveChip_NeverCoversBooksTray()
    {
        // Bug fix (gate-b playtest screenshot, "note overlaps the books tray"): the chip's dock
        // offset was a hand-tuned magic constant that went stale the moment the header grew a
        // Books Tray zone (Ledger/Forecast/Commissions/Legends/Demand/Renown/Progress icon row) —
        // both are top-right, so a too-small offset put the chip's top edge INSIDE the tray's own
        // rect. UpdateObjectiveDock now measures the header's real height instead of trusting the
        // constant blindly.
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            var tray = Find<Control>(ui, "BooksTray");
            AssertThat(tray.Visible).IsTrue();
            var trayRect = tray.GetGlobalRect();

            AssertThat(!ui.Objective.Visible || !ui.Objective.GetGlobalRect().Intersects(trayRect))
                .OverrideFailureMessage(
                    "Objective chip overlaps the HUD header's Books Tray — "
                    + $"chip={ui.Objective.GetGlobalRect()} tray={trayRect}")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task LedgerOpen_ObjectiveChip_NeverCoversLedger()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceDay(ui);                                     // day 1 -> Evening arms the Return Ritual gate
            ui._Process(MainUi.ReturnRitualDelaySeconds + 0.1); // gate elapses -> Ledger opens
            AssertThat(ui.Ledger.Visible).IsTrue();
            await SettleLayout(ui);

            AssertThat(!ui.Objective.Visible
                    || !ui.Objective.GetGlobalRect().Intersects(ui.Ledger.GetGlobalRect()))
                .OverrideFailureMessage("Objective chip overlaps the open Evening Ledger modal")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task ObjectiveChip_HeightTracksContent_NotFixedEmptyPanel()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);

            // A fresh Morning-1 mount has exactly one objective step — the un-expanded chip must be
            // sized to its own content, NOT padded to the old fixed 260px dock. Assert against the
            // content min-size (robust to objective-text length — e.g. U4's location-aware step copy
            // is longer than the original) rather than a magic pixel threshold; and prove it shrank
            // below the retired fixed dock.
            var chip = ui.Objective;
            AssertThat(chip.Size.Y)
                .OverrideFailureMessage($"objective chip height {chip.Size.Y} still reserves the fixed 260px empty dock")
                .IsLess(260f);
            AssertThat(chip.Size.Y)
                .OverrideFailureMessage($"objective chip height {chip.Size.Y} exceeds its content min-size {chip.GetCombinedMinimumSize().Y} — not content-tracked")
                .IsLessEqual(chip.GetCombinedMinimumSize().Y + 2f);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U2 (shell-and-audio plan, R1/KTD-C): structural HUD/world non-occlusion ─────────────────

    [TestCase]
    public async Task WorldRegion_NeverIntersects_TheHudHeader()
    {
        var ui = MountMainUi();
        try
        {
            // Advance once, same precondition as AfterFirstTick_CoreHudControls_StayInsideViewport
            // above: the header's row 1 grows to its full stat-chip set on the first tick, so this
            // measures the header at its real (not fresh-mount-collapsed) height.
            PressEnabled(ui, "AdvancePhase");
            await SettleLayout(ui);

            var world = ui.Town.GetGlobalRect();
            var header = ui.HudHeader.GetGlobalRect();

            // The strong version (task brief): not "the mine happens to be visible", but "these two
            // rects cannot overlap at all" — true regardless of where any building sits, regardless
            // of camera position, so a later HUD change that grows the header back over the world
            // fails THIS assertion immediately rather than waiting for a human to notice a building
            // went missing.
            AssertThat(world.Intersects(header))
                .OverrideFailureMessage(
                    $"Town2D's rect {world} intersects the HUD header's rect {header}. The world can " +
                    "still be occluded by the header — U2's whole point is that this must be " +
                    "impossible BY CONSTRUCTION: the header sits in layout flow (MainUi.BuildUi's " +
                    "`Layout` VBox) and Town2D only ever fills the `WorldSlot` region below it.")
                .IsFalse();

            // A zero-height (or off-tree) header would make the assertion above vacuously true —
            // pin that the header is real and actually occupies space, so this test cannot pass by
            // both rects being degenerate.
            AssertThat(header.Size.Y)
                .OverrideFailureMessage("the HUD header measured zero height — this test would pass vacuously")
                .IsGreater(0f);
            AssertThat(world.Size.Y)
                .OverrideFailureMessage("Town2D measured zero height — this test would pass vacuously")
                .IsGreater(0f);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task HudHeader_MeasuredHeight_StaysInsideItsBudget()
    {
        var ui = MountMainUi();
        try
        {
            PressEnabled(ui, "AdvancePhase"); // full stat-chip row, same reasoning as above
            await SettleLayout(ui);

            var height = ui.HudHeader.GetCombinedMinimumSize().Y;
            AssertThat(height)
                .OverrideFailureMessage(
                    $"HUD header measured {height}px tall — over the {MainUi.HeaderBudgetPx}px budget " +
                    "(R2/KTD-C). The rework must stay inside a stated budget; if a real redesign needs " +
                    "more room, raise MainUi.HeaderBudgetPx deliberately (and this test with it) rather " +
                    "than letting the header creep back toward eating the world it now merely sits above.")
                .IsLessEqual(MainUi.HeaderBudgetPx);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task AfterFocusOnMineGateSettles_TheGateBuildingsScreenRect_IsFullyInsideTheWorldRegion()
    {
        var ui = MountMainUi();
        try
        {
            // Town2D owns a live SubViewport; awaiting frames while one renders is the documented
            // gdUnit headless hang (godot-3d-headless-test-hang) — disable it first. Disabling the
            // RENDER does not stop Camera2D's own position-smoothing math, which runs regardless.
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            var gate = ui.Town.FindBuilding("minegate");
            ui.Town.FocusOnMineGate();

            AssertThat(await WaitUntilCameraSettles(ui))
                .OverrideFailureMessage(
                    "The camera never settled after FocusOnMineGate — the pan is stuck, never armed, " +
                    "or the gate building is missing.")
                .IsTrue();

            // The world region is derived INDEPENDENTLY of Town2D's own rect — from the viewport
            // size and the header's measured bottom edge — rather than trusting
            // `ui.Town.GetGlobalRect()` directly. Verified against the pre-U2 code (a live
            // mutation check, not a hypothetical): under the old full-rect-behind-the-header
            // mount, Town2D's own rect WAS the whole window, so comparing the gate against it
            // would have passed vacuously even with the gate sitting at screen y=16 — 147px
            // inside the header's 163px band. Comparing against the header's own measured edge
            // catches that; comparing against Town2D's rect would not have.
            var viewport = ui.GetViewportRect().Size;
            var headerBottom = ui.HudHeader.GetGlobalRect().End.Y;
            var worldRegion = new Rect2(0f, headerBottom, viewport.X, viewport.Y - headerBottom);
            var gateRect = BuildingScreenRect(ui.Town, gate);

            // Grown by a hair to absorb sub-pixel float rounding in the world->canvas->screen chain
            // (the same class of slack WaitForLayout's own WindowRect tolerance exists for) —
            // never enough to hide a real clip.
            AssertThat(worldRegion.Grow(1.5f).Encloses(gateRect))
                .OverrideFailureMessage(
                    $"The mine gate's on-screen rect {gateRect} is not fully inside the world region " +
                    $"{worldRegion} (viewport below the header) once the camera settled on it. This is " +
                    "the exact PT8 finding ('mine is off the screen at the top') — the gate sits at " +
                    "the town's far north edge, historically the first thing the header's occluded " +
                    "band ate.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// Waits on the CAMERA-ARRIVED condition — never a frame count (task brief) — by watching
    /// <see cref="Town2D.Cam"/>'s actual RENDERED position
    /// (<see cref="Camera2D.GetScreenCenterPosition"/>, what the viewport's canvas transform is
    /// built from) STOP CHANGING between frames, the same "wait for the geometry itself, not a
    /// guessed duration" idiom <c>HumanPlayer.WaitForLayout</c> uses for Control rects.
    ///
    /// <para><b>Deliberately not "distance to the focus target below an epsilon."</b> A live
    /// probe proved that condition can never fire: <see cref="Camera2D.LimitTop"/> (0, set in
    /// <c>Town2D.Build</c>) clamps the RENDERED center so the view never shows above the town's
    /// own top edge, and the mine gate sits close enough to that edge that the settled camera
    /// stops ~40 world-px short of the gate's exact anchor — correctly, by design, not a bug.
    /// "Stopped moving" is the honest arrival condition; "reached the exact target" is not always
    /// true even when the pan worked.</para>
    ///
    /// <para>Bounded by <paramref name="maxFrames"/> as a hang guard only — and deliberately well
    /// under <c>Town2D.MineGateFocusSeconds</c> (3.2s): the SAME probe found the beat's own
    /// smoothing settles in well under one second (frame ~40-45 at 60fps), so a generous 150-frame
    /// (2.5s) cap still catches it comfortably before the beat itself expires and the camera
    /// reverts to the player — waiting past that point would make this test measure the WRONG
    /// moment (the player's position, not the pan) instead of failing loudly.</para>
    /// </summary>
    private static async Task<bool> WaitUntilCameraSettles(
        MainUi ui, float stillPx = 0.1f, int maxFrames = 150)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        await ui.ToSignal(tree, SceneTree.SignalName.ProcessFrame); // let the beat apply at least once
        var previous = ui.Town.Cam.GetScreenCenterPosition();
        for (var frame = 0; frame < maxFrames; frame++)
        {
            await ui.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            var current = ui.Town.Cam.GetScreenCenterPosition();
            if (current.DistanceTo(previous) < stillPx)
            {
                return true;
            }

            previous = current;
        }

        return false;
    }

    /// <summary>
    /// World -> canvas -> screen for a building's whole clickable footprint (extends
    /// <c>RealClickReachesBuildingTests</c>' single-point version of this same chain to all four
    /// corners of its <see cref="Building2D.Interact"/> shape), so callers get a screen-space
    /// <see cref="Rect2"/> they can fully-contain-check against the world region instead of a
    /// single point that happens to land inside it.
    /// </summary>
    private static Rect2 BuildingScreenRect(Town2D town, Building2D building)
    {
        var shape = building.Interact.GetChild<CollisionShape2D>(0);
        var rect = (RectangleShape2D)shape.Shape;
        var half = rect.Size / 2f;
        var centerWorld = building.Interact.GlobalPosition + shape.Position;

        Vector2 ToScreen(Vector2 worldPoint)
        {
            var canvasPoint = town.WorldViewport.GetCanvasTransform() * worldPoint;
            return town.GetGlobalRect().Position + (canvasPoint * town.CanvasShrink);
        }

        var corners = new[]
        {
            ToScreen(centerWorld + new Vector2(-half.X, -half.Y)),
            ToScreen(centerWorld + new Vector2(half.X, -half.Y)),
            ToScreen(centerWorld + new Vector2(-half.X, half.Y)),
            ToScreen(centerWorld + new Vector2(half.X, half.Y)),
        };

        var minX = corners.Min(p => p.X);
        var maxX = corners.Max(p => p.X);
        var minY = corners.Min(p => p.Y);
        var maxY = corners.Max(p => p.Y);
        return new Rect2(minX, minY, maxX - minX, maxY - minY);
    }
}
#endif
