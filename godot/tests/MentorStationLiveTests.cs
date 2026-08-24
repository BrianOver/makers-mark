#if GDUNIT_TESTS
using System.Linq;
using GameSim;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// R14.5/U-T2-5 (Wave A substrate, §11.14.4): live, honest-input-only proof that Bryn is actually
/// reachable and speaks — a real click on her real station, reading only the visible on-screen
/// toast, the same idiom <c>StationIdentityTests</c> already uses for the anvil/furnace/shelf press.
/// <see cref="MentorVoiceTests"/> covers her pure logic in isolation; this file is the seam a
/// player actually presses.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class MentorStationLiveTests
{
    /// <summary>
    /// U4 (§11.14.14): this used to pin <c>RejectionToast</c> — the four-second banner built to
    /// reject illegal actions, not to carry a lesson. That was the wrong behaviour to pin, not a
    /// correct one this unit is merely re-verifying: <see cref="MainUi.RejectionToastSeconds"/>
    /// truncated her longest lessons mid-sentence, and that toast path renders copy with no markup
    /// parser, so the counter step spoke literal <c>**asterisks**</c>. Rewritten to pin her own
    /// untimed <see cref="MentorBanner"/> instead — see <see cref="PressingBryn_NeverShowsTheOldRejectionToast"/>
    /// and <see cref="PressingBryn_NeverTimesOut_EvenLongAfterTheOldFourSecondWindow"/> for the two
    /// defects this replaces, proven directly.
    /// </summary>
    [TestCase]
    public void PressingBryn_ShowsHerCurrentLesson_ThroughHerOwnUntimedBanner()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);

            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");
            room.Stations.First(s => s.Key == MentorVoice.StationId).RaisePick();

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("Pressing Bryn never showed her own banner.")
                .IsTrue();

            var spoken = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            var expected = MentorVoice.CurrentLesson(TutorialStep.BuyMaterial);

            AssertThat(spoken)
                .OverrideFailureMessage($"Pressing Bryn showed \"{spoken}\" instead of her live current-lesson voice line \"{expected}\".")
                .IsEqual(expected);
            AssertThat(spoken).Contains(MentorVoice.Name);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The rejection-toast half of the fix: her station used to special-case straight into
    /// <c>ShowBellToast</c>, so pressing her also lit the SAME banner an illegal action rejection
    /// uses. That banner must now stay dark for her entirely.</summary>
    [TestCase]
    public void PressingBryn_NeverShowsTheOldRejectionToast()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");
            room.Stations.First(s => s.Key == MentorVoice.StationId).RaisePick();

            AssertThat(Find<PanelContainer>(ui, "ToastBanner").Visible)
                .OverrideFailureMessage("Pressing Bryn lit the rejection-toast banner — that surface is for illegal actions, not her voice.")
                .IsFalse();
            AssertThat(Find<Label>(ui, "RejectionToast").Text)
                .OverrideFailureMessage("Pressing Bryn wrote into the rejection toast's own label.")
                .IsEqual(string.Empty);
            AssertThat(ui.ToastRemaining)
                .OverrideFailureMessage("Pressing Bryn armed the rejection toast's own countdown.")
                .IsEqual(0.0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The truncation half of the fix, proven directly rather than inferred: drive the
    /// clock well past <see cref="MainUi.RejectionToastSeconds"/> (the window that used to cut her
    /// off) and confirm she is still on screen, saying the exact same thing — no timer, ever (law:
    /// no timers on decisions). Mirrors <c>RejectionUxTests.ForcedRejection_RendersPlayerPhrasedToast_ThenClears</c>'s
    /// own <c>ui._Process(RejectionToastSeconds + ...)</c> idiom, but asserts the opposite outcome.</summary>
    [TestCase]
    public void PressingBryn_NeverTimesOut_EvenLongAfterTheOldFourSecondWindow()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");
            room.Stations.First(s => s.Key == MentorVoice.StationId).RaisePick();

            var expected = Find<Label>(ui.Mentor, "MentorBannerText").Text;

            ui._Process(MainUi.RejectionToastSeconds + 10.0);

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("Bryn's banner disappeared on its own — it must only ever close on the player's own \"Got it\" press.")
                .IsTrue();
            AssertThat(Find<Label>(ui.Mentor, "MentorBannerText").Text).IsEqual(expected);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Pressing her never opens a panel and never advances the chain — the "no step
    /// completion depends on speaking to her" half of R14.5, proven through the real click rather
    /// than only the table-level <c>Station_IsHonestFlavor_NeverGatesAnyStepsCompletion</c> check.</summary>
    [TestCase]
    public void PressingBryn_OpensNoPanel_AndNeverAdvancesTheChain()
    {
        var ui = MountMainUi();
        try
        {
            var stepBefore = ui.Tutorial.Step;

            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");
            room.Stations.First(s => s.Key == MentorVoice.StationId).RaisePick();

            AssertThat(ui.Drawer.IsOpen)
                .OverrideFailureMessage("Pressing Bryn opened a drawer panel — her station must be honest flavor only (Action: null).")
                .IsFalse();
            AssertThat(ui.Tutorial.Step)
                .OverrideFailureMessage("Pressing Bryn advanced the tutorial chain — no step may gate on speaking to her (R14.5).")
                .IsEqual(stepBefore);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>She is present regardless of which craft the player actually picked — R14.5 does not
    /// scope her to blacksmithing.</summary>
    [TestCase("tanning")]
    [TestCase("alchemy")]
    [TestCase("engineering")]
    public void BrynIsPresent_InEveryProfessionsOwnWorkshop_NotOnlyBlacksmiths(string professionId)
    {
        var adapter = new GodotClient.SimAdapter(GameComposition.NewCampaign(2026, professionId));
        var ui = MountMainUi(adapter);
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");

            AssertThat(room.Stations.Any(s => s.Key == MentorVoice.StationId))
                .OverrideFailureMessage($"Bryn's station is missing from the '{professionId}' workshop.")
                .IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
