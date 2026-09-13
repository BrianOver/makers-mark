#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using GameSim;
using GameSim.Contracts;
using GameSim.Professions;
using GdUnit4;
using Godot;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-07 (§11.15): the three lessons split back out of BuyMaterial's own bolted-on TeachNote
/// paragraph — <see cref="TutorialFlow.SlotBudgetLessonId"/>, <see
/// cref="TutorialFlow.StationPressLessonId"/>, and <see cref="TutorialFlow.LeavingARoomLessonId"/>
/// (that last one's own fire test lives beside its sibling in <see
/// cref="TutorialCopyIsFollowableTests"/>, since it shares that suite's "names the real bound key"
/// idiom). This file covers the other two, their once-ever anti-nag pin, and the two structural
/// tripwires the unit exists to leave behind: no <see cref="TutorialStepDef.TeachNote"/> grows past
/// a sane budget again, and the Lessons book shows all three as separate cards, never one wall.
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class SplitLessonsTests
{
    [TestCase]
    public void StationLessons_DoNotFire_BeforeTheWorkshopIsEverEntered()
    {
        var ui = MountMainUi();
        try
        {
            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.SlotBudgetLessonId)).IsFalse();
            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.StationPressLessonId)).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void StationLessons_BothFire_TheFirstTimeTheWorkshopIsEntered()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tutorial.NotifyEnteredBuilding("forge");

            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.SlotBudgetLessonId))
                .OverrideFailureMessage("Entering the workshop must fire the slot-budget lesson.")
                .IsTrue();
            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.StationPressLessonId))
                .OverrideFailureMessage("Entering the workshop must fire the station-press lesson.")
                .IsTrue();

            // Spot-check content, not a golden string: each id carries its OWN distinct fact, never
            // the other's — the whole point of splitting one paragraph into three.
            AssertThat(ui.Tutorial.FirstTouch.Fired[TutorialFlow.SlotBudgetLessonId])
                .Contains("action slots");
            AssertThat(ui.Tutorial.FirstTouch.Fired[TutorialFlow.StationPressLessonId])
                .Contains("press E");
            AssertThat(ui.Tutorial.FirstTouch.Fired[TutorialFlow.SlotBudgetLessonId].Contains("press E", StringComparison.Ordinal))
                .OverrideFailureMessage("The slot-budget lesson leaked the station-press lesson's own content.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void StationLessons_DoNotFire_ForAnyOtherBuilding()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tutorial.NotifyEnteredBuilding("market");
            ui.Tutorial.NotifyEnteredBuilding("tavern");
            ui.Tutorial.NotifyEnteredBuilding("minegate");
            ui.Tutorial.NotifyEnteredBuilding("noticeboard");

            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.SlotBudgetLessonId)).IsFalse();
            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.StationPressLessonId)).IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>The anti-nag pin (this repo's own 1287x memorial-nag precedent) — a second, third,
    /// hundredth arrival at the workshop must never re-fire either lesson.</summary>
    [TestCase]
    public void StationLessons_NeverRefire_OnARepeatedEntry()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tutorial.NotifyEnteredBuilding("forge");
            var firstBudgetText = ui.Tutorial.FirstTouch.Fired[TutorialFlow.SlotBudgetLessonId];

            for (var i = 0; i < 25; i++)
            {
                ui.Tutorial.NotifyEnteredBuilding("forge");
            }

            AssertThat(ui.Tutorial.ConsumeFirstTouch(TutorialFlow.SlotBudgetLessonId, "a different line"))
                .OverrideFailureMessage("The slot-budget lesson re-fired after its first arrival.")
                .IsNull();
            AssertThat(ui.Tutorial.ConsumeFirstTouch(TutorialFlow.StationPressLessonId, "a different line"))
                .OverrideFailureMessage("The station-press lesson re-fired after its first arrival.")
                .IsNull();
            AssertThat(ui.Tutorial.FirstTouch.Fired[TutorialFlow.SlotBudgetLessonId])
                .OverrideFailureMessage("A repeat arrival changed the ORIGINAL fired text — the book's record must not shift after the fact.")
                .IsEqual(firstBudgetText);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Independent of <see cref="TutorialFlow.Active"/> — a returning smith who skipped the
    /// numbered chain entirely still needs to know how a station works, same "the long tail matters
    /// to every campaign" precedent <see cref="TutorialFlow.ConsumeLedgerTip"/> already set.</summary>
    [TestCase]
    public void StationLessons_StillFire_AfterTheChainIsDismissed()
    {
        var ui = MountMainUi();
        try
        {
            ui.Tutorial.Dismiss();
            AssertThat(ui.Tutorial.Active).IsFalse();

            ui.Tutorial.NotifyEnteredBuilding("forge");

            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.SlotBudgetLessonId)).IsTrue();
            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.StationPressLessonId)).IsTrue();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The tripwire this unit exists to leave behind (P2-SCREEN-07's own "Verification" clause): the
    /// next unit that finds itself wanting "the ONE row guaranteed to reach the screen" must find a
    /// real address for the fact instead of bolting a fourth sentence onto whichever row rendered
    /// first. 800 is picked from what the surviving notes actually need — OpenCounter's own five-verb
    /// paragraph (the longest today, deliberately unsplit: one cohesive live-haggle mechanic, not a
    /// bolted-together bundle of unrelated facts) measures 761; every other registry row and all
    /// three split lessons below sit under 400.
    /// </summary>
    private const int TeachNoteBudget = 800;

    /// <summary>
    /// P2-SCREEN-15 (§11.15): the three split lessons' own ids and source text, in one place — every
    /// property test below (the book, the character budget, and the on-screen speak/once-ever pair)
    /// iterates THIS array rather than hand-writing three cases apiece, so a fourth split lesson is
    /// covered by every one of them the day it is added here, never a fourth hand-written test.
    /// </summary>
    private static readonly (string Id, string Text)[] SplitLessons =
    [
        (TutorialFlow.SlotBudgetLessonId, TutorialFlow.SlotBudgetLessonText),
        (TutorialFlow.StationPressLessonId, TutorialFlow.StationPressLessonText),
        (TutorialFlow.LeavingARoomLessonId, TutorialFlow.LeavingARoomLessonText),
    ];

    [TestCase]
    public void NoRegistryRows_TeachNote_ExceedsTheCharacterBudget()
    {
        var offenders = TutorialFlow.Registry
            .Where(def => def.TeachNote.Length > TeachNoteBudget)
            .Select(def => $"{def.Step}: {def.TeachNote.Length} chars")
            .ToList();

        AssertThat(offenders.Count)
            .OverrideFailureMessage(
                $"TeachNote(s) over the {TeachNoteBudget}-character budget — split it into its own " +
                $"first-touch lesson instead of growing a registry row's paragraph:\n" +
                string.Join("\n", offenders))
            .IsEqual(0);
    }

    [TestCase]
    public void NoneOfTheThreeSplitLessons_ExceedsTheCharacterBudget()
    {
        var campaign = GameComposition.NewCampaign(ScriptedSession.Seed, ProfessionRegistry.BlacksmithId);
        var ui = MountMainUi(new SimAdapter(campaign));
        try
        {
            ui.Tutorial.NotifyEnteredBuilding("forge");
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.AdvancePhase();

            foreach (var (id, _) in SplitLessons)
            {
                var text = ui.Tutorial.FirstTouch.Fired[id];
                AssertThat(text.Length)
                    .OverrideFailureMessage($"\"{id}\" is {text.Length} chars — over the {TeachNoteBudget}-character budget.")
                    .IsLessEqual(TeachNoteBudget);
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void LessonsBook_ShowsAllThreeSplitLessons_AsSeparateCards()
    {
        var campaign = GameComposition.NewCampaign(ScriptedSession.Seed, ProfessionRegistry.BlacksmithId);
        var ui = MountMainUi(new SimAdapter(campaign));
        try
        {
            // Drive all three trigger moments — entering the workshop (budget + station-press) and
            // crafting the first item (leaving-a-room, off the starter kit, no Buy needed).
            ui.Tutorial.NotifyEnteredBuilding("forge");
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.AdvancePhase();

            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.SlotBudgetLessonId)).IsTrue();
            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.StationPressLessonId)).IsTrue();
            AssertThat(ui.Tutorial.FirstTouch.HasFired(TutorialFlow.LeavingARoomLessonId)).IsTrue();

            ui.OpenPanel("Lessons");
            var text = RenderedText(ui.Lessons);

            // Each lesson renders under its OWN human title (LessonsPanel.FirstTouchTitles), and each
            // title's text appears exactly once — three separate cards, not one paragraph wearing
            // three headings.
            foreach (var (id, _) in SplitLessons)
            {
                var title = LessonsPanel.FirstTouchTitles[id];
                AssertThat(text.Contains(title, StringComparison.Ordinal))
                    .OverrideFailureMessage($"The Lessons book never shows \"{title}\" (id \"{id}\").")
                    .IsTrue();
                AssertThat(text.Contains(ui.Tutorial.FirstTouch.Fired[id], StringComparison.Ordinal))
                    .OverrideFailureMessage($"The Lessons book never shows \"{id}\"'s own fired text.")
                    .IsTrue();
            }
        }
        finally
        {
            Unmount(ui);
        }
    }

    // ── P2-SCREEN-15: the three lessons now speak on screen, not only in the book ─────────────────

    /// <summary>
    /// The unit's own core claim: each of the three lessons reaches the screen at its own honest
    /// moment — entering the workshop (slot-budget, station-press) and the first-ever craft
    /// (leaving-a-room) — driven through the REAL production routes (a real building pick, a real
    /// craft action, exactly like <see cref="TutorialRegistryConformanceTests.EnteringTheAnchoredBuilding_TicksTheChecklistsArrivedSubBox"/>
    /// drives its own click), not a direct call to <see cref="TutorialFlow.NotifyEnteredBuilding"/>/
    /// <see cref="TutorialFlow.Advance"/> — those prove the BOOK half above; this proves the screen
    /// half is actually wired from <c>MainUi</c> into <see cref="MentorBanner"/>.
    ///
    /// <para>Also pins "each speaks once": the identical moments, repeated, speak nothing new — the
    /// 1287x memorial-nag precedent this whole first-touch tier exists to never repeat.</para>
    /// </summary>
    [TestCase]
    public void EachSplitLesson_SpeaksOnScreen_AtItsOwnMoment_ThenNeverAgain()
    {
        var campaign = GameComposition.NewCampaign(ScriptedSession.Seed, ProfessionRegistry.BlacksmithId);
        var ui = MountMainUi(new SimAdapter(campaign));
        try
        {
            ui.Town.FindBuilding("forge").RaisePick();
            ui.Adapter.Queue(new CraftAction(ScriptedSession.CraftRecipeId, ScriptedSession.CraftMaterial));
            ui.Adapter.AdvancePhase();

            var spoken = new List<string>();
            while (ui.Mentor.Visible)
            {
                spoken.Add(Find<Label>(ui.Mentor, "MentorBannerText").Text);
                ui.Mentor.Dismiss();
            }

            foreach (var (id, lessonText) in SplitLessons)
            {
                AssertThat(spoken.Contains(MentorVoice.Speak(lessonText)))
                    .OverrideFailureMessage($"\"{id}\" never reached the screen at its own moment — only the book.")
                    .IsTrue();
            }

            // Each speaks once: arriving at the identical moments again says nothing new.
            ui.Town.FindBuilding("forge").RaisePick();
            AssertThat(ui.Tutorial.Advance(ui.Adapter.CurrentState))
                .OverrideFailureMessage("A second Advance() call after the item is already crafted re-spoke the leaving-a-room lesson.")
                .IsNull();

            AssertThat(ui.Mentor.Visible)
                .OverrideFailureMessage("A second arrival at an already-taught moment spoke a lesson again.")
                .IsFalse();
            AssertThat(ui.Mentor.PendingLessonCount)
                .OverrideFailureMessage("A second arrival at an already-taught moment queued a lesson again.")
                .IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The forge pair's own return-value contract, directly: <see
    /// cref="TutorialFlow.NotifyEnteredBuilding"/> hands back BOTH spoken lines on the first-ever
    /// arrival (in <see cref="SplitLessons"/>' own declared order — slot-budget consumed before
    /// station-press, see that method's own body), and nothing at all on any later one. Before this
    /// unit the method returned <see langword="void"/> and every caller — <c>MainUi</c> included —
    /// discarded whatever <see cref="TutorialFlow.ConsumeFirstTouch"/> handed back.
    /// </summary>
    [TestCase]
    public void NotifyEnteredBuilding_ReturnsBothForgeLessons_OnFirstArrival_ThenNeverAgain()
    {
        var ui = MountMainUi();
        try
        {
            var firstArrival = ui.Tutorial.NotifyEnteredBuilding("forge");

            AssertThat(firstArrival.Count)
                .OverrideFailureMessage("Entering the forge for the first time must speak both its lessons on screen, not just record them in the book.")
                .IsEqual(2);
            AssertThat(firstArrival[0]).IsEqual(MentorVoice.Speak(TutorialFlow.SlotBudgetLessonText));
            AssertThat(firstArrival[1]).IsEqual(MentorVoice.Speak(TutorialFlow.StationPressLessonText));

            AssertThat(ui.Tutorial.NotifyEnteredBuilding("forge").Count)
                .OverrideFailureMessage("A second arrival at the forge spoke a lesson again — the anti-nag pin failed.")
                .IsEqual(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// <see cref="TutorialFlow.Advance"/>'s own equivalent return-value contract for the
    /// leaving-a-room lesson — the one it fires on, and nothing on any call after.
    /// </summary>
    [TestCase]
    public void Advance_ReturnsTheLeavingARoomLesson_OnFirstCraft_ThenNeverAgain()
    {
        var ui = MountMainUi();
        try
        {
            var crafted = ui.Adapter.CurrentState with
            {
                EventLog = ui.Adapter.CurrentState.EventLog.Add(new ItemCrafted(new ItemId(1), QualityGrade.Common)),
            };

            AssertThat(ui.Tutorial.Advance(crafted))
                .OverrideFailureMessage("The first-ever craft must speak the leaving-a-room lesson on screen, not just record it in the book.")
                .IsEqual(MentorVoice.Speak(TutorialFlow.LeavingARoomLessonText));

            AssertThat(ui.Tutorial.Advance(crafted with { Day = crafted.Day + 1 }))
                .OverrideFailureMessage("A later Advance() call re-spoke the leaving-a-room lesson — the anti-nag pin failed.")
                .IsNull();
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
