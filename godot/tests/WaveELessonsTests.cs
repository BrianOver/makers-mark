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

    /// <summary>
    /// #736 (the door #735 found and booked rather than built): before this fix,
    /// <c>ForgePanel.ShowTalentsLesson</c> fired ONLY from <c>OnUnlockPressed</c> — the success
    /// path — so the one lesson naming the Forge Tier requirement could only ever arrive after the
    /// player had already satisfied it. <c>tier-2-smithing</c> has no prerequisite (see <see
    /// cref="GameSim.Crafting.TalentTree"/>) but DOES carry a Forge Tier requirement (index 1, "Tier
    /// 2"), and a fresh campaign's workshop starts at Tier 1 — so this button is REFUSED on a fresh
    /// mount for exactly the Forge-Tier reason the bug names, never a missing-prerequisite one.
    /// </summary>
    [TestCase]
    public void RefusedUnlockPress_TeachesTheSameTalentLesson_AtTheWallInsteadOfAfterIt()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            var button = Find<Button>(ui.Forge, "Unlock_tier-2-smithing");
            AssertThat(button.Disabled)
                .OverrideFailureMessage("Setup check: a refused Unlock stays pressable (onRefused keeps it enabled) — Disabled=true means this test proves nothing about a real player's press.")
                .IsFalse();
            AssertThat(button.Text)
                .OverrideFailureMessage($"Setup check: expected the Forge-Tier refusal reason on the button label, got \"{button.Text}\".")
                .Contains("Forge Tier");

            Press(ui.Forge, "Unlock_tier-2-smithing");

            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible)
                .OverrideFailureMessage("The talent lesson never showed on a REFUSED Unlock press — the exact gap #736 exists to close.")
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

    /// <summary>Law: skipping stays legal, and a refused press must never itself be swallowed — the
    /// button's own <c>onRefused</c> callback still reports the verdict's reason via
    /// <c>ForgePanel.SetFeedback</c> exactly as before, AND still queues no
    /// <see cref="UnlockTalentAction"/>, whether or not the lesson banner also has something to
    /// say. Same fixture as <see cref="RefusedUnlockPress_TeachesTheSameTalentLesson_AtTheWallInsteadOfAfterIt"/>.</summary>
    [TestCase]
    public void RefusedUnlockPress_StillReportsItsOwnRefusalReason_AndQueuesNothing()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            Press(ui.Forge, "Unlock_tier-2-smithing");

            var feedback = Find<Label>(ui.Forge, "ForgeFeedback");
            AssertThat(feedback.Visible)
                .OverrideFailureMessage("A refused press must still report its own reason (law: skipping stays legal) -- the lesson must never swallow the refusal.")
                .IsTrue();
            AssertThat(feedback.Text).Contains("Forge Tier");

            AssertThat(ui.Adapter.AppliedThisPhase.OfType<UnlockTalentAction>()
                .Any(a => a.NodeId == "tier-2-smithing"))
                .OverrideFailureMessage("A refused Unlock press queued UnlockTalentAction anyway -- the lesson must never itself apply the gated action.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Anti-repeat rule: <see cref="TutorialFlow.ConsumeFirstTouch"/>'s once-ever contract
    /// (the SAME mechanism every other first-touch lesson in this file relies on, not a bespoke
    /// second one) — a player may hit a refused button many times (this repo has shipped and later
    /// killed a lesson that fired 1,287 times); the SECOND refused press, after the banner from the
    /// first has already been dismissed, must show nothing, while the refusal's own reason keeps
    /// reporting every time (never gated behind the lesson).</summary>
    [TestCase]
    public void RepeatedRefusedPresses_NeverRepeatTheLesson_ButKeepReportingTheRefusal()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            Press(ui.Forge, "Unlock_tier-2-smithing");
            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible)
                .OverrideFailureMessage("Setup check: the first refused press should teach the lesson.")
                .IsTrue();
            PressEnabled(ui.Forge, "ForgeMentorDismiss");

            Press(ui.Forge, "Unlock_tier-2-smithing");
            Press(ui.Forge, "Unlock_tier-2-smithing");

            AssertThat(Find<PanelContainer>(ui.Forge, "ForgeMentorBanner").Visible)
                .OverrideFailureMessage("The once-ever talent lesson fired again on a later refused press.")
                .IsFalse();
            var feedback = Find<Label>(ui.Forge, "ForgeFeedback");
            AssertThat(feedback.Visible)
                .OverrideFailureMessage("The refusal reason stopped reporting once the lesson had already fired once -- these must stay independent.")
                .IsTrue();
            AssertThat(feedback.Text).Contains("Forge Tier");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The lesson is a READ of <see cref="TutorialFlow"/>'s own first-touch bookkeeping —
    /// showing it on a refused press must never itself write sim state (whole-state fingerprint,
    /// same idiom <c>ForgeBatchEchoTests.RenderingTheEchoChip_WritesNoSimState</c> already uses —
    /// never a hand-listed field set that could silently miss a mutation elsewhere in the tree).</summary>
    [TestCase]
    public void RefusedUnlockPress_WritesNoSimState()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Forge");

            var before = GameSim.Kernel.SaveCodec.Serialize(ui.Adapter.CurrentState);

            Press(ui.Forge, "Unlock_tier-2-smithing");

            var after = GameSim.Kernel.SaveCodec.Serialize(ui.Adapter.CurrentState);
            AssertThat(after).IsEqual(before);
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
    /// anywhere on it — a bare open teaches the lesson.
    ///
    /// <para>P2-ONBOARD-02 (§11.15): no longer a floating <see cref="GodotClient.Ui.MentorBanner"/>
    /// popup — a rendered pass found Bryn's banner covering nearly every first-opened panel, and
    /// this was one of four lessons firing on OPEN into that centred card. It now renders as this
    /// panel's own once-ever header caption.</para>
    /// </summary>
    [TestCase]
    public void OpeningDepthsForTheFirstTime_TeachesTheReadOnlySurfaceLesson()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Depths");

            var caption = Find<Label>(ui.Depths, "OnceEverCaption");
            AssertThat(caption.Visible)
                .OverrideFailureMessage("The read-only-surface lesson never showed on Depths' first-ever open.")
                .IsTrue();
            // U3 (§11.14.14): used to assert "sim" — the line named the engine out loud until this
            // unit's register-check fix made it the town's own record instead (MentorVoiceTests'
            // widened corpus check pins the ban going forward).
            AssertThat(caption.Text).Contains("town");
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The read-only-surface lesson must render as its own caption, never the floating banner.")
                .IsFalse();
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
    /// promise was never kept for it. Fixed alongside the census, not deferred as a finding.
    ///
    /// <para>P2-ONBOARD-02 (§11.15): see <see
    /// cref="OpeningDepthsForTheFirstTime_TeachesTheReadOnlySurfaceLesson"/>'s own note — the same
    /// conversion, on the roster's own caption instead of Depths'.</para>
    /// </summary>
    [TestCase]
    public void OpeningHeroesForTheFirstTime_TeachesTheReadOnlySurfaceLesson()
    {
        var ui = MountMainUi();
        try
        {
            ui.OpenPanel("Heroes");

            var caption = Find<Label>(ui.Heroes, "OnceEverCaption");
            AssertThat(caption.Visible)
                .OverrideFailureMessage("The read-only-surface lesson never showed on Heroes' first-ever open.")
                .IsTrue();
            // U3 (§11.14.14): see the Depths test above for why this is "town", not "sim".
            AssertThat(caption.Text).Contains("town");
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The read-only-surface lesson must render as its own caption, never the floating banner.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Same lesson, other door: opening Bestiary first must ALSO teach it, and opening
    /// Depths right after must NOT show it a second time (<see
    /// cref="TutorialFlow.ConsumeFirstTouch"/>'s once-ever contract, shared across both open
    /// paths) — now checked on each panel's OWN caption rather than a shared banner, since P2-
    /// ONBOARD-02 gives every one of the four read-only surfaces its own stable caption label.</summary>
    [TestCase]
    public void OpeningBestiaryFirst_TeachesTheSameLesson_AndDepthsAfterDoesNotRepeatIt()
    {
        var ui = MountMainUi();
        try
        {
            ui.Bestiary.ShowAll();
            AssertThat(Find<Label>(ui.Bestiary, "OnceEverCaption").Visible)
                .OverrideFailureMessage("The read-only-surface lesson never showed on Bestiary's first-ever open.")
                .IsTrue();

            ui.OpenPanel("Depths");

            AssertThat(Find<Label>(ui.Depths, "OnceEverCaption").Visible)
                .OverrideFailureMessage("The once-ever lesson fired a second time from a different read-only surface.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>U26 (§11.14.14, R19, "a player learns where the game publishes what the town
    /// wants"): the campaign's first <see cref="HeroPassedOnItem"/> ARMS <see
    /// cref="TutorialFlow.ConsumeDemandBoardBeat"/> but must never SPEAK the same day — see that
    /// method's own doc for why an immediate fire would hijack the tutorial's own pulse mid-Morning.
    /// Driven through the real Mentor banner (a live tick), because this half is exactly the
    /// regression this unit's own call-site doc names.</summary>
    [TestCase]
    public void FirstHeroRefusal_ArmsTheDemandBoardBeat_ButNeverSpeaksTheSameDay()
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
                .OverrideFailureMessage("The demand-board beat spoke the SAME day the refusal landed.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The morning-after half: once armed, the very next day speaks — and names the board
    /// correctly. Calls <see cref="TutorialFlow.ConsumeDemandBoardBeat"/> directly against a
    /// Day-advanced projection, the same "hand a modified state to a pure method" idiom
    /// <c>TutorialFlowTests</c>' own day-2/3 helpers already use, rather than simulating a full real
    /// day (heroes shopping/mustering) just to watch the calendar turn over.</summary>
    [TestCase]
    public void ArmedDemandBoardBeat_SpeaksTheMorningAfter()
    {
        var baseState = GameComposition.NewCampaign(ScriptedSession.Seed);
        var armedState = baseState with
        {
            EventLog = baseState.EventLog.Add(new HeroPassedOnItem(new HeroId(1), new ItemId(1), "too pricey")),
        };
        var ui = MountMainUi(new SimAdapter(armedState));
        try
        {
            // Arms (silently) on day 1 — the SimAdapter's own current state IS the armed state.
            AssertThat(ui.Tutorial.ConsumeDemandBoardBeat(ui.Adapter.CurrentState))
                .OverrideFailureMessage("Setup check: the beat should arm silently on the refusal's own day.")
                .IsNull();

            var tomorrow = ui.Adapter.CurrentState with { Day = ui.Adapter.CurrentState.Day + 1 };
            var beat = ui.Tutorial.ConsumeDemandBoardBeat(tomorrow);

            AssertThat(beat)
                .OverrideFailureMessage("The demand-board beat never spoke on the morning after the refusal.")
                .IsNotNull();
            AssertThat(beat).Contains(MentorVoice.Name);
            AssertThat(beat).Contains("Demand board");

            // Once-ever: a THIRD call, even later, must not speak again.
            var laterStill = tomorrow with { Day = tomorrow.Day + 1 };
            AssertThat(ui.Tutorial.ConsumeDemandBoardBeat(laterStill))
                .OverrideFailureMessage("The once-ever demand-board beat spoke a second time.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The other half of R19's own test scenario: nothing fires — not even an arm — before
    /// a refusal exists.</summary>
    [TestCase]
    public void NoRefusalYet_TheDemandBoardBeatStaysSilent()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The demand-board beat fired with no refusal ever logged.")
                .IsFalse();
            AssertThat(ui.Tutorial.ConsumeDemandBoardBeat(ui.Adapter.CurrentState with { Day = 9 }))
                .OverrideFailureMessage("The demand-board beat spoke with no refusal ever logged.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
