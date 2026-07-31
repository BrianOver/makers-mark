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
            AssertThat(Bell(ui).Text).IsEqual("Lower them into the mine");
            AssertThat(ClockLabel(ui).Text).Contains("Quest");

            PressEnabled(ui, "AdvancePhase"); // Expedition → Camp
            // Camp's label depends on whether anyone is actually parked below — see BellVerb. It must
            // NEVER be "Ring the return bell": that verb is RecallPartyAction's (CampPanel), and this
            // button does the opposite of recalling.
            AssertThat(Bell(ui).Text).IsNotEqual("Ring the return bell");
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// The bell must describe what pressing it DOES, in the state the player is actually in.
    ///
    /// <para>Three playtest complaints were one bug. <c>GameKernel.Advance</c> walks every day through
    /// Camp and ExpeditionDeep unconditionally, so a party whose target floor sits inside stage 1
    /// finishes at the Expedition tick and walks home — and the player then rings two more bells labelled
    /// about a mine that is empty. Verbatim: "return bell does nothing but moved it to 'deep' phase??",
    /// "hitting 'lower them into the mine' brings them back to the town??", "?? not able to see the
    /// heroes in the mine".</para>
    ///
    /// <para>Both branches are asserted here because the empty-mine branch is the one the player hit and
    /// a phase-only label made it invisible.</para>
    /// </summary>
    [TestCase]
    public void CampBell_TellsTheTruth_AboutWhetherAnyoneIsActuallyBelow()
    {
        var ui = MountMainUi();
        try
        {
            PressEnabled(ui, "AdvancePhase"); // Morning → Expedition
            PressEnabled(ui, "AdvancePhase"); // Expedition → Camp
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);

            var below = !ui.Adapter.CurrentState.InFlight.IsEmpty;
            var expected = below ? "Let them press deeper" : "Close the vigil";

            AssertThat(Bell(ui).Text)
                .OverrideFailureMessage(
                    $"At Camp with InFlight.Count={ui.Adapter.CurrentState.InFlight.Count}, the bell reads " +
                    $"\"{Bell(ui).Text}\" but pressing it advances Camp → ExpeditionDeep, which " +
                    (below
                        ? "sends the party DEEPER. A label promising a return while sending them down is the "
                        : "does nothing to anyone, because nobody is below. A label about the mine here is the ") +
                    $"reported bug. Expected \"{expected}\".")
                .IsEqual(expected);

            // And the deep phase, same rule.
            PressEnabled(ui, "AdvancePhase"); // Camp → ExpeditionDeep
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.ExpeditionDeep);
            AssertThat(Bell(ui).Text)
                .IsEqual(ui.Adapter.CurrentState.InFlight.IsEmpty ? "Close the vigil" : "Ring the return bell");
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
