#if GDUNIT_TESTS
using System;
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
/// P2-ONBOARD-08 (§11.15): Factorio's skippability standard, made executable — "if the player
/// tabbed through all the speech bubbles without looking, and did not read the story text, they
/// should still be able to finish the level." That is not a skip BUTTON; it is a claim about the
/// guided course's own completion predicates — none of them may read comprehension. The rework's
/// argument for why the claim holds was prose ("no completion predicate reads comprehension,
/// the day-8 backstop stands, unread dormant beats retire Skipped") until this suite: it plays the
/// real client through <see cref="HumanPlayer"/> only, presses through every lesson unread, and
/// asserts the course still finishes.
///
/// <para><b>Honest-input discipline (see <see cref="HumanPlayer"/>'s own class doc).</b> Every
/// press below is a real pushed <see cref="InputEvent"/> at a real control's own hit-tested
/// centre, proven by that control's own <c>Pressed</c> signal actually firing — never a direct
/// method call (<c>Conductor.Hurry()</c>, <c>Mentor.Dismiss()</c>) and never a signal emitted by
/// hand. Screen state is read only to decide WHICH of three fixed controls to press next (is a
/// lesson banner up, is the vigil slate up, otherwise press the one continue verb) — never to
/// decide WHAT the lesson says or WHAT the vigil's own stakes are. This lane never buys material,
/// never crafts, never shelves, never posts a bounty, never opens the counter/ledger/wall/hero
/// panels, and never sends or recalls a vigil supply — every one of those is taught content this
/// lane deliberately refuses to act on.</para>
///
/// <para><b>The one live decision with no default.</b> <see cref="RaidConductor.Beat.VigilStop"/>
/// holds indefinitely (law: no timers on decisions) and <c>MainUi</c>'s own continue-verb button
/// (<c>"AdvancePhase"</c>) only reopens the slate while it is up rather than skipping it — so this
/// lane answers it with a single fixed, non-comprehending choice, <c>"CampDeeper"</c> ("Send them
/// deeper"), every single time. That is a real gameplay control, not story prose, and there is no
/// other way past it — the class doc on <c>Panels.CampPanel</c> names it as the ONLY way the vigil
/// stop ever ends.</para>
/// </summary>
[TestSuite]
[RequireGodotRuntime]
public class TabThroughLaneTests
{
    /// <summary>Generous hang guard, not the expected exit — the loop's real exit is
    /// <see cref="TutorialFlow.Completed"/>. Sized well above the worst case: eight days at
    /// <see cref="UiTestSupport.MaxPhasesPerDay"/> (8) ticks apiece, plus room for several vigil
    /// stops and a crowded lesson backlog (capped at four) along the way.</summary>
    private const int MaxPresses = 300;

    /// <summary>Per-tick hang guard on draining the lesson backlog alone — the backlog itself is
    /// capped at four, so ten dismiss presses in a row that never clears it is a stuck banner, not
    /// slow progress.</summary>
    private const int MaxDismissesPerTick = 10;

    /// <summary>
    /// The registry rows whose own <see cref="TutorialStepDef.IsDone"/> reads an EventLog/ActionLog
    /// fact that only a real player verb (buy, craft, shelve, post a bounty, answer the counter,
    /// answer a commission) can produce. This lane never performs any of them — not through the
    /// UI, not through the adapter — so every one of these rows reading <see cref="ChecklistRow.
    /// Done"/> at the end of the run would be a completion predicate that fired without the verb it
    /// claims to record: the concrete, checkable shape of "falsely Done." <see
    /// cref="TutorialStep.WatchDeparture"/> and <see cref="TutorialStep.EveningClose"/> are
    /// deliberately absent — both are autonomous/day-boundary facts (a party departs on its own
    /// schedule; the day reaches Evening) that this lane's OWN presses do not gate, so either may
    /// legitimately read Done without this lane ever having "read" anything.</summary>
    private static readonly TutorialStep[] NeverPerformedVerbs =
    {
        TutorialStep.BuyMaterial, TutorialStep.Craft, TutorialStep.Shelve,
        TutorialStep.PostBounty, TutorialStep.OpenCounter, TutorialStep.Commission,
    };

    [TestCase]
    public async Task TabThrough_ReadsNothing_StillFinishes()
    {
        var ui = MountMainUi();
        try
        {
            var player = new HumanPlayer(ui);
            var presses = 0;
            var lessonsDismissedUnread = 0;
            var vigilStopsAnswered = 0;

            // Settle the HUD's own header row before the first press — a freshly mounted
            // container reports its pre-layout rect for a few frames, and clicking the
            // continue verb's centre before it settles lands on whatever the header hasn't
            // moved out of that spot yet (measured: "AdvancePhase" at (32.5, 20), stacked under
            // the DayChip). Real players never click a screen mid-layout; neither should this.
            await player.WaitForLayout(ui);

            while (!ui.Tutorial.Completed)
            {
                if (++presses > MaxPresses)
                {
                    throw new InvalidOperationException(
                        $"The tab-through lane never reached TutorialFlow.Completed after {MaxPresses} " +
                        $"presses (day {ui.Adapter.CurrentState.Day}, phase {ui.Adapter.CurrentState.Phase}). " +
                        "A course that cannot finish for a player who reads nothing is exactly the defect " +
                        $"this lane exists to catch.{player.TraceTail()}");
                }

                // Drain every lesson banner unread — "tabbed through all the speech bubbles."
                // Never inspected for its text, only for whether one is still up.
                var dismisses = 0;
                while (ui.Mentor.Visible)
                {
                    if (++dismisses > MaxDismissesPerTick)
                    {
                        throw new InvalidOperationException(
                            $"MentorBanner never drained after {MaxDismissesPerTick} \"Got it\" presses — " +
                            $"either the backlog is growing faster than it drains, or Dismiss is not " +
                            $"advancing the queue.{player.TraceTail()}");
                    }

                    await player.ClickControl(Find<Button>(ui, "MentorBannerDismiss"), "Got it (unread)");
                    lessonsDismissedUnread++;
                }

                // The one live decision the game holds open with no default: answer it the same
                // fixed way every time, never by reading what it says.
                if (ui.Camp.Visible)
                {
                    await player.ClickControl(Find<Button>(ui.Camp, "CampDeeper"), "Send them deeper (unread)");
                    vigilStopsAnswered++;
                    continue;
                }

                // The one generic continue verb — "Skip" at Morning/Evening, "Hurry the day along"
                // through the raid span, "Return to the vigil" if this same press just reopened the
                // slate above (MainUi's own single-control dispatch on Conductor.Current).
                await player.ClickControl(Find<Button>(ui, "AdvancePhase"));
            }

            var finishDay = ui.Adapter.CurrentState.Day;
            var viaBackstop = finishDay >= TutorialFlow.ChainBackstopDay;

            AssertThat(ui.Tutorial.Completed)
                .OverrideFailureMessage("The course never finished for a player who read nothing.")
                .IsTrue();

            // "Handle both exits": event-shaped graduation (the Memory row settling) always fires
            // STRICTLY BEFORE the backstop (TutorialFlow.Advance's own ordering), so Completed must
            // never first read true on a day past it.
            AssertThat(finishDay)
                .OverrideFailureMessage(
                    $"Completed on day {finishDay}, past the day-8 backstop " +
                    $"({TutorialFlow.ChainBackstopDay}) — the backstop is supposed to be the LATEST " +
                    "possible exit, never a later one.")
                .IsLessEqual(TutorialFlow.ChainBackstopDay);

            // ── The retirement census ──────────────────────────────────────────────────────
            var state = ui.Adapter.CurrentState;
            var rows = ui.Tutorial.Checklist(state);
            var rowsByDisplayIndex = rows.ToDictionary(r => r.DisplayIndex);
            var doneCount = rows.Count(r => r.Done);
            var skippedCount = rows.Count(r => r.Skipped);

            var loss = ui.Tutorial.LossActRow(state);
            var proof = ui.Tutorial.ProofBeatRow(state);
            var memory = ui.Tutorial.MemoryActRow(state);

            var report =
                $"Exit: {(viaBackstop ? "day-8 backstop" : "event-shaped graduation (Memory row settled)")} " +
                $"on day {finishDay}, after {presses} continue-verb presses, " +
                $"{lessonsDismissedUnread} lesson(s) dismissed unread, and {vigilStopsAnswered} vigil " +
                $"stop(s) answered unread. Registry rows: {rows.Count} total, {doneCount} Done, " +
                $"{skippedCount} Skipped. Dormant acts — Loss: {DescribeDormant(loss)}, " +
                $"Proof: {DescribeDormant(proof)}, Memory: {DescribeDormant(memory)}.";

            // U-T9-13 (this PR): deny-by-default over the ENUM, not a hand-picked list of
            // symptoms. This repo has shipped the hand-listed-fixture bug more than once — a guard
            // iterating a literal array silently stops covering the family it was meant to guard
            // the moment a new member joins — and this exact suite used to have one: a hard-coded
            // "known gap" list of the four rows TutorialStep.OpenCounter's own missing
            // anti-stranding sweep left stranded (Vigil/EveningClose/MeetHeroes/Commission),
            // written by hand after the fact rather than derived from the property it was
            // standing in for. OpenCounter's sweep is now fixed (TutorialFlow.Registry's
            // EveningClose row) — this walks every TutorialStep by reflection instead of
            // reinstating a list, so a FUTURE step with no sweep fails by name automatically,
            // the same way this one did.
            //
            // Fixing OpenCounter's own sweep does NOT clear the whole old "known gap" list — it
            // clears three of the four (Vigil/EveningClose settle via EveningClose's own row;
            // MeetHeroes settles too, but only because it shares LookIn's own accepted "UI-only
            // step reads Done once frozen-and-past" exception — AnsweredForReal's default branch,
            // pinned by TheStepsWhoseCompletionIsAUiHook_AreNeverStampedSkipped). Commission —
            // ONE slot past MeetHeroes — does not: it is a NEW finding, only reachable now that
            // OpenCounter's own fix lets the chain get this far, and it is a real, structurally
            // identical gap, not fixed here. MeetHeroes' own IsDone is UI-only (`_ => false`,
            // advanced only by NotifyPanelOpened) and, unlike Vigil/OpenCounter, has no LATER row
            // with an already-unconditional (day-based) IsDone to ride — Commission is the
            // terminal row, and its own IsDone is deliberately a real player verb (Accept/Decline
            // — TutorialRegistryConformanceTests.EveryStepsCompletionFact_IsReachableByPlayerActionAlone
            // pins that on purpose). Riding ChainBackstopDay itself, or widening Checklist()'s own
            // inactive-isPast rule to cover every row unconditionally, would fix it — but the
            // latter regresses Checklist()'s own documented early-Dismiss case ("every slot the
            // chain never reached stays a plain upcoming row... honest, since the course really
            // did end before getting there"), and the former has no shipped precedent to mirror.
            // Named here, not patched blind — a follow-up unit's fix, same shape as this one's.
            var pinnedExceptions = new Dictionary<TutorialStep, string>
            {
                [TutorialStep.Commission] =
                    "One slot past TutorialStep.MeetHeroes, whose own IsDone is UI-only and has no " +
                    "later row's unconditional fact to ride (Commission is terminal, and its own " +
                    "IsDone is deliberately a real Accept/Decline verb) — a player who never opens " +
                    "Hero Cards/Tavern leaves Step frozen at MeetHeroes, one slot short of Commission " +
                    "ever becoming \"past\". A real, structurally identical gap to the one this PR " +
                    "fixes for OpenCounter, only reachable now that the fix lets the chain get this " +
                    "far — booked as a follow-up, not patched here.",
            };

            var unsettled = UnsettledSteps(rowsByDisplayIndex, pinnedExceptions)
                .Select(step =>
                {
                    var def = TutorialFlow.Registry.First(d => d.Step == step);
                    return $"{step} (slot {def.DisplayIndex}, \"{rowsByDisplayIndex[def.DisplayIndex].Label}\")";
                })
                .ToList();

            AssertThat(unsettled)
                .OverrideFailureMessage(
                    $"{report}\n\nThese steps never settled Done or Skipped even after the course " +
                    "finished, and are not named as a pinned exception (with a reason) above — the " +
                    "chain's pointer never reached, or was never swept past, them: " +
                    string.Join(", ", unsettled))
                .IsEmpty();

            // Zero falsely Done, made concrete: this lane provably never bought material, never
            // crafted, never shelved, never posted a bounty, never answered the counter, and never
            // answered a commission — so none of THOSE rows' own durable facts can be true, and any
            // one of them reading Done anyway is a completion predicate lying about what happened.
            var neverPerformedSlots = NeverPerformedVerbs
                .Select(step => TutorialFlow.Registry.First(def => def.Step == step).DisplayIndex)
                .ToHashSet();
            var falselyDone = rows.Where(r => neverPerformedSlots.Contains(r.DisplayIndex) && r.Done).ToList();

            AssertThat(falselyDone)
                .OverrideFailureMessage(
                    $"{report}\n\nThese rows read Done despite this lane never performing the verb " +
                    "their own IsDone predicate claims happened: " +
                    string.Join(", ", falselyDone.Select(r => $"[{r.DisplayIndex}] {r.Label}")))
                .IsEmpty();
        }
        finally
        {
            Unmount(ui);
        }
    }

    private static string DescribeDormant(ChecklistRow? row) =>
        row is null ? "never armed" : row.Value.Done ? "Done" : row.Value.Skipped ? "Skipped" : "current/unsettled";

    /// <summary>U-T9-13: the deny-by-default core both <see cref="TabThrough_ReadsNothing_StillFinishes"/>
    /// and <see cref="APlantedUnsweptStep_FailsByName_NotSilently"/> share — walks every
    /// <see cref="TutorialStep"/> by reflection (<see cref="Enum.GetValues{TEnum}()"/>), never a
    /// hand-typed subset, and names whichever ones neither <see cref="ChecklistRow.Done"/> nor
    /// <see cref="ChecklistRow.Skipped"/> read true for, skipping only steps named in
    /// <paramref name="pinnedExceptions"/>. Shared, rather than re-typed per test, so the SAME
    /// algorithm both proves the real course settles and proves the algorithm itself would catch a
    /// step that does not.</summary>
    private static List<TutorialStep> UnsettledSteps(
        IReadOnlyDictionary<int, ChecklistRow> rowsByDisplayIndex,
        IReadOnlyDictionary<TutorialStep, string> pinnedExceptions) =>
        Enum.GetValues<TutorialStep>()
            .Where(step => !pinnedExceptions.ContainsKey(step))
            .Where(step => rowsByDisplayIndex[TutorialFlow.Registry.First(d => d.Step == step).DisplayIndex]
                is { Done: false, Skipped: false })
            .ToList();

    /// <summary>
    /// U-T9-13: proves <see cref="UnsettledSteps"/> is reflective rather than a hand-list — the
    /// exact shape of bug this PR replaces (a guard iterating a literal array of the four rows
    /// TutorialStep.OpenCounter's own missing sweep happened to strand, which would have kept
    /// reading green forever if a FIFTH row started going unswept, since nothing would have ever
    /// added it to the array by hand).
    ///
    /// <para>A real new <see cref="TutorialStep"/> enum member cannot be "planted" from a test —
    /// the enum is compiled, fixed, production code. What CAN be proven from a test is the thing
    /// that actually matters: that the algorithm names ANY step whose row fails to settle, driven
    /// purely by walking <see cref="Enum.GetValues{TEnum}()"/> against real checklist data, with no
    /// per-step name baked into the loop itself. This fakes one step's own row as neither Done nor
    /// Skipped (standing in for "a future step whose own sweep was forgotten") and shows it is
    /// caught, by name, exactly once — then shows naming it in the exception list is the only thing
    /// that silences it.</para>
    /// </summary>
    [TestCase]
    public void APlantedUnsweptStep_FailsByName_NotSilently()
    {
        // Every real DisplayIndex reads settled (Done) except one, planted deliberately —
        // TutorialStep.Shelve's own slot, chosen only because it is not shared with any other step
        // (unlike slot 1, BuyMaterial/Craft) and is not TutorialStep.Commission (this PR's own
        // pinned exception, which must stay excluded for an unrelated, already-documented reason).
        var plantedSlot = TutorialFlow.Registry.First(d => d.Step == TutorialStep.Shelve).DisplayIndex;
        var rowsByDisplayIndex = Enumerable.Range(1, TutorialFlow.TotalSteps)
            .ToDictionary(
                slot => slot,
                slot => new ChecklistRow(
                    DisplayIndex: slot, Label: $"slot {slot}", Done: slot != plantedSlot,
                    Current: false, VisitedAnchor: false, GatingNote: null, Skipped: false));

        var caught = UnsettledSteps(rowsByDisplayIndex, new Dictionary<TutorialStep, string>());

        AssertThat(caught)
            .OverrideFailureMessage(
                $"Planted exactly one unswept slot ({plantedSlot}, TutorialStep.Shelve) but the " +
                $"reflective guard named: {string.Join(", ", caught)}. It must name the planted " +
                "step, and only the planted step, by walking the enum — never a hand-typed subset " +
                "that happens to include or exclude it.")
            .ContainsExactly(TutorialStep.Shelve);

        var silenced = UnsettledSteps(
            rowsByDisplayIndex, new Dictionary<TutorialStep, string> { [TutorialStep.Shelve] = "test-planted" });

        AssertThat(silenced)
            .OverrideFailureMessage("Naming the planted step as a pinned exception should silence it.")
            .IsEmpty();
    }
}
#endif
