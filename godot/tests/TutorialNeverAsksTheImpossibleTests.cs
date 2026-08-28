#if GDUNIT_TESTS
using System;
using System.Collections.Generic;
using System.Linq;
using GameSim.Advisor;
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

    /// <summary>
    /// U13 (§11.14.14): the whole point of deriving <c>StepActionAvailable</c> from
    /// <see cref="ActionLegality.IsLegal"/> instead of a hand mirror — proves, independently of
    /// <c>TutorialStepDef.CanonicalAction</c>'s own implementation, that "this step reads
    /// available" and "<see cref="ActionLegality.LegalActions"/> for this phase contains an action
    /// from this step's own family" are the SAME fact, across every phase and with the day's action
    /// budget both full and spent. <see cref="ActionFamilyByStep"/> names each family independently
    /// of <c>TutorialFlow.cs</c>, so this cannot pass merely because the test re-quotes the
    /// production code's own implementation.
    /// </summary>
    [TestCase]
    public void EveryGatedStep_AvailabilityMatchesTheSimsOwnLegalActions_AcrossEveryPhaseAndBudget()
    {
        var ui = MountMainUi();
        try
        {
            // Day 3 clears every row's own MinDay gate at once (the highest is 3, Commission/
            // MeetHeroes) so this test isolates the phase/slot/domain dimension StepActionAvailable
            // now derives from IsLegal, not the day dimension (unchanged by this unit, covered
            // elsewhere).
            var baseState = ui.Adapter.CurrentState with { Day = 3 };

            foreach (var def in TutorialFlow.Registry)
            {
                if (!ActionFamilyByStep.TryGetValue(def.Step, out var isFamilyMember))
                {
                    // LookIn/Shelve/WatchDeparture/Vigil/EveningClose/MeetHeroes: no action family at
                    // all. Commission: deliberately un-gated (its own Registry comment). Craft: gated,
                    // but on a NARROWER question than "matches LegalActions" — its own dedicated test
                    // below (CraftStep_StaysAvailableWithNoMaterial_ButGoesQuietOnlyWhenSlotsRunOut)
                    // covers it precisely.
                    continue;
                }

                foreach (var phase in Enum.GetValues<DayPhase>())
                {
                    foreach (var slots in new[] { 0, ActionBudget.SlotsPerDay })
                    {
                        var state = baseState with { Phase = phase, ActionSlotsRemaining = slots };
                        var expected = ActionLegality.LegalActions(state, phase).Any(isFamilyMember);

                        AssertThat(TutorialFlow.StepActionAvailableForTests(state, def))
                            .OverrideFailureMessage(
                                $"{def.Step} at phase {phase}, slots={slots}: StepActionAvailable read " +
                                $"{!expected}, but ActionLegality.LegalActions itself said {expected} for " +
                                "this step's own action family. The two must never disagree — that IS the " +
                                "one-legality-source contract this unit exists to hold.")
                            .IsEqual(expected);
                    }
                }
            }
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// THE drift test (§11.14.14's own ask): proves a FUTURE slot-consuming action type needs no
    /// edit to this file, or to <c>TutorialFlow.cs</c>, for its own tutorial step to correctly go
    /// quiet on a slot-exhausted day. It asks <see cref="ActionBudget.ConsumesSlot"/> ITSELF which
    /// of the registry's canonical actions spend a slot — never a hardcoded step name — so if a
    /// later unit adds an eleventh slot-consuming action type and wires it as some row's
    /// <c>CanonicalAction</c>, this test starts covering it with zero changes here. This is the
    /// exact shape of the shipped defect this unit closes (Craft's own slot check once missing from
    /// a hand-copied "the two that spend a slot" list, U-T9-11) — the fix is that no such list
    /// exists in this adapter to fall out of date at all.
    /// </summary>
    [TestCase]
    public void SlotConsumingCanonicalActions_GoUnavailableAtZeroSlots_DiscoveredFromActionBudgetItself()
    {
        var ui = MountMainUi();
        try
        {
            // One purchase, so BuyMaterial/PostBounty (whose OWN canonical action IS run through
            // full IsLegal, gold included) still have a legal candidate at a full budget — not
            // needed for Craft, whose row never checks material at all (its own Registry comment).
            ui.Adapter.Queue(new BuyMaterialAction(ScriptedSession.CraftMaterial, ScriptedSession.CopperNeeded));
            var fullBudget = ui.Adapter.CurrentState;
            AssertThat(fullBudget.ActionSlotsRemaining)
                .OverrideFailureMessage("Fixture guard: the day should still have slots left after one purchase.")
                .IsGreater(0);
            var exhausted = fullBudget with { ActionSlotsRemaining = 0 };

            var slotConsumingStepsChecked = 0;
            foreach (var def in TutorialFlow.Registry)
            {
                var action = def.CanonicalAction?.Invoke(fullBudget);
                if (action is null || !ActionBudget.ConsumesSlot(action))
                {
                    continue; // this row has no verb, or its verb does not compete for the day's budget.
                }

                // The fixture guard asks StepActionAvailable itself (the PUBLIC contract), not raw
                // ActionLegality.IsLegal — Craft's own row deliberately never calls IsLegal at all
                // (its own Registry comment), so a guard written against IsLegal directly would be
                // the wrong question for that row and right for every other one.
                AssertThat(TutorialFlow.StepActionAvailableForTests(fullBudget, def))
                    .OverrideFailureMessage(
                        $"Fixture guard: {def.Step} must read available with a full budget, or the " +
                        "zero-slots comparison below proves nothing.")
                    .IsTrue();

                AssertThat(TutorialFlow.StepActionAvailableForTests(exhausted, def))
                    .OverrideFailureMessage(
                        $"{def.Step}'s canonical action is one ActionBudget.ConsumesSlot lists as " +
                        "spending a slot, yet the step still read available with zero slots left today " +
                        $"— the exact Craft-omission defect this unit removes the possibility of " +
                        $"(nothing here names {def.Step} by hand; ActionBudget.ConsumesSlot(action) is " +
                        "what found it).")
                    .IsFalse();

                slotConsumingStepsChecked++;
            }

            AssertThat(slotConsumingStepsChecked)
                .OverrideFailureMessage("Fixture guard: no registry row's canonical action consumes a slot — this test would be vacuous.")
                .IsGreater(0);
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// U13: Craft's own deliberate exception, pinned directly. Its card must stay ACTIONABLE with
    /// zero material — buying is BuyMaterial's own job, or the starter kit's, never Craft's own gate
    /// — but go quiet the instant the day's slots run out, matching <see
    /// cref="ActionBudget.ConsumesSlot"/> for <see cref="CraftAction"/>. This unit's first draft ran
    /// Craft through full <see cref="ActionLegality.IsLegal"/> and broke exactly this —
    /// <c>TutorialCopyIsFollowableTests</c>' own Day-3/no-purchase fixture caught it, which is why
    /// <see cref="ActionFamilyByStep"/> deliberately excludes Craft rather than asserting the wrong
    /// contract for it.
    /// </summary>
    [TestCase]
    public void CraftStep_StaysAvailableWithNoMaterial_ButGoesQuietOnlyWhenSlotsRunOut()
    {
        var ui = MountMainUi();
        try
        {
            var craftDef = TutorialFlow.Registry.First(d => d.Step == TutorialStep.Craft);
            var noMaterialFullSlots = ui.Adapter.CurrentState with
            {
                Day = 3, Phase = DayPhase.Morning, ActionSlotsRemaining = ActionBudget.SlotsPerDay,
            };

            AssertThat(TutorialFlow.StepActionAvailableForTests(noMaterialFullSlots, craftDef))
                .OverrideFailureMessage(
                    "Craft read unavailable with zero material even though slots remain — material " +
                    "sufficiency belongs to BuyMaterial's own job, never to Craft's own gate.")
                .IsTrue();

            var noMaterialNoSlots = noMaterialFullSlots with { ActionSlotsRemaining = 0 };
            AssertThat(TutorialFlow.StepActionAvailableForTests(noMaterialNoSlots, craftDef))
                .OverrideFailureMessage(
                    "Craft still read available with zero slots left — the exact Craft-omission " +
                    "defect this unit exists to prevent from ever coming back.")
                .IsFalse();
        }
        finally { Unmount(ui); }
    }

    /// <summary>
    /// U13: the load-bearing slot-exhaustion string (register #157's own hard-won wording) must
    /// survive byte for byte now that its CONDITION is asked of the sim instead of restated — this
    /// unit's own constraint was "their wording, not their condition, must not change". Also pins
    /// Commission's own DELIBERATE non-gating (this unit's reverted first draft — see this row's
    /// own Registry comment): its main card keeps quoting the tray tooltip even outside Morning,
    /// while GatingNote (pre-existing, unchanged) alone carries the Morning-only nuance.
    /// </summary>
    [TestCase]
    public void ExistingGatingWording_SurvivesDerivingAvailabilityFromIsLegal()
    {
        var ui = MountMainUi();
        try
        {
            var noSlots = ui.Adapter.CurrentState with { ActionSlotsRemaining = 0 };
            AssertThat(ui.Tutorial.TopSlotText(noSlots))
                .OverrideFailureMessage("BuyMaterial's slot-exhaustion wording changed — only the condition that reaches it should have.")
                .Contains("No action slots left today");

            var commissionDef = TutorialFlow.Registry.First(d => d.Step == TutorialStep.Commission);
            var eveningDay3 = ui.Adapter.CurrentState with { Day = 3, Phase = DayPhase.Evening };

            AssertThat(ui.Tutorial.GatingNoteForTests(commissionDef, eveningDay3) ?? string.Empty)
                .Contains("Commissions are answered in the Morning");
            AssertThat(ui.Tutorial.CopyFor(TutorialStep.Commission, eveningDay3))
                .OverrideFailureMessage(
                    "Commission's own card swapped to a wait variant outside Morning — its main " +
                    "instruction is meant to stay unconditional; GatingNote alone carries the nuance.")
                .Contains(GodotClient.MainUi.CommissionsTrayTooltip);
        }
        finally { Unmount(ui); }
    }

    /// <summary>Independent of <c>TutorialFlow.cs</c>'s own <c>CanonicalAction</c> implementation —
    /// names each step's action FAMILY by its real <see cref="PlayerAction"/> types, the same
    /// domain each row's own registry comment (<c>TutorialFlow.Registry</c>) already describes in
    /// prose. Only the three rows judged by FULL legality appear here — Craft is gated on a
    /// narrower question (its own dedicated test above) and Commission is deliberately un-gated (its
    /// own Registry comment) — every other row is skipped by the test above.</summary>
    private static readonly IReadOnlyDictionary<TutorialStep, Func<PlayerAction, bool>> ActionFamilyByStep =
        new Dictionary<TutorialStep, Func<PlayerAction, bool>>
        {
            [TutorialStep.BuyMaterial] = a => a is BuyMaterialAction,
            [TutorialStep.PostBounty] = a => a is PostBountyAction,
            [TutorialStep.OpenCounter] = a =>
                a is OpenCounterAction or PresentItemAction or SuggestItemAction or HaggleResponseAction or CloseCounterAction,
        };

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
