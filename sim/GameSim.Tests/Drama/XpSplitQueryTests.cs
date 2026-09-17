using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Heroes;

namespace GameSim.Tests.Drama;

using static DramaFixtures;

/// <summary>
/// P2-PROOF-17: <see cref="XpSplitQuery"/>'s one load-bearing invariant is
/// <see cref="XpSplitQuery.Split.Total"/> never disagreeing with <see cref="HeroXp.ForExpedition"/> —
/// a number on screen that could contradict the number the sim actually applied is worse than no
/// number. Every case here is state-in/record-out: no tick, no RNG draw, no clock.
/// </summary>
public class XpSplitQueryTests
{
    private const int Hero = 1;
    private const int Other = 2;
    private static readonly ItemId Blade = new(10);
    private static readonly ItemId Shield = new(11);

    [Fact]
    public void For_NonSurvivor_ReturnsNull()
    {
        var result = Result(party: [Hero], survivors: [], deaths: [Hero]);

        Assert.Null(XpSplitQuery.For(result, new HeroId(Hero)));
    }

    [Fact]
    public void For_HeroNotInThisExpeditionAtAll_ReturnsNull()
    {
        var result = Result(party: [Other], survivors: [Other], deaths: []);

        Assert.Null(XpSplitQuery.For(result, new HeroId(Hero)));
    }

    [Fact]
    public void For_SurvivorWithNoBeats_IsSurviveAndFloorOnly()
    {
        var result = Result(party: [Hero], survivors: [Hero], deaths: [], deepestCleared: 2);

        var split = XpSplitQuery.For(result, new HeroId(Hero));

        Assert.NotNull(split);
        Assert.Equal(HeroXp.SurviveXp, split!.SurviveXp);
        Assert.Equal(2, split.FloorsCleared);
        Assert.Equal(2 * HeroXp.PerFloorXp, split.FloorXp);
        Assert.Equal(0, split.CreditedBeats);
        Assert.Equal(0, split.BeatXp);
    }

    [Fact]
    public void For_SurvivorWithZeroFloorsCleared_OmitsFloorShare()
    {
        var result = Result(party: [Hero], survivors: [Hero], deaths: [], deepestCleared: 0);

        var split = XpSplitQuery.For(result, new HeroId(Hero));

        Assert.NotNull(split);
        Assert.Equal(0, split!.FloorsCleared);
        Assert.Equal(0, split.FloorXp);
        Assert.Equal(HeroXp.SurviveXp, split.Total);
    }

    [Fact]
    public void For_CreditsOnlyKillingBlowAndLethalSaveBeats_ForThisHero()
    {
        var beats = new[]
        {
            new AttributionBeat(BeatType.KillingBlow, Blade, new HeroId(Hero), Floor: 1, Detail: "kill"),
            new AttributionBeat(BeatType.LethalSave, Shield, new HeroId(Hero), Floor: 1, Detail: "save"),
            // Not credited: wrong hero, and beat types HeroXp never pays for.
            new AttributionBeat(BeatType.KillingBlow, Blade, new HeroId(Other), Floor: 1, Detail: "someone else's kill"),
            new AttributionBeat(BeatType.Provisioned, Blade, new HeroId(Hero), Floor: 1, Detail: "provisioned"),
            new AttributionBeat(BeatType.PotionLifesave, Blade, new HeroId(Hero), Floor: 1, Detail: "potion"),
            new AttributionBeat(BeatType.BreakpointClear, Blade, new HeroId(Hero), Floor: 1, Detail: "gate"),
        };
        var result = Result(party: [Hero, Other], survivors: [Hero, Other], deaths: [], deepestCleared: 1, beats: beats);

        var split = XpSplitQuery.For(result, new HeroId(Hero));

        Assert.NotNull(split);
        Assert.Equal(2, split!.CreditedBeats);
        Assert.Equal(2 * HeroXp.PerBeatXp, split.BeatXp);
    }

    [Fact]
    public void For_RivalArmedSurvivor_GetsTheHonestZeroBeatShare()
    {
        // Beats are only ever emitted for player-crafted items (AttributionEngine.IsPlayerCrafted) —
        // a hero who fought entirely on rival iron simply has no beats in result.Beats at all.
        var result = Result(party: [Hero], survivors: [Hero], deaths: [], deepestCleared: 3, beats: []);

        var split = XpSplitQuery.For(result, new HeroId(Hero));

        Assert.NotNull(split);
        Assert.Equal(0, split!.CreditedBeats);
        Assert.Equal(0, split.BeatXp);
        Assert.Equal(HeroXp.ForExpedition(3, 0), split.Total);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(3, 0)]
    [InlineData(0, 2)]
    [InlineData(2, 3)]
    [InlineData(5, 1)]
    public void Total_NeverDisagreesWithHeroXpForExpedition(int deepestCleared, int creditedBeatCount)
    {
        var beats = Enumerable.Range(0, creditedBeatCount)
            .Select(i => new AttributionBeat(
                i % 2 == 0 ? BeatType.KillingBlow : BeatType.LethalSave,
                Blade, new HeroId(Hero), Floor: 1, Detail: $"beat-{i}"))
            .ToArray();
        var result = Result(party: [Hero], survivors: [Hero], deaths: [], deepestCleared: deepestCleared, beats: beats);

        var split = XpSplitQuery.For(result, new HeroId(Hero));

        Assert.NotNull(split);
        Assert.Equal(HeroXp.ForExpedition(deepestCleared, creditedBeatCount), split!.Total);
    }
}
