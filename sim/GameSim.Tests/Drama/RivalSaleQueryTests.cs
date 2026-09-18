using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Drama;
using GameSim.Kernel;

namespace GameSim.Tests.Drama;

/// <summary>
/// P2-MEMORY-26 ("the rival takes a name"): <see cref="RivalSaleQuery"/> reads <see
/// cref="ShelfEntry.StockedDay"/> (#908's recorded fact) against a rival <see cref="ItemSold"/> —
/// state-in/state-out, no tick, no RNG. Every case builds the GameState directly rather than
/// through a full day's tick, since the query itself is the unit under test.
/// </summary>
public class RivalSaleQueryTests
{
    private static Item Player(int id, ItemSlot slot, QualityGrade quality = QualityGrade.Common) => new(
        new ItemId(id), "recipe", $"Player Item {id}", slot, quality,
        new ItemStats(Attack: 5, Defense: 0, Weight: 1),
        new MakersMark("You", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item Rival(int id, ItemSlot slot, string name = "Rival Item") => new(
        new ItemId(id), "recipe", name, slot, QualityGrade.Common,
        new ItemStats(Attack: 5, Defense: 0, Weight: 1),
        Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState Build(
        int day,
        (int Id, ItemSlot Slot, int Price, int StockedDay)[] shelf,
        ItemSold sale,
        ItemSlot rivalSlot,
        params Item[] extraItems)
    {
        var items = ImmutableSortedDictionary<int, Item>.Empty
            .Add(sale.Item.Value, Rival(sale.Item.Value, rivalSlot));
        foreach (var (id, slot, _, _) in shelf)
        {
            items = items.Add(id, Player(id, slot));
        }

        foreach (var item in extraItems)
        {
            items = items.SetItem(item.Id.Value, item);
        }

        var shelfEntries = shelf
            .Select(s => new ShelfEntry(new ItemId(s.Id), s.Price, s.StockedDay))
            .ToImmutableList();

        var state = GameFactory.NewGame(seed: 777) with
        {
            Day = day,
            Items = items,
            EventLog = ImmutableList.Create<GameEvent>(sale with { Day = day }),
        };

        return state with { Player = state.Player with { Shelf = shelfEntries } };
    }

    [Fact]
    public void StockedBeforeToday_SameSlot_YieldsOneMatch()
    {
        var sale = new ItemSold(new ItemId(90), new HeroId(1), Price: 22, FromPlayerShop: false);
        var state = Build(
            day: 5,
            shelf: [(10, ItemSlot.Weapon, 40, StockedDay: 3)],
            sale: sale,
            rivalSlot: ItemSlot.Weapon);

        var matches = RivalSaleQuery.ForDay(state, day: 5);

        var match = Assert.Single(matches);
        Assert.Equal(new HeroId(1), match.Buyer);
        Assert.Equal(new ItemId(90), match.RivalItem);
        Assert.Equal(22, match.RivalPrice);
        Assert.Equal(new ItemId(10), match.YourItem);
        Assert.Equal(40, match.YourPrice);
    }

    [Fact]
    public void StockedTheSameDay_YieldsNothing()
    {
        // The honest predicate: same-day is silence, never a guess about tick order.
        var sale = new ItemSold(new ItemId(90), new HeroId(1), Price: 22, FromPlayerShop: false);
        var state = Build(
            day: 5,
            shelf: [(10, ItemSlot.Weapon, 40, StockedDay: 5)],
            sale: sale,
            rivalSlot: ItemSlot.Weapon);

        Assert.Empty(RivalSaleQuery.ForDay(state, day: 5));
    }

    [Fact]
    public void PlayerShopSale_YieldsNothing()
    {
        var sale = new ItemSold(new ItemId(90), new HeroId(1), Price: 22, FromPlayerShop: true);
        var state = Build(
            day: 5,
            shelf: [(10, ItemSlot.Weapon, 40, StockedDay: 3)],
            sale: sale,
            rivalSlot: ItemSlot.Weapon);

        Assert.Empty(RivalSaleQuery.ForDay(state, day: 5));
    }

    [Fact]
    public void DifferentSlot_YieldsNothing()
    {
        var sale = new ItemSold(new ItemId(90), new HeroId(1), Price: 22, FromPlayerShop: false);
        var state = Build(
            day: 5,
            shelf: [(10, ItemSlot.Armor, 40, StockedDay: 3)],
            sale: sale,
            rivalSlot: ItemSlot.Weapon);

        Assert.Empty(RivalSaleQuery.ForDay(state, day: 5));
    }

    [Fact]
    public void SeveralMatchingPieces_PicksHighestQualityThenCheapestThenLowestId()
    {
        var sale = new ItemSold(new ItemId(90), new HeroId(1), Price: 22, FromPlayerShop: false);
        var items = ImmutableSortedDictionary<int, Item>.Empty
            .Add(90, Rival(90, ItemSlot.Weapon))
            .Add(10, Player(10, ItemSlot.Weapon, QualityGrade.Common))
            .Add(11, Player(11, ItemSlot.Weapon, QualityGrade.Superior))
            .Add(12, Player(12, ItemSlot.Weapon, QualityGrade.Superior));

        var state = GameFactory.NewGame(seed: 777) with
        {
            Day = 5,
            Items = items,
            EventLog = ImmutableList.Create<GameEvent>(sale with { Day = 5 }),
        };
        state = state with
        {
            Player = state.Player with
            {
                Shelf = ImmutableList.Create(
                    new ShelfEntry(new ItemId(10), 15, StockedDay: 1),  // common, cheap -- loses on quality
                    new ShelfEntry(new ItemId(11), 50, StockedDay: 1),  // superior, pricier
                    new ShelfEntry(new ItemId(12), 35, StockedDay: 1)), // superior, cheaper -- wins
            },
        };

        var match = Assert.Single(RivalSaleQuery.ForDay(state, day: 5));

        Assert.Equal(new ItemId(12), match.YourItem);
        Assert.Equal(35, match.YourPrice);
    }

    [Fact]
    public void SoldEarlierTheSameDay_NoLongerOnShelf_YieldsNothing()
    {
        // A piece sold earlier the same day is already gone from Player.Shelf by the time the
        // end-of-day state is read -- it did not "sit there" at night either way.
        var sale = new ItemSold(new ItemId(90), new HeroId(1), Price: 22, FromPlayerShop: false);
        var state = Build(
            day: 5,
            shelf: [],
            sale: sale,
            rivalSlot: ItemSlot.Weapon);

        Assert.Empty(RivalSaleQuery.ForDay(state, day: 5));
    }

    [Fact]
    public void MatchFor_MirrorsForDay_ForTheSameEvent()
    {
        var sale = new ItemSold(new ItemId(90), new HeroId(1), Price: 22, FromPlayerShop: false);
        var state = Build(
            day: 5,
            shelf: [(10, ItemSlot.Weapon, 40, StockedDay: 3)],
            sale: sale,
            rivalSlot: ItemSlot.Weapon);

        var direct = RivalSaleQuery.MatchFor(state, sale with { Day = 5 });

        Assert.NotNull(direct);
        Assert.Equal(RivalSaleQuery.ForDay(state, day: 5)[0], direct);
    }
}
