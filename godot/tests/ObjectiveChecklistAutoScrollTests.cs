#if GDUNIT_TESTS
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GdUnit4;
using Godot;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U11 (§11.14.14): <see cref="ObjectiveTracker.ChecklistMaxHeight"/> is a fixed 75px window (that
/// constant's own doc), and the checklist was built with no scroll call anywhere — past roughly the
/// third displayed step, the CURRENT row, its TeachNote, and (the acute case) its GatingNote all sat
/// below the fold with nothing nudging the scrollbar to reveal them. The trap warning a GatingNote
/// exists to show was unreadable at exactly the moment it applied.
///
/// <para>These drive <see cref="ObjectiveTracker.Refresh"/> directly with a hand-built checklist
/// (never the full tutorial chain — reaching a late step through real play would make the fixture's
/// row count and content an accident of whatever day the test happened to advance to) against a
/// REAL, fully mounted <see cref="ObjectiveTracker"/> (<see cref="MountMainUi"/>'s own
/// <c>ui.Objective</c>), so the theme cascade and container layout are the genuine production
/// ones.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class ObjectiveChecklistAutoScrollTests
{
    /// <summary><paramref name="total"/> short label-only rows, the one at
    /// <paramref name="currentIndex"/> marked Current (and, done, every earlier one) with the given
    /// optional TeachNote/GatingNote text.</summary>
    private static IReadOnlyList<ChecklistRow> BuildRows(
        int currentIndex, int total, string? teach = null, string? gating = null) =>
        Enumerable.Range(0, total)
            .Select(i => new ChecklistRow(
                DisplayIndex: i,
                Label: $"Step {i}",
                Done: i < currentIndex,
                Current: i == currentIndex,
                VisitedAnchor: false,
                GatingNote: i == currentIndex ? gating : null,
                TeachNote: i == currentIndex ? teach : null))
            .ToList();

    private static async Task SettleDeferredScroll(Node from)
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        for (var i = 0; i < 20; i++)
        {
            await from.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
    }

    [TestCase]
    public async Task StepChange_ScrollsTheChecklist_SoTheCurrentRowIsWithinTheVisibleHeight()
    {
        var ui = MountMainUi();
        try
        {
            var state = ui.Adapter.CurrentState;

            // Ten label-only rows, current at the LAST one — far enough down that
            // ChecklistMaxHeight's fixed 75px window cannot show it without scrolling, and a
            // freshly (re)built checklist starts scrolled to the top (row 0).
            ui.Objective.Refresh(state, tutorialOverride: "Do the thing", checklist: BuildRows(currentIndex: 9, total: 10));
            await SettleDeferredScroll(ui);

            var currentLine = Find<HBoxContainer>(ui.Objective, "TutorialChecklistRow_9");
            var scroll = Find<ScrollContainer>(ui.Objective, "ObjectiveTutorialChecklistScroll");
            var viewport = scroll.GetGlobalRect();

            AssertThat(viewport.Encloses(currentLine.GetGlobalRect()))
                .OverrideFailureMessage(
                    $"Current row 9/10 at {currentLine.GetGlobalRect()} is not fully within the " +
                    $"checklist's own visible window {viewport} after the step changed — it stayed " +
                    "below the fold.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }

    [TestCase]
    public async Task StepChange_KeepsTheCurrentRowsTeachAndGatingNotes_WithinTheVisibleHeight()
    {
        var ui = MountMainUi();
        try
        {
            var state = ui.Adapter.CurrentState;

            // Short copy on purpose: the claim under test is that the SCROLL follows the current
            // row's own notes, not how much text this fixed 75px window can hold — that is a
            // separate, pre-existing copy-budget question (TutorialCopyIsFollowableTests), not this
            // unit's own defect.
            var rows = BuildRows(currentIndex: 4, total: 6, teach: "A short teaching note.", gating: "A short warning note.");
            ui.Objective.Refresh(state, tutorialOverride: "Do the thing", checklist: rows);
            await SettleDeferredScroll(ui);

            var scroll = Find<ScrollContainer>(ui.Objective, "ObjectiveTutorialChecklistScroll");
            var viewport = scroll.GetGlobalRect();
            var teach = Find<Label>(ui.Objective, "TutorialChecklistTeachNote");
            var gating = Find<Label>(ui.Objective, "TutorialChecklistGatingNote");

            AssertThat(viewport.Encloses(teach.GetGlobalRect()))
                .OverrideFailureMessage(
                    $"TeachNote at {teach.GetGlobalRect()} is not fully within the checklist's visible " +
                    $"window {viewport}.")
                .IsTrue();
            AssertThat(viewport.Encloses(gating.GetGlobalRect()))
                .OverrideFailureMessage(
                    $"GatingNote at {gating.GetGlobalRect()} is not fully within the checklist's visible " +
                    $"window {viewport} — this is the trap warning going unread at the exact moment it " +
                    "applies, the acute case this unit exists to fix.")
                .IsTrue();
        }
        finally { Unmount(ui); }
    }
}
#endif
