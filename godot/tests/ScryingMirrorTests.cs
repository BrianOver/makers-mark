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
}
#endif
