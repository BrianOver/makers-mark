#if GDUNIT_TESTS
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U16 (§11.14.14, "the first thing any player ever reads"): pins the first-morning cold-open beat
/// — Bryn's statement of law 1's negative half (the player is the smith, never descends, and no
/// hero here ever takes an order from them) — end to end through the REAL front door, the same
/// idiom <see cref="ReturningSmithTests"/> already established for <see
/// cref="NewGameSelect.OnBeginPressed"/>-driven scenarios.
///
/// <para><b>Why a bare mount must never show it (the design's own load-bearing fact):</b> nothing
/// in live <see cref="GameSim.Contracts.GameState"/> distinguishes a freshly-Begun campaign from
/// <c>UiTestSupport.MountMainUi()</c>'s own "fresh seed-2026 campaign" default — both are Day
/// 1, Morning, <see cref="TutorialStep.BuyMaterial"/>, empty <see cref="TutorialFlow.FirstTouch"/>.
/// Only <see cref="NewGameSelect.OnBeginPressed"/> knows which one just happened, which is why the
/// beat is gated on <see cref="MainUi.FirstMorningBeatPending"/> rather than on anything read off
/// state — <see cref="BareMount_NeverShowsTheBeat"/> is the test that would fail loudly the day that
/// gate stops working and every OTHER suite's own Mentor/FirstTouch assertions start silently
/// drifting instead.</para>
///
/// <para><b>Nesting note (every scenario below):</b> <c>MountMainUi</c> must be called BEFORE
/// <c>UnmountNewGameSelect</c> tears the front-door screen down — that teardown clears <see
/// cref="MainUi.FirstMorningBeatPending"/> as its own leak guard (the same guard <see
/// cref="NewGameSelectTests"/>/<see cref="ReturningSmithTests"/> now carry), so mounting MainUi
/// AFTER it — the way <see cref="ReturningSmithTests.ReturningSmithChoice_SurvivesAReload"/> mounts
/// its own <c>ui1</c> — would find the flag already cleared and never see the beat at all.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class FirstMorningBeatTests
{
    private static NewGameSelect MountNewGameSelect()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        var screen = GD.Load<PackedScene>("res://scenes/new_game_select.tscn").Instantiate<NewGameSelect>();
        tree.Root.AddChild(screen);
        return screen;
    }

    private static void UnmountNewGameSelect(NewGameSelect screen)
    {
        MainUi.AdapterOverride = null; // never leak a picked campaign into a later suite
        MainUi.FirstMorningBeatPending = false; // same leak guard NewGameSelectTests/ReturningSmithTests carry
        screen.GetParent()?.RemoveChild(screen);
        screen.Free();
    }

    /// <summary>Seeds a "prior campaign already fired lesson X" fact directly into the shared save
    /// file — the exact idiom <see cref="ReturningSmithTests"/> uses to make <see
    /// cref="TutorialFlow.HasPriorProgress"/> read true for the next screen, reused here to control
    /// whether the cold-open's OWN id is or is not already in that prior campaign's fired set.</summary>
    private static void SeedFiredLesson(string id, string text)
    {
        var stale = new TutorialFlow();
        stale.Build();
        try
        {
            stale.ConsumeFirstTouch(id, text);
        }
        finally
        {
            stale.Free();
        }
    }

    private static string BannerText(MainUi ui) =>
        ui.Mentor.FindChild("MentorBannerText", true, false) is Label label ? label.Text : string.Empty;

    private static readonly string ExpectedBanner = MentorVoice.Speak(TutorialFlow.FirstMorningBeatText);

    [TestCase]
    public void FreshBegin_ShowsTheBeat_BeforeAnyNumberedStepIsTaken()
    {
        TutorialFlow.DeleteForTests(); // guarantee no prior-campaign file leaked in from another suite

        var screen = MountNewGameSelect();
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");
            Press(screen, "Begin");

            var adapter = MainUi.AdapterOverride;
            AssertThat(adapter).IsNotNull();

            var ui = MountMainUi(adapter);
            try
            {
                // Still exactly step 1, untouched — the beat arrived on top of it, not after it.
                AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);

                AssertThat(ui.Mentor.Visible)
                    .OverrideFailureMessage("A genuine new-game Begin press must show the cold-open beat immediately.")
                    .IsTrue();
                AssertThat(BannerText(ui)).IsEqual(ExpectedBanner);
                AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.FirstMorningBeatId)).IsTrue();
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            UnmountNewGameSelect(screen);
        }
    }

    /// <summary>The load-bearing negative — see class doc. If this ever starts failing, the gate
    /// moved from "front door only" to "every fresh mount", and every OTHER suite's Mentor/FirstTouch
    /// assertions are the ones that will pay for it, silently, one flaky count at a time.</summary>
    [TestCase]
    public void BareMount_NeverShowsTheBeat()
    {
        var ui = MountMainUi(); // no NewGameSelect, no Begin — exactly what most OTHER suites do
        try
        {
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("A bare test mount (never touched Begin) must never show the cold-open beat.")
                .IsFalse();
            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.FirstMorningBeatId)).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void UndismissedBeat_SurvivesAReload_UnchangedAndNotDuplicated()
    {
        TutorialFlow.DeleteForTests();

        var screen = MountNewGameSelect();
        screen.SceneChange = _ => { };
        MainUi? ui1 = null;
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");
            Press(screen, "Begin");

            var adapter = MainUi.AdapterOverride;
            ui1 = MountMainUi(adapter); // MUST happen before UnmountNewGameSelect — see class doc
            AssertThat(ui1.Mentor.Visible).IsTrue();
            // No Dismiss() — the player never pressed "Got it" before the process exited.
        }
        finally
        {
            UnmountNewGameSelect(screen);
        }

        try
        {
            // "Quit and relaunch" — no Unmount(ui1) first (TutorialMentorPersistenceTests precedent):
            // Unmount deletes the very file this test is proving survives a real reload.
            var ui2 = MountMainUi();
            try
            {
                AssertThat(ui2.Mentor.Visible)
                    .OverrideFailureMessage("The undismissed cold-open beat did not survive a reload.")
                    .IsTrue();
                AssertThat(BannerText(ui2)).IsEqual(ExpectedBanner);
                AssertThat(ui2.Mentor.PendingLessonCount)
                    .OverrideFailureMessage("The restored line must not ALSO sit in the backlog behind itself.")
                    .IsEqual(0);
            }
            finally
            {
                Unmount(ui2);
            }
        }
        finally
        {
            Unmount(ui1!);
        }
    }

    [TestCase]
    public void DismissedBeat_NeverReappears_OnReload()
    {
        TutorialFlow.DeleteForTests();

        var screen = MountNewGameSelect();
        screen.SceneChange = _ => { };
        MainUi? ui1 = null;
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");
            Press(screen, "Begin");

            var adapter = MainUi.AdapterOverride;
            ui1 = MountMainUi(adapter); // MUST happen before UnmountNewGameSelect — see class doc
            AssertThat(ui1.Mentor.Visible).IsTrue();
            ui1.Mentor.Dismiss(); // the player read it and pressed "Got it" for real
        }
        finally
        {
            UnmountNewGameSelect(screen);
        }

        try
        {
            var ui2 = MountMainUi();
            try
            {
                AssertThat(ui2.Mentor.Visible)
                    .OverrideFailureMessage("A dismissed cold-open beat came back from the dead on reload.")
                    .IsFalse();
                AssertThat(ui2.Tutorial.ConsumeFirstTouch(TutorialFlow.FirstMorningBeatId, TutorialFlow.FirstMorningBeatText))
                    .OverrideFailureMessage("The once-ever engine itself must still refuse this id.")
                    .IsNull();
            }
            finally
            {
                Unmount(ui2);
            }
        }
        finally
        {
            Unmount(ui1!);
        }
    }

    /// <summary>Returning-smith decision (U16): the beat is independent of Active/Dismissed, same
    /// precedent as <see cref="TutorialFlow.ConsumeLedgerTip"/> — a veteran who has genuinely never
    /// heard it (here: an old save that fired some OTHER lesson but never this one) still hears it
    /// once, same as anyone else. HasPriorProgress only needs SOME fired lesson on file; it does not
    /// need to be this one.</summary>
    [TestCase]
    public void ReturningSmith_NeverSeenTheBeatBefore_StillGetsItOnce()
    {
        SeedFiredLesson("u16-unrelated-prior-lesson", "seen before");

        var screen = MountNewGameSelect();
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");
            AssertThat(Find<VBoxContainer>(screen, "ReturningSmithChoice").Visible).IsTrue();
            Press(screen, "SkipCourse");
            Press(screen, "Begin");

            var adapter = MainUi.AdapterOverride;
            var ui = MountMainUi(adapter); // MUST happen before UnmountNewGameSelect — see class doc
            try
            {
                AssertThat(ui.Tutorial.Dismissed)
                    .OverrideFailureMessage("Fixture guard: the returning-smith Skip path must decline the numbered chain.")
                    .IsTrue();
                AssertThat(ui.Mentor.Visible)
                    .OverrideFailureMessage("A returning smith who never saw the cold-open beat before must still get it once.")
                    .IsTrue();
                AssertThat(BannerText(ui)).IsEqual(ExpectedBanner);
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            UnmountNewGameSelect(screen);
        }
    }

    /// <summary>The other half of the same decision: a returning smith whose PRIOR campaign already
    /// showed this exact beat must not hear it a second time — <see
    /// cref="TutorialFlow.ResetForReturningSmith"/>'s own "fired ids survive" contract, applied to
    /// this id like any other.</summary>
    [TestCase]
    public void ReturningSmith_AlreadySawTheBeatBefore_NeverReshownOnTheNewCampaign()
    {
        SeedFiredLesson(TutorialFlow.FirstMorningBeatId, MentorVoice.Speak(TutorialFlow.FirstMorningBeatText));

        var screen = MountNewGameSelect();
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");
            Press(screen, "SkipCourse");
            Press(screen, "Begin");

            var adapter = MainUi.AdapterOverride;
            var ui = MountMainUi(adapter); // MUST happen before UnmountNewGameSelect — see class doc
            try
            {
                AssertThat(ui.Mentor.Visible)
                    .OverrideFailureMessage("A returning smith who already saw the cold-open beat must not see it again.")
                    .IsFalse();
            }
            finally
            {
                Unmount(ui);
            }
        }
        finally
        {
            UnmountNewGameSelect(screen);
        }
    }
}
#endif
