using System;
using GameSim.Contracts;

namespace GodotClient;

/// <summary>
/// Hybrid day clock (U2/R1): GATED by default — nothing advances a phase without an
/// explicit player Advance (<see cref="AdvanceNow"/>). The optional auto-advance
/// toggle (<see cref="AutoAdvance"/>, default OFF) opts back into the U11 real-time
/// cadence: <see cref="SimAdapter.AdvancePhase"/> fires when the current phase's
/// wall-clock time elapses, with play/pause plus a fast-forward multiplier as
/// sub-controls. Both paths fire the SAME advance — this is the root fix for the
/// "modal opened on wrong side of tick" rejection class. Presentation-side timing
/// only: the sim never reads a wall clock (KTD2 determinism). Plain C#, driven by
/// <c>MainUi._Process(delta)</c>, so it is testable without an engine runtime.
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
    /// U2/R1 gate: OFF by default — the timer NEVER ticks the sim unless the player
    /// opts into auto mode. Explicit <see cref="AdvanceNow"/> is always available.
    /// </summary>
    public bool AutoAdvance { get; private set; }

    /// <summary>Within auto mode the town moves by default; pause is the exception.</summary>
    public bool Playing { get; private set; } = true;

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
    /// Gated (auto OFF) or paused: a no-op — Elapsed does not accrue and the sim is
    /// NEVER ticked from here (U2/R1), so time accrued before a toggle-off is harmless.
    /// </summary>
    public void Update(double deltaSeconds)
    {
        if (!AutoAdvance || !Playing)
        {
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
