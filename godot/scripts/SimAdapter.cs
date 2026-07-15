using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using GameSim;
using GameSim.Contracts;
using GameSim.Kernel;

namespace GodotClient;

/// <summary>
/// The ONE bridge between the Godot presentation layer and the pure sim (KTD2).
/// Owns the campaign <see cref="GameState"/> and the composed <see cref="GameKernel"/>
/// (built through <see cref="GameComposition"/> ONLY, so a seed means the same world
/// everywhere). Panels render <see cref="CurrentState"/> and submit
/// <see cref="PlayerAction"/>s through <see cref="Queue"/>; <see cref="AdvancePhase"/>
/// applies the queued batch via one kernel Tick. No game rules live here — pure
/// delegation. Deliberately plain C# (zero Godot types) so adapter fidelity is
/// testable without an engine runtime.
/// </summary>
public sealed class SimAdapter
{
    private readonly GameKernel _kernel = GameComposition.BuildKernel();
    private readonly List<PlayerAction> _pending = [];

    public SimAdapter(ulong seed) => CurrentState = GameComposition.NewCampaign(seed);

    /// <summary>The world as of the last tick. Immutable — render freely.</summary>
    public GameState CurrentState { get; private set; }

    /// <summary>Events stamped by the most recent <see cref="AdvancePhase"/>.</summary>
    public ImmutableList<GameEvent> LastEvents { get; private set; } = ImmutableList<GameEvent>.Empty;

    /// <summary>Actions the kernel refused on the most recent tick — typed reasons, never a silent drop.</summary>
    public ImmutableList<RejectedAction> LastRejections { get; private set; } = ImmutableList<RejectedAction>.Empty;

    /// <summary>Actions queued for the next <see cref="AdvancePhase"/>, in submission order.</summary>
    public IReadOnlyList<PlayerAction> PendingActions => _pending;

    /// <summary>
    /// Raised after every <see cref="AdvancePhase"/> with the phase and day that were
    /// just processed (<see cref="CurrentState"/> is already the post-tick world).
    /// The Evening completion of day N is the Ledger trigger for day N.
    /// </summary>
    public event Action<DayPhase, int>? StateChanged;

    /// <summary>Queue a player action for the next tick. Phase legality is the kernel's call, not ours.</summary>
    public void Queue(PlayerAction action) => _pending.Add(action);

    /// <summary>Run one kernel tick with the queued batch. The queue is consumed either way.</summary>
    public TickResult AdvancePhase()
    {
        var completedPhase = CurrentState.Phase;
        var completedDay = CurrentState.Day;
        var result = _kernel.Tick(CurrentState, _pending.ToImmutableList());
        _pending.Clear();
        CurrentState = result.NewState;
        LastEvents = result.Events;
        LastRejections = result.Rejected;
        StateChanged?.Invoke(completedPhase, completedDay);
        return result;
    }
}
