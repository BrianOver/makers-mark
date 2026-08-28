#if GDUNIT_TESTS
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// U19 (§11.14.14, R32): <see cref="StuckPlayerDetector"/> is plain, engine-free bookkeeping — same
/// shape as <see cref="FirstTouchLessonsTests"/>'s own coverage of <see cref="FirstTouchLessons"/> —
/// so none of these need <c>[RequireGodotRuntime]</c>. The full stack (a real idle window offering a
/// real step's teaching through <c>MentorBanner</c>, a real refusal promoting the banner, the once-ever
/// gate holding across both) is <c>StuckPlayerMainUiTests</c>'s job; this file pins the two counting
/// primitives that stack is built on.
/// </summary>
[TestSuite]
public class StuckPlayerDetectorTests
{
    // ── TickIdle: the idle half ───────────────────────────────────────────────────────────────

    [TestCase]
    public void TickIdle_ReturnsFalse_WhileBelowTheThreshold()
    {
        var detector = new StuckPlayerDetector();

        AssertThat(detector.TickIdle(10, 45)).IsFalse();
        AssertThat(detector.TickIdle(10, 45)).IsFalse();
        AssertThat(detector.TickIdle(10, 45)).IsFalse(); // 30 total — still short of 45
    }

    [TestCase]
    public void TickIdle_ReturnsTrue_ExactlyTheTickItCrossesTheThreshold()
    {
        var detector = new StuckPlayerDetector();

        AssertThat(detector.TickIdle(40, 45)).IsFalse();
        AssertThat(detector.TickIdle(5, 45))
            .OverrideFailureMessage("40 + 5 = 45 should cross the threshold on this exact call.")
            .IsTrue();
    }

    /// <summary>The one-shot latch — this is the whole anti-repeat contract <see
    /// cref="StuckPlayerDetector"/>'s own class doc claims for the idle half: once offered, silence,
    /// no matter how much MORE idle time accumulates, until something calls <see
    /// cref="StuckPlayerDetector.ResetIdle"/>.</summary>
    [TestCase]
    public void TickIdle_NeverFiresASecondTime_WithoutAResetBetween()
    {
        var detector = new StuckPlayerDetector();

        AssertThat(detector.TickIdle(50, 45)).IsTrue();
        AssertThat(detector.TickIdle(1000, 45))
            .OverrideFailureMessage("A one-shot latch fired a second time from continued idling alone.")
            .IsFalse();
        AssertThat(detector.TickIdle(1000, 45)).IsFalse();
    }

    [TestCase]
    public void ResetIdle_ZeroesTheAccumulatedTime_NotJustTheLatch()
    {
        var detector = new StuckPlayerDetector();
        detector.TickIdle(44, 45); // one second short

        detector.ResetIdle();

        // If Reset only cleared the latch (and not the accumulated seconds), one more second here
        // would already cross 45 — proving the seconds themselves were truly zeroed, not just unlatched.
        AssertThat(detector.TickIdle(1, 45))
            .OverrideFailureMessage("ResetIdle left prior accumulated idle time in place.")
            .IsFalse();
    }

    [TestCase]
    public void ResetIdle_LetsTheLatchFireAgain()
    {
        var detector = new StuckPlayerDetector();
        AssertThat(detector.TickIdle(45, 45)).IsTrue();

        detector.ResetIdle();

        AssertThat(detector.TickIdle(45, 45))
            .OverrideFailureMessage("A reset detector never offered help a second time.")
            .IsTrue();
    }

    // ── RegisterRefusal: the repeated-refusal half ────────────────────────────────────────────

    [TestCase]
    public void RegisterRefusal_ReturnsTheRunningCount_ForThatExactText()
    {
        var detector = new StuckPlayerDetector();

        AssertThat(detector.RegisterRefusal("You can't afford that yet.")).IsEqual(1);
        AssertThat(detector.RegisterRefusal("You can't afford that yet.")).IsEqual(2);
        AssertThat(detector.RegisterRefusal("You can't afford that yet.")).IsEqual(3);
    }

    [TestCase]
    public void RegisterRefusal_CountsEachDistinctTextIndependently()
    {
        var detector = new StuckPlayerDetector();

        AssertThat(detector.RegisterRefusal("A")).IsEqual(1);
        AssertThat(detector.RegisterRefusal("B")).IsEqual(1);
        AssertThat(detector.RegisterRefusal("A")).IsEqual(2);
        AssertThat(detector.RegisterRefusal("B")).IsEqual(2);
    }

    /// <summary>R32 asks for help on the third OCCURRENCE of the same refusal, not the third
    /// CONSECUTIVE one — a different refusal (or a real action) landing in between must not reset
    /// the count, unlike <see cref="StuckPlayerDetector.ResetIdle"/>'s own idle half.</summary>
    [TestCase]
    public void RegisterRefusal_NeverResetsOnItsOwn_EvenAcrossAnUnrelatedRefusal()
    {
        var detector = new StuckPlayerDetector();

        detector.RegisterRefusal("gold");
        detector.RegisterRefusal("materials"); // an unrelated refusal in between
        detector.RegisterRefusal("gold");
        var third = detector.RegisterRefusal("gold");

        AssertThat(third)
            .OverrideFailureMessage("An unrelated refusal in between reset the count for the recurring one.")
            .IsEqual(3);
    }
}
#endif
