using GameSim.Contracts;
using GameSim.Expedition;
using GameSim.Heroes;
using GameSim.Venues;

namespace GameSim.Drama;

/// <summary>
/// Morning gossip (R14): the tavern talks about YESTERDAY. This system reads the
/// previous day's <see cref="GameState.EventLog"/> entries — already stamped with
/// their <see cref="EventId"/>s by the kernel — and emits up to
/// <see cref="GossipGenerator.MaxLinesPerDay"/> templated lines citing them.
///
/// Why the day-after design: the kernel stamps event ids only AFTER a system's
/// Process returns, so gossip emitted in the same Evening as its source would have
/// to predict ids. Reading yesterday's log instead keeps R14 a lookup of real,
/// stamped ids — no coupling to the kernel's stamping arithmetic. Draws no RNG.
///
/// Campaign identity for voice/variant picks (KTD3) is <c>state.Rng.Inc</c>: the
/// Pcg32 stream increment is seed-derived, campaign-constant, and already in every
/// save. Flavor identity only — it never feeds sim rules.
///
/// Phase B (B3, R-B6): passes <see cref="RelationshipSystem.Affinity"/>, bound over the FULL
/// <c>state</c> (not just yesterday's slice — bonds/grudges/grief accrue over the whole campaign),
/// as the salience-v2 affinity lookup. Still draw-free: <c>RelationshipSystem</c> is a pure read
/// model over the already-stamped log, so this adds zero RNG and zero decisions — only WHICH
/// already-tellable line wins a slot changes.
/// </summary>
public sealed class GossipSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Morning;

    public string Name => "gossip";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        // U1 held-Morning guard (see RentSystem): fire once per calendar Morning, not once per
        // stepped-counter tick. Skip while a session is open; CounterQueueSystem runs ahead of
        // this system so the closing tick sees Closed==true and gossip is emitted once.
        if (state.Counter is { Closed: false })
        {
            return state;
        }

        var yesterday = state.Day - 1;
        if (yesterday < 1)
        {
            return state; // day 1 has no yesterday — the tavern is quiet
        }

        foreach (var gossip in GossipGenerator.Generate(
            DayLog.For(state.EventLog, yesterday),
            state.Heroes,
            state.Items,
            campaignId: state.Rng.Inc,
            affinityLookup: (a, b) => RelationshipSystem.Affinity(new HeroId(a), new HeroId(b), state),
            isDecisiveKillingBlow: beat => KillingBlowWasDecisive(state, beat)))
        {
            events.Emit(gossip);
        }

        return state;
    }

    /// <summary>P2-MEMORY-24: the same predicate the night card uses to decide a kill earned its own
    /// row — <see cref="TellingQuery.KillingBlowIsDecisive"/> over the recorded fight — so the tavern
    /// and the ledger never disagree about which kills mattered. A beat whose result is no longer
    /// held (or that never matched one) ranks as incidental.</summary>
    private static bool KillingBlowWasDecisive(GameState state, AttributionBeatEvent beatEvent)
    {
        foreach (var result in state.LastNightExpeditions)
        {
            var beat = result.Beats.FirstOrDefault(b =>
                b.Beat == beatEvent.Beat && b.Item == beatEvent.Item && b.Hero == beatEvent.Hero
                && b.Floor == beatEvent.Floor && b.Detail == beatEvent.Detail);
            if (beat is null)
            {
                continue;
            }

            var venue = VenueRegistry.All.TryGetValue(result.VenueId, out var v) ? v : VenueRegistry.Mine;
            return TellingQuery.KillingBlowIsDecisive(result, beat, state.Items, venue);
        }

        return false;
    }
}
