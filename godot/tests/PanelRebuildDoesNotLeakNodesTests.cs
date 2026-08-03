#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Rebuilding a panel must not strand its old subtree in the shared Godot runtime.
///
/// <para><b>Why this exists.</b> <c>SimPanel.Clear</c> detaches each child and <c>QueueFree</c>s it.
/// Both halves are deliberate and individually correct — it CANNOT free immediately, because
/// <c>Clear</c> runs inside the pressed-signal emission of the very button it is clearing (see
/// <c>ClearDuringSignalTests</c> for the signal-11 crash that caused), and it CANNOT skip
/// <c>RemoveChild</c>, because a rebuild that appends beside the old rows gets auto-renamed
/// duplicates and every name-based lookup breaks. Together, though, they leaked: <c>RemoveChild</c>
/// makes the node PARENTLESS, so <c>Unmount</c>'s synchronous <c>ui.Free()</c> has no path to cascade
/// down to it, and <c>QueueFree</c> defers the actual delete to a frame boundary that an engine test
/// driving the sim directly never reaches. So every panel rebuild stranded its entire previous
/// subtree — permanently, in a runtime every later test in the session shares.</para>
///
/// <para><b>What it cost, measured on this suite before the fix:</b> ~468,000 stranded nodes across
/// 144 warning-emitting tests, of which 375,655 came from
/// <c>Playtest3dClickThrough.PlayTheClient_ByClicking_EveryVerbButton_AcrossAFullSession</c> alone.
/// gdUnit surfaces these as <c>Detected &lt;N&gt; orphan nodes during test execution!</c> — a warning,
/// not a failure, which is why hundreds of them accumulated unchallenged. Under enough of that
/// pressure the shared runtime dies mid-session (<c>Connection interrupted by cancellation
/// requested</c>; exit code -1073741819 on Windows, 139 on Linux CI) and the suite then reports
/// <c>Passed!</c> for however much of itself it managed to finish. With a pass FLOOR of
/// ENGINE_MIN_PASSED=300 against ~799 tests, a run that silently drops a third of itself still clears
/// the guard — so this leak could hide arbitrary test loss, which is worse than any flake.</para>
///
/// <para><b>Why it asserts on Godot's own orphan counter.</b>
/// <c>Performance.Monitor.ObjectOrphanNodeCount</c> is the engine's count of live nodes that are in NO
/// tree — the identical quantity gdUnit reports per test. Deliberately NOT
/// <c>ObjectNodeCount</c>, which counts nodes IN the tree and therefore cannot see this bug at all:
/// the whole defect is nodes that left the tree and never died. The assertion is made AFTER
/// <c>Unmount</c>, because during the test the detached nodes are legitimately awaiting a frame that
/// will never come; what must not survive is the teardown.</para>
///
/// <para>Do not "fix" a future failure here by pumping a process frame to flush the deletion queue.
/// That was measured on <c>Playtest3dClickThrough</c>: 36s to over 5 minutes. Frames are not
/// available at this cadence, which is the entire reason the leak needed closing another way.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PanelRebuildDoesNotLeakNodesTests
{
    /// <summary>Enough rebuilds that a per-rebuild leak is unmistakable against the noise floor, but
    /// still a fast test. The real client does this many rebuilds in a couple of minutes of play —
    /// every button press refreshes every panel.</summary>
    private const int Rebuilds = 50;

    /// <summary>
    /// Ceiling on nodes still orphaned after teardown. The fixed code lands at essentially zero;
    /// the unfixed code leaves thousands (50 rebuilds x 3 panels x the nodes in each). A budget in
    /// between keeps the test honest about the bug without pinning an exact node count that harmless
    /// panel-content changes would churn.
    /// </summary>
    private const int LeakBudget = 200;

    [TestCase]
    public void RebuildingPanelsManyTimes_StrandsNothingOnceTheUiIsUnmounted()
    {
        var before = OrphanNodeCount();

        var ui = MountMainUi();
        try
        {
            // MainUi owns a Town2D with a live SubViewport; leaving it rendering is the documented
            // gdUnit headless hang.
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;

            ui.OpenPanel("Forge");

            // Rebuild through the panels' real Refresh (which is what calls Clear), not by poking
            // containers — the leak lives in the shared rebuild path, so the test must use it.
            for (var i = 0; i < Rebuilds; i++)
            {
                ui.Forge.Refresh();
                ui.Heroes.Refresh();
                ui.Depths.Refresh();
            }
        }
        finally
        {
            Unmount(ui);
        }

        var leaked = OrphanNodeCount() - before;

        AssertThat(leaked)
            .OverrideFailureMessage(
                $"{Rebuilds} rebuilds of 3 panels left {leaked} nodes still alive and parented to "
                + $"nothing after the UI was unmounted (budget {LeakBudget}). SimPanel.Clear detaches "
                + "each child and QueueFrees it; a parentless node cannot be reached by Unmount's "
                + "ui.Free() cascade, and QueueFree waits for a frame boundary this test never "
                + "reaches — so the subtree survives into every later test in this shared runtime. "
                + "That is what gdUnit reports as 'Detected <N> orphan nodes', what eventually kills "
                + "the runtime mid-session with exit code -1073741819 / 139, and what then lets a "
                + "truncated run still report Passed! above the ENGINE_MIN_PASSED floor. Check that "
                + "SimPanel.Clear still hands each detached child to PanelGraveyard.Bury and that "
                + "MainUi still drains it on NOTIFICATION_ENTER_TREE / NOTIFICATION_EXIT_TREE.")
            .IsLess(LeakBudget);
    }

    /// <summary>Live nodes belonging to no tree — the engine's own counter, and the exact quantity
    /// gdUnit's per-test orphan warning reports.</summary>
    private static int OrphanNodeCount() =>
        (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount);
}
#endif
