using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// Phase D (U-D1, gold sink 3a): the coal/flux forge-supply vendor — a flat, repeatable,
/// no-RNG Morning purchase (the same shape as <see cref="MaterialVendorHandlers"/>).
/// </summary>
public class ForgeSupplyHandlersTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static GameState MorningState() => GameFactory.NewGame(seed: 42);

    private static (GameState State, RejectedAction? Rejected, List<GameEvent> Events) Apply(
        GameState state, BuyForgeSupplyAction action)
    {
        var handler = new ForgeSupplyHandlers();
        var sink = new TestSink();
        var (next, rejected) = handler.Apply(state, action, new Pcg32(state.Rng), sink);
        return (next, rejected, sink.Events);
    }

    [Fact]
    public void BuyForgeSupply_IsMorningOnly()
    {
        var handler = new ForgeSupplyHandlers();
        var action = new BuyForgeSupplyAction("coal", 1);
        Assert.True(handler.CanHandle(action, DayPhase.Morning));
        Assert.False(handler.CanHandle(action, DayPhase.Expedition));
        Assert.False(handler.CanHandle(action, DayPhase.Evening));
    }

    [Fact]
    public void BuyCoal_MovesGoldExactly_EmitsSink_NoRngDraw()
    {
        var state = MorningState();
        var (after, rejected, events) = Apply(state, new BuyForgeSupplyAction("coal", 5));

        Assert.Null(rejected);
        Assert.Equal(80, after.Player.Gold); // 100 - 5*4
        Assert.Equal(5, after.Player.Materials["coal"]);
        Assert.Equal(state.Rng, after.Rng);
        var purchase = Assert.Single(events.OfType<MaterialPurchased>());
        Assert.Equal("coal", purchase.MaterialKey);
        Assert.Equal(5, purchase.Quantity);
        Assert.Equal(20, purchase.Cost);
    }

    [Fact]
    public void BuyFlux_IsThePremiumConsumable_MovesGoldExactly()
    {
        var state = MorningState();
        var (after, rejected, events) = Apply(state, new BuyForgeSupplyAction("flux", 2));

        Assert.Null(rejected);
        Assert.Equal(20, after.Player.Gold); // 100 - 2*40
        Assert.Equal(2, after.Player.Materials["flux"]);
        Assert.Equal(80, Assert.Single(events.OfType<MaterialPurchased>()).Cost);
    }

    [Fact]
    public void Materials_AccumulateOntoExistingStock()
    {
        var start = MorningState();
        var state = start with { Player = start.Player with { Materials = start.Player.Materials.SetItem("coal", 3) } };

        var (after, rejected, _) = Apply(state, new BuyForgeSupplyAction("coal", 2));

        Assert.Null(rejected);
        Assert.Equal(5, after.Player.Materials["coal"]);
    }

    [Theory]
    [InlineData("gravel")]
    [InlineData("copper")] // a real ore key, but not a forge SUPPLY — still unstocked here
    public void UnknownSupplyKey_Rejected_NoStateChange(string key)
    {
        var (after, rejected, events) = Apply(MorningState(), new BuyForgeSupplyAction(key, 1));

        Assert.NotNull(rejected);
        Assert.Contains("does not stock", rejected.Reason);
        Assert.Equal(100, after.Player.Gold);
        Assert.True(after.Player.Materials.IsEmpty);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void NonPositiveQuantity_Rejected(int qty)
    {
        var (after, rejected, _) = Apply(MorningState(), new BuyForgeSupplyAction("coal", qty));

        Assert.NotNull(rejected);
        Assert.Contains("positive", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100, after.Player.Gold);
    }

    [Fact]
    public void Unaffordable_Rejected_NoStateChange()
    {
        var start = MorningState();
        var state = start with { Player = start.Player with { Gold = 10 } };

        var (after, rejected, events) = Apply(state, new BuyForgeSupplyAction("flux", 1)); // costs 40

        Assert.NotNull(rejected);
        Assert.Contains("Not enough gold", rejected.Reason);
        Assert.Equal(10, after.Player.Gold);
        Assert.Empty(events);
    }

    [Fact]
    public void NoActionSlotsLeft_Rejected()
    {
        var state = MorningState() with { ActionSlotsRemaining = 0 };

        var (after, rejected, _) = Apply(state, new BuyForgeSupplyAction("coal", 1));

        Assert.NotNull(rejected);
        Assert.Contains("No action slots left", rejected.Reason);
        Assert.Equal(100, after.Player.Gold);
    }
}
