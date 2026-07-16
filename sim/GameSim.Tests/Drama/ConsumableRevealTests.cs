using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Tests.Drama;

/// <summary>
/// P2 reveal bookkeeping: recorded <see cref="ConsumableUse"/>s deplete the bearer's
/// persistent <see cref="Hero.Pack"/> at the Evening reveal — the resolver only
/// records; the reveal applies.
/// </summary>
public class ConsumableRevealTests
{
    private static Item Salve(int id) => new(
        new ItemId(id), "field-salve", "Field Salve", ItemSlot.Consumable, QualityGrade.Common,
        new ItemStats(0, 0, 0), new MakersMark("You", 1),
        ImmutableList<ItemHistoryEntry>.Empty, new ConsumableEffect(ConsumableKind.Heal, 6));

    private static GameState PackHero(GameState state, int heroId, params Item[] salves)
    {
        foreach (var salve in salves)
        {
            state = DramaFixtures.WithItem(state, salve);
        }

        var hero = state.Heroes[heroId];
        return state with
        {
            Heroes = state.Heroes.SetItem(heroId, hero with
            {
                Pack = salves.Select(s => s.Id).ToImmutableList(),
            }),
        };
    }

    [Fact]
    public void Reveal_RemovesUsedSalveFromPack_LeavesUnusedStock()
    {
        var drunk = Salve(20);
        var spare = Salve(21);
        var state = PackHero(DramaFixtures.NewWorld(), heroId: 1, drunk, spare);

        var combat = DramaFixtures.Combat(1, 1, "Cave Rat", monsterKilled: true) with
        {
            Uses = ImmutableList.Create(new ConsumableUse(drunk.Id, Round: 2, HpBefore: 5, HpAfter: 11)),
        };
        var result = DramaFixtures.Result(
            party: [1], survivors: [1], deaths: [],
            floors: [new FloorOutcome(1, true, [combat])]);

        var tick = DramaFixtures.TickEvening(DramaFixtures.AtEvening(state, result));

        Assert.Equal(ImmutableList.Create(spare.Id), tick.NewState.Heroes[1].Pack);
        // The drunk salve stays in the item catalog — its maker's-mark history is permanent (R5).
        Assert.True(tick.NewState.Items.ContainsKey(drunk.Id.Value));
    }

    [Fact]
    public void Reveal_DepletesTheFallensPackToo()
    {
        // The salve was drunk either way — a death does not refund it.
        var drunk = Salve(20);
        var state = PackHero(DramaFixtures.NewWorld(), heroId: 1, drunk);

        var combat = DramaFixtures.Combat(1, 1, "Cave Rat", taken: 40) with
        {
            Uses = ImmutableList.Create(new ConsumableUse(drunk.Id, Round: 1, HpBefore: 5, HpAfter: 11)),
        };
        var result = DramaFixtures.Result(
            party: [1], survivors: [], deaths: [1],
            deepestCleared: 0,
            floors: [new FloorOutcome(1, false, [combat])]);

        var tick = DramaFixtures.TickEvening(DramaFixtures.AtEvening(state, result));

        Assert.False(tick.NewState.Heroes[1].Alive);
        Assert.Empty(tick.NewState.Heroes[1].Pack);
    }
}
