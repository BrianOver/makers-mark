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
}
#endif
