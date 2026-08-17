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
    [TestCase]
    public void PressingBryn_ShowsHerCurrentLesson_QuotingTheActiveStepsTeachNote()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.Step).IsEqual(TutorialStep.BuyMaterial);

            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");
            room.Stations.First(s => s.Key == MentorVoice.StationId).RaisePick();

            var toast = Find<Label>(ui, "RejectionToast").Text;
            var expected = MentorVoice.CurrentLesson(TutorialStep.BuyMaterial);

            AssertThat(toast)
                .OverrideFailureMessage($"Pressing Bryn showed \"{toast}\" instead of her live current-lesson voice line \"{expected}\".")
                .IsEqual(expected);
            AssertThat(toast).Contains(MentorVoice.Name);
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
