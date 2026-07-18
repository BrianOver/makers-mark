#if GDUNIT_TESTS
using GameSim.Contracts;
using GdUnit4;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Hybrid clock rules (U2/R1 over the U11 timer): gated by default — the timer never
/// ticks the sim unless auto-advance is explicitly enabled. Pure C#, no Godot runtime.
/// </summary>
[TestSuite]
public class PhaseClockTests
{
    [TestCase]
    public void GatedByDefault_UpdateNeverAdvances_ExplicitAdvanceTicksOnce()
    {
        // U2/R1 core invariant: nothing advances a phase without explicit player
        // Advance. A fresh clock is gated (auto OFF) — arbitrarily large wall-clock
        // time must leave the sim untouched.
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);

        clock.Update(PhaseClock.MorningSeconds * 10);
        AssertThat(adapter.CurrentState.Day).IsEqual(1);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

        adapter.AdvancePhase(); // the explicit player Advance — the ONLY way forward when gated
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
    }

    [TestCase]
    public void AdvanceNow_TicksExactlyOnce_AndRestartsThePhaseTimer()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
        var ticks = 0;
        adapter.StateChanged += (_, _) => ticks++;

        clock.AdvanceNow();
        AssertThat(ticks).IsEqual(1);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        AssertThat(clock.Elapsed).IsEqual(0.0);
    }

    [TestCase]
    public void ToggleAutoOffMidPhase_AccruedTimeIsHarmless()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);

        clock.SetAutoAdvance(true);
        clock.Update(PhaseClock.MorningSeconds - 1); // accrue almost a full phase
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

        clock.ToggleAuto(); // back to gated — accrued time must never fire the tick
        for (var frame = 0; frame < 10; frame++)
        {
            clock.Update(PhaseClock.MorningSeconds * 10);
        }

        AssertThat(adapter.CurrentState.Day).IsEqual(1);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
    }

    [TestCase]
    public void AutoOn_Update_TicksTheSim_WhenPhaseDurationElapses()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
        clock.SetAutoAdvance(true); // U2: the U11 timed cadence is now opt-in

        clock.Update(PhaseClock.MorningSeconds - 0.5);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

        clock.Update(0.5);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        AssertThat(clock.Elapsed).IsEqual(0.0);
        AssertThat(clock.PhaseDuration).IsEqual(PhaseClock.ExpeditionSeconds);
    }

    [TestCase]
    public void AutoOn_Paused_DoesNotAdvance()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
        clock.SetAutoAdvance(true);
        clock.Pause();

        clock.Update(PhaseClock.MorningSeconds * 10);
        AssertThat(adapter.CurrentState.Day).IsEqual(1);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
        AssertThat(clock.Elapsed).IsEqual(0.0);

        clock.Play();
        clock.Update(PhaseClock.MorningSeconds);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
    }

    [TestCase]
    public void AutoOn_SpeedMultiplier_FastForwardsRealTime()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
        clock.SetAutoAdvance(true);
        clock.CycleSpeed(); // 2x

        AssertThat(clock.SpeedMultiplier).IsEqual(2);
        clock.Update(PhaseClock.MorningSeconds / 2);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
    }

    [TestCase]
    public void CycleSpeed_Wraps1x2x4x()
    {
        var clock = new PhaseClock(new SimAdapter(ScriptedSession.Seed));
        AssertThat(clock.SpeedMultiplier).IsEqual(1);
        clock.CycleSpeed();
        AssertThat(clock.SpeedMultiplier).IsEqual(2);
        clock.CycleSpeed();
        AssertThat(clock.SpeedMultiplier).IsEqual(4);
        clock.CycleSpeed();
        AssertThat(clock.SpeedMultiplier).IsEqual(1);
    }
}
#endif
