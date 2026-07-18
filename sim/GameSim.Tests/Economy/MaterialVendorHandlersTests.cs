using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;
using GameSim.Professions;

namespace GameSim.Tests.Economy;

/// <summary>
/// Covers the Morning materials vendor (Playable Core R2/R3): the always-available supply
/// floor. Pricing is <c>ceilDiv(quantity * unitPrice * (1000 + 250), 1000)</c> — ceiling
/// division so a single unit still carries the markup and the vendor stays strictly
/// pricier than the heroes' Evening ore offers. The cost is a recorded gold SINK
/// (<see cref="MaterialPurchased"/>, vendor purse unmodeled — TariffApplied-style KTD3);
/// GoldConservationTests reconciles it into the town-total invariant.
/// </summary>
public class MaterialVendorHandlersTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    /// <summary>Fresh day-1 Morning: player 100g, no materials (GameFactory defaults).</summary>
    private static GameState MorningState() => GameFactory.NewGame(seed: 42);

    private static (GameState State, RejectedAction? Rejected, List<GameEvent> Events) Apply(
        GameState state, BuyMaterialAction action)
    {
        var handler = new MaterialVendorHandlers();
        var sink = new TestSink();
        var (next, rejected) = handler.Apply(state, action, new Pcg32(state.Rng), sink);
        return (next, rejected, sink.Events);
    }

    /// <summary>Focused kernel: the vendor handler alone, no systems — phase legality comes
    /// from the kernel's handler dispatch, exactly as in the full composition.</summary>
    private static GameKernel VendorKernel() => new(
        ImmutableList<IPhaseSystem>.Empty,
        ImmutableList.Create<IActionHandler>(new MaterialVendorHandlers()));

    // ---- Balance pin -------------------------------------------------------------------

    [Fact]
    public void VendorMarkup_IsPinnedAt250Permille()
    {
        // Balance-flagged constant (plan 005 KTD-C): +25% over base. Changing it is a
        // deliberate re-balance, not a drive-by — this pin makes that a visible test edit.
        Assert.Equal(250, MaterialVendorHandlers.VendorMarkupPermille);
    }

    // ---- Phase legality ------------------------------------------------------------------

    [Fact]
    public void BuyMaterial_IsMorningOnly()
    {
        var handler = new MaterialVendorHandlers();
        var action = new BuyMaterialAction("copper", 1);
        Assert.True(handler.CanHandle(action, DayPhase.Morning));
        Assert.False(handler.CanHandle(action, DayPhase.Expedition));
        Assert.False(handler.CanHandle(action, DayPhase.Camp));
        Assert.False(handler.CanHandle(action, DayPhase.ExpeditionDeep));
        Assert.False(handler.CanHandle(action, DayPhase.Evening));
        Assert.False(handler.CanHandle(new StockAction(new ItemId(1), 5), DayPhase.Morning));
    }

    [Theory]
    [InlineData(DayPhase.Expedition)]
    [InlineData(DayPhase.Evening)]
    public void BuyOutsideMorning_IsRejectedByTheKernel_NothingMoves(DayPhase phase)
    {
        var state = MorningState() with { Phase = phase };

        var tick = VendorKernel().Tick(state, ImmutableList.Create<PlayerAction>(
            new BuyMaterialAction("copper", 1)));

        var rejection = Assert.Single(tick.Rejected);
        Assert.Contains("No handler accepts", rejection.Reason);
        Assert.Equal(100, tick.NewState.Player.Gold);
        Assert.True(tick.NewState.Player.Materials.IsEmpty);
        Assert.Empty(tick.Events);
    }

    // ---- Happy paths ---------------------------------------------------------------------

    [Fact]
    public void BuyFourCopper_MovesGoldExactly_EmitsOnePurchaseSink()
    {
        var (after, rejected, events) = Apply(MorningState(), new BuyMaterialAction("copper", 4));

        Assert.Null(rejected);
        Assert.Equal(85, after.Player.Gold); // 100 − ceilDiv(4·3·1250, 1000) = 100 − 15
        Assert.Equal(4, after.Player.Materials["copper"]);
        var purchase = Assert.Single(events.OfType<MaterialPurchased>());
        Assert.Equal("copper", purchase.MaterialKey);
        Assert.Equal(4, purchase.Quantity);
        Assert.Equal(15, purchase.Cost);
        Assert.Single(events); // the sink stamp is the ONLY emission
    }

    [Fact]
    public void SingleUnit_StillCarriesTheMarkup()
    {
        // ceil(1·3·1.25) = ceil(3.75) = 4 — the markup survives single-unit rounding, so
        // the vendor can never be bought at base price one unit at a time.
        var (after, rejected, events) = Apply(MorningState(), new BuyMaterialAction("copper", 1));

        Assert.Null(rejected);
        Assert.Equal(96, after.Player.Gold);
        Assert.Equal(1, after.Player.Materials["copper"]);
        Assert.Equal(4, Assert.Single(events.OfType<MaterialPurchased>()).Cost);
    }

    [Fact]
    public void TwoSelectedProfessions_BuyIronAndCopper_InOneMorningBatch()
    {
        // The vendor serves every selected profession from the one priced pool — a
        // blacksmith+tanner save buys both lines in a single Morning tick.
        var start = MorningState();
        var state = start with
        {
            Player = start.Player with
            {
                SelectedProfessions = ImmutableSortedSet.Create(
                    ProfessionRegistry.BlacksmithId, TanningProfession.Id),
            },
        };

        var tick = VendorKernel().Tick(state, ImmutableList.Create<PlayerAction>(
            new BuyMaterialAction("iron", 2),
            new BuyMaterialAction("copper", 3)));

        Assert.Empty(tick.Rejected);
        Assert.Equal(2, tick.NewState.Player.Materials["iron"]);
        Assert.Equal(3, tick.NewState.Player.Materials["copper"]);
        Assert.Equal(75, tick.NewState.Player.Gold); // 100 − ceil(12.5)=13 − ceil(11.25)=12
        Assert.Equal(2, tick.Events.OfType<MaterialPurchased>().Count());
    }

    [Fact]
    public void BuyToExactlyZeroGold_Succeeds()
    {
        var start = MorningState();
        var state = start with { Player = start.Player with { Gold = 15 } };

        var (after, rejected, _) = Apply(state, new BuyMaterialAction("copper", 4)); // costs exactly 15

        Assert.Null(rejected);
        Assert.Equal(0, after.Player.Gold);
        Assert.Equal(4, after.Player.Materials["copper"]);
    }

    [Fact]
    public void Materials_AccumulateOntoExistingStock()
    {
        var start = MorningState();
        var state = start with
        {
            Player = start.Player with
            {
                Materials = start.Player.Materials.SetItem("copper", 2),
            },
        };

        var (after, rejected, _) = Apply(state, new BuyMaterialAction("copper", 4));

        Assert.Null(rejected);
        Assert.Equal(6, after.Player.Materials["copper"]);
    }

    // ---- Typed rejections ------------------------------------------------------------------

    [Fact]
    public void Unaffordable_Rejected_StateUnchanged()
    {
        var start = MorningState();
        var state = start with { Player = start.Player with { Gold = 10 } };

        var (after, rejected, events) = Apply(state, new BuyMaterialAction("copper", 100));

        Assert.NotNull(rejected);
        Assert.Equal("Not enough gold: need 375, have 10.", rejected.Reason); // ceil(100·3·1.25) = 375
        Assert.Equal(10, after.Player.Gold);
        Assert.True(after.Player.Materials.IsEmpty);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData("electrum")] // registered (grade 6) but NOT in the priced pool — inert until re-baseline
    [InlineData("nonsense")] // never registered at all
    public void UnpricedKey_Rejected_NoStateChange(string key)
    {
        var (after, rejected, events) = Apply(MorningState(), new BuyMaterialAction(key, 1));

        Assert.NotNull(rejected);
        Assert.Contains("does not sell", rejected.Reason);
        Assert.Equal(100, after.Player.Gold);
        Assert.True(after.Player.Materials.IsEmpty);
        Assert.Empty(events);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveQuantity_Rejected(int quantity)
    {
        var (after, rejected, events) = Apply(MorningState(), new BuyMaterialAction("copper", quantity));

        Assert.NotNull(rejected);
        Assert.Contains("positive", rejected.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(100, after.Player.Gold);
        Assert.True(after.Player.Materials.IsEmpty);
        Assert.Empty(events);
    }
}
