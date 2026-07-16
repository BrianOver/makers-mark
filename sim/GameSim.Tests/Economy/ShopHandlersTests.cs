using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// Covers R16's player half: stock, reprice, unstock — each with typed rejections.
/// Stocking is NOT a sale: no events are emitted by any of these handlers; the only
/// shop event is <see cref="ItemSold"/>, emitted by the shopping system at purchase.
/// </summary>
public class ShopHandlersTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Item PlayerItem(int id, ItemSlot slot = ItemSlot.Weapon) => new(
        new ItemId(id), "longsword", "Longsword", slot, QualityGrade.Fine,
        new ItemStats(Attack: 23, Defense: 0, Weight: 5),
        new MakersMark("You", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);

    private static Item RivalItem(int id) => new(
        new ItemId(id), "rival-blade-1", "Traveler's Sword", ItemSlot.Weapon, QualityGrade.Common,
        new ItemStats(Attack: 9, Defense: 0, Weight: 4),
        Mark: null, ImmutableList<ItemHistoryEntry>.Empty);

    private static GameState BaseState(params Item[] items) =>
        GameFactory.NewGame(seed: 42) with
        {
            Items = items.ToImmutableSortedDictionary(i => i.Id.Value, i => i),
            NextItemId = items.Length == 0 ? 1 : items.Max(i => i.Id.Value) + 1,
        };

    private static (GameState State, RejectedAction? Rejected, List<GameEvent> Events) Apply(
        GameState state, PlayerAction action)
    {
        var handler = new ShopHandlers();
        var sink = new TestSink();
        var (next, rejected) = handler.Apply(state, action, new Pcg32(state.Rng), sink);
        return (next, rejected, sink.Events);
    }

    // ---- Phase legality --------------------------------------------------------------

    [Fact]
    public void ShopManagement_IsLegalInAllThreePhases()
    {
        // The shop stays manageable while heroes are away — stocking/pricing during the
        // Expedition crafting window is exactly the intended play pattern.
        var handler = new ShopHandlers();
        foreach (var phase in new[] { DayPhase.Morning, DayPhase.Expedition, DayPhase.Evening })
        {
            Assert.True(handler.CanHandle(new StockAction(new ItemId(1), 10), phase));
            Assert.True(handler.CanHandle(new SetPriceAction(new ItemId(1), 10), phase));
            Assert.True(handler.CanHandle(new UnstockAction(new ItemId(1)), phase));
        }
    }

    [Fact]
    public void ForeignActions_AreNotHandled()
    {
        var handler = new ShopHandlers();
        Assert.False(handler.CanHandle(new CraftAction("longsword", "iron"), DayPhase.Morning));
        Assert.False(handler.CanHandle(new BuyOreAction(new HeroId(1), "iron", 1), DayPhase.Evening));
    }

    // ---- StockAction -----------------------------------------------------------------

    [Fact]
    public void Stock_HappyPath_ShelvesTheItem_NoEvents()
    {
        var state = BaseState(PlayerItem(1));

        var (after, rejected, events) = Apply(state, new StockAction(new ItemId(1), 25));

        Assert.Null(rejected);
        Assert.Empty(events); // stocking isn't a sale
        var entry = Assert.Single(after.Player.Shelf);
        Assert.Equal(new ItemId(1), entry.Item);
        Assert.Equal(25, entry.Price);
    }

    [Fact]
    public void Stock_UnknownItem_Rejected()
    {
        var (after, rejected, _) = Apply(BaseState(), new StockAction(new ItemId(99), 25));

        Assert.NotNull(rejected);
        Assert.Contains("I99", rejected.Reason);
        Assert.Empty(after.Player.Shelf);
    }

    [Fact]
    public void Stock_RivalItem_Rejected_NotPlayerCrafted()
    {
        var state = BaseState(RivalItem(1));

        var (after, rejected, _) = Apply(state, new StockAction(new ItemId(1), 25));

        Assert.NotNull(rejected);
        Assert.Contains("player-crafted", rejected.Reason);
        Assert.Empty(after.Player.Shelf);
    }

    [Fact]
    public void Stock_AlreadyShelved_Rejected()
    {
        var state = BaseState(PlayerItem(1)) with
        {
            Player = PlayerState.NewGame(100) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(1), 25)),
            },
        };

        var (after, rejected, _) = Apply(state, new StockAction(new ItemId(1), 30));

        Assert.NotNull(rejected);
        Assert.Contains("already", rejected.Reason);
        Assert.Single(after.Player.Shelf); // unchanged
        Assert.Equal(25, after.Player.Shelf[0].Price);
    }

    [Fact]
    public void Stock_ItemEquippedByAHero_Rejected()
    {
        var item = PlayerItem(1);
        var hero = new Hero(
            new HeroId(1), "Torvald", "vanguard", Level: 1, MaxHp: 30, Gold: 40,
            GearSet.Empty with { Weapon = item.Id }, ImmutableList<ItemMemory>.Empty,
            Alive: true, DeepestFloorReached: 0, DiedOnDay: null);
        var state = BaseState(item) with
        {
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, hero),
        };

        var (after, rejected, _) = Apply(state, new StockAction(item.Id, 25));

        Assert.NotNull(rejected);
        Assert.Contains("equipped", rejected.Reason);
        Assert.Contains("Torvald", rejected.Reason);
        Assert.Empty(after.Player.Shelf);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Stock_NonPositivePrice_Rejected(int price)
    {
        var state = BaseState(PlayerItem(1));

        var (after, rejected, _) = Apply(state, new StockAction(new ItemId(1), price));

        Assert.NotNull(rejected);
        Assert.Contains("price", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(after.Player.Shelf);
    }

    // ---- SetPriceAction --------------------------------------------------------------

    [Fact]
    public void SetPrice_HappyPath_UpdatesTheEntry()
    {
        var state = BaseState(PlayerItem(1)) with
        {
            Player = PlayerState.NewGame(100) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(1), 200)),
            },
        };

        var (after, rejected, events) = Apply(state, new SetPriceAction(new ItemId(1), 10));

        Assert.Null(rejected);
        Assert.Empty(events);
        var entry = Assert.Single(after.Player.Shelf);
        Assert.Equal(10, entry.Price);
    }

    [Fact]
    public void SetPrice_NotShelved_Rejected()
    {
        var state = BaseState(PlayerItem(1));

        var (_, rejected, _) = Apply(state, new SetPriceAction(new ItemId(1), 10));

        Assert.NotNull(rejected);
        Assert.Contains("not on the shelf", rejected.Reason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void SetPrice_NonPositivePrice_Rejected(int price)
    {
        var state = BaseState(PlayerItem(1)) with
        {
            Player = PlayerState.NewGame(100) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(1), 25)),
            },
        };

        var (after, rejected, _) = Apply(state, new SetPriceAction(new ItemId(1), price));

        Assert.NotNull(rejected);
        Assert.Equal(25, after.Player.Shelf[0].Price); // unchanged
    }

    // ---- UnstockAction ---------------------------------------------------------------

    [Fact]
    public void Unstock_HappyPath_RemovesEntry_ItemSurvivesInWorld()
    {
        var state = BaseState(PlayerItem(1)) with
        {
            Player = PlayerState.NewGame(100) with
            {
                Shelf = ImmutableList.Create(new ShelfEntry(new ItemId(1), 25)),
            },
        };

        var (after, rejected, events) = Apply(state, new UnstockAction(new ItemId(1)));

        Assert.Null(rejected);
        Assert.Empty(events);
        Assert.Empty(after.Player.Shelf);
        Assert.True(after.Items.ContainsKey(1)); // the item itself is not destroyed
    }

    [Fact]
    public void Unstock_NotShelved_Rejected()
    {
        var state = BaseState(PlayerItem(1));

        var (_, rejected, _) = Apply(state, new UnstockAction(new ItemId(1)));

        Assert.NotNull(rejected);
        Assert.Contains("not on the shelf", rejected.Reason);
    }
}
