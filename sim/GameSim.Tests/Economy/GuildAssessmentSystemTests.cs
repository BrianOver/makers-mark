using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// Covers Phase D U-D2 (plan 2026-07-21-008): the Guild Assessment's own 7-day dues cadence, the
/// passive Confidence decay + depth-record/attribution-beat/hero-death deltas read off yesterday's
/// stamped log, and the legible edge-triggered threshold consequences (rival expansion, a hero
/// considering leaving, the latched soft-fail at 0). Confidence itself stays on the existing
/// <see cref="RentState.ConfidencePermille"/> gauge (U-D2 extends it; does not add a second meter).
/// </summary>
public class GuildAssessmentSystemTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static (GameState State, List<GameEvent> Events) Run(GameState state)
    {
        var system = new GuildAssessmentSystem();
        var sink = new TestSink();
        var after = system.Process(state, new Pcg32(state.Rng), sink);
        return (after, sink.Events);
    }

    private static GameState BaseState(ulong seed = 1) => GameFactory.NewGame(seed);

    [Fact]
    public void SystemContract_MorningPhase_StableName()
    {
        var system = new GuildAssessmentSystem();
        Assert.Equal(DayPhase.Morning, system.Phase);
        Assert.Equal("guild-assessment", system.Name);
    }

    [Fact]
    public void NotYetDue_CountsDown_AndAppliesPassiveDailyDecay()
    {
        var start = BaseState() with
        {
            Day = 2,
            Assessment = new GuildAssessmentState(DaysUntilAssessment: 3, DuesGold: 20, AssessmentsPassed: 0, MissedAssessments: 0, SoftFailed: false),
            Rent = new RentState(DaysUntilDue: 5, AmountDueGold: 30, MissedPayments: 0, ConfidencePermille: 900),
        };

        var (after, events) = Run(start);

        Assert.Equal(2, after.Assessment.DaysUntilAssessment);
        Assert.Equal(20, after.Assessment.DuesGold);
        Assert.Equal(start.Player.Gold, after.Player.Gold);
        Assert.Equal(900 - GuildAssessmentSystem.PassiveDailyDecayPermille, after.Rent.ConfidencePermille);
        Assert.Empty(events.OfType<GuildAssessmentPassed>());
        Assert.Empty(events.OfType<GuildAssessmentMissed>());
    }

    [Fact]
    public void DueDate_SufficientGold_PaysInFull_EscalatesModestly_RecoversConfidence()
    {
        var start = BaseState() with
        {
            Day = 8,
            Assessment = new GuildAssessmentState(DaysUntilAssessment: 1, DuesGold: 20, AssessmentsPassed: 0, MissedAssessments: 0, SoftFailed: false),
            Rent = new RentState(DaysUntilDue: 5, AmountDueGold: 30, MissedPayments: 0, ConfidencePermille: 500),
        };

        var (after, events) = Run(start);

        var passed = Assert.Single(events.OfType<GuildAssessmentPassed>());
        Assert.Equal(20, passed.DuesPaidGold);
        Assert.Equal(start.Player.Gold - 20, after.Player.Gold);
        Assert.Equal(GuildAssessmentState.CadenceDays, after.Assessment.DaysUntilAssessment); // countdown restarts
        Assert.Equal(1, after.Assessment.AssessmentsPassed);
        Assert.Equal(0, after.Assessment.MissedAssessments);
        Assert.True(after.Assessment.DuesGold > 20, "an on-time payment must still escalate the next ask");
        Assert.Equal(passed.NextDuesGold, after.Assessment.DuesGold);

        // Confidence: -10 passive decay, +100 assessment-passed bonus, net +90.
        Assert.Equal(500 - GuildAssessmentSystem.PassiveDailyDecayPermille + GuildAssessmentSystem.AssessmentPassedBonusPermille, after.Rent.ConfidencePermille);
        Assert.Equal(passed.ConfidencePermille, after.Rent.ConfidencePermille);
    }

    [Fact]
    public void DueDate_InsufficientGold_MissesAssessment_NeverGoesNegative_EscalatesSteeply_LosesConfidence()
    {
        var start = BaseState() with
        {
            Day = 8,
            Player = BaseState().Player with { Gold = 5 }, // less than the 20g ask
            Assessment = new GuildAssessmentState(DaysUntilAssessment: 1, DuesGold: 20, AssessmentsPassed: 0, MissedAssessments: 0, SoftFailed: false),
            Rent = new RentState(DaysUntilDue: 5, AmountDueGold: 30, MissedPayments: 0, ConfidencePermille: 500),
        };

        var (after, events) = Run(start);

        var missed = Assert.Single(events.OfType<GuildAssessmentMissed>());
        Assert.Equal(20, missed.DuesDueGold);
        Assert.Equal(1, missed.MissedAssessments);

        // NEVER game-over: gold is untouched.
        Assert.Equal(5, after.Player.Gold);
        Assert.True(after.Player.Gold >= 0);

        Assert.Equal(GuildAssessmentState.CadenceDays, after.Assessment.DaysUntilAssessment); // clock restarts regardless
        Assert.Equal(1, after.Assessment.MissedAssessments);
        Assert.True(after.Assessment.DuesGold > 20, "a missed assessment must escalate the next ask");
        Assert.Equal(missed.NextDuesGold, after.Assessment.DuesGold);

        // Confidence: -10 passive decay, -50 missed-assessment penalty, net -60.
        Assert.Equal(500 - GuildAssessmentSystem.PassiveDailyDecayPermille - GuildAssessmentSystem.AssessmentMissedPenaltyPermille, after.Rent.ConfidencePermille);
        Assert.Equal(missed.ConfidencePermille, after.Rent.ConfidencePermille);
    }

    [Fact]
    public void MissedEscalation_IsSteeperThanOnTimeEscalation()
    {
        var onTimeStart = BaseState() with
        {
            Day = 8,
            Assessment = new GuildAssessmentState(1, 100, 0, 0, false),
            Rent = new RentState(5, 30, 0, 500),
        };
        var missedStart = BaseState() with
        {
            Day = 8,
            Player = BaseState().Player with { Gold = 0 },
            Assessment = new GuildAssessmentState(1, 100, 0, 0, false),
            Rent = new RentState(5, 30, 0, 500),
        };

        var (paidAfter, _) = Run(onTimeStart);
        var (missedAfter, _) = Run(missedStart);

        Assert.True(missedAfter.Assessment.DuesGold > paidAfter.Assessment.DuesGold,
            "missing the assessment must escalate the next ask MORE than paying it on time");
    }

    [Fact]
    public void Escalation_NeverExceedsMaxDuesGold()
    {
        var start = BaseState() with
        {
            Day = 8,
            Player = BaseState().Player with { Gold = 0 },
            Assessment = new GuildAssessmentState(1, GuildAssessmentSystem.MaxDuesGold, 3, 5, false),
            Rent = new RentState(5, 30, 0, 500),
        };

        var (after, _) = Run(start);

        Assert.Equal(GuildAssessmentSystem.MaxDuesGold, after.Assessment.DuesGold);
    }

    [Fact]
    public void YesterdaysFloorRecord_AddsConfidence()
    {
        var start = BaseState() with
        {
            Day = 3,
            Assessment = new GuildAssessmentState(5, 20, 0, 0, false),
            Rent = new RentState(5, 30, 0, 500),
            EventLog = ImmutableList.Create<GameEvent>(
                new FloorRecordSet(new HeroId(1), 2) { Day = 2, Id = new EventId(1) }),
        };

        var (after, _) = Run(start);

        Assert.Equal(500 - GuildAssessmentSystem.PassiveDailyDecayPermille + GuildAssessmentSystem.DepthRecordBonusPermille, after.Rent.ConfidencePermille);
    }

    [Theory]
    [InlineData(BeatType.KillingBlow)]
    [InlineData(BeatType.LethalSave)]
    [InlineData(BeatType.BreakpointClear)]
    public void YesterdaysAttributionBeat_AboveThreshold_AddsConfidence(BeatType beat)
    {
        var start = BaseState() with
        {
            Day = 3,
            Assessment = new GuildAssessmentState(5, 20, 0, 0, false),
            Rent = new RentState(5, 30, 0, 500),
            EventLog = ImmutableList.Create<GameEvent>(
                new AttributionBeatEvent(beat, new ItemId(1), new HeroId(1), 2, "detail") { Day = 2, Id = new EventId(1) }),
        };

        var (after, _) = Run(start);

        Assert.Equal(500 - GuildAssessmentSystem.PassiveDailyDecayPermille + GuildAssessmentSystem.AttributionBeatBonusPermille, after.Rent.ConfidencePermille);
    }

    [Fact]
    public void YesterdaysProvisionedBeat_IsBelowThreshold_NoConfidenceBonus()
    {
        var start = BaseState() with
        {
            Day = 3,
            Assessment = new GuildAssessmentState(5, 20, 0, 0, false),
            Rent = new RentState(5, 30, 0, 500),
            EventLog = ImmutableList.Create<GameEvent>(
                new AttributionBeatEvent(BeatType.Provisioned, new ItemId(1), new HeroId(1), 2, "detail") { Day = 2, Id = new EventId(1) }),
        };

        var (after, _) = Run(start);

        Assert.Equal(500 - GuildAssessmentSystem.PassiveDailyDecayPermille, after.Rent.ConfidencePermille);
    }

    [Fact]
    public void YesterdaysHeroDeath_SubtractsConfidence()
    {
        var start = BaseState() with
        {
            Day = 3,
            Assessment = new GuildAssessmentState(5, 20, 0, 0, false),
            Rent = new RentState(5, 30, 0, 500),
            EventLog = ImmutableList.Create<GameEvent>(
                new HeroDied(new HeroId(1), 2, "lost to the Mine", GearSet.Empty) { Day = 2, Id = new EventId(1) }),
        };

        var (after, _) = Run(start);

        Assert.Equal(500 - GuildAssessmentSystem.PassiveDailyDecayPermille - GuildAssessmentSystem.HeroDeathPenaltyPermille, after.Rent.ConfidencePermille);
    }

    [Fact]
    public void ConfidenceCrossingBelowRivalThreshold_FiresOnce_AndBumpsRivalShare()
    {
        var start = BaseState() with
        {
            Day = 3,
            Assessment = new GuildAssessmentState(5, 20, 0, 0, false),
            Rent = new RentState(5, 30, 0, GuildAssessmentSystem.RivalExpansionThreshold + GuildAssessmentSystem.PassiveDailyDecayPermille / 2),
            RivalMarketSharePermille = 100,
        };

        var (after, events) = Run(start);

        Assert.True(after.Rent.ConfidencePermille < GuildAssessmentSystem.RivalExpansionThreshold);
        Assert.Single(events.OfType<RivalExpansionTriggered>());
        Assert.Equal(100 + GuildAssessmentSystem.RivalExpansionSharePermille, after.RivalMarketSharePermille);

        // Already below threshold the next Morning: rival share keeps pressing, but the event does not re-fire.
        var (after2, events2) = Run(after);
        Assert.Empty(events2.OfType<RivalExpansionTriggered>());
        Assert.Equal(after.RivalMarketSharePermille + GuildAssessmentSystem.RivalExpansionSharePermille, after2.RivalMarketSharePermille);
    }

    [Fact]
    public void ConfidenceCrossingBelowHeroLeavingThreshold_FiresOnce_NamesBoycottingHero()
    {
        var start = GameComposition.NewCampaign(seed: 1) with
        {
            Day = 10, // every starting hero has gone unmet-demand since day 1 with no purchases -> boycotting by day 7+
            Assessment = new GuildAssessmentState(5, 20, 0, 0, false),
            Rent = new RentState(5, 30, 0, GuildAssessmentSystem.HeroLeavingThreshold + GuildAssessmentSystem.PassiveDailyDecayPermille / 2),
        };

        var (after, events) = Run(start);

        Assert.True(after.Rent.ConfidencePermille < GuildAssessmentSystem.HeroLeavingThreshold);
        var considering = Assert.Single(events.OfType<HeroConsideringLeaving>());
        Assert.True(start.Heroes.ContainsKey(considering.Hero.Value));

        var (after2, events2) = Run(after);
        Assert.Empty(events2.OfType<HeroConsideringLeaving>());
        _ = after2;
    }

    [Fact]
    public void ConfidenceHitsZero_TownConfidenceCollapsed_FiresOnceAndLatches()
    {
        var start = BaseState() with
        {
            Day = 3,
            Assessment = new GuildAssessmentState(5, 20, 0, 3, false),
            Rent = new RentState(5, 30, 0, GuildAssessmentSystem.PassiveDailyDecayPermille), // exactly enough decay to hit 0
        };

        var (after, events) = Run(start);

        Assert.Equal(0, after.Rent.ConfidencePermille);
        var collapsed = Assert.Single(events.OfType<TownConfidenceCollapsed>());
        Assert.Equal(3, collapsed.MissedAssessments);
        Assert.True(after.Assessment.SoftFailed);

        // Still at 0 the next Morning: latched, does not re-fire.
        var (after2, events2) = Run(after);
        Assert.Equal(0, after2.Rent.ConfidencePermille);
        Assert.Empty(events2.OfType<TownConfidenceCollapsed>());
        Assert.True(after2.Assessment.SoftFailed);
    }

    [Fact]
    public void ConfidenceNeverExceedsOneThousand_OrDropsBelowZero()
    {
        var high = BaseState() with
        {
            Day = 8,
            Assessment = new GuildAssessmentState(1, 20, 0, 0, false),
            Rent = new RentState(5, 30, 0, 995),
        };
        var (afterHigh, _) = Run(high);
        Assert.InRange(afterHigh.Rent.ConfidencePermille, 0, 1000);

        var low = BaseState() with
        {
            Day = 8,
            Player = BaseState().Player with { Gold = 0 },
            Assessment = new GuildAssessmentState(1, 20, 0, 0, false),
            Rent = new RentState(5, 30, 0, 5),
        };
        var (afterLow, _) = Run(low);
        Assert.InRange(afterLow.Rent.ConfidencePermille, 0, 1000);
    }

    [Fact]
    public void HeldMorningGuard_SkipsWhileCounterSessionOpen()
    {
        var start = BaseState() with
        {
            Day = 3,
            Assessment = new GuildAssessmentState(5, 20, 0, 0, false),
            Rent = new RentState(5, 30, 0, 500),
            Counter = CounterState.Empty, // Closed defaults to false
        };

        var (after, events) = Run(start);

        Assert.Empty(events);
        Assert.Equal(start.Assessment, after.Assessment);
        Assert.Equal(start.Rent, after.Rent);
    }

    [Fact]
    public void DrawsNoRng_TwoRunsIdentical()
    {
        var start = BaseState() with
        {
            Day = 8,
            Assessment = new GuildAssessmentState(1, 20, 0, 0, false),
            Rent = new RentState(5, 30, 0, 500),
        };
        var system = new GuildAssessmentSystem();

        var rngA = new Pcg32(start.Rng);
        var a = system.Process(start, rngA, new TestSink());
        var rngB = new Pcg32(start.Rng);
        var b = system.Process(start, rngB, new TestSink());

        Assert.Equal(SaveCodec.Serialize(a), SaveCodec.Serialize(b));
        Assert.Equal(start.Rng, rngA.Snapshot());
    }
}
