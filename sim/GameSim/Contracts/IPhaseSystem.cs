using System.Collections.Immutable;

namespace GameSim.Contracts;

/// <summary>
/// A sim module's hook into the day loop. The kernel invokes systems in a FIXED
/// registration order every phase — that order is part of the determinism contract
/// (single RNG stream, KTD4): reordering systems changes every outcome for a seed.
/// Systems are pure: consume state + rng, return new state, emit events via the sink.
/// </summary>
public interface IPhaseSystem
{
    /// <summary>Which phase this system acts in.</summary>
    DayPhase Phase { get; }

    /// <summary>Stable name for diagnostics and ordering audits.</summary>
    string Name { get; }

    /// <summary>Advance the world for this phase. Player actions were already applied by the kernel.</summary>
    GameState Process(GameState state, IDeterministicRng rng, IEventSink events);
}

/// <summary>Collects events during a tick; the kernel assigns ids and appends to the log.</summary>
public interface IEventSink
{
    /// <summary>Record an event. The kernel stamps <see cref="GameEvent.Id"/> and <see cref="GameEvent.Day"/>.</summary>
    void Emit(GameEvent gameEvent);
}

/// <summary>
/// Applies player actions for the current phase before systems run. The kernel owns
/// dispatch; modules register one handler per action type they accept.
/// </summary>
public interface IActionHandler
{
    /// <summary>Action types this handler accepts, and in which phases.</summary>
    bool CanHandle(PlayerAction action, DayPhase phase);

    /// <summary>Apply the action or reject it with a typed reason.</summary>
    (GameState State, RejectedAction? Rejected) Apply(GameState state, PlayerAction action, IDeterministicRng rng, IEventSink events);
}
