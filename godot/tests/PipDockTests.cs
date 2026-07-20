#if GDUNIT_TESTS
using GameSim.Contracts;
using GdUnit4;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U16 (KTD13 HUD layout spec): the bottom-right PiP dock, mounted on the real
/// <c>MainUi</c> so its visibility is driven by the real phase clock, not a hand-built card.
/// Covers the one explicitly test-pinned rule (PiP absent Morning/Evening) plus the click-to-
/// expand wire into <see cref="MainUi.Mirror"/>.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class PipDockTests
{
    [TestCase]
    public void Morning_PipAbsent()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Adapter.CurrentState.Phase).IsEqual(DayPhase.Morning);
            ui.Pip._Process(0.0); // Docked is computed in _Process (the slide-out driver) — force one frame
            AssertThat(ui.Pip.Docked).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Expedition_Camp_Deep_PipDocked_Evening_PipAbsentAgain()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceToPhase(ui, DayPhase.Expedition);
            ui.Pip._Process(0.0); // force the slide-state recompute (no real frame pump in this suite)
            AssertThat(ui.Pip.Docked).IsTrue();

            AdvanceToPhase(ui, DayPhase.Evening);
            // Camp/ExpeditionDeep may or may not exist for today's parties depending on staging —
            // either way, the moment the sim reports Evening the dock's intent flips back off.
            ui.Pip._Process(0.0);
            AssertThat(ui.Pip.Docked).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void Clock_Paused_FeedHoldsStill_Played_ItAdvances()
    {
        // U25 follow-up (a): the PiP feed pauses with the clock (paused != engaged — an engaged
        // surface, e.g. a drawer open over the world, keeps the feed flowing per KTD3; PipDock's
        // own visibility never forces a pause the way ScryingMirror's does, so gating on
        // PhaseClock.Playing here is safe — see ScryingMirror.cs for why that surface differs).
        var ui = MountMainUi();
        try
        {
            AdvanceToPhase(ui, DayPhase.Camp);
            ui.Clock.Pause();

            ui.Pip._Process(100.0); // would force a full reveal if the feed were still advancing
            AssertThat(ui.Pip.CurrentBeats.IsEmpty).IsTrue();

            ui.Clock.Play();
            ui.Pip._Process(100.0);
            AssertThat(ui.Pip.CurrentBeats.IsEmpty).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ExpandButton_OpensTheScryingMirror()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceToPhase(ui, DayPhase.Expedition);
            AssertThat(ui.Mirror.Visible).IsFalse();

            Press(ui.Pip, "PipExpand");

            AssertThat(ui.Mirror.Visible).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
