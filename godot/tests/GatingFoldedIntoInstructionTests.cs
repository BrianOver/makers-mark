#if GDUNIT_TESTS
using System;
using System.Collections.Immutable;
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using GodotClient.Panels;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// P2-SCREEN-08 (§11.15): the card's gating note stops being a second text block — the checklist box
/// P2-SCREEN-06 already emptied — and folds into the ONE instruction sentence <see
/// cref="TutorialFlow.StepText"/> renders. "Why you cannot do the thing I just told you to do" is the
/// same question as "what do I do now"; splitting them across a button row is what produced the
/// severed-sentence defect P2-SCREEN-06 fixed for the OTHER half of this card.
///
/// <para><see cref="TutorialFlow.GatingNote"/> itself is unchanged (<see
/// cref="TutorialNeverAsksTheImpossibleTests"/> already pins its own logic byte-for-byte) — this file
/// pins the NEW address: every case <c>GatingNote</c> can return non-null for while the step is still
/// nominally actionable (Vigil with no staged stop, an empty commission board, an empty shelf under
/// an open counter) now reaches the screen folded onto <see cref="TutorialFlow.CopyFor"/>'s single
/// line, and the fully-blocked cases (<see cref="TutorialFlow.WaitText"/>'s own territory) never
/// double up with it.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class GatingFoldedIntoInstructionTests
{
    [TestCase]
    public void OpenCounterWithAnEmptyShelf_FoldsTheReason_OntoTheSameInstructionLine()
    {
        var ui = MountMainUi();
        try
        {
            var state = ui.Adapter.CurrentState with { Day = 1, Phase = DayPhase.Morning };
            AssertThat(state.Player.Shelf.Count)
                .OverrideFailureMessage("Fixture guard: a fresh campaign's shelf should already be empty.")
                .IsEqual(0);

            var copy = ui.Tutorial.CopyFor(TutorialStep.OpenCounter, state);

            AssertThat(copy)
                .OverrideFailureMessage("OpenCounter's own instruction (press Open Counter) must survive the fold, not be replaced by it.")
                .Contains("Open Counter");
            AssertThat(copy)
                .OverrideFailureMessage("An empty shelf's own reason never reached the card's one instruction line.")
                .Contains("Nothing on the shelf yet");
        }
        finally
        {
            Unmount(ui);
        }
    }

    [TestCase]
    public void CommissionWithAnEmptyBoard_FoldsTheReason_OntoTheSameInstructionLine()
    {
        var ui = MountMainUi();
        try
        {
            var state = ui.Adapter.CurrentState with
            {
                Day = 3, Phase = DayPhase.Morning, Commissions = ImmutableList<Commission>.Empty,
            };

            var copy = ui.Tutorial.CopyFor(TutorialStep.Commission, state);

            AssertThat(copy)
                .OverrideFailureMessage("Commission's own tray-tooltip instruction must survive the fold.")
                .Contains(GodotClient.MainUi.CommissionsTrayTooltip);
            AssertThat(copy)
                .OverrideFailureMessage("An empty commission board's own reason never reached the card's one instruction line.")
                .Contains("No one is asking today");
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>Regression: Vigil used to hand-append <c>VigilGatingNote</c> directly inline in its own
    /// switch arm. P2-SCREEN-08 moved that onto the generic fold instead — this pins the RENDERED text
    /// is byte-identical either way, so the address changed and the words did not.</summary>
    [TestCase]
    public void Vigil_StillFoldsItsOwnMusterTruth_OntoTheSameInstructionLine_ByteIdentical()
    {
        var ui = MountMainUi();
        try
        {
            var state = ui.Adapter.CurrentState with { Day = 1 };
            var copy = ui.Tutorial.CopyFor(TutorialStep.Vigil, state);

            AssertThat(copy)
                .OverrideFailureMessage("Vigil's own press instructions must survive the fold.")
                .Contains("press **Send**, or press **Recall**.");

            var def = TutorialFlow.Registry.First(d => d.Step == TutorialStep.Vigil);
            var expectedGate = ui.Tutorial.GatingNoteForTests(def, state);
            AssertThat(expectedGate)
                .OverrideFailureMessage("Fixture guard: Vigil should always carry a muster-truth gating note.")
                .IsNotNull();
            AssertThat(copy)
                .OverrideFailureMessage("Vigil's own muster-truth line no longer reaches the card at all.")
                .Contains(expectedGate!);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>No standalone surface renders <see cref="TutorialFlow.GatingNote"/> as a second block
    /// any more — <see cref="LessonsPanel"/> (the checklist's own new home, P2-SCREEN-06) never
    /// reads <c>ChecklistRow.GatingNote</c> at all, and <see cref="ObjectiveTracker"/> carries exactly
    /// one prose <c>Label</c> (its own class doc, unchanged by this unit). This is the negative half of
    /// the fold: the gating text is now reachable from exactly ONE address.</summary>
    [TestCase]
    public void LessonsBook_NeverRendersAGatingNote_AsItsOwnSeparateBlock()
    {
        var ui = MountMainUi();
        try
        {
            // Land on a state whose OpenCounter row carries a live gating note (empty shelf), so if
            // the book rendered it anywhere this fixture would catch it.
            var state = ui.Adapter.CurrentState with { Day = 1, Phase = DayPhase.Morning };
            var def = TutorialFlow.Registry.First(d => d.Step == TutorialStep.OpenCounter);
            var gate = ui.Tutorial.GatingNoteForTests(def, state);
            AssertThat(gate)
                .OverrideFailureMessage("Fixture guard: OpenCounter should read a gating note against an empty shelf.")
                .IsNotNull();

            ui.OpenPanel("Lessons");
            var text = RenderedText(ui.Lessons);

            AssertThat(text.Contains(gate!, StringComparison.Ordinal))
                .OverrideFailureMessage(
                    $"The Lessons book rendered a GatingNote (\"{gate}\") as its own block — the address " +
                    "this unit moved that text OFF of.")
                .IsFalse();
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The tripwire the deleted generic fallback becomes: every (step, day, phase, slots) combination
    /// the sim can actually place a fresh campaign into is driven through <see
    /// cref="TutorialFlow.CopyFor"/>. Before this unit an unauthored case would have silently printed
    /// "Not available right now — nothing lost by waiting."; now it throws — this sweep is the proof
    /// that throw is never reached, i.e. every reachable unavailable case really does have its own
    /// authored line (mirrors <see
    /// cref="TutorialNeverAsksTheImpossibleTests.EveryGatedStep_AvailabilityMatchesTheSimsOwnLegalActions_AcrossEveryPhaseAndBudget"/>'s
    /// own sweep shape, widened here across Day too, since the deleted fallback's own reachable cases
    /// depended on the day-gate branch as much as the phase/slot ones).
    /// </summary>
    [TestCase]
    public void EveryGatedStepAcrossEveryDayPhaseAndBudget_NeverReachesTheDeletedGenericFallback()
    {
        var ui = MountMainUi();
        try
        {
            var baseState = ui.Adapter.CurrentState;
            var checkedUnavailable = 0;

            foreach (var def in TutorialFlow.Registry)
            {
                foreach (var day in new[] { 1, 2, 3, 4 })
                {
                    foreach (var phase in Enum.GetValues<DayPhase>())
                    {
                        foreach (var slots in new[] { 0, ActionBudget.SlotsPerDay })
                        {
                            var state = baseState with { Day = day, Phase = phase, ActionSlotsRemaining = slots };

                            if (TutorialFlow.StepActionAvailableForTests(state, def))
                            {
                                continue;
                            }

                            checkedUnavailable++;
                            // Deliberately NOT wrapped in try/catch: WaitText's replacement for the
                            // deleted generic fallback is a throw (InvalidOperationException) — if any
                            // combination below reaches it, this test case fails LOUDLY with that
                            // exception's own message naming the exact step/day/phase/slots, which is
                            // the whole point (a silent generic string would never have failed at all).
                            var copy = ui.Tutorial.CopyFor(def.Step, state);

                            AssertThat(copy.Contains("Not available right now", StringComparison.Ordinal))
                                .OverrideFailureMessage(
                                    $"{def.Step} at Day {day}, {phase}, slots={slots} still printed the " +
                                    $"deleted generic fallback text: \"{copy}\"")
                                .IsFalse();
                        }
                    }
                }
            }

            AssertThat(checkedUnavailable)
                .OverrideFailureMessage("Fixture guard: no (step, day, phase, slots) combination ever read unavailable — this sweep would be vacuous.")
                .IsGreater(0);
        }
        finally
        {
            Unmount(ui);
        }
    }

    /// <summary>
    /// The honest wait-copy corpus relocates, it does not die — its own address (phase-gate lines
    /// bolted onto a step's ONE instruction) was already correct before this unit; this pins each of
    /// them survives byte-identical, since this unit never touches <see cref="TutorialFlow.WaitText"/>'s
    /// real (non-fallback) cases, only deletes the one line that could never legitimately be reached.
    /// </summary>
    [TestCase]
    public void TheHonestWaitCopyCorpus_SurvivesVerbatim()
    {
        var ui = MountMainUi();
        try
        {
            var morningOnlyVendor = ui.Adapter.CurrentState with { Day = 1, Phase = DayPhase.Expedition };
            AssertThat(ui.Tutorial.CopyFor(TutorialStep.BuyMaterial, morningOnlyVendor))
                .Contains("material vendor only trades in the Morning");

            var counterClosed = ui.Adapter.CurrentState with { Day = 1, Phase = DayPhase.Expedition };
            AssertThat(ui.Tutorial.CopyFor(TutorialStep.OpenCounter, counterClosed))
                .Contains("The counter only opens in the Morning");

            var bountyOffWindow = ui.Adapter.CurrentState with { Day = 3, Phase = DayPhase.Expedition };
            AssertThat(ui.Tutorial.CopyFor(TutorialStep.PostBounty, bountyOffWindow))
                .Contains("Bounties board only takes postings in the Morning or Evening");

            var noSlots = ui.Adapter.CurrentState with { ActionSlotsRemaining = 0 };
            AssertThat(ui.Tutorial.CopyFor(TutorialStep.BuyMaterial, noSlots))
                .Contains("No action slots left today");

            var beforeDay3 = ui.Adapter.CurrentState with { Day = 1 };
            AssertThat(ui.Tutorial.CopyFor(TutorialStep.MeetHeroes, beforeDay3))
                .Contains("Day 3 lesson");
        }
        finally
        {
            Unmount(ui);
        }
    }
}
#endif
