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

    /// <summary>The immediately-resolved actions themselves — surfaced as <see cref="AppliedThisPhase"/>.</summary>
    private readonly List<PlayerAction> _applied = [];

    /// <summary>Refusals raised by immediately-resolved actions since the last <see cref="AdvancePhase"/>,
    /// accumulated for the same reason as <see cref="_appliedThisPhase"/>.</summary>
    private readonly List<RejectedAction> _rejectedThisPhase = [];

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
    /// Workshop actions ALREADY APPLIED this phase, in submission order — the immediate-resolution
    /// counterpart to <see cref="PendingActions"/> (see <see cref="Queue"/> and
    /// <see cref="ActionTiming"/> for which verbs land here rather than in the queue). Cleared by
    /// <see cref="AdvancePhase"/> alongside the queue.
    ///
    /// <para>Exists so "what did the player just do" is answerable for BOTH halves of the split. Without
    /// it the only way to check an immediate action was to diff game state, which is a weaker assertion
    /// than reading the action itself: it can tell you gold went down, but not that the price on the
    /// StockAction was the one the price tag showed.</para>
    /// </summary>
    public IReadOnlyList<PlayerAction> AppliedThisPhase => _applied;

    /// <summary>
    /// Raised after every <see cref="AdvancePhase"/> with the phase and day that were
    /// just processed (<see cref="CurrentState"/> is already the post-tick world).
    /// The Evening completion of day N is the Ledger trigger for day N.
    /// </summary>
    public event Action<DayPhase, int>? StateChanged;

    /// <summary>
    /// U3 (loop-legibility plan, KTD-B): raised the instant <see cref="Queue"/> DEFERS an action
    /// to the bell (see <see cref="ActionTiming"/>) — never for an immediately-resolved one
    /// (<see cref="StateChanged"/> already answers those). The action is already present in
    /// <see cref="PendingActions"/> by the time this fires.
    ///
    /// <para>This is the ONE shared signal every deferred submission raises, regardless of which
    /// panel called <see cref="Queue"/> — so the bell tray and its acknowledgment toast need no
    /// per-panel wiring: <c>MainUi</c> subscribes exactly once, and a future panel that submits a
    /// fourth bell-rider gets the tray/toast for free the moment it calls <see cref="Queue"/>.</para>
    /// </summary>
    public event Action<PlayerAction>? ActionQueued;

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
            ActionQueued?.Invoke(action);
            // No-op unless MM_PLAYTEST_LOG is set (PlaytestLog.Active) — see PlaytestLog.Action's own
            // doc. This is the ONE choke point every panel's submit passes through, immediate or
            // bell-queued, so a future verb needs no call site of its own to show up in the trail.
            // U-T6-1: ActionSubject.Describe reads only the action's own fields (no sim lookup, no
            // evaluator) — the central fill-in that replaces the per-panel opt-in the doc above
            // proposed and that two days of sessions proved nobody was doing.
            PlaytestLog.Action(action.GetType().Name, immediate: false, CurrentState.Day, CurrentState.Phase,
                ActionSubject.Describe(action));
            return;
        }

        var result = _kernel.ApplyNow(CurrentState, action);
        CurrentState = result.NewState;
        _applied.Add(action);
        PlaytestLog.Action(action.GetType().Name, immediate: true, CurrentState.Day, CurrentState.Phase,
            ActionSubject.Describe(action));

        // U-T6: result.Events is THIS action's own new events only (never the phase's accumulated
        // LastEvents below) — the immediate counterpart to AdvancePhase's own DecisionEvents.LogAll
        // call, so a haggle response's CustomerWalked (an immediate-path event — see
        // GameSim.Kernel.ActionTiming) reaches the session log exactly once, not once per subsequent
        // immediate action this phase.
        DecisionEvents.LogAll(result.Events);

        // LastEvents means "everything that has happened this phase", not "whatever happened most
        // recently". Immediate actions accumulate here and AdvancePhase prepends them to the tick's
        // own events, so a consumer that only wakes on a phase boundary — the narrator, the Ledger —
        // still sees the purchases and crafts the player made during the phase. Overwriting instead
        // would silently drop them: the buy would be visible for a few frames and then vanish from
        // the record the Evening retells.
        _appliedThisPhase.AddRange(result.Events);
        LastEvents = _appliedThisPhase.ToImmutableList();

        // Refusals accumulate too, and for a sharper reason than events do: the rejection TOAST is the
        // only feedback a player gets when the sim says no. If an immediate action's refusal were
        // overwritten by the next tick's (usually empty) rejection list, the toast would appear and then
        // silently vanish at the bell — telling the player off and then hiding the evidence.
        _rejectedThisPhase.AddRange(result.Rejected);
        LastRejections = _rejectedThisPhase.ToImmutableList();
        StateChanged?.Invoke(CurrentState.Phase, CurrentState.Day);
    }

    /// <summary>Run one kernel tick with the queued batch. The queue is consumed either way.</summary>
    public TickResult AdvancePhase()
    {
        var completedPhase = CurrentState.Phase;
        var completedDay = CurrentState.Day;

        // Capture the results the Evening reveal is about to consume — the narrator retells them at
        // the Ledger, which renders the post-tick (already-cleared) state (V7b).
        //
        // U-T6: also the ONLY point that can log the typed Halt (and this expedition's shape) to the
        // session trail before ExpeditionRevealSystem clears PendingExpeditions this same tick — see
        // DecisionEvents.LogRevealed's own doc ("the reveal deletes its own evidence", §11.14.8).
        if (completedPhase == DayPhase.Evening && !CurrentState.PendingExpeditions.IsEmpty)
        {
            LastRevealedExpeditions = CurrentState.PendingExpeditions;
            LastRevealedDay = completedDay;
            DecisionEvents.LogRevealed(LastRevealedExpeditions);
        }

        var result = _kernel.Tick(CurrentState, _pending.ToImmutableList());
        _pending.Clear();
        CurrentState = result.NewState;
        // U-T6: result.Events is exactly this tick's own new events (never the phase's accumulated
        // LastEvents assembled just below), so a hero decision that fires on a real tick — the
        // Morning shopping pass, the Evening reveal's HeroDied/AttributionBeatEvent — is logged once.
        DecisionEvents.LogAll(result.Events);
        // The phase's full record: whatever the player did immediately during it, in order, then
        // whatever the tick itself produced (see Queue's immediate branch for why).
        LastEvents = _appliedThisPhase.Count == 0
            ? result.Events
            : _appliedThisPhase.ToImmutableList().AddRange(result.Events);
        _appliedThisPhase.Clear();
        _applied.Clear();
        LastRejections = _rejectedThisPhase.Count == 0
            ? result.Rejected
            : _rejectedThisPhase.ToImmutableList().AddRange(result.Rejected);
        _rejectedThisPhase.Clear();
        StateChanged?.Invoke(completedPhase, completedDay);
        return result;
    }

    /// <summary>
    /// U3 (loop-legibility plan, KTD-B): pull a still-pending action off the bell before it rings.
    /// Returns false — never a silent no-op — if <paramref name="action"/> is no longer in the
    /// queue (already withdrawn, or a tick already consumed it); callers must surface that to the
    /// player rather than let a stale withdraw button pretend to work.
    ///
    /// <para><b>Reference-based removal, deliberately not <see cref="List{T}.Remove"/>.</b> Concrete
    /// <see cref="PlayerAction"/> types are records, so two structurally-equal-but-distinct pending
    /// submissions (e.g. two separate <see cref="UpgradeForgeAction"/> clicks, which compare equal
    /// by value) must not let one chip's withdraw button remove the OTHER chip's entry. Matching by
    /// <see cref="object.ReferenceEquals"/> against the exact instance the tray chip was built from
    /// guarantees the withdrawn entry is the one the player actually clicked.</para>
    ///
    /// <para><b>Why this is determinism-free.</b> <see cref="AdvancePhase"/> is the only place
    /// <see cref="_pending"/> ever reaches the kernel (<c>_kernel.Tick(CurrentState, _pending...)</c>).
    /// Removing an action from that list before <see cref="AdvancePhase"/> runs means the kernel,
    /// the event log, and the replay/save format never observe it — there is nothing to re-baseline,
    /// because an action the kernel never saw cannot appear in its output. Two runs that differ only
    /// by a withdraw-before-the-bell are byte-identical after the tick (see
    /// <c>BellTrayTests.Withdraw_MakesTheActionNeverReachTheKernel_DeterminismFree</c>).</para>
    /// </summary>
    public bool Withdraw(PlayerAction action)
    {
        for (var i = 0; i < _pending.Count; i++)
        {
            if (ReferenceEquals(_pending[i], action))
            {
                _pending.RemoveAt(i);
                return true;
            }
        }

        return false;
    }
}
