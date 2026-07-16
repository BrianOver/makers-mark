using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Tests.Heroes;

/// <summary>Covers R17's budget half (loot income) and R7's per-item memory.</summary>
public class HeroOpsTests
{
    private static Hero MakeHero(int gold = 40) => new(
        new HeroId(1), "Testa", "striker", Level: 1, MaxHp: 25, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    [Fact]
    public void ApplyLootIncome_GrowsBudget()
    {
        var hero = HeroOps.ApplyLootIncome(MakeHero(gold: 40), 25);

        Assert.Equal(65, hero.Gold);
    }

    [Fact]
    public void ApplyLootIncome_ZeroIsANoOp()
    {
        var hero = HeroOps.ApplyLootIncome(MakeHero(gold: 40), 0);

        Assert.Equal(40, hero.Gold);
    }

    [Fact]
    public void ApplyLootIncome_NegativeGold_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HeroOps.ApplyLootIncome(MakeHero(), -5));
    }

    [Fact]
    public void RecordItemMemory_AppendsNewEntry()
    {
        var hero = HeroOps.RecordItemMemory(MakeHero(), new ItemId(9), kills: 2, saves: 1);

        var memory = Assert.Single(hero.Memories);
        Assert.Equal(new ItemId(9), memory.Item);
        Assert.Equal(2, memory.Kills);
        Assert.Equal(1, memory.Saves);
    }

    [Fact]
    public void RecordItemMemory_AccumulatesOntoExistingEntry()
    {
        var hero = HeroOps.RecordItemMemory(MakeHero(), new ItemId(9), kills: 2, saves: 1);
        hero = HeroOps.RecordItemMemory(hero, new ItemId(9), kills: 3, saves: 0);

        var memory = Assert.Single(hero.Memories);
        Assert.Equal(5, memory.Kills);
        Assert.Equal(1, memory.Saves);
    }

    [Fact]
    public void RecordItemMemory_DistinctItems_GetDistinctEntries()
    {
        var hero = HeroOps.RecordItemMemory(MakeHero(), new ItemId(9), kills: 1, saves: 0);
        hero = HeroOps.RecordItemMemory(hero, new ItemId(10), kills: 0, saves: 2);

        Assert.Equal(2, hero.Memories.Count);
        Assert.Equal(new ItemId(9), hero.Memories[0].Item);
        Assert.Equal(new ItemId(10), hero.Memories[1].Item);
    }
}
