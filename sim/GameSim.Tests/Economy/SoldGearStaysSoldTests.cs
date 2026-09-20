using System.Collections.Immutable;
using GameSim.Advisor;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;
using Xunit;

namespace GameSim.Tests.Economy;

/// <summary>
/// P2-HONEST-34: a gear piece a hero has bought never returns to the shelf for free. Before this
/// unit ShopHandlers rule 3b closed only consumables; a sold sword the hero later dropped for an
/// upgrade sat in <c>GameState.Items</c> unworn and was stockable — and stocked — again. Sale history
/// is <see cref="SaleHistory"/> over the recorded log: shelf/commission <see cref="ItemSold"/> and the
/// counter's <see cref="CounterSaleClosed"/>. <see cref="ActionLegality"/> mirrors the kernel rule.
/// </summary>
public sealed class SoldGearStaysSoldTests
{
    private sealed class TestSink : IEventSink
    {
        public void Emit(GameEvent gameEvent)
        {
        }
    }

    private static readonly ItemId Sword = new(1);
    private static readonly HeroId Buyer = new(1);

    private static Item PlayerSword() => new(
        Sword, "longsword", "Longsword", ItemSlot.Weapon, QualityGrade.Fine,
        new ItemStats(Attack: 23, Defense: 0, Weight: 5),
        new MakersMark("You", CraftedOnDay: 1), ImmutableList<ItemHistoryEntry>.Empty);

    /// <summary>The sword exists, nobody wears it (the buyer already dropped it for an upgrade), and
    /// the log carries <paramref name="sale"/> if any.</summary>
    private static GameState UnwornSword(GameEvent? sale) =>
        GameFactory.NewGame(seed: 42) with
        {
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(Sword.Value, PlayerSword()),
            NextItemId = 2,
            EventLog = sale is null ? ImmutableList<GameEvent>.Empty : ImmutableList.Create(sale with { Id = new EventId(1), Day = 3 }),
        };

    private static RejectedAction? TryStock(GameState state)
    {
        var (_, rejected) = new ShopHandlers().Apply(state, new StockAction(Sword, 25), new Pcg32(state.Rng), new TestSink());
        return rejected;
    }

    [Fact]
    public void NeverSoldUnwornGear_StillStocks()
    {
        var state = UnwornSword(sale: null);

        Assert.Null(TryStock(state));
        Assert.True(ActionLegality.IsLegal(state, new StockAction(Sword, 25), DayPhase.Morning));
        Assert.False(SaleHistory.EverSold(state, Sword));
    }

    [Fact]
    public void GearSoldFromTheShelf_ThenDropped_DoesNotComeBack()
    {
        var state = UnwornSword(new ItemSold(Sword, Buyer, Price: 30, FromPlayerShop: true));

        var rejected = TryStock(state);
        Assert.NotNull(rejected);
        Assert.Contains("was already sold", rejected.Reason);
        Assert.False(ActionLegality.IsLegal(state, new StockAction(Sword, 25), DayPhase.Morning));
        Assert.True(SaleHistory.EverSold(state, Sword));
        Assert.Contains(Sword.Value, SaleHistory.SoldItemIds(state));
    }

    [Fact]
    public void GearSoldAtTheCounter_DoesNotComeBack_Either()
    {
        // The stepped counter closes its own sale event and never stamps ItemSold — a rule keyed on
        // ItemSold alone would have left the counter's sales re-shelvable.
        var state = UnwornSword(new CounterSaleClosed(Buyer, Sword, Price: 28, Pinned: true));

        var rejected = TryStock(state);
        Assert.NotNull(rejected);
        Assert.Contains("was already sold", rejected.Reason);
        Assert.False(ActionLegality.IsLegal(state, new StockAction(Sword, 25), DayPhase.Morning));
    }

    [Fact]
    public void ARivalsSale_OfADifferentItem_ClosesNothingOfYours()
    {
        var state = UnwornSword(new ItemSold(new ItemId(77), Buyer, Price: 12, FromPlayerShop: false));

        Assert.Null(TryStock(state));
        Assert.False(SaleHistory.EverSold(state, Sword));
    }
}
