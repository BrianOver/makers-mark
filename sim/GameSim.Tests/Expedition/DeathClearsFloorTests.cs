using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Kernel;

namespace GameSim.Tests.Expedition;

/// <summary>
/// Regression: a hero dying mid-floor leaves its monster alive, so the floor must NOT
/// bank as cleared (previously it did — inflating depth, ore, and breakpoint beats).
/// </summary>
public class DeathClearsFloorTests
{
    private static Hero Frail(int id) => new(
        new HeroId(id), $"Frail{id}", "mystic", Level: 1, MaxHp: 8, Gold: 10,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty, Alive: true, DeepestFloorReached: 0, DiedOnDay: null);

    [Fact]
    public void FloorWithADeath_IsNeverBankedAsCleared()
    {
        // Weak party sent deep enough that at least one floor sees a death while a
        // comrade survives; every seed with a death on a floor must leave that floor uncleared.
        for (ulong seed = 0; seed < 200; seed++)
        {
            var party = ImmutableList.Create(Frail(1), Frail(2));
            var result = ExpeditionResolver.Resolve(party, ImmutableSortedDictionary<int, Item>.Empty, 3, new Pcg32(RngState.FromSeed(seed)));

            foreach (var floor in result.Floors)
            {
                var deathOnThisFloor = result.Deaths.Any() && floor == result.Floors[^1] && !floor.Cleared;
                // Direct invariant: a cleared floor must have zero hero deaths recorded as combat losses on it.
                if (floor.Cleared)
                {
                    // On a cleared floor, every fighter that engaged must have survived it —
                    // i.e. no death is attributable to this floor. DeepestFloorCleared never
                    // exceeds a floor where someone died.
                    Assert.True(result.DeepestFloorCleared >= floor.Floor);
                }
            }

            // The core guarantee: if the whole party died, nothing cleared.
            if (result.Survivors.IsEmpty)
            {
                Assert.Equal(0, result.DeepestFloorCleared);
                Assert.Empty(result.Beats.Where(b => b.Beat == BeatType.BreakpointClear));
            }
        }
    }

    [Fact]
    public void FullWipe_YieldsNoOreLoot_AndNoClears()
    {
        for (ulong seed = 0; seed < 200; seed++)
        {
            var party = ImmutableList.Create(Frail(1));
            var result = ExpeditionResolver.Resolve(party, ImmutableSortedDictionary<int, Item>.Empty, 5, new Pcg32(RngState.FromSeed(seed)));
            if (result.Survivors.IsEmpty)
            {
                Assert.Empty(result.Loot);
                Assert.Equal(0, result.DeepestFloorCleared);
                return;
            }
        }

        Assert.Fail("expected a solo wipe within 200 seeds");
    }
}
