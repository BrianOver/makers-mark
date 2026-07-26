using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// Phase D (U-D1, gold sink 1): the forge-tier upgrade — exponential fixed gold + lock-and-key
/// Mine-floor ore, no RNG.
/// </summary>
public class ForgeTierHandlersTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static GameState Morning(int gold, string oreKey, int oreQty) =>
        GameFactory.NewGame(seed: 42) with
        {
            Player = PlayerState.NewGame(gold) with
            {
                Materials = ImmutableSortedDictionary<string, int>.Empty.SetItem(oreKey, oreQty),
            },
        };

    private static (GameState State, RejectedAction? Rejected, List<GameEvent> Events) Apply(GameState state) =>
        Apply(state, new UpgradeForgeAction());

    private static (GameState State, RejectedAction? Rejected, List<GameEvent> Events) Apply(GameState state, UpgradeForgeAction action)
    {
        var handler = new ForgeTierHandlers();
        var sink = new TestSink();
        var (next, rejected) = handler.Apply(state, action, new Pcg32(state.Rng), sink);
        return (next, rejected, sink.Events);
    }

    [Fact]
    public void UpgradeForge_IsMorningOnly()
    {
        var handler = new ForgeTierHandlers();
        var action = new UpgradeForgeAction();
        Assert.True(handler.CanHandle(action, DayPhase.Morning));
        Assert.False(handler.CanHandle(action, DayPhase.Expedition));
        Assert.False(handler.CanHandle(action, DayPhase.Camp));
        Assert.False(handler.CanHandle(action, DayPhase.ExpeditionDeep));
        Assert.False(handler.CanHandle(action, DayPhase.Evening));
    }

    [Fact]
    public void FreshSave_StartsAtForgeTierI_NoUpgradeSpent()
    {
        var state = GameFactory.NewGame(seed: 1);
        Assert.Equal(0, ForgeTierHandlers.CurrentTierIndex(state.Player));
    }

    [Fact]
    public void UpgradeToTierII_SpendsExactGoldAndOre_NoRngDraw_NoEvent()
    {
        var state = Morning(gold: 400, oreKey: "copper", oreQty: 25);

        var (after, rejected, events) = Apply(state);

        Assert.Null(rejected);
        Assert.Equal(0, after.Player.Gold);
        Assert.Equal(0, after.Player.Materials["copper"]);
        Assert.Equal(1, ForgeTierHandlers.CurrentTierIndex(after.Player));
        Assert.Equal(state.Rng, after.Rng); // zero RNG draw
        Assert.Empty(events); // matches the UnlockTalentAction no-event precedent
    }

    [Fact]
    public void CostsEscalateExponentially_ThroughAllFourUpgrades()
    {
        // 400 / 1600 / 6400 / 25600, ore floor 1..4 (copper/iron/steel/mithril), per the plan.
        var state = GameFactory.NewGame(seed: 7) with
        {
            Player = PlayerState.NewGame(startingGold: 400 + 1600 + 6400 + 25600) with
            {
                Materials = ImmutableSortedDictionary<string, int>.Empty
                    .SetItem("copper", 25).SetItem("iron", 25).SetItem("steel", 25).SetItem("mithril", 25),
            },
        };

        var expectedCosts = new[] { 400, 1600, 6400, 25600 };
        var expectedOre = new[] { "copper", "iron", "steel", "mithril" };
        var goldBefore = state.Player.Gold;

        for (var i = 0; i < 4; i++)
        {
            var (after, rejected, _) = Apply(state, new UpgradeForgeAction());
            Assert.Null(rejected);
            Assert.Equal(goldBefore - expectedCosts[i], after.Player.Gold);
            Assert.Equal(0, after.Player.Materials[expectedOre[i]]);
            Assert.Equal(i + 1, ForgeTierHandlers.CurrentTierIndex(after.Player));
            goldBefore = after.Player.Gold;
            state = after;
        }

        // Now at Forge Tier V (max) — a fifth attempt is rejected outright, whatever the wallet holds.
        var maxed = state with { Player = state.Player with { Gold = 999_999 } };
        var (finalAfter, finalRejected, _) = Apply(maxed, new UpgradeForgeAction());
        Assert.NotNull(finalRejected);
        Assert.Contains("Tier V", finalRejected.Reason);
        Assert.Equal(999_999, finalAfter.Player.Gold); // unchanged — rejection never touches state
    }

    [Fact]
    public void InsufficientOre_Rejected_NoStateChange()
    {
        var state = Morning(gold: 400, oreKey: "copper", oreQty: 10); // need 25

        var (after, rejected, events) = Apply(state);

        Assert.NotNull(rejected);
        Assert.Contains("Not enough copper", rejected.Reason);
        Assert.Equal(400, after.Player.Gold);
        Assert.Equal(10, after.Player.Materials["copper"]);
        Assert.Empty(events);
    }

    [Fact]
    public void InsufficientGold_Rejected_NoStateChange()
    {
        var state = Morning(gold: 399, oreKey: "copper", oreQty: 25);

        var (after, rejected, _) = Apply(state);

        Assert.NotNull(rejected);
        Assert.Contains("Not enough gold", rejected.Reason);
        Assert.Equal(399, after.Player.Gold);
        Assert.Equal(25, after.Player.Materials["copper"]);
    }

    [Fact]
    public void NoActionSlotsLeft_Rejected()
    {
        var state = Morning(gold: 400, oreKey: "copper", oreQty: 25) with { ActionSlotsRemaining = 0 };

        var (after, rejected, _) = Apply(state);

        Assert.NotNull(rejected);
        Assert.Contains("No action slots left", rejected.Reason);
        Assert.Equal(0, ForgeTierHandlers.CurrentTierIndex(after.Player));
    }
}
