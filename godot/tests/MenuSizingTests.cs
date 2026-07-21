#if GDUNIT_TESTS
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Menu-sizing regression (gate-b, playtest-observed): the OBJECTIVE panel collapsed to ~1
/// char wide, and the day-phase timeline row rendered with zero gap between phase labels
/// (run-on text). These mount the real MainUi, settle container layout, and assert the two
/// fixed geometry/theme facts directly — never text content.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MenuSizingTests
{
    [TestCase]
    public async Task ObjectiveTracker_AfterLayout_ResolvesToDockWidth()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            var tracker = ui.Objective; // public property on MainUi
            AssertThat(tracker.Size.X >= GodotClient.Ui.ObjectiveTracker.DockWidth - 1f)
                .OverrideFailureMessage($"objective width {tracker.Size.X} < {GodotClient.Ui.ObjectiveTracker.DockWidth}")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public async Task DayTimeline_HasNonZeroSeparation()
    {
        var ui = MountMainUi();
        try
        {
            await SettleLayout(ui);
            AssertThat(ui.Timeline.GetThemeConstant("separation") >= 6).IsTrue();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// Resize follow-up (fast-follow): the objective chip's OffsetTop/OffsetBottom clamp used to
    /// run once at build time only, so a live resize to a shorter window could clip it (the chip
    /// is docked TopRight, so these offsets ARE the absolute on-screen Y coordinates). MainUi now
    /// wires <see cref="Godot.Viewport.SizeChanged"/> to re-run the same clamp
    /// (<c>MainUi.ClampObjectiveDockToViewport</c>) — this shrinks the root viewport to a short
    /// height after mount and asserts the chip's bottom edge never exceeds it. 2D-UI only (no 3D
    /// SubViewport frame-pump), so <see cref="SettleLayout"/> is safe here.
    /// </summary>
    [TestCase]
    public async Task ObjectiveTracker_AfterShortResize_BottomStaysWithinViewport()
    {
        var ui = MountMainUi();
        var root = ui.GetTree().Root;
        var originalSize = root.Size;
        try
        {
            await SettleLayout(ui);

            const int ShortHeight = 150; // well under OffsetTop(64) + DockHeight(260)
            root.Size = new Vector2I(originalSize.X, ShortHeight);
            await SettleLayout(ui);

            var tracker = ui.Objective;
            var viewportHeight = ui.GetViewportRect().Size.Y;
            AssertThat(tracker.OffsetBottom <= viewportHeight)
                .OverrideFailureMessage(
                    $"objective bottom {tracker.OffsetBottom} exceeds viewport height {viewportHeight}")
                .IsTrue();
            AssertThat(tracker.OffsetTop < tracker.OffsetBottom)
                .OverrideFailureMessage(
                    $"objective chip collapsed: top {tracker.OffsetTop} >= bottom {tracker.OffsetBottom}")
                .IsTrue();
        }
        finally
        {
            root.Size = originalSize; // never leak a resized window into later test cases
            Unmount(ui);
        }
    }
}
#endif
