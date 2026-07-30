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

    /// <summary>Events raised by immediately-resolved actions since the last <see cref="AdvancePhase"/>
    /// — see <see cref="Queue"/>'s immediate branch for why they accumulate rather than overwrite.</summary>
    private readonly List<GameEvent> _appliedThisPhase = [];

    public SimAdapter(ulong seed) => CurrentState = GameComposition.NewCampaign(seed);

    /// <summary>
    /// Scenario injection (U12 engine tests / future replay loading): adopt a prepared
    /// campaign state as-is. Same kernel, same determinism — only the starting world differs.
    /// </summary>
    public SimAdapter(GameState initialState) => CurrentState = initialState;

    /// <summary>The world as of the last tick. Immutable — render freely.</summary>
    public GameState CurrentState { get; private set; }

    /// <summary>Events stamped by the most recent <see cref="AdvancePhase"/>.</summary>
    public ImmutableList<GameEvent> LastEvents { get; private set; } = ImmutableList<GameEvent>.Empty;

    /// <summary>Actions the kernel refused on the most recent tick — typed reasons, never a silent drop.</summary>
    public ImmutableList<RejectedAction> LastRejections { get; private set; } = ImmutableList<RejectedAction>.Empty;

    /// <summary>
    /// The expeditions the most recent Evening tick revealed, snapshotted from
    /// <see cref="GameState.PendingExpeditions"/> BEFORE that tick ran. The Evening reveal
    /// (<c>ExpeditionRevealSystem</c>) consumes and clears PendingExpeditions and the event log
    /// carries no <c>FloorOutcome</c>/<c>CombatEvent</c> stream, so this snapshot is the ONLY place
    /// the post-tick Evening Ledger can retell the day through <c>ExpeditionNarrator</c> (V7b).
    /// Empty until the first Evening tick with pending results.
    /// </summary>
    public ImmutableList<ExpeditionResult> LastRevealedExpeditions { get; private set; } = ImmutableList<ExpeditionResult>.Empty;

    /// <summary>The day whose expeditions <see cref="LastRevealedExpeditions"/> holds (0 = none yet).</summary>
    public int LastRevealedDay { get; private set; }

    /// <summary>Actions queued for the next <see cref="AdvancePhase"/>, in submission order.</summary>
    public IReadOnlyList<PlayerAction> PendingActions => _pending;

    /// <summary>
    /// Raised after every <see cref="AdvancePhase"/> with the phase and day that were
    /// just processed (<see cref="CurrentState"/> is already the post-tick world).
    /// The Evening completion of day N is the Ledger trigger for day N.
    /// </summary>
    public event Action<DayPhase, int>? StateChanged;

    /// <summary>
    /// Submit a player action. Workshop verbs (see <see cref="ActionTiming"/>) resolve IMMEDIATELY;
    /// everything else queues for the next <see cref="AdvancePhase"/>. Phase legality is the kernel's
    /// call either way, not ours.
    ///
    /// <para>Everything used to queue, which meant the world lied to the player: spend 2 copper and the
    /// material list still read 6 until the bell, and the tutorial — which watches the events a tick
    /// produces — looked frozen after the player had already done what it asked. Brian's playtest,
    /// 2026-07-30. The method keeps the name <c>Queue</c> because every call site's INTENT is unchanged
    /// ("submit this action"); only the timing of the workshop subset moved.</para>
    ///
    /// <para>An immediately-resolved action updates <see cref="CurrentState"/>, <see cref="LastEvents"/>
    /// and <see cref="LastRejections"/> on the spot and raises <see cref="StateChanged"/> with the
    /// CURRENT phase/day — nothing completed, so the phase and day are the ones still in progress. Any
    /// listener that treats StateChanged as "a phase just ended" must therefore compare against its own
    /// last-seen phase rather than assuming a boundary; <c>MainUi</c> already refreshes idempotently.</para>
    /// </summary>
    public void Queue(PlayerAction action)
    {
        if (!ActionTiming.ResolvesImmediately(action))
        {
            _pending.Add(action);
            return;
        }

        var result = _kernel.ApplyNow(CurrentState, action);
        CurrentState = result.NewState;

        // LastEvents means "everything that has happened this phase", not "whatever happened most
        // recently". Immediate actions accumulate here and AdvancePhase prepends them to the tick's
        // own events, so a consumer that only wakes on a phase boundary — the narrator, the Ledger —
        // still sees the purchases and crafts the player made during the phase. Overwriting instead
        // would silently drop them: the buy would be visible for a few frames and then vanish from
        // the record the Evening retells.
        _appliedThisPhase.AddRange(result.Events);
        LastEvents = _appliedThisPhase.ToImmutableList();
        LastRejections = result.Rejected;
        StateChanged?.Invoke(CurrentState.Phase, CurrentState.Day);
    }

    /// <summary>Run one kernel tick with the queued batch. The queue is consumed either way.</summary>
    public TickResult AdvancePhase()
    {
        var completedPhase = CurrentState.Phase;
        var completedDay = CurrentState.Day;

        // Capture the results the Evening reveal is about to consume — the narrator retells them at
        // the Ledger, which renders the post-tick (already-cleared) state (V7b).
        if (completedPhase == DayPhase.Evening && !CurrentState.PendingExpeditions.IsEmpty)
        {
            LastRevealedExpeditions = CurrentState.PendingExpeditions;
            LastRevealedDay = completedDay;
        }

        var result = _kernel.Tick(CurrentState, _pending.ToImmutableList());
        _pending.Clear();
        CurrentState = result.NewState;
        // The phase's full record: whatever the player did immediately during it, in order, then
        // whatever the tick itself produced (see Queue's immediate branch for why).
        LastEvents = _appliedThisPhase.Count == 0
            ? result.Events
            : _appliedThisPhase.ToImmutableList().AddRange(result.Events);
        _appliedThisPhase.Clear();
        LastRejections = result.Rejected;
        StateChanged?.Invoke(completedPhase, completedDay);
        return result;
    }
}
