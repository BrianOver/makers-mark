#if GDUNIT_TESTS
using GameSim.Expedition;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U17 (§11.14.14 defect): "starting a new campaign re-fires every once-ever lesson and re-runs all
/// ten numbered steps from scratch, and the only way out (the ✕ dismiss confirm) forfeits the
/// apprenticeship warrant — copy written for a first-timer, the wrong cost for a veteran's
/// preference." This suite pins the returning-smith choice <see cref="NewGameSelect"/> now offers at
/// New-Game time: <see cref="TutorialFlow.ResetForNewGame"/> ("run the course") stays byte-for-byte
/// what it was — a genuinely first-time player is unaffected by this unit's existence — and <see
/// cref="TutorialFlow.ResetForReturningSmith"/> ("skip it") is the new second path: the numbered
/// chain never mounts, fired once-ever lesson ids survive into the new campaign, the previous
/// campaign's own pending mentor-banner backlog does NOT, and — the whole reason this is not simply
/// <see cref="TutorialFlow.Dismiss"/> under a new name — the apprenticeship warrant (<see
/// cref="ApprenticeWarrant.Covers"/>) stays whole, because nothing on this path ever submits
/// <see cref="GameSim.Contracts.ConcludeApprenticeshipAction"/>.
///
/// <para>Follows <c>TutorialFlowTests</c>' own precedent for seeding a PRIOR campaign's save file: a
/// bare, untethered <see cref="TutorialFlow"/> instance (<c>Build()</c> then the call that matters,
/// then <c>Free()</c>) writes to the SAME <c>user://tutorial_flow.json</c> real play uses, without
/// needing a full <see cref="MainUi"/> mount for "campaign one".</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ReturningSmithTests
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
        // U16: defensive, same reasoning as NewGameSelectTests' own Unmount — every test in THIS
        // file happens to consume it via MountMainUi right after Begin, but a leak guard should not
        // depend on that staying true forever.
        MainUi.FirstMorningBeatPending = false;
        screen.GetParent()?.RemoveChild(screen);
        screen.Free();
    }

    /// <summary>Seeds a "prior campaign" fact directly into the shared save file — mirrors
    /// <c>TutorialFlowTests</c>' own "stale" idiom (used there to Dismiss), used here to fire a
    /// lesson so <see cref="TutorialFlow.HasPriorProgress"/> reads true for the next screen.</summary>
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

    [TestCase]
    public void FirstTimePlayer_NoPriorSave_ChoiceNeverShown_AndBeginUnaffected()
    {
        TutorialFlow.DeleteForTests(); // guarantee no prior-campaign file leaked in from another suite

        var screen = MountNewGameSelect();
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");

            AssertThat(Find<VBoxContainer>(screen, "ReturningSmithChoice").Visible)
                .OverrideFailureMessage("A true first-timer must never see the returning-smith choice.")
                .IsFalse();

            Press(screen, "Begin");

            var adapter = MainUi.AdapterOverride;
            AssertThat(adapter).IsNotNull();

            var ui = MountMainUi(adapter);
            try
            {
                AssertThat(ui.Tutorial.Dismissed).IsFalse();
                AssertThat(ui.Tutorial.Active).IsTrue();
                AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);
                // U16: a real Begin press now fires exactly one thing unconditionally — Bryn's
                // cold-open beat (see TutorialFlow.FirstMorningBeatText's own doc) — before this
                // assertion even runs, so "unaffected by U17's own mechanism" is now pinned as
                // "exactly the cold-open and nothing else", not "nothing at all".
                AssertThat(ui.Tutorial.FirstTouch.Fired.Count).IsEqual(1);
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

    [TestCase]
    public void ReturningSmith_SkipsCourse_KeepsWarrant_AndGetsNoNumberedChain()
    {
        SeedFiredLesson("u17-warrant-check", "seen before");

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
            AssertThat(adapter).IsNotNull();

            AssertThat(ApprenticeWarrant.Concluded(adapter!.CurrentState))
                .OverrideFailureMessage("The returning-smith Skip path must never submit ConcludeApprenticeshipAction.")
                .IsFalse();
            AssertThat(ApprenticeWarrant.Covers(adapter.CurrentState))
                .OverrideFailureMessage("Skipping the course must not forfeit the apprenticeship warrant.")
                .IsTrue();

            var ui = MountMainUi(adapter);
            try
            {
                AssertThat(ui.Tutorial.Dismissed).IsTrue();
                AssertThat(ui.Tutorial.Active)
                    .OverrideFailureMessage("A returning smith who chose Skip must get no numbered chain.")
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

    [TestCase]
    public void ReturningSmith_SkipsCourse_CarriesFiredLessonsForward_AndNeverRefiresThem()
    {
        SeedFiredLesson("u17-once-seen", "already taught");

        var screen = MountNewGameSelect();
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");
            Press(screen, "SkipCourse");
            Press(screen, "Begin");

            var adapter = MainUi.AdapterOverride;
            var ui = MountMainUi(adapter);
            try
            {
                AssertThat(ui.Tutorial.FirstTouch.HasFired("u17-once-seen")).IsTrue();
                AssertThat(ui.Tutorial.ConsumeFirstTouch("u17-once-seen", "already taught"))
                    .OverrideFailureMessage("Campaign two on the returning path must not re-fire a lesson already seen.")
                    .IsNull();
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

    [TestCase]
    public void ReturningSmith_RunsCourseAnyway_ClearsFiredLessons_SameAsAFirstTimer()
    {
        SeedFiredLesson("u17-run-clears", "should not survive");

        var screen = MountNewGameSelect();
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");
            // "RunCourse" is never pressed — proves the default itself is "run the course", not just
            // a case the toggle can reach.
            Press(screen, "Begin");

            var adapter = MainUi.AdapterOverride;
            var ui = MountMainUi(adapter);
            try
            {
                AssertThat(ui.Tutorial.Dismissed).IsFalse();
                AssertThat(ui.Tutorial.Active).IsTrue();
                AssertThat(ui.Tutorial.FirstTouch.HasFired("u17-run-clears"))
                    .OverrideFailureMessage(
                        "Choosing \"run the course\" must clear fired lessons, same as any first-timer's reset.")
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

    [TestCase]
    public void ReturningSmithChoice_SurvivesAReload()
    {
        SeedFiredLesson("u17-reload", "seed");

        var screen = MountNewGameSelect();
        screen.SceneChange = _ => { };
        try
        {
            Press(screen, "NewGame");
            Press(screen, "Pick_blacksmith");
            Press(screen, "SkipCourse");
            Press(screen, "Begin");
        }
        finally
        {
            UnmountNewGameSelect(screen);
        }

        var ui1 = MountMainUi();
        try
        {
            AssertThat(ui1.Tutorial.Dismissed).IsTrue();

            // "Quit and relaunch" — no Unmount(ui1) first (TutorialMentorPersistenceTests precedent):
            // Unmount deletes the very file this test is proving survives a real reload.
            var ui2 = MountMainUi();
            try
            {
                AssertThat(ui2.Tutorial.Dismissed)
                    .OverrideFailureMessage("The returning-smith choice did not survive a reload.")
                    .IsTrue();
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

    [TestCase]
    public void ReturningSmith_DoesNotInheritThePreviousCampaignsPendingBannerQueue()
    {
        var ui1 = MountMainUi();
        try
        {
            ui1.Mentor.ShowFirstTouch(ui1.Tutorial.ConsumeFirstTouch("u17-banner-a", "on screen"));
            ui1.Mentor.ShowFirstTouch(ui1.Tutorial.ConsumeFirstTouch("u17-banner-b", "queued behind it"));
            AssertThat(ui1.Mentor.Visible)
                .OverrideFailureMessage("Fixture guard: a line must be on screen for this scenario to mean anything.")
                .IsTrue();
            AssertThat(ui1.Mentor.PendingLessonCount)
                .OverrideFailureMessage("Fixture guard: one line should be queued behind the first.")
                .IsEqual(1);

            // The exact call NewGameSelect.OnBeginPressed makes when the returning-smith choice is
            // Skip — called directly, mid-test, the same way TutorialMentorPersistenceTests calls
            // ResetForNewGame() directly rather than driving the real front door for this half.
            TutorialFlow.ResetForReturningSmith();

            var ui2 = MountMainUi(); // "New Game, Skip" == a fresh instance loading the now-reset file
            try
            {
                AssertThat(ui2.Mentor.Visible)
                    .OverrideFailureMessage("A returning player inherited the previous campaign's on-screen banner line.")
                    .IsFalse();
                AssertThat(ui2.Mentor.PendingLessonCount)
                    .OverrideFailureMessage("A returning player inherited the previous campaign's pending banner queue.")
                    .IsEqual(0);

                // Carried forward regardless — ties this scenario back to the "fired ids survive" rule
                // so a reader can see both halves of ResetForReturningSmith's contract in one place.
                AssertThat(ui2.Tutorial.FirstTouch.HasFired("u17-banner-a")).IsTrue();
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
}
#endif
