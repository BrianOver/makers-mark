using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;

namespace GameSim.Tests.Drama;

/// <summary>
/// P2-MEMORY-23: "famous" counts decisive deeds, not every beat. Three cave rats never made a legend;
/// they only ever tripped a threshold that counted beats of any kind.
/// </summary>
public class LegendQueryDecisiveTests
{
    private static readonly HeroId Torvald = new(1);
    private static readonly ItemId Emberbite = new(10);

    private static AttributionBeatEvent Beat(BeatType kind, int id, bool decisive) =>
        new(kind, Emberbite, Torvald, Floor: 2, $"beat {id}", Decisive: decisive) { Id = new EventId(id), Day = 1 };

    private static GameState WithLog(params AttributionBeatEvent[] beats) =>
        GameFactory.NewGame(seed: 42) with { EventLog = beats.ToImmutableList<GameEvent>() };

    [Fact]
    public void ThreeIncidentalKills_DoNotMakeAHeroFamous_ButStillCountAsBeats()
    {
        var state = WithLog(
            Beat(BeatType.KillingBlow, 1, decisive: false),
            Beat(BeatType.KillingBlow, 2, decisive: false),
            Beat(BeatType.KillingBlow, 3, decisive: false));

        Assert.Equal(3, LegendQuery.AttributionBeatCount(state, Torvald));
        Assert.Equal(0, LegendQuery.LegendDeedCount(state, Torvald));
        Assert.False(LegendQuery.IsFamousDead(state, Torvald));
    }

    [Fact]
    public void ThreeDecisiveKills_AreStillNotALegend_KillsAreTheJob()
    {
        // 22 killing blows a night, 96% of them "decisive" by the counterfactual - counting them,
        // every hero is famous by day 5. A kill stays tellable for gossip; it is not a legend's deed.
        var state = WithLog(
            Beat(BeatType.KillingBlow, 1, decisive: true),
            Beat(BeatType.KillingBlow, 2, decisive: true),
            Beat(BeatType.KillingBlow, 3, decisive: true));

        Assert.Equal(0, LegendQuery.LegendDeedCount(state, Torvald));
        Assert.False(LegendQuery.IsFamousDead(state, Torvald));
    }

    [Fact]
    public void ALethalSaveALifesaveAndABreakpoint_MakeAHeroFamous()
    {
        var state = WithLog(
            Beat(BeatType.LethalSave, 1, decisive: true),
            Beat(BeatType.KillingBlow, 2, decisive: true),
            Beat(BeatType.PotionLifesave, 3, decisive: true),
            Beat(BeatType.BreakpointClear, 4, decisive: true));

        Assert.Equal(3, LegendQuery.LegendDeedCount(state, Torvald));
        Assert.True(LegendQuery.LegendDeedCount(state, Torvald) >= LegendQuery.FamousBeatThreshold);
        Assert.True(LegendQuery.IsFamousDead(state, Torvald));
    }

    [Fact]
    public void DecisiveCount_IsPerHero()
    {
        var other = new HeroId(2);
        var state = WithLog(
            Beat(BeatType.LethalSave, 1, decisive: true),
            new AttributionBeatEvent(BeatType.LethalSave, Emberbite, other, 2, "theirs", Decisive: true) { Id = new EventId(2), Day = 1 });

        Assert.Equal(1, LegendQuery.LegendDeedCount(state, Torvald));
        Assert.Equal(1, LegendQuery.LegendDeedCount(state, other));
    }

    [Fact]
    public void IsLegendDeed_ReadsTheRecordedStamp_AndNeverAKill()
    {
        Assert.True(LegendQuery.IsLegendDeed(Beat(BeatType.LethalSave, 1, decisive: true)));
        Assert.False(LegendQuery.IsLegendDeed(Beat(BeatType.LethalSave, 2, decisive: false)));
        Assert.False(LegendQuery.IsLegendDeed(Beat(BeatType.KillingBlow, 3, decisive: true)));
    }
}
