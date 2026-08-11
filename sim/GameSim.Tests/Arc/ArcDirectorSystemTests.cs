using System.Collections.Immutable;
using System.Linq;
using GameSim.Arc;
using GameSim.Contracts;
using GameSim.Tests.Drama;

namespace GameSim.Tests.Arc;

/// <summary>
/// Phase D (U-D3): the arc director's threshold logic. Uses the same single-system-kernel
/// fixture as the Drama reveal tests (<see cref="DramaFixtures.Tick"/>) so each case exercises
/// the real <see cref="GameKernel"/> phase-advance + event-stamping path, not a hand-rolled stub.
/// </summary>
public class ArcDirectorSystemTests
{
    private static GameState AtEveningWithDepth(int floor)
    {
        var baseState = DramaFixtures.NewWorld();
        return baseState with
        {
            Phase = DayPhase.Evening,
            Drama = baseState.Drama with { DepthsBoard = baseState.Drama.DepthsBoard.SetItem(1, floor) },
        };
    }

    /// <summary>L5: Act III/Climax now key on <see cref="Hero.LadderRank"/>, not floor depth —
    /// stamps hero 1 (Torvald, the starting roster's first hero) with the given rank alongside the
    /// given depth-board floor (still needed for the Act I-&gt;II leg, unchanged by L5).</summary>
    private static GameState AtEveningWithDepthAndRank(int floor, int rank)
    {
        var baseState = AtEveningWithDepth(floor);
        var hero = baseState.Heroes[1];
        return baseState with
        {
            Heroes = baseState.Heroes.SetItem(1, hero with { LadderRank = rank }),
        };
    }

    [Fact]
    public void NoDepthYet_StaysActI_NoEvents()
    {
        var state = DramaFixtures.NewWorld() with { Phase = DayPhase.Evening };
        var result = DramaFixtures.Tick(state, new ArcDirectorSystem());

        Assert.Equal(CampaignAct.ActI, result.NewState.Arc.Act);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void ReachesFloor3_AdvancesToActII_Once()
    {
        var state = AtEveningWithDepth(ArcDirectorSystem.ActIIFloorThreshold);
        var result = DramaFixtures.Tick(state, new ArcDirectorSystem());

        Assert.Equal(CampaignAct.ActII, result.NewState.Arc.Act);
        Assert.Equal(state.Day, result.NewState.Arc.ActIIStartDay);
        var advanced = Assert.Single(result.Events.OfType<ActAdvanced>());
        Assert.Equal(CampaignAct.ActII, advanced.Act);
        Assert.Empty(result.Events.OfType<ClimaxReached>());

        // Same depth again next Evening: Act II already reached — no re-fire.
        var again = DramaFixtures.Tick(result.NewState with { Phase = DayPhase.Evening }, new ArcDirectorSystem());
        Assert.Empty(again.Events.OfType<ActAdvanced>());
        Assert.Equal(CampaignAct.ActII, again.NewState.Arc.Act);
        Assert.Equal(result.NewState.Arc.ActIIStartDay, again.NewState.Arc.ActIIStartDay);
    }

    [Fact]
    public void ReachesTerminalRank_AdvancesToActIII_ButDoesNotYetFireClimax()
    {
        // L5: reaching the ladder's top rung (TerminalRank) opens Act III — the last dungeon admits
        // this hero — but the Climax (beating that dungeon) is a LATER, separate event now.
        var state = AtEveningWithDepthAndRank(ArcDirectorSystem.ActIIFloorThreshold, ArcDirectorSystem.TerminalRank);
        var result = DramaFixtures.Tick(state, new ArcDirectorSystem());

        Assert.Equal(CampaignAct.ActIII, result.NewState.Arc.Act);
        var acts = result.Events.OfType<ActAdvanced>().Select(a => a.Act).ToImmutableList();
        Assert.Equal(ImmutableList.Create(CampaignAct.ActII, CampaignAct.ActIII), acts);
        Assert.Empty(result.Events.OfType<ClimaxReached>());
        Assert.Equal(0, result.NewState.Arc.ClimaxDay);
    }

    [Fact]
    public void ReachesClimaxRank_AdvancesToActIII_AndFiresClimax_SameTick()
    {
        // A hero who jumps straight to ClimaxRank in one synthetic tick (never realistic on a real
        // trace — LadderRank increments by exactly one graduation per Evening — but the cascade must
        // still resolve correctly) fires Act II, Act III, and the Climax all in the same Process call.
        var state = AtEveningWithDepthAndRank(ArcDirectorSystem.ActIIFloorThreshold, ArcDirectorSystem.ClimaxRank);
        var result = DramaFixtures.Tick(state, new ArcDirectorSystem());

        Assert.Equal(CampaignAct.ActIII, result.NewState.Arc.Act);
        var acts = result.Events.OfType<ActAdvanced>().Select(a => a.Act).ToImmutableList();
        Assert.Equal(ImmutableList.Create(CampaignAct.ActII, CampaignAct.ActIII), acts);
        var climax = Assert.Single(result.Events.OfType<ClimaxReached>());
        Assert.Equal(ArcDirectorSystem.ActIIFloorThreshold, climax.DeepestFloorReached);
        Assert.Equal(state.Day, result.NewState.Arc.ClimaxDay);
    }

    [Fact]
    public void ClimaxDoesNotRefire_OnSubsequentTicks()
    {
        var state = AtEveningWithDepthAndRank(ArcDirectorSystem.ActIIFloorThreshold, ArcDirectorSystem.ClimaxRank);
        var afterClimax = DramaFixtures.Tick(state, new ArcDirectorSystem()).NewState;

        var again = DramaFixtures.Tick(afterClimax with { Phase = DayPhase.Evening }, new ArcDirectorSystem());
        Assert.Empty(again.Events.OfType<ClimaxReached>());
        Assert.Empty(again.Events.OfType<ActAdvanced>());
        Assert.Equal(CampaignAct.ActIII, again.NewState.Arc.Act);
        Assert.Equal(afterClimax.Arc.ClimaxDay, again.NewState.Arc.ClimaxDay);
    }

    [Fact]
    public void ActIII_AndClimax_CanLandOnDifferentDays()
    {
        // The core L5 claim: unlike the pre-L5 design (same tick, always), a hero can open Act III
        // on one day (reaching TerminalRank) and beat the Climax on a LATER day (reaching
        // ClimaxRank), because they are two separate graduation events in the sim.
        var openedActIII = AtEveningWithDepthAndRank(ArcDirectorSystem.ActIIFloorThreshold, ArcDirectorSystem.TerminalRank);
        var afterActIII = DramaFixtures.Tick(openedActIII, new ArcDirectorSystem()).NewState;
        Assert.Equal(CampaignAct.ActIII, afterActIII.Arc.Act);
        Assert.Equal(0, afterActIII.Arc.ClimaxDay);

        // Several days pass with no rank change: still no Climax.
        var laterSameRank = afterActIII with { Day = afterActIII.Day + 6, Phase = DayPhase.Evening };
        var stillNoClimax = DramaFixtures.Tick(laterSameRank, new ArcDirectorSystem()).NewState;
        Assert.Equal(0, stillNoClimax.Arc.ClimaxDay);
        Assert.Equal(CampaignAct.ActIII, stillNoClimax.Arc.Act);

        // The hero later graduates the terminal venue's own bottom floor (ClimaxRank): the Climax
        // fires on THIS later day, distinct from afterActIII's ActIIIStartDay.
        var hero = stillNoClimax.Heroes[1];
        var graduated = stillNoClimax with
        {
            Phase = DayPhase.Evening,
            Heroes = stillNoClimax.Heroes.SetItem(1, hero with { LadderRank = ArcDirectorSystem.ClimaxRank }),
        };
        var afterClimax = DramaFixtures.Tick(graduated, new ArcDirectorSystem());

        var climax = Assert.Single(afterClimax.Events.OfType<ClimaxReached>());
        Assert.NotNull(climax);
        Assert.Equal(graduated.Day, afterClimax.NewState.Arc.ClimaxDay);
        Assert.NotEqual(afterActIII.Arc.ActIIIStartDay, afterClimax.NewState.Arc.ClimaxDay);
    }

    [Fact]
    public void EndingFires_ExactlyAtDelay_NotBefore()
    {
        var baseState = DramaFixtures.NewWorld();
        var climaxDay = baseState.Day; // day 1
        var atThreshold = baseState with
        {
            Phase = DayPhase.Evening,
            // L5: Ending schedules off ClimaxDay, not ActIIIStartDay — set both to the same day here
            // (Act III opened, then immediately climaxed) purely so this fixture stays a minimal
            // single-day setup; ActIII_AndClimax_CanLandOnDifferentDays covers the split explicitly.
            Arc = new ArcState(CampaignAct.ActIII, ActIIStartDay: climaxDay, ActIIIStartDay: climaxDay, EndingDay: 0)
            {
                ClimaxDay = climaxDay,
            },
        };

        // One day short of the delay: no Ending yet.
        var tooSoon = atThreshold with { Day = climaxDay + ArcDirectorSystem.EndingDelayDays - 1 };
        var early = DramaFixtures.Tick(tooSoon, new ArcDirectorSystem());
        Assert.Equal(CampaignAct.ActIII, early.NewState.Arc.Act);
        Assert.Empty(early.Events.OfType<CampaignEnded>());

        // At the delay: the Ending fires exactly once.
        var due = atThreshold with { Day = climaxDay + ArcDirectorSystem.EndingDelayDays };
        var ended = DramaFixtures.Tick(due, new ArcDirectorSystem());
        Assert.Equal(CampaignAct.Ended, ended.NewState.Arc.Act);
        Assert.Equal(due.Day, ended.NewState.Arc.EndingDay);
        Assert.Single(ended.Events.OfType<CampaignEnded>());
    }

    [Fact]
    public void EndedArc_IsInert_OnFurtherTicks()
    {
        var baseState = DramaFixtures.NewWorld();
        var ended = baseState with
        {
            Phase = DayPhase.Evening,
            Arc = new ArcState(CampaignAct.Ended, ActIIStartDay: 1, ActIIIStartDay: 1, EndingDay: 6),
        };

        var result = DramaFixtures.Tick(ended, new ArcDirectorSystem());

        Assert.Equal(ended.Arc, result.NewState.Arc);
        Assert.Empty(result.Events);
    }

    [Fact]
    public void FinalChronicle_TalliesMemorialsBeatsGossipAndLegends()
    {
        var baseState = DramaFixtures.NewWorld();
        var heroWithBeats = new HeroId(1); // Torvald — alive in the starting roster
        var deadHero = new HeroId(2);

        var log = ImmutableList.Create<GameEvent>(
            new AttributionBeatEvent(BeatType.KillingBlow, new ItemId(1), heroWithBeats, 1, "x") { Id = new EventId(1), Day = 1 },
            new AttributionBeatEvent(BeatType.LethalSave, new ItemId(1), heroWithBeats, 1, "y") { Id = new EventId(2), Day = 1 },
            new AttributionBeatEvent(BeatType.BreakpointClear, new ItemId(1), heroWithBeats, 1, "z") { Id = new EventId(3), Day = 1 },
            new GossipEmitted(new EventId(1), "the tavern talks") { Id = new EventId(4), Day = 1 });

        const int deepestFloor = 5; // stand-in "deepest floor reached" payload — no longer the arc's own trigger (L5)
        var state = baseState with
        {
            Phase = DayPhase.Evening,
            EventLog = log,
            Drama = baseState.Drama with
            {
                DepthsBoard = baseState.Drama.DepthsBoard.SetItem(heroWithBeats.Value, deepestFloor),
                Memorials = ImmutableList.Create(new Memorial(deadHero, "Fallen Hero", 1, "Old Sword")),
            },
            Arc = new ArcState(CampaignAct.ActIII, ActIIStartDay: 1, ActIIIStartDay: 1, EndingDay: 0) { ClimaxDay = 1 },
        };

        var due = state with { Day = 1 + ArcDirectorSystem.EndingDelayDays };
        var result = DramaFixtures.Tick(due, new ArcDirectorSystem());

        var ending = Assert.Single(result.Events.OfType<CampaignEnded>());
        Assert.Equal(deepestFloor, ending.DeepestFloorReached);
        Assert.Equal(1, ending.MemorialCount);
        Assert.Equal(0, ending.HonoredMemorialCount); // memorial never honored in this fixture
        Assert.Equal(3, ending.AttributionBeatCount);
        Assert.Equal(1, ending.GossipHighlightCount);
        // Torvald: 3 beats >= LegendBeatThreshold, alive -> counts. The dead memorial has zero
        // beats and no signed gear -> does not count. Total legends == 1.
        Assert.Equal(1, ending.LegendaryHeroCount);
    }

    [Fact]
    public void Deterministic_SameInput_SameOutput()
    {
        GameState Run()
        {
            var state = AtEveningWithDepth(ArcDirectorSystem.ActIIFloorThreshold);
            return DramaFixtures.Tick(state, new ArcDirectorSystem()).NewState;
        }

        var a = Run();
        var b = Run();
        Assert.Equal(a.Arc, b.Arc);
        Assert.Equal(a.Rng, b.Rng); // zero new RNG — the stream position must match too
    }
}
