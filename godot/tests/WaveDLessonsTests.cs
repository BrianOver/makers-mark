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

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The proof lesson never showed on the campaign's first-ever attribution beat.")
                .IsTrue();
            var lessonText = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(lessonText).Contains(MentorVoice.Name);
            AssertThat(lessonText).Contains("proof");

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
            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The proof lesson never showed despite two qualifying beats landing this tick.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text)
                .OverrideFailureMessage($"The banner shows something other than a single, coherent proof lesson: \"{text}\"")
                .Contains("proof");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>"the forecast board taught": fires the first time the board is EVER opened,
    /// regardless of what tomorrow's muster looks like.</summary>
    [TestCase]
    public void OpeningTheForecastBoardForTheFirstTime_TeachesWhatItIs()
    {
        var ui = MountMainUi(new SimAdapter(GameComposition.NewCampaign(9701)));
        try
        {
            ui.Forecast.ShowForTomorrow(ui.Adapter.CurrentState);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("The forecast board lesson never showed on its first-ever open.")
                .IsTrue();
            var text = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(text).Contains(MentorVoice.Name);
            AssertThat(text).Contains("preview");
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
