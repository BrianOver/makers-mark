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
            var doneCount = rows.Count(r => r.Done);
            var skippedCount = rows.Count(r => r.Skipped);
            var neither = rows.Where(r => !r.Done && !r.Skipped).ToList();

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

            // The whole honesty of the third state: nothing SHOULD be left in limbo once the course
            // has finished — a registry row that is neither Done nor Skipped is one the anti-
            // stranding sweeps failed to carry the chain's own pointer past.
            //
            // FINDING (this lane's own, not fixed here — TutorialFlow.cs's registry is another
            // session's lane, and this unit's brief is explicit: report and book, never patch the
            // registry from a test PR): TutorialStep.OpenCounter is the ONE numbered step with no
            // anti-stranding sweep. Every OTHER swept step is named in some LATER row's own
            // AdvanceFrom — e.g. Vigil is carried forward by EveningClose's AdvanceFrom, which
            // ChecklistRow's own class doc calls out by name ("today only the Vigil row") — but
            // OpenCounter's row is the sole entry in its own AdvanceFrom (Registry: `AdvanceFrom:
            // [TutorialStep.OpenCounter]`), so a player who never opens the counter stays parked
            // there for the rest of the campaign. TutorialFlow.ChainBackstopDay still closes the
            // COURSE on day 8 (Completed flips true — proven above), but Step itself never moves
            // again, so Checklist() never marks Vigil/EveningClose/MeetHeroes/Commission Done OR
            // Skipped: they read "○ still upcoming" in the Lessons book forever, even after the
            // campaign has ended. That is a real gap in "every unreached row retires Skipped, never
            // falsely Done" — this run just never falls INTO it for the four verb rows it never
            // performs (asserted below); it falls OUT of the honest-third-state guarantee instead,
            // for whichever rows sit downstream of the one step nothing ever carries the player
            // past. The fix, when someone picks it up, is the same shape Vigil already has: add
            // OpenCounter to a later row's AdvanceFrom (AnsweredForReal already lists
            // TutorialStep.OpenCounter, so extending its sweep retires it honestly Skipped, not
            // falsely Done — no registry change this PR makes).
            var knownGapSteps = new[]
            {
                TutorialStep.Vigil, TutorialStep.EveningClose, TutorialStep.MeetHeroes, TutorialStep.Commission,
            };
            var knownGapSlots = knownGapSteps
                .Select(step => TutorialFlow.Registry.First(def => def.Step == step).DisplayIndex)
                .ToHashSet();
            var unexpectedlyStuck = neither.Where(r => !knownGapSlots.Contains(r.DisplayIndex)).ToList();

            AssertThat(unexpectedlyStuck)
                .OverrideFailureMessage(
                    $"{report}\n\nThese rows never settled Done or Skipped even after the course " +
                    "finished, and they are NOT the documented OpenCounter-anti-stranding gap above " +
                    "— a NEW row the chain's pointer can never reach: " +
                    string.Join(", ", unexpectedlyStuck.Select(r => $"[{r.DisplayIndex}] {r.Label}")))
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
}
#endif
