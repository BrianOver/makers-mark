#if GDUNIT_TESTS
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U-T2-7 (Wave A substrate, §11.14.4): <see cref="TutorialFlow.ConsumeFirstTouch"/>'s own
/// persistence and independence contract — <see cref="FirstTouchLessonsTests"/> already proves the
/// underlying <see cref="FirstTouchLessons"/> engine's anti-nag pin in isolation; this file proves
/// <see cref="TutorialFlow"/> actually persists it across a reload (the SAME "second, independent
/// instance reading the SAME user:// file" idiom <c>TutorialRegistryConformanceTests
/// .MidTutorialProgress_PersistsAcrossAReload_WithNoDismissOrComplete</c> already established for
/// <see cref="TutorialFlow.Step"/>) and never gates on <see cref="TutorialFlow.Active"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialFirstTouchTests
{
    [TestCase]
    public void ConsumeFirstTouch_ReturnsTheText_OnlyTheFirstTimeForAGivenId()
    {
        var flow = new TutorialFlow();
        flow.Build();
        try
        {
            var first = flow.ConsumeFirstTouch("test-first-touch", "A one-time lesson.");
            var second = flow.ConsumeFirstTouch("test-first-touch", "A one-time lesson.");

            AssertThat(first).IsEqual("A one-time lesson.");
            AssertThat(second)
                .OverrideFailureMessage("ConsumeFirstTouch fired a SECOND time for the same id — the anti-nag pin failed at the TutorialFlow seam.")
                .IsNull();
            AssertThat(flow.FirstTouch.HasFired("test-first-touch")).IsTrue();
        }
        finally
        {
            flow.Free();
            // A fired ConsumeFirstTouch call Saves() unconditionally (its own doc: "persisted
            // immediately") to the SAME user://tutorial_flow.json every MountMainUi-paired test
            // relies on Unmount's own TutorialFlow.DeleteForTests() to keep clean. This suite builds
            // a bare TutorialFlow with no MainUi/Unmount pair, so IT is the one that must delete the
            // file it just wrote — leaving it behind otherwise leaks whatever Completed/Dismissed
            // this test's own flow happened to have into the NEXT engine test's fresh MountMainUi(),
            // which reads it back via Load() at boot. Root-cause of the cross-suite Active=false flake
            // this fix closes (see ConsumeFirstTouch_StillFires_AfterTheChainIsDismissed below).
            TutorialFlow.DeleteForTests();
        }
    }

    /// <summary>The persistence half — a fired id must survive a reload and refuse to re-fire on a
    /// brand-new <see cref="TutorialFlow"/> instance reading the same save.</summary>
    [TestCase]
    public void ConsumeFirstTouch_PersistsAcrossAReload_AndNeverRefires()
    {
        var flow = new TutorialFlow();
        flow.Build();
        try
        {
            var fired = flow.ConsumeFirstTouch("reload-check", "Fired before the reload.");
            AssertThat(fired).IsNotNull();
        }
        finally
        {
            flow.Free();
            // Deliberately NOT TutorialFlow.DeleteForTests() here — the whole point of this test is
            // that user://tutorial_flow.json survives into the second instance below. Cleanup runs
            // once, after THAT instance is done reading it (same doc as the sibling test above).
        }

        var reloaded = new TutorialFlow();
        try
        {
            reloaded.Build();
            reloaded.Load();

            AssertThat(reloaded.FirstTouch.HasFired("reload-check"))
                .OverrideFailureMessage("A first-touch fire did not survive a reload — TutorialFlow.Save/Load dropped it.")
                .IsTrue();
            AssertThat(reloaded.FirstTouch.Fired["reload-check"]).IsEqual("Fired before the reload.");
            AssertThat(reloaded.ConsumeFirstTouch("reload-check", "a different line"))
                .OverrideFailureMessage("A reloaded TutorialFlow re-fired an id that had already fired in a prior session — exactly the 1287x memorial nag shape, at the persistence seam this time.")
                .IsNull();
        }
        finally
        {
            reloaded.Free();
            TutorialFlow.DeleteForTests(); // see the sibling test's doc: this suite owns its own leak guard
        }
    }

    /// <summary>Mirrors <see cref="TutorialFlow.ConsumeLedgerTip"/>'s own precedent: the long tail's
    /// lessons matter to every campaign, dismissed or not.</summary>
    [TestCase]
    public void ConsumeFirstTouch_StillFires_AfterTheChainIsDismissed()
    {
        var flow = new TutorialFlow();
        flow.Build();
        try
        {
            flow.Dismiss();
            AssertThat(flow.Active).IsFalse();

            var fired = flow.ConsumeFirstTouch("post-dismiss", "Still teaches after dismissal.");

            AssertThat(fired)
                .OverrideFailureMessage("A dismissed chain refused to fire a first-touch lesson — this tier must be independent of Active, same as ConsumeLedgerTip.")
                .IsEqual("Still teaches after dismissal.");
        }
        finally
        {
            flow.Free();
            // This test's own ConsumeFirstTouch call Saves() Dismissed=true to disk (see the first
            // test's doc). Without this line that flag leaked into every later engine test's fresh
            // MountMainUi() this session — including TutorialFlowTests.FreshCampaign_..., which reads
            // it back via Load() and finds Active already false on a "fresh" campaign it never
            // touched. Confirmed root cause of that exact cross-suite flake.
            TutorialFlow.DeleteForTests();
        }
    }
}
#endif
