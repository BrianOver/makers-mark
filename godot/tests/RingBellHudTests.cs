#if GDUNIT_TESTS
using System.Collections.Immutable;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// Wave 1 "Ring the Bell" (plan 2026-07-24-003): player-decided pacing. The advance button (node
/// name kept as "AdvancePhase") carries a phase-contextual verb (U3), the clock label names the
/// player phase Dawn/Prepare/Quest–Watch/Quest–Vigil/Night (U4), and ringing the bell while a
/// counter session is open closes it first so the day never silently fails to advance (U5).
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class RingBellHudTests
{
    private static Button Bell(MainUi ui) => Find<Button>(ui, "AdvancePhase");
    private static Label ClockLabel(MainUi ui) => Find<Label>(ui, "ClockLabel");

    [TestCase]
    public void BellVerb_And_PhaseBanner_TrackTheKernelPhase()
    {
        var ui = MountMainUi();
        try
        {
            // Day 1 Morning, no counter open → Dawn / "Send them off".
            AssertThat(Bell(ui).Text).IsEqual("Send them off");
            AssertThat(ClockLabel(ui).Text).Contains("Dawn");

            PressEnabled(ui, "AdvancePhase"); // Morning → Expedition
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            AssertThat(Bell(ui).Text).IsEqual("Lower the winch");
            AssertThat(ClockLabel(ui).Text).Contains("Quest");

            PressEnabled(ui, "AdvancePhase"); // Expedition → Camp
            AssertThat(Bell(ui).Text).IsEqual("Ring the return bell");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void OpenCounter_ShowsPrepareBanner()
    {
        var ui = MountMainUi();
        try
        {
            ui.Adapter.Queue(new OpenCounterAction());
            ui.Adapter.AdvancePhase(); // applies OpenCounter; day HOLDS at Morning (session open)
            ui.RefreshAll();

            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(ui.Adapter.CurrentState.Counter).IsNotNull();
            AssertThat(ClockLabel(ui).Text).Contains("Prepare");
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public void RingingBell_WithOpenCounter_ClosesSessionAndAdvances_NeverSilentlyHolds()
    {
        var ui = MountMainUi();
        try
        {
            // Open a counter session (day holds at Morning while Closed==false).
            ui.Adapter.Queue(new OpenCounterAction());
            ui.Adapter.AdvancePhase();
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            AssertThat(ui.Adapter.CurrentState.Counter is { Closed: false }).IsTrue();

            // Ring the bell: U5 must close the session first so the day actually moves — a naive
            // AdvanceNow here would tick with Counter{Closed:false} and silently stay at Morning.
            PressEnabled(ui, "AdvancePhase");

            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
            AssertThat(ui.Adapter.CurrentState.Counter).IsNull(); // session torn down on the day boundary
        }
        finally { Unmount(ui); }
    }
}
#endif
