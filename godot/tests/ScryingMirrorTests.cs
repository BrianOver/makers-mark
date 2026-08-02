#if GDUNIT_TESTS
using GameSim.Contracts;
using GdUnit4;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U16: the expanded scrying mirror, driven through the real <c>MainUi</c> mount so party
/// formation, staging, and resolution all come from a genuine ticked campaign — a fresh seed's
/// starting SIX heroes (<see cref="GameSim.GameComposition.NewCampaign(ulong)"/>) split into
/// exactly two parties of three (<see cref="GameSim.Heroes.PartyFormation.FormParties"/>), which
/// is what makes this suite's multi-party scenario naturally occurring rather than hand-built.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ScryingMirrorTests
{
    [TestCase]
    public void MultiParty_TabSwitch_ShowsTheSecondPartysOwnBeats()
    {
        var ui = MountMainUi();
        try
        {
            // Fresh heroes: DeepestFloorReached 0 -> target floor 1 -> checkpoint<1 -> both
            // parties resolve UNSTAGED straight into PendingExpeditions at the Expedition tick.
            AdvanceToPhase(ui, DayPhase.Camp);
            ui.Mirror.ShowMirror();

            AssertThat(ui.Mirror.PartyCount).IsEqual(2);

            ui.Mirror._Process(100.0); // force both playheads fully revealed (no engine frame pump needed)

            ui.Mirror.SelectParty(0);
            var firstPartyBeats = ui.Mirror.VisibleBeats;
            AssertThat(firstPartyBeats.IsEmpty).IsFalse();

            ui.Mirror.SelectParty(1);
            var secondPartyBeats = ui.Mirror.VisibleBeats;
            AssertThat(secondPartyBeats.IsEmpty).IsFalse();

            AssertThat(string.Join("|", secondPartyBeats)).IsNotEqual(string.Join("|", firstPartyBeats));
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CloseMirror_ResumesTheClock_IfItWasPlaying()
    {
        var ui = MountMainUi();
        try
        {
            ui.Clock.Play();
            ui.Mirror.ShowMirror();
            AssertThat(ui.Clock.Playing).IsFalse(); // opening pauses, same as Ledger/Camp

            ui.Mirror.CloseMirror();

            AssertThat(ui.Clock.Playing).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── U9 (world-and-interiors plan, KTD-4): the Mirror hosts the animated delve ──────────────
    // The owner's complaint ("the watch has no animations - all text") traced to this panel being
    // 100% Labels/Buttons while the animated MineWatch/DelveStage strip existed one door over,
    // hosted only by DepthsPanel. See MineWatchRehostTests for the single-instance/re-parent
    // proof; these two cover what THIS panel specifically must show once it borrows the strip.

    [TestCase]
    public void ShowMirror_DuringLiveExpedition_HostsTheAnimatedStrip()
    {
        var ui = MountMainUi();
        try
        {
            AdvanceToPhase(ui, DayPhase.Expedition);
            ui.Mirror.ShowMirror();

            var watch = ui.Mirror.Watch;
            AssertThat(watch)
                .OverrideFailureMessage(
                    "ScryingMirror did not host the MineWatch strip -- the owner's complaint (\"all " +
                    "text, no animations\") would still reproduce.")
                .IsNotNull();
            AssertThat(watch!.Visible).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void ShowMirror_DuringCamp_BeatsKeepRevealing_DespiteTheMirrorsOwnPause()
    {
        // R4: "pressing Watch shows sprites fighting, not paragraphs" -- proves the
        // ForceRevealWhilePaused fix end-to-end through the real MainUi wiring. ScryingMirror
        // force-pauses PhaseClock on open (same as Ledger/Camp); without the override the
        // borrowed strip's own beat reveal would freeze at the exact instant a player opened the
        // Mirror to watch it -- the same bug this panel's OWN _feed was already fixed for (see
        // this class's _Process remarks).
        var ui = MountMainUi();
        try
        {
            AdvanceToPhase(ui, DayPhase.Camp);
            ui.Mirror.ShowMirror();
            AssertThat(ui.Clock.Playing)
                .OverrideFailureMessage("Precondition: opening the Mirror is supposed to force-pause the clock.")
                .IsFalse();

            var watch = ui.Mirror.Watch!;
            watch._Process(100.0); // force full reveal -- would stay clouded if the pause froze it

            AssertThat(watch.CurrentBeats.IsEmpty)
                .OverrideFailureMessage(
                    "The strip's beats never revealed while the Mirror was open -- opening it to " +
                    "watch the show froze the show.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
