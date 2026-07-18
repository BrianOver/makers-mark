using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;
using GameSim.Venues;

namespace GameSim.Expedition;

/// <summary>
/// Expedition-phase system (staged resolution, verdict §5 step 4): forms parties (U5 rules),
/// picks each party's target floor, and resolves STAGE 1 (floors [1..checkpoint]) at DEPARTURE as
/// a pure function (KTD5). A party that clears every stage-1 floor with nobody dead and nobody too
/// hurt PARKS as an <see cref="InFlightExpedition"/> and a <see cref="PartyCampReport"/> is emitted
/// (the winch-house slate); any other stage-1 ending finalises immediately into
/// <see cref="GameState.PendingExpeditions"/>. Stage 2 runs at the ExpeditionDeep tick
/// (<see cref="ExpeditionDeepSystem"/>). The Evening reveal — applying deaths, gold, ore offers,
/// ledger cards — belongs to the drama systems (U8).
/// </summary>
public sealed class ExpeditionSystem : IPhaseSystem
{
    // THE tuning knob (kill-risk 2). Step-0 histogram (20 seeds × 100 days, recounted from the
    // runs/ corpus 2026-07-17): deaths by floor 1/2/3/4 = 59/182/191/25 (n=457). 87.1% of deaths
    // happen ABOVE floor 1, i.e. in stage 2, after the camp window. Deepening this to 2 puts the
    // modal death floor (3, with 2 close behind) partly before the window; do that only if
    // post-staging telemetry shows deaths migrating deeper. Depth-scaling (e.g. target-3 late game)
    // is a v2 data-tuning PR.
    internal const int CampCheckpointDepth = 1;

    /// <summary>The stage-1 checkpoint floor for a target: camp sits below this floor. Clamped so
    /// the checkpoint can never equal the target — <c>checkpoint &lt; 1</c> means an unstaged
    /// expedition (target floor 1: the whole run resolves at the Expedition tick as today).</summary>
    internal static int CheckpointFor(int targetFloor) => Math.Min(CampCheckpointDepth, targetFloor - 1);

    public DayPhase Phase => DayPhase.Expedition;

    public string Name => "expedition";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var parties = PartyFormation.FormParties(state.Heroes); // filters dead internally

        // The Mine is the only LIVE venue (P4 live-venue contract: VenueRegistry.LiveRotation).
        var venue = VenueRegistry.Mine;

        foreach (var partyIds in parties)
        {
            var party = partyIds.Select(id => state.Heroes[id.Value]).ToImmutableList();

            // Push one floor past the party's best prior depth, capped at the venue's bottom (R9).
            var targetFloor = Math.Clamp(party.Max(h => h.DeepestFloorReached) + 1, 1, venue.FloorCount);

            // Influence, never orders (R18): a member who accepted a bounty commits the
            // party to that bounty's floor for the day.
            var bounty = state.Bounties.FirstOrDefault(b =>
                b.AcceptedBy is { } acceptor && partyIds.Contains(acceptor));
            if (bounty is not null)
            {
                targetFloor = bounty.TargetFloor;
            }

            var checkpoint = CheckpointFor(targetFloor);
            if (checkpoint < 1)
            {
                // Unstaged (target floor 1): resolve the whole run now, park in PendingExpeditions.
                var result = ExpeditionResolver.Resolve(party, state.Items, venue, targetFloor, rng);
                state = state with { PendingExpeditions = state.PendingExpeditions.Add(result) };
            }
            else
            {
                var (completed, inFlight) = ExpeditionResolver.ResolveStage1(
                    party, state.Items, venue, targetFloor, checkpoint, rng);
                if (completed is not null)
                {
                    // Stage-1 wipe / gate / floor-lost / too-hurt: finalise now, no camp report.
                    state = state with { PendingExpeditions = state.PendingExpeditions.Add(completed) };
                }
                else
                {
                    // Cleared stage 1 clean: camp below the checkpoint, decide at the Camp tick.
                    state = state with { InFlight = state.InFlight.Add(inFlight!) };
                    events.Emit(BuildCampReport(inFlight!, state.Items));
                }
            }

            events.Emit(new PartyDeparted(partyIds, targetFloor));
        }

        return state;
    }

    /// <summary>The winch-house slate: current HP and the count of Heal consumables left in each
    /// camped hero's working pack — the facts the player decides send/recall/hold on. Never lists a
    /// dead hero (the v1 park invariant guarantees none).</summary>
    private static PartyCampReport BuildCampReport(InFlightExpedition inFlight, ImmutableSortedDictionary<int, Item> items)
    {
        var healsLeft = inFlight.Packs.ToImmutableSortedDictionary(
            kv => kv.Key,
            kv => kv.Value.Count(id => items.TryGetValue(id.Value, out var item) && item.Effect is { Kind: ConsumableKind.Heal }));

        return new PartyCampReport(
            inFlight.Party,
            inFlight.CheckpointFloor,
            inFlight.TargetFloor,
            inFlight.Hp,
            healsLeft);
    }
}
