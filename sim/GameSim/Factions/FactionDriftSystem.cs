using GameSim.Contracts;

namespace GameSim.Factions;

/// <summary>
/// Morning faction drift (P5 U2, R5/KTD5): every non-neutral standing steps one
/// <see cref="FactionDefinition.DriftStep"/> toward neutral (0), never past it — a standing whose
/// magnitude is below one step snaps straight to 0. Neglect erodes earned standing back to neutral;
/// buying the faction's ore (<c>OreMarketHandlers</c>) is the only riser (KTD8: standing never goes
/// below 0 in this core, so drift only ever steps DOWN, but the step is sign-symmetric for the
/// dormant negative half).
///
/// COMPOSITION ORDER (contract): register this system FIRST in the Morning group in
/// <c>GameComposition.BuildKernel</c>. Running drift before any Morning system that reads standing
/// makes the drifted value the settled standing for the whole day. No current Morning system reads
/// standing, so the slot is behaviorally inert today, but pinning it first is load-bearing the moment
/// a future Morning system does (deferred hero-side drama).
///
/// Determinism: pure integer, draws NO RNG — the kernel stream is untouched across a Morning tick
/// with drift active (this daily-state change, not a dice roll, is why the Balance bands move
/// deliberately in U5). No floats, no transcendental <c>Math.*</c>, no wall clock.
/// </summary>
public sealed class FactionDriftSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Morning;

    public string Name => "faction-drift";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var standing = state.Player.Standing;
        if (standing is null || standing.Count == 0)
        {
            return state; // nothing earned yet — neutral everywhere, no work and no map to grow
        }

        var drifted = standing;
        foreach (var (factionId, value) in standing)
        {
            if (value == 0)
            {
                continue; // already neutral — leave the entry as-is
            }

            // Faction params drive the step size. Standing is only ever written for a registered
            // faction, so an unresolved id would be malformed data — skip it rather than guess.
            if (!FactionRegistry.TryGet(factionId, out var faction) || faction is null)
            {
                continue;
            }

            var stepped = StepTowardZero(value, faction.DriftStep);

            // P5 U4 (R9/KTD7): drift only lowers standing, so it can only cross the favored EXIT
            // boundary DOWNWARD (cooled). Emit a stamped FactionStandingShifted the flavor engine
            // renders. Runs once per Morning, so at most one cooled beat per faction per day-cycle;
            // the display name rides in on the event (no registry lookup in the renderer, KTD7). This
            // stays pure integer — no RNG (KTD5).
            if (FactionStandingThresholds.Crossing(faction, value, stepped) is { } direction)
            {
                events.Emit(new FactionStandingShifted(faction.Id, faction.DisplayName, direction));
            }

            drifted = drifted.SetItem(factionId, stepped);
        }

        return ReferenceEquals(drifted, standing)
            ? state
            : state with { Player = state.Player with { Standing = drifted } };
    }

    /// <summary>
    /// Move <paramref name="value"/> one <paramref name="step"/> toward 0 without overshooting: a
    /// magnitude below one step snaps to 0. Sign-symmetric (the negative half is dormant in this
    /// core, KTD8). Pure integer.
    /// </summary>
    private static int StepTowardZero(int value, int step) =>
        value > 0 ? Math.Max(value - step, 0) : Math.Min(value + step, 0);
}
