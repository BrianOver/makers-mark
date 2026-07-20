using System.Collections.Immutable;
using GameSim.Contracts;

namespace GameSim.Kernel;

/// <summary>
/// The deterministic heartbeat (KTD4). One <see cref="Tick"/> = one phase:
/// apply player actions (or reject them, typed), run the phase's systems in fixed
/// registration order, stamp events, advance the phase machine, log the action batch.
/// </summary>
public sealed class GameKernel
{
    private readonly ImmutableList<IPhaseSystem> _systems;
    private readonly ImmutableList<IActionHandler> _handlers;

    public GameKernel(ImmutableList<IPhaseSystem> systems, ImmutableList<IActionHandler> handlers)
    {
        _systems = systems;
        _handlers = handlers;
    }

    /// <summary>
    /// Whether some registered handler accepts <paramref name="action"/>'s type during
    /// <paramref name="phase"/> — the EXACT predicate <see cref="Tick"/> uses at step 1 before
    /// applying. Exposed so a UI can reject a phase-illegal action at INPUT time (playtest finding
    /// N3: <c>buymat</c>/<c>buyore</c>/<c>recall</c> in the wrong phase used to queue silently and
    /// only fail a full phase later at the next tick). Pure: no state change, no RNG.
    /// </summary>
    public bool Accepts(PlayerAction action, DayPhase phase) =>
        _handlers.Any(h => h.CanHandle(action, phase));

    public TickResult Tick(GameState state, ImmutableList<PlayerAction> actions)
    {
        var rng = new Pcg32(state.Rng);
        var sink = new EventCollector();
        var rejected = ImmutableList.CreateBuilder<RejectedAction>();

        // 1. Apply player actions for this phase.
        foreach (var action in actions)
        {
            var handler = _handlers.FirstOrDefault(h => h.CanHandle(action, state.Phase));
            if (handler is null)
            {
                rejected.Add(new RejectedAction(action, $"No handler accepts {action.GetType().Name} during {state.Phase}."));
                continue;
            }

            var (nextState, rejection) = handler.Apply(state, action, rng, sink);
            if (rejection is not null)
            {
                rejected.Add(rejection);
            }
            else
            {
                state = nextState;
            }
        }

        // 2. Run this phase's systems in registration order (RNG draw order contract).
        foreach (var system in _systems)
        {
            if (system.Phase == state.Phase)
            {
                state = system.Process(state, rng, sink);
            }
        }

        // 3. Stamp and append events.
        var nextEventId = state.NextEventId;
        var stamped = ImmutableList.CreateBuilder<GameEvent>();
        foreach (var raw in sink.Drain())
        {
            stamped.Add(raw with { Id = new EventId(nextEventId++), Day = state.Day });
        }

        // 4. Log the action batch, advance the phase machine, persist RNG stream.
        var (nextDay, nextPhase) = Advance(state.Day, state.Phase);
        var newState = state with
        {
            Rng = rng.Snapshot(),
            NextEventId = nextEventId,
            EventLog = state.EventLog.AddRange(stamped),
            ActionLog = state.ActionLog.Add(new LoggedBatch(state.Day, state.Phase, actions)),
            Day = nextDay,
            Phase = nextPhase,
        };

        return new TickResult(newState, stamped.ToImmutable(), rejected.ToImmutable());
    }

    // The 5-phase day (staged resolution). Camp/ExpeditionDeep sit between Expedition and Evening;
    // day ORDER is defined here, never by DayPhase's numeric value (Camp=3/ExpeditionDeep=4 append
    // after Evening=2 in the enum for save compat — KTD4).
    private static (int Day, DayPhase Phase) Advance(int day, DayPhase phase) => phase switch
    {
        DayPhase.Morning => (day, DayPhase.Expedition),
        DayPhase.Expedition => (day, DayPhase.Camp),
        DayPhase.Camp => (day, DayPhase.ExpeditionDeep),
        DayPhase.ExpeditionDeep => (day, DayPhase.Evening),
        DayPhase.Evening => (day + 1, DayPhase.Morning),
        _ => throw new InvalidOperationException($"Unknown phase {phase}"),
    };

    private sealed class EventCollector : IEventSink
    {
        private readonly List<GameEvent> _events = [];

        public void Emit(GameEvent gameEvent) => _events.Add(gameEvent);

        public IReadOnlyList<GameEvent> Drain() => _events;
    }
}
