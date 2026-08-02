#if GDUNIT_TESTS
using System.Threading.Tasks;
using GameSim.Contracts;
using GdUnit4;
using Godot;
using GodotClient.Audio;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// An immediate action is not a tick, and must not be mistaken for one.
///
/// <para><b>Why this exists.</b> Since the 2026-07-30 immediate-action change, <c>SimAdapter.Queue</c>
/// raises the SAME <c>StateChanged</c> event as a real phase tick — with the current, un-advanced
/// phase, because nothing completed. <c>MainUi.OnPhaseCompleted</c> handles both callers. Two of its
/// consumers did not tell them apart, and the owner found all four symptoms by playing:</para>
///
/// <list type="bullet">
/// <item>"hitting stock keeps sending the heroes out" — Stock during Morning re-ran the town's
/// <c>DepartWanderingHeroes</c> choreography.</item>
/// <item>"why did the heroes come back to the town visually?" — any immediate action during
/// Expedition/ExpeditionDeep re-ran <c>ReturnSurvivors</c>, marching the party home mid-raid.</item>
/// <item>"doing anything in the forge changes the music" — every accepted action fired the day's 1.6s
/// bronze bell over a -22 dB music bed.</item>
/// <item>"shop stock sound was changed... it's now a scary bell instead of the shop/register noise" —
/// Shelve's own cue, then the bell on top of it.</item>
/// </list>
///
/// <para>Both consumers now check <c>completedPhase != state.Phase</c> before acting. These tests press
/// real buttons and read the two observation surfaces those consumers write
/// (<c>Town2D.PhaseChoreographyRuns</c>, <c>AudioDirector.LastCuePlayed</c>) — the cue is recorded even
/// while muted precisely so an automated run can pin cue CHOICE.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ImmediateActionsDoNotReplayThePhaseTests
{
    [TestCase]
    public async Task BuyingAMaterial_DoesNotRunTheTownsPhaseChoreography()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            ui.OpenPanel("Forge");
            await SettleLayout(ui);

            var phaseBefore = ui.Adapter.CurrentState.Phase;
            var runsBefore = ui.Town.PhaseChoreographyRuns;

            PressEnabled(ui.Forge, $"BuyMat_{ScriptedSession.CraftMaterial}");
            await SettleLayout(ui);

            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Precondition: an immediate action must not advance the phase.")
                .IsEqual(phaseBefore);

            AssertThat(ui.Town.PhaseChoreographyRuns)
                .OverrideFailureMessage(
                    "Buying re-ran the town's phase choreography. Nothing completed, so nobody should "
                    + "march anywhere — this is the bug behind \"hitting stock keeps sending the heroes "
                    + "out\" and \"why did the heroes come back to the town visually?\". MainUi must "
                    + "check completedPhase != state.Phase before calling Town.OnPhaseCompleted.")
                .IsEqual(runsBefore);
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public async Task BuyingAMaterial_DoesNotRingTheDaysBell()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            ui.OpenPanel("Forge");
            await SettleLayout(ui);

            var audio = AudioDirector.For(ui);
            AssertThat(audio).IsNotNull();

            // Scope the window to this one press. Asserting on LastCuePlayed alone does NOT work here:
            // OnBuyMaterialPressed queues the action FIRST and plays its own Coin cue after, so a
            // spurious Bell raised inside Queue is overwritten before the assertion sees it. The first
            // draft of this test did exactly that and passed with the fix reverted — absence needs a
            // window, not a snapshot.
            audio!.ClearRecentCues();

            PressEnabled(ui.Forge, $"BuyMat_{ScriptedSession.CraftMaterial}");
            await SettleLayout(ui);

            AssertThat(audio.RecentCues)
                .OverrideFailureMessage(
                    $"Buying played [{string.Join(", ", audio.RecentCues)}] — the day's bell is in there. "
                    + "The bell belongs to the day advancing; ringing it on every accepted "
                    + "craft/buy/shelve is what the owner heard as \"doing anything in the forge changes "
                    + "the music\" and \"the shop stock sound is now a scary bell instead of the "
                    + "shop/register noise\". SoundTheTick must return early when nothing completed.")
                .NotContains(Cue.Bell);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The other half of the contract: a REAL phase tick must still do both things. A guard that
    /// silences everything would pass the two tests above and break the game.
    /// </summary>
    [TestCase]
    public async Task ARealPhaseTick_StillRunsTheChoreographyAndSoundsTheBell()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.WorldViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
            await SettleLayout(ui);

            var runsBefore = ui.Town.PhaseChoreographyRuns;
            var phaseBefore = ui.Adapter.CurrentState.Phase;

            ui.Adapter.AdvancePhase();
            await SettleLayout(ui);

            AssertThat(ui.Adapter.CurrentState.Phase)
                .OverrideFailureMessage("Precondition: AdvancePhase must actually advance the phase.")
                .IsNotEqual(phaseBefore);

            AssertThat(ui.Town.PhaseChoreographyRuns)
                .OverrideFailureMessage(
                    "A real phase tick did NOT run the town's choreography. The immediate-action guard "
                    + "has over-reached and now suppresses the real thing too.")
                .IsGreater(runsBefore);
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
