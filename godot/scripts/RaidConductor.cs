using System;
using GameSim.Contracts;

namespace GodotClient;

/// <summary>
/// U1 (plan 2026-08-03-001, KTD-A "the two-bell day"): sequences the raid span — Expedition, Camp,
/// ExpeditionDeep — as a show the player watches, instead of three bells they crank with nothing to
/// decide (the owner's repeated "the loop is not complete" + "lower into the mine has them
/// return???"). Plain C# (<see cref="PhaseClock"/>'s own testability idiom): depends only on <see
/// cref="SimAdapter"/>/<see cref="PhaseClock"/> plus two injected, Godot-free completion predicates,
/// so the whole state machine is unit-testable without an engine runtime.
///
/// <para><b>Beats:</b> <c>Idle -&gt; SendOff -&gt; (stage-1 tick) -&gt; VigilStop? -&gt; DeepTick -&gt;
/// (deep tick) -&gt; Homecoming -&gt; Idle</c>. <see cref="Current"/> is DERIVED from the adapter's own
/// phase on every real tick (<see cref="Resync"/>, wired to <see cref="SimAdapter.StateChanged"/> in
/// the constructor) rather than purely tracked — so any caller that ticks the phase directly,
/// bypassing this conductor entirely (every existing engine test that drives phases via
/// <c>Adapter.AdvancePhase()</c> does exactly this), can never leave it stale: the very next real
/// tick — however it was caused — re-derives the correct beat from scratch.</para>
///
/// <para><b>The one real stop:</b> <see cref="Beat.VigilStop"/> fires only when a party parked
/// (<c>state.InFlight</c> non-empty) after the stage-1 tick — <see cref="Update"/> holds it
/// INDEFINITELY, no timer, until <see cref="ResolveVigil"/> is called (wired to the camp slate's
/// third verb, "Send them deeper"). Every other beat is a show with a pinned maximum: its completion
/// predicate is checked every <see cref="Update"/>, but a stuck (or simply absent — the Mirror is
/// optional, ambient content) predicate can never hang the day, because the pinned max always wins
/// eventually. <b>Day-1 reality check:</b> every hero's first-ever trip targets floor 1, which is
/// structurally unstaged (<c>ExpeditionSystem.CheckpointFor</c>: checkpoint &lt; 1 whenever the
/// target floor is 1) — so <see cref="Beat.VigilStop"/> is the UNCOMMON case, not the common one, and
/// <see cref="Update"/>'s empty-InFlight path must carry every day (not just day 1) through Camp and
/// ExpeditionDeep on its own with zero stop.</para>
///
/// <para><b>Ticking:</b> every phase advance below rides <see cref="PhaseClock.AdvanceNow"/> — the
/// SAME path the bell and the opt-in auto-clock use — so replay format and RNG draw order are
/// provably unchanged (zero <c>sim/</c> diff).</para>
/// </summary>
public sealed class RaidConductor
{
    /// <summary>SendOff's pinned max: <c>Town2D</c>'s departure dwell (1.0s) plus a handful of
    /// heroes' file-stagger (0.35s each) — generous over the real cascade so a normal-sized party's
    /// send-off always finishes on its own condition, never the backstop.</summary>
    public const double SendOffMaxSeconds = 6.0;

    /// <summary>An auto-continued empty beat — nobody parked, nothing to show. The plan's own
    /// phrase: "an empty Camp/Deep costs ~a second of show, not a click."</summary>
    public const double EmptyBeatSeconds = 1.0;

    /// <summary>The deep-tick show's pinned max when a party genuinely presses on — long enough to
    /// glance at the Mirror/mine-watch strip (ambient, optional content — there is no completion
    /// predicate for it, only this duration), never long enough to feel like a wait.</summary>
    public const double DeepShowSeconds = 3.0;

    /// <summary>Homecoming's pinned max — covers <c>Town2D.MinDelveShowSeconds</c> (8s) plus the
    /// file-stagger walk-in, with headroom.</summary>
    public const double HomecomingMaxSeconds = 12.0;

    /// <summary>The conductor's own beats. <see cref="Idle"/> covers both Morning and Evening — the
    /// two phases that keep a real bell, because the player has phase-specific verbs there (KTD-A's
    /// presentation contract: "a phase is player-operated iff the player has phase-specific verbs in
    /// it").</summary>
    public enum Beat
    {
        Idle,
        SendOff,
        VigilStop,
        DeepTick,
        Homecoming,
    }

    private readonly SimAdapter _adapter;
    private readonly PhaseClock _clock;
    private readonly Func<bool> _departureShowDone;
    private readonly Func<bool> _homecomingShowDone;

    private double _elapsed;

    public RaidConductor(SimAdapter adapter, PhaseClock clock, Func<bool> departureShowDone, Func<bool> homecomingShowDone)
    {
        _adapter = adapter;
        _clock = clock;
        _departureShowDone = departureShowDone;
        _homecomingShowDone = homecomingShowDone;

        // Self-subscribed (not driven by an external "notify" call) so this class is fully
        // standalone-testable — construct it over a bare SimAdapter/PhaseClock, tick either one any
        // way at all, and Current is always correct on the next read. Registered in the constructor
        // so a caller that builds this BEFORE its own StateChanged subscription (MainUi does, on
        // purpose — see MainUi._Ready) sees Current already resynced by the time its own handler runs.
        _adapter.StateChanged += Resync;
    }

    /// <summary>The beat the conductor is currently in — read every frame by the HUD (Hurry caption,
    /// bell-vs-Hurry routing) and by the Camp modal wiring.</summary>
    public Beat Current { get; private set; } = Beat.Idle;

    /// <summary>
    /// Re-derive <see cref="Current"/> from the adapter's own post-tick state. Fires on EVERY
    /// <see cref="SimAdapter.StateChanged"/> — including immediate actions, which re-raise the event
    /// with an UNCHANGED phase (<see cref="SimAdapter.Queue"/>'s own doc) and must never perturb the
    /// beat (a SendSupply press during the vigil stop must not end it).
    /// </summary>
    private void Resync(DayPhase completedPhase, int completedDay)
    {
        var state = _adapter.CurrentState;
        if (completedPhase == state.Phase)
        {
            return; // immediate-action noise, not a real phase transition
        }

        _elapsed = 0;
        Current = state.Phase switch
        {
            DayPhase.Expedition => Beat.SendOff,
            DayPhase.Camp => state.InFlight.IsEmpty ? Beat.DeepTick : Beat.VigilStop,
            DayPhase.ExpeditionDeep => Beat.DeepTick,
            // Evening reached FROM ExpeditionDeep means a raid actually happened — survivors are
            // queued to walk home (Town2D.ReturnSurvivors, fired by MainUi.OnPhaseCompleted from
            // this SAME tick) and deserve their homecoming beat. Evening reached from Morning (the
            // NoRaidToHost collapse — nobody alive to muster) or from its own day-roll has nothing
            // to show; go straight to Idle.
            DayPhase.Evening when completedPhase == DayPhase.ExpeditionDeep => Beat.Homecoming,
            _ => Beat.Idle, // Morning, or Evening with nothing to come home from
        };
    }

    /// <summary>One tick via the shared path — <see cref="Resync"/> fires synchronously inside this
    /// call (through <see cref="SimAdapter.StateChanged"/>) and updates <see cref="Current"/> before
    /// this method returns.</summary>
    private void Tick() => _clock.AdvanceNow();

    /// <summary>
    /// Driven every frame from <c>MainUi._Process</c>, independent of <see
    /// cref="PhaseClock.AutoAdvance"/> — the raid span is never opt-in the way the old Innkeeper's
    /// Clock is (see the plan's own "why this is not the rejected living clock": that clock timed
    /// phases the player WORKS in; this one only ever times the phases the player cannot act in at
    /// all, plus the one real stop, which this method never times).
    /// </summary>
    public void Update(double deltaSeconds)
    {
        if (Current is Beat.Idle or Beat.VigilStop)
        {
            return; // Idle: a real bell owns this phase. VigilStop: the one timer-free stop.
        }

        _elapsed += deltaSeconds;

        switch (Current)
        {
            case Beat.SendOff:
                if (_departureShowDone() || _elapsed >= SendOffMaxSeconds)
                {
                    Tick(); // stage-1 tick: Expedition -> Camp
                }

                break;

            case Beat.DeepTick:
                var state = _adapter.CurrentState;
                // At Camp, InFlight is guaranteed empty here (a non-empty Camp is VigilStop, not
                // DeepTick — see Resync). At ExpeditionDeep it may be either, depending on whether a
                // party pressed on from the vigil.
                var empty = state.Phase == DayPhase.Camp || state.InFlight.IsEmpty;
                var max = empty ? EmptyBeatSeconds : DeepShowSeconds;
                if (_elapsed >= max)
                {
                    Tick(); // Camp -> ExpeditionDeep, or ExpeditionDeep -> Evening
                }

                break;

            case Beat.Homecoming:
                if (_homecomingShowDone() || _elapsed >= HomecomingMaxSeconds)
                {
                    Current = Beat.Idle; // already at Evening — nothing left to tick, just to show
                }

                break;
        }
    }

    /// <summary>
    /// Skip the current beat's show and land at the next stop — bounded, and by construction never
    /// skips <see cref="Beat.VigilStop"/> past unseen: the loop below stops the INSTANT it reaches
    /// Idle (Evening's own bell) or VigilStop (the modal's own decision), so a party that parks is
    /// always still sitting there, un-hurried-past, the moment this returns.
    /// </summary>
    public void Hurry()
    {
        // At most 4 beats stand between any point and the next stop (SendOff -> Camp -> Deep ->
        // Homecoming) — bounded well short of that as a defensive guard against ever spinning.
        for (var guard = 0; guard < 8 && Current is not (Beat.Idle or Beat.VigilStop); guard++)
        {
            if (Current == Beat.Homecoming)
            {
                Current = Beat.Idle;
                continue;
            }

            Tick(); // SendOff or DeepTick: force the beat's own tick now, skipping its show wait
        }
    }

    /// <summary>
    /// The camp slate's third verb ("Send them deeper") calls this: the only way <see
    /// cref="Beat.VigilStop"/> ever ends. A no-op from any other beat — defensive, since only that
    /// verb's own handler should ever call it.
    /// </summary>
    public void ResolveVigil()
    {
        if (Current != Beat.VigilStop)
        {
            return;
        }

        Tick(); // Camp -> ExpeditionDeep
    }
}
