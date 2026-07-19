#if GDUNIT_TESTS
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P007 U7 (R11/R12/KD1): the themed HUD header — the real home for the hybrid day
/// clock. Both the <c>AdvancePhase</c> button and the <c>AutoAdvance</c> toggle must
/// drive the SAME gated advance (<see cref="PhaseClock.AdvanceNow"/> /
/// <see cref="PhaseClock.Update"/> -> <see cref="SimAdapter.AdvancePhase"/>) — never a
/// second code path (KD1) — and the stat-chip row must stay live. The rejection banner's
/// full transience matrix (timeout AND clean-tick clearing, player-phrased text, raw
/// string never rendered) is already covered by <see cref="RejectionUxTests"/>; the one
/// scenario here drives the SAME rejection through the real HUD Advance button instead
/// of a direct Adapter call, proving the banner is wired to the control a player clicks.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class DayAdvanceHudTests
{
    [TestCase]
    public void AdvanceButton_Press_AdvancesExactlyOnePhase()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            PressEnabled(ui, "AdvancePhase");
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            // A second press ticks exactly one more phase — never more, never zero.
            PressEnabled(ui, "AdvancePhase");
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Camp);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void AutoToggle_FiresSameAdvanceOnTimer_DisablingStopsIt()
    {
        var ui = MountMainUi();
        try
        {
            // Gated by default: MountMainUi pauses the clock and auto starts OFF, so an
            // arbitrarily large frame delta through the real _Process path is a no-op.
            AssertThat(ui.Clock.AutoAdvance).IsFalse();
            ui._Process(PhaseClock.MorningSeconds * 10);
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);

            // Opt in through the real controls: Auto toggle, then Play (MountMainUi
            // paused the clock so the timer needs an explicit resume).
            PressEnabled(ui, "AutoAdvance");
            AssertThat(ui.Clock.AutoAdvance).IsTrue();
            PressEnabled(ui, "PlayPause");
            AssertThat(ui.Clock.Playing).IsTrue();

            // One frame >= the phase duration drives exactly one tick through PhaseClock.
            // Update -> the SAME SimAdapter.AdvancePhase the AdvancePhase button calls.
            ui._Process(PhaseClock.MorningSeconds);
            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);

            // Disabling auto stops the timer cold: further huge deltas never touch the sim.
            PressEnabled(ui, "AutoAdvance");
            AssertThat(ui.Clock.AutoAdvance).IsFalse();
            for (var frame = 0; frame < 5; frame++)
            {
                ui._Process(PhaseClock.MorningSeconds * 10);
            }

            AssertThat(ui.Adapter.CurrentState.Day).IsEqual(1);
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Expedition);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Advance_RejectedAction_ShowsPlayerPhrasedBanner_ClearsOnNextCleanAdvance()
    {
        var ui = MountMainUi();
        try
        {
            // Queue a doomed action (no handler accepts an ore buy at Morning), then drive
            // it through the SAME HUD control a player clicks — not a direct Adapter call.
            ui.Adapter.Queue(new BuyOreAction(new HeroId(1), ScriptedSession.CraftMaterial, 1));
            PressEnabled(ui, "AdvancePhase");

            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(1);
            var rendered = RenderedText(ui);
            AssertThat(rendered).Contains("Can't do that right now.");
            AssertThat(rendered.Contains("REJECTED:")).IsFalse();

            // The next clean advance — same Advance button — clears the banner early,
            // without waiting out the wall-clock toast timeout.
            PressEnabled(ui, "AdvancePhase");
            AssertThat(ui.Adapter.LastRejections.Count).IsEqual(0);
            AssertThat(RenderedText(ui).Contains("Can't do that right now.")).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void StatChips_ReflectLiveDayPhaseGoldHeroes_AfterTick()
    {
        var ui = MountMainUi();
        try
        {
            PressEnabled(ui, "AdvancePhase");
            var state = ui.Adapter.CurrentState;
            var alive = state.Heroes.Values.Count(h => h.Alive);

            AssertThat(RenderedText(Find<Control>(ui, "DayChip"))).Contains($"{state.Day}");
            AssertThat(RenderedText(Find<Control>(ui, "PhaseChip"))).Contains(state.Phase.ToString());
            AssertThat(RenderedText(Find<Control>(ui, "GoldChip"))).Contains($"{state.Player.Gold}g");
            AssertThat(RenderedText(Find<Control>(ui, "HeroesChip"))).Contains($"{alive}/{state.Heroes.Count}");
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
