#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U14 (§11.14.14 defect): "a lesson is marked seen before anyone sees it." <see
/// cref="TutorialFlow.ConsumeFirstTouch"/> persists a lesson id as FIRED the instant the game decides
/// it is due — well before <see cref="MentorBanner"/> ever draws it. The banner's own backlog (<see
/// cref="MentorBanner.PendingLessonCount"/>) used to be runtime-only, so quitting with lines still
/// queued (or one still on screen, undismissed) lost them forever while the id stayed marked fired,
/// permanently unable to fire again. This suite pins the fix: <see
/// cref="MentorBanner.SnapshotForPersistence"/>/<see cref="MentorBanner.RestoreFromPersistence"/>,
/// persisted through <see cref="TutorialFlow.PendingMentorLines"/>/<see
/// cref="TutorialFlow.RecordMentorQueue"/>, plus the SAME persistence treatment for the "arrived at
/// this step's anchor" ratchet (<c>TutorialFlow._visitedAnchorForStep</c>), which had the identical
/// runtime-only shape for a smaller-blast-radius reason (a re-armed handoff, not a lost lesson).
///
/// <para><b>"Quit and reload" is modeled the same way <see
/// cref="TutorialFlowTests.Dismiss_MidChain_PersistsAndNeverReprompts_AfterRemount"/> already does</b>:
/// a second (third, fourth...) <see cref="MainUi"/> mounted WITHOUT ever calling <see
/// cref="Unmount"/> on the earlier one first — <see cref="Unmount"/> deletes the very
/// <c>user://tutorial_flow.json</c> file this suite is proving survives a real quit.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialMentorPersistenceTests
{
    private static string BannerText(MainUi ui) =>
        ui.Mentor.FindChild("MentorBannerText", true, false) is Label label ? label.Text : string.Empty;

    /// <summary>The core defect scenario: several beats fired and queued (mixing both ranks, so the
    /// test also proves rank ordering survives the round trip, not just insertion order), then the
    /// game "quits" and reloads. Every one of them must come back, in the exact order <see
    /// cref="MentorBanner.Dismiss"/> would have walked them had the process never died at all.</summary>
    [TestCase]
    public void QuitWithSeveralBeatsQueued_RestoresAllOfThem_InOriginalRankOrder()
    {
        var ui = MountMainUi();
        try
        {
            ui.Mentor.ShowFirstTouch(ui.Tutorial.ConsumeFirstTouch("u14-a", "first"));
            ui.Mentor.ShowFirstTouch(ui.Tutorial.ConsumeFirstTouch("u14-b", "second"));
            ui.Mentor.ShowFirstTouch(ui.Tutorial.ConsumeFirstTouch("u14-c", "an act"), rank: MentorVoiceRank.Act);
            ui.Mentor.ShowFirstTouch(ui.Tutorial.ConsumeFirstTouch("u14-d", "fourth"));

            AssertThat(BannerText(ui)).IsEqual("first");
            AssertThat(ui.Mentor.PendingLessonCount)
                .OverrideFailureMessage("Fixture guard: three lines should be queued behind the first.")
                .IsEqual(3);

            var ui2 = MountMainUi(); // "quit and relaunch" — no Unmount(ui) first
            try
            {
                AssertThat(BannerText(ui2))
                    .OverrideFailureMessage("The line on screen at quit time was not restored.")
                    .IsEqual("first");
                AssertThat(ui2.Mentor.PendingLessonCount).IsEqual(3);

                ui2.Mentor.Dismiss();
                AssertThat(BannerText(ui2))
                    .OverrideFailureMessage("An Act-ranked beat must outrank a Lesson for the screen, even across a reload.")
                    .IsEqual("an act");

                ui2.Mentor.Dismiss();
                AssertThat(BannerText(ui2)).IsEqual("second");

                ui2.Mentor.Dismiss();
                AssertThat(BannerText(ui2)).IsEqual("fourth");

                ui2.Mentor.Dismiss();
                AssertThat(ui2.Mentor.Visible).IsFalse();
            }
            finally
            {
                Unmount(ui2);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The anti-nag pin (class doc) — the repo's own scar tissue, a 1287x memorial re-fire.
    /// A beat the player actually read and dismissed must never come back, proven across THREE
    /// separate quit/reload cycles, not just one.</summary>
    [TestCase]
    public void BeatShownAndDismissed_NeverReappears_AcrossThreeQuitReloadCycles()
    {
        var ui1 = MountMainUi();
        try
        {
            ui1.Mentor.ShowFirstTouch(ui1.Tutorial.ConsumeFirstTouch("u14-once", "shown once"));
            AssertThat(BannerText(ui1)).IsEqual("shown once");
            ui1.Mentor.Dismiss(); // the player read it and pressed "Got it" for real
            AssertThat(ui1.Mentor.Visible).IsFalse();

            var ui2 = MountMainUi(); // reload #1
            try
            {
                AssertThat(ui2.Mentor.Visible).IsFalse();
                AssertThat(ui2.Mentor.PendingLessonCount).IsEqual(0);

                var ui3 = MountMainUi(); // reload #2
                try
                {
                    AssertThat(ui3.Mentor.Visible).IsFalse();
                    AssertThat(ui3.Mentor.PendingLessonCount).IsEqual(0);

                    var ui4 = MountMainUi(); // reload #3
                    try
                    {
                        AssertThat(ui4.Mentor.Visible)
                            .OverrideFailureMessage("A dismissed beat came back from the dead on a third reload.")
                            .IsFalse();
                        AssertThat(ui4.Mentor.PendingLessonCount).IsEqual(0);
                        AssertThat(ui4.Tutorial.ConsumeFirstTouch("u14-once", "shown once"))
                            .OverrideFailureMessage("The once-ever engine itself must still refuse this id.")
                            .IsNull();
                    }
                    finally
                    {
                        Unmount(ui4);
                    }
                }
                finally
                {
                    Unmount(ui3);
                }
            }
            finally
            {
                Unmount(ui2);
            }
        }
        finally
        {
            Unmount(ui1);
        }
    }

    /// <summary>The "arrived at this step's anchor" ratchet (<see
    /// cref="TutorialFlow.NotifyEnteredBuilding"/>/<see cref="TutorialFlow.VisitedCurrentAnchor"/>)
    /// used to live only in a runtime field — a reload mid-step reset it to false and re-armed a
    /// handoff (the checklist's own "✓ Arrived" sub-tick) the player had already completed.</summary>
    [TestCase]
    public void ArrivedRatchet_SurvivesAReload_SoACompletedHandoffIsNotReArmed()
    {
        var ui = MountMainUi();
        try
        {
            // Fresh mount: Step is BuyMaterial, whose Station anchor lives in "forge" (registry).
            AssertThat(ui.Tutorial.VisitedCurrentAnchor).IsFalse();
            ui.Tutorial.NotifyEnteredBuilding("forge");
            AssertThat(ui.Tutorial.VisitedCurrentAnchor).IsTrue();

            var ui2 = MountMainUi(); // "quit and relaunch"
            try
            {
                AssertThat(ui2.Tutorial.Step)
                    .OverrideFailureMessage("Fixture guard: Step must still be BuyMaterial for VisitedCurrentAnchor to mean the same thing on both sides.")
                    .IsEqual(ui.Tutorial.Step);
                AssertThat(ui2.Tutorial.VisitedCurrentAnchor)
                    .OverrideFailureMessage("A reload re-armed a handoff the player had already completed.")
                    .IsTrue();
            }
            finally
            {
                Unmount(ui2);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Root-cause precedent (<see cref="TutorialFlow.ResetForNewGame"/>'s own doc): a
    /// genuinely NEW campaign must not inherit ANY of a previous campaign's fired-but-unshown state —
    /// the fired ids, the queued/on-screen banner lines, and the arrived ratchet all have to clear
    /// together, not just the Completed/Dismissed/Step flags this method already cleared before U14.</summary>
    [TestCase]
    public void NewGame_ClearsFired_Shown_Queued_AndTheRatchet_Together()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tutorial.NotifyEnteredBuilding("forge");
            ui.Mentor.ShowFirstTouch(ui.Tutorial.ConsumeFirstTouch("u14-newgame-a", "on screen"));
            ui.Mentor.ShowFirstTouch(ui.Tutorial.ConsumeFirstTouch("u14-newgame-b", "queued"));

            AssertThat(ui.Tutorial.VisitedCurrentAnchor).IsTrue();
            AssertThat(ui.Tutorial.FirstTouch.HasFired("u14-newgame-a")).IsTrue();
            AssertThat(ui.Mentor.PendingLessonCount)
                .OverrideFailureMessage("Fixture guard: one line should be queued behind the first.")
                .IsEqual(1);

            // The exact call NewGameSelect.OnBeginPressed makes before building a fresh campaign.
            TutorialFlow.ResetForNewGame();

            var ui2 = MountMainUi(); // "New Game" == a fresh instance loading the now-cleared file
            try
            {
                AssertThat(ui2.Tutorial.FirstTouch.HasFired("u14-newgame-a")).IsFalse();
                AssertThat(ui2.Tutorial.FirstTouch.HasFired("u14-newgame-b")).IsFalse();
                AssertThat(ui2.Mentor.Visible).IsFalse();
                AssertThat(ui2.Mentor.PendingLessonCount).IsEqual(0);
                AssertThat(ui2.Tutorial.VisitedCurrentAnchor)
                    .OverrideFailureMessage("New Game inherited a PRIOR campaign's arrived ratchet.")
                    .IsFalse();
            }
            finally
            {
                Unmount(ui2);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The other half of the defect: a beat that was ON SCREEN — fired, shown once, but
    /// never dismissed — when the process exited. It must come back (not lost), exactly once (not
    /// duplicated into the backlog behind itself).</summary>
    [TestCase]
    public void BeatMidDisplayWhenGameQuit_IsRestoredAsUnshown_NotLostAndNotDuplicated()
    {
        var ui = MountMainUi();
        try
        {
            ui.Mentor.ShowFirstTouch(ui.Tutorial.ConsumeFirstTouch("u14-middisplay", "still reading this"));
            AssertThat(ui.Mentor.Visible).IsTrue();
            AssertThat(BannerText(ui)).IsEqual("still reading this");
            // No Dismiss() — the player never pressed "Got it" before the process exited.

            var ui2 = MountMainUi();
            try
            {
                AssertThat(ui2.Mentor.Visible)
                    .OverrideFailureMessage("A beat that was mid-display when the game quit was lost on reload.")
                    .IsTrue();
                AssertThat(BannerText(ui2)).IsEqual("still reading this");
                AssertThat(ui2.Mentor.PendingLessonCount)
                    .OverrideFailureMessage("The restored line must not ALSO sit in the backlog behind itself.")
                    .IsEqual(0);

                ui2.Mentor.Dismiss();
                AssertThat(ui2.Mentor.Visible)
                    .OverrideFailureMessage("Dismissing the restored line should close the banner outright — nothing was duplicated behind it.")
                    .IsFalse();
            }
            finally
            {
                Unmount(ui2);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
