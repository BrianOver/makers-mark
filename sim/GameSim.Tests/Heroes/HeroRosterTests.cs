using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Kernel;

namespace GameSim.Tests.Heroes;

/// <summary>Covers R7's roster half: six persistent named heroes with roles and budgets.</summary>
public class HeroRosterTests
{
    [Fact]
    public void StartingSix_ExactlySixHeroes_KeyedByIds1Through6()
    {
        var roster = HeroRoster.StartingSix();

        Assert.Equal(6, roster.Count);
        Assert.Equal(new[] { 1, 2, 3, 4, 5, 6 }, roster.Keys);
        Assert.All(roster, kv => Assert.Equal(kv.Key, kv.Value.Id.Value));
    }

    [Fact]
    public void StartingSix_RolesSplit_TwoVanguardTwoStrikerTwoMystic()
    {
        var roster = HeroRoster.StartingSix();

        Assert.Equal(2, roster.Values.Count(h => h.Role == HeroRole.Vanguard));
        Assert.Equal(2, roster.Values.Count(h => h.Role == HeroRole.Striker));
        Assert.Equal(2, roster.Values.Count(h => h.Role == HeroRole.Mystic));
    }

    [Fact]
    public void StartingSix_AllAlive_Level1_StatsInBand()
    {
        var roster = HeroRoster.StartingSix();

        Assert.All(roster.Values, h =>
        {
            Assert.True(h.Alive);
            Assert.Equal(1, h.Level);
            Assert.InRange(h.MaxHp, 20, 30);
            Assert.InRange(h.Gold, 30, 60);
            Assert.False(string.IsNullOrWhiteSpace(h.Name));
            Assert.Equal(GearSet.Empty, h.Gear);
            Assert.Empty(h.Memories);
            Assert.Equal(0, h.DeepestFloorReached);
            Assert.Null(h.DiedOnDay);
        });

        // Named, distinct personalities: names must be unique.
        Assert.Equal(6, roster.Values.Select(h => h.Name).Distinct().Count());
    }

    [Fact]
    public void StartingSix_IsDeterministic_TwoCallsIdentical()
    {
        var a = HeroRoster.StartingSix();
        var b = HeroRoster.StartingSix();

        Assert.Equal(a.Values, b.Values);
    }

    [Fact]
    public void InstallStartingRoster_SetsHeroesAndNextHeroId7()
    {
        var state = HeroRoster.InstallStartingRoster(GameFactory.NewGame(seed: 1));

        Assert.Equal(6, state.Heroes.Count);
        Assert.Equal(HeroRoster.NextHeroIdAfterRoster, state.NextHeroId);
        Assert.Equal(7, state.NextHeroId);
    }

    [Fact]
    public void CreateRecruit_IsLevel1_Alive_WithAssignedId()
    {
        var recruit = HeroRoster.CreateRecruit(nextHeroId: 7, new Pcg32(RngState.FromSeed(99)));

        Assert.Equal(7, recruit.Id.Value);
        Assert.Equal(1, recruit.Level);
        Assert.True(recruit.Alive);
        Assert.False(string.IsNullOrWhiteSpace(recruit.Name));
        Assert.InRange(recruit.MaxHp, 20, 30);
        Assert.InRange(recruit.Gold, 30, 60);
        Assert.Equal(GearSet.Empty, recruit.Gear);
    }

    [Fact]
    public void CreateRecruit_SameRngState_SameRecruit()
    {
        var a = HeroRoster.CreateRecruit(nextHeroId: 9, new Pcg32(RngState.FromSeed(2026)));
        var b = HeroRoster.CreateRecruit(nextHeroId: 9, new Pcg32(RngState.FromSeed(2026)));

        Assert.Equal(a, b);
    }
}
