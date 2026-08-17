using System.Collections.Immutable;
using GameSim.Contracts;
using GameSim.Heroes;

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

    /// <summary>
    /// Applies ONE action right now and returns the result — no phase systems, no phase advance.
    ///
    /// <para><b>Why this exists.</b> <see cref="Tick"/> is indivisible: it applies actions, runs the
    /// phase's systems, AND advances the day/phase machine. That made every player action wait for the
    /// bell, so spending 2 copper still read as 6 copper, and the tutorial (which watches the events a
    /// tick produces) looked frozen after the player had already done the thing it asked for. Brian's
    /// playtest, 2026-07-30: "since the crafts are queued (shouldn't be), the material list is
    /// confusing as it doesn't update".</para>
    ///
    /// <para>So the workshop verbs — the ones that are the player's own two hands (buy, craft, stock,
    /// reprice) — resolve immediately through here, while genuinely phase-scale commitments (posting a
    /// bounty, sending the party into the mine) still ride the bell through <see cref="Tick"/>. See
    /// <see cref="ActionTiming"/> for the split and the reasoning behind it.</para>
    ///
    /// <para><b>Determinism is preserved</b> (KTD4/KTD5): same seed + same action sequence still gives
    /// a byte-identical world, because this walks the SAME handler-selection predicate, draws from the
    /// SAME single RNG stream, and persists the stream snapshot exactly as <see cref="Tick"/> does. What
    /// changes is the ORDER of draws relative to a phase's systems within that phase — which is why
    /// this is a separate entry point rather than a change to <see cref="Tick"/>: every determinism,
    /// golden-replay, and balance test drives <see cref="Tick"/> directly with batched actions and is
    /// therefore bit-for-bit unaffected by this method existing.</para>
    ///
    /// <para>Phase legality is still the handler's call, exactly as in <see cref="Tick"/> — an action
    /// illegal this phase comes back as a <see cref="RejectedAction"/> and changes nothing.</para>
    /// </summary>
    public TickResult ApplyNow(GameState state, PlayerAction action)
    {
        var rng = new Pcg32(state.Rng);
        var sink = new EventCollector();
        var rejected = ImmutableList.CreateBuilder<RejectedAction>();

        var handler = _handlers.FirstOrDefault(h => h.CanHandle(action, state.Phase));
        if (handler is null)
        {
            rejected.Add(new RejectedAction(action, $"No handler accepts {action.GetType().Name} during {state.Phase}."));
        }
        else
        {
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

        var nextEventId = state.NextEventId;
        var stamped = ImmutableList.CreateBuilder<GameEvent>();
        foreach (var raw in sink.Drain())
        {
            stamped.Add(raw with { Id = new EventId(nextEventId++), Day = state.Day });
        }

        // Deliberately NOT touched, unlike Tick: Day, Phase, Counter teardown, and the
        // ActionSlotsRemaining reset all belong to the phase machine, and this is not a phase
        // boundary. The RNG snapshot and the action log ARE persisted, because the draws really
        // happened and the action really was taken — a save reloaded mid-phase must reflect both.
        var newState = state with
        {
            Rng = rng.Snapshot(),
            NextEventId = nextEventId,
            EventLog = state.EventLog.AddRange(stamped),
            ActionLog = state.ActionLog.Add(new LoggedBatch(state.Day, state.Phase, ImmutableList.Create(action))),
        };

        return new TickResult(newState, stamped.ToImmutable(), rejected.ToImmutable())
        {
            Traces = sink.DrainTraces().ToImmutableList(),
        };
    }

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
        var (nextDay, nextPhase) = Advance(state.Day, state.Phase, state.Counter, state.Heroes);
        var newState = state with
        {
            Rng = rng.Snapshot(),
            NextEventId = nextEventId,
            EventLog = state.EventLog.AddRange(stamped),
            ActionLog = state.ActionLog.Add(new LoggedBatch(state.Day, state.Phase, actions)),
            Day = nextDay,
            Phase = nextPhase,
            // PA3/PKD5: the ONLY place a stepped counter session is torn down — the instant the day
            // actually leaves Morning (whether closed by CloseCounterAction or by queue exhaustion),
            // never mid-hold. A run that never opens the counter has state.Counter null the whole
            // time, so this line is a no-op for it (the atomic-equivalence pin).
            Counter = state.Phase == DayPhase.Morning && nextPhase != DayPhase.Morning ? null : state.Counter,
            // Game-Feel Plan G3: the ONLY place the day's action-slot budget resets — the instant
            // Day actually increments (Evening -> Morning), mirroring the Counter-teardown precedent
            // above. Every other tick within the same calendar day (including a held Morning while a
            // counter session is open) leaves it untouched, so slots persist correctly across the
            // 5-phase day.
            ActionSlotsRemaining = nextDay != state.Day ? ActionBudget.SlotsPerDay : state.ActionSlotsRemaining,
        };

        return new TickResult(newState, stamped.ToImmutable(), rejected.ToImmutable())
        {
            Traces = sink.DrainTraces().ToImmutableList(),
        };
    }

    // The 5-phase day (staged resolution). Camp/ExpeditionDeep sit between Expedition and Evening;
    // day ORDER is defined here, never by DayPhase's numeric value (Camp=3/ExpeditionDeep=4 append
    // after Evening=2 in the enum for save compat — KTD4).
    //
    // PA3/PKD5: an open, unfinished counter session HOLDS the day at Morning instead of advancing
    // to Expedition.
    //
    // 2026-08-02 loop-legibility widening (KTD-D(1), the phase-collapse rule): the SECOND
    // state-aware case — Morning's completion folds straight to Evening, skipping
    // Expedition/Camp/ExpeditionDeep entirely, when nobody will go underground today (see
    // NoRaidToHost). Every other transition is the verbatim original switch (a run with Counter
    // null AND at least one alive hero — the default, and the ONLY path BaselinePlayer/the
    // balance gate ever exercise — takes the exact same branches it always did, byte-identical).
    private static (int Day, DayPhase Phase) Advance(int day, DayPhase phase, CounterState? counter, ImmutableSortedDictionary<int, Hero> heroes) => phase switch
    {
        DayPhase.Morning when counter is { Closed: false } => (day, DayPhase.Morning),
        DayPhase.Morning when NoRaidToHost(heroes) => (day, DayPhase.Evening),
        DayPhase.Morning => (day, DayPhase.Expedition),
        DayPhase.Expedition => (day, DayPhase.Camp),
        DayPhase.Camp => (day, DayPhase.ExpeditionDeep),
        DayPhase.ExpeditionDeep => (day, DayPhase.Evening),
        DayPhase.Evening => (day + 1, DayPhase.Morning),
        _ => throw new InvalidOperationException($"Unknown phase {phase}"),
    };

    /// <summary>
    /// KTD-D(1): true when nobody will go underground today — a pure, zero-RNG, zero-clock function
    /// of the roster as it stands after Morning's own systems have already run (this is called from
    /// <see cref="Advance"/> with that post-systems <c>state.Heroes</c>). Reuses
    /// <see cref="PartyFormation.FormParties"/> — the EXACT predicate
    /// <see cref="Expedition.ExpeditionSystem"/> itself uses two ticks later to decide whether any
    /// party departs, and the same one <see cref="MusterSystem"/> already calls this same Morning to
    /// predict it (byte-match proven by
    /// <c>MusterSystemTests.PredictedRoster_ByteMatches_ExpeditionSystem_Over100Days</c>) — so this
    /// collapse check can never disagree with what Expedition would actually have done. Every alive
    /// hero always joins SOME party (even a solo leftover — <see cref="PartyFormation"/>'s own doc
    /// comment), so this is true only when there is not one single living hero: the true
    /// "nobody-down-there" case (KTD-D(4)) — a recalled or fully-resolved-unstaged party is NOT this
    /// case (both still "went down"), so Expedition/Camp/ExpeditionDeep are unaffected once any party
    /// forms at all.
    /// </summary>
    private static bool NoRaidToHost(ImmutableSortedDictionary<int, Hero> heroes) =>
        PartyFormation.FormParties(heroes).IsEmpty;

    /// <summary>
    /// U-T6: the kernel's own sink implements <see cref="ITraceSink"/> alongside the required
    /// <see cref="IEventSink"/> — see that interface's doc for why this is the ONLY place a trace
    /// can actually reach <see cref="TickResult.Traces"/> (every test-local sink stub is a plain
    /// <see cref="IEventSink"/> and simply drops a <c>Trace</c> call it never receives, having no
    /// method to receive it on).
    /// </summary>
    private sealed class EventCollector : IEventSink, ITraceSink
    {
        private readonly List<GameEvent> _events = [];
        private readonly List<DecisionTrace> _traces = [];

        public void Emit(GameEvent gameEvent) => _events.Add(gameEvent);

        public void Trace(DecisionTrace trace) => _traces.Add(trace);

        public IReadOnlyList<GameEvent> Drain() => _events;

        public IReadOnlyList<DecisionTrace> DrainTraces() => _traces;
    }
}
