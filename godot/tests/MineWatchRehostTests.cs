#if GDUNIT_TESTS
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U9 (world-and-interiors plan, KTD-4): the plan's own flagged mitigation for its riskiest unit
/// -- a SEQUENTIAL-HOST re-parent test, proving the single shared <see cref="MineWatch"/> strip
/// never has two live parents (let alone two live <see cref="SubViewport"/>s -- constraint 4's
/// central hazard, "pumping frames while ANY SubViewport renders hangs gdUnit headless") as it
/// moves between <see cref="DepthsPanel"/> (its resting host) and <see cref="ScryingMirror"/>
/// (which borrows it for as long as it's open). Every scenario here also counts live
/// <see cref="MineWatch"/> instances under the whole mounted <c>MainUi</c> tree directly --
/// a <see cref="MineWatch"/> owns exactly one <see cref="SubViewport"/> internally, so that count
/// IS the live-viewport count this constraint cares about.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MineWatchRehostTests
{
    [TestCase]
    public void FreshMount_StripRestsInDepths_MirrorHasNone()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Depths.Watch).IsNotNull();
            AssertThat(ReferenceEquals(ui.Depths.Watch, ui.Watch)).IsTrue();
            AssertThat(ui.Mirror.Watch).IsNull();
            AssertThat(OnlyOneMineWatchUnder(ui)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ShowMirror_StealsTheStrip_DepthsLosesIt_StillOnlyOneInstance()
    {
        var ui = MountMainUi();
        try
        {
            ui.Mirror.ShowMirror();

            AssertThat(ReferenceEquals(ui.Mirror.Watch, ui.Watch))
                .OverrideFailureMessage("ScryingMirror did not borrow the shared strip on open.")
                .IsTrue();
            AssertThat(ui.Depths.Watch)
                .OverrideFailureMessage(
                    "The strip is still parented in DepthsPanel even though the Mirror is open -- " +
                    "two hosts would think they own it.")
                .IsNull();
            AssertThat(OnlyOneMineWatchUnder(ui)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CloseMirror_ReturnsTheStripToDepths_MirrorHasNone()
    {
        var ui = MountMainUi();
        try
        {
            ui.Mirror.ShowMirror();
            ui.Mirror.CloseMirror();

            AssertThat(ReferenceEquals(ui.Depths.Watch, ui.Watch))
                .OverrideFailureMessage("The strip was not handed back to DepthsPanel when the Mirror closed.")
                .IsTrue();
            AssertThat(ui.Mirror.Watch).IsNull();
            AssertThat(OnlyOneMineWatchUnder(ui)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void SequentialOpenClose_ReParentsCleanlyEveryRound_NeverTwoInstances()
    {
        // The plan's own mandated mitigation: a SEQUENTIAL re-parent test, not a single open/close
        // pair -- proving the handoff is a repeatable mechanism, not a one-shot fluke.
        var ui = MountMainUi();
        try
        {
            for (var round = 0; round < 4; round++)
            {
                ui.Mirror.ShowMirror();
                AssertThat(ReferenceEquals(ui.Mirror.Watch, ui.Watch))
                    .OverrideFailureMessage($"round {round}: Mirror did not host the strip on open.")
                    .IsTrue();
                AssertThat(ui.Depths.Watch)
                    .OverrideFailureMessage($"round {round}: Depths still held the strip while the Mirror was open.")
                    .IsNull();
                AssertThat(OnlyOneMineWatchUnder(ui))
                    .OverrideFailureMessage($"round {round}: more than one live MineWatch instance while the Mirror was open.")
                    .IsTrue();

                ui.Mirror.CloseMirror();
                AssertThat(ReferenceEquals(ui.Depths.Watch, ui.Watch))
                    .OverrideFailureMessage($"round {round}: Depths did not get the strip back on close.")
                    .IsTrue();
                AssertThat(ui.Mirror.Watch)
                    .OverrideFailureMessage($"round {round}: Mirror still held the strip after close.")
                    .IsNull();
                AssertThat(OnlyOneMineWatchUnder(ui))
                    .OverrideFailureMessage($"round {round}: more than one live MineWatch instance after close.")
                    .IsTrue();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void WatchButton_OpensTheMirror_HostingTheSameSharedInstance()
    {
        // Same handoff, through the REAL entry point a player uses (the persistent Watch button
        // beside the bell), not a direct ShowMirror() call.
        var ui = MountMainUi();
        try
        {
            AdvanceToPhase(ui, DayPhase.Expedition);
            PressEnabled(ui, "WatchButton");

            AssertThat(ui.Mirror.Visible).IsTrue();
            AssertThat(ReferenceEquals(ui.Mirror.Watch, ui.Watch)).IsTrue();
            AssertThat(OnlyOneMineWatchUnder(ui)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void PipExpand_OpensTheMirror_HostingTheSameSharedInstance()
    {
        // The THIRD door to the same show (R4/R5): the PiP dock's own expand button.
        var ui = MountMainUi();
        try
        {
            AdvanceToPhase(ui, DayPhase.Expedition);
            ui.Pip._Process(0.0); // force the slide-state recompute (no real frame pump in this suite)

            Press(ui.Pip, "PipExpand");

            AssertThat(ui.Mirror.Visible).IsTrue();
            AssertThat(ReferenceEquals(ui.Mirror.Watch, ui.Watch)).IsTrue();
            AssertThat(OnlyOneMineWatchUnder(ui)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Counts every <see cref="MineWatch"/> node anywhere under <paramref name="root"/> --
    /// the direct proof of constraint 4 ("exactly one live SubViewport, ever").</summary>
    private static bool OnlyOneMineWatchUnder(Node root) => CountMineWatch(root) == 1;

    private static int CountMineWatch(Node node)
    {
        var count = node is MineWatch ? 1 : 0;
        foreach (var child in node.GetChildren())
        {
            count += CountMineWatch(child);
        }

        return count;
    }
}
#endif
