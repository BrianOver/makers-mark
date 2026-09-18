using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// P2-MEMORY-26's recorded fact. Stocking emits no event by design (<c>ShopHandlers</c>), so the
/// only durable record of "your piece sat on the shelf before the hero shopped" is the day stamp
/// on <see cref="ShelfEntry"/>. These pin the stamp's three properties: written on stock, kept on
/// reprice, and absent (0) for a save written before the field existed — which every real day is
/// later than, so an old save reads as "stocked before today", never as a false "stocked today".
/// </summary>
public class ShelfEntryStockedDayTests
{
    private sealed class NullSink : IEventSink
    {
        public void Emit(GameEvent gameEvent) { }
    }

    private static Item PlayerItem(int id) => new(
        new ItemId(id), "shortsword", "Test Shortsword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(Attack: 6, Defense: 0, Weight: 1),
        new MakersMark("You", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState OnDay(int day) =>
        GameFactory.NewGame(seed: 4611) with
        {
            Day = day,
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(9001, PlayerItem(9001)),
            NextItemId = 9002,
        };

    private static GameState Apply(GameState state, PlayerAction action)
    {
        var (next, rejected) = new ShopHandlers().Apply(state, action, new Pcg32(state.Rng), new NullSink());
        Assert.Null(rejected);
        return next;
    }

    [Fact]
    public void StockingAPiece_StampsTheSimDayItWentOnTheShelf()
    {
        var after = Apply(OnDay(7), new StockAction(new ItemId(9001), 40));

        var entry = Assert.Single(after.Player.Shelf);
        Assert.Equal(7, entry.StockedDay);
        Assert.Equal(40, entry.Price);
    }

    [Fact]
    public void RepricingAPiece_KeepsTheDayItWasStocked()
    {
        var stocked = Apply(OnDay(7), new StockAction(new ItemId(9001), 40));

        var repriced = Apply(stocked with { Day = 9 }, new SetPriceAction(new ItemId(9001), 25));

        var entry = Assert.Single(repriced.Player.Shelf);
        Assert.Equal(25, entry.Price);
        Assert.Equal(7, entry.StockedDay); // a reprice is not a re-shelving
    }

    [Fact]
    public void ASaveWrittenBeforeTheField_ReadsAsStockedOnDayZero()
    {
        // The exact JSON shape a pre-stamp save carries for a shelf entry: no StockedDay member.
        var json = """{"Item":{"Value":9001},"Price":40}""";

        var entry = System.Text.Json.JsonSerializer.Deserialize<ShelfEntry>(json)!;

        Assert.Equal(0, entry.StockedDay);
        Assert.True(entry.StockedDay < 1, "day 0 must read as 'before any real day', never as 'today'");
    }
}
