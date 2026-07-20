using System;
using GameSim.Contracts;

namespace GodotClient;

/// <summary>
/// Living day clock (U15/KTD3 — "flows-but-waits"): auto-advance is ON by default for a
/// new campaign — <see cref="SimAdapter.AdvancePhase"/> fires when the current phase's
/// wall-clock time elapses, with play/pause plus a fast-forward multiplier as
/// sub-controls, and an explicit skip (<see cref="AdvanceNow"/>) always available (player
/// intent wins over the timer, engaged or not). The <see cref="Engaged"/> latch (AE1) is
/// the "waits" half: while any drawer/modal/interior surface is open, an expired phase
/// timer DEFERS the tick instead of firing — the clock still flows right up to the
/// boundary (ambience/live feeds keep animating) and holds there until the surface
/// closes, then ticks on the very next <see cref="Update"/> call. No queued or phase-legal
/// action is ever lost to timing. A settings escape hatch (adapter-side, <c>MainUi</c>'s
/// <c>ClockSettings</c> JSON store at <c>user://</c>) can restore the old fully-manual
/// mode by disabling auto-advance and persisting that choice across campaigns — never
/// part of the sim save (KTD2). Presentation-side timing only: the sim never reads a wall
/// clock. Plain C#, driven by <c>MainUi._Process(delta)</c>, so it is testable without an
/// engine runtime.
/// </summary>
public sealed class PhaseClock
{
    // Pinned default durations (seconds) — tuning knobs, keep them here.
    public const double MorningSeconds = 45;
    public const double ExpeditionSeconds = 30;
    public const double EveningSeconds = 45;

    private static readonly int[] Speeds = [1, 2, 4];

    private readonly SimAdapter _adapter;
    private int _speedIndex;

    public PhaseClock(SimAdapter adapter) => _adapter = adapter;

    /// <summary>
    /// U15 (KTD3): ON by default for a new campaign — the living clock flows on its own.
    /// The explicit skip (<see cref="AdvanceNow"/>) is always available regardless. A
    /// persisted preference (MainUi's settings escape hatch) can restore manual mode on
    /// load by calling <see cref="SetAutoAdvance"/> before the first frame.
    /// </summary>
    public bool AutoAdvance { get; private set; } = true;

    /// <summary>Within auto mode the town moves by default; pause is the exception.</summary>
    public bool Playing { get; private set; } = true;

    /// <summary>
    /// U15 (KTD3/AE1) engaged latch — the named cross-unit interface U18 (Wave 3) reads to
    /// render the day-timeline's waiting state. Set by the adapter (<c>MainUi</c>) whenever
    /// a drawer/modal/interior surface is open; TAB-ERA INTERIM RULE until U21's drawers
    /// land: any tab other than Town, or an open modal, engages it. While engaged, an
    /// expired phase timer holds at the boundary instead of ticking — see <see cref="Update"/>.
    /// </summary>
    public bool Engaged { get; set; }

    public int SpeedMultiplier => Speeds[_speedIndex];

    /// <summary>Seconds accrued in the current phase (already speed-scaled).</summary>
    public double Elapsed { get; private set; }

    public double PhaseDuration => DurationOf(_adapter.CurrentState.Phase);

    public double Remaining => Math.Max(0, PhaseDuration - Elapsed);

    public static double DurationOf(DayPhase phase) => phase switch
    {
        DayPhase.Morning => MorningSeconds,
        DayPhase.Expedition => ExpeditionSeconds,
        DayPhase.Evening => EveningSeconds,
        // DELIBERATE fallback: Camp and ExpeditionDeep (5-phase kernel) have no
        // pinned duration yet — they borrow MorningSeconds so auto mode keeps
        // moving through them until those phases get their own tuning knobs.
        _ => MorningSeconds,
    };

    public void Play() => Playing = true;

    public void Pause() => Playing = false;

    public void TogglePlay() => Playing = !Playing;

    public void ToggleAuto() => AutoAdvance = !AutoAdvance;

    public void SetAutoAdvance(bool enabled) => AutoAdvance = enabled;

    /// <summary>
    /// The explicit player Advance (U2/R1): exactly one sim tick, and the phase
    /// timer restarts so auto mode (if later enabled) times the NEW phase from zero.
    /// Same underlying advance as the auto timer — never a separate code path.
    /// </summary>
    public void AdvanceNow()
    {
        Elapsed = 0;
        _adapter.AdvancePhase();
    }

    /// <summary>Cycle 1x → 2x → 4x → 1x.</summary>
    public void CycleSpeed() => _speedIndex = (_speedIndex + 1) % Speeds.Length;

    /// <summary>
    /// Accrue wall-clock time; ticks the sim once when the phase's time elapses.
    /// At most one advance per call — a huge delta cannot skip whole phases silently.
    /// Auto OFF or paused: a no-op — Elapsed does not accrue and the sim is NEVER ticked
    /// from here, so time accrued before a toggle-off is harmless.
    /// <para>
    /// U15 (KTD3/AE1): while <see cref="Engaged"/>, Elapsed still accrues (capped at
    /// <see cref="PhaseDuration"/> — the "flows" half, ambience/live feeds keep animating
    /// right up to the boundary) but the tick itself is withheld (the "waits" half). The
    /// very next <see cref="Update"/> call after disengage finds Elapsed already at the
    /// cap and ticks immediately — a disengage never has to wait out a fresh full phase.
    /// </para>
    /// </summary>
    public void Update(double deltaSeconds)
    {
        if (!AutoAdvance || !Playing)
        {
            return;
        }

        if (Engaged)
        {
            Elapsed = Math.Min(Elapsed + deltaSeconds * SpeedMultiplier, PhaseDuration);
            return;
        }

        Elapsed += deltaSeconds * SpeedMultiplier;
        if (Elapsed >= PhaseDuration)
        {
            Elapsed = 0;
            _adapter.AdvancePhase();
        }
    }
}
