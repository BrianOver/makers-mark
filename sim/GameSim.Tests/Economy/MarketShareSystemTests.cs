using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// Covers the market-share meter (Game-Feel Plan G3): an idle day (the player spent zero of the
/// day's action-budget slots) cedes ground to the rival; any real-work day claws it back. Pure
/// integer, no RNG — <see cref="RivalRestockSystem"/>'s discount tests cover the downstream
/// consequence this meter feeds.
/// </summary>
public class MarketShareSystemTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static (GameState State, List<GameEvent> Events) Run(GameState state)
    {
        var system = new MarketShareSystem();
        var sink = new TestSink();
        var after = system.Process(state, new Pcg32(state.Rng), sink);
        return (after, sink.Events);
    }

    [Fact]
    public void SystemContract_EveningPhase_StableName()
    {
        var system = new MarketShareSystem();
        Assert.Equal(DayPhase.Evening, system.Phase);
        Assert.Equal("market-share", system.Name);
    }

    [Fact]
    public void FullSlots_Untouched_ReadsAsIdle_RaisesRivalShare()
    {
        // ActionSlotsRemaining still at the full day's budget == nothing was spent today.
        var start = GameFactory.NewGame(seed: 1) with
        {
            ActionSlotsRemaining = ActionBudget.SlotsPerDay,
            RivalMarketSharePermille = 200,
        };

        var (after, events) = Run(start);

        Assert.Equal(200 + MarketShareSystem.IdleGainPerMille, after.RivalMarketSharePermille);
        var shift = Assert.Single(events.OfType<MarketShareShifted>());
        Assert.True(shift.RivalGained);
        Assert.Equal(after.RivalMarketSharePermille, shift.Permille);
    }

    [Theory]
    [InlineData(4)] // one slot spent
    [InlineData(0)] // every slot spent
    public void AnySlotSpent_ReadsAsActive_LowersRivalShare(int slotsRemaining)
    {
        var start = GameFactory.NewGame(seed: 1) with
        {
            ActionSlotsRemaining = slotsRemaining,
            RivalMarketSharePermille = 300,
        };

        var (after, events) = Run(start);

        Assert.Equal(300 - MarketShareSystem.ActiveRecoveryPerMille, after.RivalMarketSharePermille);
        var shift = Assert.Single(events.OfType<MarketShareShifted>());
        Assert.False(shift.RivalGained);
    }

    [Fact]
    public void IdleGain_ClampsAtOneThousand_NoOpAtTheCeiling()
    {
        var start = GameFactory.NewGame(seed: 1) with
        {
            ActionSlotsRemaining = ActionBudget.SlotsPerDay,
            RivalMarketSharePermille = 1000,
        };

        var (after, events) = Run(start);

        Assert.Equal(1000, after.RivalMarketSharePermille);
        Assert.Empty(events); // already at the clamp in the direction today would move it — no-op
    }

    [Fact]
    public void ActiveRecovery_ClampsAtZero_NoOpAtTheFloor()
    {
        var start = GameFactory.NewGame(seed: 1) with
        {
            ActionSlotsRemaining = 0,
            RivalMarketSharePermille = 0,
        };

        var (after, events) = Run(start);

        Assert.Equal(0, after.RivalMarketSharePermille);
        Assert.Empty(events);
    }

    [Fact]
    public void DrawsNoRng_TwoRunsIdentical()
    {
        var start = GameFactory.NewGame(seed: 1) with { ActionSlotsRemaining = ActionBudget.SlotsPerDay };
        var system = new MarketShareSystem();

        var rngA = new Pcg32(start.Rng);
        var a = system.Process(start, rngA, new TestSink());
        var rngB = new Pcg32(start.Rng);
        var b = system.Process(start, rngB, new TestSink());

        Assert.Equal(SaveCodec.Serialize(a), SaveCodec.Serialize(b));
        Assert.Equal(start.Rng, rngA.Snapshot());
    }
}
