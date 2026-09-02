#if GDUNIT_TESTS
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T2 Wave D (§11.14.4, Act III): the forecast board's two new lessons ("the forecast board
/// taught", "the muster speaks" — dilemma #3) and the counterfactual proof's first-touch lesson
/// (link 4, "the proof taught the first time it lands") together with the <c>MineWatch.BarkFor</c>
/// fix that makes the proof's own flare actually name the item (a prerequisite this unit found
/// along the way — the bark used to discard <see cref="AttributionBeatEvent.Detail"/> entirely).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class WaveDLessonsTests
{
    /// <summary>Parks a pending <see cref="ExpeditionResult"/> carrying ONE hand-crafted
    /// <see cref="AttributionBeat"/> at Evening — the exact shape <c>ExpeditionRevealSystemTests</c>
    /// (sim-level) uses to drive <see cref="GameSim.Drama.ExpeditionRevealSystem"/> deterministically,
    /// with no combat RNG involved: the system only ever forwards <c>result.Beats</c> into
    /// <see cref="AttributionBeatEvent"/>s, it never recomputes them.</summary>
    private static GameState StateWithPendingAttributionBeat(ulong seed, ItemId itemId, HeroId heroId, string itemName)
    {
        var baseState = GameComposition.NewCampaign(seed);
        var item = new Item(
            itemId, "test-proof-item", itemName, ItemSlot.Weapon, QualityGrade.Fine,
            new ItemStats(Attack: 8, Defense: 0, Weight: 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);

        var result = new ExpeditionResult(
            Party: ImmutableList.Create(heroId),
            TargetFloor: 2,
            DeepestFloorCleared: 2,
            Floors: ImmutableList<FloorOutcome>.Empty,
            Survivors: ImmutableList.Create(heroId),
            Deaths: ImmutableList<HeroId>.Empty,
            Beats: ImmutableList.Create(new AttributionBeat(
                BeatType.KillingBlow, itemId, heroId, 2, $"{itemName} landed the killing blow on the Cave Rat")),
            Loot: ImmutableList<OreLoot>.Empty,
            GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty);

        return baseState with
        {
            Phase = DayPhase.Evening,
            Items = baseState.Items.Add(item.Id.Value, item),
            PendingExpeditions = baseState.PendingExpeditions.Add(result),
        };
    }

    /// <summary>
    /// U30: drive the Return Ritual's own delayed Evening reveal to completion.
    ///
    /// <para>Before U30 the proof spoke through a first-touch polled every RefreshAll tick, so a bare
    /// <c>AdvancePhase()</c> was enough to see it — wherever the player happened to be standing. U30
    /// moved it onto the automatic ledger reveal, anchored into the very beat card it describes, which
    /// is the whole improvement: the line now arrives on the screen it is talking about. That reveal is
    /// gated behind <see cref="MainUi.LedgerDelayRemaining"/> and driven by <c>_Process</c>, so these
    /// tests must drive it rather than assert one tick early.</para>
    ///
    /// <para>Waits on the CONDITION (the ledger actually revealed), never a guessed frame count, and
    /// fails loudly rather than letting a silent non-reveal read as "the lesson did not fire".</para>
    /// </summary>
    private static void DriveTheEveningLedgerReveal(GodotClient.MainUi ui)
    {
        for (var i = 0; i < 600 && ui.LedgerDelayRemaining > 0; i++)
        {
            ui._Process(0.016);
        }

        AssertThat(ui.LedgerDelayRemaining)
            .OverrideFailureMessage(
                "Setup check: the Evening ledger never revealed, so nothing here can say whether the " +
                "proof lesson fires. The reveal itself is broken, not the lesson.")
            .IsEqual(0.0);
    }

    /// <summary>Link 4's whole payload, end to end: the sim's own counterfactual beat both teaches
    /// itself (first-touch, once) AND finally reaches the one screen the player is watching with
    /// the item actually named.</summary>
    [TestCase]
    public void FirstAttributionBeat_TeachesTheProof_AndTheWatchNamesTheItem()
    {
        var heroId = new HeroId(1);
        var itemId = new ItemId(9601);
        var ui = MountMainUi(new SimAdapter(StateWithPendingAttributionBeat(9601, itemId, heroId, "Emberbite")));
        try
        {
            ui.Adapter.AdvancePhase(); // Evening: ExpeditionRevealSystem forwards the parked beat

            AssertThat(ui.Adapter.LastEvents.OfType<AttributionBeatEvent>().Any(b => b.Item == itemId))
                .OverrideFailureMessage("Setup check: the parked ExpeditionResult never produced an AttributionBeatEvent this tick.")
                .IsTrue();

            DriveTheEveningLedgerReveal(ui);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The proof lesson never showed on the campaign's first-ever attribution beat.")
                .IsTrue();
            var lessonText = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(lessonText).Contains(MentorVoice.Name);

            // U30: identify the lesson by WHERE IT POINTS, not by a word in its prose. This assertion
            // used to require the literal "proof" and broke the moment the copy was rewritten to stop
            // naming the engine — a lexical proxy for "is this the right lesson" that fails on every
            // future copy pass. The anchor is the durable fact: only the Proof act's own voice aims at
            // the ledger's beat card.
            AssertThat(ui.Mentor.CurrentAnchor)
                .OverrideFailureMessage(
                    $"The banner is showing something, but not aimed at the ledger's beat card " +
                    $"(anchor: {ui.Mentor.CurrentAnchor}) — so this is not the proof beat.")
                .IsEqual(TutorialFlow.ProofBeatAnchor("Ledger"));

            // The bark fix: the watch's own flare must name the item (b.Detail), not just the hero
            // and a generic verb — the exact defect this unit found and fixed.
            var bark = Find<Label>(ui.Watch, "RecordBark").Text;
            AssertThat(bark)
                .OverrideFailureMessage($"MineWatch's own bark does not name the item that earned the beat: \"{bark}\"")
                .Contains("Emberbite");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Two qualifying beats landing in the very SAME tick must still show the lesson only
    /// ONCE — <see cref="TutorialFlow.ConsumeFirstTouch"/>'s own once-ever contract, already proven
    /// generically elsewhere, exercised here through this unit's own new call site.</summary>
    [TestCase]
    public void TwoAttributionBeatsInTheSameTick_StillShowTheProofLessonOnlyOnce()
    {
        var heroId = new HeroId(1);
        var firstItemId = new ItemId(9602);
        var secondItemId = new ItemId(9603);
        var baseState = StateWithPendingAttributionBeat(9602, firstItemId, heroId, "Gatebreaker");
        var secondItem = new Item(
            secondItemId, "test-proof-item-2", "Oathkeeper", ItemSlot.Shield, QualityGrade.Fine,
            new ItemStats(Attack: 0, Defense: 7, Weight: 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var secondResult = new ExpeditionResult(
            Party: ImmutableList.Create(heroId), TargetFloor: 3, DeepestFloorCleared: 3,
            Floors: ImmutableList<FloorOutcome>.Empty, Survivors: ImmutableList.Create(heroId),
            Deaths: ImmutableList<HeroId>.Empty,
            Beats: ImmutableList.Create(new AttributionBeat(BeatType.LethalSave, secondItemId, heroId, 3, "Oathkeeper turned a lethal hit")),
            Loot: ImmutableList<OreLoot>.Empty, GoldEarnedByHero: ImmutableSortedDictionary<int, int>.Empty);
        var state = baseState with
        {
            Items = baseState.Items.Add(secondItem.Id.Value, secondItem),
            PendingExpeditions = baseState.PendingExpeditions.Add(secondResult),
        };

        var ui = MountMainUi(new SimAdapter(state));
        try
        {
            ui.Adapter.AdvancePhase(); // Evening: both parked results reveal in this ONE tick

            AssertThat(ui.Adapter.LastEvents.OfType<AttributionBeatEvent>().Count())
                .OverrideFailureMessage("Setup check: this fixture did not produce two attribution beats in one tick.")
                .IsEqual(2);

            DriveTheEveningLedgerReveal(ui);
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The proof lesson never showed despite two qualifying beats landing this tick.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text)
                .OverrideFailureMessage($"The banner shows something other than Bryn's own voice: \"{text}\"")
                .Contains(MentorVoice.Name);

            // U30: see the sibling test — identified by anchor, not by a word in the copy.
            AssertThat(ui.Mentor.CurrentAnchor)
                .OverrideFailureMessage(
                    $"Two beats landed, but the banner is not aimed at the ledger's beat card " +
                    $"(anchor: {ui.Mentor.CurrentAnchor}) — so this is not the proof beat firing once.")
                .IsEqual(TutorialFlow.ProofBeatAnchor("Ledger"));
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>"the forecast board taught": fires the first time the board is EVER opened.
    ///
    /// <para>Used to be a <see cref="GodotClient.Ui.MentorBanner"/> popup that genuinely collided
    /// with the muster dilemma below (a fresh campaign's starter heroes always carry a gear gap, so
    /// this test and <c>FirstForecastWithAGearGap</c> were unwinnable simultaneously under any
    /// single banner priority order — PR #575's fix, "the muster dilemma outranks the generic
    /// orientation note"). P2-ONBOARD-02 (§11.15) retires that collision entirely: this lesson is
    /// now a header caption on <see cref="RaidForecastBoard"/> itself, a different surface from the
    /// banner the muster dilemma still uses, so the two can never contend for the same screen again.
    /// The fully-geared fixture below is no longer REQUIRED for that reason, but is kept — it still
    /// proves the caption fires even on the common everyday case (a gap also present).</para>
    /// </summary>
    [TestCase]
    public void OpeningTheForecastBoardForTheFirstTime_TeachesWhatItIs()
    {
        var baseState = GameComposition.NewCampaign(9701);
        var fullyGeared = baseState with
        {
            Heroes = baseState.Heroes.SetItems(baseState.Heroes.Select(kv =>
                new System.Collections.Generic.KeyValuePair<int, Hero>(
                    kv.Key,
                    kv.Value with { Gear = new GearSet(new ItemId(90001), new ItemId(90002), new ItemId(90003)) }))),
        };
        var ui = MountMainUi(new SimAdapter(fullyGeared));
        try
        {
            var noGaps = GameSim.Heroes.RaidForecast.ForTomorrow(ui.Adapter.CurrentState)
                .All(p => p.GearGaps.IsEmpty);
            AssertThat(noGaps)
                .OverrideFailureMessage("Setup check: this fixture still has a gear gap -- it would collide with the muster lesson and prove nothing about the orientation lesson standing alone.")
                .IsTrue();

            ui.Forecast.ShowForTomorrow(ui.Adapter.CurrentState);

            var caption = Find<Label>(ui.Forecast, "OnceEverCaption");
            AssertThat(caption.Visible)
                .OverrideFailureMessage("The forecast board lesson never showed on its first-ever open.")
                .IsTrue();
            AssertThat(caption.Text).Contains("preview");
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The forecast board's own orientation note must render as its own caption, never the floating banner.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>"the muster speaks" (dilemma #3): fires once a rendered forecast actually shows a
    /// party marching with a real gear gap — a fresh campaign's starter heroes reliably have at
    /// least one empty slot, the same everyday case the dilemma describes.</summary>
    [TestCase]
    public void FirstForecastWithAGearGap_TeachesTheMusterDilemma()
    {
        var ui = MountMainUi(new SimAdapter(GameComposition.NewCampaign(9702)));
        try
        {
            ui.Forecast.ShowForTomorrow(ui.Adapter.CurrentState);

            var gapExists = GameSim.Heroes.RaidForecast.ForTomorrow(ui.Adapter.CurrentState)
                .Any(p => !p.GearGaps.IsEmpty);
            AssertThat(gapExists)
                .OverrideFailureMessage("Setup check: no party in this fixture's forecast shows a gear gap -- this test proves nothing about the muster dilemma without one.")
                .IsTrue();

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The muster dilemma never showed despite a real gear gap on the board.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains("empty slot");
            AssertThat(text).Contains("survive");
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
