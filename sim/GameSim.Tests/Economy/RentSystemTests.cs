using GameSim.Contracts;
using GameSim.Economy;
using GameSim.Kernel;

namespace GameSim.Tests.Economy;

/// <summary>
/// Covers the guild-rent deadline heartbeat (Game-Feel Plan G3): the countdown, the on-time vs.
/// missed escalation split, the confidence gauge, and the NEVER-game-over guarantee (a missed
/// payment is a soft, legible consequence — the till never goes negative).
/// </summary>
public class RentSystemTests
{
    private sealed class TestSink : IEventSink
    {
        public List<GameEvent> Events { get; } = [];
        public void Emit(GameEvent gameEvent) => Events.Add(gameEvent);
    }

    private static (GameState State, List<GameEvent> Events) Run(GameState state)
    {
        var system = new RentSystem();
        var sink = new TestSink();
        var after = system.Process(state, new Pcg32(state.Rng), sink);
        return (after, sink.Events);
    }

    [Fact]
    public void SystemContract_MorningPhase_StableName()
    {
        var system = new RentSystem();
        Assert.Equal(DayPhase.Morning, system.Phase);
        Assert.Equal("rent", system.Name);
    }

    [Fact]
    public void NotYetDue_OnlyCountsDown_NothingElseChanges()
    {
        var start = GameFactory.NewGame(seed: 1); // RentState.Initial: DaysUntilDue = CadenceDays

        var (after, events) = Run(start);

        Assert.Empty(events);
        Assert.Equal(RentState.CadenceDays - 1, after.Rent.DaysUntilDue);
        Assert.Equal(start.Rent.AmountDueGold, after.Rent.AmountDueGold);
        Assert.Equal(start.Rent.ConfidencePermille, after.Rent.ConfidencePermille);
        Assert.Equal(start.Player.Gold, after.Player.Gold);
    }

    [Fact]
    public void DueDate_SufficientGold_PaysInFull_EscalatesModestly_RecoversConfidence()
    {
        var start = GameFactory.NewGame(seed: 1) with
        {
            Rent = new RentState(DaysUntilDue: 1, AmountDueGold: 30, MissedPayments: 0, ConfidencePermille: 900),
        };

        var (after, events) = Run(start);

        var paid = Assert.Single(events.OfType<RentPaid>());
        Assert.Equal(30, paid.AmountGold);
        Assert.Equal(start.Player.Gold - 30, after.Player.Gold);
        Assert.Equal(RentState.CadenceDays, after.Rent.DaysUntilDue); // countdown restarts
        Assert.Equal(0, after.Rent.MissedPayments);
        Assert.Equal(940, after.Rent.ConfidencePermille); // +40, capped 1000
        Assert.Equal(after.Rent.AmountDueGold, paid.NextAmountDueGold);
        Assert.True(after.Rent.AmountDueGold > 30, "an on-time payment must still escalate the next ask");
        Assert.Equal(35, after.Rent.AmountDueGold); // MulDiv(30, 1150, 1000) = round(34.5), ties away from zero -> 35
    }

    [Fact]
    public void DueDate_InsufficientGold_MissesPayment_NeverGoesNegative_EscalatesSteeply_LosesConfidence()
    {
        var start = GameFactory.NewGame(seed: 1) with
        {
            Player = GameFactory.NewGame(seed: 1).Player with { Gold = 10 }, // less than the 30g ask
            Rent = new RentState(DaysUntilDue: 1, AmountDueGold: 30, MissedPayments: 0, ConfidencePermille: 500),
        };

        var (after, events) = Run(start);

        var missed = Assert.Single(events.OfType<RentMissed>());
        Assert.Equal(30, missed.AmountDueGold);
        Assert.Equal(1, missed.MissedPayments);

        // NEVER game-over: gold is untouched (never driven negative), the player is fully playable.
        Assert.Equal(10, after.Player.Gold);
        Assert.True(after.Player.Gold >= 0);

        Assert.Equal(RentState.CadenceDays, after.Rent.DaysUntilDue); // the clock restarts regardless
        Assert.Equal(1, after.Rent.MissedPayments);
        Assert.Equal(350, after.Rent.ConfidencePermille); // -150, floored at 0
        Assert.Equal(missed.ConfidencePermille, after.Rent.ConfidencePermille);
        Assert.True(after.Rent.AmountDueGold > 30, "a missed payment must escalate the next ask");
        Assert.Equal(missed.NextAmountDueGold, after.Rent.AmountDueGold);
    }

    [Fact]
    public void MissedEscalation_IsSteeperThanOnTimeEscalation()
    {
        var onTimeStart = GameFactory.NewGame(seed: 1) with
        {
            Rent = new RentState(DaysUntilDue: 1, AmountDueGold: 100, MissedPayments: 0, ConfidencePermille: 500),
        };
        var missedStart = GameFactory.NewGame(seed: 1) with
        {
            Player = GameFactory.NewGame(seed: 1).Player with { Gold = 0 },
            Rent = new RentState(DaysUntilDue: 1, AmountDueGold: 100, MissedPayments: 0, ConfidencePermille: 500),
        };

        var (paidAfter, _) = Run(onTimeStart);
        var (missedAfter, _) = Run(missedStart);

        Assert.True(missedAfter.Rent.AmountDueGold > paidAfter.Rent.AmountDueGold,
            "missing rent must escalate the next ask MORE than paying it on time");
    }

    [Fact]
    public void Escalation_NeverExceedsMaxRentGold()
    {
        var start = GameFactory.NewGame(seed: 1) with
        {
            Player = GameFactory.NewGame(seed: 1).Player with { Gold = 0 },
            Rent = new RentState(DaysUntilDue: 1, AmountDueGold: RentSystem.MaxRentGold, MissedPayments: 5, ConfidencePermille: 0),
        };

        var (after, _) = Run(start);

        Assert.Equal(RentSystem.MaxRentGold, after.Rent.AmountDueGold);
        Assert.Equal(0, after.Rent.ConfidencePermille); // already floored — stays at 0, never negative
    }

    [Fact]
    public void ConfidenceRecovery_NeverExceedsOneThousand()
    {
        var start = GameFactory.NewGame(seed: 1) with
        {
            Rent = new RentState(DaysUntilDue: 1, AmountDueGold: 30, MissedPayments: 0, ConfidencePermille: 990),
        };

        var (after, _) = Run(start);

        Assert.Equal(1000, after.Rent.ConfidencePermille);
    }

    [Fact]
    public void DrawsNoRng_TwoRunsIdentical()
    {
        var start = GameFactory.NewGame(seed: 1) with
        {
            Rent = new RentState(DaysUntilDue: 1, AmountDueGold: 30, MissedPayments: 0, ConfidencePermille: 500),
        };
        var system = new RentSystem();

        var rngA = new Pcg32(start.Rng);
        var a = system.Process(start, rngA, new TestSink());
        var rngB = new Pcg32(start.Rng);
        var b = system.Process(start, rngB, new TestSink());

        Assert.Equal(SaveCodec.Serialize(a), SaveCodec.Serialize(b));
        Assert.Equal(start.Rng, rngA.Snapshot());
    }
}
