using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;

namespace GameSim.Expedition;

/// <summary>
/// Expedition-phase system: forms parties (U5 rules), picks each party's target floor,
/// resolves the whole expedition at DEPARTURE as a pure function (KTD5), and parks the
/// result in <see cref="GameState.PendingExpeditions"/>. The Evening reveal — applying
/// deaths, gold, ore offers, ledger cards — belongs to the drama systems (U8).
/// </summary>
public sealed class ExpeditionSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Expedition;

    public string Name => "expedition";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var parties = PartyFormation.FormParties(state.Heroes); // filters dead internally

        foreach (var partyIds in parties)
        {
            var party = partyIds.Select(id => state.Heroes[id.Value]).ToImmutableList();

            // Push one floor past the party's best prior depth, capped at the Mine's bottom (R9).
            var targetFloor = Math.Clamp(party.Max(h => h.DeepestFloorReached) + 1, 1, MonsterTable.FloorCount);

            // Influence, never orders (R18): a member who accepted a bounty commits the
            // party to that bounty's floor for the day.
            var bounty = state.Bounties.FirstOrDefault(b =>
                b.AcceptedBy is { } acceptor && partyIds.Contains(acceptor));
            if (bounty is not null)
            {
                targetFloor = bounty.TargetFloor;
            }

            var result = ExpeditionResolver.Resolve(party, state.Items, targetFloor, rng);
            state = state with { PendingExpeditions = state.PendingExpeditions.Add(result) };
            events.Emit(new PartyDeparted(partyIds, targetFloor));
        }

        return state;
    }
}
