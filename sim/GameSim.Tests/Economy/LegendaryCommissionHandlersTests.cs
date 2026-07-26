using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// Phase D (U-D1, gold sink 5): the capped legendary commission — a large, one-off, forge-tier-
/// scaled gold sink that guarantees a Masterwork item outright. Zero RNG; capped at
/// <see cref="LegendaryCommissionHandlers.MaxPerCampaign"/> per campaign (the "bounded" property
/// the plan's balance gate asks for).
/// </summary>
public class LegendaryCommissionHandlersTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static GameState Ready(int gold = 3000, int copper = 4) =>
        GameFactory.NewGame(seed: 5) with
        {
            Player = PlayerState.NewGame(gold) with
            {
                Materials = ImmutableSortedDictionary<string, int>.Empty.SetItem("copper", copper),
            },
        };

    private static (GameState State, RejectedAction? Rejected, List<GameEvent> Events) Apply(
        GameState state, CommissionLegendaryWorkAction action)
    {
        var handler = new LegendaryCommissionHandlers();
        var sink = new TestSink();
        var (next, rejected) = handler.Apply(state, action, new Pcg32(state.Rng), sink);
        return (next, rejected, sink.Events);
    }

    [Fact]
    public void CommissionLegendaryWork_IsLegalInEveryPhase()
    {
        var handler = new LegendaryCommissionHandlers();
        var action = new CommissionLegendaryWorkAction("dagger", "copper");
        Assert.True(handler.CanHandle(action, DayPhase.Morning));
        Assert.True(handler.CanHandle(action, DayPhase.Evening));
    }

    [Fact]
    public void GuaranteesMasterwork_EvenAtBaseTier1MaterialGrade_NoRngDraw()
    {
        // Dagger (tier 1) with copper (grade 1) would only roll Superior at best on an ordinary
        // craft's odds — the commission guarantees Masterwork outright regardless.
        var state = Ready();

        var (after, rejected, events) = Apply(state, new CommissionLegendaryWorkAction("dagger", "copper"));

        Assert.Null(rejected);
        Assert.Equal(state.Rng, after.Rng); // zero RNG draw
        Assert.Equal(QualityGrade.Masterwork, Assert.Single(events.OfType<ItemCrafted>()).Quality);
        Assert.Equal(0, after.Player.Gold); // 3000 - 3000*(0+1)
        Assert.Equal(0, after.Player.Materials["copper"]); // dagger needs 2, x2 multiplier = 4
        Assert.Equal(1, after.Player.Materials[LegendaryCommissionHandlers.CommissionsUsedKey]);
    }

    [Fact]
    public void CostScalesWithForgeTier()
    {
        var start = Ready(gold: 6000);
        var state = start with
        {
            Player = start.Player with { Materials = start.Player.Materials.SetItem(ForgeTierHandlers.ForgeTierKey, 1) },
        }; // Forge Tier II -> cost = 3000 * (1+1) = 6000

        var (after, rejected, _) = Apply(state, new CommissionLegendaryWorkAction("dagger", "copper"));

        Assert.Null(rejected);
        Assert.Equal(0, after.Player.Gold);
    }

    [Fact]
    public void CappedAtFourPerCampaign_FifthIsRejected()
    {
        var state = Ready(gold: 3000 * 10, copper: 4 * 10);

        for (var i = 0; i < LegendaryCommissionHandlers.MaxPerCampaign; i++)
        {
            var (after, rejected, _) = Apply(state, new CommissionLegendaryWorkAction("dagger", "copper"));
            Assert.Null(rejected);
            Assert.Equal(i + 1, after.Player.Materials[LegendaryCommissionHandlers.CommissionsUsedKey]);
            state = after;
        }

        var goldBeforeFifth = state.Player.Gold;
        var (finalState, finalRejected, finalEvents) = Apply(state, new CommissionLegendaryWorkAction("dagger", "copper"));

        Assert.NotNull(finalRejected);
        Assert.Contains("already spoken for", finalRejected.Reason);
        Assert.Equal(goldBeforeFifth, finalState.Player.Gold); // the cap enforcement — gold stays bounded
        Assert.Empty(finalEvents);
    }

    [Fact]
    public void InsufficientMaterial_Rejected_NoStateChange()
    {
        var state = Ready(copper: 3); // needs 2*2 = 4

        var (after, rejected, _) = Apply(state, new CommissionLegendaryWorkAction("dagger", "copper"));

        Assert.NotNull(rejected);
        Assert.Contains("Not enough copper", rejected.Reason);
        Assert.Equal(3, after.Player.Materials["copper"]);
    }

    [Fact]
    public void InsufficientGold_Rejected()
    {
        var state = Ready(gold: 2999);

        var (_, rejected, _) = Apply(state, new CommissionLegendaryWorkAction("dagger", "copper"));

        Assert.NotNull(rejected);
        Assert.Contains("Not enough gold", rejected.Reason);
    }

    [Fact]
    public void NoActionSlotsLeft_Rejected()
    {
        var state = Ready() with { ActionSlotsRemaining = 0 };

        var (_, rejected, _) = Apply(state, new CommissionLegendaryWorkAction("dagger", "copper"));

        Assert.NotNull(rejected);
        Assert.Contains("No action slots left", rejected.Reason);
    }
}
