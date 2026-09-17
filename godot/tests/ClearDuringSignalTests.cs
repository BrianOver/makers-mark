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
///
/// <para><b>A third, independent instance (P2-MEMORY-14, found 2026-09-15).</b>
/// <c>GodotClient.Panels.LegendsWall</c> extends <c>Control</c>, not <c>SimPanel</c>, so it carried
/// its OWN private <c>Clear</c> — written before the fix above existed, with the same immediate
/// <c>Free()</c> the two tests below pin against. Four of its own buttons rebuild <c>_body</c> from
/// inside their own <c>Pressed</c> handler (<c>BindTheBook</c> and the "LegendsWallBack" button on
/// <c>ShowActorPage</c>/<c>ShowItemPage</c>/<c>RenderBindPage</c>), so every one of them hit this
/// exact crash class on every press — caught live in a local full-suite run, never by CI, because no
/// test asserted the pressed button's own survival. Fixed by routing <c>LegendsWall.Clear</c> through
/// the same <see cref="GodotClient.Panels.PanelGraveyard"/> registry rather than reinventing a
/// second copy; regression coverage lives with its own sibling tests in <c>LegendsWallTests</c>
/// (<c>BindTheBookButton_OpensTheClosingChapter_ReachableAndReturnable</c>,
/// <c>BookShellMigration_LosesNoVerb_EveryPreExistingControlStillResolves</c>,
/// <c>LegendItemRow_OpensTheItemsOwnPage_NotTheOldPopup</c>), not duplicated here — same reason
/// <c>ShopPanel.PlaceOnShelf</c> above gets a mention and not a third copy of this file's own two
/// tests: both share the exact mechanism already pinned below.</para>
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
