#if GDUNIT_TESTS
using GameSim.Contracts;
using GdUnit4;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>Real-time auto-advance rules (U11 pinned design). Pure C#, no Godot runtime.</summary>
[TestSuite]
public class PhaseClockTests
{
    [TestCase]
    public void Update_TicksTheSim_WhenPhaseDurationElapses()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);

        clock.Update(PhaseClock.MorningSeconds - 0.5);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

        clock.Update(0.5);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        AssertThat(clock.Elapsed).IsEqual(0.0);
        AssertThat(clock.PhaseDuration).IsEqual(PhaseClock.ExpeditionSeconds);
    }

    [TestCase]
    public void Paused_DoesNotAdvance()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
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
    public void SpeedMultiplier_FastForwardsRealTime()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
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
