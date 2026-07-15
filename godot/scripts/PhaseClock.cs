using System;
using GameSim.Contracts;

namespace GodotClient;

/// <summary>
/// Real-time phase auto-advance (U11 pinned design): the town moves on its own, so
/// the day-phase control is play/pause plus a fast-forward multiplier — never a
/// manual next-phase button. Calls <see cref="SimAdapter.AdvancePhase"/> when the
/// current phase's wall-clock time elapses. Presentation-side timing only: the sim
/// never reads a wall clock (KTD2 determinism). Plain C#, driven by
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

    /// <summary>The town moves by default; pause is the exception.</summary>
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
        _ => MorningSeconds,
    };

    public void Play() => Playing = true;

    public void Pause() => Playing = false;

    public void TogglePlay() => Playing = !Playing;

    /// <summary>Cycle 1x → 2x → 4x → 1x.</summary>
    public void CycleSpeed() => _speedIndex = (_speedIndex + 1) % Speeds.Length;

    /// <summary>
    /// Accrue wall-clock time; ticks the sim once when the phase's time elapses.
    /// At most one advance per call — a huge delta cannot skip whole phases silently.
    /// </summary>
    public void Update(double deltaSeconds)
    {
        if (!Playing)
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
