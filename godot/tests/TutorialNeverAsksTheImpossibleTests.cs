#if GDUNIT_TESTS
using System.Linq;
using GameSim.Contracts;
using GdUnit4;
using GodotClient.Ui;
using static GdUnit4.Assertions;
using static GodotClient.Tests.UiTestSupport;

namespace GodotClient.Tests;

/// <summary>
/// U-T9-11 (§11.14.13): a guided course may never keep asking for something the player cannot do.
/// Every wall in this chain already had an honest wait line — except the ones below, and one of them
/// is the LAST step, the one whose own copy says "the loop is yours after this".
///
/// <list type="bullet">
/// <item><b>Craft spent a slot the card never mentioned.</b> Both slot branches read
/// <c>BuyMaterial or PostBounty</c>, and the comment beside them called those "the two that spend a
/// slot" — false: <c>ActionBudget.ConsumesSlot</c> lists <c>CraftAction</c> and
/// <c>CraftingHandlers.ApplyCraft</c> decrements the budget. A 100g purse and five slots make
/// spending the day on material reachable without doing anything strange.</item>
/// <item><b>The Commission step had no gating case at all</b> — not for the Morning-only gate on
/// Accept/Decline, and not for an empty board, which is an ordinary day rather than an edge case
/// (the board is gap-driven and only posts when a mustering hero has a hole in their kit).</item>
/// <item><b>Swept rows claimed credit.</b> The anti-stranding sweeps carry the chain past an
/// unanswered step on purpose, so nobody is ever stuck — but the checklist then rendered those rows
/// as ✓ Done, the exact false checkmark <c>ChecklistRow.Skipped</c>'s own doc forbids. Only Vigil
/// could read Skipped.</item>
/// </list>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TutorialNeverAsksTheImpossibleTests
{
    /// <summary>Crafting is legal in every phase — the forge never closes — so the slot is the whole
    /// of its availability, and a slot-exhausted day has to say so.</summary>
    [TestCase]
    public void WithNoSlotsLeft_TheCraftStep_SaysSoInsteadOfAskingAnyway()
    {
        var ui = MountMainUi();
        try
        {
            var state = ui.Adapter.CurrentState with { ActionSlotsRemaining = 0 };

            var current = ui.Tutorial.Checklist(state).Where(r => r.Current).ToList();
            AssertThat(current.Count)
                .OverrideFailureMessage("Fixture guard: the chain should have exactly one current row.")
                .IsEqual(1);
            var note = current[0].GatingNote;

            AssertThat(ui.Tutorial.TopSlotText(state))
                .OverrideFailureMessage(
                    "A slot-exhausted day must not read as an instruction to craft. CraftAction spends "
                    + "a slot, so the press would bounce off a gate the card never named.")
                .Contains("slots");
            AssertThat(note ?? string.Empty).Contains("slots");
        }
        finally { Unmount(ui); }
    }

    /// <summary>The last step of the course, outside Morning. Accept and Decline are Morning-gated,
    /// and the card said nothing about it all day.</summary>
    [TestCase]
    public void TheCommissionStep_OutsideMorning_NamesTheWindow()
    {
        AssertGatingNoteFor(
            TutorialStep.Commission,
            state => state with { Day = 3, Phase = DayPhase.Evening, Commissions = SomeCommission(state) },
            "Morning",
            "The course's last step is Morning-gated and said nothing about it — a nag pointing at "
            + "greyed buttons is the player's final impression of the teacher.");
    }

    /// <summary>And with nobody asking, which is an ordinary day rather than an edge case.</summary>
    [TestCase]
    public void TheCommissionStep_WithAnEmptyBoard_SaysNobodyIsAsking()
    {
        AssertGatingNoteFor(
            TutorialStep.Commission,
            state => state with { Day = 3, Phase = DayPhase.Morning, Commissions = ImmutableListOfNone() },
            "asking",
            "With an empty board the card demanded Accept or Decline against nothing, for days, and "
            + "then the chain's backstop closed it wordlessly.");
    }

    /// <summary>The false checkmark. A row the sweep carried past, whose own durable predicate is
    /// still false, must read Skipped — never Done.</summary>
    [TestCase]
    public void ASweptStep_ReadsSkipped_NotDone()
    {
        var ui = MountMainUi();
        try
        {
            // Ring the bell without shelving anything: WatchDeparture's unconditional sweep carries
            // the chain past Shelve, which the player never answered.
            PressEnabled(ui, "AdvancePhase");

            var state = ui.Adapter.CurrentState;
            var rows = ui.Tutorial.Checklist(state).Where(r => r.Label.Contains("shelf")).ToList();

            AssertThat(rows.Count)
                .OverrideFailureMessage("Fixture guard: the shelve row should exist in the checklist.")
                .IsEqual(1);
            AssertThat(state.Player.Shelf.Count)
                .OverrideFailureMessage("Fixture guard: nothing should be on the shelf for this to prove anything.")
                .IsEqual(0);
            AssertThat(rows[0].Done)
                .OverrideFailureMessage(
                    "The sweep carried the chain past Shelve and the row claimed ✓ Done. That is a "
                    + "checkmark for something the player never did.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>The exclusions are deliberate, and this pins them so they stay named rather than
    /// drifting into silence: LookIn and MeetHeroes complete through a UI notification, not a fact in
    /// the sim, so asking their predicate would stamp a false DASH in place of a false tick.</summary>
    [TestCase]
    public void TheStepsWhoseCompletionIsAUiHook_AreNeverStampedSkipped()
    {
        var ui = MountMainUi();
        try
        {
            var byStep = TutorialFlow.Registry.ToDictionary(d => d.Step);

            foreach (var step in new[] { TutorialStep.LookIn, TutorialStep.MeetHeroes })
            {
                AssertThat(byStep[step].IsDone(ui.Adapter.CurrentState))
                    .OverrideFailureMessage(
                        $"{step}'s predicate now answers true on a fresh campaign, so it may be a real "
                        + "fact after all — if so, add it to AnsweredForReal and delete this row.")
                    .IsFalse();
            }
        }
        finally { Unmount(ui); }
    }

    private static void AssertGatingNoteFor(
        TutorialStep step, System.Func<GameState, GameState> shape, string mustContain, string why)
    {
        var ui = MountMainUi();
        try
        {
            // Day 3: GatingNote answers the MinDay gate FIRST ("Comes on Day 3"), so a day-1 fixture
            // would never reach the case under test. The Commission step's own MinDay is 3.
            var state = shape(ui.Adapter.CurrentState);
            var def = TutorialFlow.Registry.First(d => d.Step == step);

            AssertThat(ui.Tutorial.GatingNoteForTests(def, state) ?? string.Empty)
                .OverrideFailureMessage(why)
                .Contains(mustContain);
        }
        finally { Unmount(ui); }
    }

    private static System.Collections.Immutable.ImmutableList<Commission> ImmutableListOfNone() =>
        System.Collections.Immutable.ImmutableList<Commission>.Empty;

    private static System.Collections.Immutable.ImmutableList<Commission> SomeCommission(GameState state) =>
        state.Commissions.IsEmpty
            ? System.Collections.Immutable.ImmutableList.Create(
                new Commission(new HeroId(1), ItemSlot.Weapon, QualityGrade.Common, DeadlineDay: 9, PremiumGold: 20))
            : state.Commissions;
}
#endif
