#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim;
using GameSim.Contracts;
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

    /// <summary>
    /// P2-SCREEN-15 (fix): the property the test above only happens to exercise, pinned in its own
    /// right — pressing her answers in HER voice regardless of what the banner is already saying.
    /// Before this unit the forge door recorded its two lessons silently, so the banner was always
    /// free by the time the player reached her and the test above passed for a reason that had
    /// nothing to do with the rule. The moment the door started speaking, it didn't.
    ///
    /// <para>The second half is the cost of preempting, and the half worth guarding: the displaced
    /// note must be DISPLACED, not dropped. ConsumeFirstTouch has already marked its id fired and
    /// persisted that, so a line the banner bins here never fires again for this campaign — the
    /// exact defect MentorBanner's own queue was built to end. "Got it" must bring it back.</para>
    /// </summary>
    [TestCase]
    public void PressingBryn_DisplacesWhateverTheDoorSaid_WithoutLosingIt()
    {
        var ui = MountMainUi();
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();

            var doorNote = Find<Label>(ui.Mentor, "MentorBannerText").Text;
            AssertThat(doorNote)
                .OverrideFailureMessage("Walking into the forge said nothing, so this test proves nothing about a busy banner.")
                .IsNotEmpty();

            var room = ui.Town.FindInteriorRoom("forge");
            room.Stations.First(s => s.Key == MentorVoice.StationId).RaisePick();

            AssertThat(Find<Label>(ui.Mentor, "MentorBannerText").Text)
                .OverrideFailureMessage("Pressing Bryn queued her behind the door's own note instead of answering — asking her is the one act that must be answered on the press.")
                .IsEqual(MentorVoice.CurrentLesson(ui.Tutorial.Step));

            ui.Mentor.Dismiss();

            AssertThat(Find<Label>(ui.Mentor, "MentorBannerText").Text)
                .OverrideFailureMessage("The door's own note was binned rather than displaced — its lesson id is already spent, so binning it loses those words for the whole campaign.")
                .IsEqual(doorNote);
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

    /// <summary>
    /// U34 (§11, R25): the live seam for <see cref="MentorVoice.NextObservation"/> — pressing her
    /// once the current lesson is exhausted (Dismissed, no live objective) actually speaks a logged
    /// observation, and pressing her again after it has been told does NOT repeat it. Torvald
    /// (<c>HeroId</c> 1, <see cref="GameSim.Heroes.HeroRoster.StartingSix"/>'s own anchor) is sent
    /// underground wearing a player-marked weapon — the one fact this fixture's state carries — and
    /// nothing else in it (empty materials/shelf/commissions, Day 1 so <c>DemandBoard.DepthStalls</c>'s
    /// own stall-threshold can never trip, Evening so the Morning-only buy fallback can't fire)
    /// leaves <see cref="GameSim.Advisor.ObjectiveAdvisor.Suggest"/> with nothing to say — the exact
    /// precondition <see cref="MentorIdleVoice.HasLiveObjective"/> needs for the observation branch
    /// to be the thing actually reached.
    /// </summary>
    [TestCase]
    public void PressingBryn_OnceLessonIsExhausted_SpeaksALoggedObservation_ThenDoesNotRepeatIt()
    {
        var weaponId = new ItemId(9001);
        var weapon = new Item(
            weaponId, "recipe", "Emberbite", ItemSlot.Weapon, QualityGrade.Common,
            new ItemStats(6, 0, 2), new MakersMark("You", 1), ImmutableList<ItemHistoryEntry>.Empty);
        var torvaldId = new HeroId(1);

        var baseState = GameComposition.NewCampaign(2026) with { Phase = DayPhase.Evening };
        var torvald = baseState.Heroes[torvaldId.Value] with { Gear = GearSet.Empty with { Weapon = weaponId } };
        var expedition = new InFlightExpedition(
            Party: ImmutableList.Create(torvaldId), TargetFloor: 3, CheckpointFloor: 3, VenueId: "mine",
            Hp: ImmutableSortedDictionary<int, int>.Empty,
            Packs: ImmutableSortedDictionary<int, ImmutableList<ItemId>>.Empty,
            Gold: ImmutableSortedDictionary<int, int>.Empty, Dead: ImmutableSortedSet<int>.Empty,
            Floors: ImmutableList<FloorOutcome>.Empty, Loot: ImmutableList<OreLoot>.Empty, DeepestFloorCleared: 0);
        var state = baseState with
        {
            Heroes = baseState.Heroes.SetItem(torvaldId.Value, torvald),
            Items = ImmutableSortedDictionary<int, Item>.Empty.Add(weaponId.Value, weapon),
            InFlight = ImmutableList.Create(expedition),
        };

        var ui = MountMainUi(new GodotClient.SimAdapter(state));
        try
        {
            ui.Tutorial.Dismiss();
            ui.Town.FindBuilding("forge").RaisePick();
            var room = ui.Town.FindInteriorRoom("forge");
            var mentorStation = room.Stations.First(s => s.Key == MentorVoice.StationId);

            mentorStation.RaisePick();
            var firstSpoken = Find<Label>(ui.Mentor, "MentorBannerText").Text;

            AssertThat(firstSpoken.Contains(torvald.Name, StringComparison.Ordinal)
                       && firstSpoken.Contains(weapon.Name, StringComparison.Ordinal))
                .OverrideFailureMessage(
                    $"Pressing Bryn once the lesson was exhausted never spoke the logged observation. Got: \"{firstSpoken}\"")
                .IsTrue();

            ui.Mentor.Dismiss();
            mentorStation.RaisePick();
            var secondSpoken = Find<Label>(ui.Mentor, "MentorBannerText").Text;

            AssertThat(secondSpoken.Contains(weapon.Name, StringComparison.Ordinal))
                .OverrideFailureMessage($"Pressing Bryn a second time repeated the same told observation: \"{secondSpoken}\"")
                .IsFalse();
            AssertThat(secondSpoken)
                .OverrideFailureMessage($"With the observation told and nothing else live, she must fall back to her resting line. Got: \"{secondSpoken}\"")
                .IsEqual(MentorVoice.Speak(MentorVoice.RestingLine));
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
