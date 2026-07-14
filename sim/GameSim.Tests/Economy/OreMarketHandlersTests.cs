using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// Covers R6's buyback half: the player buys floor-scaled ore from returning heroes'
/// open offers during Evening. Gold moves player→hero exactly (R17 feedback loop);
/// materials move offer→player exactly; nothing is created or destroyed.
/// Offers themselves are created by U8's Evening reveal — tests seed them directly.
/// </summary>
public class OreMarketHandlersTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static Hero MakeHero(int id, int gold = 40, bool alive = true) => new(
        new HeroId(id), $"Hero{id}", HeroRole.Vanguard, Level: 1, MaxHp: 30, Gold: gold,
        GearSet.Empty, ImmutableList<ItemMemory>.Empty,
        Alive: alive, DeepestFloorReached: 0, DiedOnDay: alive ? null : 2);

    /// <summary>Evening state with one hero (H1, 40g), player 100g, and the given open offers.</summary>
    private static GameState EveningState(params OreOffered[] offers) =>
        GameFactory.NewGame(seed: 42) with
        {
            Phase = DayPhase.Evening,
            Heroes = ImmutableSortedDictionary<int, Hero>.Empty.Add(1, MakeHero(1)),
            OpenOreOffers = offers.ToImmutableList(),
        };

    private static OreOffered Offer(int heroId, string material, int quantity, int unitPrice) =>
        new(new HeroId(heroId), material, quantity, unitPrice);

    private static (GameState State, RejectedAction? Rejected, List<GameEvent> Events) Apply(
        GameState state, BuyOreAction action)
    {
        var handler = new OreMarketHandlers();
        var sink = new TestSink();
        var (next, rejected) = handler.Apply(state, action, new Pcg32(state.Rng), sink);
        return (next, rejected, sink.Events);
    }

    // ---- Phase legality --------------------------------------------------------------

    [Fact]
    public void BuyOre_IsEveningOnly()
    {
        var handler = new OreMarketHandlers();
        var action = new BuyOreAction(new HeroId(1), "iron", 1);
        Assert.True(handler.CanHandle(action, DayPhase.Evening));
        Assert.False(handler.CanHandle(action, DayPhase.Morning));
        Assert.False(handler.CanHandle(action, DayPhase.Expedition));
        Assert.False(handler.CanHandle(new StockAction(new ItemId(1), 5), DayPhase.Evening));
    }

    // ---- Happy paths -----------------------------------------------------------------

    [Fact]
    public void BuyWholeOffer_MovesGoldAndMaterialsExactly_RemovesOffer()
    {
        var state = EveningState(Offer(1, "iron", quantity: 5, unitPrice: 4));

        var (after, rejected, events) = Apply(state, new BuyOreAction(new HeroId(1), "iron", 5));

        Assert.Null(rejected);
        Assert.Empty(events); // pure transfer — the OreOffered event already exists (U8)
        Assert.Equal(80, after.Player.Gold);            // 100 - 5*4
        Assert.Equal(60, after.Heroes[1].Gold);         // 40 + 20
        Assert.Equal(5, after.Player.Materials["iron"]);
        Assert.Empty(after.OpenOreOffers);
    }

    [Fact]
    public void PartialQuantity_ReducesTheOffer_InPlace()
    {
        var state = EveningState(Offer(1, "iron", quantity: 5, unitPrice: 4));

        var (after, rejected, _) = Apply(state, new BuyOreAction(new HeroId(1), "iron", 2));

        Assert.Null(rejected);
        Assert.Equal(92, after.Player.Gold);            // 100 - 2*4
        Assert.Equal(48, after.Heroes[1].Gold);         // 40 + 8
        Assert.Equal(2, after.Player.Materials["iron"]);
        var remaining = Assert.Single(after.OpenOreOffers);
        Assert.Equal(3, remaining.Quantity);
        Assert.Equal(new HeroId(1), remaining.From);
        Assert.Equal("iron", remaining.MaterialKey);
        Assert.Equal(4, remaining.UnitPrice);
    }

    [Fact]
    public void Materials_AccumulateOntoExistingStock()
    {
        var state = EveningState(Offer(1, "iron", quantity: 3, unitPrice: 4));
        state = state with
        {
            Player = state.Player with
            {
                Materials = state.Player.Materials.SetItem("iron", 2),
            },
        };

        var (after, rejected, _) = Apply(state, new BuyOreAction(new HeroId(1), "iron", 3));

        Assert.Null(rejected);
        Assert.Equal(5, after.Player.Materials["iron"]);
    }

    // ---- Typed rejections ------------------------------------------------------------

    [Fact]
    public void Overspend_Rejected_NothingMoves()
    {
        var state = EveningState(Offer(1, "iron", quantity: 5, unitPrice: 4)) with
        {
            Player = PlayerState.NewGame(10), // cost would be 20
        };

        var (after, rejected, _) = Apply(state, new BuyOreAction(new HeroId(1), "iron", 5));

        Assert.NotNull(rejected);
        Assert.Contains("gold", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(10, after.Player.Gold);
        Assert.Equal(40, after.Heroes[1].Gold);
        Assert.False(after.Player.Materials.ContainsKey("iron"));
        Assert.Equal(5, Assert.Single(after.OpenOreOffers).Quantity);
    }

    [Fact]
    public void WrongHero_Rejected()
    {
        var state = EveningState(Offer(1, "iron", quantity: 5, unitPrice: 4));

        var (_, rejected, _) = Apply(state, new BuyOreAction(new HeroId(2), "iron", 1));

        Assert.NotNull(rejected);
        Assert.Contains("offer", rejected.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WrongMaterial_Rejected()
    {
        var state = EveningState(Offer(1, "iron", quantity: 5, unitPrice: 4));

        var (_, rejected, _) = Apply(state, new BuyOreAction(new HeroId(1), "copper", 1));

        Assert.NotNull(rejected);
        Assert.Contains("offer", rejected.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void QuantityAboveOffer_Rejected()
    {
        var state = EveningState(Offer(1, "iron", quantity: 5, unitPrice: 4));

        var (after, rejected, _) = Apply(state, new BuyOreAction(new HeroId(1), "iron", 6));

        Assert.NotNull(rejected);
        Assert.Contains("5", rejected.Reason);
        Assert.Equal(5, Assert.Single(after.OpenOreOffers).Quantity);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveQuantity_Rejected(int quantity)
    {
        var state = EveningState(Offer(1, "iron", quantity: 5, unitPrice: 4));

        var (_, rejected, _) = Apply(state, new BuyOreAction(new HeroId(1), "iron", quantity));

        Assert.NotNull(rejected);
    }

    [Fact]
    public void OfferFromADeadHero_Rejected()
    {
        // A stale offer can outlive its hero; gold must never flow to a corpse.
        var state = EveningState(Offer(1, "iron", quantity: 5, unitPrice: 4));
        state = state with
        {
            Heroes = state.Heroes.SetItem(1, MakeHero(1, alive: false)),
        };

        var (after, rejected, _) = Apply(state, new BuyOreAction(new HeroId(1), "iron", 1));

        Assert.NotNull(rejected);
        Assert.Equal(100, after.Player.Gold);
    }
}
