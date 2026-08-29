#if GDUNIT_TESTS
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T2 Wave E (§11.14.4, the long tail): talents and the second profession, the Foundry's four
/// gold-for-certainty verbs, and the read-only surfaces (HeroCards/Depths/Bestiary) — each gets a
/// first-touch lesson through the shared <see cref="MentorBanner"/>/<see
/// cref="TutorialFlow.ConsumeFirstTouch"/> mechanism, same contract every earlier wave used.
/// Reforge (<see cref="GodotClient.Panels.LegendsWall"/>) and quick-travel-unlocked (<see
/// cref="TutorialFlow"/>) live as new [TestCase]s inside their own existing suites instead
/// (<c>LegendsWallTests</c>/<c>TutorialFlowTests</c>) — both already carry a private fixture this
/// unit's tests would otherwise have to duplicate.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WaveELessonsTests
{
    /// <summary>"talents and the second profession" (ForgePanel half): keen-eye has no
    /// prerequisites, so it is unlockable from a fresh save — the same fixture
    /// <c>ForgeCraftTests.TalentUnlock_QueuesUnlockTalentAction</c>-shaped tests already use.
    /// <c>ForgePanel.ShowTalentsLesson</c> routes through the panel's OWN private
    /// <c>ShowMentorFirstTouch</c>/<c>_mentorBanner</c> (the <c>ForgeMentorLessonsTests</c>
    /// precedent) — NOT the shared <c>MainUi.Mentor</c> every other Wave C/D/E lesson uses — so
    /// this asserts against <c>ui.Forge</c>'s own banner controls, not <c>ui.Mentor</c>.</summary>
    [TestCase]
    public void FirstTalentUnlock_TeachesTheTalentLesson()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            PressEnabled(ui.Forge, "Unlock_keen-eye");

            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible)
                .OverrideFailureMessage("The talent lesson never showed on the campaign's first-ever Unlock press.")
                .IsTrue();
            var text = Find<Label>(ui.Forge, "ForgeMentorText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text).Contains("Talent");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>"talents and the second profession" (Progress half): <see
    /// cref="GodotClient.Panels.ProgressionPanel"/>'s own general profession-switch header is a
    /// SECOND path to the same lesson MainUi's tutorial-picker path already teaches
    /// (<c>TutorialFlowTests.SecondProfessionAffordance_...</c>) — shares the same first-touch id,
    /// exercised here through the OTHER call site.</summary>
    [TestCase]
    public void PickingASecondProfessionThroughProgress_TeachesTheSameLesson()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Progress");
            var second = TanningProfession.Id;

            PressEnabled(ui.Progress, $"ProfessionToggle_{second}");
            PressEnabled(ui.Progress, "ConfirmProfessions");

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The second-profession lesson never showed from Progress's own switch header.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text).Contains("second profession");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>"the Foundry's four verbs at affordability": a fresh campaign starts with
    /// <c>GameFactory.StartingPlayerGold</c> = 100, comfortably above coal's 4g unit price, so
    /// "Buy 1" is a real, legal, day-1 press — no fixture beyond a fresh mount needed.
    /// <c>ForgePanel.ShowFoundryVerbsLesson</c> routes through the same private
    /// <c>_mentorBanner</c> as the talent lesson above, not <c>MainUi.Mentor</c>.</summary>
    [TestCase]
    public void FirstFoundryVerbPress_TeachesTheFoundryLesson()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            var buyCoal = Find<Button>(ui.Forge, "BuySupply_coal");
            AssertThat(buyCoal.Disabled)
                .OverrideFailureMessage("Setup check: a fresh campaign cannot afford 1 coal (4g) out of its own starting gold -- this test proves nothing about the Foundry lesson without a legal press.")
                .IsFalse();

            PressEnabled(ui.Forge, "BuySupply_coal");

            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible)
                .OverrideFailureMessage("The Foundry lesson never showed on the campaign's first-ever Foundry-verb press.")
                .IsTrue();
            var text = Find<Label>(ui.Forge, "ForgeMentorText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text).Contains("Foundry");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>"the read-only surfaces": Depths carries no gate (<c>SurfaceUnlocks.GateFor</c>
    /// returns null for it, per <c>MainUi.OpenPanel</c>'s own doc) and no player-submitted action
    /// anywhere on it — a bare open teaches the lesson.</summary>
    [TestCase]
    public void OpeningDepthsForTheFirstTime_TeachesTheReadOnlySurfaceLesson()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Depths");

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The read-only-surface lesson never showed on Depths' first-ever open.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            // U3 (§11.14.14): used to assert "sim" — the line named the engine out loud until this
            // unit's register-check fix made it the town's own record instead (MentorVoiceTests'
            // widened corpus check pins the ban going forward).
            AssertThat(text).Contains("town");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Wave F's own coverage census caught this gap while classifying panels: the
    /// read-only-surfaces lesson's copy already named "Heroes" among the boards it covers, but the
    /// trigger only ever checked <c>id is "HeroCards" or "Depths"</c> — <c>HeroesPanel</c>'s own
    /// drawer id is "Heroes", a DIFFERENT panel from HeroCards' <c>HeroPanel</c>, so the copy's own
    /// promise was never kept for it. Fixed alongside the census, not deferred as a finding.</summary>
    [TestCase]
    public void OpeningHeroesForTheFirstTime_TeachesTheReadOnlySurfaceLesson()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Heroes");

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The read-only-surface lesson never showed on Heroes' first-ever open.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            // U3 (§11.14.14): see the Depths test above for why this is "town", not "sim".
            AssertThat(text).Contains("town");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Same lesson, other door: opening Bestiary first must ALSO teach it, and opening
    /// Depths right after must NOT show it a second time (<see
    /// cref="TutorialFlow.ConsumeFirstTouch"/>'s once-ever contract, shared across both open
    /// paths).</summary>
    [TestCase]
    public void OpeningBestiaryFirst_TeachesTheSameLesson_AndDepthsAfterDoesNotRepeatIt()
    {
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The read-only-surface lesson never showed on Bestiary's first-ever open.")
                .IsTrue();

            ui.Mentor.Dismiss();
            ui.OpenPanel("Depths");

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The once-ever lesson fired a second time from a different read-only surface.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>U26 (§11.14.14, R19, "a player learns where the game publishes what the town
    /// wants"): the FIRST time a hero ever passes on an item (<see cref="HeroPassedOnItem"/>, the
    /// durable fact <see cref="GameSim.Drama.DemandBoard.Snapshot"/> itself rolls up into pass
    /// reasons), Bryn points at the Demand board — nothing here is built, the board already
    /// computes the pass reasons, the depth stalls, and the published bounty floor; nothing pointed
    /// at it before this unit. Any real tick re-checks the durable EventLog fact, so a plain,
    /// side-effect-light Morning buy (the same trigger <c>DilemmaLessonsTests</c>' own fixtures use
    /// to force a tick) is enough — the refusal itself was logged before this test ever mounted.</summary>
    [TestCase]
    public void FirstHeroRefusal_TeachesTheDemandBoardLesson()
    {
        var baseState = GameComposition.NewCampaign(ScriptedSession.Seed);
        var state = baseState with
        {
            EventLog = baseState.EventLog.Add(new HeroPassedOnItem(new HeroId(1), new ItemId(1), "too pricey")),
        };
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The demand-board lesson never showed after the campaign's first-ever refusal.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text).Contains("Demand board");
            AssertThat(ui.Mentor.CurrentAnchor)
                .OverrideFailureMessage("The demand-board lesson does not point at the board's own tray button.")
                .IsEqual(TutorialAnchor.ForHud("OpenDemand"));
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The other half of R19's own test scenario: nothing fires before a refusal exists.</summary>
    [TestCase]
    public void NoRefusalYet_TheDemandBoardLessonStaysSilent()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The demand-board lesson fired with no refusal ever logged.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
