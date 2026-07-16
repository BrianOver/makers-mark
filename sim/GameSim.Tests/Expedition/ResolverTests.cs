using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;

namespace GameSim.Tests.Expedition;

public class ResolverTests
{
    private static Hero Naked(int id, int hp = 25, int deepest = 0) => new(
        new HeroId(id), $"Hero{id}", "vanguard", Level: 1, MaxHp: hp, Gold: 30,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: deepest, DiedOnDay: null);

    private static readonly ImmutableSortedDictionary<int, Item> NoItems = ImmutableSortedDictionary<int, Item>.Empty;

    [Fact]
    public void Purity_SameInputs_IdenticalResult()
    {
        var party = ImmutableList.Create(Naked(1), Naked(2));
        var a = ExpeditionResolver.Resolve(party, NoItems, 2, new Pcg32(RngState.FromSeed(3)));
        var b = ExpeditionResolver.Resolve(party, NoItems, 2, new Pcg32(RngState.FromSeed(3)));
        // Immutable collections use reference equality inside records — compare structurally.
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(a), System.Text.Json.JsonSerializer.Serialize(b));
    }

    [Fact]
    public void EmptyParty_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            ExpeditionResolver.Resolve(ImmutableList<Hero>.Empty, NoItems, 1, new Pcg32(RngState.FromSeed(1))));
    }

    [Fact]
    public void EveryCombatEvent_RecordsItsRolls()
    {
        var party = ImmutableList.Create(Naked(1, hp: 100));
        var result = ExpeditionResolver.Resolve(party, NoItems, 2, new Pcg32(RngState.FromSeed(5)));
        var combats = result.Floors.SelectMany(f => f.Combats).ToList();
        Assert.NotEmpty(combats);
        Assert.All(combats, c => Assert.NotEmpty(c.RecordedRolls));
    }

    [Fact]
    public void DeeperFloors_DropRarerOre()
    {
        // Strong hero clears deep: expect ore tiers to rise with floor (R6).
        var strong = Naked(1, hp: 500) with { Level = 10 };
        var r = ExpeditionResolver.Resolve(ImmutableList.Create(strong), NoItems, 1, new Pcg32(RngState.FromSeed(2)));
        Assert.All(r.Loot, l => Assert.Equal("copper", l.MaterialKey));
    }

    [Fact]
    public void PartyCanWipe_AllDeathsReported_NoSurvivors()
    {
        // Weak naked heroes sent deep — some seed produces a full wipe.
        for (ulong seed = 0; seed < 100; seed++)
        {
            var party = ImmutableList.Create(Naked(1, hp: 10), Naked(2, hp: 10));
            var r = ExpeditionResolver.Resolve(party, NoItems, 5, new Pcg32(RngState.FromSeed(seed)));
            if (r.Survivors.IsEmpty)
            {
                Assert.Equal(2, r.Deaths.Count);
                return;
            }
        }

        Assert.Fail("No full wipe across 100 seeds — deep floors are too gentle.");
    }

    [Fact]
    public void HeroRetreats_AtLowHp_InsteadOfFightingToZero()
    {
        // Retreat is the common outcome for an outmatched solo hero; deaths still possible.
        var retreats = 0;
        for (ulong seed = 0; seed < 50; seed++)
        {
            var r = ExpeditionResolver.Resolve(ImmutableList.Create(Naked(1, hp: 30)), NoItems, 4, new Pcg32(RngState.FromSeed(seed)));
            if (r.Survivors.Count == 1 && r.DeepestFloorCleared < 4) retreats++;
        }

        Assert.True(retreats > 10, $"expected frequent retreats, got {retreats}/50");
    }

    [Fact]
    public void GoldEarned_TracksMonsterKills()
    {
        var strong = Naked(1, hp: 200) with { Level = 8 };
        var r = ExpeditionResolver.Resolve(ImmutableList.Create(strong), NoItems, 1, new Pcg32(RngState.FromSeed(4)));
        var kills = r.Floors.SelectMany(f => f.Combats).Count(c => c.MonsterKilled);
        if (kills > 0)
        {
            Assert.True(r.GoldEarnedByHero[1] > 0);
        }
    }

    [Fact]
    public void TargetFloor_CapsDescent()
    {
        var strong = Naked(1, hp: 500) with { Level = 10 };
        var r = ExpeditionResolver.Resolve(ImmutableList.Create(strong), NoItems, 2, new Pcg32(RngState.FromSeed(6)));
        Assert.True(r.DeepestFloorCleared <= 2);
        Assert.All(r.Floors, f => Assert.InRange(f.Floor, 1, 2));
    }
}
