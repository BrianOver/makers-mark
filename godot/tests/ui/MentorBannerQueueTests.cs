#if GDUNIT_TESTS
using System.Linq;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// The banner's busy-guard used to DROP a lesson that arrived while another was on screen, and its
/// own doc claimed the opposite — that the lesson "simply waits for a later call once the banner is
/// free again". There is no later call: <c>TutorialFlow.ConsumeFirstTouch</c> marks an id fired and
/// persists that BEFORE the copy ever reaches the banner, so the dropped lesson never fires again.
///
/// <para>What was actually lost was the teachable moment — the one where the player has just done
/// the thing the lesson explains. The words themselves survived: <c>LessonsPanel</c> renders every id
/// <c>FirstTouch.Fired</c> holds, forever. But a lesson buried in a book the player has to go and
/// open is worth a fraction of the same words arriving the instant they press the button, which is
/// the entire premise of the first-touch tier existing separately from that book.</para>
///
/// <para>Measured when this was found: of twelve <c>ShowFirstTouch</c> call sites in
/// <c>godot/scripts</c>, zero passed <c>preempt</c>, and <c>ForgeMentorLessonsTests</c> carried a
/// workaround comment for it. These tests pin the queue that fixes it, including the one law that
/// constrains the design: nothing appears or disappears except on a player press.</para>
/// </summary>
[TestSuite]
// RequireGodotRuntime is not optional here, and the reason is worth stating: Build() reaches
// GameTheme.PanelStyleWood()/UiKit.Card, which touch the engine's resource system, and from a bare
// [TestSuite] context that is a hard crash of the TEST HOST -- not a failure. The first run of this
// file reported "Passed! Failed: 0, Passed: 1387" with roughly eighty tests never executed. The
// pass COUNT is the only evidence a suite ran; the verdict is not.
[RequireGodotRuntime]
public class MentorBannerQueueTests
{
    private static MentorBanner Built()
    {
        var banner = new MentorBanner();
        banner.Build();
        return banner;
    }

    [TestCase]
    public void AFirstLesson_GoesStraightOnScreen()
    {
        var banner = Built();
        try
        {
            AssertThat(banner.ShowFirstTouch("first")).IsTrue();
            AssertThat(banner.Visible).IsTrue();
            AssertThat(banner.PendingLessonCount).IsEqual(0);
        }
        finally { banner.Free(); }
    }

    /// <summary>
    /// U4 (§11.14.14): before this unit, <c>MainUi.OnStationActivated</c> spoke Bryn's station
    /// press through <c>ShowBellToast</c> instead of this banner — a Label with no markup parser —
    /// so <c>TutorialFlow</c>'s <c>**bold**</c> TeachNote copy (meaningful for the CLI, meaningless
    /// for a Godot <see cref="Label"/>) rendered as literal asterisks: "**Present** a shelved
    /// item." <see cref="MentorBanner.Show"/>/<see cref="MentorBanner.ShowFirstTouch"/> now strip it
    /// on the way in (<see cref="ObjectiveTracker.Plain"/>, the same seam the objective card already
    /// uses for the identical TeachNote text), so nothing reaching this banner can carry it through.
    /// </summary>
    [TestCase]
    public void Show_StripsBoldMarkup_SoNoLiteralAsteriskReachesTheLabel()
    {
        var banner = Built();
        try
        {
            banner.Show("**Present** a shelved item, or **Suggest** one first.");

            AssertThat(BannerText(banner))
                .OverrideFailureMessage($"Bryn spoke literal markdown: \"{BannerText(banner)}\".")
                .IsEqual("Present a shelved item, or Suggest one first.");
        }
        finally { banner.Free(); }
    }

    /// <summary>Ties the strip to a REAL line rather than only a synthetic fixture: OpenCounter's
    /// TeachNote is her heaviest use of the markup (five bolded verbs) and, measured against every
    /// other row in <see cref="TutorialFlow.Registry"/>, her single longest lesson — the same one
    /// the old four-second rejection toast (<see cref="MainUi.RejectionToastSeconds"/>) truncated
    /// worst before U4 routed her station press through this banner instead. Asserting full
    /// equality against <see cref="ObjectiveTracker.Plain"/>'s own output (not just "no asterisks")
    /// proves this is a strip, not an accidental truncation to the same effect.</summary>
    [TestCase]
    public void Show_RendersHerLongestRealLesson_FullyAndWithNoMarkdown()
    {
        var withMarkup = MentorVoice.CurrentLesson(TutorialStep.OpenCounter);
        AssertThat(withMarkup.Contains("*"))
            .OverrideFailureMessage("Fixture guard: OpenCounter's TeachNote no longer carries markdown — this test needs a different lesson to mean anything.")
            .IsTrue();
        AssertThat(TutorialFlow.Registry.All(def => MentorVoice.CurrentLesson(def.Step).Length <= withMarkup.Length))
            .OverrideFailureMessage("Fixture guard: OpenCounter is no longer her longest lesson.")
            .IsTrue();

        var banner = Built();
        try
        {
            banner.Show(withMarkup);

            AssertThat(BannerText(banner)).IsEqual(ObjectiveTracker.Plain(withMarkup));
        }
        finally { banner.Free(); }
    }

    /// <summary>The defect itself: a second lesson arriving while the first is up. It must be kept,
    /// and the return value must still answer the question callers actually ask — "did THIS one get
    /// the screen" — because <c>ForgePanel</c>'s material-ceiling/mark-read pair branches on it.</summary>
    [TestCase]
    public void ASecondLesson_ArrivingWhileBusy_IsQueuedNotDropped()
    {
        var banner = Built();
        try
        {
            banner.ShowFirstTouch("first");

            AssertThat(banner.ShowFirstTouch("second"))
                .OverrideFailureMessage("A queued lesson did not get the screen, so the return must still be false.")
                .IsFalse();
            AssertThat(banner.PendingLessonCount)
                .OverrideFailureMessage("The second lesson was dropped. It is already consumed — dropping it costs it its moment.")
                .IsEqual(1);
            AssertThat(BannerText(banner))
                .OverrideFailureMessage("The queued lesson must not overwrite the one the player is still reading.")
                .IsEqual("first");
        }
        finally { banner.Free(); }
    }

    /// <summary>The drain, and the law that shapes it: the ONLY thing that advances the banner is
    /// the player's own "Got it". No timer, no frame count, no auto-advance.</summary>
    [TestCase]
    public void GotIt_AdvancesThroughTheQueue_AndOnlyAnEmptyQueueCloses()
    {
        var banner = Built();
        try
        {
            banner.ShowFirstTouch("first");
            banner.ShowFirstTouch("second");
            banner.ShowFirstTouch("third");

            banner.Dismiss();
            AssertThat(banner.Visible)
                .OverrideFailureMessage("Closing on the first press would discard the two lessons still waiting.")
                .IsTrue();
            AssertThat(BannerText(banner)).IsEqual("second");

            banner.Dismiss();
            AssertThat(banner.Visible).IsTrue();
            AssertThat(BannerText(banner)).IsEqual("third");

            banner.Dismiss();
            AssertThat(banner.Visible)
                .OverrideFailureMessage("With nothing left to show, the last press closes the banner.")
                .IsFalse();
            AssertThat(banner.PendingLessonCount).IsEqual(0);
        }
        finally { banner.Free(); }
    }

    /// <summary>Preempting reorders; it does not discard. The displaced note has already been
    /// consumed, so it takes the FRONT of the queue and is the next thing "Got it" shows.</summary>
    [TestCase]
    public void Preempting_ShowsTheUrgentOneNow_AndKeepsTheDisplacedNoteNext()
    {
        var banner = Built();
        try
        {
            banner.ShowFirstTouch("the generic orientation note");
            banner.ShowFirstTouch("queued earlier");

            AssertThat(banner.ShowFirstTouch("the act the player just performed", preempt: true)).IsTrue();
            AssertThat(BannerText(banner)).IsEqual("the act the player just performed");

            banner.Dismiss();
            AssertThat(BannerText(banner))
                .OverrideFailureMessage(
                    "The displaced note goes to the FRONT, not the bin and not the back — it fired "
                    + "first and it reads better right after the specific lesson than behind later arrivals.")
                .IsEqual("the generic orientation note");

            banner.Dismiss();
            AssertThat(BannerText(banner)).IsEqual("queued earlier");
        }
        finally { banner.Free(); }
    }

    /// <summary>A null <c>fired</c> is the once-ever engine saying "not this time" and must never
    /// touch the queue or the screen.</summary>
    [TestCase]
    public void ALessonThatDidNotFire_ChangesNothing()
    {
        var banner = Built();
        try
        {
            banner.ShowFirstTouch("first");

            AssertThat(banner.ShowFirstTouch(null)).IsFalse();
            AssertThat(banner.PendingLessonCount).IsEqual(0);
            AssertThat(BannerText(banner)).IsEqual("first");
        }
        finally { banner.Free(); }
    }

    /// <summary>The backlog is capped: a player facing a fifth stacked lesson is being lectured, not
    /// taught, and every one of them is still in the Lessons book. Past the cap, dropping is the
    /// kinder failure — and a run that reaches it means some caller is firing in a batch.</summary>
    [TestCase]
    public void TheBacklogIsCapped_RatherThanGrowingWithoutBound()
    {
        var banner = Built();
        try
        {
            banner.ShowFirstTouch("on screen");
            for (var i = 0; i < 20; i++)
            {
                banner.ShowFirstTouch($"queued {i}");
            }

            AssertThat(banner.PendingLessonCount)
                .OverrideFailureMessage("An unbounded backlog turns teaching into a lecture the player cannot escape.")
                .IsLessEqual(4);
        }
        finally { banner.Free(); }
    }

    /// <summary>
    /// U-T9-0: the measured reason rank exists. Twelve seeds, ten days, <c>BaselinePlayer</c>: day 4
    /// lands FOUR course voices on eight seeds and FIVE on the other four (Act II, the first
    /// attribution beat, the first fulfilled commission, the warrant's dawn, and on a third of seeds
    /// the first hero death). The backlog caps at four. Before rank, a full queue refused whatever
    /// arrived LAST — and the proof is a late-evening beat, so the line most likely to be lost was
    /// the one sentence the course exists to deliver.
    /// </summary>
    [TestCase]
    public void OnAFullNight_TheLessonIsDropped_NeverTheAct()
    {
        var banner = Built();
        try
        {
            banner.ShowFirstTouch("on screen");
            for (var i = 0; i < 4; i++)
            {
                banner.ShowFirstTouch($"tool tip {i}", rank: MentorVoiceRank.Lesson);
            }

            AssertThat(banner.PendingLessonCount)
                .OverrideFailureMessage("Fixture guard: the backlog should be at its cap before the act arrives.")
                .IsEqual(4);

            banner.ShowFirstTouch("the proof fired", rank: MentorVoiceRank.Act);

            AssertThat(banner.PendingLessonCount)
                .OverrideFailureMessage("The cap must hold — an act displaces a lesson, it does not lengthen the queue.")
                .IsEqual(4);

            banner.Dismiss();
            AssertThat(BannerText(banner))
                .OverrideFailureMessage(
                    "The act arrived at a full queue and was refused, so the most important sentence "
                    + "in the game was lost to a tool tip. An act outranks a lesson for the screen.")
                .IsEqual("the proof fired");
        }
        finally { banner.Free(); }
    }

    /// <summary>Rank orders the backlog; it does not reorder within a rank. Two acts on one night
    /// still arrive in the order the sim produced them.</summary>
    [TestCase]
    public void ActsComeFirst_AndKeepTheirOwnOrderAmongThemselves()
    {
        var banner = Built();
        try
        {
            banner.ShowFirstTouch("on screen");
            banner.ShowFirstTouch("a tool explained itself", rank: MentorVoiceRank.Lesson);
            banner.ShowFirstTouch("somebody died", rank: MentorVoiceRank.Act);
            banner.ShowFirstTouch("the proof fired", rank: MentorVoiceRank.Act);

            banner.Dismiss();
            AssertThat(BannerText(banner)).IsEqual("somebody died");
            banner.Dismiss();
            AssertThat(BannerText(banner)).IsEqual("the proof fired");
            banner.Dismiss();
            AssertThat(BannerText(banner))
                .OverrideFailureMessage("The tool tip is last, not lost — the Lessons book is not its only home tonight.")
                .IsEqual("a tool explained itself");
        }
        finally { banner.Free(); }
    }

    /// <summary>A lesson arriving at a queue full of acts yields, rather than evicting one. The drop
    /// rule picks the weakest waiting line; when the arrival IS the weakest, the arrival is it.</summary>
    [TestCase]
    public void ALessonArrivingAtAQueueOfActs_YieldsInsteadOfEvictingOne()
    {
        var banner = Built();
        try
        {
            banner.ShowFirstTouch("on screen");
            for (var i = 0; i < 4; i++)
            {
                banner.ShowFirstTouch($"act {i}", rank: MentorVoiceRank.Act);
            }

            banner.ShowFirstTouch("a tool tip", rank: MentorVoiceRank.Lesson);

            AssertThat(banner.PendingLessonCount).IsEqual(4);
            for (var i = 0; i < 4; i++)
            {
                banner.Dismiss();
                AssertThat(BannerText(banner))
                    .OverrideFailureMessage("A lesson evicted an act. Nothing weaker than the arrival was waiting, so the arrival had to yield.")
                    .IsEqual($"act {i}");
            }
        }
        finally { banner.Free(); }
    }

    /// <summary>Preempting must not demote the line it displaces. An act pushed off the screen by a
    /// more urgent act re-enters as an act, ahead of any lesson already waiting.</summary>
    [TestCase]
    public void APreemptedAct_ReentersAsAnAct_NotAsALesson()
    {
        var banner = Built();
        try
        {
            banner.ShowFirstTouch("somebody died", rank: MentorVoiceRank.Act);
            banner.ShowFirstTouch("a tool explained itself", rank: MentorVoiceRank.Lesson);
            banner.ShowFirstTouch("the proof fired", preempt: true, rank: MentorVoiceRank.Act);

            AssertThat(BannerText(banner)).IsEqual("the proof fired");

            banner.Dismiss();
            AssertThat(BannerText(banner))
                .OverrideFailureMessage(
                    "The displaced act fell behind a tool tip. Being preempted is not a demotion — it "
                    + "re-enters at its own rank.")
                .IsEqual("somebody died");
        }
        finally { banner.Free(); }
    }

    private static string BannerText(MentorBanner banner) =>
        banner.FindChild("MentorBannerText", true, false) is Label label ? label.Text : string.Empty;
}
#endif
