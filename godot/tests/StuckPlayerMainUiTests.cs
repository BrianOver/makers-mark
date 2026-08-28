#if GDUNIT_TESTS
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U19 (§11.14.14, R32): "a player who is stuck, idle, or repeatedly refused is offered help once,
/// without a nag." <see cref="StuckPlayerDetectorTests"/> pins the counting primitives in isolation;
/// this suite drives the whole stack through the real <see cref="MainUi"/> — the idle clock actually
/// reaching <c>MainUi.Mentor</c> through <c>TutorialFlow.ConsumeFirstTouch</c>, and a real, repeatedly
/// refused <see cref="PlayerAction"/> actually promoting the toast onto that same banner.
///
/// <para><c>ui._Process(delta)</c> is called directly with large deltas (the same seam <see
/// cref="RejectionUxTests"/> already uses for <c>MainUi.RejectionToastSeconds</c>) rather than pumping
/// real engine frames — <c>MainUi.StuckIdleThresholdSeconds</c> is 45 real seconds, and no test here
/// should cost 45 seconds of wall-clock to prove a wall-clock fact.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class StuckPlayerMainUiTests
{
    private static string BannerText(MainUi ui) =>
        ui.Mentor.FindChild("MentorBannerText", true, false) is Label label ? label.Text : string.Empty;

    // ── Idle half ──────────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void IdlingPastThreshold_OffersCurrentStepsTeaching_Once_AndASecondIdlePeriod_OffersNothing()
    {
        var ui = MountMainUi();
        try
        {
            var step = ui.Tutorial.Step; // BuyMaterial on a fresh mount
            AssertThat(ui.Mentor.Visible).IsFalse();

            ui._Process(MainUi.StuckIdleThresholdSeconds + 0.1);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("Idling past the threshold did not offer the current step's own teaching.")
                .IsTrue();
            AssertThat(BannerText(ui)).IsEqual(ObjectiveTracker.Plain(MentorVoice.CurrentLesson(step)));

            ui.Mentor.Dismiss();
            AssertThat(ui.Mentor.Visible).IsFalse();

            // A real, unrelated (refused) action — proves the SECOND idle window is genuinely a
            // second one, not just more of the first (see the real-action-resets test below for the
            // reset mechanism itself). Chosen specifically because it does not touch BuyMaterial's
            // own IsDone fact (MaterialPurchased), so Step stays put and this really is "the same
            // step, idle again" rather than a different step's own first idle window.
            ui.Adapter.Queue(new BuyOreAction(new HeroId(1), ScriptedSession.CraftMaterial, 1));
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Fixture guard: this action must not itself advance BuyMaterial's own slot.")
                .IsEqual(step);

            ui._Process(MainUi.StuckIdleThresholdSeconds + 0.1);
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("A second idle period for the SAME step re-offered the same teaching.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void RealAction_ResetsTheIdleClock_SoThePreResetElapsedTimeDoesNotCount()
    {
        var ui = MountMainUi();
        try
        {
            var half = MainUi.StuckIdleThresholdSeconds / 2;
            ui._Process(half); // halfway there — not idle long enough to offer help yet
            AssertThat(ui.Mentor.Visible).IsFalse();

            // A real action — even a refused one, through SimAdapter.Queue's own one choke point
            // (StuckPlayerDetector's own class doc) — counts as the player doing something.
            ui.Adapter.Queue(new BuyOreAction(new HeroId(1), ScriptedSession.CraftMaterial, 1));
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Fixture guard: this action must not itself advance the step.")
                .IsEqual(TutorialStep.BuyMaterial);

            // If the clock had NOT reset, total elapsed (half + half + 0.1) would already clear the
            // threshold here. It must not — the pre-reset half-window was thrown away.
            ui._Process(half + 0.1);
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("A real action did not reset the idle clock — help fired on the pre-reset elapsed time.")
                .IsFalse();

            // Genuinely half+0.1 seconds idle SINCE the reset — now past the threshold.
            ui._Process(half + 0.1);
            AssertThat(ui.Mentor.Visible).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void NothingFiresOnceTheCourseIsComplete()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tutorial.Dismiss(); // Active goes false the same way Completed would (both gate CheckStuckPlayer)
            AssertThat(ui.Tutorial.Active).IsFalse();

            ui._Process(MainUi.StuckIdleThresholdSeconds * 4); // idle far past the threshold, repeatedly

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The idle-help offer fired after the tutorial chain was no longer active.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void IdleOffer_SurvivesBeingIgnored_WithoutReFiringOrQueuingBehindItself()
    {
        var ui = MountMainUi();
        try
        {
            ui._Process(MainUi.StuckIdleThresholdSeconds + 0.1);
            AssertThat(ui.Mentor.Visible).IsTrue();
            var text = BannerText(ui);

            // Keep "idling" WITHOUT dismissing — the offer must sit exactly as it landed, never
            // duplicated into the backlog behind itself and never replaced by a second line.
            ui._Process(MainUi.StuckIdleThresholdSeconds * 3);

            AssertThat(ui.Mentor.Visible).IsTrue();
            AssertThat(BannerText(ui)).IsEqual(text);
            AssertThat(ui.Mentor.PendingLessonCount)
                .OverrideFailureMessage("An ignored idle offer queued a duplicate of itself behind the original.")
                .IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Law 2 (no timers on decisions), made concrete: the ONLY effect of the idle clock
    /// crossing its threshold is that the banner offers itself. It must never itself touch
    /// <see cref="GameState"/> — no gold spent, no slot consumed, no phase advanced — the way a real
    /// timed GATE on a decision would.</summary>
    [TestCase]
    public void CrossingTheIdleThreshold_NeverChangesGameState_OnlyOffersTheBanner()
    {
        var ui = MountMainUi();
        try
        {
            var before = ui.Adapter.CurrentState;

            ui._Process(MainUi.StuckIdleThresholdSeconds + 0.1);

            AssertThat(ui.Mentor.Visible).IsTrue(); // fixture guard: the offer actually fired
            AssertThat(ui.Adapter.CurrentState)
                .OverrideFailureMessage("Crossing the idle threshold changed GameState — a wall-clock timer decided something for the player.")
                .IsEqual(before);
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── Refusal half ───────────────────────────────────────────────────────────────────────────

    [TestCase]
    public void NthIdenticalRefusal_PromotesToTheBanner_AndOneMoreDoesNotRepeatIt()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Mentor.Visible).IsFalse();

            // One short of the promotion count: still just the ordinary, auto-clearing toast.
            for (var i = 1; i < MainUi.StuckRefusalPromotionCount; i++)
            {
                ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.UnaffordablePrice));
            }

            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(MainUi.StuckRefusalPromotionCount - 1);
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("Promoted to the banner before the identical refusal reached StuckRefusalPromotionCount.")
                .IsFalse();

            // The Nth identical refusal.
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.UnaffordablePrice));
            var rejected = ui.Adapter.LastRejections[^1];
            var friendly = MainUi.FriendlyRejection(rejected.Reason, rejected.Action);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The Nth identical refusal did not promote its note to the banner.")
                .IsTrue();
            AssertThat(BannerText(ui)).IsEqual(ObjectiveTracker.Plain(MentorVoice.Speak(friendly)));

            ui.Mentor.Dismiss();
            AssertThat(ui.Mentor.Visible).IsFalse();

            // One more, past the promotion count — the once-ever gate must hold.
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.UnaffordablePrice));
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("A refusal past the promotion count repeated the banner promotion.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The ordinary, un-promoted toast is untouched by any of this — the player still sees
    /// the friendly line every single time, exactly as before this unit (R32 adds an escalation, it
    /// does not remove the existing feedback).</summary>
    [TestCase]
    public void BelowThePromotionCount_TheOrdinaryToastStillRendersEveryTime()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.UnaffordablePrice));
            AssertThat(RenderedText(ui)).Contains("You can't afford that yet.");
            AssertThat(ui.Mentor.Visible).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
