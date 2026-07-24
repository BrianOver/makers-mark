#if GDUNIT_TESTS
using GameSim.Contracts;
using GameSim.Materials;
using GdUnit4;
using static GdUnit4.Assertions;

namespace GodotClient.Tests;

/// <summary>
/// Living clock rules (U15/KTD3 — "flows-but-waits" — over the U11 timer/U2 gate): a
/// fresh clock auto-advances by default, an explicit skip is always available (player
/// intent wins), and the AE1 <see cref="PhaseClock.Engaged"/> latch defers an expired
/// timer's tick until disengage without ever dropping a queued action. Pure C#, no
/// Godot runtime.
/// </summary>
[TestSuite]
public class PhaseClockTests
{
    [TestCase]
    public void FreshClock_AutoAdvanceIsOff_ByDefault_PlayerDecidedPacing()
    {
        // U2 (Ring the Bell): a new campaign is PLAYER-DECIDED — the timer is off by default and
        // the day advances only on the bell (AdvanceNow). The old timed clock is opt-in.
        var clock = new PhaseClock(new SimAdapter(ScriptedSession.Seed));
        AssertThat(clock.AutoAdvance).IsFalse();
    }

    [TestCase]
    public void ManualModeStillAvailable_UpdateNeverAdvances_ExplicitSkipTicksOnce()
    {
        // The old fully-manual mode (auto explicitly OFF) still holds: arbitrarily large
        // wall-clock time leaves the sim untouched, and the explicit skip is the only way
        // forward.
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
        clock.SetAutoAdvance(false);

        clock.Update(PhaseClock.MorningSeconds * 10);
        AssertThat(adapter.CurrentState.Day).IsEqual(1);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

        clock.AdvanceNow(); // the explicit player skip — the ONLY way forward when gated
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

    // ── U15/AE1: the Engaged latch ("flows-but-waits") ────────────────────────────────────

    [TestCase]
    public void Engaged_TimerExpiry_HoldsAtBoundary_DoesNotTick()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
        clock.SetAutoAdvance(true);
        clock.Engaged = true;

        // Way past the phase's duration — still no tick while engaged.
        clock.Update(PhaseClock.MorningSeconds * 10);
        AssertThat(adapter.CurrentState.Day).IsEqual(1);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

        // "Flows": Elapsed still accrued (ambience/live feeds keep animating) but "waits":
        // held at the boundary rather than overshooting or wrapping.
        AssertThat(clock.Elapsed).IsEqual(clock.PhaseDuration);
        AssertThat(clock.Remaining).IsEqual(0.0);
    }

    [TestCase]
    public void Disengage_TicksWithinOneFrame()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
        clock.SetAutoAdvance(true);
        clock.Engaged = true;
        clock.Update(PhaseClock.MorningSeconds * 10); // holds at the boundary

        clock.Engaged = false;
        clock.Update(0.001); // the very next frame — a tiny delta is enough

        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        AssertThat(clock.Elapsed).IsEqual(0.0); // the phase timer restarted for the new phase
    }

    [TestCase]
    public void QueuedMorningAction_SubmittedWhileEngaged_LandsInTheMorningBatch()
    {
        // AE1: no queued, phase-legal action is ever lost to timing. While engaged, the
        // Morning phase timer has expired but the tick is deferred — Morning is still
        // live, so an action queued now is still a Morning action when it finally applies.
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
        clock.SetAutoAdvance(true);
        clock.Engaged = true;
        clock.Update(PhaseClock.MorningSeconds * 10); // expired, but held — Morning never left

        var materialKey = MaterialRegistry.PricedPool[0];
        adapter.Queue(new BuyMaterialAction(materialKey, 1));

        clock.Engaged = false;
        clock.Update(0.001); // disengage ticks the held Morning batch, action included

        AssertThat(adapter.LastRejections.IsEmpty).IsTrue();
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        AssertThat(adapter.CurrentState.Player.Materials.TryGetValue(materialKey, out var stock) ? stock : 0)
            .IsGreater(0);
    }

    [TestCase]
    public void Paused_HoldsIndefinitely_EvenWhileEngaged()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
        clock.SetAutoAdvance(true);
        clock.Engaged = true;
        clock.Pause();

        for (var frame = 0; frame < 10; frame++)
        {
            clock.Update(PhaseClock.MorningSeconds * 10);
        }

        AssertThat(adapter.CurrentState.Day).IsEqual(1);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
        AssertThat(clock.Elapsed).IsEqual(0.0); // paused: not even the "flows" half accrues

        // Disengaging alone still isn't enough — Play() is what resumes it.
        clock.Engaged = false;
        clock.Update(PhaseClock.MorningSeconds * 10);
        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
    }

    [TestCase]
    public void Skip_DuringEngaged_TicksImmediately_PlayerIntentWins()
    {
        var adapter = new SimAdapter(ScriptedSession.Seed);
        var clock = new PhaseClock(adapter);
        clock.SetAutoAdvance(true);
        clock.Engaged = true; // e.g. a drawer/modal is open

        clock.AdvanceNow(); // the explicit skip bypasses Engaged entirely

        AssertThat(adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        AssertThat(clock.Elapsed).IsEqual(0.0);
    }
}
#endif
