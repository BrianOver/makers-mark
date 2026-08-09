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
/// <see cref="Update"/>'s empty-InFlight path carries every day (not just day 1) through Camp and
/// ExpeditionDeep on its own with zero stop.</para>
///
/// <para><b>...which is exactly how one bell reached Night.</b> Owner report, 2026-08-09: "i clicked
/// send them off and it auto jumped to night???? yet this is still on tutorial 5??? this is a
/// critical bug as it skipped most the game and prevented me from playing more". Measured on a fresh
/// day 1: <b>4.77 real seconds</b> from the Morning bell to Evening with zero further input, of which
/// the two empty beats are <see cref="EmptyBeatSeconds"/> each. The apprenticeship chain's own step 5
/// ("Press 👁 Watch to look in on them") is issued on the Expedition→Camp tick — the tick that emits
/// <c>PartyDeparted</c> — and the Watch control exists only while the phase is Expedition/Camp/
/// ExpeditionDeep (<c>MainUi.UpdateClockLabel</c>), so the player had <b>exactly 2.00 seconds</b> to
/// answer an instruction the game had only just printed. A timer that destroys the answer to a
/// question the game is still asking is a timer ON a decision (§11.7.8), and no amount of show
/// pacing makes it not one.</para>
///
/// <para><b>The hold (2026-08-09 fix).</b> The shows keep their timers; the timers now stop dead
/// while the player owes an answer this span is the only place to give. Two sources, checked every
/// <see cref="Update"/>: <see cref="PhaseClock.Engaged"/> — the "flows-but-waits" latch that has
/// governed the phase clock since U15 and that this class was simply never wired to, so a drawer, a
/// walkable interior or a modal now holds the raid span exactly as it already held Morning — and the
/// injected <c>showHeld</c> predicate, which <c>MainUi</c> feeds the one on-screen ask that lives
/// inside this span and nowhere else. <b>Held means paused, not deferred:</b> unlike <see
/// cref="PhaseClock.Update"/>'s engaged branch (which keeps accruing to the cap so a disengage ticks
/// immediately), <see cref="_elapsed"/> does NOT accrue while held — otherwise leaving the forge
/// after a minute would fire the whole rest of the day in one frame, which is the reported bug with
/// extra steps. <b>And the hold binds the TIMER only:</b> <see cref="Hurry"/> — the player's own
/// press — walks straight through it, because skipping stays legal and its cost is named in copy,
/// never engineered (§11.7.8). The day waits on the player; it never traps them.</para>
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
    private readonly Func<bool> _showHeld;

    private double _elapsed;

    /// <param name="showHeld">
    /// "The player owes an answer that only this span can receive" — see the class doc's own hold
    /// section. Checked every <see cref="Update"/>; while true, the show's timers stop (and do not
    /// accrue) so the phase cannot move. <see cref="Hurry"/> ignores it entirely: the player's own
    /// press is always allowed through. <c>MainUi</c> passes the apprenticeship chain's Watch step;
    /// <see cref="PhaseClock.Engaged"/> is folded in by <see cref="Update"/> itself and does NOT
    /// need to be repeated here.
    /// </param>
    public RaidConductor(
        SimAdapter adapter,
        PhaseClock clock,
        Func<bool> departureShowDone,
        Func<bool> homecomingShowDone,
        Func<bool> showHeld)
    {
        _adapter = adapter;
        _clock = clock;
        _departureShowDone = departureShowDone;
        _homecomingShowDone = homecomingShowDone;
        _showHeld = showHeld;

        // The vigil must be armed the instant this class exists, not merely on the next transition.
        // Current used to start at Idle unconditionally, which is a law break on exactly one shape:
        // a campaign RESUMED at Camp with a party already parked (CampaignSave stores the phase, and
        // MainUi._Ready builds this over whatever it loaded). Idle routes MainUi._Process into
        // Clock.Update — so the opt-in Innkeeper's Clock times the vigil away on a wall-clock timer,
        // measured, Camp -> ExpeditionDeep with the party still parked — and routes the bell press
        // into Clock.AdvanceNow() rather than the reopen-the-slate branch, ending an unanswered
        // decision outright. Deriving the parked-Camp case (and ONLY that case: the show beats have
        // no decision at stake, and a resumed show simply keeps its bell, which is harmless) closes
        // both doors with no behaviour change for any other phase.
        Current = adapter.CurrentState is { Phase: DayPhase.Camp, InFlight.IsEmpty: false }
            ? Beat.VigilStop
            : Beat.Idle;

        // Self-subscribed (not driven by an external "notify" call) so this class is fully
        // standalone-testable — construct it over a bare SimAdapter/PhaseClock, tick either one any
        // way at all, and Current is always correct on the next read. Registered in the constructor
        // so a caller that builds this BEFORE its own StateChanged subscription (MainUi does, on
        // purpose — see MainUi._Ready) sees Current already resynced by the time its own handler runs.
        _adapter.StateChanged += Resync;
    }

    /// <summary>The beat the conductor is currently in — read every frame by the HUD (Hurry caption,
    /// bell-vs-Hurry routing) and by the Camp modal wiring.</summary>
    public Beat Current { get; private set; }

    /// <summary>
    /// True while a show beat is running but its timer is held — the player owes an answer, or a
    /// surface owns the screen (class doc's hold section). Surfaced so the HUD can say so rather
    /// than leaving a visibly stopped day unexplained: the law allows skipping, but only when its
    /// cost is NAMED in copy. False in <see cref="Beat.Idle"/>/<see cref="Beat.VigilStop"/>, which
    /// have no timer to hold in the first place.
    /// </summary>
    public bool ShowHeld =>
        Current is not (Beat.Idle or Beat.VigilStop) && (_clock.Engaged || _showHeld());

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

        if (_clock.Engaged || _showHeld())
        {
            // Held: the player owes an answer, or a surface owns the screen. Return BEFORE accruing
            // — a held show is paused, not deferred (class doc). Accruing here would bank the whole
            // wait and fire every remaining beat the frame the hold lifts, which is the reported bug
            // wearing a different hat.
            return;
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
    ///
    /// <para>Deliberately ignores the <see cref="ShowHeld"/> hold. The hold exists to stop a TIMER
    /// from answering for the player; this method IS the player (the bell-row control's own press).
    /// Skipping stays legal — §11.7.8 — so the only thing a hold ever costs is that the day sits
    /// still until someone chooses to move it.</para>
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
