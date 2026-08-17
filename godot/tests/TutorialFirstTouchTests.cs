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
        }
    }
}
#endif
