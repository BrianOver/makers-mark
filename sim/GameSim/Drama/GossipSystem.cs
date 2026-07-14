using GameSim.Contracts;

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
/// </summary>
public sealed class GossipSystem : IPhaseSystem
{
    public DayPhase Phase => DayPhase.Morning;

    public string Name => "gossip";

    public GameState Process(GameState state, IDeterministicRng rng, IEventSink events)
    {
        var yesterday = state.Day - 1;
        if (yesterday < 1)
        {
            return state; // day 1 has no yesterday — the tavern is quiet
        }

        foreach (var gossip in GossipGenerator.Generate(DayLog.For(state.EventLog, yesterday), state.Heroes, state.Items))
        {
            events.Emit(gossip);
        }

        return state;
    }
}
