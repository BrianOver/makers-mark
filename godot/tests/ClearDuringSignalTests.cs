#if GDUNIT_TESTS
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Pressing a panel button must not destroy that button while its signal is still emitting.
///
/// <para><b>The crash.</b> The owner clicked auto-craft and the process died with signal 11, after
/// Godot logged "Object was freed or unreferenced while a signal is being emitted from it". The stack
/// was a straight line: the Craft button's pressed handler → <c>ForgePanel.OnCraftPressed</c> →
/// <c>SimAdapter.Queue</c> → (Queue ticks the sim SYNCHRONOUSLY) → <c>MainUi.OnPhaseCompleted</c> →
/// <c>RefreshAll</c> → <c>ForgePanel.Refresh</c> → <c>SimPanel.Clear</c> → <c>Free()</c> on the very
/// button mid-emit. The identical stack exists via <c>ShopPanel.PlaceOnShelf</c>, which is the second
/// warning in the same log.</para>
///
/// <para><b>Why nothing caught it.</b> <c>SimPanel.Clear</c> documented an invariant — "never called
/// from a signal handler of a node being cleared" — that the game's single most common action broke,
/// and no test drove a real button press through a sim tick. Tests that call <c>OnCraftPressed</c>
/// directly (rather than through the button's signal) never put a live emission on the stack, so the
/// use-after-free could not happen in them.</para>
///
/// <para>These two tests therefore do the awkward thing on purpose: press the REAL button through its
/// signal, and separately pin the freeing mechanism itself.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public partial class ClearDuringSignalTests
{
    /// <summary>
    /// The end-to-end shape. If <c>Clear</c> regresses to an immediate <c>Free</c>, this does not
    /// merely fail — it takes the test runner's process down with signal 11, which is itself an
    /// unmissable signal.
    /// </summary>
    [TestCase]
    public void PressingCraftThroughItsSignal_DoesNotFreeTheButtonMidEmission()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            ui.Adapter.AdvancePhase();
            ui.OpenPanel("Forge");

            // Through the signal, NOT by calling OnCraftPressed directly — the direct call is what
            // every previous test did, and it is precisely why none of them could reproduce this.
            PressEnabled(ui.Forge, $"Craft_{ScriptedSession.CraftRecipeId}");

            // Surviving to here is most of the assertion. The rest proves the refresh still happened
            // rather than being skipped to dodge the crash.
            var queued = ui.Adapter.AppliedThisPhase.OfType<CraftAction>().ToList();
            AssertThat(queued.Count)
                .OverrideFailureMessage(
                    "Pressing Craft through its real pressed signal queued no CraftAction — the "
                    + "action path regressed, or the press never reached the handler.")
                .IsEqual(1);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The mechanism, pinned directly: after <c>Clear</c> the child must be OUT of the tree (so a
    /// rebuild starts empty and leaves no stale rows — the original reason immediate Free was chosen)
    /// but must NOT yet be destroyed (so an in-flight signal on it cannot dereference freed memory).
    /// </summary>
    [TestCase]
    public void Clear_DetachesImmediately_ButDefersDestruction()
    {
        var parent = new Control { Name = "ClearProbeParent" };
        ((SceneTree)Engine.GetMainLoop()).Root.AddChild(parent);
        try
        {
            var child = new Button { Name = "ClearProbeChild", Text = "probe" };
            parent.AddChild(child);

            ClearProbe.Run(parent);

            AssertThat(parent.GetChildCount())
                .OverrideFailureMessage(
                    "Clear left children attached. A rebuild would then append to the old rows and "
                    + "the panel would show stale duplicates.")
                .IsEqual(0);

            AssertThat(GodotObject.IsInstanceValid(child))
                .OverrideFailureMessage(
                    "Clear destroyed the child IMMEDIATELY. That is the signal-11 crash: when Clear "
                    + "runs inside a button's own pressed emission (Craft -> Queue -> ticks the sim "
                    + "-> RefreshAll -> Refresh -> Clear), freeing now dereferences the emitting "
                    + "object. It must be QueueFree so destruction lands at end of frame.")
                .IsTrue();
        }
        finally
        {
            parent.QueueFree();
        }
    }

    /// <summary>Reaches <c>SimPanel</c>'s protected static <c>Clear</c> — the thing under test is the
    /// shared helper every panel inherits, not any one panel's use of it.</summary>
    private sealed partial class ClearProbe : GodotClient.Panels.SimPanel
    {
        public static void Run(Node parent) => Clear(parent);

        public override void Refresh()
        {
            // Never called: this probe exists only to reach the protected static Clear.
        }
    }
}
#endif
