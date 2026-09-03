#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Heroes;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-ONBOARD-07 (§11.15, P2-OQ2): "Bryn wrong on purpose" — the mentor's own greedy shelf rule
/// (beat 3) and her correction, "eating her rule", once the sim has proven it wrong. Its whole
/// legality rests on one discipline: her mechanism copy is unattributed and true, only her OPINION
/// is wrong, and she says it is an opinion — nothing anywhere ever tells the player she was wrong.
/// This suite is the pin for that discipline, plus the P2-ONBOARD-09 fix folded into this unit: the
/// correction beat keys on <see cref="RelationshipBand"/> (the mechanism that actually pays a pinned
/// price — <c>CommissionSystem.PremiumBonusFor</c>), never on the dead <c>CounterState.GoodwillPermille</c>
/// chip.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class BrynWrongOnPurposeTests
{
    // ── Beat 3: her rule, wrong on purpose ──────────────────────────────────────────────────────

    /// <summary>The plan carries this copy verbatim (§11.15, "Beat 3, her rule, wrong on purpose")
    /// — pinned so a future edit cannot silently paraphrase it.</summary>
    [TestCase]
    public void GreedyRuleLessonText_MatchesThePlanVerbatim()
    {
        AssertThat(TutorialFlow.GreedyRuleLessonText).IsEqual(
            "My rule, since you're asking the shelf to do your haggling: cost, then half again on top. "
            + "Coin is the only thing this bench eats, and a hero who wants it will find the gold. "
            + "That's my rule, anyway. You're the one with the stamp.");
    }

    /// <summary>
    /// The whole legality of this unit: her wrong number is framed as HER preference, never as a
    /// fact about what the shelf or the game will do. Recettear's lesson (cited in the class doc
    /// above and in <see cref="TutorialFlow.GreedyRuleLessonText"/>'s own doc) is that a mentor
    /// stating a preference can never be caught lying about what the sim decided — this is the test
    /// that pins the preference framing itself, not just the words.
    /// </summary>
    [TestCase]
    public void GreedyRuleLessonText_ReadsAsHerOpinion_NeverAGameFact()
    {
        var text = TutorialFlow.GreedyRuleLessonText;

        AssertThat(text.Contains("My rule", StringComparison.Ordinal))
            .OverrideFailureMessage("The greedy rule never frames itself as HER rule.")
            .IsTrue();
        AssertThat(text.Contains("That's my rule, anyway", StringComparison.Ordinal))
            .OverrideFailureMessage("The greedy rule never disclaims itself as an opinion, not a fact.")
            .IsTrue();
        AssertThat(text.Contains("You're the one with the stamp", StringComparison.Ordinal))
            .OverrideFailureMessage("The greedy rule never hands the decision back to the player (influence never orders).")
            .IsTrue();
    }

    /// <summary>The once-ever contract every first-touch lesson in this codebase carries (the
    /// 1287x memorial-nag precedent) — pinned directly on <see cref="TutorialFlow.ConsumeGreedyRuleLesson"/>,
    /// the actual call site <c>MainUi.OnTownBuildingClicked</c> uses on the player's first walk to the
    /// Shop.</summary>
    [TestCase]
    public void ConsumeGreedyRuleLesson_FiresExactlyOnce()
    {
        var ui = MountMainUi();
        try
        {
            var first = ui.Tutorial.ConsumeGreedyRuleLesson();
            AssertThat(first).IsEqual(MentorVoice.Speak(TutorialFlow.GreedyRuleLessonText));

            AssertThat(ui.Tutorial.ConsumeGreedyRuleLesson())
                .OverrideFailureMessage("Her greedy rule re-fired on a second walk to the Shop.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Same register check <see cref="TutorialFlow.GreedyRuleLessonText"/>'s corpus siblings
    /// already pass (never an order, never the engine's own name) — pinned directly for these two new
    /// lines without touching the shared <c>HerFullCorpus</c> array another lane owns right now.</summary>
    [TestCase]
    public void HerTwoNewLines_NeverReadAsACommand_OrNameTheEngine()
    {
        string[] lines = [TutorialFlow.GreedyRuleLessonText, TutorialFlow.RuleRevisedBeatText];
        string[] bannedEngineWords = ["the sim", "button", "click", "HUD"];

        foreach (var line in lines)
        {
            AssertThat(line.TrimEnd().EndsWith('!'))
                .OverrideFailureMessage($"\"{line}\" ends with an exclamation — reads as an order.")
                .IsFalse();
            AssertThat(line.Contains(" must ", StringComparison.Ordinal))
                .OverrideFailureMessage($"\"{line}\" contains \"must\" — reads as a command to the player.")
                .IsFalse();

            foreach (var banned in bannedEngineWords)
            {
                AssertThat(line.Contains(banned, StringComparison.Ordinal))
                    .OverrideFailureMessage($"\"{line}\" names \"{banned}\" — Bryn is a townsfolk, not the engine.")
                    .IsFalse();
            }
        }
    }

    /// <summary>
    /// Proof requirement #2: no copy anywhere tells the player she was wrong. The correction
    /// (<see cref="TutorialFlow.RuleRevisedBeatText"/>) is the one line in the whole game positioned
    /// to say it, and it does not — every correction arrives from the sim ARMING the beat at all,
    /// never from a sentence saying so.
    /// </summary>
    [TestCase]
    public void NoCopy_EverTellsThePlayerBrynWasWrong()
    {
        string[] lines = [TutorialFlow.GreedyRuleLessonText, TutorialFlow.RuleRevisedBeatText];
        string[] bannedAdmissions =
        [
            "wrong", "mistake", "incorrect", "error", "bad advice", "was wrong", "misled", "lied",
        ];

        foreach (var line in lines)
        {
            foreach (var banned in bannedAdmissions)
            {
                AssertThat(line.Contains(banned, StringComparison.OrdinalIgnoreCase))
                    .OverrideFailureMessage($"\"{line}\" contains \"{banned}\" — the game is telling the player she was wrong, not letting the sim prove it.")
                    .IsFalse();
            }
        }
    }

    // ── Mechanism check: the shelf has no fairness memory, the counter has all of it ───────────
    //
    // No test here re-drives a full craft-to-shelf scenario — GameSim.Heroes.ShoppingAi.EvaluateItem's
    // own doc (sim/GameSim/Heroes/ShoppingAi.cs) already states the shelf's ENTIRE verdict surface:
    // role fit, veteran quality, affordability, gear-score gain, nothing else — no price-fairness
    // check, ever. HaggleResolver.CloseSale (sim/GameSim/Counter/HaggleResolver.cs) is the one place a
    // sale ever moves Hero.MoodPermille, and RelationshipBands.For (below) is the one place mood turns
    // into a paid band. TutorialCopyIsFollowableTests.TheCounterStep_TeachesThatAFairAnswerIsRememberedAndASqueezeIsnt
    // already pins the counter half of this claim; this suite pins the redemption beat's own use of it.

    // ── "Eating her rule": keyed on the band, not the dead GoodwillPermille chip ─────────────────

    private static GameState PinnedCloseState(int moodPermille) => WithFirstHero(
        GameComposition.NewCampaign(9401),
        hero => hero with { MoodPermille = moodPermille },
        hero => ImmutableList.Create<GameEvent>(
            new CounterSaleClosed(hero.Id, new ItemId(1), Price: 40, Pinned: true)));

    private static GameState CommissionFulfilledState() => GameComposition.NewCampaign(9401) with
    {
        EventLog = ImmutableList.Create<GameEvent>(
            new CommissionFulfilled(new HeroId(1), new ItemId(1), Premium: 15)),
    };

    private static GameState WithFirstHero(
        GameState baseState, Func<Hero, Hero> updateHero, Func<Hero, ImmutableList<GameEvent>> eventLog)
    {
        var hero = updateHero(baseState.Heroes.Values.First());
        return baseState with
        {
            Heroes = baseState.Heroes.SetItem(hero.Id.Value, hero),
            EventLog = eventLog(hero),
        };
    }

    /// <summary>The band half of the trigger: a pinned close for a hero whose mood alone (no shelf
    /// purchases at all) has already crossed <c>RelationshipBands.RegularMinMood</c> arms the beat.</summary>
    [TestCase]
    public void RuleRevisedBeat_Arms_OnAPinnedClose_WhoseBandHasRisenToRegular()
    {
        var state = PinnedCloseState(moodPermille: RelationshipBands.RegularMinMood);
        AssertThat(RelationshipBands.For(state.Heroes.Values.First().Id, state))
            .OverrideFailureMessage("Test fixture is broken: the hero is not actually at Regular band.")
            .IsEqual(RelationshipBand.Regular);

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            var line = ui.Tutorial.ConsumeRuleRevisedBeat(ui.Adapter.CurrentState);
            AssertThat(line).IsEqual(TutorialFlow.RuleRevisedBeatText);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The negative half, and the one that actually proves "keyed on the band, not the chip": the
    /// IDENTICAL pinned close, but the hero's mood never crossed Regular — the beat must NOT arm.
    /// Before P2-ONBOARD-09's fix, a trigger keyed on <c>CounterState.GoodwillPermille</c> (a field
    /// nothing else reads) could not tell this case apart from the one above at all.
    /// </summary>
    [TestCase]
    public void RuleRevisedBeat_DoesNotArm_OnAPinnedClose_WhoseBandIsStillStranger()
    {
        var state = PinnedCloseState(moodPermille: 0);
        AssertThat(RelationshipBands.For(state.Heroes.Values.First().Id, state)).IsEqual(RelationshipBand.Stranger);

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            AssertThat(ui.Tutorial.ConsumeRuleRevisedBeat(ui.Adapter.CurrentState)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The dead-chip proof, directly: a pinned close at Regular band arms the beat exactly
    /// the same whether the live counter session's own <c>GoodwillPermille</c> reads deeply negative
    /// (as a run of fleeces would leave it) or the default zero — the field has zero bearing either
    /// way, because nothing in <see cref="TutorialFlow"/> ever reads it.</summary>
    [TestCase]
    public void RuleRevisedBeat_IgnoresGoodwillPermille_Entirely()
    {
        var state = PinnedCloseState(moodPermille: RelationshipBands.RegularMinMood) with
        {
            Counter = CounterState.Empty with { GoodwillPermille = -999 },
        };

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            AssertThat(ui.Tutorial.ConsumeRuleRevisedBeat(ui.Adapter.CurrentState))
                .OverrideFailureMessage("A deeply negative GoodwillPermille blocked the beat — it must be keyed on the band, not this dead chip.")
                .IsEqual(TutorialFlow.RuleRevisedBeatText);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The other half of "whichever comes first": no pinned close at all, but a commission
    /// premium was collected — U33's own "a promise kept" fact, whose content this unit replaces.</summary>
    [TestCase]
    public void RuleRevisedBeat_Arms_OnTheFirstCommissionFulfilled_WithNoPinnedCloseAtAll()
    {
        var ui = MountMainUi(new SimAdapter(CommissionFulfilledState()));
        try
        {
            AssertThat(ui.Tutorial.ConsumeRuleRevisedBeat(ui.Adapter.CurrentState)).IsEqual(TutorialFlow.RuleRevisedBeatText);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Fires at most once ever, the same contract every other dormant act in this file
    /// keeps.</summary>
    [TestCase]
    public void RuleRevisedBeat_FiresAtMostOnce()
    {
        var state = CommissionFulfilledState();
        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            AssertThat(ui.Tutorial.ConsumeRuleRevisedBeat(ui.Adapter.CurrentState)).IsNotNull();

            var laterState = state with
            {
                Day = 20,
                EventLog = state.EventLog.Add(new CommissionFulfilled(new HeroId(2), new ItemId(2), Premium: 25)),
            };
            AssertThat(ui.Tutorial.ConsumeRuleRevisedBeat(laterState)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>U33's own test scenario, carried forward onto this unit's replacement content: "a
    /// declining player gets no promise-kept line."</summary>
    [TestCase]
    public void RuleRevisedBeat_NeverArms_WhenTheChainWasDismissed()
    {
        var ui = MountMainUi(new SimAdapter(CommissionFulfilledState()));
        try
        {
            ui.Tutorial.Dismiss();
            AssertThat(ui.Tutorial.ConsumeRuleRevisedBeat(ui.Adapter.CurrentState)).IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Confirms the Demand board's own unlock condition still reads as the plan describes
    /// (§11.15: "her bad advice causes the discovery surface to open") before this unit relies on it
    /// — <c>SurfaceUnlocks.Gates</c> is read directly, never re-implemented here.</summary>
    [TestCase]
    public void DemandBoard_OpensOnTheSamePassReason_HerRuleWouldProduce()
    {
        var baseState = GameComposition.NewCampaign(9401);
        AssertThat(SurfaceUnlocks.IsOpen(baseState, "Demand"))
            .OverrideFailureMessage("Sanity: Demand must start closed on a fresh campaign.")
            .IsFalse();

        var passedState = baseState with
        {
            EventLog = ImmutableList.Create<GameEvent>(
                new HeroPassedOnItem(new HeroId(1), new ItemId(1), "couldn't afford it")),
        };
        AssertThat(SurfaceUnlocks.IsOpen(passedState, "Demand"))
            .OverrideFailureMessage("Demand must open on a HeroPassedOnItem — the exact event an overpriced, greedy-rule shelf item produces.")
            .IsTrue();
    }
}
#endif
