using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Venues;

namespace GameSim.Expedition;

/// <summary>
/// ExpeditionDeep-phase system (staged resolution, verdict §5 step 4): finalises every
/// <see cref="GameState.InFlight"/> party parked at the Expedition tick. For each in-flight
/// expedition — in list order — it fetches the party's heroes by stored id order (safe: nothing
/// mutates gear/MaxHp/Alive between the ticks; the Camp tick touches <c>Hero.Pack</c> only, and the
/// working pack lives in <see cref="InFlightExpedition.Packs"/>), resolves stage 2
/// (floors [checkpoint+1..target]) on the LIVE kernel stream, and appends the finished
/// <see cref="ExpeditionResult"/> to <see cref="GameState.PendingExpeditions"/> for the Evening
/// reveal. Stage-2 rolls are drawn HERE — provably undrawn while the party camped, since the
/// parked record carries no RNG state (KTD4: the kernel stream is the single authority).
///
/// Emits nothing: the town learns the outcome at the Evening reveal; the narrator (U5) reads the
/// recorded floor data. Clears <see cref="GameState.InFlight"/> when done.
/// </summary>
public sealed class ExpeditionDeepSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.ExpeditionDeep;

    public string Name => "expedition-deep";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        foreach (var inFlight in state.InFlight)
        {
            var party = inFlight.Party.Select(id => state.Heroes[id.Value]).ToImmutableList();
            var venue = VenueRegistry.Require(inFlight.VenueId);

            var result = ExpeditionResolver.ResolveStage2(inFlight, party, state.Items, venue, rng);
            state = state with { PendingExpeditions = state.PendingExpeditions.Add(result) };
        }

        return state with { InFlight = ImmutableList<InFlightExpedition>.Empty };
    }
}
