using GameSim.Contracts;
using GameSim.Kernel;

namespace GameSim.Economy;

/// <summary>
/// The guild-rent deadline heartbeat (Game-Feel Plan G3, docs/design/2026-07-21-game-feel-plan.md
/// §G3): every <see cref="RentState.CadenceDays"/> Mornings the till is billed. Paying escalates the
/// NEXT ask modestly (the cost of doing business keeps rising); missing it escalates it MORE (a
/// growing-debt penalty) and lands a legible SOFT consequence — a confidence hit — never game-over.
///
/// COMPOSITION ORDER: registered in the Morning group right after <see cref="Factions.FactionDriftSystem"/>
/// and BEFORE <see cref="DestitutionRecoverySystem"/>, so a rent bill that leaves the player at a
/// true dead-end is caught and rescued THE SAME MORNING by the no-softlock floor — rent can bite,
/// but it can never brick a save.
///
/// Determinism: pure integer (<see cref="IntegerCurves.MulDiv"/> for the escalation per-mille), no
/// RNG, no wall clock, no transcendental math (CLAUDE.md rules 4-5) — inserting it changes the
/// ECONOMY (gold leaves the till on a fixed cadence, by design — this is the scarcity feature), but
/// never the RNG stream.
/// </summary>
public sealed class RentSystem : IPhaseSystem
{
    /// <summary>On-time payment escalation: +150‰ (15%) of the current ask, rounded to nearest.</summary>
    public const int OnTimeEscalationPerMille = 150;

    /// <summary>Missed-payment escalation: +350‰ (35%) — a steeper growing-debt penalty.</summary>
    public const int MissedEscalationPerMille = 350;

    /// <summary>A cap so escalation cannot run away across a long campaign.</summary>
    public const int MaxRentGold = 500;

    /// <summary>Confidence lost per missed payment (permille), floored at 0.</summary>
    public const int MissedConfidencePenalty = 150;

    /// <summary>Confidence regained per on-time payment (permille), capped at 1000.</summary>
    public const int PaidConfidenceRecovery = 40;

    public DayPhase Phase => DayPhase.Morning;

    public string Name => "rent";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var rent = state.Rent;
        var daysLeft = rent.DaysUntilDue - 1;
        if (daysLeft > 0)
        {
            return state with { Rent = rent with { DaysUntilDue = daysLeft } };
        }

        // Due today.
        if (state.Player.Gold >= rent.AmountDueGold)
        {
            var nextAmount = EscalatedAmount(rent.AmountDueGold, OnTimeEscalationPerMille);
            var nextConfidence = Math.Min(1000, rent.ConfidencePermille + PaidConfidenceRecovery);
            var paid = rent.AmountDueGold;

            events.Emit(new RentPaid(paid, nextAmount));

            return state with
            {
                Player = state.Player with { Gold = state.Player.Gold - paid },
                Rent = new RentState(RentState.CadenceDays, nextAmount, rent.MissedPayments, nextConfidence),
            };
        }

        // Missed: no gold moves (never drive the till negative — DestitutionRecoverySystem, which
        // runs immediately after this system, rescues a true dead-end this same Morning).
        var missedNextAmount = EscalatedAmount(rent.AmountDueGold, MissedEscalationPerMille);
        var missedConfidence = Math.Max(0, rent.ConfidencePermille - MissedConfidencePenalty);
        var missedCount = rent.MissedPayments + 1;

        events.Emit(new RentMissed(rent.AmountDueGold, missedNextAmount, missedCount, missedConfidence));

        return state with
        {
            Rent = new RentState(RentState.CadenceDays, missedNextAmount, missedCount, missedConfidence),
        };
    }

    private static int EscalatedAmount(int current, int escalationPerMille)
    {
        var raised = (int)IntegerCurves.MulDiv(current, 1000 + escalationPerMille, 1000);
        return Math.Min(MaxRentGold, Math.Max(current, raised));
    }
}
